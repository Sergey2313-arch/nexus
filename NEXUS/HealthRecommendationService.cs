using System;

namespace NEXUS
{
    public sealed class HealthRecommendationService
    {
        public HealthDiagnosticReport Analyze(HardwareHealthSnapshot snapshot)
        {
            HealthDiagnosticReport report = new();

            AnalyzeCpu(snapshot, report);
            AnalyzeGpu(snapshot, report);
            AnalyzeStorage(snapshot, report);
            AnalyzeDiskSpace(snapshot, report);
            AnalyzeMemory(snapshot, report);

            report.Score = Math.Clamp(report.Score, 0, 100);
            report.Status = report.Score switch
            {
                >= 90 => "NORMAL",
                >= 75 => "ATTENTION",
                >= 55 => "SERVICE ADVISED",
                _ => "HIGH RISK"
            };

            return report;
        }

        private static void AnalyzeCpu(
            HardwareHealthSnapshot snapshot,
            HealthDiagnosticReport report)
        {
            if (!snapshot.CpuTemperature.HasValue)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "CPU",
                    Title = "Температура CPU недоступна",
                    Details = "NEXUS не получает корректное значение датчика температуры процессора.",
                    Recommendation = "Это не считается неисправностью. Позже можно подключить дополнительный источник телеметрии.",
                    Severity = DiagnosticSeverity.Info
                });

