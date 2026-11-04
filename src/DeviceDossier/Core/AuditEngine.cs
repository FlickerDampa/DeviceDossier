using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DeviceDossier.Collectors;
using DeviceDossier.Export;
using DeviceDossier.Util;

namespace DeviceDossier.Core
{
    public sealed class AuditStep
    {
        public AuditStep(string id, string label, Func<AuditContext, AuditReport, Section> run, Func<AuditOptions, bool> enabled, Func<Section> skipped)
        {
            Id = id;
            Label = label;
            Run = run;
            Enabled = enabled;
            Skipped = skipped;
        }

        public string Id { get; private set; }
        public string Label { get; private set; }
        public Func<AuditContext, AuditReport, Section> Run { get; private set; }
        public Func<AuditOptions, bool> Enabled { get; private set; }
        public Func<Section> Skipped { get; private set; }
    }

    public sealed class AuditResult
    {
        public AuditReport Report { get; set; }
        public string ExcelPath { get; set; }
        public string HtmlPath { get; set; }
        public string XmlPath { get; set; }
        public string Folder { get; set; }
        public string SaveNote { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public static class AuditEngine
    {
        private const string SkippedText = "Skipped (not selected for this audit)";

        public static List<AuditStep> Steps()
        {
            return new List<AuditStep>
            {
                new AuditStep("device", "Device, BIOS and chassis", (c, r) => DeviceCollector.Device(c, r), null, null),
                new AuditStep("os", "Operating system and activation", (c, r) => OsCollector.Collect(c), null, null),
                new AuditStep("cpu", "Processor", (c, r) => HardwareCollector.Cpu(c), null, null),
                new AuditStep("ram", "Memory", (c, r) => HardwareCollector.Memory(c), null, null),
                new AuditStep("gpu", "Graphics", (c, r) => HardwareCollector.Gpu(c), null, null),
                new AuditStep("storage", "Storage and drive health", (c, r) => HardwareCollector.Storage(c), null, null),
                new AuditStep("motherboard", "Motherboard", (c, r) => DeviceCollector.Motherboard(c), null, null),
                new AuditStep("power", "Battery / power supply", (c, r) => DeviceCollector.Power(c, r), null, null),
                new AuditStep("network", "Network adapters", (c, r) => NetworkCollector.Adapters(c), null, null),
                new AuditStep("drives", "Mapped network drives (all profiles)", (c, r) => NetworkCollector.MappedDrives(c), o => o.IncludeMappedDrives,
                    () => new Section("drives", "8", "MAPPED NETWORK DRIVES (SERVER AUDIT)").With(
                        Block.Table("User Profile", "Drive Letter", "Status", "Server IP", "Matches Known Server List", "Subfolder Path", "Full UNC Path")
                            .AddRow(Fmt.NA, Fmt.NA, SkippedText, Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA))),
                new AuditStep("security", "Security (AV, BitLocker, patches, admins)", (c, r) => SecurityCollector.Collect(c), o => o.IncludeSecurity, SkippedSecurity),
                new AuditStep("software", "Installed software", (c, r) => SoftwareCollector.Installed(c), o => o.IncludeSoftware,
                    () => new Section("software", "10", "INSTALLED SOFTWARE - Total: 0 apps").With(
                        Block.Table("No.", "Software Name", "Version", "Publisher", "Install Date", "Install Location").AddRow(Fmt.NA, SkippedText, Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA))),
                new AuditStep("extensions", "Browser extensions", (c, r) => SoftwareCollector.BrowserExtensions(c), o => o.IncludeBrowserExtensions,
                    () => new Section("extensions", "10b", "BROWSER EXTENSIONS - Total: 0").With(
                        Block.Table("Browser", "User Profile", "Extension Name", "Version", "Extension ID").AddRow(Fmt.NA, Fmt.NA, SkippedText, Fmt.NA, Fmt.NA))),
                new AuditStep("monitors", "Monitors and displays", (c, r) => DisplayCollector.Monitors(c), null, null),
                new AuditStep("baseline", "Security baseline (TPM, Secure Boot, firewall)", (c, r) => SecurityCollector.Baseline(c), o => o.IncludeSecurity,
                    () => new Section("baseline", "12", "SECURITY BASELINE").With(Block.KeyValue().Add("Status", SkippedText)))
            };
        }

        private static Section SkippedSecurity()
        {
            return new Section("security", "9", "SECURITY")
                .With(Block.Table("Product Name", "Details").WithCaption("Installed EDR / Antivirus", CaptionStyle.Bold).AddRow(Fmt.NA, SkippedText))
                .With(Block.Table("Drive", "Protection Status", "Volume Status", "Encryption %", "Key Protector(s)").WithCaption("BitLocker Status", CaptionStyle.Bold).AddRow(Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, SkippedText))
                .With(Block.KeyValue().WithCaption("Patch & Update Level", CaptionStyle.Bold).Add("Latest Security Update", SkippedText).Add("Latest Feature Update", Fmt.NA).Add("Latest Windows (Quality) Update", Fmt.NA).Add("Latest General/Suite Update", Fmt.NA))
                .With(Block.Table("Account Name", "Type", "Source").WithCaption("Local Administrative Accounts", CaptionStyle.Bold).AddRow(Fmt.NA, Fmt.NA, SkippedText));
        }

        public static Task<AuditResult> RunAsync(AuditOptions options, AuditSettings settings, IProgress<AuditProgress> progress, CancellationToken token)
        {
            return Task.Factory.StartNew(() => Run(options, settings, progress, token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public static AuditResult Run(AuditOptions options, AuditSettings settings, IProgress<AuditProgress> progress, CancellationToken token)
        {
            var started = DateTime.Now;
            var steps = Steps();
            var ctx = new AuditContext(settings, options, progress, token)
            {
                IsAdmin = Elevation.IsAdministrator(),
                TotalSteps = steps.Count + 1
            };
            var report = new AuditReport
            {
                RanAsAdmin = ctx.IsAdmin,
                RunAsUser = Environment.UserDomainName + "\\" + Environment.UserName,
                ToolVersion = AppInfo.Version,
                Generated = started
            };

            ctx.Info("IT audit started on " + Environment.MachineName + (ctx.IsAdmin ? " (administrator)" : " (limited mode - not administrator)"));
            NetworkCollector.LoadProfiles(ctx);

            for (int i = 0; i < steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = steps[i];
                ctx.Step = i + 1;
                ctx.StepId = step.Id;
                Section section;
                if (step.Enabled != null && !step.Enabled(options))
                {
                    ctx.Report("Skipped: " + step.Label, LogLevel.Warning);
                    section = step.Skipped();
                    section.State = SectionState.Skipped;
                }
                else
                {
                    ctx.Info("Collecting " + step.Label.ToLowerInvariant());
                    try
                    {
                        section = RunWithLimit(step, ctx, report, StepLimitSeconds(step, settings), token);
                        if (section.State == SectionState.Warning) ctx.Warn("  " + section.StateNote);
                        else if (section.State != SectionState.Error) ctx.Ok(step.Label + " done");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ctx.Error(step.Label + " FAILED - bypassed. " + ex.Message);
                        section = ErrorSection(step, ex);
                    }
                }
                report.Add(section);
            }

            if (string.IsNullOrEmpty(report.DeviceName)) report.DeviceName = Environment.MachineName;
            if (options.HasAssetRecord) report.Sections.Insert(0, AssetSection(options));

            ctx.Step = steps.Count + 1;
            ctx.StepId = "save";
            ctx.Info("Saving report");
            var result = new AuditResult { Report = report };
            string folder = options.OutputFolder;
            string error;
            if (string.IsNullOrWhiteSpace(folder) || !AppPaths.EnsureWritable(folder, out error))
            {
                string reason = string.IsNullOrWhiteSpace(folder) ? "no folder chosen" : "folder not writable";
                folder = AppPaths.DesktopFolder;
                result.SaveNote = "Could not save to the chosen folder (" + reason + ") - saved to the Desktop instead.";
                ctx.Error(result.SaveNote);
            }

            string stamp = started.ToString("ddMMyyyy_HHmm", CultureInfo.InvariantCulture);
            string baseName = SafeFileName(settings.FileNamePrefix) + "_" + SafeFileName(report.DeviceName) + "_" + stamp;
            string stem = Path.Combine(folder, baseName);
            for (int n = 2; File.Exists(stem + ".xlsx") || File.Exists(stem + ".html") || File.Exists(stem + ".xml"); n++)
                stem = Path.Combine(folder, baseName + "_" + n);

            var formats = options.Formats == ReportFormats.None ? ReportFormats.Excel : options.Formats;
            if ((formats & ReportFormats.Excel) != 0)
            {
                result.ExcelPath = stem + ".xlsx";
                AuditExcelExporter.Save(report, settings, result.ExcelPath);
                ctx.Ok("Excel report saved: " + result.ExcelPath);
            }
            if ((formats & ReportFormats.Html) != 0)
            {
                result.HtmlPath = stem + ".html";
                HtmlReportExporter.Save(report, result.HtmlPath);
                ctx.Ok("HTML report saved: " + result.HtmlPath);
            }
            if ((formats & ReportFormats.Xml) != 0)
            {
                result.XmlPath = stem + ".xml";
                XmlReportFile.Save(report, result.XmlPath);
                ctx.Ok("XML report saved: " + result.XmlPath);
            }
            report.SavedTo = result.ExcelPath ?? result.HtmlPath ?? result.XmlPath;
            result.Folder = folder;
            result.Duration = DateTime.Now - started;
            ctx.Ok("AUDIT COMPLETE - saved to " + folder);
            return result;
        }

        private static int StepLimitSeconds(AuditStep step, AuditSettings settings)
        {
            int limit = settings.StepTimeoutSeconds;
            if (step.Id == "security") limit = Math.Max(limit, settings.PatchTimeoutSeconds + 120);
            return limit;
        }

        private static Section RunWithLimit(AuditStep step, AuditContext ctx, AuditReport report, int seconds, CancellationToken token)
        {
            var work = Task.Factory.StartNew(() => step.Run(ctx, report), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            bool finished;
            try
            {
                finished = work.Wait(checked(seconds * 1000), token);
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException ?? ex;
                if (inner is OperationCanceledException) throw new OperationCanceledException(token);
                throw inner;
            }
            catch (OperationCanceledException)
            {
                Abandon(work);
                throw;
            }
            if (!finished)
            {
                Abandon(work);
                throw new TimeoutException("Windows did not answer within " + seconds + " seconds, so this section was skipped. Run the audit again or raise the step time limit in Settings.");
            }
            return work.Result;
        }

        private static void Abandon(Task work)
        {
            work.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static Section AssetSection(AuditOptions options)
        {
            var block = Block.KeyValue()
                .Add("Asset Tag", Fmt.OrNA((options.AssetTag ?? "").Trim()))
                .Add("Assigned To", Fmt.OrNA((options.AssignedTo ?? "").Trim()))
                .Add("Location / Department", Fmt.OrNA((options.Location ?? "").Trim()))
                .Add("Notes", Fmt.OrNA((options.Notes ?? "").Trim()));
            return new Section("asset", "0", "ASSET RECORD").With(block);
        }

        private static Section ErrorSection(AuditStep step, Exception ex)
        {
            Section section;
            if (step.Skipped != null)
            {
                section = step.Skipped();
                foreach (var b in section.Blocks)
                {
                    if (b.Kind == BlockKind.Table && b.Rows.Count > 0)
                        for (int c = 0; c < b.Headers.Length; c++)
                            if (Fmt.Text(b.Rows[0][c]) == SkippedText) b.Rows[0][c] = "ERROR - collection failed and was bypassed: " + ex.Message;
                }
            }
            else
            {
                var number = new Dictionary<string, string>
                {
                    { "device", "1|DEVICE INFORMATION" }, { "os", "2|OPERATING SYSTEM AND ACTIVATION" }, { "cpu", "3|PROCESSOR (CPU) DETAILS" },
                    { "ram", "4|RAM DETAILS" }, { "gpu", "5|GPU DETAILS" }, { "storage", "6|STORAGE / DRIVES" }, { "motherboard", "6b|MOTHERBOARD DETAILS" },
                    { "power", "6c|POWER SUPPLY (PSU) DETAILS" }, { "network", "7|NETWORK ADAPTERS AND MAC ADDRESSES" }, { "monitors", "11|MONITORS / DISPLAYS" }
                };
                string spec;
                if (!number.TryGetValue(step.Id, out spec)) spec = "?|" + step.Label.ToUpperInvariant();
                var parts = spec.Split('|');
                section = new Section(step.Id, parts[0], parts[1]).With(Block.KeyValue().Add("Status", "ERROR - collection failed and was bypassed: " + ex.Message));
            }
            section.State = SectionState.Error;
            section.StateNote = ex.Message;
            return section;
        }

        public static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
