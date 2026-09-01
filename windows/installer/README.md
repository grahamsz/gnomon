# Windows MSI

Build the self-contained agent publish first, then the WiX project as shown in
the [Windows README](../README.md). A Release build produces
`GnomonAgent-{version}-x64.msi`.

Interactive installation shows a standard Windows installer and then opens the
Gnomon configuration window. It asks for the Home Assistant address (default
`homeassistant.local`), long-lived access token, kid ID, device ID, and Windows
account, then restarts the watchdog service.

For managed deployment, silent installation remains available:

```powershell
msiexec /i GnomonAgent-1.0.0-x64.msi /qn /l*v install.log
& "$env:ProgramFiles\Gnomon\Gnomon.Agent.exe" --configure
```

The configuration window elevates only when it writes the machine configuration.
It accepts a hostname or an HTTP(S)/WS(S) URL and normalizes it to Home
Assistant's WebSocket endpoint. The installer is per-machine and needs
administrator rights. Upgrades preserve
`config.json`, `rules-cache.json`, and logs. Uninstall removes the service,
binary, and HKLM Run value while intentionally leaving `%ProgramData%\Gnomon`.
Downgrades are blocked; same-version repair is allowed.

For a development signature, create/export a code-signing certificate to PFX and
build with `-p:SignCertificate=C:\path\dev.pfx -p:SignPassword=...`. Production
builds should use a trusted certificate. Unsigned or self-signed builds can show
the expected Windows SmartScreen unknown-publisher warning.
