using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DeviceDossier.Core;

namespace DeviceDossier.Util
{
    public sealed class UsbDrive
    {
        public string Root { get; set; }
        public string Label { get; set; }
        public double FreeGb { get; set; }
        public double TotalGb { get; set; }
        public bool IsToolDrive { get; set; }

        public string Display
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Label) ? "USB drive" : Label;
                return Root.TrimEnd('\\') + "  " + name + "  (" + Fmt.Size(FreeGb) + " free of " + Fmt.Size(TotalGb) + ")" + (IsToolDrive ? "  - DeviceDossier runs from here" : "");
            }
        }

        public override string ToString()
        {
            return Display;
        }
    }

    public static class UsbDrives
    {
        public static List<UsbDrive> List()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in SafeDrives())
                if (d.DriveType == DriveType.Removable) roots.Add(d.RootDirectory.FullName);

            try
            {
                foreach (var disk in Wmi.TryQuery("SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                {
                    string diskId = Wmi.Str(disk, "DeviceID").Replace("\\", "\\\\");
                    foreach (var part in Wmi.TryQuery("ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + diskId + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                    {
                        string partId = Wmi.Str(part, "DeviceID");
                        foreach (var logical in Wmi.TryQuery("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + partId + "'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                            roots.Add(Wmi.Str(logical, "DeviceID") + "\\");
                    }
                }
            }
            catch
            {
            }

            string toolRoot = AppPaths.ExeDriveRoot ?? "";
            var list = new List<UsbDrive>();
            foreach (var root in roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var info = new DriveInfo(root);
                    if (!info.IsReady) continue;
                    list.Add(new UsbDrive
                    {
                        Root = info.RootDirectory.FullName,
                        Label = info.VolumeLabel,
                        FreeGb = info.AvailableFreeSpace / 1073741824.0,
                        TotalGb = info.TotalSize / 1073741824.0,
                        IsToolDrive = string.Equals(info.RootDirectory.FullName, toolRoot, StringComparison.OrdinalIgnoreCase)
                    });
                }
                catch
                {
                }
            }
            return list.OrderByDescending(d => d.IsToolDrive).ToList();
        }

        public static bool IsUsbRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            return List().Any(d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<DriveInfo> SafeDrives()
        {
            try { return DriveInfo.GetDrives(); }
            catch { return new DriveInfo[0]; }
        }
    }
}
