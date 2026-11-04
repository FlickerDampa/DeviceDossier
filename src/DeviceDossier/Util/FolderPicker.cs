using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeviceDossier.Util
{
    public static class FolderPicker
    {
        private const uint FosPickFolders = 0x20;
        private const uint FosForceFileSystem = 0x40;
        private const uint FosNoChangeDir = 0x8;
        private const uint SigdnFileSysPath = 0x80058000;
        private const int Cancelled = unchecked((int)0x800704C7);

        public static string Pick(IntPtr owner, string title, string initialFolder)
        {
            try
            {
                return PickModern(owner, title, initialFolder);
            }
            catch (COMException)
            {
                return PickClassic(title, initialFolder);
            }
            catch (InvalidCastException)
            {
                return PickClassic(title, initialFolder);
            }
        }

        private static string PickModern(IntPtr owner, string title, string initialFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosNoChangeDir);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(initialFolder))
                {
                    string start = initialFolder;
                    while (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = Path.GetDirectoryName(start);
                    if (!string.IsNullOrEmpty(start))
                    {
                        IShellItem item;
                        var iid = typeof(IShellItem).GUID;
                        if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out item) == 0) dialog.SetFolder(item);
                    }
                }
                int hr = dialog.Show(owner);
                if (hr == Cancelled) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                IShellItem result;
                dialog.GetResult(out result);
                string path;
                result.GetDisplayName(SigdnFileSysPath, out path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        private static string PickClassic(string title, string initialFolder)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder)) dlg.SelectedPath = initialFolder;
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw
        {
        }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
