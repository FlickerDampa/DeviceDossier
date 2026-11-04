# Contributing

Thanks for helping improve DeviceDossier.

## Reporting bugs

Open an issue using the bug report template. Wrong or missing values are the most useful reports. Please include:

- the Windows edition and build, and whether it was run as an administrator
- the section and field that is wrong, what it showed, and what you expected
- the XML report if you can share it. **Remove or replace** serial numbers, product keys, MAC and IP addresses, and
  user names first.

## Development setup

- Windows with the .NET SDK 8 or Visual Studio 2022
- `dotnet build src/DeviceDossier/DeviceDossier.csproj -c Release`
- The tests need Python 3 with `openpyxl` and `lxml`, plus Mono to run `tests/ExportParity` on Linux or WSL

Before opening a pull request, run:

```bash
python3 tests/XamlLint/check.py
bash tests/ExportParity/run.sh
```

## Guidelines

- **Read-only.** Collectors must never change the audited PC: no settings, services, files outside the report folder,
  or registry writes.
- **No network access.** DeviceDossier must not send data anywhere.
- **Start other programs by full path only,** never by bare name, because the app usually runs with admin rights.
- **Keep report shapes stable.** Sections that are skipped or fail still produce the same rows and columns. If you
  change the XML format, update `docs/report-schema.xsd` and raise `XmlReportFile.SchemaVersion`.
- **Fail softly.** If something can't be read, record `N/A - <reason>` and let the audit carry on.
- **Theme-aware UI.** Use `DynamicResource` colours from `Themes/Light.xaml` and `Themes/Dark.xaml`, and add any new
  colour to both palettes and to the High Contrast map in `Views/ThemeManager.cs`.
- **Style.** C# 7.3 for .NET Framework 4.8, no new NuGet runtime dependencies, and no code comments that just restate
  the code.

## Pull requests

Keep pull requests focused. Describe what changed and how you tested it, ideally on more than one PC, and add a line
to `CHANGELOG.md`.
