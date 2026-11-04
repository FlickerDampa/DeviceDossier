#!/usr/bin/env bash
# Rebuilds the fixture audit with DeviceDossier's exporters and checks the Excel output matches the
# original IT_Audit_Tool.ps1 layout cell by cell, directly and after an XML save/load round trip.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
src="$here/../../src/DeviceDossier"
work="$(mktemp -d)"
mcs -target:library -out:"$work/core.dll" \
  -r:System.dll -r:System.Core.dll -r:System.Management.dll -r:System.DirectoryServices.dll \
  -r:System.IO.Compression.dll -r:System.Xml.Linq.dll -r:System.Xml.dll \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  "$src"/Core/*.cs "$src"/Collectors/*.cs "$src"/Export/*.cs "$src"/Util/*.cs
mcs -out:"$work/parity.exe" -r:"$work/core.dll" "$here/ParityHarness.cs"
python3 "$here/audit_to_json.py" "$here/fixture_audit.xlsx" "$work/fixture.json"
echo "== Excel export"
mono "$work/parity.exe" "$work/fixture.json" "$work/rebuilt.xlsx" --html "$work/report.html"
python3 "$here/compare.py" "$here/fixture_audit.xlsx" "$work/rebuilt.xlsx"
echo "== XML save/load round trip"
mono "$work/parity.exe" "$work/fixture.json" "$work/roundtrip.xlsx" --roundtrip "$work/report.xml"
python3 "$here/compare.py" "$here/fixture_audit.xlsx" "$work/roundtrip.xlsx"
python3 -c "import sys;from lxml import etree;s=etree.XMLSchema(etree.parse(sys.argv[1]));d=etree.parse(sys.argv[2]);ok=s.validate(d);print('XML report matches docs/report-schema.xsd:',ok);sys.exit(0 if ok else str(s.error_log))" "$here/../../docs/report-schema.xsd" "$work/report.xml"
python3 -c "import sys,html.parser;p=html.parser.HTMLParser();p.feed(open(sys.argv[1],encoding='utf-8').read());print('HTML report parses:',sys.argv[1])" "$work/report.html"
if [ -n "${KEEP:-}" ]; then cp "$work"/report.* "$KEEP"/; fi
