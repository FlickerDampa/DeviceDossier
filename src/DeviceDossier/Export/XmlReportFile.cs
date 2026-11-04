using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DeviceDossier.Core;

namespace DeviceDossier.Export
{
    public static class XmlReportFile
    {
        public const int SchemaVersion = 1;

        public static void Save(AuditReport report, string path)
        {
            var root = new XElement("DeviceDossierReport",
                new XAttribute("schema", SchemaVersion),
                new XAttribute("tool", (report.ToolName ?? AppInfo.Name) + " " + (report.ToolVersion ?? "")),
                new XAttribute("generated", report.Generated.ToString("s", CultureInfo.InvariantCulture)),
                new XAttribute("device", report.DeviceName ?? ""),
                new XAttribute("runAs", report.RunAsUser ?? ""),
                new XAttribute("administrator", report.RanAsAdmin ? "true" : "false"),
                new XAttribute("laptop", report.IsLaptop ? "true" : "false"));

            foreach (var s in report.Sections)
            {
                var sec = new XElement("Section",
                    new XAttribute("id", s.Id ?? ""),
                    new XAttribute("number", s.Number ?? ""),
                    new XAttribute("title", s.Title ?? ""),
                    new XAttribute("state", s.State.ToString()));
                if (!string.IsNullOrEmpty(s.StateNote)) sec.Add(new XAttribute("note", s.StateNote));

                foreach (var b in s.Blocks)
                {
                    XElement block;
                    if (b.Kind == BlockKind.KeyValue)
                    {
                        block = new XElement("Fields");
                        foreach (var f in b.Fields) block.Add(Value(new XElement("Field", new XAttribute("name", f.Name)), f.Value));
                    }
                    else
                    {
                        block = new XElement("Table", new XElement("Columns", b.Headers.Select(h => new XElement("Column", Clean(h)))));
                        foreach (var r in b.Rows)
                        {
                            var row = new XElement("Row");
                            for (int c = 0; c < b.Headers.Length; c++) row.Add(Value(new XElement("Cell", new XAttribute("column", b.Headers[c])), r[c]));
                            block.Add(row);
                        }
                    }
                    if (!string.IsNullOrEmpty(b.Caption))
                    {
                        block.SetAttributeValue("caption", Clean(b.Caption));
                        block.SetAttributeValue("captionStyle", b.CaptionStyle.ToString());
                    }
                    sec.Add(block);
                }
                root.Add(sec);
            }

            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(path, settings)) new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(w);
        }

        public static AuditReport Load(string path)
        {
            XDocument doc;
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc = XDocument.Load(reader);
            var root = doc.Root;
            if (root == null || (root.Name.LocalName != "DeviceDossierReport" && root.Name.LocalName != "SysAuditReport")) throw new FormatException("This file is not a DeviceDossier XML report.");

            var report = new AuditReport
            {
                DeviceName = (string)root.Attribute("device"),
                RunAsUser = (string)root.Attribute("runAs"),
                RanAsAdmin = (string)root.Attribute("administrator") == "true",
                IsLaptop = (string)root.Attribute("laptop") == "true",
                ToolName = ToolPart((string)root.Attribute("tool"), true),
                ToolVersion = ToolPart((string)root.Attribute("tool"), false)
            };
            DateTime generated;
            if (DateTime.TryParse((string)root.Attribute("generated"), CultureInfo.InvariantCulture, DateTimeStyles.None, out generated)) report.Generated = generated;

            foreach (var sec in root.Elements("Section"))
            {
                var section = new Section((string)sec.Attribute("id"), (string)sec.Attribute("number"), (string)sec.Attribute("title"));
                SectionState state;
                if (Enum.TryParse((string)sec.Attribute("state"), out state)) section.State = state;
                section.StateNote = (string)sec.Attribute("note");
                foreach (var el in sec.Elements())
                {
                    Block block;
                    if (el.Name.LocalName == "Fields")
                    {
                        block = Block.KeyValue();
                        foreach (var f in el.Elements("Field")) block.Add((string)f.Attribute("name"), Read(f));
                    }
                    else if (el.Name.LocalName == "Table")
                    {
                        var cols = el.Element("Columns");
                        block = Block.Table(cols == null ? new string[0] : cols.Elements("Column").Select(c => c.Value).ToArray());
                        foreach (var row in el.Elements("Row")) block.AddRow(row.Elements("Cell").Select(Read).ToArray());
                    }
                    else continue;
                    var caption = (string)el.Attribute("caption");
                    if (!string.IsNullOrEmpty(caption))
                    {
                        CaptionStyle style;
                        if (!Enum.TryParse((string)el.Attribute("captionStyle"), out style)) style = CaptionStyle.Bold;
                        block.WithCaption(caption, style);
                    }
                    section.With(block);
                }
                report.Add(section);
            }
            return report;
        }

        private static string ToolPart(string tool, bool name)
        {
            tool = (tool ?? "").Trim();
            int space = tool.LastIndexOf(' ');
            if (space <= 0) return name ? (tool.Length > 0 ? tool : AppInfo.Name) : "";
            return name ? tool.Substring(0, space) : tool.Substring(space + 1);
        }

        private static XElement Value(XElement el, object value)
        {
            if (Fmt.IsNumber(value))
            {
                el.Add(new XAttribute("type", "number"));
                el.Value = Fmt.ToDouble(value).ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                el.Value = Clean(Fmt.Text(value));
            }
            return el;
        }

        private static object Read(XElement el)
        {
            if ((string)el.Attribute("type") == "number")
            {
                double d;
                if (double.TryParse(el.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            }
            return el.Value;
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
                if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }
}
