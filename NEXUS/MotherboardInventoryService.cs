using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed class MotherboardInventoryService
    {
        public Task<MotherboardInventorySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Capture(cancellationToken), cancellationToken);
        }

        private static MotherboardInventorySnapshot Capture(CancellationToken cancellationToken)
        {
            MotherboardInventorySnapshot snapshot = new();

            try
            {
                snapshot.Board = QuerySingle(
                    "SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard",
                    item => new MotherboardInfo
                    {
                        Manufacturer = Read(item, "Manufacturer"),
                        Product = Read(item, "Product"),
                        Version = Read(item, "Version"),
                        SerialNumber = Read(item, "SerialNumber")
                    });
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add("Не удалось прочитать сведения о материнской плате: " + ex.Message);
            }

            try
            {
                snapshot.Bios = QuerySingle(
                    "SELECT Manufacturer, SMBIOSBIOSVersion, Version, ReleaseDate, SerialNumber FROM Win32_BIOS",
                    item => new BiosInfo
                    {
                        Manufacturer = Read(item, "Manufacturer"),
                        Version = FirstNotEmpty(Read(item, "SMBIOSBIOSVersion"), Read(item, "Version")),
                        SerialNumber = Read(item, "SerialNumber"),
                        ReleaseDate = ParseWmiDate(Read(item, "ReleaseDate"))
                    });
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add("Не удалось прочитать сведения BIOS/UEFI: " + ex.Message);
            }

            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SerialNumber FROM Win32_PhysicalMemory");

                foreach (ManagementObject item in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    snapshot.MemoryModules.Add(new MemoryModuleInfo
                    {
                        Bank = FirstNotEmpty(Read(item, "DeviceLocator"), Read(item, "BankLabel")),
                        CapacityBytes = ReadLong(item, "Capacity"),
                        SpeedMhz = ReadInt(item, "ConfiguredClockSpeed", ReadInt(item, "Speed", 0)),
                        Manufacturer = Read(item, "Manufacturer"),
                        PartNumber = Read(item, "PartNumber"),
                        SerialNumber = Read(item, "SerialNumber")
                    });
                    item.Dispose();
                }
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add("Не удалось прочитать модули оперативной памяти: " + ex.Message);
            }

            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT Name, PNPClass, Status, ConfigManagerErrorCode, PNPDeviceID, Manufacturer, Service FROM Win32_PnPEntity WHERE Name IS NOT NULL");

                foreach (ManagementObject item in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int errorCode = ReadInt(item, "ConfigManagerErrorCode", -1);
                    snapshot.Devices.Add(new BoardDeviceInfo
                    {
                        Name = Read(item, "Name"),
                        Category = NormalizeCategory(Read(item, "PNPClass")),
                        Status = Read(item, "Status"),
                        ErrorCode = errorCode,
                        DeviceId = Read(item, "PNPDeviceID"),
                        Manufacturer = Read(item, "Manufacturer"),
                        Service = Read(item, "Service")
                    });
                    item.Dispose();

                    if (snapshot.Devices.Count >= 600)
                    {
                        snapshot.Warnings.Add("Список устройств ограничен первыми 600 записями.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add("Не удалось получить список PnP-устройств: " + ex.Message);
            }

            snapshot.CapturedAt = DateTime.Now;
            return snapshot;
        }

        private static T? QuerySingle<T>(string query, Func<ManagementObject, T> projector) where T : class
        {
            using ManagementObjectSearcher searcher = new(query);
            foreach (ManagementObject item in searcher.Get())
            {
                try
                {
                    return projector(item);
                }
                finally
                {
                    item.Dispose();
                }
            }

            return null;
        }

        private static string Read(ManagementBaseObject item, string property)
        {
            try
            {
                return Convert.ToString(item[property], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long ReadLong(ManagementBaseObject item, string property)
        {
            try
            {
                return Convert.ToInt64(item[property], CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadInt(ManagementBaseObject item, string property, int fallback)
        {
            try
            {
                object? value = item[property];
                return value == null ? fallback : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static string ParseWmiDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            try
            {
                return ManagementDateTimeConverter.ToDateTime(value).ToString("dd.MM.yyyy");
            }
            catch
            {
                return value;
            }
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string NormalizeCategory(string category)
        {
            return category.ToUpperInvariant() switch
            {
                "DISPLAY" => "Видеоадаптеры",
                "NET" => "Сетевые устройства",
                "BLUETOOTH" => "Bluetooth",
                "USB" => "USB-контроллеры и устройства",
                "HDC" => "Контроллеры накопителей",
                "SCSIADAPTER" => "Контроллеры накопителей",
                "MEDIA" => "Аудио и мультимедиа",
                "SYSTEM" => "Системные устройства",
                "PROCESSOR" => "Процессоры",
                "BATTERY" => "Питание и батарея",
                "HIDCLASS" => "Устройства ввода",
                "KEYBOARD" => "Устройства ввода",
                "MOUSE" => "Устройства ввода",
                "PORTS" => "Порты",
                "MONITOR" => "Мониторы",
                "SOFTWARECOMPONENT" => "Программные компоненты устройств",
                "FIRMWARE" => "Прошивки",
                "" => "Прочие устройства",
                _ => category
            };
        }
    }

    public sealed class MotherboardInventorySnapshot
    {
        public MotherboardInfo? Board { get; set; }
        public BiosInfo? Bios { get; set; }
        public List<MemoryModuleInfo> MemoryModules { get; } = new();
        public List<BoardDeviceInfo> Devices { get; } = new();
        public List<string> Warnings { get; } = new();
        public DateTime CapturedAt { get; set; }
    }

    public sealed class MotherboardInfo
    {
        public string Manufacturer { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
    }

    public sealed class BiosInfo
    {
        public string Manufacturer { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
    }

    public sealed class MemoryModuleInfo
    {
        public string Bank { get; set; } = string.Empty;
        public long CapacityBytes { get; set; }
        public int SpeedMhz { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
    }

    public sealed class BoardDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ErrorCode { get; set; } = -1;
        public string DeviceId { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;

        public bool HasProblem => ErrorCode > 0 ||
                                  (!string.IsNullOrWhiteSpace(Status) &&
                                   !Status.Equals("OK", StringComparison.OrdinalIgnoreCase));
    }
}
