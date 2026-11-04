using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class SoftwareCollector
    {
        private sealed class App
        {
            public string Name, Version, Publisher, InstallDate, Location;
        }

        public static Section Installed(AuditContext ctx)
        {
            var apps = new List<App>();
            Read(apps, RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            Read(apps, RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            Read(apps, RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");

            var unique = apps
                .OrderBy(a => a.Name, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(a => a.Version, StringComparer.OrdinalIgnoreCase)
                .GroupBy(a => a.Name + "\u0001" + (a.Version ?? ""), StringComparer.InvariantCultureIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var table = Block.Table("No.", "Software Name", "Version", "Publisher", "Install Date", "Install Location");
            int n = 1;
            foreach (var a in unique)
                table.AddRow(n++, a.Name, Fmt.OrNA(a.Version), Fmt.OrNA(a.Publisher), Fmt.OrNA(InstallDate(a.InstallDate)), Fmt.OrNA(a.Location));

            return new Section("software", "10", "INSTALLED SOFTWARE - Total: " + unique.Count + " apps").With(table);
        }

        private static string InstallDate(string raw)
        {
            DateTime date;
            if (!string.IsNullOrEmpty(raw) && raw.Length == 8 && DateTime.TryParseExact(raw, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date))
                return date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
            return raw;
        }

        private static void Read(List<App> apps, RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var key = root.OpenSubKey(path))
                {
                    if (key == null) return;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = key.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                var name = Convert.ToString(k.GetValue("DisplayName"));
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                apps.Add(new App
                                {
                                    Name = name,
                                    Version = Convert.ToString(k.GetValue("DisplayVersion")),
                                    Publisher = Convert.ToString(k.GetValue("Publisher")),
                                    InstallDate = Convert.ToString(k.GetValue("InstallDate")),
                                    Location = Convert.ToString(k.GetValue("InstallLocation"))
                                });
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static Section BrowserExtensions(AuditContext ctx)
        {
            var table = Block.Table("Browser", "User Profile", "Extension Name", "Version", "Extension ID");
            var section = new Section("extensions", "10b", "BROWSER EXTENSIONS");
            bool denied = false;

            foreach (var p in ctx.Profiles)
            {
                ctx.Token.ThrowIfCancellationRequested();
                try
                {
                    Chromium(table, Path.Combine(p.Path, @"AppData\Local\Google\Chrome\User Data"), "Chrome", p.Name, false);
                    Chromium(table, Path.Combine(p.Path, @"AppData\Local\Microsoft\Edge\User Data"), "Edge", p.Name, false);
                    Chromium(table, Path.Combine(p.Path, @"AppData\Roaming\Opera Software"), "Opera", p.Name, true);
                    Firefox(table, Path.Combine(p.Path, @"AppData\Roaming\Mozilla\Firefox\Profiles"), p.Name);
                }
                catch (UnauthorizedAccessException)
                {
                    denied = true;
                }
            }
            if (denied && !ctx.IsAdmin) section.Warn("Other users' browser profiles can only be read with administrator rights.");
            if (table.Rows.Count == 0) table.AddRow(Fmt.NA, Fmt.NA, "No browser extensions found", Fmt.NA, Fmt.NA);
            section.Title = "BROWSER EXTENSIONS - Total: " + table.Rows.Count;
            return section.With(table);
        }

        private static void Chromium(Block table, string userData, string browser, string user, bool opera)
        {
            if (!Directory.Exists(userData)) return;
            foreach (var profileDir in Directory.GetDirectories(userData))
            {
                string leaf = Path.GetFileName(profileDir);
                bool match = opera
                    ? leaf.StartsWith("Opera", StringComparison.OrdinalIgnoreCase)
                    : leaf == "Default" || System.Text.RegularExpressions.Regex.IsMatch(leaf, @"^Profile \d+$");
                if (!match) continue;

                string extRoot = Path.Combine(profileDir, "Extensions");
                if (!Directory.Exists(extRoot)) continue;
                foreach (var extDir in Directory.GetDirectories(extRoot))
                {
                    string id = Path.GetFileName(extDir);
                    if (id == "Temp") continue;
                    var versionDir = Directory.GetDirectories(extDir).FirstOrDefault();
                    if (versionDir == null) continue;
                    string manifestPath = Path.Combine(versionDir, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        var manifest = Json.ParseObject(File.ReadAllText(manifestPath));
                        if (manifest == null) continue;
                        string name = Value(manifest, "name");
                        if (name.StartsWith("__MSG_", StringComparison.Ordinal))
                            name = Localize(versionDir, name, Value(manifest, "default_locale")) ?? name;
                        table.AddRow(browser, user, name, Value(manifest, "version"), id);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string Localize(string versionDir, string token, string defaultLocale)
        {
            string key = token.Replace("__MSG_", "").Replace("__", "");
            var locales = new List<string> { "en", "en_US", "en_GB" };
            if (!string.IsNullOrEmpty(defaultLocale)) locales.Add(defaultLocale);
            foreach (var loc in locales)
            {
                string path = Path.Combine(versionDir, "_locales", loc, "messages.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var msgs = Json.ParseObject(File.ReadAllText(path));
                    if (msgs == null) continue;
                    foreach (var kv in msgs)
                    {
                        if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                        var entry = kv.Value as Dictionary<string, object>;
                        var message = entry == null ? null : Value(entry, "message");
                        if (!string.IsNullOrEmpty(message)) return message;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static void Firefox(Block table, string profilesRoot, string user)
        {
            if (!Directory.Exists(profilesRoot)) return;
            foreach (var dir in Directory.GetDirectories(profilesRoot))
            {
                string path = Path.Combine(dir, "extensions.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var data = Json.ParseObject(File.ReadAllText(path));
                    var addons = data == null ? null : (data.ContainsKey("addons") ? data["addons"] as object[] : null);
                    if (addons == null) continue;
                    foreach (var o in addons)
                    {
                        var addon = o as Dictionary<string, object>;
                        if (addon == null) continue;
                        string id = Value(addon, "id");
                        if (id.Length == 0) continue;
                        if (Value(addon, "location") == "app-builtin" || Value(addon, "isBuiltin") == "True") continue;
                        var locale = addon.ContainsKey("defaultLocale") ? addon["defaultLocale"] as Dictionary<string, object> : null;
                        string name = locale != null ? Value(locale, "name") : "";
                        table.AddRow("Firefox", user, name.Length > 0 ? name : id, Value(addon, "version"), id);
                    }
                }
                catch
                {
                }
            }
        }

        private static string Value(Dictionary<string, object> map, string key)
        {
            object v;
            return map.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : "";
        }
    }
}
