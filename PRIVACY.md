# Privacy

DeviceDossier is an offline tool. It has no telemetry, never connects to the internet or any server, and writes
reports only to the folder you choose. This page lists what a report can contain, so you can decide where to keep it
and who may see it.

## What a report contains

| Area | Personal or sensitive data |
|---|---|
| Asset record | Whatever you type: asset tag, the person the device is assigned to, location, notes |
| Device | Computer name, domain, BIOS and motherboard serial numbers, system UUID, asset tag |
| Windows | Registered user, install and boot times, Windows product key (masked by default), activation channel |
| Hardware | Serial numbers of memory modules, drives, monitors and the battery |
| Network | MAC addresses, IP addresses, gateways, DNS servers |
| Mapped drives | Local user names and the network paths each user has mapped or saved |
| Security | Antivirus status, BitLocker status and key protector types (never recovery keys), local administrator accounts, Windows Update history |
| Software | Installed programs, including ones installed only for the user running the audit |
| Browser extensions | Extensions installed in Chrome, Edge, Opera and Firefox for every user profile on the PC |

DeviceDossier does not read browsing history, passwords, saved credentials, documents, email or BitLocker recovery keys.

## Reducing what is collected

- **Settings → Mask the Windows product key** is on by default. Only the last five characters are shown.
- Untick **Mapped network drives**, **Installed software** or **Browser extensions** to leave those sections out. Skipped
  sections stay in the report, marked as skipped.
- Leave the asset record empty if you don't need it.

## Where reports go

- By default, reports go to a folder on the Desktop. On many PCs the Desktop is synced to OneDrive, which uploads
  everything in it to the cloud. DeviceDossier warns you when the chosen folder is synced. Pick a local folder or a USB
  drive to avoid this.
- If an error occurs, technical details are written to `%TEMP%\DeviceDossier_error.log` on the audited PC. This file
  contains error messages and stack traces, not report data.

## Your responsibility

Reports describe a device and the people who use it. Only audit devices you own or are authorised to audit, tell users
when your policies require it, and store reports somewhere access-controlled.
