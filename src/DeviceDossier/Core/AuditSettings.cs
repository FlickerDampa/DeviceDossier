using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DeviceDossier.Util;

namespace DeviceDossier.Core
{
    public enum SaveLocationMode { Desktop, UsbDrive, Folder }

    public enum AppTheme { System, Light, Dark }

    [Flags]
    public enum ReportFormats { None = 0, Excel = 1, Html = 2, Xml = 4 }

    public sealed class AuditOptions
    {
        public AuditOptions()
        {
            IncludeMappedDrives = true;
            IncludeSecurity = true;
            IncludePatchHistory = true;
            IncludeSoftware = true;
            IncludeBrowserExtensions = true;
            Formats = ReportFormats.Excel | ReportFormats.Html;
        }

        public string OutputFolder { get; set; }
        public ReportFormats Formats { get; set; }
        public bool IncludeMappedDrives { get; set; }
        public bool IncludeSecurity { get; set; }
        public bool IncludePatchHistory { get; set; }
        public bool IncludeSoftware { get; set; }
        public bool IncludeBrowserExtensions { get; set; }
        public string AssetTag { get; set; }
        public string AssignedTo { get; set; }
        public string Location { get; set; }
        public string Notes { get; set; }

        public bool HasAssetRecord
        {
            get { return !string.IsNullOrWhiteSpace(AssetTag) || !string.IsNullOrWhiteSpace(AssignedTo) || !string.IsNullOrWhiteSpace(Location) || !string.IsNullOrWhiteSpace(Notes); }
        }

        public bool IsFullAudit
        {
            get { return IncludeMappedDrives && IncludeSecurity && IncludePatchHistory && IncludeSoftware && IncludeBrowserExtensions; }
        }
    }

    public sealed class KnownServer
    {
        public KnownServer()
        {
        }

        public KnownServer(string address, string label)
        {
            Address = address;
            Label = label;
        }

        [XmlAttribute] public string Address { get; set; }
        [XmlAttribute] public string Label { get; set; }
    }

    [XmlRoot("DeviceDossierSettings")]
    public sealed class AuditSettings
    {
        public const string FileName = "DeviceDossier.settings.xml";
        private static readonly string[] LegacyFileNames = { "SysAudit.settings.xml", "SysAudit.ini" };

        public AuditSettings()
        {
            SaveLocation = SaveLocationMode.Desktop;
            ReportFolderName = "DeviceDossier Reports";
            MaskProductKey = true;
            StepTimeoutSeconds = 180;
            FileNamePrefix = "IT_Audit";
            ExportExcel = true;
            ExportHtml = true;
            ExportXml = false;
            IncludeMappedDrives = true;
            IncludeSecurity = true;
            IncludePatchHistory = true;
            IncludeSoftware = true;
            IncludeBrowserExtensions = true;
            RequestAdminAtStartup = true;
            ExcludeToolDrive = true;
            PatchTimeoutSeconds = 60;
            Theme = AppTheme.System;
            KnownServers = new List<KnownServer>();
            ExcludedDiskModels = new List<string>();
        }

        public SaveLocationMode SaveLocation { get; set; }
        public string CustomFolder { get; set; }
        public string ReportFolderName { get; set; }
        public string FileNamePrefix { get; set; }
        public bool ExportExcel { get; set; }
        public bool ExportHtml { get; set; }
        public bool ExportXml { get; set; }
        public bool IncludeMappedDrives { get; set; }
        public bool IncludeSecurity { get; set; }
        public bool IncludePatchHistory { get; set; }
        public bool IncludeSoftware { get; set; }
        public bool IncludeBrowserExtensions { get; set; }
        public bool RequestAdminAtStartup { get; set; }
        public bool MaskProductKey { get; set; }
        public bool ExcludeToolDrive { get; set; }
        public int PatchTimeoutSeconds { get; set; }
        public int StepTimeoutSeconds { get; set; }
        public AppTheme Theme { get; set; }

        [XmlArrayItem("Server")] public List<KnownServer> KnownServers { get; set; }
        [XmlArrayItem("Model")] public List<string> ExcludedDiskModels { get; set; }

        [XmlIgnore] public string LoadedFrom { get; private set; }

        [XmlIgnore]
        public ReportFormats DefaultFormats
        {
            get
            {
                var f = ReportFormats.None;
                if (ExportExcel) f |= ReportFormats.Excel;
                if (ExportHtml) f |= ReportFormats.Html;
                if (ExportXml) f |= ReportFormats.Xml;
                return f == ReportFormats.None ? ReportFormats.Excel : f;
            }
        }

        public string KnownServerLabel(string server)
        {
            var hit = KnownServers.FirstOrDefault(k => string.Equals((k.Address ?? "").Trim(), server, StringComparison.OrdinalIgnoreCase));
            if (hit == null) return null;
            return "Yes - " + (string.IsNullOrWhiteSpace(hit.Label) ? hit.Address : hit.Label.Trim());
        }

