using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed class ProcessTrustItem
    {
        public string Name { get; set; } = "";
        public int ProcessId { get; set; }
        public string Path { get; set; } = "";
        public string Company { get; set; } = "";
        public string Product { get; set; } = "";
        public bool? HasEmbeddedCertificate { get; set; }
        public string CertificateSubject { get; set; } = "";
        public bool SuspiciousLocation { get; set; }
        public bool SystemLookalikeOutsideWindows { get; set; }
        public long MemoryBytes { get; set; }

        public int RiskPoints =>
            (SuspiciousLocation ? 20 : 0) +
            (SystemLookalikeOutsideWindows ? 35 : 0) +
            (HasEmbeddedCertificate == false && SuspiciousLocation ? 10 : 0);
    }

    public sealed class ProcessTrustReport
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int ProcessesSeen { get; set; }
        public int PathsReadable { get; set; }
        public int Signed { get; set; }
        public int Unsigned { get; set; }
        public int ReviewRequired { get; set; }
        public List<ProcessTrustItem> Items { get; } = new();
    }

    public sealed class ProcessTrustInspectorService
    {
        private static readonly HashSet<string> SystemLookalikes = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "lsass", "csrss", "winlogon", "services",
            "smss", "taskhostw", "conhost", "dwm"
        };

        public Task<ProcessTrustReport> RunAsync()
        {
            return Task.Run(Scan);
        }

        private static ProcessTrustReport Scan()
        {
            ProcessTrustReport report = new();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    report.ProcessesSeen++;

                    string name = process.ProcessName;
                    string path = "";
                    try
                    {
                        path = process.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    report.PathsReadable++;

                    string company = "";
                    string product = "";
                    try
                    {
                        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                        company = version.CompanyName ?? "";
                        product = version.ProductName ?? "";
                    }
                    catch
                    {
                    }

                    bool? signed = TryReadCertificate(path, out string subject);
                    if (signed == true) report.Signed++;
                    else if (signed == false) report.Unsigned++;

                    bool suspiciousLocation = IsWritableOrTemporaryLocation(path);
                    bool lookalike = SystemLookalikes.Contains(name) &&
                                     !path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);

                    ProcessTrustItem item = new()
                    {
                        Name = name,
                        ProcessId = process.Id,
                        Path = path,
                        Company = company,
                        Product = product,
                        HasEmbeddedCertificate = signed,
                        CertificateSubject = subject,
                        SuspiciousLocation = suspiciousLocation,
                        SystemLookalikeOutsideWindows = lookalike,
                        MemoryBytes = SafeMemory(process)
                    };

                    if (item.RiskPoints >= 20)
                        report.ReviewRequired++;

                    report.Items.Add(item);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return report;
        }

        private static bool? TryReadCertificate(string path, out string subject)
        {
            subject = "";
            try
            {
                X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                subject = certificate.Subject ?? "";
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWritableOrTemporaryLocation(string path)
        {
            string normalized = Environment.ExpandEnvironmentVariables(path)
                .Replace('/', '\\');

            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[] reviewFolders =
            {
                Path.GetTempPath(),
                Path.Combine(user, "Downloads"),
                Path.Combine(local, "Temp"),
                Path.Combine(roaming, "Temp")
            };

            return reviewFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Any(folder => normalized.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        }

        private static long SafeMemory(Process process)
        {
            try
            {
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }
    }
}
