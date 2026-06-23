# TrayLight Intune Deployment

A 15-minute guide for IT administrators who want to roll out TrayLight to
their managed Windows devices via **Microsoft Intune** (or classic Group
Policy). All configuration is pushed through the Windows registry under
`HKLM\SOFTWARE\Policies\TrayLight\` using the supplied ADMX template.

## 1. Download the MSI

Grab the latest signed MSI from the GitHub Releases page:

- **Releases:** <https://github.com/daniel-fraubaum/TrayLight/releases>
- **Latest:** <https://github.com/daniel-fraubaum/TrayLight/releases/latest>
- **Asset name:** `TrayLight-vX.Y.Z.msi` (≈ 70 MB, self-contained — no .NET
  runtime required on the target device)

While you're on the release page, also download the two Group Policy
template files for use in step 3:

- `TrayLight.admx`
- `TrayLight.adml`

> If they're not attached to the release, fetch them from the repo at
> [`config/admx/TrayLight.admx`](../config/admx/TrayLight.admx) and
> [`config/admx/en-US/TrayLight.adml`](../config/admx/en-US/TrayLight.adml).

## 2. Deploy the MSI via Intune

1. **Apps ▸ Windows ▸ Add ▸ Line-of-business app** → upload
   `TrayLight-vX.Y.Z.msi`.
2. **App information**:
   - **Publisher:** `headsinthecloud.blog`
   - **Command-line arguments:** *(leave blank — `/qn` is implied for LOB)*
   - **Ignore app version:** *No* (so upgrades replace the previous version
     in place).
   - **Logo:** upload a 256×256 PNG of your choice. This icon appears in
     the **Company Portal**, in the Intune admin center app list, and in
     any toast Intune raises for this app. It is purely cosmetic and is
     **not** the in-app TrayLight branding (configure that separately
     via the `Branding\Logo` policy — see [section 3b](#3b-customer-logo)).
3. **Assignments** → *Required* for the device groups that should run
   TrayLight.

That's it for the install. LOB MSI deployments run silently by default;
no command-line arguments are needed.

### What the install does

- Installs to `C:\Program Files\TrayLight\`.
- Writes `HKLM\Software\Microsoft\Windows\CurrentVersion\Run\TrayLight`
  so TrayLight launches automatically on every user logon.
- Registers an uninstall entry under
  `Uninstall\{2493D886-5C60-45E0-95BE-7F450BE2C2FC}` with
  `Publisher = headsinthecloud.blog`.
- Removes the Run key on uninstall. Major-version upgrades replace the
  previous install in place.

To suppress auto-start without uninstalling, push `Behavior\AutoStart = 0`
(REG_DWORD) via the *Auto-start at logon* ADMX policy — TrayLight then
exits immediately when the Run key launches it.

### Manual install / uninstall (for reference)

If you ever need to invoke the MSI by hand (RMM, scripted deployment,
troubleshooting):

```pwsh
# install
msiexec /i TrayLight-vX.Y.Z.msi /qn

