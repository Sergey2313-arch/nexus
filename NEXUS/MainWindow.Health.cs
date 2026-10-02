using LibreHardwareMonitor.Hardware;
using Microsoft.UI.Xaml;
using NEXUS.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NEXUS;

public sealed partial class MainWindow
{
    private string? _diagnosticReportPath;

    private List<HardwareReading> CaptureHardwareReadings()
    {
        var readings = new List<HardwareReading>();
        foreach (var hardware in _hardwareMonitor.Hardware) Capture(hardware, readings);
        foreach (var disk in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            try { if (disk.TotalSize > 0) readings.Add(new(disk.Name, "Disk Used", 100.0 * (disk.TotalSize - disk.AvailableFreeSpace) / disk.TotalSize)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var memory = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memory)) readings.Add(new("RAM", "RAM Load", memory.dwMemoryLoad));
        return readings;
    }

    private static void Capture(IHardware hardware, List<HardwareReading> readings)
    {
        bool gpu = hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuIntel;
        string? metric = hardware.HardwareType == HardwareType.Cpu ? "CPU Temperature" : gpu ? "GPU Temperature" : hardware.HardwareType == HardwareType.Storage ? "Storage Temperature" : null;
        var temperatures = hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).Select(s => (double)s.Value!.Value).ToList();
        if (metric != null && temperatures.Count > 0) readings.Add(new(hardware.Name, metric, temperatures.Max()));
        if (hardware.HardwareType == HardwareType.Storage)
        {
            var health = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Level && s.Name.Contains("remaining", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
            if (health != null) readings.Add(new(hardware.Name, "Storage Health", health.Value!.Value));
            else
            {
                var used = hardware.Sensors.FirstOrDefault(s => s.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
                if (used != null) readings.Add(new(hardware.Name, "Storage Health", Math.Clamp(100 - used.Value!.Value, 0, 100)));
            }
        }
        foreach (var child in hardware.SubHardware) Capture(child, readings);
    }

    private void OpenDiagnosticReportButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_diagnosticReportPath != null) Process.Start(new ProcessStartInfo(_diagnosticReportPath) { UseShellExecute = true }); }
        catch (Exception ex) { SecurityScanStatusText.Text = "Не удалось открыть отчёт: " + ex.Message; }
    }
}
