# Changelog

## 1.0.0-beta.1

First public release. DeviceDossier began as an internal PowerShell audit script (`IT_Audit_Tool.ps1`) and an
unreleased app called SysAudit. It was renamed because that name is already used by another Windows audit project.

### Reports
- Excel, HTML and XML reports, in any combination.
  - The Excel workbook keeps the single-sheet layout of the original script.
  - The HTML report is self-contained, with tabs, search across the whole report, sortable tables, dark mode and a print layout.
  - The XML format is documented in `docs/report-schema.xsd`.
- Saved XML reports can be opened in the app and saved again as Excel, HTML or XML. Reports from SysAudit open too.
- An optional **asset record** (asset tag, assigned to, location, notes) can be entered in the app or with `--asset-tag`, `--assigned-to`, `--location` and `--notes`.

### What is collected
- Device: model name (for example Lenovo's "Legion Pro 7" instead of "82WQ"), system UUID and SMBIOS asset tag.
- Windows: display version (for example 25H2) and the full build number including the update revision.
- Storage:
  - real NVMe serial numbers, read from the drive's NVMe Identify data with a read-only query, instead of the `ACE4_2E00_...` EUI-64 identifier Windows reports
  - the bus type (NVMe, SATA, USB and so on) instead of "SCSI"
  - SSD or HDD instead of "Fixed hard disk media"
  - the drive's own health status where wear data isn't available
- Network: virtual adapters without an address (WAN Miniport, Wi-Fi Direct and similar) are left out, and the report says how many.
- Software: each installed version is listed separately, so two versions of the same product both appear. Install dates use `dd-MM-yyyy`.
- Mapped drives: every local profile, including logged-off users, drives mapped only in a signed-in session, and saved network locations.
- Displays: monitor manufacturer, model, serial number, manufacture date, size and connection.
- Security baseline: firmware type, Secure Boot, TPM, firewall profiles, UAC, Remote Desktop, SMBv1, Guest account, Defender signatures and pending restart.
- GPU details come straight from the display driver and `nvidia-smi`. GPU-Z is not used.

### Fixes from real-PC testing
- **Firmware type** showed "Unknown" on UEFI PCs. It now uses Windows' `GetFirmwareType`.
- **Guest account** is now read from the account's own flags. The previous check could report a disabled account as enabled.
- **Battery** status code 2 now reads "On AC power, not charging" instead of "Unknown".
- **Windows Defender** is shown once, with its protection status in the same row as the Security Center entry.
- **Patch level:**
  - "Latest Security Update" no longer picks the Defender platform update or definition updates.
  - "Latest Windows (Quality) Update" recognises Microsoft's newer update titles, such as "2026-10 Security Update (KB…) (26300.9711)", as well as Insider builds.
  - Microsoft Store app updates no longer appear as "Latest General/Suite Update".
- **Windows Server:** the antivirus section explains that Server has no Security Center.

### Safety and privacy
- **Logged-off users' profiles** are opened privately and read-only with `RegLoadAppKey`. They are no longer mounted with `reg.exe load`, so a failed unload can't give the user a temporary profile at their next sign-in, and no external program is started by name while running as administrator.
- **Each section has a time limit** (180 seconds by default, configurable). A section that hangs is marked as an error and the audit carries on, and Cancel takes effect immediately.
- **The Windows product key is masked by default.**
- **A warning is shown** when reports would be saved to a OneDrive-synced folder.
- `PRIVACY.md` lists everything a report can contain.

### App
- New icon: a dossier folder holding a processor chip, on a blue gradient tile. It has a simplified design for 16–24 px and is used for the exe, the window and taskbar, the sidebar, and the HTML report header and favicon. The sources are in `docs/icon/`.
- Light, dark and High Contrast themes. The default follows Windows, including the title bar.
- Redesigned interface:
  - icon sidebar and page headers
  - themed controls throughout
  - overview cards that reflow on smaller screens
  - the report opens as soon as an audit finishes
  - screen-reader names for icon buttons
- Save to the Desktop, any detected USB drive, or any folder.
- Settings page with import and export. `SysAudit.settings.xml` and `SysAudit.ini` files are converted automatically.
- Command line: `--silent`, `--output`, `--formats`, `--settings`, `--skip`, `--log`, plus the asset options.

### Project
- No `System.Web` dependency. Browser-extension files are read with a built-in JSON reader.
- `NuGet.config` included, so builds work on PCs with only Visual Studio's offline package source.
- Tests:
  - XAML resource and handler checks
  - Excel parity
  - XML round trip
  - schema validation
  - a silent-audit smoke test on Windows in CI
- Releases include SHA-256 checksums and are published without third-party actions.
- Added `SECURITY.md`, `CONTRIBUTING.md` and issue templates.
