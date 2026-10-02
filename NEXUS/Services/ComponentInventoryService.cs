using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS.Services;

public sealed record ComponentItem(string Kind, string Name, string Details);
public static class ComponentInventoryService
{
    public static async Task<List<ComponentItem>> ReadAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new()
            $items = @(
                Get-CimInstance Win32_Processor | ForEach-Object { [pscustomobject]@{Kind='CPU';Name=$_.Name.Trim();Details=('Ядра: ' + $_.NumberOfCores + '; потоки: ' + $_.NumberOfLogicalProcessors + '; максимальная частота: ' + $_.MaxClockSpeed + ' MHz')} }
                Get-CimInstance Win32_VideoController | ForEach-Object { [pscustomobject]@{Kind='GPU';Name=$_.Name;Details=('Драйвер: ' + $_.DriverVersion + '; видеопроцессор: ' + $_.VideoProcessor)} }
                Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [pscustomobject]@{Kind='RAM';Name=(([string]$_.Manufacturer).Trim() + ' ' + ([string]$_.PartNumber).Trim());Details=('Слот: ' + $_.DeviceLocator + '; ёмкость: ' + [math]::Round($_.Capacity/1GB,1) + ' GB; частота: ' + $_.ConfiguredClockSpeed + ' MHz')} }
                Get-CimInstance Win32_BaseBoard | ForEach-Object { [pscustomobject]@{Kind='Board';Name=($_.Manufacturer + ' ' + $_.Product);Details=('Версия: ' + $_.Version)} }
                Get-CimInstance Win32_DiskDrive | ForEach-Object { [pscustomobject]@{Kind='Storage';Name=$_.Model;Details=('Ёмкость: ' + [math]::Round($_.Size/1GB,1) + ' GB; интерфейс: ' + $_.InterfaceType + '; ' + $_.MediaType)} }
                Get-CimInstance Win32_BIOS | ForEach-Object { [pscustomobject]@{Kind='BIOS';Name=($_.Manufacturer + ' ' + $_.SMBIOSBIOSVersion);Details=('Версия BIOS: ' + $_.Version)} }
            ); ConvertTo-Json -InputObject $items -Depth 4 -Compress
            """;
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell недоступен");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(output,error); cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException("Сбор компонентов превысил 30 секунд"); }
        string json = await output, failure = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(failure);
        return JsonSerializer.Deserialize<List<ComponentItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? new();
    }
}
