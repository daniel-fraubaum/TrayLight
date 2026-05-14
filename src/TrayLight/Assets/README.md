# Assets

Files shipped with the application:

| File               | Purpose                                                              |
| ------------------ | -------------------------------------------------------------------- |
| `app.ico`          | MSI Add/Remove-Programs icon and Start-Menu shortcut icon.           |
| `app-normal.ico`   | Tray icon shown when no info tile is reporting a warning.            |
| `app-warning.ico`  | Tray icon shown when at least one info tile reports a warning.       |
| `default-logo.png` | Header logo shown until the customer overrides it via `Branding\Logo`. |

All icons should be Windows `.ico` files containing 16x16, 32x32, 48x48 and
256x256 frames so Windows picks the right resolution for the tray, the
Start-Menu and Add/Remove Programs.

To replace any of them, drop a file with the same name into this folder
before building the MSI. To convert a PNG to a multi-resolution ICO with
ImageMagick:

```pwsh
magick convert logo.png -define icon:auto-resize=16,32,48,256 app.ico
```