# uninstall
msiexec /x {2493D886-5C60-45E0-95BE-7F450BE2C2FC} /qn
```

### Warning thresholds

TrayLight only raises a tray-icon warning for two scenarios:

| Trigger          | Registry value                                                                       | Default |
| ---------------- | ------------------------------------------------------------------------------------ | ------- |
| Storage full     | `HKLM\SOFTWARE\Policies\TrayLight\InfoItems\StorageUsed\StorageLimit` (REG_DWORD %)  | `90`    |
| Reboot overdue   | `HKLM\SOFTWARE\Policies\TrayLight\Behavior\RebootWarningDays` (REG_DWORD days)       | `7`     |

Set `RebootWarningDays = 0` to disable the reboot warning entirely. All
other tiles (Computer Name, OS Version, Network, Identity, Intune Sync,
Serial Number) are informational and never raise warnings.

### Tray icon visibility

After installation, the TrayLight icon may appear in the taskbar overflow
area (the `^` arrow). To keep it permanently visible, drag the icon from
the overflow onto the main taskbar. This is a one-time action per user —
Windows remembers the preference. This is a Windows limitation that
affects all system tray applications and cannot be controlled via policy.
Consider informing end users about this step via email or your internal IT
knowledge base, especially during initial rollout.

## 3. Configure TrayLight via ADMX

TrayLight is configured exclusively through ADMX-based policies. The
template is shipped with each release (and lives in
[`config/admx/`](../config/admx/) in this repo).

### 3a. Imported Administrative Template

1. Go to **Devices ▸ Configuration ▸ Import ADMX** and upload:
   - `TrayLight.admx`
   - `TrayLight.adml` (the `en-US` localization)
2. Create a new **Imported Administrative templates** profile. The
   template exposes the full configuration surface:

   - **Branding** — Title, AccentColor, Logo, TrayIcon, CompanyName
   - **Behavior** — AutoStart, RefreshIntervalMinutes, ShowWelcomeScreen,
     NotifyOnUpdates, RebootWarningDays
   - **Footer** — Text, ShowLastSync, InfoText
   - **Logging** — Event log, file log, retention, minimum level
   - **Information Tiles** — one policy per tile (`ComputerName`,
     `OsVersion`, `LastReboot`, `StorageUsed`, `NetworkInfo`,
     `SerialNumber`, `IntuneSync`). Each policy exposes Enabled (0/1),
     Position (0–7), Title override and Icon (Segoe Fluent Icons code).
     Only `LastReboot` and `StorageUsed` can raise warnings, so
     `ShowNotificationBadge` is exposed on those two tiles. `StorageUsed`
     adds a `StorageLimit` (% threshold) field.
   - **Shortcuts** — a master *Allow shortcut tiles* toggle plus six
     individually configurable slots (*Shortcut slot 1* … *Shortcut slot 6*).
     Each slot exposes Title, Subtitle, ActionType
     (`url` / `app` / `command`), Action, Icon and
     Position. The **Action** field supports dynamic `{{Placeholder}}`
     tokens (e.g. `{{ComputerName}}`, `{{OsVersion}}`, `{{SerialNumber}}`,
     `{{IntuneSync}}`) that are replaced with live device values when the
     button is clicked — see [3d. Dynamic placeholders](#3d-dynamic-placeholders-in-shortcut-actions).

3. Assign the profile to the same device groups as the MSI.

> **Localized UI vs. ADMX text.** TrayLight auto-detects the Windows display
> language and shows its built-in UI strings in English, German or French
> accordingly. Any text you configure through ADMX (tile titles, footer text,
> info text, shortcut titles, branding title, …) **always overrides** the
> auto-detected language — the localized strings are only the defaults used when
> no ADMX value is set.
>
> **ADMX overrides as a localization mechanism.** Because every visible string
> can be overridden, ADMX doubles as a way to localize TrayLight into a language
> that isn't built in. If your organization needs a language other than English,
> German or French, simply set all visible strings (tile titles, header/branding
> title, shortcut labels, footer and info text) to that language via the Intune
> Settings Catalog — no code change, PR or new release required.

### 3b. Info text block

The `Footer\InfoText` policy (registry: `HKLM\SOFTWARE\Policies\TrayLight\Footer\InfoText`,
type `REG_SZ`) renders a free-text section between the Quick Actions area
and the popup footer. Useful for office hours, helpdesk contact details,
on-call info, or any short notice you want every user to see at a glance.

The text is single-line in the registry but supports a tiny inline markup:

| Token | Meaning                                       |
| ----- | --------------------------------------------- |
| `*…*` | Wraps **bold** text (asterisks must be paired) |
| `\|`  | Line break                                     |
| `\|\|` | Blank line between blocks                     |

If `Footer\InfoText` is empty or unset, the section is hidden completely
and no extra space is reserved in the popup.

**Example value** (paste exactly into the Administrative Templates editor):

```
*IT-Office Hours* | Mon-Thu: 07:00-17:00 | Fri: 07:00-15:00 || *IT-Hotline* | +43 1 234 5678 || *Emergency outside office hours:* | Only incidents critical to production
```

Renders as:

> **IT-Office Hours**
> Mon-Thu: 07:00-17:00
> Fri: 07:00-15:00
>
> **IT-Hotline**
> +43 1 234 5678
>
> **Emergency outside office hours:**
> Only incidents critical to production

Maximum length is 1023 characters (Intune's ADMX text-field limit). The block is scrollable above ~120 px,
so very long text won't push the popup off-screen.

### 3c. Customer logo

The `Branding\Logo` policy accepts a **URL** or a **local file path**.

- **Option A — Logo URL (recommended).** Host your PNG anywhere reachable
  from the device (CDN, blob storage, intranet web server) and set
  `Branding\Logo = https://corp.example.com/logo.png` in your
  Administrative Template profile. TrayLight downloads it to
  `%ProgramData%\TrayLight\Cache\logo.png` on first launch and re-checks
  the URL once a day. If the URL is unreachable the cached copy is reused.
- **Option B — Local file path.** Push the PNG to devices via any channel
  you already use (Intune *Win32 app* file payload, file-share copy,
  scripted download, …) and set `Branding\Logo` to its absolute path,
  for example `C:\ProgramData\Corp\TrayLightLogo.png`. TrayLight reads
  the file directly — no download attempt is made.

If `Branding\Logo` is empty or unset, TrayLight falls back to the built-in
default icon shipped inside the executable.

### 3d. Dynamic placeholders in shortcut actions

A shortcut **Action** can embed `{{Placeholder}}` tokens that TrayLight
replaces with **live device values at the moment the button is clicked**
(not when the policy is read), so the substituted data is always current.

| Placeholder        | Resolves to                                            |
| ------------------ | ------------------------------------------------------ |
| `{{ComputerName}}` | Device name                                            |
| `{{OsVersion}}`    | OS edition + version (e.g. `Win 11 Ent 25H2`)          |
| `{{LastReboot}}`   | Relative uptime (e.g. `4h 11m ago`)                    |
| `{{Storage}}`      | Storage usage (e.g. `66% used`)                        |
| `{{SerialNumber}}` | Hardware serial number                                 |
| `{{IntuneSync}}`   | Last Intune sync (e.g. `13m ago` or `Not enrolled`)    |
| `{{Network}}`      | Network type + IP (e.g. `Ethernet 192.168.199.52`)     |
| `{{UserName}}`     | Current logged-in user                                 |
| `{{DomainName}}`   | AD domain or workgroup name                            |

