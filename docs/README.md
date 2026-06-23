# TrayLight

A modern Windows System Tray (notification area) application built with WPF and .NET 10.

## Features

- Lives as a `NotifyIcon` in the Windows taskbar notification area.
- **Left-click** → opens an Action-Center–style flyout positioned just above the tray icon.
- **Right-click** → context menu (`About`, `Refresh`, `Quit`).
- MVVM architecture using [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm).
- Dependency injection via `Microsoft.Extensions.DependencyInjection`.
- **All configuration is read from the Windows registry** under
  `HKLM\SOFTWARE\Policies\TrayLight\` (deployed via Intune Settings Catalog
  or Group Policy / ADMX). See [config/admx/README.md](../config/admx/README.md).
- Single-instance enforcement using a named `Mutex`.
- Auto-start at logon via an HKLM `Run` key written by the MSI installer.
- MSI packaging produced by the WiX v5 project [`src/TrayLight.Installer`](../src/TrayLight.Installer/).

## Project Layout

```
TrayLight/
├── TrayLight.sln
├── src/
│   ├── TrayLight/                     # Main WPF application (.NET 10)
│   │   ├── App.xaml(.cs)
│   │   ├── Models/
│   │   ├── ViewModels/
│   │   ├── Views/
│   │   ├── Services/
│   │   └── Helpers/
│   └── TrayLight.Installer/           # WiX v5 MSI installer project
│       ├── TrayLight.Installer.wixproj
│       └── Package.wxs
├── config/
│   └── admx/                          # ADMX/ADML for GPO / Intune Settings Catalog
├── tests/
│   └── TrayLight.Tests/
└── docs/
    └── README.md
```

## Build & Run

Requires the .NET 10 SDK (with the *Windows Desktop* workload).

```pwsh
dotnet restore TrayLight.sln
dotnet build   TrayLight.sln -c Debug
dotnet run --project src/TrayLight/TrayLight.csproj
```

## Build the MSI

```pwsh
dotnet build src/TrayLight.Installer/TrayLight.Installer.wixproj -c Release
# → src/TrayLight.Installer/bin/x64/Release/TrayLight.msi
```

The WiX SDK is restored automatically via `WixToolset.Sdk` — no Visual
Studio extension or external tool is required.

## Auto-start

The installer writes
`HKLM\Software\Microsoft\Windows\CurrentVersion\Run\TrayLight =
"C:\Program Files\TrayLight\TrayLight.exe"`, so TrayLight launches at every
user logon. Uninstalling the MSI removes the entry.

## Configuration

All settings live under `HKLM\SOFTWARE\Policies\TrayLight\`. See
[config/admx/README.md](../config/admx/README.md) for the full registry layout
and the supplied ADMX/ADML for Group Policy / Intune Settings Catalog
deployment. Missing values fall back to the in-app defaults; the registry is
re-read once per minute so MDM policy refreshes take effect without a restart.
