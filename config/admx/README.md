# TrayLight Registry Configuration

TrayLight reads **all** runtime configuration from the Windows registry under

```
HKLM\SOFTWARE\Policies\TrayLight\
```

The hive is populated via:

- **Microsoft Intune** – Settings Catalog rules pointing at the OMA-URIs below, **or**
- **Group Policy** – import `config/admx/TrayLight.admx` + `config/admx/en-US/TrayLight.adml` into your Central Store.

Missing values fall back to the in-app defaults (see `AppConfiguration.CreateDefault`). The app re-reads the registry once per minute, so policy refreshes take effect without restarting.

The ADMX template now exposes **every** field listed below — including per-tile
`Title`/`Icon`/`Position` (with `ShowNotificationBadge` available on `LastReboot`
and `StorageUsed` only) and full per-shortcut configuration.
Use the *Imported Administrative templates* flow in Intune to configure them via the
UI; raw OMA-URI values still work and are documented here for reference.

## Layout

| Sub-key                         | Value name                  | Type      | Notes                                                                |
|---------------------------------|-----------------------------|-----------|----------------------------------------------------------------------|
| `Branding`                      | `Title`                     | REG_SZ    | Header text. Default `IT Support`.                                   |
| `Branding`                      | `AccentColor`               | REG_SZ    | `#RRGGBB` or `#AARRGGBB`. Default `#0078D4`.                         |
| `Branding`                      | `Logo`                      | REG_SZ    | URL (https://...) or local path of header logo. URLs are cached.    |
| `Branding`                      | `TrayIcon`                  | REG_SZ    | Path to a custom `.ico`.                                             |
| `Branding`                      | `CompanyName`               | REG_SZ    | Shown in About / Welcome.                                            |
| `Behavior`                      | `AutoStart`                 | REG_DWORD | `0`/`1`.                                                             |
| `Behavior`                      | `RefreshIntervalMinutes`    | REG_DWORD | `0` disables.                                                        |
| `Behavior`                      | `ShowWelcomeScreen`         | REG_DWORD | `0`/`1`.                                                             |
| `Behavior`                      | `NotifyOnUpdates`           | REG_DWORD | `0`/`1`.                                                             |
| `Behavior`                      | `RebootWarningDays`         | REG_DWORD | Days of uptime before Last-Reboot tile warns. `0` disables. Default `7`. |
| `Footer`                        | `Text`                      | REG_SZ    | Custom footer line above the hardcoded `Powered by headsinthecloud.blog`. |
| `Footer`                        | `ShowLastSync`              | REG_DWORD | `0`/`1`.                                                             |
| `Footer`                        | `InfoText`                  | REG_SZ    | Free-text block between Quick Actions and footer. Markup: `*bold*`, `|` line break, `||` blank line. Max 1023 chars (Intune ADMX limit); empty hides the section. |
| `Logging`                       | `EnableEventLog`            | REG_DWORD | `0`/`1`.                                                             |
| `Logging`                       | `EnableFileLog`             | REG_DWORD | `0`/`1`.                                                             |
| `Logging`                       | `LogRetentionDays`          | REG_DWORD | Days kept.                                                           |
| `Logging`                       | `MinimumLevel`              | REG_SZ    | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`.     |
| `InfoItems\<TypeName>`          | (subkey existence)          | -        | Presence of the per-tile subkey means the tile is enabled (the GP policy state controls this). Type names: `ComputerName`, `OsVersion`, `LastReboot`, `StorageUsed`, `NetworkInfo`, `EntraIdStatus`, `SerialNumber`, `IntuneSync`. |
| `InfoItems\<TypeName>`          | `Position`                  | REG_DWORD | `-1` (hidden) or `0..7`.                                             |
| `InfoItems\<TypeName>`          | `Title`                     | REG_SZ    | Custom label override.                                               |
| `InfoItems\<TypeName>`          | `Icon`                      | REG_SZ    | Segoe Fluent Icons code, e.g. `E977`.                                |
| `InfoItems\<TypeName>`          | `ShowNotificationBadge`     | REG_DWORD | `0`/`1`. Honored only on `LastReboot` and `StorageUsed` (the only tiles that raise warnings). |
| `InfoItems\StorageUsed`         | `StorageLimit`              | REG_DWORD | Percent threshold for warning (0..100). Default `90`.                |
| `Shortcuts`                     | `AllowUserShortcuts`        | REG_DWORD | Master toggle. `0` hides every shortcut tile.                        |
| `Shortcuts\<n>`                 | `Title`                     | REG_SZ    | Required.                                                            |
| `Shortcuts\<n>`                 | `Subtitle`                  | REG_SZ    |                                                                      |
| `Shortcuts\<n>`                 | `Icon`                      | REG_SZ    | Segoe Fluent Icons code, e.g. `E8F2`.                                |
| `Shortcuts\<n>`                 | `ActionType`                | REG_SZ    | `url`, `app`, or `command`.                                          |
| `Shortcuts\<n>`                 | `Action`                    | REG_SZ    | URL / executable / command line.                                     |
| `Shortcuts\<n>`                 | `Position`                  | REG_DWORD | Sort order, `-1` hides.                                              |
| `Shortcuts\<n>`                 | `RequiresConfirmation`      | REG_DWORD | `0`/`1`.                                                             |
| `Shortcuts\<n>`                 | `ConfirmationMessage`       | REG_SZ    |                                                                      |
| `Shortcuts\<n>`                 | `SuccessMessage`            | REG_SZ    |                                                                      |

`<n>` is the slot number. The ADMX exposes slots `1` through `6`; the
registry reader accepts any numeric subkey, so additional slots can be
pushed via raw Settings Catalog OMA-URI rules if needed.
