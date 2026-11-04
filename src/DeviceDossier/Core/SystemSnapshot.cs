using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DeviceDossier.Util;

namespace DeviceDossier.Core
{
    public sealed class SystemSnapshot
    {
        public string DeviceName { get; private set; }
        public string Model { get; private set; }
        public string DeviceType { get; private set; }
        public string CurrentUser { get; private set; }
        public string ProfileName { get; private set; }
        public bool IsAdmin { get; private set; }
        public string Cpu { get; private set; }
        public string CpuDetail { get; private set; }
        public List<string> Gpus { get; private set; }
        public string Ram { get; private set; }
        public string RamDetail { get; private set; }
        public string Os { get; private set; }
        public string OsDetail { get; private set; }
        public string Storage { get; private set; }
        public string StorageDetail { get; private set; }
        public string Network { get; private set; }
        public string NetworkDetail { get; private set; }

        public static SystemSnapshot Load()
        {
            var s = new SystemSnapshot { Gpus = new List<string>() };
            s.CurrentUser = Environment.UserDomainName + "\\" + Environment.UserName;
            s.ProfileName = Environment.UserName;
            s.IsAdmin = Elevation.IsAdministrator();

            var cs = Wmi.First("SELECT Name, Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
            s.DeviceName = Wmi.Str(cs, "Name");
            if (string.IsNullOrEmpty(s.DeviceName)) s.DeviceName = Environment.MachineName;
            s.Model = (Wmi.Str(cs, "Manufacturer") + " " + Wmi.Str(cs, "Model")).Trim();
            bool battery = Wmi.TryQuery("SELECT DeviceID FROM Win32_Battery").Count > 0;
            s.DeviceType = battery ? "Laptop" : "Desktop";

            var cpus = Wmi.TryQuery("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            if (cpus.Count > 0)
            {
                var c = cpus[0];
                s.Cpu = Wmi.Str(c, "Name");
                s.CpuDetail = string.Format(CultureInfo.InvariantCulture, "{0} cores / {1} threads · {2:0.00} GHz",
                    cpus.Sum(x => Wmi.Long(x, "NumberOfCores")), cpus.Sum(x => Wmi.Long(x, "NumberOfLogicalProcessors")), Wmi.Long(c, "MaxClockSpeed") / 1000.0);
            }

            foreach (var g in Wmi.TryQuery("SELECT Name FROM Win32_VideoController"))
            {
                var name = Wmi.Str(g, "Name");
                if (name.Length > 0 && !s.Gpus.Contains(name)) s.Gpus.Add(name);
            }

            var sticks = Wmi.TryQuery("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
            double total = sticks.Sum(x => (double)Wmi.Long(x, "Capacity"));
            if (total <= 0) total = Wmi.Long(cs, "TotalPhysicalMemory");
            s.Ram = Fmt.Text(Math.Round(total / 1073741824.0)) + " GB";
            long speed = sticks.Count > 0 ? sticks.Max(x => Wmi.Long(x, "Speed")) : 0;
            s.RamDetail = sticks.Count + (sticks.Count == 1 ? " module" : " modules") + (speed > 0 ? " · " + speed + " MHz" : "");

            var os = Wmi.First("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
            s.Os = Wmi.Str(os, "Caption").Replace("Microsoft ", "");
            s.OsDetail = "Version " + Wmi.Str(os, "Version") + " · " + Wmi.Str(os, "OSArchitecture");

            var disks = Wmi.TryQuery("SELECT Size, InterfaceType FROM Win32_DiskDrive");
            double bytes = disks.Where(d => Wmi.Str(d, "InterfaceType") != "USB").Sum(d => (double)Wmi.Long(d, "Size"));
            int fixedCount = disks.Count(d => Wmi.Str(d, "InterfaceType") != "USB");
            s.Storage = Fmt.Size(bytes / 1073741824.0);
            s.StorageDetail = fixedCount + (fixedCount == 1 ? " drive" : " drives") + (disks.Count > fixedCount ? " + " + (disks.Count - fixedCount) + " USB" : "");

            s.Network = "Not connected";
            s.NetworkDetail = "";
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) continue;
                    s.Network = ip.Address.ToString();
                    s.NetworkDetail = (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet") + " · " + nic.Description;
                    break;
                }
            }
            catch
            {
            }
            return s;
        }
    }
}
