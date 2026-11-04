# Security policy

## Supported versions

Only the latest release, including pre-releases, receives security fixes.

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private reporting instead: open the
repository's **Security** tab and choose **Report a vulnerability**.

Include:

- the DeviceDossier version (shown in the sidebar, or with `DeviceDossier.exe --help`)
- the Windows version and whether DeviceDossier was run as an administrator
- steps to reproduce, and what an attacker could achieve

You should get a reply within a week. Fixes are released as soon as they're ready, and reporters are credited in the
changelog unless they prefer not to be.

## Scope

DeviceDossier usually runs with administrator rights, so these are especially relevant:

- anything that makes DeviceDossier run code that isn't its own, for example from its folder or a settings file
- anything that makes it change the audited PC; it is meant to be read-only
- report files (Excel, HTML or XML) that could run script or exploit the program that opens them
- sensitive data appearing in a report when the settings say it should be masked or skipped

## Verifying downloads

Each release includes `SHA256SUMS.txt`. Compare it with:

```powershell
Get-FileHash .\DeviceDossier.zip -Algorithm SHA256
```
