using System;
using System.Collections.Generic;
using System.Linq;

namespace NEXUS.Security;

public sealed record HardwareReading(string Device, string Metric, double Value, string Source = "");
public sealed record HealthAssessment(int? HardwareScore, bool HardwareComplete, int SecurityScore, bool SecurityComplete, int? OverallScore, IReadOnlyList<SecurityFinding> Findings);

public static class HealthAnalyzer
{
    public static HealthAssessment Analyze(DiagnosticResult result, IReadOnlyList<HardwareReading> readings)
    {
        var findings = new List<SecurityFinding>();
        foreach (var r in readings)
        {
            bool temperature = r.Metric == "CPU Temperature" || r.Metric == "GPU Temperature" || r.Metric == "Storage Temperature";
            double warning = r.Metric == "Storage Temperature" ? 60 : r.Metric == "GPU Temperature" ? 85 : 80;
            double critical = r.Metric == "Storage Temperature" ? 70 : r.Metric == "GPU Temperature" ? 95 : 90;
            if (temperature && r.Value >= warning)
                findings.Add(new(r.Value >= critical ? "Critical" : "Warning", "Hardware", "Повышенная температура: " + r.Device, $"{r.Metric}: {r.Value:F1} °C", "Проверьте нагрузку, вентиляцию и охлаждение. Если температура сохраняется, очистите систему охлаждения и проверьте вентиляторы."));
            if (r.Metric == "Storage Health" && r.Value <= 70)
                findings.Add(new(r.Value <= 40 ? "Critical" : "Warning", "Hardware", "Снижен ресурс накопителя: " + r.Device, $"Остаточный ресурс: {r.Value:F0}%", "Создайте резервную копию. Проверьте SMART утилитой производителя и запланируйте замену при ухудшении."));
            if (r.Metric == "Disk Used" && r.Value >= 90)
                findings.Add(new("Warning", "Hardware", "Мало свободного места: " + r.Device, $"Занято {r.Value:F0}%", "Освободите место через параметры хранилища Windows; не удаляйте неизвестные системные файлы."));
            if (r.Metric == "RAM Load" && r.Value >= 90)
                findings.Add(new("Warning", "Hardware", "Высокая загрузка памяти", $"{r.Value:F0}%", "Проверьте потребление памяти приложениями. Разовый пик не означает неисправность."));
        }
        int? hardware = readings.Count == 0 ? null : Math.Max(0, 100 - findings.Sum(f => f.Severity == "Critical" ? 30 : 10));
        int security = 100 - Math.Min(100, result.Findings.Where(f => f.Severity != "Info").DistinctBy(f => (f.Title, f.Evidence)).Sum(f => f.Severity == "Critical" ? 30 : 10));
        bool hardwareComplete = new[] { "CPU Temperature", "GPU Temperature", "Storage Temperature", "Storage Health", "Disk Used", "RAM Load" }.All(metric => readings.Any(r => r.Metric == metric));
        int? overall = hardware.HasValue && hardwareComplete && result.IsComplete ? Math.Min(hardware.Value, security) : null;
        return new(hardware, hardwareComplete, security, result.IsComplete, overall, findings);
    }
}
