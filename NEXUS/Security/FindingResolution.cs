using System;

namespace NEXUS.Security;

public sealed record FindingResolution(string Id, string Label, string Details);
public static class FindingResolver
{
    public static FindingResolution Resolve(SecurityFinding finding)
    {
        string title = finding.Title;
        if (finding.Category == "Hardware")
        {
            if (title.Contains("Температура", StringComparison.OrdinalIgnoreCase)) return new("thermal", "Проверить охлаждение", "Показывает температуры и графики. Физическое обслуживание охлаждения выполняется вручную.");
            if (title.Contains("ресурс")) return new("backup", "План резервного копирования", "Сниженный ресурс SSD не восстанавливается программной очисткой: требуется резервная копия и проверка производителя.");
            if (title.Contains("места")) return new("temp", "Освободить место", "Анализ временных файлов, затем подтверждение удаления.");
            if (title.Contains("памяти")) return new("ram", "Управлять приложениями", "Выбор приложения и освобождение рабочего набора; причина высокого расхода RAM может потребовать отдельного решения.");
        }
        if (finding.Category == "Integrity") return new("dism", "Восстановление DISM / SFC", "Восстановление с подтверждением, затем повторная проверка целостности.");
        if (finding.Category == "Defender")
        {
            if (title.Contains("устарели")) return new("defender-update", "Обновить базы Defender", "Запускает штатную команду обновления сигнатур с повышением прав.");
            return new("defender", "Открыть защиту Windows", "Проверьте другой антивирус, текущие угрозы и выполненные действия в журнале защиты.");
        }
        if (finding.Category == "Configuration")
        {
            if (title.Contains("брандмауэр")) return new("firewall", "Включить брандмауэр", "Включает Domain/Private/Public с подтверждением; проверьте политику вашей организации.");
            if (title.Contains("UAC")) return new("uac", "Настроить UAC", "Открывает штатный интерфейс настройки контроля учётных записей.");
        }
        if (finding.Category == "Events") return new("events", "Изучить события входа", "Историческое событие нельзя отменить. Проверьте журнал Security и источник входов.");
        if (finding.Category == "Startup" || finding.Category == "Tasks" || finding.Category == "Services") return new("persistence", "Проверить автозапуск", "Откроет инструменты Windows. Отключение неизвестной службы может нарушить работу приложений.");
        if (finding.Category == "Files" || finding.Category == "Processes" || finding.Category == "Network" || finding.Category == "Correlation") return new("defender-scan", "Проверить Defender", "Запускает быструю проверку Defender. Подозрительные признаки сами по себе не являются основанием для удаления файла.");
        return new("manual", "Показать шаги решения", finding.Recommendation);
    }
}
