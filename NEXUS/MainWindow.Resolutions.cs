using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NEXUS.Security;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NEXUS;

public sealed partial class MainWindow
{
    private bool _findingOperationRunning;
    private async void ResolveFindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not FindingResolution action) return;
        try
        {
            switch (action.Id)
            {
                case "thermal": NavigateTo("temperatures"); return;
                case "temp": NavigateTo("maintenance"); if (AnalyzeTempButton.IsEnabled) AnalyzeTempButton_Click(sender, e); return;
                case "ram": NavigateTo("maintenance"); if (RefreshMemoryProcessesButton.IsEnabled) RefreshMemoryProcessesButton_Click(sender, e); return;
                case "dism":
                    AiButton_Click(sender,e);
                    AiTaskStatusText.Text = "Для хранилища компонентов запустите DISM, затем SFC; после завершения повторите диагностику. Обе команды требуют подтверждения.";
                    return;
                case "defender": await Windows.System.Launcher.LaunchUriAsync(new Uri("windowsdefender:")); return;
                case "uac": StartWindowsTool("UserAccountControlSettings.exe"); return;
                case "events": StartWindowsTool("eventvwr.msc"); return;
                case "persistence": StartWindowsTool("taskschd.msc"); return;
                case "backup":
                    await ShowResolutionSteps("Резервная копия и накопитель", "1. Скопируйте важные файлы на другой физический диск или в облачное хранилище.\n2. Проверьте, что копии открываются.\n3. Проверьте SMART утилитой производителя.\n4. При ухудшении ресурса планируйте замену накопителя. Программная очистка не восстанавливает износ."); return;
                case "manual": await ShowResolutionSteps("Рекомендуемые действия", action.Details); return;
                case "firewall": case "defender-update": case "defender-scan":
                    if (_findingOperationRunning) { ResolutionStatusText.Text = "Действие уже выполняется."; return; }
                    _findingOperationRunning = true;
                    try
                    {
                        string command = action.Id == "firewall" ? "Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True -ErrorAction Stop" : action.Id == "defender-update" ? "Update-MpSignature -ErrorAction Stop" : "Start-MpScan -ScanType QuickScan -ErrorAction Stop";
                        var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme=ElementTheme.Dark, Title=action.Label, Content=action.Details + "\nWindows запросит права администратора. После выполнения повторите диагностику.", PrimaryButtonText="Выполнить", CloseButtonText="Отмена", DefaultButton=ContentDialogButton.Close };
                        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                        ResolutionStatusText.Text = "Выполняется: " + action.Label;
                        int code = await RunFixedSecurityAction(command);
                        if (_securityWindowClosed) return;
                        ResolutionStatusText.Text = $"{action.Label}: команда завершилась с кодом {code}. Повторите диагностику для проверки результата.";
                        _logService.Write("Security", "ResolutionAction", "FindingActions", action.Label, ResolutionStatusText.Text, severity:code==0?"Info":"Warning");
                    }
                    finally { _findingOperationRunning = false; }
                    return;
            }
        }
        catch (Exception ex) { if (!_securityWindowClosed) ResolutionStatusText.Text = "Действие не завершено: " + ex.Message; }
    }
    private Task<ContentDialogResult> ShowResolutionSteps(string title, string steps)
    {
        return ShowStepsAsync(title,steps);
    }
    private async Task<ContentDialogResult> ShowStepsAsync(string title, string steps)
    {
        var dialog = new ContentDialog { XamlRoot=ShellRoot.XamlRoot, RequestedTheme=ElementTheme.Dark, Title=title, Content=new TextBlock { Text=steps, TextWrapping=TextWrapping.Wrap, IsTextSelectionEnabled=true }, CloseButtonText="Закрыть" };
        return await dialog.ShowAsync();
    }
    private static void StartWindowsTool(string name)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = name.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) ? new ProcessStartInfo(Path.Combine(system,"mmc.exe")) : new ProcessStartInfo(Path.Combine(system,name));
        start.UseShellExecute=true;
        if (name.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)) start.Arguments = "\"" + Path.Combine(system,name) + "\"";
        Process.Start(start)?.Dispose();
    }
    private static async Task<int> RunFixedSecurityAction(string fixedCommand)
    {
        string script = "$ErrorActionPreference='Stop'; try { " + fixedCommand + "; exit 0 } catch { Write-Error $_; exit 1 }";
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var start = new ProcessStartInfo(exe) { UseShellExecute=true, Verb="runas", Arguments="-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)) };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить действие.");
        await process.WaitForExitAsync(); return process.ExitCode;
    }
}
