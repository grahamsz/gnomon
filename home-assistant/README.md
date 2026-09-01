# Gnomon Home Assistant integration

Copy the repository's `custom_components/gnomon` directory into
`custom_components/gnomon` under your Home Assistant configuration directory,
restart HA, and add **Gnomon** from Settings → Devices & services.
The first setup seeds common categories, Windows/Android app rules, and domains.

Configure daily resets with an ordinary HA automation (there is intentionally no
midnight behavior in the integration):

```yaml
automation:
  - alias: "Reset Alex screen time"
    trigger: [{platform: time, at: "06:00:00"}]
    action:
      - action: gnomon.reset
        data: {kid: alex}
```

Create a long-lived access token for each agent. The integration persists the
ledger, limits, rules, registered devices, and unknown inbox through HA's storage
helper. Agent connectivity deliberately starts off after an HA restart.

## Test

From the repository root after loading `dev-env.ps1`:

```powershell
Push-Location home-assistant
python -m pytest
Pop-Location
```
