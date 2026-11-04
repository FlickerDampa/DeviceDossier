using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DeviceDossier.Util
{
    public static class ShellLink
    {
        private const int HasTargetIdList = 0x1;
        private const int HasLinkInfo = 0x2;
        private const int VolumeIdAndLocalBasePath = 0x1;
        private const int NetworkRelativeLinkAndPathSuffix = 0x2;

        public static string ReadTarget(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch { return null; }
            try
            {
                string target = FromLinkInfo(data);
                if (!string.IsNullOrEmpty(target)) return target;
            }
            catch
            {
            }
            return ScanForUnc(data);
        }

        private static string FromLinkInfo(byte[] d)
        {
            if (d.Length < 0x4C || BitConverter.ToInt32(d, 0) != 0x4C) return null;
            int flags = BitConverter.ToInt32(d, 0x14);
            int pos = 0x4C;
            if ((flags & HasTargetIdList) != 0) pos += 2 + BitConverter.ToUInt16(d, pos);
            if ((flags & HasLinkInfo) == 0) return null;

            int info = pos;
            int headerSize = BitConverter.ToInt32(d, info + 4);
            int infoFlags = BitConverter.ToInt32(d, info + 8);
            int localBaseOffset = BitConverter.ToInt32(d, info + 16);
            int networkOffset = BitConverter.ToInt32(d, info + 20);
            int suffixOffset = BitConverter.ToInt32(d, info + 24);
            int suffixUnicode = headerSize >= 0x24 ? BitConverter.ToInt32(d, info + 32) : 0;
            string suffix = suffixUnicode > 0 ? Unicode(d, info + suffixUnicode) : Ansi(d, info + suffixOffset);

            if ((infoFlags & NetworkRelativeLinkAndPathSuffix) != 0)
            {
                int cnr = info + networkOffset;
                int netNameOffset = BitConverter.ToInt32(d, cnr + 8);
                int netUnicode = netNameOffset > 0x14 ? BitConverter.ToInt32(d, cnr + 20) : 0;
                string net = netUnicode > 0 ? Unicode(d, cnr + netUnicode) : Ansi(d, cnr + netNameOffset);
                if (string.IsNullOrEmpty(net)) return null;
                return string.IsNullOrEmpty(suffix) ? net : net.TrimEnd('\\') + "\\" + suffix;
            }
            if ((infoFlags & VolumeIdAndLocalBasePath) != 0)
            {
                int localUnicode = headerSize >= 0x24 ? BitConverter.ToInt32(d, info + 28) : 0;
                string local = localUnicode > 0 ? Unicode(d, info + localUnicode) : Ansi(d, info + localBaseOffset);
                return string.IsNullOrEmpty(suffix) ? local : local + suffix;
            }
            return null;
        }

        private static string Ansi(byte[] d, int offset)
        {
            if (offset <= 0 || offset >= d.Length) return "";
            int end = offset;
            while (end < d.Length && d[end] != 0) end++;
            return Encoding.Default.GetString(d, offset, end - offset);
        }

        private static string Unicode(byte[] d, int offset)
        {
            if (offset <= 0 || offset >= d.Length - 1) return "";
            int end = offset;
            while (end + 1 < d.Length && (d[end] != 0 || d[end + 1] != 0)) end += 2;
            return Encoding.Unicode.GetString(d, offset, end - offset);
        }

        private static string ScanForUnc(byte[] data)
        {
            var pattern = new Regex(@"\\\\[A-Za-z0-9._\-]+\\[^\x00-\x1F""<>|*?]+");
            var m = pattern.Match(Encoding.Unicode.GetString(data));
            if (m.Success) return m.Value.Trim();
            m = pattern.Match(Encoding.Default.GetString(data));
            return m.Success ? m.Value.Trim() : null;
        }
    }
}
