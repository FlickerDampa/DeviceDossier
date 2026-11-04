using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DeviceDossier.Util
{
    public static class OfflineHive
    {
        private const int KeyRead = 0x20019;
        private const int ProcessAppKey = 0x1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegLoadAppKey(string file, out SafeRegistryHandle key, int samDesired, int options, int reserved);

        public static RegistryKey OpenReadOnly(string hiveFile)
        {
            SafeRegistryHandle handle;
            int status = RegLoadAppKey(hiveFile, out handle, KeyRead, ProcessAppKey, 0);
            if (status != 0)
            {
                if (handle != null) handle.Dispose();
                throw new Win32Exception(status);
            }
            return RegistryKey.FromHandle(handle);
        }
    }
}
