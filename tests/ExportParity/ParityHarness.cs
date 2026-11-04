using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DeviceDossier.Core;
using DeviceDossier.Export;
using DeviceDossier.Util;

public static class ParityHarness
{
    public static int Main(string[] args)
    {
        var root = Json.ParseObject(File.ReadAllText(args[0]));
        var report = new AuditReport
        {
            DeviceName = (string)root["device"],
            Generated = DateTime.ParseExact((string)root["generated"], "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            ToolVersion = "test"
        };
        foreach (Dictionary<string, object> s in (object[])root["sections"])
        {
            string number = (string)s["number"];
            string id;
            if (!Ids.TryGetValue(number, out id)) id = "s" + number;
            var section = new Section(id, number, (string)s["title"]);
            foreach (Dictionary<string, object> b in (object[])s["blocks"])
            {
                Block block;
                if ((string)b["kind"] == "KeyValue")
                {
                    block = Block.KeyValue();
                    foreach (object[] f in (object[])b["fields"]) block.Add((string)f[0], Val(f[1]));
                }
                else
                {
                    var headers = new List<string>();
                    foreach (var h in (object[])b["headers"]) headers.Add((string)h);
                    block = Block.Table(headers.ToArray());
                    foreach (object[] r in (object[])b["rows"])
                    {
                        var vals = new object[r.Length];
                        for (int i = 0; i < r.Length; i++) vals[i] = Val(r[i]);
                        block.AddRow(vals);
                    }
                }
                if (b["caption"] != null) block.WithCaption((string)b["caption"], (string)b["style"] == "Italic" ? CaptionStyle.Italic : CaptionStyle.Bold);
                section.With(block);
            }
            report.Add(section);
        }
        report.DeviceName = report.DeviceName ?? "TEST";
        report.RunAsUser = "DEMO\\auditor";
        report.RanAsAdmin = true;

        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--roundtrip")
            {
                XmlReportFile.Save(report, args[i + 1]);
                var reloaded = XmlReportFile.Load(args[i + 1]);
                report = reloaded;
                Console.WriteLine("xml round trip " + args[i + 1]);
            }
            else if (args[i] == "--html")
            {
                HtmlReportExporter.Save(report, args[i + 1]);
                Console.WriteLine("html " + args[i + 1]);
            }
        }
        AuditExcelExporter.Save(report, new AuditSettings(), args[1]);
        Console.WriteLine("saved " + args[1]);
        return 0;
    }

    private static readonly Dictionary<string, string> Ids = new Dictionary<string, string>
    {
        { "1", "device" }, { "2", "os" }, { "3", "cpu" }, { "4", "ram" }, { "5", "gpu" }, { "6", "storage" }, { "6b", "motherboard" },
        { "6c", "power" }, { "7", "network" }, { "8", "drives" }, { "9", "security" }, { "10", "software" }, { "10b", "extensions" },
        { "11", "monitors" }, { "12", "baseline" }
    };

    private static object Val(object v)
    {
        if (v is long) return (double)(long)v;
        return v;
    }
}
