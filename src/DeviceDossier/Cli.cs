using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier
{
    public static class Cli
    {
        public const int ExitOk = 0;
        public const int ExitFailed = 1;
        public const int ExitBadArguments = 2;

        private static readonly string Usage =
            AppInfo.Name + " " + AppInfo.Version + " - portable Windows inventory and audit\n\n" +
            "Usage:\n" +
            "  DeviceDossier.exe                      Open the app\n" +
            "  DeviceDossier.exe --silent [options]   Run a full audit without a window\n\n" +
            "Options:\n" +
            "  --output <folder>        Where to save the reports (default: from settings)\n" +
            "  --formats <list>         xlsx, html, xml or all (default: from settings)\n" +
            "  --settings <file>        Use this DeviceDossier.settings.xml instead of the local one\n" +
            "  --skip <list>            drives, security, patches, software, extensions\n" +
            "  --log <file>             Append progress messages to this file\n" +
            "  --asset-tag <text>       Record an asset tag in the report\n" +
            "  --assigned-to <text>     Record who the device is assigned to\n" +
            "  --location <text>        Record the location or department\n" +
            "  --notes <text>           Record free-text notes\n" +
            "  --help                   Show this text\n\n" +
            "Exit codes: 0 = reports saved, 1 = audit failed, 2 = invalid arguments.";

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        public static bool Handles(string[] args)
        {
            return args.Any(a => Is(a, "--silent", "-silent", "/silent", "--help", "-h", "/?", "-?"));
        }

        public static int Run(string[] args)
        {
            bool console = false;
            try { console = AttachConsole(-1); }
            catch { }

            Action<string> write = line =>
            {
                if (console)
                {
                    try { Console.WriteLine(line); }
                    catch { }
                }
            };

            if (args.Any(a => Is(a, "--help", "-h", "/?", "-?")))
            {
                if (console) write("\n" + Usage);
                else MessageBox.Show(Usage, "DeviceDossier command line", MessageBoxButton.OK, MessageBoxImage.Information);
                return ExitOk;
            }

            string output = null, formats = null, settingsFile = null, skip = null, logFile = null;
            string assetTag = null, assignedTo = null, location = null, notes = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (Is(a, "--silent", "-silent", "/silent", App.ElevatedFlag)) continue;
                string name = a, value = null;
                int eq = a.IndexOf('=');
                if (a.StartsWith("--") && eq > 2)
                {
                    name = a.Substring(0, eq);
                    value = a.Substring(eq + 1);
                }
                else if (i + 1 < args.Length)
                {
                    value = args[i + 1];
                }

                bool consumed = value != null && eq < 0;
                if (Is(name, "--output", "-o")) output = value;
                else if (Is(name, "--formats", "-f")) formats = value;
                else if (Is(name, "--settings")) settingsFile = value;
                else if (Is(name, "--skip")) skip = value;
                else if (Is(name, "--log")) logFile = value;
                else if (Is(name, "--asset-tag")) assetTag = value;
                else if (Is(name, "--assigned-to")) assignedTo = value;
                else if (Is(name, "--location")) location = value;
                else if (Is(name, "--notes")) notes = value;
                else return Fail(write, console, "Unknown option: " + a);

                if (string.IsNullOrWhiteSpace(value) || (eq < 0 && value.StartsWith("--"))) return Fail(write, console, "Missing value for " + name);
                if (consumed) i++;
            }

            StreamWriter log = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(logFile))
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(logFile));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    log = new StreamWriter(logFile, true) { AutoFlush = true };
                }
            }
            catch (Exception ex)
            {
                return Fail(write, console, "Cannot write the log file: " + ex.Message);
            }

            Action<string> emit = line =>
            {
                string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line;
                write(stamped);
                if (log != null)
                {
                    try { log.WriteLine(stamped); }
                    catch { }
                }
            };

            try
            {
                AuditSettings settings;
                try
                {
                    settings = string.IsNullOrWhiteSpace(settingsFile) ? AuditSettings.Load() : AuditSettings.Import(settingsFile);
                }
                catch (Exception ex)
                {
                    return Fail(emit, console, "Cannot read the settings file: " + (ex.InnerException ?? ex).Message);
                }

                var options = settings.CreateOptions();
                options.OutputFolder = string.IsNullOrWhiteSpace(output)
                    ? AppPaths.ResolveOutputFolder(settings, null)
                    : Environment.ExpandEnvironmentVariables(output.Trim().Trim('"'));

                if (formats != null)
                {
                    var f = ReportFormats.None;
                    foreach (var part in List(formats))
                    {
                        if (Is(part, "xlsx", "excel")) f |= ReportFormats.Excel;
                        else if (Is(part, "html", "htm")) f |= ReportFormats.Html;
                        else if (Is(part, "xml")) f |= ReportFormats.Xml;
                        else if (Is(part, "all")) f |= ReportFormats.Excel | ReportFormats.Html | ReportFormats.Xml;
                        else return Fail(emit, console, "Unknown format: " + part);
                    }
                    options.Formats = f;
                }

                if (skip != null)
                {
                    foreach (var part in List(skip))
                    {
                        if (Is(part, "drives", "mapped")) options.IncludeMappedDrives = false;
                        else if (Is(part, "security")) options.IncludeSecurity = false;
                        else if (Is(part, "patches", "updates")) options.IncludePatchHistory = false;
                        else if (Is(part, "software", "apps")) options.IncludeSoftware = false;
                        else if (Is(part, "extensions")) options.IncludeBrowserExtensions = false;
                        else return Fail(emit, console, "Unknown section for --skip: " + part);
                    }
                }

                options.AssetTag = assetTag;
                options.AssignedTo = assignedTo;
                options.Location = location;
                options.Notes = notes;

                if (AppPaths.IsCloudSynced(options.OutputFolder))
                    emit("WARNING: " + options.OutputFolder + " is synced to OneDrive - the reports will be uploaded to the cloud.");
                if (!Elevation.IsAdministrator())
                    emit("WARNING: not running as administrator - some values will be reported as unavailable.");

                var progress = new ImmediateProgress(p => emit((p.Level == LogLevel.Error ? "ERROR: " : p.Level == LogLevel.Warning ? "WARNING: " : "") + p.Message));
                var result = AuditEngine.Run(options, settings, progress, CancellationToken.None);
                foreach (var file in new[] { result.ExcelPath, result.HtmlPath, result.XmlPath }.Where(x => x != null))
                    emit("Saved: " + file);
                return ExitOk;
            }
            catch (Exception ex)
            {
                App.WriteCrashLog(ex);
                emit("ERROR: audit failed - " + ex.Message);
                return ExitFailed;
            }
            finally
            {
                if (log != null) log.Dispose();
            }
        }

        private static int Fail(Action<string> write, bool console, string message)
        {
            write("ERROR: " + message + (console ? "\n\n" + Usage : ""));
            return ExitBadArguments;
        }

        private static IEnumerable<string> List(string value)
        {
            return value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
        }

        private static bool Is(string value, params string[] options)
        {
            return options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class ImmediateProgress : IProgress<AuditProgress>
        {
            private readonly Action<AuditProgress> _handler;

            public ImmediateProgress(Action<AuditProgress> handler)
            {
                _handler = handler;
            }

            public void Report(AuditProgress value)
            {
                _handler(value);
            }
        }
    }
}
