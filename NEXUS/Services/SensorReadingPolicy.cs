using System;

namespace NEXUS.Services;

public static class SensorReadingPolicy
{
    public static double? CurrentValue(string kind, double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return null;
        if (kind == "Temperature" && value.Value <= 1) return null;
        if (kind == "Clock" && value.Value <= 0) return null;
        return value;
    }

    public static bool IsGpuCoreTemperature(string name) =>
        !name.Contains("SoC", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("VR", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Memory", StringComparison.OrdinalIgnoreCase);
}
