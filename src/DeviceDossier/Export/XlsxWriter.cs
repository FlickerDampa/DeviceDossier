using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DeviceDossier.Core;

namespace DeviceDossier.Export
{
    public enum XStyle
    {
        Default = 0,
        Bold = 1,
        Title = 2,
        ItalicCentered = 3,
        SectionHeader = 4,
        TableHeader = 5,
        AltRow = 6,
        Italic = 7
    }

    public sealed class XlsxSheet
    {
        private sealed class Cell
        {
            public object Value;
            public XStyle Style;
        }

        private readonly SortedDictionary<int, SortedDictionary<int, Cell>> _rows = new SortedDictionary<int, SortedDictionary<int, Cell>>();
        private readonly List<string> _merges = new List<string>();
        private readonly SortedDictionary<int, double> _widths = new SortedDictionary<int, double>();

        public XlsxSheet(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
        public int FrozenRows { get; set; }

        public void Set(int row, int col, object value, XStyle style)
        {
            SortedDictionary<int, Cell> cells;
            if (!_rows.TryGetValue(row, out cells))
            {
                cells = new SortedDictionary<int, Cell>();
                _rows[row] = cells;
            }
            Cell existing;
            if (cells.TryGetValue(col, out existing))
            {
                if (value != null) existing.Value = value;
                existing.Style = style;
            }
            else
            {
                cells[col] = new Cell { Value = value, Style = style };
            }
        }

        public void Style(int row, int col, XStyle style)
        {
            Set(row, col, null, style);
        }

        public void Merge(int row, int fromCol, int toCol)
        {
            _merges.Add(Ref(row, fromCol) + ":" + Ref(row, toCol));
        }

        public void Width(int col, double width)
        {
            _widths[col] = width;
        }

        public static string Ref(int row, int col)
        {
            return ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);
        }

        public static string ColumnName(int col)
        {
            var sb = new StringBuilder();
            while (col > 0)
            {
                int rem = (col - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                col = (col - 1) / 26;
            }
            return sb.ToString();
        }

        internal string ToXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            if (FrozenRows > 0)
            {
                string top = Ref(FrozenRows + 1, 1);
                sb.Append("<pane ySplit=\"").Append(FrozenRows).Append("\" topLeftCell=\"").Append(top).Append("\" activePane=\"bottomLeft\" state=\"frozen\"/>");
                sb.Append("<selection pane=\"bottomLeft\" activeCell=\"").Append(top).Append("\" sqref=\"").Append(top).Append("\"/>");
            }
            sb.Append("</sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            if (_widths.Count > 0)
            {
                sb.Append("<cols>");
                foreach (var w in _widths)
                    sb.Append("<col min=\"").Append(w.Key).Append("\" max=\"").Append(w.Key).Append("\" width=\"")
                      .Append(w.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            foreach (var row in _rows)
            {
                sb.Append("<row r=\"").Append(row.Key).Append("\">");
                foreach (var c in row.Value)
                {
                    string r = Ref(row.Key, c.Key);
                    int s = (int)c.Value.Style;
                    object v = c.Value.Value;
                    if (v == null)
                    {
                        sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(s).Append("\"/>");
                    }
                    else if (Fmt.IsNumber(v))
                    {
                        double d = Fmt.ToDouble(v);
                        sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(s).Append("\"><v>")
                          .Append(d.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                    }
                    else
                    {
                        sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(s).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                          .Append(Escape(Fmt.Text(v))).Append("</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (_merges.Count > 0)
            {
                sb.Append("<mergeCells count=\"").Append(_merges.Count).Append("\">");
                foreach (var m in _merges) sb.Append("<mergeCell ref=\"").Append(m).Append("\"/>");
                sb.Append("</mergeCells>");
            }
            sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        internal static string Escape(string text)
        {
            if (text.Length > 32767) text = text.Substring(0, 32767);
            var sb = new StringBuilder(text.Length + 16);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        bool valid = ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch);
                        if (valid) sb.Append(ch);
                        else sb.Append("_x").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public static class XlsxWriter
    {
        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"5\">" +
            "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
            "<font><b/><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
            "<font><b/><sz val=\"16\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
            "<font><b/><sz val=\"13\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
            "<font><i/><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
            "</fonts>" +
            "<fills count=\"6\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF112E51\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F497D\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFBDD7EE\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEBF1FA\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"8\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"5\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        public static void Save(XlsxSheet sheet, string path, string creator)
        {
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                    "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
                    "</Types>");
                Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                    "</Relationships>");
                Entry(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:creator>" + XlsxSheet.Escape(creator) + "</dc:creator>" +
                    "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "</dcterms:created>" +
                    "</cp:coreProperties>");
                Entry(zip, "docProps/app.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>" + XlsxSheet.Escape(creator) + "</Application></Properties>");
                Entry(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<bookViews><workbookView/></bookViews>" +
                    "<sheets><sheet name=\"" + XlsxSheet.Escape(sheet.Name) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Entry(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Entry(zip, "xl/styles.xml", Styles);
                Entry(zip, "xl/worksheets/sheet1.xml", sheet.ToXml());
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }
    }
}
