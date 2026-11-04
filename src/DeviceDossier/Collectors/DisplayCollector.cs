using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class DisplayCollector
    {
        private static readonly Dictionary<string, string> Vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ACR", "Acer" }, { "AOC", "AOC" }, { "APP", "Apple" }, { "AUO", "AU Optronics" }, { "AUS", "ASUS" }, { "BNQ", "BenQ" },
            { "BOE", "BOE" }, { "CMN", "Innolux" }, { "DEL", "Dell" }, { "ENC", "EIZO" }, { "GBT", "Gigabyte" }, { "GSM", "LG" },
            { "HPN", "HP" }, { "HWP", "HP" }, { "IVM", "Iiyama" }, { "IVO", "InfoVision" }, { "LEN", "Lenovo" }, { "LGD", "LG Display" },
            { "MSI", "MSI" }, { "MEI", "Panasonic" }, { "NEC", "NEC" }, { "PHL", "Philips" }, { "SAM", "Samsung" }, { "SDC", "Samsung Display" },
            { "SEC", "Samsung" }, { "SHP", "Sharp" }, { "SNY", "Sony" }, { "VSC", "ViewSonic" }, { "XMI", "Xiaomi" }, { "HKC", "HKC" }
        };

        private static string Connection(long code)
        {
            switch (code)
            {
                case 0: return "VGA";
                case 1: return "S-Video";
                case 2: return "Composite";
                case 3: return "Component";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 6: return "LVDS (internal)";
                case 9: return "SDI";
                case 10: return "DisplayPort";
                case 11: return "DisplayPort (internal)";
                case 12: return "UDI";
                case 13: return "UDI (internal)";
                case 15: return "Miracast (wireless)";
                case 0x80000000: return "Internal panel";
                default: return "Other";
            }
        }

        public static Section Monitors(AuditContext ctx)
        {
            var table = Block.Table("Monitor", "Manufacturer", "Product Code", "Serial Number", "Manufactured", "Size", "Connection", "Active");
            var ids = Wmi.TryQuery(@"root\wmi", "SELECT * FROM WmiMonitorID");
            var sizes = Index(Wmi.TryQuery(@"root\wmi", "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"));
            var links = Index(Wmi.TryQuery(@"root\wmi", "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams"));

            foreach (var m in ids)
            {
                string instance = Wmi.Str(m, "InstanceName");
                string vendorCode = Chars(Wmi.Get(m, "ManufacturerName"));
                string vendor;
                string maker = Vendors.TryGetValue(vendorCode, out vendor) ? vendorCode + " (" + vendor + ")" : Fmt.OrNA(vendorCode);
                string name = Chars(Wmi.Get(m, "UserFriendlyName"));
                if (name.Length == 0) name = vendor != null ? vendor + " display" : "Display";

                long year = Wmi.Long(m, "YearOfManufacture"), week = Wmi.Long(m, "WeekOfManufacture");
                string made = year > 0 ? (week > 0 && week <= 53 ? "Week " + week + ", " + year : year.ToString(CultureInfo.InvariantCulture)) : Fmt.NA;

                string size = Fmt.NA;
                ManagementObject p;
                if (sizes.TryGetValue(instance, out p))
                {
                    double w = Wmi.Long(p, "MaxHorizontalImageSize"), h = Wmi.Long(p, "MaxVerticalImageSize");
                    if (w > 0 && h > 0) size = (Math.Sqrt(w * w + h * h) / 2.54).ToString("0.#", CultureInfo.InvariantCulture) + "\" (" + w + " x " + h + " cm)";
                }
                string connection = Fmt.NA;
                if (links.TryGetValue(instance, out p)) connection = Connection(Convert.ToInt64(Wmi.Get(p, "VideoOutputTechnology") ?? -1L) & 0xFFFFFFFF);

                bool active = !(Wmi.Get(m, "Active") is bool) || (bool)Wmi.Get(m, "Active");
                table.AddRow(name, maker, Fmt.OrNA(Chars(Wmi.Get(m, "ProductCodeID"))), Fmt.OrNA(Chars(Wmi.Get(m, "SerialNumberID"))), made, size, connection, active ? "Yes" : "No");
            }
            if (table.Rows.Count == 0) table.AddRow(Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA, "No monitors reported by Windows");
            return new Section("monitors", "11", "MONITORS / DISPLAYS").With(table);
        }

        private static Dictionary<string, ManagementObject> Index(List<ManagementObject> list)
        {
            var map = new Dictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in list)
            {
                string key = Wmi.Str(o, "InstanceName");
                if (key.Length > 0 && !map.ContainsKey(key)) map[key] = o;
            }
            return map;
        }

        private static string Chars(object value)
        {
            var arr = value as Array;
            if (arr == null) return Fmt.Str(value);
            var chars = new List<char>();
            foreach (var v in arr)
            {
                int c = Convert.ToInt32(v, CultureInfo.InvariantCulture);
                if (c == 0) break;
                chars.Add((char)c);
            }
            return new string(chars.ToArray()).Trim();
        }
    }
}
