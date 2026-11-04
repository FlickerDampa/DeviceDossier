using System.Reflection;

namespace DeviceDossier.Core
{
    public static class AppInfo
    {
        public const string Name = "DeviceDossier";

        public static string Version
        {
            get
            {
                var assembly = typeof(AppInfo).Assembly;
                var info = (AssemblyInformationalVersionAttribute[])assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (info.Length > 0 && !string.IsNullOrWhiteSpace(info[0].InformationalVersion))
                {
                    string v = info[0].InformationalVersion;
                    int plus = v.IndexOf('+');
                    return plus > 0 ? v.Substring(0, plus) : v;
                }
                return assembly.GetName().Version.ToString(3);
            }
        }
    }
}
