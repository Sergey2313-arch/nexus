using System;
using System.IO;
using System.Text.Json;

namespace NEXUS.Services;

public sealed record AiConnectionSettings(string Endpoint, string Model);
public static class AiConnectionSettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "ai-connection.json");
    public static AiConnectionSettings? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Файл настроек подключения слишком большой.");
        var settings = JsonSerializer.Deserialize<AiConnectionSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Не удалось прочитать настройки подключения.");
        Validate(settings);
        return settings;
    }
    public static void Save(AiConnectionSettings settings, string? path = null)
    {
        Validate(settings);
        path ??= DefaultPath;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate(AiConnectionSettings settings)
    {
        if (settings.Endpoint == null || settings.Endpoint.Length > 4096 || settings.Model == null || settings.Model.Length > 256 || string.IsNullOrWhiteSpace(settings.Model) || System.Linq.Enumerable.Any(settings.Model, char.IsControl)) throw new ArgumentException("Укажите адрес API и имя модели (до 256 символов).");
        AiAssistantService.ValidateEndpoint(settings.Endpoint);
    }
}
