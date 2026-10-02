using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace NEXUS
{
    public sealed class LogEvent
    {
        public long Id { get; set; }

        public DateTime Timestamp { get; set; }

        public string Category { get; set; } = "";

        public string EventType { get; set; } = "";

        public string Source { get; set; } = "";

        public string Title { get; set; } = "";

        public string Details { get; set; } = "";

        public int? ProcessId { get; set; }

        public string FilePath { get; set; } = "";

        public string Severity { get; set; } = "Info";
    }

    public sealed class LogService
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public LogService(string? databasePath = null)
        {
            string nexusFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "NEXUS");

            if (databasePath != null) nexusFolder = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
            Directory.CreateDirectory(nexusFolder);

            _databasePath =
                databasePath == null ? Path.Combine(nexusFolder, "nexus.db") : Path.GetFullPath(databasePath);

            _connectionString =
                $"Data Source={_databasePath}";

            InitializeDatabase();
        }

        public string DatabasePath =>
            _databasePath;

        private void InitializeDatabase()
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS LogEvents
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                Timestamp TEXT NOT NULL,

                Category TEXT NOT NULL,

                EventType TEXT NOT NULL,

                Source TEXT,

                Title TEXT,

                Details TEXT,

                ProcessId INTEGER,

                FilePath TEXT,

                Severity TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS
            IX_LogEvents_Timestamp
            ON LogEvents(Timestamp);

            CREATE INDEX IF NOT EXISTS
            IX_LogEvents_Category
            ON LogEvents(Category);
            """;

            command.ExecuteNonQuery();
        }

        public void Write(
            string category,
            string eventType,
            string source,
            string title,
            string details = "",
            int? processId = null,
            string filePath = "",
            string severity = "Info")
        {
            try
            {
                using SqliteConnection connection =
                    new SqliteConnection(
                        _connectionString);

                connection.Open();

                SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                """
                INSERT INTO LogEvents
                (
                    Timestamp,
                    Category,
                    EventType,
                    Source,
                    Title,
                    Details,
                    ProcessId,
                    FilePath,
                    Severity
                )
                VALUES
                (
                    $timestamp,
                    $category,
                    $eventType,
                    $source,
                    $title,
                    $details,
                    $processId,
                    $filePath,
                    $severity
                );
                """;

                command.Parameters.AddWithValue(
                    "$timestamp",
                    DateTime.Now.ToString("O"));

                command.Parameters.AddWithValue(
                    "$category",
                    category);

                command.Parameters.AddWithValue(
                    "$eventType",
                    eventType);

                command.Parameters.AddWithValue(
                    "$source",
                    source);

                command.Parameters.AddWithValue(
                    "$title",
                    title);

                command.Parameters.AddWithValue(
                    "$details",
                    details);

                command.Parameters.AddWithValue(
                    "$processId",
                    processId.HasValue
                        ? processId.Value
                        : DBNull.Value);

                command.Parameters.AddWithValue(
                    "$filePath",
                    filePath);

                command.Parameters.AddWithValue(
                    "$severity",
                    severity);

                command.ExecuteNonQuery();
            }
            catch
            {
                // Журнал никогда не должен
                // ломать основную работу NEXUS.
            }
        }

        public List<LogEvent> GetLatest(
            int count = 250, string? category = null)
        {
            List<LogEvent> result =
                new List<LogEvent>();

            using SqliteConnection connection =
                new SqliteConnection(
                    _connectionString);

            connection.Open();

            SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            SELECT
                Id,
                Timestamp,
                Category,
                EventType,
                Source,
                Title,
                Details,
                ProcessId,
                FilePath,
                Severity

            FROM LogEvents

            WHERE ($category IS NULL OR Category = $category OR ($category = 'Action' AND EventType IN ('MemoryTrim', 'TempCleanup', 'SystemRepair', 'ResolutionAction') AND Source IN ('Maintenance', 'AssistantTasks', 'FindingActions')))

            ORDER BY Id DESC

            LIMIT $count;
            """;

            command.Parameters.AddWithValue(
                "$count",
                count);
            command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                LogEvent item =
                    new LogEvent();

                item.Id =
                    reader.GetInt64(0);

                item.Timestamp =
                    DateTime.Parse(
                        reader.GetString(1));

                item.Category =
                    reader.GetString(2);

                item.EventType =
                    reader.GetString(3);

                item.Source =
                    reader.IsDBNull(4)
                        ? ""
                        : reader.GetString(4);

                item.Title =
                    reader.IsDBNull(5)
                        ? ""
                        : reader.GetString(5);

                item.Details =
                    reader.IsDBNull(6)
                        ? ""
                        : reader.GetString(6);

                item.ProcessId =
                    reader.IsDBNull(7)
                        ? null
                        : reader.GetInt32(7);

                item.FilePath =
                    reader.IsDBNull(8)
                        ? ""
                        : reader.GetString(8);

                item.Severity =
                    reader.GetString(9);

                result.Add(item);
            }

            return result;
        }
    }
}