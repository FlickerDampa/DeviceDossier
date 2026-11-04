using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Util;

namespace DeviceDossier.Collectors
{
    public static class HardwareCollector
    {
        private static readonly Dictionary<int, string> MemoryTypes = new Dictionary<int, string>
        {
            { 1, "Other" }, { 2, "DRAM" }, { 3, "Sync DRAM" }, { 4, "Cache DRAM" },
            { 20, "DDR" }, { 21, "DDR2" }, { 22, "DDR2 FB-DIMM" }, { 24, "DDR3" }, { 26, "DDR4" }, { 34, "DDR5" }
        };

        private static readonly Dictionary<int, string> SmbiosMemoryTypes = new Dictionary<int, string>
        {
            { 18, "DDR" }, { 19, "DDR2" }, { 20, "DDR2 FB-DIMM" }, { 24, "DDR3" }, { 26, "DDR4" }, { 27, "LPDDR" },
            { 28, "LPDDR2" }, { 29, "LPDDR3" }, { 30, "LPDDR4" }, { 34, "DDR5" }, { 35, "LPDDR5" }
        };

        public static Section Cpu(AuditContext ctx)
        {
            var table = Block.Table("CPU Model", "Manufacturer", "Cores", "Logical Processors", "Max Speed (MHz)", "Current Speed (MHz)", "CPU ID / Serial", "Socket", "Architecture", "L2 Cache (KB)", "L3 Cache (KB)");
            foreach (var cpu in Wmi.TryQuery("SELECT * FROM Win32_Processor"))
            {
                string arch;
                switch ((int)Wmi.Long(cpu, "Architecture"))
                {
                    case 0: arch = "x86"; break;
                    case 9: arch = "x64"; break;
                    case 5: arch = "ARM"; break;
                    case 12: arch = "ARM64"; break;
                    default: arch = "Other"; break;
                }
                table.AddRow(
                    Wmi.Str(cpu, "Name"),
                    Wmi.Str(cpu, "Manufacturer"),
                    Wmi.Long(cpu, "NumberOfCores"),
                    Wmi.Long(cpu, "NumberOfLogicalProcessors"),
                    Wmi.Long(cpu, "MaxClockSpeed"),
                    Wmi.Long(cpu, "CurrentClockSpeed"),
                    Wmi.Str(cpu, "ProcessorId"),
                    Wmi.Str(cpu, "SocketDesignation"),
                    arch,
                    Wmi.Long(cpu, "L2CacheSize"),
                    Wmi.Long(cpu, "L3CacheSize"));
            }
            return new Section("cpu", "3", "PROCESSOR (CPU) DETAILS").With(table);
        }

        public static Section Memory(AuditContext ctx)
        {
            var sticks = Wmi.TryQuery("SELECT * FROM Win32_PhysicalMemory");
            double totalBytes = sticks.Sum(s => (double)Wmi.Long(s, "Capacity"));
            var table = Block.Table("Stick No.", "Capacity (GB)", "Manufacturer", "Part Number", "Serial Number", "Speed (MHz)", "Memory Type", "Slot / Location", "Bank", "Form Factor");
            int n = 1;
            foreach (var ram in sticks)
            {
                string type;
                if (!MemoryTypes.TryGetValue((int)Wmi.Long(ram, "MemoryType"), out type))
                    if (!SmbiosMemoryTypes.TryGetValue((int)Wmi.Long(ram, "SMBIOSMemoryType"), out type)) type = "Unknown";
                string form;
                switch ((int)Wmi.Long(ram, "FormFactor"))
                {
                    case 8: form = "DIMM"; break;
                    case 12: form = "SO-DIMM"; break;
                    case 13: form = "TSOP"; break;
                    default: form = "Other"; break;
                }
                table.AddRow(n++, Fmt.Gb(Wmi.Long(ram, "Capacity")), Wmi.Str(ram, "Manufacturer"), Wmi.Str(ram, "PartNumber"), Wmi.Str(ram, "SerialNumber"),
                    Wmi.Long(ram, "Speed"), type, Wmi.Str(ram, "DeviceLocator"), Wmi.Str(ram, "BankLabel"), form);
            }
            var title = string.Format(CultureInfo.InvariantCulture, "RAM DETAILS - Total: {0} GB - Sticks: {1}", Fmt.Text(Fmt.Gb(totalBytes)), sticks.Count);
            return new Section("ram", "4", title).With(table);
        }

