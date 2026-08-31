# Windows MSI

Build the self-contained agent publish first, then the WiX project as shown in
the [Windows README](../README.md). A Release build produces
`GnomonAgent-{version}-x64.msi`.

Silent parent-managed installation:

```powershell
msiexec /i GnomonAgent-1.0.0-x64.msi /qn /l*v install.log
notepad $env:ProgramData\Gnomon\config.json
net start GnomonAgent
```

The installer is per-machine and needs administrator rights. Upgrades preserve
`config.json`, `rules-cache.json`, and logs. Uninstall removes the service,
binary, and HKLM Run value while intentionally leaving `%ProgramData%\Gnomon`.
Downgrades are blocked; same-version repair is allowed.

For a development signature, create/export a code-signing certificate to PFX and
build with `-p:SignCertificate=C:\path\dev.pfx -p:SignPassword=...`. Production
builds should use a trusted certificate. Unsigned or self-signed builds can show
the expected Windows SmartScreen unknown-publisher warning.
