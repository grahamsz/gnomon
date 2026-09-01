# Releases, signing, and HACS

GitHub Actions builds all three components on pull requests. A push to `main`
also replaces the moving `edge` prerelease, while a semantic tag such as
`v1.0.0` creates a production release.

## GitHub repository metadata

HACS requires a public repository with Issues enabled, a description, and at
least one topic. This repository is public and Issues are enabled. In the
repository's **About** settings, set:

- Description: `Transparent screen-time measurement for Home Assistant, Windows, and Android`
- Topics: `home-assistant`, `hacs`, `screen-time`, `android`, `windows`

The description and topics cannot be stored in Git, so they must be set once in
GitHub's repository UI.

## Android signing (required for production)

Create one upload keystore and keep it permanently. Android will only install an
upgrade when it is signed by the same key as the installed app.

```powershell
keytool -genkeypair -v -keystore gnomon-release.jks -alias gnomon -keyalg RSA -keysize 4096 -validity 10000
$encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path .\gnomon-release.jks)))
$encoded | gh secret set ANDROID_KEYSTORE_BASE64
gh secret set ANDROID_KEYSTORE_PASSWORD
gh secret set ANDROID_KEY_ALIAS
gh secret set ANDROID_KEY_PASSWORD
```

Set `ANDROID_KEY_ALIAS` to `gnomon` if you use the command above. Keep the JKS
and its passwords in a password manager/offline backup; losing them prevents
future APK updates. Edge releases use the same key when configured. Without it,
edge still builds an installable debug APK, but successive edge APKs may require
uninstalling the prior build. Production intentionally fails rather than publish
an unusable unsigned APK.

## Windows signing (optional)

The MSI and executable are usable unsigned, but Windows may show an
unknown-publisher/SmartScreen warning. To sign them, export a trusted code-signing
certificate as PFX and set both secrets:

```powershell
$encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path .\gnomon-signing.pfx)))
$encoded | gh secret set WINDOWS_CERTIFICATE_BASE64
gh secret set WINDOWS_CERTIFICATE_PASSWORD
```

## Production release

After the signing secrets are configured, create and push an annotated semantic
version tag. The workflow tests everything, publishes the Windows MSI/portable
ZIP/browser companion, signed Android APK/AAB, and a manual-install `gnomon.zip`
Home Assistant package.

```powershell
git tag -a v1.0.0 -m "Gnomon 1.0.0"
git push origin v1.0.0
```

Windows Installer versions limit the first two version numbers to 255 and the
third to 65535. The workflow validates this before building.

## Add Gnomon to HACS and Home Assistant

The root-level `custom_components/gnomon` layout lets HACS install the default
branch or a tagged release directly from GitHub. The separately attached
`gnomon.zip` asset is only for manual installation outside HACS.

1. In HACS, open **Integrations**, choose the three-dot menu, then **Custom repositories**.
2. Enter `https://github.com/grahamsz/gnomon`, select **Integration**, and add it.
3. Open Gnomon in HACS, choose **Download** (select `main` until a production
   release exists), and restart Home Assistant.
4. In Home Assistant, go to **Settings → Devices & services → Add integration** and select **Gnomon**.
5. Enter the first child's display name and stable lowercase ID, such as `alex`.
6. Create a Home Assistant long-lived access token from the user profile for each agent.
7. Configure Android in its visible setup screen. The interactive Windows MSI
   opens Gnomon's guided configuration window after installation; for a silent
   deployment, run `%ProgramFiles%\Gnomon\Gnomon.Agent.exe --configure` afterward.

The Home Assistant WebSocket URL is normally
`ws://homeassistant.local:8123/api/websocket`. The kid ID in each agent must
match the ID configured in the integration; give every device its own device ID.
