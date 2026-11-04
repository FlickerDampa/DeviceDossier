using System.Globalization;
using DeviceDossier.Core;

namespace DeviceDossier.Export
{
    public static class AuditExcelExporter
    {
        private const int LastMergedColumn = 7;
        private static readonly double[] ColumnWidths = { 28, 40, 22, 22, 22, 40, 28, 22, 22, 22, 22 };

        public static XlsxSheet Build(AuditReport report)
        {
            var ws = new XlsxSheet("IT Audit Report") { FrozenRows = 2 };
            int row = 1;

            ws.Set(row, 1, "IT AUDIT REPORT", XStyle.Title);
            ws.Merge(row, 1, LastMergedColumn);
            row++;

            ws.Set(row, 1, "Generated: " + report.Generated.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + "   Device: " + report.DeviceName, XStyle.ItalicCentered);
            ws.Merge(row, 1, LastMergedColumn);
            row += 2;

            for (int s = 0; s < report.Sections.Count; s++)
            {
                var section = report.Sections[s];
                ws.Set(row, 1, section.Heading, XStyle.SectionHeader);
                for (int c = 2; c <= LastMergedColumn; c++) ws.Style(row, c, XStyle.SectionHeader);
                ws.Merge(row, 1, LastMergedColumn);
                row++;

                for (int b = 0; b < section.Blocks.Count; b++)
                {
                    var block = section.Blocks[b];
                    if (!string.IsNullOrEmpty(block.Caption))
                    {
                        ws.Set(row, 1, block.Caption, block.CaptionStyle == CaptionStyle.Italic ? XStyle.Italic : XStyle.Bold);
                        ws.Merge(row, 1, LastMergedColumn);
                        row++;
                    }
                    row = block.Kind == BlockKind.KeyValue ? WriteFields(ws, row, block) : WriteTable(ws, row, block);
                    if (b < section.Blocks.Count - 1) row++;
                }
                row += 2;
            }

            for (int i = 0; i < ColumnWidths.Length; i++) ws.Width(i + 1, ColumnWidths[i]);
            return ws;
        }

        public static void Save(AuditReport report, AuditSettings settings, string path)
        {
            XlsxWriter.Save(Build(report), path, "DeviceDossier " + (report.ToolVersion ?? ""));
        }

        private static int WriteFields(XlsxSheet ws, int row, Block block)
        {
            foreach (var f in block.Fields)
            {
                ws.Set(row, 1, f.Name, XStyle.Bold);
                ws.Set(row, 2, f.Value ?? "", XStyle.Default);
                ws.Merge(row, 2, LastMergedColumn);
                row++;
            }
            return row;
        }

        private static int WriteTable(XlsxSheet ws, int row, Block block)
        {
            for (int c = 0; c < block.Headers.Length; c++) ws.Set(row, c + 1, block.Headers[c], XStyle.TableHeader);
            row++;
            bool alt = false;
            foreach (var r in block.Rows)
            {
                for (int c = 0; c < block.Headers.Length; c++)
                    ws.Set(row, c + 1, r[c] ?? "", alt ? XStyle.AltRow : XStyle.Default);
                alt = !alt;
                row++;
            }
            return row;
        }
    }
}
