# TrayLight

> A lightweight Windows System Tray app that gives end users instant access
> to device info, IT support shortcuts, and Intune sync — branded for your
> organization and configured entirely through Group Policy / Intune.

[![Latest release](https://img.shields.io/github/v/release/daniel-fraubaum/TrayLight?include_prereleases&sort=semver)](https://github.com/daniel-fraubaum/TrayLight/releases/latest)
[![Build](https://github.com/daniel-fraubaum/TrayLight/actions/workflows/build.yml/badge.svg)](https://github.com/daniel-fraubaum/TrayLight/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

![TrayLight Screenshot](docs/images/traylight-screenshot.png)

## Features

- System Tray icon with a modern Windows 11 Fluent Design popup
- **Tray-icon hover tooltip** — hovering the tray icon shows a compact device
  summary (computer name, IP address, last Intune sync, and any active warning
  count) without opening the full popup; localized like the rest of the UI
- Tray-icon state swap: shows a warning variant
  (`Assets/app-warning.ico`) whenever any info tile reports a real warning,
  otherwise the normal icon (`Assets/app-normal.ico`)
- **Default info tiles** (always shown, six in total):
  - **Computer Name** — click to copy
  - **OS Version** — informational
  - **Last Reboot** — uptime; warns when above the configured day limit
  - **Storage Usage** — system drive %; warns at the configured threshold
  - **Serial Number** — hardware serial via WMI (with VM detection); click to copy
  - **Intune Sync** — time since last MDM check-in; click to trigger a sync
- **Additional tiles available via ADMX** (not enabled by default):
  - **Network Info** — active SSID/Ethernet + IPv4. Adapter detection ignores
    virtual adapters (Hyper-V, VMware, VirtualBox, vEthernet, WSL, TAP/Npcap)
    and APIPA (169.254.x.x) addresses, preferring the physical Ethernet/Wi-Fi
    adapter that owns the default gateway, so the real device IP is shown even
    on machines running Hyper-V or VMware.
- Up to **6 configurable Quick Action shortcut buttons** (URL / App / Command),
  each with its own title, subtitle, icon and position via ADMX
- **Dynamic placeholders** in shortcut actions — embed `{{ComputerName}}`,
  `{{OsVersion}}`, `{{SerialNumber}}`, `{{IntuneSync}}` and more; replaced with
  live device values at click-time (auto URL-encoded for mailto/https links)
- **Customizable info text block** under the Quick Actions — show office
  hours, helpdesk hotline, on-call info, etc. Tiny inline markup
  (`*bold*`, `|` line break, `||` blank line) configured via a single
  ADMX policy.
- Toast notifications for warnings raised by info tiles
- **Multi-language UI** — auto-detects the Windows display language; ships with
  English, German and French as runtime JSON files, and any other language can
  be added by dropping a JSON file into the `Languages` folder — no rebuild
  required (see [Localization](#localization))
- Light / Dark theme follows the Windows system setting
- WPF with drop-shadow on Windows 11
- Welcome screen on first launch
- "Copy system info" button for helpdesk tickets
- Warnings only ever come from two scenarios: storage above threshold, or
  uptime above `Behavior\RebootWarningDays`. Everything else is purely
  informational — a fresh device produces zero warnings.

## Configuration

TrayLight works **out of the box with zero configuration** — on a fresh
device with no policy in place, it starts with sensible defaults
("IT Support" title, the `#0078D4` accent color, and the six default tiles
listed above). Customization is optional and pushed via **Intune Settings
Catalog** (or Group Policy) using the bundled ADMX/ADML templates.

The MSI registers an HKLM `Run` key so TrayLight launches automatically on
every user logon. Admins can disable this without uninstalling by setting
`Behavior\AutoStart = 0` in policy — the process then exits immediately on
startup.

- Registry root: `HKLM\SOFTWARE\Policies\TrayLight\`
- Templates: [`config/admx/TrayLight.admx`](config/admx/TrayLight.admx) and [`config/admx/en-US/TrayLight.adml`](config/admx/en-US/TrayLight.adml)
- Reference: [`config/admx/README.md`](config/admx/README.md)

For the full Intune walkthrough see [`docs/INTUNE-DEPLOYMENT.md`](docs/INTUNE-DEPLOYMENT.md).

> **ADMX text doubles as a localization mechanism.** Every visible string
> (tile titles, header/branding title, shortcut labels, footer and info text)
> can be overridden via policy, and those overrides always take precedence over
> the auto-detected UI language. So for a language that isn't built in, set the
> text in your language via Intune Settings Catalog — no code change required.
> See [Localization](#localization) for the full priority order.

### Customer logo

The `Branding\Logo` policy accepts either a URL or a local file path:

- **Option A — URL (easiest):** set `Logo = https://corp.example.com/logo.png`
  via Intune. TrayLight downloads it to
  `%ProgramData%\TrayLight\Cache\logo.png` and refreshes once a day. If the
  download fails the previously cached copy is used.
- **Option B — Bundled file (offline / airgapped):** drop a `logo.png` into
  `src/TrayLight/Assets/` before building the MSI; it ships inside the
  package and is installed to `C:\Program Files\TrayLight\Assets\logo.png`.
  Set `Logo = C:\Program Files\TrayLight\Assets\logo.png` in policy.

## Tray icon visibility

After installation, the TrayLight icon may appear in the taskbar overflow
area (the `^` arrow). To keep it permanently visible, drag the icon from
the overflow onto the main taskbar. This is a one-time action per user —
Windows remembers the preference. This is a Windows limitation that
affects all system tray applications and cannot be controlled via policy.

## Getting Started

**Prerequisites**

- .NET 10 SDK (with the *Windows Desktop* workload)
- Windows 11 SDK `10.0.26100`

**Build & run**

```pwsh
git clone https://github.com/daniel-fraubaum/TrayLight.git
cd TrayLight
dotnet restore TrayLight.sln
dotnet build   TrayLight.sln -c Debug
dotnet run --project src/TrayLight/TrayLight.csproj
```

**Build the MSI locally**

```pwsh
dotnet build src/TrayLight.Installer/TrayLight.Installer.wixproj -c Release
# → src/TrayLight.Installer/bin/x64/Release/TrayLight.msi
```

> CI/CD on GitHub Actions builds the MSI automatically on every tagged
> release (`v*`) and attaches `TrayLight-vX.Y.Z.msi` to the GitHub Release.

**Installation**

> The MSI is **self-contained** — it ships with its own .NET 10 runtime, so
> no .NET installation is required on the target machine. The package size
> is roughly 70 MB as a result.

- **Manual (interactive):** double-click `TrayLight.msi` to launch the setup
  wizard. The install directory defaults to `C:\Program Files\TrayLight\`.
  The final page offers a *Launch TrayLight* checkbox.
- **Silent (e.g. for Intune / scripted rollout):**
  ```pwsh
  msiexec /i TrayLight.msi /qn
  ```
- **Uninstall:**
  ```pwsh
  msiexec /x {2493D886-5C60-45E0-95BE-7F450BE2C2FC} /qn
  ```

## Deployment

See [`docs/INTUNE-DEPLOYMENT.md`](docs/INTUNE-DEPLOYMENT.md) for:

- Uploading the MSI to Intune as a Line-of-Business app
- Silent install / uninstall / detection rules
- Configuration via Intune Settings Catalog (ADMX import) — every tile
  exposes Enabled / Position / Title / Icon (notification badge configurable
  on Last Reboot and Storage Used only), plus
  six fully-configurable shortcut slots
- Classic on-prem **Group Policy** deployment (Central Store + GPSI for
  the MSI, Administrative Templates for configuration)
- Warning thresholds (`StorageLimit`, `RebootWarningDays`)

## Localization

TrayLight supports multiple languages out of the box. On startup it
**auto-detects the Windows display language** (via
`CultureInfo.CurrentUICulture`) and shows **all UI text** — tile titles, status
messages, menu items and dialogs — in the matching language. No manual language
switch is required.

Translations are plain **JSON files** loaded at runtime from the `Languages`
folder next to the executable (`C:\Program Files\TrayLight\Languages\`). There
are no compiled `.resx` satellite assemblies, so a new language is just a file
drop — **no rebuild required**.

**Built-in languages:**

| Language | File      | Status |
|----------|-----------|--------|
| English  | `en.json` | Default / reference / fallback |
| German   | `de.json` | Full |
| French   | `fr.json` | Full |

### How resolution works

`en.json` is **always** loaded first as the base, then the file matching the
Windows display language is overlaid on top. Any key missing from a translation
therefore falls back to English automatically (never blank). The match order is:

1. **Exact culture file** — e.g. `de-AT.json`
2. **Language file** — e.g. `de.json`
3. **English** — `en.json` (the neutral base)

Regional variants resolve to their base language (e.g. `de-AT` → `de.json`,
`fr-CA` → `fr.json`). If the `Languages` folder is missing or a file is corrupt,
TrayLight falls back to an embedded copy of `en.json`, logs a warning and keeps
running.

### Three ways to get a language

**1. Use a built-in language (EN / DE / FR).** Nothing to do — the app picks the
right one from the Windows display language.

**2. Drop a custom JSON file — no rebuild needed.** To add e.g. Dutch, copy
`en.json` to `nl.json`, translate the values, place it in
`C:\Program Files\TrayLight\Languages\`, and restart TrayLight. That's it. This
is fully deployable at scale via a script or an **Intune Win32 app** (see
[INTUNE-DEPLOYMENT.md](docs/INTUNE-DEPLOYMENT.md#3e-deploying-custom-language-files)).
Only keys you translate need to be present; anything omitted falls back to
English.

Example `nl.json` (partial — untranslated keys fall back to English):

```json
{
  "TileComputerName": "Computernaam",
  "TileNetwork": "Netwerk",
  "QuickActions": "Snelle acties",
  "MenuQuit": "Afsluiten"
}
```

**3. Contribute a translation via PR.** Add a new `<language>.json` under
`src/TrayLight/Languages/` (copy `en.json`, keep the keys, translate the
values) and open a pull request so the language ships built-in for everyone. Use
`en.json` as the complete reference key list.

> **Tip:** ADMX policy values still override any localized string. If you only
> need to rename a few tiles/labels for one language, you can also set them via
> the **Intune Settings Catalog** (ADMX) without touching a JSON file — the
> policy value always wins.

## Author

Created by **[headsinthecloud.blog](https://headsinthecloud.blog) - Daniel Fraubaum**
- Blog: <https://headsinthecloud.blog>
- GitHub: <https://github.com/daniel-fraubaum>

## License

Released under the [MIT License](LICENSE).
