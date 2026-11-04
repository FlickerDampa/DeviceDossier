using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class OsCollector
    {
        private static readonly Dictionary<int, string> LicenseStatus = new Dictionary<int, string>
        {
            { 0, "Unlicensed" }, { 1, "Licensed" }, { 2, "OOB Grace" }, { 3, "OOT Grace" },
            { 4, "Non-Genuine Grace" }, { 5, "Notification" }, { 6, "Extended Grace" }
        };

        public static Section Collect(AuditContext ctx)
        {
            var os = Wmi.First("SELECT * FROM Win32_OperatingSystem");
            var products = Wmi.TryQuery("SELECT LicenseStatus, ProductKeyChannel FROM SoftwareLicensingProduct WHERE Name LIKE 'Windows%' AND PartialProductKey IS NOT NULL");
            var slp = products.Count > 0 ? products[0] : null;

            string activation = "Unknown";
            if (slp != null && !LicenseStatus.TryGetValue((int)Wmi.Long(slp, "LicenseStatus"), out activation)) activation = "Unknown";

            var block = Block.KeyValue()
                .Add("OS Name", Wmi.Str(os, "Caption"))
                .Add("Version", Wmi.Str(os, "Version"))
                .Add("Display Version", Fmt.OrNA(CurrentVersion("DisplayVersion") ?? CurrentVersion("ReleaseId")))
                .Add("Build Number", Wmi.Str(os, "BuildNumber") + Ubr())
                .Add("Architecture", Wmi.Str(os, "OSArchitecture"))
                .Add("Install Date", Fmt.CimDate(Wmi.Get(os, "InstallDate"), "dd-MM-yyyy"))
                .Add("Last Boot", Fmt.CimDate(Wmi.Get(os, "LastBootUpTime"), "dd-MM-yyyy HH:mm"))
                .Add("Registered User", Fmt.OrNA(Wmi.Str(os, "RegisteredUser")))
                .Add("Activation Status", activation)
                .Add("Windows Key", Mask(GetWindowsKey(ctx), ctx.Settings.MaskProductKey))
                .Add("Product Key Ch.", slp != null ? Wmi.Str(slp, "ProductKeyChannel") : Fmt.NA);
            return new Section("os", "2", "OPERATING SYSTEM AND ACTIVATION").With(block);
        }

        private static string CurrentVersion(string name)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var value = key == null ? null : key.GetValue(name);
                    string text = value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Ubr()
        {
            string ubr = CurrentVersion("UBR");
            return string.IsNullOrEmpty(ubr) ? "" : "." + ubr;
        }

        private static string Mask(string key, bool mask)
        {
            if (!mask || key == null || !Regex.IsMatch(key, @"^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$")) return key;
            return "XXXXX-XXXXX-XXXXX-XXXXX-" + key.Substring(24);
        }

        public static string GetWindowsKey(AuditContext ctx)
        {
            try
            {
                var svc = Wmi.First("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
                var oa3 = Wmi.Str(svc, "OA3xOriginalProductKey");
                if (!string.IsNullOrWhiteSpace(oa3)) return oa3;
            }
            catch
            {
            }

            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var id = key == null ? null : key.GetValue("DigitalProductId") as byte[];
                    if (id != null && id.Length >= 67) return DecodeProductKey(id);
                }
            }
            catch
            {
            }
            return "Unable to retrieve - run as Administrator";
        }

        public static string DecodeProductKey(byte[] digitalProductId)
        {
            const int offset = 52;
            const string chars = "BCDFGHJKMPQRTVWXY2346789";
            var key = (byte[])digitalProductId.Clone();
            int isWin8 = (key[66] / 6) & 1;
            key[66] = (byte)((key[66] & 0xF7) | ((isWin8 & 2) * 4));

            var raw = new StringBuilder();
            int last = 0;
            for (int i = 24; i >= 0; i--)
            {
                int cur = 0;
                for (int j = 14; j >= 0; j--)
                {
                    cur = cur * 256 + key[j + offset];
                    key[j + offset] = (byte)(cur / 24);
                    cur %= 24;
                }
                raw.Insert(0, chars[cur]);
                last = cur;
            }

            string decoded = raw.ToString();
            if (isWin8 == 1)
            {
                string head = decoded.Substring(1, last);
                decoded = head + "N" + decoded.Substring(1 + last);
            }

            var sb = new StringBuilder();
            for (int i = 0; i < 25; i++)
            {
                if (i > 0 && i % 5 == 0) sb.Append('-');
                sb.Append(decoded[i]);
            }
            return sb.ToString();
        }
    }
}
