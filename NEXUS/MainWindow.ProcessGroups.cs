using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace NEXUS;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<ProcessGroup> _processGroups = new();
    private readonly Dictionary<string, BitmapImage> _processIconCache = new(StringComparer.OrdinalIgnoreCase);
    private RunningProcessItem? _selectedManagedProcess;
    public sealed class ProcessGroup : INotifyPropertyChanged
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Cpu { get; init; } = "";
        public string Memory { get; init; } = "";
        public string State { get; init; } = "";
        public bool IsExpanded { get; set; }
        public List<RunningProcessItem> Children { get; init; } = new();
        private ImageSource? _icon;
        public ImageSource? Icon { get => _icon; set { _icon=value; PropertyChanged?.Invoke(this,new(nameof(Icon))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private void RefreshProcessGroups()
    {
        var expanded = _processGroups.Where(g=>g.IsExpanded).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int sort = ProcessSortComboBox?.SelectedIndex ?? 0;
        var groups = _runningProcesses.GroupBy(p=>string.IsNullOrEmpty(p.Path) ? p.Name : p.Path, StringComparer.OrdinalIgnoreCase).Select(group=>
        {
            var children = group.ToList();
            return new ProcessGroup { Key=group.Key, Name=$"{children[0].Name} ({children.Count})", Kind=children.Any(p=>!string.IsNullOrWhiteSpace(p.Details)) ? "Приложения" : "Фоновые процессы", Cpu=children.All(p=>p.Cpu=="—")?"—":$"{children.Sum(p=>p.CpuPercent):F1}%"+(children.Any(p=>p.Cpu=="—")?" + ?":""), Memory=FormatBytes(children.Sum(p=>p.MemoryBytes)), State=children.Any(p=>p.State=="Нет ответа")?"Нет ответа":"Работает", Children=children, IsExpanded=expanded.Contains(group.Key) };
        }).OrderBy(g=>g.Kind=="Приложения"?0:1).ThenBy(g=>sort==2?g.Name:"").ThenByDescending(g=>sort==1?g.Children.Sum(p=>p.CpuPercent):sort==0?g.Children.Sum(p=>p.MemoryBytes):0).ThenBy(g=>sort==3?g.Children.Min(p=>p.ProcessId):0).ToList();
        _processGroups.Clear();
        foreach (var group in groups) { _processGroups.Add(group); _=LoadProcessIconAsync(group); }
        if (_selectedManagedProcess != null)
            _selectedManagedProcess = _runningProcesses.FirstOrDefault(p=>p.ProcessId==_selectedManagedProcess.ProcessId && p.StartedUtc==_selectedManagedProcess.StartedUtc);
        RunningProcessesCountText.Text = $"Приложения: {groups.Count(g=>g.Kind=="Приложения")} групп • Фоновые процессы: {groups.Count(g=>g.Kind!="Приложения")} групп • PID: {_runningProcesses.Count}";
    }
    private async Task LoadProcessIconAsync(ProcessGroup group)
    {
        string path=group.Children[0].Path;
        if (string.IsNullOrEmpty(path) || path.StartsWith("\\\\",StringComparison.Ordinal)) return;
        if (_processIconCache.TryGetValue(path,out var cached)) { group.Icon=cached; return; }
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail=await file.GetThumbnailAsync(ThumbnailMode.SingleItem,32,ThumbnailOptions.UseCurrentScale);
            if (thumbnail==null) return;
            var image=new BitmapImage(); await image.SetSourceAsync(thumbnail);
            if (_securityWindowClosed) return;
            group.Icon=image;
            if (_processIconCache.Count<128) _processIconCache[path]=image;
        }
        catch { /* Keep the generic application icon when the shell icon is inaccessible. */ }
    }
    private void SelectManagedProcessButton_Click(object sender,RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RunningProcessItem item) { _selectedManagedProcess=item; ProcessActionStatusText.Text=$"Выбран: {item.Name} • PID {item.ProcessId}"; }
    }
    private void EndManagedProcessButton_Click(object sender,RoutedEventArgs e)
    {
        SelectManagedProcessButton_Click(sender,e); EndSelectedProcessButton_Click(sender,e);
    }
    private void OpenManagedProcessLocationButton_Click(object sender,RoutedEventArgs e)
    {
        if (((Button)sender).Tag is RunningProcessItem item) OpenLocation(item.Path);
    }
}