- For `url` actions starting with `mailto:` or `https://`, values are
  automatically URL-encoded so spaces and special characters don't break the
  link. `app` / `command` actions receive the raw value.
- Unresolved tokens (e.g. a disabled tile) become `N/A`.

**Example** — a pre-filled IT support mail (set as the *Action* of a shortcut
slot with ActionType `url`):

```
mailto:it@example.com?subject=Support - {{ComputerName}}&body=Device: {{ComputerName}}%0AOS: {{OsVersion}}%0ASerial: {{SerialNumber}}%0AIntune Sync: {{IntuneSync}}
```

## 4. Verify on a target device

```pwsh
# Confirm policy values are present
reg query 'HKLM\SOFTWARE\Policies\TrayLight' /s

# Confirm the MSI is installed
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' |
  Where-Object DisplayName -eq 'TrayLight' |
  Select-Object DisplayName, DisplayVersion, Publisher, InstallLocation
```

TrayLight re-reads the registry once per minute, so policy changes take
effect without restarting the app.

## 5. Classic Group Policy (AD / on-prem)

The same ADMX/ADML files work in a traditional Active Directory environment
without any modification — Intune is just one consumer of the standard
Group Policy template format.

### 5a. Install the templates in the Central Store

On a domain controller (or any workstation with RSAT), copy the templates
into the SYSVOL central store so every domain admin sees them:

```pwsh
$store = "\\$env:USERDNSDOMAIN\SYSVOL\$env:USERDNSDOMAIN\Policies\PolicyDefinitions"
New-Item -ItemType Directory -Force -Path "$store\en-US" | Out-Null
Copy-Item TrayLight.admx        "$store\TrayLight.admx"        -Force
Copy-Item TrayLight.adml        "$store\en-US\TrayLight.adml"  -Force
```

If your domain has no central store yet, copy the same files into
`C:\Windows\PolicyDefinitions\` on each admin workstation instead.

### 5b. Deploy the MSI via Group Policy Software Installation

1. Place `TrayLight-vX.Y.Z.msi` on a UNC share readable by *Domain Computers*
   (e.g. `\\fileserver\software$\TrayLight\`).
2. **GPMC ▸** new GPO linked to the target OU **▸ Computer Configuration ▸
   Policies ▸ Software Settings ▸ Software installation ▸ New ▸ Package**.
3. Choose **Assigned**. The MSI installs on the next reboot.
4. *(Optional)* Run a startup script that calls `msiexec /i \\...\TrayLight.msi /qn`
   if you prefer scripted rollout over GPSI.

### 5c. Configure TrayLight via GPO

1. **GPMC ▸** edit the GPO **▸ Computer Configuration ▸ Policies ▸
   Administrative Templates ▸ TrayLight**. All categories (Branding,
   Behavior, Footer, Logging, Information Tiles, Shortcuts) appear with
   the same UI as in Intune.
2. Configure the policies you need and save.
3. On clients run `gpupdate /force` (or wait for the next refresh cycle).
   TrayLight re-reads `HKLM\SOFTWARE\Policies\TrayLight\` within one
   minute, so changes apply without restarting the app.

Because TrayLight reads only from `HKLM\SOFTWARE\Policies\…` — the
"true policies" hive owned by the Group Policy engine — values are
removed automatically when the GPO is unlinked. There is no registry
tattooing to clean up.

## 6. Intune sync trigger

The *Intune Sync* tile shows the time elapsed since the last MDM check-in.

- **Enrollment detection**: `HKLM\SOFTWARE\Microsoft\Enrollments\<EnrollmentGuid>\ProviderID = MS DM Server`.
- **Last sync time**: `HKLM\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\<EnrollmentGuid>\Protected\ConnInfo\ServerLastSuccessTime`
  (REG_SZ, ISO 8601 basic format `yyyyMMddTHHmmssZ`). This is the same
  timestamp Windows Settings → *Accounts* → *Access work or school* → *Info*
  surfaces as "Last sync was successful". The newest value across all
  enrollments wins.

Clicking the tile launches `intunemanagementextension://syncapp`, which is
the same URI the Company Portal uses to ask the Intune Management
Extension to sync immediately.

States:

- *Not enrolled* — no MDM enrollment with `ProviderID = MS DM Server` was
  found. Tile is non-clickable.
- *Unknown* — device is enrolled but `ServerLastSuccessTime` could not be
  read (e.g. the `Protected\ConnInfo` key is ACL-restricted on this build).
  Tile remains clickable so the user can force a sync.
- *just now* / *X minutes ago* / *Xh Ym ago* / *X days ago* — last sync
  formatted with the same relative-time rules as the *Last refreshed*
  footer line.

Provider activity is logged to the *TrayLight* Application event-log source
under event id `2000` (`InfoItemUpdated`) with the detected enrollment
state, last-sync timestamp, and source.
