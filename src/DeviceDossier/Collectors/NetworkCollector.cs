using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class NetworkCollector
    {
        public static Section Adapters(AuditContext ctx)
        {
            var table = Block.Table("Adapter", "MAC Address", "IP Address", "Subnet", "Gateway", "DHCP", "DNS");
            var physical = new Dictionary<long, bool>();
            foreach (var a in Wmi.TryQuery("SELECT Index, PhysicalAdapter FROM Win32_NetworkAdapter"))
                physical[Wmi.Long(a, "Index")] = Wmi.Get(a, "PhysicalAdapter") is bool && (bool)Wmi.Get(a, "PhysicalAdapter");

            int hidden = 0;
            foreach (var nic in Wmi.TryQuery("SELECT * FROM Win32_NetworkAdapterConfiguration"))
            {
                string mac = Wmi.Str(nic, "MACAddress");
                if (string.IsNullOrEmpty(mac)) continue;
                string ip = Wmi.Str(nic, "IPAddress");
                bool isPhysical;
                if (!physical.TryGetValue(Wmi.Long(nic, "Index"), out isPhysical)) isPhysical = true;
                if (!isPhysical && ip.Length == 0)
                {
                    hidden++;
                    continue;
                }
                bool dhcp = Wmi.Get(nic, "DHCPEnabled") is bool && (bool)Wmi.Get(nic, "DHCPEnabled");
                table.AddRow(Wmi.Str(nic, "Description"), mac, ip, Wmi.Str(nic, "IPSubnet"),
                    Wmi.Str(nic, "DefaultIPGateway"), dhcp ? "Yes" : "No", Wmi.Str(nic, "DNSServerSearchOrder"));
            }
            if (hidden > 0) table.WithCaption(hidden + " virtual adapter(s) without an IP address are not listed (for example WAN Miniport and Wi-Fi Direct).", CaptionStyle.Italic);
            return new Section("network", "7", "NETWORK ADAPTERS AND MAC ADDRESSES").With(table);
        }

        public static void LoadProfiles(AuditContext ctx)
        {
            var systemSids = new[] { "S-1-5-18", "S-1-5-19", "S-1-5-20" };
            var skip = new[] { "DefaultAccount", "WDAGUtilityAccount", "Public", "defaultuser0", "Default" };
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var list = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                {
                    if (list == null) return;
                    foreach (var sid in list.GetSubKeyNames())
                    {
                        if (systemSids.Contains(sid) || sid.Length <= 12) continue;
                        using (var k = list.OpenSubKey(sid))
                        {
                            var img = k == null ? null : Convert.ToString(k.GetValue("ProfileImagePath"));
                            if (string.IsNullOrEmpty(img)) continue;
                            img = Environment.ExpandEnvironmentVariables(img);
                            if (!Directory.Exists(img)) continue;
                            var leaf = Path.GetFileName(img.TrimEnd('\\'));
                            if (skip.Any(s => string.Equals(s, leaf, StringComparison.OrdinalIgnoreCase))) continue;
                            ctx.Profiles.Add(new UserProfile { Sid = sid, Name = leaf, Path = img });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Warn("  Could not read the local user profile list: " + ex.Message);
            }
        }

        public static Section MappedDrives(AuditContext ctx)
        {
            var known = ctx.Settings.KnownServers.Select(k => k.Address).ToList();
            var section = new Section("drives", "8", "MAPPED NETWORK DRIVES (SERVER AUDIT)");
            var table = Block.Table("User Profile", "Drive Letter", "Status", "Server IP", "Matches Known Server List", "Subfolder Path", "Full UNC Path")
                .WithCaption(known.Count > 0 ? "Known servers checked: " + string.Join(", ", known) : "Known servers checked: none configured (add them in Settings)", CaptionStyle.Italic);
            section.With(table);

            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ctx.Profiles)
            {
                try
                {
                    using (var hive = Registry.Users.OpenSubKey(p.Sid))
                    {
                        if (hive == null) continue;
                        loaded.Add(p.Sid);
                        using (var net = hive.OpenSubKey("Network")) AddRows(ctx, table, net, p.Name, "Configured (user currently logged on)");
                    }
                }
                catch
                {
                }
            }

            bool skippedOffline = false;
            foreach (var p in ctx.Profiles)
            {
                if (loaded.Contains(p.Sid)) continue;
                string ntuser = Path.Combine(p.Path, "NTUSER.DAT");
                if (!File.Exists(ntuser)) continue;
                if (!ctx.IsAdmin)
                {
                    skippedOffline = true;
                    continue;
                }
                try
                {
                    using (var hive = OfflineHive.OpenReadOnly(ntuser))
                    using (var net = hive.OpenSubKey("Network"))
                        AddRows(ctx, table, net, p.Name, "Configured (user logged off - read from profile)");
                }
                catch (Exception ex)
                {
                    ctx.Warn("  Could not read the saved profile of " + p.Name + " (" + ex.Message + ")");
                    section.Warn("The saved drive mappings of " + p.Name + " could not be read.");
                }
            }
            if (skippedOffline) section.Warn("Profiles that are not logged on can only be read with administrator rights.");

            AddSessionMappings(ctx, table);

            string currentUser = Environment.UserDomainName + "\\" + Environment.UserName;
            foreach (var d in Wmi.TryQuery("SELECT * FROM Win32_MappedLogicalDisk"))
            {
                string letter = Wmi.Str(d, "Name").TrimEnd(':') + ":";
                string unc = Wmi.Str(d, "ProviderName");
                if (HasMapping(table, letter, unc)) continue;
                AddRow(ctx, table, currentUser + " (active session)", letter, "Connected (active)", unc);
            }

            bool deniedLocations = false;
            foreach (var p in ctx.Profiles)
            {
                string root = Path.Combine(p.Path, @"AppData\Roaming\Microsoft\Windows\Network Shortcuts");
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var entry in Directory.GetFileSystemEntries(root))
                    {
                        string lnk = Directory.Exists(entry) ? Path.Combine(entry, "target.lnk") : entry;
                        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(lnk)) continue;
                        string target = ShellLink.ReadTarget(lnk);
                        if (string.IsNullOrEmpty(target) || !target.StartsWith(@"\\")) continue;
                        AddRow(ctx, table, p.Name, "(none)", "Network location: " + Path.GetFileNameWithoutExtension(entry), target);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    deniedLocations = true;
                }
                catch
                {
                }
            }
            if (deniedLocations && !ctx.IsAdmin) section.Warn("Network locations of other users can only be read with administrator rights.");

            if (table.Rows.Count == 0)
                table.AddRow(Fmt.NA, Fmt.NA, "No mapped network drives found across any user profile", Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA);
            return section;
        }

        private static bool HasMapping(Block table, string letter, string unc)
        {
            int li = table.Column("Drive Letter"), ui = table.Column("Full UNC Path");
            return table.Rows.Any(r => string.Equals(Fmt.Text(r[li]), letter, StringComparison.OrdinalIgnoreCase) && string.Equals(Fmt.Text(r[ui]).TrimEnd('\\'), (unc ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        private static void AddSessionMappings(AuditContext ctx, Block table)
        {
            try
            {
                var users = new Dictionary<string, string>();
                foreach (var lo in Wmi.TryQuery("SELECT * FROM Win32_LoggedOnUser"))
                {
                    var account = Regex.Match(Wmi.Str(lo, "Antecedent"), "Domain=\"([^\"]*)\",Name=\"([^\"]*)\"");
                    var session = Regex.Match(Wmi.Str(lo, "Dependent"), "LogonId=\"(\\d+)\"");
                    if (account.Success && session.Success) users[session.Groups[1].Value] = account.Groups[1].Value + "\\" + account.Groups[2].Value;
                }
                foreach (var link in Wmi.TryQuery("SELECT * FROM Win32_LogonSessionMappedDisk"))
                {
                    var session = Regex.Match(Wmi.Str(link, "Antecedent"), "LogonId=\"(\\d+)\"");
                    string diskPath = Wmi.Str(link, "Dependent");
                    if (diskPath.Length == 0) continue;
                    try
                    {
                        using (var disk = new System.Management.ManagementObject(diskPath))
                        {
                            disk.Get();
                            string letter = Wmi.Str(disk, "Name").TrimEnd(':') + ":";
                            string unc = Wmi.Str(disk, "ProviderName");
                            if (unc.Length == 0 || HasMapping(table, letter, unc)) continue;
                            string user;
                            if (!session.Success || !users.TryGetValue(session.Groups[1].Value, out user)) user = "Logon session " + (session.Success ? session.Groups[1].Value : "?");
                            AddRow(ctx, table, user + " (signed-in session)", letter, "Connected (signed-in user session)", unc);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddRows(AuditContext ctx, Block table, RegistryKey network, string user, string status)
        {
            if (network == null) return;
            foreach (var letter in network.GetSubKeyNames())
            {
                using (var k = network.OpenSubKey(letter))
                {
                    var remote = k == null ? null : Convert.ToString(k.GetValue("RemotePath"));
                    if (string.IsNullOrEmpty(remote)) continue;
                    AddRow(ctx, table, user, letter + ":", status, remote);
                }
            }
        }

        private static void AddRow(AuditContext ctx, Block table, string user, string letter, string status, string remote)
        {
            string server = "", sub = "";
            var m = Regex.Match(remote ?? "", @"^\\\\([^\\]+)\\(.+)$");
            if (m.Success)
            {
                server = m.Groups[1].Value;
                sub = m.Groups[2].Value;
            }
            else
            {
                m = Regex.Match(remote ?? "", @"^\\\\([^\\]+)$");
                if (m.Success) server = m.Groups[1].Value;
            }
            string label = ctx.Settings.KnownServerLabel(server);
            if (label == null) label = server.Length > 0 ? "No - Unrecognized server" : Fmt.NA;
            table.AddRow(user, letter, status, Fmt.OrNA(server), label, Fmt.OrNA(sub), remote);
        }
    }
}
