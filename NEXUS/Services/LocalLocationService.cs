using System;
using System.Diagnostics;
using System.IO;

namespace NEXUS.Services;

public sealed record LocalLocation(string Folder, string? SelectedFile);
public static class LocalLocationService
{
    public static LocalLocation Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("В событии не сохранён путь. Новые события содержат путь, если Windows разрешает его прочитать.");
        path = path.Trim();
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\?", StringComparison.Ordinal))
            throw new IOException("Поддерживаются только обычные абсолютные локальные пути.");
        path = Path.GetFullPath(path);
        if (File.Exists(path)) return new(Path.GetDirectoryName(path)!, path);
        if (Directory.Exists(path)) return new(path, null);
        var parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (Directory.Exists(parent)) return new(parent, null);
            parent = Path.GetDirectoryName(parent);
        }
        throw new IOException("Файл и родительские папки недоступны.");
    }

    public static ProcessStartInfo ExplorerStart(LocalLocation location)
    {
        // Explorer has its own /select parsing; quote the file separately from the switch.
        if (location.Folder.Contains('"') || location.SelectedFile?.Contains('"') == true) throw new IOException("Недопустимый путь.");
        return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = false,
            Arguments = location.SelectedFile != null ? "/select,\"" + location.SelectedFile + "\"" : "\"" + location.Folder + "\""
        };
    }
}
