using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class SecurityCollector
    {
        [DllImport("kernel32.dll")]
        private static extern bool GetFirmwareType(out int firmwareType);

        private static readonly string[] VolumeStatus =
        {
            "FullyDecrypted", "FullyEncrypted", "EncryptionInProgress", "DecryptionInProgress", "EncryptionPaused", "DecryptionPaused"
        };

        private static readonly string[] ProtectorTypes =
        {
            "Unknown", "Tpm", "ExternalKey", "RecoveryPassword", "TpmPin", "TpmStartupKey", "TpmPinStartupKey",
            "PublicKey", "Password", "TpmNetworkKey", "AdAccountOrGroup"
        };

        public static Section Collect(AuditContext ctx)
        {
            var section = new Section("security", "9", "SECURITY");
            section.With(Run(ctx, section, "EDR / Antivirus", "Installed EDR / Antivirus", Antivirus, () =>
                Block.Table("Product Name", "Details").AddRow("ERROR", "EDR/AV collection failed and was bypassed")));
            section.With(Run(ctx, section, "BitLocker status", "BitLocker Status", BitLocker, () =>
                Block.Table("Drive", "Protection Status", "Volume Status", "Encryption %", "Key Protector(s)").AddRow("ERROR", Fmt.NA, Fmt.NA, Fmt.NA, "BitLocker collection failed and was bypassed")));

            if (ctx.Options.IncludePatchHistory)
            {
                section.With(Run(ctx, section, "Patch / update level (max " + ctx.Settings.PatchTimeoutSeconds + "s)", "Patch & Update Level", PatchLevel, () =>
                    PatchBlock("ERROR - bypassed", Fmt.NA, Fmt.NA, Fmt.NA)));
            }
            else
            {
                section.With(PatchBlock("Skipped (not selected for this audit)", Fmt.NA, Fmt.NA, Fmt.NA));
            }

            section.With(Run(ctx, section, "Local administrator accounts", "Local Administrative Accounts", LocalAdmins, () =>
                Block.Table("Account Name", "Type", "Source").AddRow("ERROR", Fmt.NA, "Local-admins collection failed and was bypassed")));
            ctx.Ok("Security posture collection complete");
            return section;
        }

        private static Block Run(AuditContext ctx, Section section, string label, string caption, Func<AuditContext, Section, Block> work, Func<Block> fallback)
        {
            ctx.Info("  Security: " + label);
            try
            {
                return work(ctx, section);
            }
            catch (Exception ex)
            {
                ctx.Error("  Security: " + label + " FAILED - bypassed. " + ex.Message);
                section.State = SectionState.Error;
                return fallback().WithCaption(caption, CaptionStyle.Bold);
            }
        }

        public static Section Baseline(AuditContext ctx)
        {
            var section = new Section("baseline", "12", "SECURITY BASELINE");
            var block = Block.KeyValue();
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                long secureBoot = RegLong(hklm, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", -1);
                block.Add("Firmware Type", Firmware(hklm, secureBoot));

                block.Add("Secure Boot", secureBoot == 1 ? "On" : secureBoot == 0 ? "Off" : "Not supported (legacy BIOS or unavailable)");

                block.Add("TPM", Tpm(ctx, section));

                foreach (var profile in new[] { new[] { "Domain", "DomainProfile" }, new[] { "Private", "StandardProfile" }, new[] { "Public", "PublicProfile" } })
                {
                    long policy = RegLong(hklm, @"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + profile[1], "EnableFirewall", -1);
                    long local = RegLong(hklm, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profile[1], "EnableFirewall", -1);
                    long value = policy >= 0 ? policy : local;
                    block.Add("Firewall - " + profile[0] + " Profile", value == 1 ? "On" + (policy >= 0 ? " (set by policy)" : "") : value == 0 ? "Off" + (policy >= 0 ? " (set by policy)" : "") : "Unknown");
                }

                long lua = RegLong(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1);
                block.Add("User Account Control", lua == 1 ? "On" : "Off");

                long rdp = RegLong(hklm, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1);
                block.Add("Remote Desktop", rdp == 0 ? "Enabled" : "Disabled");

                block.Add("SMBv1 Protocol", Smb1());
                block.Add("Guest Account", Guest());

                var mp = Wmi.TryQuery(@"root\Microsoft\Windows\Defender", "SELECT AntivirusSignatureVersion, AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus").FirstOrDefault();
                string sig = mp == null ? Fmt.NA : Fmt.OrNA(Wmi.Str(mp, "AntivirusSignatureVersion"));
                string sigDate = mp == null ? "" : Fmt.CimDate(Wmi.Get(mp, "AntivirusSignatureLastUpdated"), "dd-MM-yyyy");
                block.Add("Defender Signatures", sigDate.Length > 0 ? sig + " (updated " + sigDate + ")" : sig);

                bool reboot = KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                    || KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                block.Add("Pending Restart", reboot ? "Yes - a restart is waiting to finish updates" : "No");
            }
            return section.With(block);
        }

        private static string Tpm(AuditContext ctx, Section section)
        {
            try
            {
                var tpm = Wmi.Query(@"root\CIMV2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm").FirstOrDefault();
                if (tpm == null) return "Not detected";
                string spec = Wmi.Str(tpm, "SpecVersion").Split(',')[0].Trim();
                bool enabled = Wmi.Get(tpm, "IsEnabled_InitialValue") is bool && (bool)Wmi.Get(tpm, "IsEnabled_InitialValue");
                bool activated = Wmi.Get(tpm, "IsActivated_InitialValue") is bool && (bool)Wmi.Get(tpm, "IsActivated_InitialValue");
                string maker = Wmi.Str(tpm, "ManufacturerIdTxt");
                return "Present - TPM " + Fmt.OrNA(spec) + ", " + (enabled ? "enabled" : "disabled") + ", " + (activated ? "activated" : "not activated") + (maker.Length > 0 ? " (" + maker + " " + Wmi.Str(tpm, "ManufacturerVersion") + ")" : "");
            }
            catch (Exception ex)
            {
                if (Wmi.IsAccessDenied(ex) || !ctx.IsAdmin)
                {
                    section.Warn("TPM status needs administrator rights.");
                    return Fmt.NeedsAdmin;
                }
                return "Not detected";
            }
        }

        private static string Smb1()
        {
            var feature = Wmi.TryQuery("SELECT InstallState FROM Win32_OptionalFeature WHERE Name='SMB1Protocol'").FirstOrDefault();
            if (feature == null) return "Not installed";
            long state = Wmi.Long(feature, "InstallState");
            return state == 1 ? "Enabled (legacy, insecure)" : state == 2 ? "Disabled" : "Not installed";
        }

        private static string Firmware(RegistryKey hklm, long secureBoot)
        {
            try
            {
                int type;
                if (GetFirmwareType(out type))
                {
                    if (type == 2) return "UEFI";
                    if (type == 1) return "Legacy BIOS";
                }
            }
            catch
            {
            }
            long pe = RegLong(hklm, @"SYSTEM\CurrentControlSet\Control", "PEFirmwareType", -1);
            if (pe == 2 || secureBoot >= 0) return "UEFI";
            if (pe == 1) return "Legacy BIOS";
            return "Unknown";
        }

        private const int AccountDisabledFlag = 0x2;

        private static string Guest()
        {
            var guest = Wmi.TryQuery("SELECT Name, Disabled, SID FROM Win32_UserAccount WHERE LocalAccount=True").FirstOrDefault(u => Wmi.Str(u, "SID").EndsWith("-501"));
            if (guest == null) return Fmt.NA;
            string name = Wmi.Str(guest, "Name");
            try
            {
                using (var entry = new DirectoryEntry("WinNT://" + Environment.MachineName + "/" + name + ",user"))
                {
                    var flags = entry.Properties["UserFlags"].Value;
                    if (flags != null) return (Convert.ToInt32(flags, CultureInfo.InvariantCulture) & AccountDisabledFlag) != 0 ? "Disabled" : "Enabled - the built-in Guest account can sign in";
                }
            }
            catch
            {
            }
            object disabled = Wmi.Get(guest, "Disabled");
            if (disabled is bool) return (bool)disabled ? "Disabled" : "Enabled - the built-in Guest account can sign in";
            return Fmt.NA;
        }

        private static string OnOff(object value)
        {
            if (value is bool) return (bool)value ? "On" : "Off";
            return Fmt.OrNA(Fmt.Str(value));
        }

        private static bool IsServer()
        {
            var os = Wmi.First("SELECT ProductType FROM Win32_OperatingSystem");
            return os != null && Wmi.Long(os, "ProductType") > 1;
        }

        private static long RegLong(RegistryKey root, string path, string name, long fallback)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    var v = k == null ? null : k.GetValue(name);
                    return v == null ? fallback : Convert.ToInt64(v, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return fallback;
            }
        }

        private static bool KeyExists(RegistryKey root, string path)
        {
            try
            {
                using (var k = root.OpenSubKey(path)) return k != null;
            }
            catch
            {
                return false;
            }
        }

        private static Block Antivirus(AuditContext ctx, Section section)
        {
            var table = Block.Table("Product Name", "Details").WithCaption("Installed EDR / Antivirus", CaptionStyle.Bold);
            var mp = Wmi.TryQuery(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus").FirstOrDefault();
            string defender = null;
            if (mp != null)
            {
                string mode = Wmi.Str(mp, "AMRunningMode");
                defender = "Real-time protection: " + OnOff(Wmi.Get(mp, "RealTimeProtectionEnabled")) + " | Antimalware service: " + OnOff(Wmi.Get(mp, "AMServiceEnabled")) + " | Mode: " + Fmt.OrNA(mode);
            }

            var securityCenter = Wmi.TryQuery(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
            foreach (var av in securityCenter)
            {
                string name = Wmi.Str(av, "displayName");
                bool isDefender = name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0;
                table.AddRow(name, "Registered with Windows Security Center" + (isDefender && defender != null ? " | " + defender : ""));
                if (isDefender) defender = null;
            }
            if (defender != null)
                table.AddRow("Microsoft Defender Antivirus", (securityCenter.Count == 0 && IsServer() ? "Windows Server (no Security Center) | " : "") + defender);
            if (table.Rows.Count == 0) table.AddRow(Fmt.NA, IsServer() ? "No antivirus detected. Windows Server has no Security Center, so only Microsoft Defender can be detected." : "Could not detect any registered AV/EDR product");
            return table;
        }

        private static Block BitLocker(AuditContext ctx, Section section)
        {
            var table = Block.Table("Drive", "Protection Status", "Volume Status", "Encryption %", "Key Protector(s)").WithCaption("BitLocker Status", CaptionStyle.Bold);
            List<ManagementObject> volumes;
            try
            {
                volumes = Wmi.Query(@"root\CIMV2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            }
            catch (Exception ex)
            {
                string why = Wmi.IsAccessDenied(ex) || !ctx.IsAdmin
                    ? "Could not query BitLocker - requires administrator"
                    : "Could not query BitLocker (module may be unavailable on this Windows edition)";
                if (!ctx.IsAdmin) section.Warn("BitLocker status needs administrator rights.");
                return table.AddRow(Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, why);
            }

            foreach (var v in volumes)
            {
                string drive = Wmi.Str(v, "DriveLetter");
                if (string.IsNullOrEmpty(drive)) drive = Wmi.Str(v, "DeviceID");

                long protection = Wmi.Long(v, "ProtectionStatus");
                string protectionText = protection == 1 ? "On" : protection == 0 ? "Off" : "Unknown";

                string status = "Unknown", percent = "";
                try
                {
                    var conv = v.InvokeMethod("GetConversionStatus", null, null);
                    long cs = Wmi.Long(conv, "ConversionStatus");
                    if (cs >= 0 && cs < VolumeStatus.Length) status = VolumeStatus[cs];
                    percent = Wmi.Long(conv, "EncryptionPercentage") + "%";
                }
                catch
                {
                }

                var protectors = new List<string>();
                try
                {
                    var inParams = v.GetMethodParameters("GetKeyProtectors");
                    inParams["KeyProtectorType"] = 0u;
                    var outParams = v.InvokeMethod("GetKeyProtectors", inParams, null);
                    var ids = Wmi.Get(outParams, "VolumeKeyProtectorID") as string[];
                    if (ids != null)
                    {
                        foreach (var id in ids)
                        {
                            var tIn = v.GetMethodParameters("GetKeyProtectorType");
                            tIn["VolumeKeyProtectorID"] = id;
                            var tOut = v.InvokeMethod("GetKeyProtectorType", tIn, null);
                            long t = Wmi.Long(tOut, "KeyProtectorType");
                            protectors.Add(t >= 0 && t < ProtectorTypes.Length ? ProtectorTypes[t] : "Unknown");
                        }
                    }
                }
                catch
                {
                }
                table.AddRow(drive, protectionText, status, percent, string.Join(", ", protectors));
            }
            if (table.Rows.Count == 0) table.AddRow(Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, "No BitLocker-capable volumes reported");
            return table;
        }

        private static Block PatchBlock(string security, string feature, string quality, string suite)
        {
            return Block.KeyValue().WithCaption("Patch & Update Level", CaptionStyle.Bold)
                .Add("Latest Security Update", security)
                .Add("Latest Feature Update", feature)
                .Add("Latest Windows (Quality) Update", quality)
                .Add("Latest General/Suite Update", suite);
        }

        private sealed class UpdateEntry
        {
            public string Title;
            public DateTime Date;
            public string Categories;
        }

        private static Block PatchLevel(AuditContext ctx, Section section)
        {
            int timeout = ctx.Settings.PatchTimeoutSeconds;
            List<UpdateEntry> history;
            Exception error;
            bool finished = Util.Timeout.RunSta(ReadUpdateHistory, timeout * 1000, out history, out error);
            if (!finished)
            {
                ctx.Error("  Security: Patch-level check TIMED OUT after " + timeout + "s - bypassed (Windows Update service not responding)");
                section.Warn("Windows Update history timed out.");
                return PatchBlock("TIMED OUT after " + timeout + "s - bypassed (Windows Update service not responding on this machine)", Fmt.NA, Fmt.NA, Fmt.NA);
            }
            if (history == null || history.Count == 0) history = ReadHotFixes();
            if (history.Count == 0)
            {
                ctx.Error("  Security: Patch-level check returned no data - bypassed");
                return PatchBlock("N/A - no data returned", Fmt.NA, Fmt.NA, Fmt.NA);
            }
            var usable = history.Where(h => !IsDefinition(h) && !IsStoreApp(h)).ToList();
            return PatchBlock(
                MostRecent(usable.Where(IsSecurity), "none found in update history"),
                MostRecent(usable.Where(IsFeature), "none found in update history"),
                MostRecent(usable.Where(IsQuality), "no cumulative update or Insider build found in update history"),
                MostRecent(usable, "none found in update history"));
        }

        private static bool HasCategory(UpdateEntry h, string name)
        {
            return (h.Categories ?? "").Split(',').Any(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TitleMatches(UpdateEntry h, string pattern)
        {
            return Regex.IsMatch(h.Title ?? "", pattern, RegexOptions.IgnoreCase);
        }

        private static bool IsDefinition(UpdateEntry h)
        {
            return HasCategory(h, "Definition Updates") || TitleMatches(h, @"Security Intelligence Update|Definition Update|Antimalware platform");
        }

        private static bool IsInsiderBuild(UpdateEntry h)
        {
            return TitleMatches(h, @"Windows 1[01].*Insider Preview");
        }

        private static bool IsStoreApp(UpdateEntry h)
        {
            return TitleMatches(h, @"^[0-9A-Z]{12}-");
        }

        private static bool IsOsCumulative(UpdateEntry h)
        {
            if (TitleMatches(h, @"\.NET")) return false;
            return TitleMatches(h, @"Cumulative Update (Preview )?for (Windows|Microsoft server operating system)")
                || TitleMatches(h, @"\(KB\d+\)\s*\(\d{5}\.\d+\)");
        }

        private static bool IsPreview(UpdateEntry h)
        {
            return TitleMatches(h, @"Cumulative Update Preview|Preview Update|Non-security");
        }

        private static bool IsFeature(UpdateEntry h)
        {
            return HasCategory(h, "Upgrades") || TitleMatches(h, @"Feature update to Windows|Upgrade to Windows") || IsInsiderBuild(h);
        }

        private static bool IsQuality(UpdateEntry h)
        {
            return IsOsCumulative(h) || IsInsiderBuild(h);
        }

        private static bool IsSecurity(UpdateEntry h)
        {
            if (TitleMatches(h, @"Security platform")) return false;
            return HasCategory(h, "Security Updates") || TitleMatches(h, @"\bSecurity Update\b") || (IsOsCumulative(h) && !IsPreview(h));
        }

        private static string MostRecent(IEnumerable<UpdateEntry> entries, string none)
        {
            var top = entries.OrderByDescending(h => h.Date).FirstOrDefault();
            return top == null ? "N/A - " + none : top.Title + " - Installed: " + top.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        }

        private static List<UpdateEntry> ReadUpdateHistory()
        {
            var list = new List<UpdateEntry>();
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type == null) return list;
            object session = Activator.CreateInstance(type);
            object searcher = Com.Call(session, "CreateUpdateSearcher");
            int count = Convert.ToInt32(Com.Call(searcher, "GetTotalHistoryCount"));
            if (count <= 0) return list;
            object entries = Com.Call(searcher, "QueryHistory", 0, count);
            int n = Convert.ToInt32(Com.Get(entries, "Count"));
            for (int i = 0; i < n; i++)
            {
                try
                {
                    object h = Com.Get(entries, "Item", i);
                    if (Convert.ToInt32(Com.Get(h, "ResultCode")) != 2) continue;
                    var cats = new List<string>();
                    try
                    {
                        object c = Com.Get(h, "Categories");
                        int cn = Convert.ToInt32(Com.Get(c, "Count"));
                        for (int j = 0; j < cn; j++) cats.Add(Convert.ToString(Com.Get(Com.Get(c, "Item", j), "Name")));
                    }
                    catch
                    {
                    }
                    var date = (DateTime)Com.Get(h, "Date");
                    if (date.Kind != DateTimeKind.Local) date = DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
                    list.Add(new UpdateEntry { Title = Convert.ToString(Com.Get(h, "Title")), Date = date, Categories = string.Join(", ", cats) });
                }
                catch
                {
                }
            }
            return list;
        }

        private static List<UpdateEntry> ReadHotFixes()
        {
            var list = new List<UpdateEntry>();
            foreach (var qfe in Wmi.TryQuery("SELECT * FROM Win32_QuickFixEngineering"))
            {
                DateTime date;
                string raw = Wmi.Str(qfe, "InstalledOn");
                if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) &&
                    !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)) continue;
                string desc = Wmi.Str(qfe, "Description");
                list.Add(new UpdateEntry { Title = desc + " " + Wmi.Str(qfe, "HotFixID"), Date = date, Categories = desc });
            }
            return list;
        }

        private static Block LocalAdmins(AuditContext ctx, Section section)
        {
            var table = Block.Table("Account Name", "Type", "Source").WithCaption("Local Administrative Accounts", CaptionStyle.Bold);
            string machine = Environment.MachineName;
            string groupName = "Administrators";
            try
            {
                var account = (NTAccount)new SecurityIdentifier("S-1-5-32-544").Translate(typeof(NTAccount));
                groupName = account.Value.Split('\\').Last();
            }
            catch
            {
            }

            try
            {
                using (var group = new DirectoryEntry("WinNT://" + machine + "/" + groupName + ",group"))
                {
                    var members = group.Invoke("Members") as IEnumerable;
                    if (members != null)
                    {
                        foreach (object m in members)
                        {
                            using (var entry = new DirectoryEntry(m))
                            {
                                var parts = (entry.Path ?? "").Replace("WinNT://", "").Split('/');
                                string name = parts.Length > 0 ? parts[parts.Length - 1] : entry.Name;
                                string authority = parts.Length > 1 ? parts[parts.Length - 2] : "";
                                string source;
                                if (authority.Equals(machine, StringComparison.OrdinalIgnoreCase)) source = "Local";
                                else if (authority.Equals("AzureAD", StringComparison.OrdinalIgnoreCase)) source = "AzureAD";
                                else if (authority.Length == 0 || name.StartsWith("S-1-")) source = "Unknown";
                                else source = "ActiveDirectory";
                                string type = string.IsNullOrEmpty(entry.SchemaClassName) ? "Unknown" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(entry.SchemaClassName.ToLowerInvariant());
                                table.AddRow(authority.Length > 0 ? authority + "\\" + name : name, type, source);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Warn("  Security: could not enumerate local Administrators group - " + ex.Message);
            }
            if (table.Rows.Count == 0) table.AddRow(Fmt.NA, Fmt.NA, "Could not enumerate local Administrators group");
            return table;
        }
    }

    internal static class Com
    {
        public static object Get(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, args);
        }

        public static object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
        }
    }
}
