using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NEXUS.Security;

public sealed record DiagnosticSnapshot(DateTime Timestamp, List<SecurityFinding> Findings, List<ScanStage> Stages, List<HardwareReading> Readings, List<SecurityFinding>? PendingFindings = null);
public sealed record FindingChange(string Status, SecurityFinding Finding, string Details);
public static class DiagnosticComparison
{
    public static string Key(SecurityFinding finding)
    {
        string target = "";
        if (finding.Category is "Files" or "Processes" or "Network" or "Startup" or "Tasks" or "Services" or "Correlation")
        {
            int signature = finding.Evidence.IndexOf(" | Authenticode:", StringComparison.Ordinal);
            target = signature >= 0 ? finding.Evidence[..signature].ToUpperInvariant() : finding.Evidence;
        }
        if (finding.Category == "Defender" && finding.Evidence.StartsWith("ThreatID=", StringComparison.Ordinal)) target = finding.Evidence.Split(';')[0];
        return finding.Category + "\n" + finding.Title + "\n" + target;
    }
    public static List<FindingChange> Compare(DiagnosticSnapshot before, DiagnosticSnapshot after)
    {
        var old = before.Findings.Concat(before.PendingFindings ?? new()).Where(f => f.Severity != "Info").DistinctBy(Key).ToDictionary(Key);
        var current = after.Findings.Where(f => f.Severity != "Info").DistinctBy(Key).ToDictionary(Key);
        var rows = new List<FindingChange>();
        foreach (var entry in current)
            rows.Add(new(old.ContainsKey(entry.Key) ? "Сохраняется" : "Новое обнаружение", entry.Value,
                old.TryGetValue(entry.Key, out var prior) ? $"Уровень: {prior.Severity} → {entry.Value.Severity}. Признак найден повторно; выполнение команды не означает устранение причины." : "Признак появился в текущей проверке. Он мог отсутствовать в прошлой выборке или быть недоступен."));
        foreach (var entry in old.Where(e => !current.ContainsKey(e.Key)))
        {
            bool covered = Covered(entry.Value, after);
            rows.Add(new(covered ? "Больше не обнаружено" : "Не удалось проверить", entry.Value,
                covered ? "Признак не найден в текущих доступных данных. Это не доказательство исправления: файловая выборка ограничена, процессы могут завершиться, а исторические события — выйти из диапазона дат." : "Соответствующий этап или датчик недоступен. Предыдущее обнаружение сохранено для следующего сравнения и не считается исправленным."));
        }
        return rows.OrderBy(r => r.Status == "Не удалось проверить" ? 0 : r.Status == "Новое обнаружение" ? 1 : r.Status == "Сохраняется" ? 2 : 3).ToList();
    }
    private static bool Covered(SecurityFinding finding, DiagnosticSnapshot snapshot)
    {
        if (finding.Category == "Hardware")
        {
            string metric = finding.Title.Contains("ресурс", StringComparison.OrdinalIgnoreCase) ? "Storage Health" : finding.Title.Contains("места", StringComparison.OrdinalIgnoreCase) ? "Disk Used" : finding.Title.Contains("памяти", StringComparison.OrdinalIgnoreCase) ? "RAM Load" : finding.Evidence.Split(':')[0];
            return snapshot.Readings.Any(r => r.Metric == metric && double.IsFinite(r.Value) && (metric == "RAM Load" || finding.Title.EndsWith(r.Device, StringComparison.Ordinal)));
        }
        if (finding.Category == "Correlation") return new[] { "Процессы", "Автозагрузка", "Службы Windows", "Подозрительные файлы", "Сетевые соединения", "Корреляция признаков" }.All(name => snapshot.Stages.Any(s => s.Name == name && s.Completed));
        string? stage = finding.Category switch
        {
            "Processes" => "Процессы", "Startup" or "Tasks" => "Автозагрузка", "Services" => "Службы Windows", "Files" => "Подозрительные файлы", "Network" => "Сетевые соединения", "Defender" => "Windows Defender", "Configuration" or "Integrity" or "Events" => "Настройки, целостность и события", "Correlation" => "Корреляция признаков", _ => null
        };
        return stage != null && snapshot.Stages.Any(s => s.Name == stage && s.Completed);
    }
    public static DiagnosticSnapshot PreserveUnverified(DiagnosticSnapshot current, IEnumerable<FindingChange> changes) => current with
    {
        PendingFindings = changes.Where(c => c.Status == "Не удалось проверить").Select(c => c.Finding).DistinctBy(Key).ToList()
    };
}
public static class DiagnosticSnapshotStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "last-diagnostic.json");
    public static DiagnosticSnapshot? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4194304) throw new InvalidDataException("Снимок диагностики слишком большой.");
        var snapshot = JsonSerializer.Deserialize<DiagnosticSnapshot>(File.ReadAllText(path));
        if (snapshot == null || snapshot.Findings == null || snapshot.Stages == null || snapshot.Readings == null) throw new InvalidDataException("Снимок диагностики повреждён.");
        return snapshot;
    }
    public static void Save(DiagnosticSnapshot snapshot, string? path = null)
    {
        path ??= DefaultPath;
        string json = JsonSerializer.Serialize(snapshot);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4194304) throw new InvalidDataException("Снимок диагностики слишком большой.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
