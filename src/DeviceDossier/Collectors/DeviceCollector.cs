using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class DeviceCollector
    {
        private static readonly Dictionary<int, string> ChassisTypes = new Dictionary<int, string>
        {
            { 1, "Discharging" }, { 2, "On AC power, not charging" }, { 3, "Desktop" }, { 4, "Low Profile Desktop" },
            { 6, "Mini Tower" }, { 7, "Tower" }, { 8, "Portable" }, { 9, "Laptop" }, { 10, "Notebook" },
            { 11, "Hand Held" }, { 14, "Sub Notebook" }, { 30, "Tablet" }, { 31, "Convertible" }, { 32, "Detachable" }
        };

        private static readonly Dictionary<int, string> BatteryStatus = new Dictionary<int, string>
        {
            { 1, "Discharging" }, { 2, "On AC power, not charging" }, { 3, "Fully Charged" }, { 4, "Low" }, { 5, "Critical" }, { 6, "Charging" },
            { 7, "Charging - High" }, { 8, "Charging - Low" }, { 9, "Charging - Critical" }, { 10, "Undefined" }, { 11, "Partially Charged" }
        };

        public static Section Device(AuditContext ctx, AuditReport report)
        {
            var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem");
            var bios = Wmi.First("SELECT * FROM Win32_BIOS");
            var enc = Wmi.First("SELECT ChassisTypes, SMBIOSAssetTag FROM Win32_SystemEnclosure");
            var product = Wmi.First("SELECT UUID, Version FROM Win32_ComputerSystemProduct");

            int chassis = 2;
            var types = Wmi.Get(enc, "ChassisTypes") as Array;
            if (types != null && types.Length > 0) chassis = Convert.ToInt32(types.GetValue(0));
            string deviceType;
            if (!ChassisTypes.TryGetValue(chassis, out deviceType)) deviceType = "Unknown (" + chassis + ")";

            string name = Wmi.Str(cs, "Name");
            if (string.IsNullOrEmpty(name)) name = Environment.MachineName;
            report.DeviceName = name;

            bool partOfDomain = Wmi.Get(cs, "PartOfDomain") is bool && (bool)Wmi.Get(cs, "PartOfDomain");
            string domain = partOfDomain ? Wmi.Str(cs, "Domain") : "Workgroup: " + Wmi.Str(cs, "Workgroup");

            var block = Block.KeyValue()
                .Add("Device Type", deviceType)
                .Add("Device Name", name)
                .Add("Manufacturer", Wmi.Str(cs, "Manufacturer"))
                .Add("Model", Wmi.Str(cs, "Model"))
                .Add("Model Name", Fmt.OrNA(ModelName(cs, product)))
                .Add("Domain / Workgroup", domain)
                .Add("BIOS Serial Number", Wmi.Str(bios, "SerialNumber"))
                .Add("BIOS Version", Wmi.Str(bios, "SMBIOSBIOSVersion"))
                .Add("BIOS Release Date", Fmt.CimDate(Wmi.Get(bios, "ReleaseDate"), "dd-MM-yyyy"))
                .Add("System UUID", Fmt.OrNA(CleanUuid(Wmi.Str(product, "UUID"))))
                .Add("Asset Tag", Fmt.OrNA(CleanTag(Wmi.Str(enc, "SMBIOSAssetTag"))));
            return new Section("device", "1", "DEVICE INFORMATION").With(block);
        }

        private static string ModelName(System.Management.ManagementObject cs, System.Management.ManagementObject product)
        {
            string family = Wmi.Str(cs, "SystemFamily");
            string version = Wmi.Str(product, "Version");
            string model = Wmi.Str(cs, "Model");
            if (Wmi.Str(cs, "Manufacturer").IndexOf("LENOVO", StringComparison.OrdinalIgnoreCase) >= 0 && CleanTag(version).Length > 0) return version;
            if (CleanTag(family).Length > 0 && !string.Equals(family, model, StringComparison.OrdinalIgnoreCase)) return family;
            return "";
        }

        private static string CleanUuid(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return "";
            string compact = uuid.Replace("-", "");
            if (compact.Trim('0').Length == 0 || compact.Trim('F', 'f').Length == 0) return "";
            return uuid.Trim();
        }

        private static string CleanTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string v = value.Trim();
            string[] placeholders = { "Default string", "To be filled by O.E.M.", "No Asset Tag", "No Asset Information", "Not Specified", "None", "System Product Name", "System Version", "0" };
            return placeholders.Any(p => string.Equals(p, v, StringComparison.OrdinalIgnoreCase)) ? "" : v;
        }

        public static Section Motherboard(AuditContext ctx)
        {
            var mb = Wmi.First("SELECT * FROM Win32_BaseBoard");
            var block = Block.KeyValue()
                .Add("Manufacturer", Wmi.Str(mb, "Manufacturer"))
                .Add("Model / Product", Wmi.Str(mb, "Product"))
                .Add("Serial Number", Wmi.Str(mb, "SerialNumber"))
                .Add("Version", Wmi.Str(mb, "Version"))
                .Add("Status", Wmi.Str(mb, "Status"));
            return new Section("motherboard", "6b", "MOTHERBOARD DETAILS").With(block);
        }

        public static Section Power(AuditContext ctx, AuditReport report)
        {
            var batteries = Wmi.TryQuery("SELECT * FROM Win32_Battery");
            report.IsLaptop = batteries.Count > 0;

            if (report.IsLaptop)
            {
                var staticData = Wmi.TryQuery(@"root\wmi", "SELECT * FROM BatteryStaticData");
                var fullCharge = Wmi.TryQuery(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity");
                var table = Block.Table("Battery Name", "Device ID", "Health (%)", "Design Capacity (mWh)", "Full Charge Capacity (mWh)", "Current Charge (%)", "Status", "Design Voltage (mV)");
                for (int i = 0; i < batteries.Count; i++)
                {
                    var bat = batteries[i];
                    long design = i < staticData.Count ? Wmi.Long(staticData[i], "DesignedCapacity") : 0;
                    long full = i < fullCharge.Count ? Wmi.Long(fullCharge[i], "FullChargedCapacity") : 0;
                    string health = design > 0 && full > 0
                        ? Math.Round(full * 100.0 / design, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"
                        : Fmt.NA;
                    string status;
                    if (!BatteryStatus.TryGetValue((int)Wmi.Long(bat, "BatteryStatus"), out status)) status = "Unknown";
                    table.AddRow(
                        Wmi.Str(bat, "Name"),
                        Wmi.Str(bat, "DeviceID"),
                        health,
                        design > 0 ? (object)design : Fmt.NA,
                        full > 0 ? (object)full : Fmt.NA,
                        Wmi.Get(bat, "EstimatedChargeRemaining") != null ? (object)Wmi.Long(bat, "EstimatedChargeRemaining") : "",
                        status,
                        Wmi.Get(bat, "DesignVoltage") != null ? (object)Wmi.Long(bat, "DesignVoltage") : "");
                }
                if (staticData.Count == 0 && !ctx.IsAdmin)
                {
                    var s = new Section("power", "6c", "BATTERY DETAILS").With(table);
                    s.Warn("Battery capacity figures may need administrator rights.");
                    return s;
                }
                return new Section("power", "6c", "BATTERY DETAILS").With(table);
            }

            var psuTable = Block.Table("Name", "Model", "Serial Number", "Power Rating (W)", "Status");
            var psus = Wmi.TryQuery("SELECT * FROM Win32_PowerSupply");
            if (psus.Count > 0)
            {
                foreach (var p in psus)
                {
                    long watts = Wmi.Long(p, "TotalOutputPower");
                    psuTable.AddRow(Wmi.Str(p, "Name"), Wmi.Str(p, "Model"), Wmi.Str(p, "SerialNumber"), watts > 0 ? (object)watts : Fmt.NA, Wmi.Str(p, "Status"));
                }
            }
            else
            {
                psuTable.AddRow(Fmt.NA, Fmt.NA, Fmt.NA, Fmt.NA,
                    "PSU details are not exposed via Windows WMI on standard desktop hardware - check the label on the physical power supply unit for wattage/model/serial");
            }
            return new Section("power", "6c", "POWER SUPPLY (PSU) DETAILS").With(psuTable);
        }
    }
}
