using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NEXUS.Security;

public static class DiagnosticReportWriter
{
    public static string Save(DiagnosticResult result, HealthAssessment health, IReadOnlyList<HardwareReading> readings)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "Reports");
        Directory.CreateDirectory(directory);
        string stem = Path.Combine(directory, "Diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(new { Computer = Environment.MachineName, Result = result, Health = health, Hardware = readings }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        static string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><title>NEXUS — диагностика</title><style>body{font:16px system-ui;max-width:1000px;margin:40px auto;padding:20px;background:#11151c;color:#e6edf3}article{background:#1d2530;padding:20px;margin:16px 0;border-radius:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere}.Warning{border-left:4px solid #ffc857}.Critical{border-left:4px solid #ff6565}</style><h1>NEXUS — отчёт диагностики</h1>");
        html.Append($"<p>{E(Environment.MachineName)} • {result.Timestamp:yyyy-MM-dd HH:mm:ss}</p><h2>Health Score: {(health.OverallScore?.ToString() ?? "недостаточно данных")}</h2><p>Железо: {health.HardwareScore?.ToString() ?? "нет данных"}/100 ({(health.HardwareComplete ? "ключевые показатели доступны" : "частичные данные")}); безопасность: {health.SecurityScore}/100 ({(health.SecurityComplete ? "все этапы" : "частичная проверка")}).</p>");
        html.Append("<p>Баллы — индикаторы обнаруженных признаков, не гарантия исправности или отсутствия угроз. Температуры отражают один снимок. Недоступные датчики и объекты не считаются исправными.</p><h2>Покрытие</h2>");
        foreach (var stage in result.Stages) html.Append($"<article><b>{E(stage.Name)} — {(stage.Completed ? "выполнено" : "недоступно/частично")}</b><pre>{E(stage.Details)}</pre></article>");
        html.Append("<h2>Показания железа</h2>");
        foreach (var r in readings) html.Append($"<p>{E(r.Device)} • {E(r.Metric)}: {r.Value:F1}</p>");
        html.Append("<h2>Находки и рекомендации</h2>");
        foreach (var f in System.Linq.Enumerable.Concat(result.Findings, health.Findings)) html.Append($"<article class='{E(f.Severity)}'><b>[{E(f.Severity)}] {E(f.Title)}</b><pre>{E(f.Evidence)}</pre><p>{E(f.Recommendation)}</p></article>");
        html.Append("</html>");
        File.WriteAllText(stem + ".html", html.ToString(), Encoding.UTF8);
        return stem + ".html";
    }
}
