using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DeviceDossier.Util
{
    public sealed class NvmeController
    {
        public string Serial { get; set; }
        public string Model { get; set; }
        public string Firmware { get; set; }
    }

    public static class NvmeIdentify
    {
        private const uint IoctlStorageQueryProperty = 0x002D1400;
        private const int StorageAdapterProtocolSpecificProperty = 49;
        private const int StorageDeviceProtocolSpecificProperty = 50;
        private const int ProtocolTypeNvme = 3;
        private const int NvmeDataTypeIdentify = 1;
        private const int IdentifyController = 1;
        private const int IdentifyLength = 4096;
        private const int QueryHeader = 8;
        private const int ProtocolDataSize = 40;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint ShareReadWrite = 0x3;
        private const uint OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

        public static NvmeController Read(int diskIndex)
        {
            string path = @"\\.\PhysicalDrive" + diskIndex;
            foreach (uint access in new[] { 0u, GenericRead, GenericRead | GenericWrite })
            {
                using (var handle = CreateFile(path, access, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) continue;
                    foreach (int property in new[] { StorageAdapterProtocolSpecificProperty, StorageDeviceProtocolSpecificProperty })
                    {
                        var result = Query(handle, property);
                        if (result != null) return result;
                    }
                }
            }
            return null;
        }

        private static NvmeController Query(SafeFileHandle handle, int property)
        {
            var buffer = new byte[QueryHeader + ProtocolDataSize + IdentifyLength];
            Write(buffer, 0, property);
            Write(buffer, 4, 0);
            Write(buffer, 8, ProtocolTypeNvme);
            Write(buffer, 12, NvmeDataTypeIdentify);
            Write(buffer, 16, IdentifyController);
            Write(buffer, 20, 0);
            Write(buffer, 24, ProtocolDataSize);
            Write(buffer, 28, IdentifyLength);

            int returned;
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out returned, IntPtr.Zero)) return null;
            if (returned < QueryHeader + ProtocolDataSize) return null;

            int offset = BitConverter.ToInt32(buffer, 8 + 16);
            int length = BitConverter.ToInt32(buffer, 8 + 20);
            int start = 8 + offset;
            if (offset <= 0 || length < 72 || start + 72 > buffer.Length) return null;

            var controller = new NvmeController
            {
                Serial = Ascii(buffer, start + 4, 20),
                Model = Ascii(buffer, start + 24, 40),
                Firmware = Ascii(buffer, start + 64, 8)
            };
            return string.IsNullOrEmpty(controller.Serial) ? null : controller;
        }

        private static void Write(byte[] buffer, int offset, int value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, buffer, offset, 4);
        }

        private static string Ascii(byte[] buffer, int offset, int length)
        {
            var sb = new StringBuilder(length);
            for (int i = offset; i < offset + length && i < buffer.Length; i++)
            {
                byte b = buffer[i];
                if (b == 0) break;
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }
    }
}
