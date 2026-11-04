using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace DeviceDossier.Util
{
    public static class Elevation
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRestartElevated(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(AppPaths.ExePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = arguments ?? "",
                    WorkingDirectory = AppPaths.ExeDirectory
                };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }

    public static class AppPaths
    {
        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static string ExeDirectory
        {
            get { return Path.GetDirectoryName(ExePath); }
        }

        public static string ExeDriveRoot
        {
            get
            {
                try { return Path.GetPathRoot(ExePath); }
                catch { return null; }
            }
        }

        public static bool RunningFromRemovableDrive
        {
            get
            {
                try
                {
                    var root = ExeDriveRoot;
                    if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return false;
                    return new DriveInfo(root).DriveType == DriveType.Removable;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool RunningFromUsb
        {
            get { return RunningFromRemovableDrive || UsbDrives.IsUsbRoot(ExeDriveRoot); }
        }

        public static string ResolveOutputFolder(Core.AuditSettings settings, string usbRoot)
        {
            switch (settings.SaveLocation)
            {
                case Core.SaveLocationMode.UsbDrive:
                    if (!string.IsNullOrEmpty(usbRoot)) return Path.Combine(usbRoot, settings.ReportFolderName);
                    var first = UsbDrives.List().FirstOrDefault();
                    return first != null ? Path.Combine(first.Root, settings.ReportFolderName) : Path.Combine(DesktopFolder, settings.ReportFolderName);
                case Core.SaveLocationMode.Folder:
                    if (!string.IsNullOrWhiteSpace(settings.CustomFolder)) return Environment.ExpandEnvironmentVariables(settings.CustomFolder.Trim());
                    return Path.Combine(DesktopFolder, settings.ReportFolderName);
                default:
                    return Path.Combine(DesktopFolder, settings.ReportFolderName);
            }
        }

        public static bool IsCloudSynced(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
                foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
                {
                    string root = Environment.GetEnvironmentVariable(variable);
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    root = root.TrimEnd('\\') + "\\";
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public static string DesktopFolder
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        }

        public static bool EnsureWritable(string folder, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, "._writetest_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "test");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public sealed class ProcessResult
    {
        public bool Started;
        public bool TimedOut;
        public int ExitCode;
        public string Output;
    }

    public static class ProcessRunner
    {
        public static ProcessResult Run(string file, string arguments, int timeoutMs)
        {
            var result = new ProcessResult { Output = "" };
            var psi = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var output = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { };
                try
                {
                    p.Start();
                    result.Started = true;
                }
                catch
                {
                    return result;
                }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    try { p.Kill(); } catch { }
                }
                else
                {
                    p.WaitForExit();
                    result.ExitCode = p.ExitCode;
                }
            }
            lock (output) result.Output = output.ToString();
            return result;
        }
    }

    public static class Timeout
    {
        public static bool RunSta<T>(Func<T> work, int timeoutMs, out T value, out Exception error)
        {
            T local = default(T);
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { local = work(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            bool finished = thread.Join(timeoutMs);
            value = finished ? local : default(T);
            error = finished ? failure : null;
            return finished;
        }
    }
}
