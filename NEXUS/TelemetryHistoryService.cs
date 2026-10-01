using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NEXUS
{
    public sealed class TelemetrySample
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double? CpuLoad { get; set; }
        public double? RamUsedPercent { get; set; }
        public double? DiskUsedPercent { get; set; }
        public double? GpuLoad { get; set; }
        public double? CpuTemperature { get; set; }
        public double? GpuTemperature { get; set; }
        public double? StorageTemperature { get; set; }
        public double? StorageHealth { get; set; }
        public double? UptimeMinutes { get; set; }
    }

    public sealed class TelemetrySummary
    {
        public int Samples { get; set; }
        public DateTime? FirstSample { get; set; }
        public DateTime? LastSample { get; set; }
        public double? AverageCpu { get; set; }
        public double? MaxCpu { get; set; }
        public double? AverageRam { get; set; }
        public double? MaxRam { get; set; }
        public double? MaxGpu { get; set; }
        public double? MaxCpuTemperature { get; set; }
        public double? MaxGpuTemperature { get; set; }
        public double? MaxStorageTemperature { get; set; }
        public double? MinimumStorageHealth { get; set; }
    }

    public sealed class TelemetryHistoryService
    {
        private readonly object _sync = new();
        private readonly string _databasePath;

        public TelemetryHistoryService(string dataFolder)
        {
            Directory.CreateDirectory(dataFolder);
            _databasePath = Path.Combine(dataFolder, "telemetry.db");
            EnsureDatabase();
        }

        public string DatabasePath => _databasePath;

        public void AddSample(TelemetrySample sample)
        {
            lock (_sync)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO TelemetrySamples
(
    Timestamp,
    CpuLoad,
    RamUsedPercent,
    DiskUsedPercent,
    GpuLoad,
    CpuTemperature,
    GpuTemperature,
    StorageTemperature,
    StorageHealth,
    UptimeMinutes
)
VALUES
(
    $timestamp,
    $cpuLoad,
    $ramUsed,
    $diskUsed,
    $gpuLoad,
    $cpuTemp,
    $gpuTemp,
    $storageTemp,
    $storageHealth,
    $uptime
);";

                command.Parameters.AddWithValue("$timestamp", sample.Timestamp.ToString("O", CultureInfo.InvariantCulture));
                AddNullable(command, "$cpuLoad", sample.CpuLoad);
                AddNullable(command, "$ramUsed", sample.RamUsedPercent);
                AddNullable(command, "$diskUsed", sample.DiskUsedPercent);
                AddNullable(command, "$gpuLoad", sample.GpuLoad);
                AddNullable(command, "$cpuTemp", sample.CpuTemperature);
                AddNullable(command, "$gpuTemp", sample.GpuTemperature);
                AddNullable(command, "$storageTemp", sample.StorageTemperature);
                AddNullable(command, "$storageHealth", sample.StorageHealth);
                AddNullable(command, "$uptime", sample.UptimeMinutes);
                command.ExecuteNonQuery();
            }
        }

        public TelemetrySummary GetSummary(TimeSpan window)
        {
            DateTime since = DateTime.Now.Subtract(window);

            lock (_sync)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    COUNT(*),
    MIN(Timestamp),
    MAX(Timestamp),
    AVG(CpuLoad),
    MAX(CpuLoad),
    AVG(RamUsedPercent),
    MAX(RamUsedPercent),
    MAX(GpuLoad),
    MAX(CpuTemperature),
    MAX(GpuTemperature),
    MAX(StorageTemperature),
    MIN(StorageHealth)
FROM TelemetrySamples
WHERE Timestamp >= $since;";
                command.Parameters.AddWithValue("$since", since.ToString("O", CultureInfo.InvariantCulture));

                using SqliteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                    return new TelemetrySummary();

                return new TelemetrySummary
                {
                    Samples = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    FirstSample = ReadDateTime(reader, 1),
                    LastSample = ReadDateTime(reader, 2),
                    AverageCpu = ReadDouble(reader, 3),
                    MaxCpu = ReadDouble(reader, 4),
                    AverageRam = ReadDouble(reader, 5),
                    MaxRam = ReadDouble(reader, 6),
                    MaxGpu = ReadDouble(reader, 7),
                    MaxCpuTemperature = ReadDouble(reader, 8),
                    MaxGpuTemperature = ReadDouble(reader, 9),
                    MaxStorageTemperature = ReadDouble(reader, 10),
                    MinimumStorageHealth = ReadDouble(reader, 11)
                };
            }
        }

        public List<TelemetrySample> GetRecent(int limit)
        {
            int safeLimit = Math.Clamp(limit, 1, 10000);
            List<TelemetrySample> result = new();

            lock (_sync)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
SELECT
    Timestamp,
    CpuLoad,
    RamUsedPercent,
    DiskUsedPercent,
    GpuLoad,
    CpuTemperature,
    GpuTemperature,
    StorageTemperature,
    StorageHealth,
    UptimeMinutes
FROM TelemetrySamples
ORDER BY Id DESC
LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", safeLimit);

                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new TelemetrySample
                    {
                        Timestamp = ReadDateTime(reader, 0) ?? DateTime.MinValue,
                        CpuLoad = ReadDouble(reader, 1),
                        RamUsedPercent = ReadDouble(reader, 2),
                        DiskUsedPercent = ReadDouble(reader, 3),
                        GpuLoad = ReadDouble(reader, 4),
                        CpuTemperature = ReadDouble(reader, 5),
                        GpuTemperature = ReadDouble(reader, 6),
                        StorageTemperature = ReadDouble(reader, 7),
                        StorageHealth = ReadDouble(reader, 8),
                        UptimeMinutes = ReadDouble(reader, 9)
                    });
                }
            }

            result.Reverse();
            return result;
        }

        public int DeleteOlderThan(TimeSpan age)
        {
            DateTime before = DateTime.Now.Subtract(age);

            lock (_sync)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DELETE FROM TelemetrySamples WHERE Timestamp < $before;";
                command.Parameters.AddWithValue("$before", before.ToString("O", CultureInfo.InvariantCulture));
                return command.ExecuteNonQuery();
            }
        }

        public string ExportCsv(string dataFolder)
        {
            Directory.CreateDirectory(dataFolder);
            string path = Path.Combine(dataFolder, $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            List<TelemetrySample> samples = GetRecent(10000);

            StringBuilder csv = new();
            csv.AppendLine("Timestamp,CpuLoad,RamUsedPercent,DiskUsedPercent,GpuLoad,CpuTemperature,GpuTemperature,StorageTemperature,StorageHealth,UptimeMinutes");

            foreach (TelemetrySample sample in samples)
            {
                csv.Append(Csv(sample.Timestamp.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Number(sample.CpuLoad)).Append(',')
                    .Append(Number(sample.RamUsedPercent)).Append(',')
                    .Append(Number(sample.DiskUsedPercent)).Append(',')
                    .Append(Number(sample.GpuLoad)).Append(',')
                    .Append(Number(sample.CpuTemperature)).Append(',')
                    .Append(Number(sample.GpuTemperature)).Append(',')
                    .Append(Number(sample.StorageTemperature)).Append(',')
                    .Append(Number(sample.StorageHealth)).Append(',')
                    .Append(Number(sample.UptimeMinutes)).AppendLine();
            }

            File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            return path;
        }

        private void EnsureDatabase()
        {
            lock (_sync)
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS TelemetrySamples
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    CpuLoad REAL NULL,
    RamUsedPercent REAL NULL,
    DiskUsedPercent REAL NULL,
    GpuLoad REAL NULL,
    CpuTemperature REAL NULL,
    GpuTemperature REAL NULL,
    StorageTemperature REAL NULL,
    StorageHealth REAL NULL,
    UptimeMinutes REAL NULL
);
CREATE INDEX IF NOT EXISTS IX_TelemetrySamples_Timestamp
ON TelemetrySamples(Timestamp);";
                command.ExecuteNonQuery();
            }
        }

        private SqliteConnection OpenConnection()
        {
            SqliteConnection connection = new($"Data Source={_databasePath}");
            connection.Open();
            return connection;
        }

        private static void AddNullable(SqliteCommand command, string name, double? value)
        {
            command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
        }

        private static double? ReadDouble(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;

            return Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static DateTime? ReadDateTime(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;

            string value = reader.GetString(ordinal);
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed
                : null;
        }

        private static string Number(double? value) =>
            value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "";

        private static string Csv(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? '"' + value.Replace("\"", "\"\"") + '"'
                : value;
    }
}
