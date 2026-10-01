using System;
using System.Collections.Generic;

namespace NEXUS
{
    public enum DiagnosticSeverity
    {
        Info,
        Good,
        Warning,
        Critical
    }

    public sealed class DiagnosticFinding
    {
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Details { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
    }

    public sealed class HardwareHealthSnapshot
    {
        public double? CpuTemperature { get; set; }
        public double? CpuLoad { get; set; }
        public double? GpuTemperature { get; set; }
        public double? GpuLoad { get; set; }
        public double? StorageTemperature { get; set; }
        public double? StorageHealth { get; set; }
        public double? StorageLifeUsed { get; set; }
        public double? DiskUsedPercent { get; set; }
        public double? RamUsedPercent { get; set; }
        public string StorageName { get; set; } = "";
    }

    public sealed class HealthDiagnosticReport
    {
        public int Score { get; set; } = 100;
        public string Status { get; set; } = "NORMAL";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<DiagnosticFinding> Findings { get; } = new();
    }

    public sealed class SecurityDiagnosticReport
    {
        public int Score { get; set; } = 100;
        public string Status { get; set; } = "PROTECTED";
        public bool IsAdministrator { get; set; }

        public bool? DefenderEnabled { get; set; }
        public bool? RealTimeProtectionEnabled { get; set; }
        public bool? FirewallEnabled { get; set; }

        public int RunningProcesses { get; set; }
        public int SuspiciousProcesses { get; set; }
        public int AutorunEntries { get; set; }
        public int SuspiciousAutoruns { get; set; }
        public int AutomaticServices { get; set; }
        public int SuspiciousServices { get; set; }
        public int EstablishedConnections { get; set; }
        public int ListeningPorts { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<DiagnosticFinding> Findings { get; } = new();
    }
}