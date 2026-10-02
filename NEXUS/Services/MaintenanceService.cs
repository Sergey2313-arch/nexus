using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NEXUS.Services;

public sealed record TempCandidate(string Path, long Bytes, DateTime LastWriteUtc);
public sealed record TempPreview(List<TempCandidate> Files, int Skipped, bool Limited)
{
    public long Bytes { get { long total = 0; foreach (var file in Files) total += file.Bytes; return total; } }
}
public sealed record CleanupResult(int Deleted, int Skipped, long Bytes);

public static class MaintenanceService
{
    public static TempPreview PreviewTemp(string root)
    {
        var files = new List<TempCandidate>();
        var queue = new Queue<string>();
        queue.Enqueue(System.IO.Path.GetFullPath(root));
        var watch = Stopwatch.StartNew();
        int visited = 0, skipped = 0;
        bool limited = false;
        while (queue.Count > 0)
        {
            if (visited >= 10000 || watch.Elapsed.TotalSeconds >= 20) { limited = true; break; }
            var directory = queue.Dequeue();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    visited++;
                    if (visited >= 10000 || watch.Elapsed.TotalSeconds >= 20) { limited = true; break; }
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) { queue.Enqueue(path); continue; }
                    var file = new FileInfo(path);
                    if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)) files.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc));
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return new(files, skipped, limited);
    }

    public static bool IsSafeCandidate(string root, TempCandidate candidate)
    {
        root = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        string path = System.IO.Path.GetFullPath(candidate.Path);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        var file = new FileInfo(path);
        if (!file.Exists || file.LastWriteTimeUtc != candidate.LastWriteUtc || file.Length != candidate.Bytes || file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) return false;
        for (var current = file.Directory; current != null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(current.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar), root.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return (file.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        return false;
    }

    public static CleanupResult CleanTemp(string root, TempPreview preview)
    {
        int deleted = 0, skipped = 0; long bytes = 0;
        foreach (var candidate in preview.Files)
        {
            try
            {
                if (!IsSafeCandidate(root, candidate)) { skipped++; continue; }
                // Open exclusively so files in use are skipped. Rename/reparse races remain OS-level limitations.
                using (var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (stream.Length != candidate.Bytes) { skipped++; continue; }
                }
                if (!IsSafeCandidate(root, candidate)) { skipped++; continue; }
                File.Delete(candidate.Path); deleted++; bytes += candidate.Bytes;
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return new(deleted, skipped, bytes);
    }

    public static (long Before, long After) TrimOwnMemory()
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        using var process = Process.GetCurrentProcess(); process.Refresh(); long before = process.WorkingSet64;
        if (!EmptyWorkingSet(process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        process.Refresh(); return (before, process.WorkingSet64);
    }
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
