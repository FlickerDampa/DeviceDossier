using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeviceDossier.Core
{
    public sealed class ReportTab
    {
        public ReportTab(string header, params string[] sections)
        {
            Header = header;
            Sections = sections;
        }

        public string Header { get; private set; }
        public string[] Sections { get; private set; }
    }

    public sealed class SummaryTile
    {
        public SummaryTile(string tag, string label, string value, string detail)
        {
            Tag = tag;
            Label = label;
            Value = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
            Detail = detail ?? "";
        }

        public string Tag { get; private set; }
        public string Label { get; private set; }
        public string Value { get; private set; }
        public string Detail { get; private set; }
    }

    public static class ReportLayout
    {
        public static readonly ReportTab[] Tabs =
        {
            new ReportTab("System", "asset", "device", "motherboard"),
            new ReportTab("Windows", "os"),
            new ReportTab("CPU", "cpu"),
            new ReportTab("Memory", "ram"),
            new ReportTab("Graphics", "gpu"),
            new ReportTab("Displays", "monitors"),
            new ReportTab("Storage", "storage"),
            new ReportTab("Power", "power"),
            new ReportTab("Network", "network"),
            new ReportTab("Mapped drives", "drives"),
            new ReportTab("Security", "security", "baseline"),
            new ReportTab("Software", "software"),
            new ReportTab("Extensions", "extensions")
        };

        public static readonly HashSet<string> ListSections = new HashSet<string> { "software", "extensions", "drives", "network" };

        public static int Severity(SectionState s)
        {
            switch (s)
            {
                case SectionState.Error: return 3;
                case SectionState.Warning: return 2;
                case SectionState.Skipped: return 1;
                default: return 0;
            }
        }

        public static SectionState Worst(IEnumerable<Section> sections)
        {
            var list = sections.ToList();
            return list.Count == 0 ? SectionState.Ok : list.Select(s => s.State).OrderByDescending(Severity).First();
        }

        public static string Pretty(string title)
        {
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            string head = dash > 0 ? title.Substring(0, dash) : title;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(head.ToLowerInvariant())
                .Replace("(Cpu)", "(CPU)").Replace("Ram ", "RAM ").Replace("Gpu ", "GPU ").Replace("Mac ", "MAC ").Replace("(Psu)", "(PSU)");
        }

        public static string StateText(Section s)
        {
            switch (s.State)
            {
                case SectionState.Error: return "Part of this section could not be collected" + (string.IsNullOrEmpty(s.StateNote) ? "." : ": " + s.StateNote);
                case SectionState.Skipped: return "Skipped - this section was not selected for this audit.";
                case SectionState.Warning: return s.StateNote ?? "Some values may be incomplete.";
                default: return "";
            }
        }

        public static List<SummaryTile> Summary(AuditReport report)
        {
            Func<string, Block> first = id => report[id] == null ? null : report[id].First;
            var tiles = new List<SummaryTile>();

            var asset = first("asset");
            if (asset != null)
                tiles.Add(new SummaryTile("TAG", "Asset", Fmt.OrNA(Val(asset, "Asset Tag")), string.Join(" · ", new[] { Val(asset, "Assigned To"), Val(asset, "Location / Department") }.Where(v => v.Length > 0 && v != Fmt.NA))));

            var dev = first("device");
            string modelName = Val(dev, "Model Name");
            string model = modelName.Length > 0 && modelName != Fmt.NA ? modelName : Val(dev, "Model");
            tiles.Add(new SummaryTile("PC", "Device", Val(dev, "Manufacturer") + " " + model, Val(dev, "Device Type") + " · S/N " + Val(dev, "BIOS Serial Number")));

            var os = first("os");
            tiles.Add(new SummaryTile("OS", "Windows", Val(os, "OS Name").Replace("Microsoft ", ""), (Val(os, "Display Version").Length > 0 && Val(os, "Display Version") != Fmt.NA ? Val(os, "Display Version") + " · " : "") + Val(os, "Activation Status") + " · " + Val(os, "Product Key Ch.") + " · build " + Val(os, "Build Number")));

            var cpu = first("cpu");
            tiles.Add(new SummaryTile("CPU", "Processor", Cell(cpu, 0, "CPU Model"), Cell(cpu, 0, "Cores") + " cores / " + Cell(cpu, 0, "Logical Processors") + " threads"));

            var ram = report["ram"];
            var ramBlock = ram == null ? null : ram.First;
            string ramTotal = ram == null ? "" : ram.Title.Replace("RAM DETAILS - Total: ", "").Split(new[] { " - " }, StringSplitOptions.None)[0];
            tiles.Add(new SummaryTile("RAM", "Memory", ramTotal, (ramBlock == null ? 0 : ramBlock.Rows.Count) + " module(s) · " + Cell(ramBlock, 0, "Memory Type") + " " + Cell(ramBlock, 0, "Speed (MHz)") + " MHz"));

            var gpu = first("gpu");
            double vram;
            string gpuDetail = double.TryParse(Cell(gpu, 0, "VRAM"), NumberStyles.Float, CultureInfo.InvariantCulture, out vram) ? Fmt.Size(vram) + " · " + Cell(gpu, 0, "Resolution") : Cell(gpu, 0, "Resolution");
            tiles.Add(new SummaryTile("GPU", "Graphics", Cell(gpu, 0, "GPU Name"), gpu != null && gpu.Rows.Count > 1 ? "+ " + string.Join(", ", gpu.Rows.Skip(1).Select(r => Fmt.Text(r[0]))) : gpuDetail));

            var mon = first("monitors");
            if (mon != null)
            {
                int monitors = mon.Rows.Count(r => Fmt.Text(r[0]) != Fmt.NA);
                tiles.Add(new SummaryTile("MON", "Displays", monitors + " monitor(s)", string.Join(", ", mon.Rows.Where(r => Fmt.Text(r[0]) != Fmt.NA).Select(r => Fmt.Text(r[0])))));
            }

            var disk = first("storage");
            double totalGb = disk == null ? 0 : disk.Rows.Sum(r => Fmt.IsNumber(r[2]) ? Fmt.ToDouble(r[2]) : 0);
            tiles.Add(new SummaryTile("SSD", "Storage", Fmt.Size(totalGb), (disk == null ? 0 : disk.Rows.Count) + " drive(s) · health " + Cell(disk, 0, "Health (%)")));

            var net = first("network");
            var ips = net == null ? new List<string>() : net.Rows.Select(r => Fmt.Text(r[2]).Split('/')[0].Trim()).Where(ip => ip.Length > 0).ToList();
            tiles.Add(new SummaryTile("NET", "Network", ips.Count > 0 ? ips[0] : "No IPv4 address", (net == null ? 0 : net.Rows.Count) + " adapter(s)"));

            var sec = report["security"];
            string av = "", bitlocker = "";
            if (sec != null && sec.Blocks.Count >= 2)
            {
                av = string.Join(", ", sec.Blocks[0].Rows.Select(r => Fmt.Text(r[0])).Where(x => x != Fmt.NA).Distinct());
                int on = sec.Blocks[1].Rows.Count(r => Fmt.Text(r[1]) == "On");
                bitlocker = "BitLocker on " + on + " of " + sec.Blocks[1].Rows.Count + " volume(s)";
            }
            tiles.Add(new SummaryTile("SEC", "Antivirus", av.Length > 0 ? av : "Not collected", bitlocker));

            var baseline = first("baseline");
            if (baseline != null) tiles.Add(new SummaryTile("TPM", "Platform security", "Secure Boot " + Fmt.OrNA(Val(baseline, "Secure Boot")), "TPM: " + Fmt.OrNA(Val(baseline, "TPM"))));

            int admins = sec != null && sec.Blocks.Count >= 4 ? sec.Blocks[3].Rows.Count : 0;
            tiles.Add(new SummaryTile("ADM", "Local administrators", admins + " account(s)", sec != null && sec.Blocks.Count >= 4 ? string.Join(", ", sec.Blocks[3].Rows.Select(r => Fmt.Text(r[0]))) : ""));

            tiles.Add(new SummaryTile("APP", "Installed software", CountRows(report["software"], "") + " apps", "Desktop and per-user programs"));
            tiles.Add(new SummaryTile("EXT", "Browser extensions", CountRows(report["extensions"], "No browser extensions found") + " extensions", "Chrome, Edge, Opera, Firefox"));
            tiles.Add(new SummaryTile("MAP", "Mapped drives", CountRows(report["drives"], "No mapped network drives found across any user profile") + " mapping(s)", "All user profiles and sessions"));
            return tiles;
        }

        public static string LatestSecurityUpdate(AuditReport report)
        {
            var sec = report["security"];
            return sec != null && sec.Blocks.Count >= 3 ? sec.Blocks[2].Get("Latest Security Update") : null;
        }

        private static int CountRows(Section s, string emptyText)
        {
            if (s == null || s.First == null || s.State == SectionState.Skipped) return 0;
            var b = s.First;
            return b.Rows.Count(r => !r.Any(v => Fmt.Text(v) == emptyText && emptyText.Length > 0) && !r.Any(v => Fmt.Text(v).StartsWith("ERROR")));
        }

        public static string Val(Block b, string name)
        {
            return b == null ? "" : (b.Get(name) ?? "");
        }

        public static string Cell(Block b, int row, string header)
        {
            if (b == null || b.Rows.Count <= row) return "";
            int c = b.Column(header);
            return c < 0 ? "" : Fmt.Text(b.Rows[row][c]);
        }
    }
}