        private sealed class NvidiaInfo
        {
            public string Uuid;
            public string Vbios;
        }

        public static Section Gpu(AuditContext ctx)
        {
            var nvidia = ReadNvidiaSmi(ctx);
            var registryVram = ReadRegistryVram();

            var table = Block.Table("GPU Name", "VRAM", "Driver Version", "Driver Date", "Video Proc", "Resolution", "Refresh Rate", "Device ID (DEV)", "Vendor ID (VEN)", "GPU-UUID", "VBIOS Version");
            foreach (var gpu in Wmi.TryQuery("SELECT * FROM Win32_VideoController"))
            {
                string name = Wmi.Str(gpu, "Name");
                string pnp = Wmi.Str(gpu, "PNPDeviceID");

                object vram = "N/A - not reported by the display driver";
                var reg = MatchRegistry(registryVram, name, pnp);
                long regBytes = reg == null ? 0 : reg.MemoryBytes;
                long adapterRam = Wmi.Long(gpu, "AdapterRAM");
                if (regBytes > 0) vram = Fmt.Gb(regBytes);
                else if (adapterRam > 0) vram = Fmt.Gb(adapterRam);

                string ven = Fmt.NA, dev = Fmt.NA;
                var m = Regex.Match(pnp, "VEN_([0-9A-Fa-f]{4})");
                if (m.Success) ven = m.Groups[1].Value;
                m = Regex.Match(pnp, "DEV_([0-9A-Fa-f]{4})");
                if (m.Success) dev = m.Groups[1].Value;

                string uuid = null, vbios = null;
                foreach (var kv in nvidia)
                {
                    if (name.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 || kv.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        uuid = kv.Value.Uuid;
                        vbios = kv.Value.Vbios;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(vbios) && reg != null && !string.IsNullOrEmpty(reg.Bios)) vbios = reg.Bios;
                if (string.IsNullOrEmpty(uuid)) uuid = "N/A - GPU-UUID is an NVIDIA-specific identifier (via nvidia-smi); not available for this GPU/vendor";
                if (string.IsNullOrEmpty(vbios)) vbios = "N/A - not reported by this GPU's driver";

                table.AddRow(name, vram, Wmi.Str(gpu, "DriverVersion"), Fmt.CimDate(Wmi.Get(gpu, "DriverDate"), "dd-MM-yyyy"), Wmi.Str(gpu, "VideoProcessor"),
                    Wmi.Str(gpu, "CurrentHorizontalResolution") + " x " + Wmi.Str(gpu, "CurrentVerticalResolution"),
                    Wmi.Str(gpu, "CurrentRefreshRate") + " Hz", dev, ven, uuid, vbios);
            }
            return new Section("gpu", "5", "GPU DETAILS").With(table);
        }

        private static Dictionary<string, NvidiaInfo> ReadNvidiaSmi(AuditContext ctx)
        {
            var result = new Dictionary<string, NvidiaInfo>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe")
            };
            string exe = candidates.FirstOrDefault(File.Exists);
            if (exe == null) return result;

            ctx.Info("  GPU: reading NVIDIA details via nvidia-smi");
            var run = ProcessRunner.Run(exe, "--query-gpu=name,uuid,vbios_version --format=csv,noheader", 15000);
            foreach (var line in (run.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = Regex.Split(line, @",\s*");
                if (parts.Length >= 3) result[parts[0].Trim()] = new NvidiaInfo { Uuid = parts[1].Trim(), Vbios = parts[2].Trim() };
            }
            return result;
        }

        private sealed class RegistryGpu
        {
            public string Description;
            public string MatchingDeviceId;
            public long MemoryBytes;
            public string Bios;
        }

        private static List<RegistryGpu> ReadRegistryVram()
        {
            var list = new List<RegistryGpu>();
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var cls = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return list;
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        try
                        {
                            using (var k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                long bytes = 0;
                                var qw = k.GetValue("HardwareInformation.qwMemorySize");
                                if (qw is long) bytes = (long)qw;
                                else if (qw is byte[] && ((byte[])qw).Length >= 8) bytes = BitConverter.ToInt64((byte[])qw, 0);
                                if (bytes <= 0)
                                {
                                    var dw = k.GetValue("HardwareInformation.MemorySize");
                                    if (dw is int) bytes = (uint)(int)dw;
                                    else if (dw is byte[] && ((byte[])dw).Length >= 4) bytes = BitConverter.ToUInt32((byte[])dw, 0);
                                }
                                list.Add(new RegistryGpu
                                {
                                    Description = Convert.ToString(k.GetValue("DriverDesc")),
                                    MatchingDeviceId = Convert.ToString(k.GetValue("MatchingDeviceId")),
                                    MemoryBytes = bytes,
                                    Bios = RegistryText(k.GetValue("HardwareInformation.BiosString"))
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
            return list;
        }

        private static string RegistryText(object value)
        {
            string text = null;
            if (value is string) text = (string)value;
            else if (value is string[]) text = string.Join(" ", (string[])value);
            else if (value is byte[]) text = System.Text.Encoding.Unicode.GetString((byte[])value);
            if (text == null) return null;
            text = text.Replace("\0", " ").Trim('\0', ' ', '\r', '\n');
            text = Regex.Replace(text, @"\s+", " ");
            return text.Length == 0 ? null : text;
        }

        private static RegistryGpu MatchRegistry(List<RegistryGpu> entries, string name, string pnp)
        {
            RegistryGpu byName = null;
            foreach (var e in entries)
            {
                bool idMatch = !string.IsNullOrEmpty(e.MatchingDeviceId) && pnp.IndexOf(e.MatchingDeviceId, StringComparison.OrdinalIgnoreCase) >= 0;
                if (idMatch) return e;
                if (byName == null && !string.IsNullOrEmpty(e.Description) && string.Equals(e.Description.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) byName = e;
            }
            return byName;
        }

        public static Section Storage(AuditContext ctx)
        {
            int toolDisk = ToolDiskIndex();
            var physical = ReadPhysicalDisks(ctx);
            var table = Block.Table("Drive Model", "Serial Number", "Size (GB)", "Interface", "Media Type", "Partitions", "Firmware", "Health (%)");
            var disks = Wmi.TryQuery("SELECT * FROM Win32_DiskDrive").OrderBy(d => Wmi.Long(d, "Index")).ToList();
            foreach (var disk in disks)
            {
                string model = Wmi.Str(disk, "Model");
                int index = (int)Wmi.Long(disk, "Index");
                if (ctx.Settings.ExcludedDiskModels.Any(x => model.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (ctx.Settings.ExcludeToolDrive && index == toolDisk)
                {
                    ctx.Info("  Storage: skipped " + model + " (the drive DeviceDossier is running from)");
                    continue;
                }
                PhysicalDisk pd;
                physical.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out pd);
                string health = pd != null && pd.Health != null ? pd.Health : ctx.IsAdmin ? "N/A - SMART wear data not exposed for this drive" : Fmt.NeedsAdmin;
                string serial = pd != null && pd.Serial.Length > 0 ? pd.Serial : CleanSerial(Wmi.Str(disk, "SerialNumber"));
                string bus = pd != null && pd.Bus != null ? pd.Bus : Wmi.Str(disk, "InterfaceType");
                if (bus == "NVMe" && (serial.Length == 0 || LooksLikeEui(serial)))
                {
                    try
                    {
                        var nvme = NvmeIdentify.Read(index);
                        if (nvme != null) serial = nvme.Serial;
                    }
                    catch
                    {
                    }
                }
                string media = pd != null && pd.Media != null ? pd.Media : Wmi.Str(disk, "MediaType");

                table.AddRow(model, Fmt.OrNA(serial), Fmt.Gb(Wmi.Long(disk, "Size")), bus,
                    media, Wmi.Long(disk, "Partitions"), Wmi.Str(disk, "FirmwareRevision"), health);
            }
            return new Section("storage", "6", "STORAGE / DRIVES").With(table);
        }

        private sealed class PhysicalDisk
        {
            public string Serial = "";
            public string Bus;
            public string Media;
            public string Health;
        }

        private static readonly Dictionary<int, string> BusTypes = new Dictionary<int, string>
        {
            { 1, "SCSI" }, { 2, "ATAPI" }, { 3, "ATA" }, { 4, "IEEE 1394" }, { 5, "SSA" }, { 6, "Fibre Channel" }, { 7, "USB" }, { 8, "RAID" },
            { 9, "iSCSI" }, { 10, "SAS" }, { 11, "SATA" }, { 12, "SD" }, { 13, "MMC" }, { 15, "Virtual" }, { 16, "Storage Spaces" }, { 17, "NVMe" }, { 18, "SCM" }, { 19, "UFS" }
        };

        private static Dictionary<string, PhysicalDisk> ReadPhysicalDisks(AuditContext ctx)
        {
            var map = new Dictionary<string, PhysicalDisk>();
            const string scope = @"root\Microsoft\Windows\Storage";
            try
            {
                foreach (var pd in Wmi.Query(scope, "SELECT * FROM MSFT_PhysicalDisk"))
                {
                    var info = new PhysicalDisk { Serial = CleanSerial(Wmi.Str(pd, "SerialNumber")) };
                    string bus;
                    if (BusTypes.TryGetValue((int)Wmi.Long(pd, "BusType"), out bus)) info.Bus = bus;
                    long media = Wmi.Long(pd, "MediaType");
                    info.Media = media == 3 ? "HDD" : media == 4 ? "SSD" : media == 5 ? "SCM" : null;
                    try
                    {
                        foreach (ManagementObject rc in pd.GetRelated("MSFT_StorageReliabilityCounter"))
                        {
                            var wear = Wmi.Get(rc, "Wear");
                            if (wear != null) info.Health = (100 - Convert.ToInt32(wear, CultureInfo.InvariantCulture)) + "% (SSD wear-based)";
                        }
                    }
                    catch
                    {
                    }
                    if (info.Health == null)
                    {
                        long status = Wmi.Long(pd, "HealthStatus");
                        if (status == 0) info.Health = media == 3 ? "Healthy (drive reports no wear data)" : null;
                        else if (status == 1) info.Health = "Warning - reported by the drive";
                        else if (status == 2) info.Health = "Unhealthy - reported by the drive";
                    }
                    map[Wmi.Str(pd, "DeviceId")] = info;
                }
            }
            catch (Exception ex)
            {
                ctx.Warn("  Storage: Windows storage details unavailable - " + ex.Message);
            }
            return map;
        }

        private static bool LooksLikeEui(string serial)
        {
            return Regex.IsMatch(serial, @"^([0-9A-Fa-f]{4}_){3,}[0-9A-Fa-f]{4}$");
        }

        private static string CleanSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return "";
            return serial.Trim().TrimEnd('.').Trim();
        }

        private static int ToolDiskIndex()
        {
            try
            {
                if (!AppPaths.RunningFromUsb) return -1;
                string letter = AppPaths.ExeDriveRoot.TrimEnd('\\');
                foreach (var part in Wmi.TryQuery("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + letter + "'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                {
                    string partId = Wmi.Str(part, "DeviceID").Replace("\\", "\\\\").Replace("'", "\\'");
                    foreach (var disk in Wmi.TryQuery("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + partId + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        return (int)Wmi.Long(disk, "Index");
                }
            }
            catch
            {
            }
            return -1;
        }
    }
}