        public AuditOptions CreateOptions()
        {
            return new AuditOptions
            {
                Formats = DefaultFormats,
                IncludeMappedDrives = IncludeMappedDrives,
                IncludeSecurity = IncludeSecurity,
                IncludePatchHistory = IncludePatchHistory,
                IncludeSoftware = IncludeSoftware,
                IncludeBrowserExtensions = IncludeBrowserExtensions
            };
        }

        public static string PortablePath
        {
            get { return Path.Combine(AppPaths.ExeDirectory, FileName); }
        }

        public static string UserPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeviceDossier", FileName); }
        }

        public static AuditSettings Load()
        {
            var candidates = new List<string> { PortablePath, UserPath };
            foreach (var name in LegacyFileNames)
            {
                candidates.Add(Path.Combine(AppPaths.ExeDirectory, name));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysAudit", name));
            }
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var s = Import(path);
                    s.LoadedFrom = path;
                    return s;
                }
                catch
                {
                }
            }
            return new AuditSettings();
        }

        public bool LoadedFromLegacyFile
        {
            get { return LoadedFrom != null && LegacyFileNames.Any(n => string.Equals(Path.GetFileName(LoadedFrom), n, StringComparison.OrdinalIgnoreCase)); }
        }

        public string Save()
        {
            Normalize();
            string error;
            if (CanWrite(AppPaths.ExeDirectory, out error))
            {
                Write(PortablePath);
                LoadedFrom = PortablePath;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UserPath));
                Write(UserPath);
                LoadedFrom = UserPath;
            }
            return LoadedFrom;
        }

        public static AuditSettings Import(string path)
        {
            var s = path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? ReadIni(path) : Read(path);
            s.Normalize();
            return s;
        }

        public void Export(string path)
        {
            Normalize();
            Write(path);
        }

        public AuditSettings Clone()
        {
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, this);
                ms.Position = 0;
                return (AuditSettings)Serializer.Deserialize(ms);
            }
        }

        public void Normalize()
        {
            if (KnownServers == null) KnownServers = new List<KnownServer>();
            if (ExcludedDiskModels == null) ExcludedDiskModels = new List<string>();
            KnownServers = KnownServers.Where(k => k != null && !string.IsNullOrWhiteSpace(k.Address))
                .Select(k => new KnownServer(k.Address.Trim(), (k.Label ?? "").Trim()))
                .GroupBy(k => k.Address, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            ExcludedDiskModels = ExcludedDiskModels.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (PatchTimeoutSeconds < 10) PatchTimeoutSeconds = 10;
            if (PatchTimeoutSeconds > 600) PatchTimeoutSeconds = 600;
            if (StepTimeoutSeconds < 30) StepTimeoutSeconds = 30;
            if (StepTimeoutSeconds > 1800) StepTimeoutSeconds = 1800;
            if (ReportFolderName == null) ReportFolderName = "";
            if (string.IsNullOrWhiteSpace(FileNamePrefix)) FileNamePrefix = "IT_Audit";
            ReportFolderName = Clean(ReportFolderName);
            FileNamePrefix = Clean(FileNamePrefix);
            if (!ExportExcel && !ExportHtml && !ExportXml) ExportExcel = true;
        }

        private static string Clean(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AuditSettings));
        private static readonly XmlSerializer LegacySerializer = new XmlSerializer(typeof(AuditSettings), new XmlRootAttribute("SysAuditSettings"));

        private static AuditSettings Read(string path)
        {
            var xml = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            string root;
            using (var probe = XmlReader.Create(path, xml))
            {
                probe.MoveToContent();
                root = probe.LocalName;
            }
            var serializer = root == "SysAuditSettings" ? LegacySerializer : Serializer;
            using (var reader = XmlReader.Create(path, xml))
            {
                var s = (AuditSettings)serializer.Deserialize(reader);
                s.Normalize();
                return s;
            }
        }

        private void Write(string path)
        {
            var tmp = path + ".tmp";
            using (var writer = XmlWriter.Create(tmp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                Serializer.Serialize(writer, this);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static bool CanWrite(string folder, out string error)
        {
            error = null;
            try
            {
                var probe = Path.Combine(folder, "._devicedossier_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static AuditSettings ReadIni(string path)
        {
            var s = new AuditSettings();
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }
                int eq = line.IndexOf('=');
                string key = eq >= 0 ? line.Substring(0, eq).Trim() : line;
                string value = eq >= 0 ? line.Substring(eq + 1).Trim() : "";
                int n;
                if (section == "knownservers" && key.Length > 0) s.KnownServers.Add(new KnownServer(key, value));
                else if (section == "excludeddisks" && key.Length > 0) s.ExcludedDiskModels.Add(eq >= 0 && value.Length > 0 ? value : key);
                else if (section == "audit" && key.Equals("PatchTimeoutSeconds", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    s.PatchTimeoutSeconds = n;
            }
            s.Normalize();
            return s;
        }
    }
}