                return;
            }

            double temp = snapshot.CpuTemperature.Value;
            double load = snapshot.CpuLoad ?? 0;

            if (temp >= 95)
            {
                report.Score -= 25;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "CPU",
                    Title = $"Критическая температура CPU: {temp:F0} °C",
                    Details = $"Текущая загрузка процессора: {load:F0}%.",
                    Recommendation = "Снизить нагрузку. Если температура повторяется или держится высокой — проверить вентиляторы, радиатор и систему охлаждения; при необходимости обратиться в сервис.",
                    Severity = DiagnosticSeverity.Critical
                });
            }
            else if (temp >= 85)
            {
                report.Score -= 12;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "CPU",
                    Title = $"Высокая температура CPU: {temp:F0} °C",
                    Details = load < 30
                        ? "Температура высокая даже при небольшой текущей нагрузке."
                        : "Температура высокая под нагрузкой.",
                    Recommendation = load < 30
                        ? "Проверить фоновые процессы и состояние охлаждения. Если ноутбук давно не обслуживался — рекомендуется чистка радиаторов и вентиляторов."
                        : "Наблюдать после снижения нагрузки. Если температура долго остаётся высокой — рекомендуется обслуживание системы охлаждения.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else if (temp >= 75 && load < 25)
            {
                report.Score -= 6;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "CPU",
                    Title = $"CPU горячий в лёгком режиме: {temp:F0} °C",
                    Details = $"Загрузка CPU около {load:F0}%.",
                    Recommendation = "Проверить фоновые процессы, вентиляционные отверстия и накопление пыли. Если такое состояние сохраняется — запланировать чистку.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "CPU",
                    Title = $"Температура CPU в норме: {temp:F0} °C",
                    Details = $"Текущая загрузка: {load:F0}%.",
                    Recommendation = "Обслуживание по температуре сейчас не требуется.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static void AnalyzeGpu(
            HardwareHealthSnapshot snapshot,
            HealthDiagnosticReport report)
        {
            if (!snapshot.GpuTemperature.HasValue)
                return;

            double temp = snapshot.GpuTemperature.Value;
            double load = snapshot.GpuLoad ?? 0;

            if (temp >= 95)
            {
                report.Score -= 20;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "GPU",
                    Title = $"Критическая температура GPU: {temp:F0} °C",
                    Details = $"Загрузка GPU: {load:F0}%.",
                    Recommendation = "Снизить нагрузку и проверить охлаждение. При повторении перегрева рекомендуется обслуживание или диагностика в сервисе.",
                    Severity = DiagnosticSeverity.Critical
                });
            }
            else if (temp >= 85)
            {
                report.Score -= 10;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "GPU",
                    Title = $"Высокая температура GPU: {temp:F0} °C",
                    Details = $"Загрузка GPU: {load:F0}%.",
                    Recommendation = "Проверить охлаждение и вентиляцию. Если температура держится высокой после снижения нагрузки — рекомендуется чистка системы охлаждения.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "GPU",
                    Title = $"Температура GPU в норме: {temp:F0} °C",
                    Details = $"Загрузка GPU: {load:F0}%.",
                    Recommendation = "Дополнительное обслуживание сейчас не требуется.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static void AnalyzeStorage(
            HardwareHealthSnapshot snapshot,
            HealthDiagnosticReport report)
        {
            if (!snapshot.StorageTemperature.HasValue &&
                !snapshot.StorageHealth.HasValue)
            {
                return;
            }

            string drive = string.IsNullOrWhiteSpace(snapshot.StorageName)
                ? "SSD"
                : snapshot.StorageName;

            if (snapshot.StorageHealth.HasValue)
            {
                double health = snapshot.StorageHealth.Value;

                if (health <= 40)
                {
                    report.Score -= 30;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Storage",
                        Title = $"{drive}: высокий износ, ресурс {health:F0}%",
                        Details = "Остаточный ресурс накопителя существенно снижен.",
                        Recommendation = "Сделать резервную копию важных данных и планировать замену накопителя. При ошибках чтения/записи — заменить как можно скорее.",
                        Severity = DiagnosticSeverity.Critical
                    });
                }
                else if (health <= 70)
                {
                    report.Score -= 12;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Storage",
                        Title = $"{drive}: заметный износ, ресурс {health:F0}%",
                        Details = "Накопитель ещё может работать нормально, но ресурс уже заметно использован.",
                        Recommendation = "Следить за SMART, регулярно делать резервные копии и наблюдать за дальнейшим падением ресурса.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
                else
                {
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Storage",
                        Title = $"{drive}: ресурс {health:F0}%",
                        Details = "Признаков значительного износа по доступным SMART-данным нет.",
                        Recommendation = "Замена накопителя по износу сейчас не требуется.",
                        Severity = DiagnosticSeverity.Good
                    });
                }
            }

            if (snapshot.StorageTemperature.HasValue)
            {
                double temp = snapshot.StorageTemperature.Value;

                if (temp >= 70)
                {
                    report.Score -= 20;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Storage",
                        Title = $"Высокая температура SSD: {temp:F0} °C",
                        Details = $"Накопитель {drive} работает при слишком высокой температуре.",
                        Recommendation = "Проверить охлаждение накопителя и вентиляцию корпуса. До выяснения причины избегать длительной тяжёлой нагрузки.",
                        Severity = DiagnosticSeverity.Critical
                    });
                }
                else if (temp >= 60)
                {
                    report.Score -= 8;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Storage",
                        Title = $"SSD нагрет: {temp:F0} °C",
                        Details = $"Температура накопителя {drive} повышена.",
                        Recommendation = "Проверить вентиляцию и наличие пыли. Если температура регулярно растёт — проверить охлаждение SSD.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
            }
        }

        private static void AnalyzeDiskSpace(
            HardwareHealthSnapshot snapshot,
            HealthDiagnosticReport report)
        {
            if (!snapshot.DiskUsedPercent.HasValue)
                return;

            double used = snapshot.DiskUsedPercent.Value;

            if (used >= 95)
            {
                report.Score -= 15;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Disk",
                    Title = $"Системный диск заполнен на {used:F0}%",
                    Details = "Критически мало свободного места.",
                    Recommendation = "Освободить место как можно скорее: удалить временные файлы, ненужные загрузки и крупные данные либо перенести их на другой накопитель.",
                    Severity = DiagnosticSeverity.Critical
                });
            }
            else if (used >= 90)
            {
                report.Score -= 8;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Disk",
                    Title = $"Системный диск заполнен на {used:F0}%",
                    Details = "Свободного места осталось мало.",
                    Recommendation = "Рекомендуется освободить место. Это также снизит риск проблем с обновлениями и временными файлами Windows.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else if (used >= 80)
            {
                report.Score -= 3;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Disk",
                    Title = $"Системный диск заполнен на {used:F0}%",
                    Details = "Запас свободного места уже небольшой.",
                    Recommendation = "Запланировать очистку и держать резерв свободного места.",
                    Severity = DiagnosticSeverity.Info
                });
            }
        }

        private static void AnalyzeMemory(
            HardwareHealthSnapshot snapshot,
            HealthDiagnosticReport report)
        {
            if (!snapshot.RamUsedPercent.HasValue)
                return;

            double used = snapshot.RamUsedPercent.Value;

            if (used >= 95)
            {
                report.Score -= 10;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "RAM",
                    Title = $"Оперативная память занята на {used:F0}%",
                    Details = "Система близка к нехватке свободной оперативной памяти.",
                    Recommendation = "Проверить самые тяжёлые процессы в Logbook. Если такая загрузка постоянная при обычной работе — рассмотреть увеличение объёма RAM.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else if (used >= 85)
            {
                report.Score -= 4;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "RAM",
                    Title = $"Высокое использование RAM: {used:F0}%",
                    Details = "Большая часть оперативной памяти занята.",
                    Recommendation = "Проверить процессы с максимальным потреблением памяти и наблюдать, сохраняется ли высокая загрузка длительно.",
                    Severity = DiagnosticSeverity.Info
                });
            }
        }
    }
}