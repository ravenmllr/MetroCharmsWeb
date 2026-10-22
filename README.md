# MetroCharmsWeb

A Windows 8-style **Charms Bar** for the desktop, built with WPF and WebView2. Touch the top-right corner of your screen and a pure-black Metro panel slides in with a clock, date, weather and battery status, plus **WhatsApp Web** and **Instagram** running right inside it. Move the mouse away and it gets out of the way.

> Unofficial hobby project. Not affiliated with or endorsed by Microsoft, Meta, WhatsApp or Instagram. It only loads the official websites in an embedded browser.

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/655f2bc8-b3b6-4336-84b7-901963e4e236" />

## Features

- **Hot corner trigger**: hover the top-right corner of the screen to slide the panel in; click anywhere outside (or press `Esc`) to hide it. A low-level mouse hook is used, so it costs nothing while idle.
- **Metro look**: pure black background, square corners, flat tiles, Segoe UI typography.
- **Header widgets**: large clock, date, current weather (Open-Meteo) with automatic location, and battery percentage with estimated time left / time to full.
- **Embedded web apps (WebView2)**: WhatsApp Web and Instagram, each in its own view. Switching tabs only shows or hides a view, so pages never reload.
- **Stays logged in**: cookies and site data persist in a dedicated profile folder, so you don't have to sign in every time.
- **Windows accent color**: the active tab and the pin button follow your Windows accent color.
- **Per-site width**: each site gets its own panel width (configurable constants).
- **Battery friendly**: Instagram is suspended whenever it isn't visible (panel hidden or WhatsApp tab selected). WhatsApp keeps running in the background.
- **Pin button**: keep the panel open while you work in other windows.
- **Extras**: reload and exit buttons, single-instance guard, hidden from Alt+Tab, external links open in your default browser.

## Requirements

- Windows 10 (1809 or newer) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already included on most Windows 11 installs)

## Build and run

```bash
git clone https://github.com/ravenmllr/MetroCharmsWeb.git
cd MetroCharmsWeb
dotnet build
dotnet run
```

The only NuGet package is `Microsoft.Web.WebView2`, restored automatically. To install it manually:

```bash
dotnet add package Microsoft.Web.WebView2
```

To get a standalone build:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## Usage

| Action | Result |
| --- | --- |
| Move the mouse to the top-right corner | Panel slides in |
| Click outside the panel, or press `Esc` | Panel hides (unless pinned) |
| WhatsApp / Instagram buttons | Switch site without reloading |
| Reload button | Reload the current site |
| Pin button | Keep the panel open |
| Exit button | Quit the app completely |

## Configuration

Everything lives in `MainWindow.xaml.cs`:

```csharp
const string UrlWhatsApp  = "https://web.whatsapp.com";
const string UrlInstagram = "https://www.instagram.com";

const double WidthWhatsApp  = 800; // panel width for WhatsApp (DIP)
const double WidthInstagram = 800; // panel width for Instagram (DIP)
```

### Swapping a site (for example Instagram to TikTok)

1. In `MainWindow.xaml.cs`, change the `UrlInstagram` constant.
2. In `MainWindow.xaml`, change the `Tag` and the label text of the `IgBtn` button. The `Tag` must match the constant exactly.
3. Optionally adjust the width constant.

Some sites open third-party login popups (for example "Continue with Google"). Those open in your default browser instead of inside the panel, so prefer email, phone or QR login.

## Data and privacy

- Login sessions and site data are stored in `%APPDATA%\MetroCharms\WebView2`. Treat that folder like a logged-in browser profile and never share it. Delete it to sign out everywhere.
- The last selected site is stored in `%APPDATA%\MetroCharms\lasturl.txt`.
- The app itself makes two network requests: weather from [Open-Meteo](https://open-meteo.com/) and a city name from [BigDataCloud](https://www.bigdatacloud.net/). Both receive your approximate coordinates, obtained from the Windows Location Service. If location access is off, the weather shows "Location off".
- The mouse hook only reads pointer position and clicks to detect the hot corner and clicks outside the panel. It does not log or transmit anything.

## Notes and limitations

- The hot corner may not trigger while the mouse is over a program running as administrator, unless MetroCharmsWeb also runs as administrator (`app.manifest`, `requestedExecutionLevel`).
- Instagram is frozen while hidden, so it won't receive anything in real time until the panel is shown again. WhatsApp keeps running, but you should verify that message sounds and notifications behave the way you expect on your system.
- Weather and battery estimates depend on your hardware and Windows services, and may be unavailable on some devices (desktop PCs show no battery line).

## Project structure

```
MetroCharmsWeb/
├── App.xaml / App.xaml.cs        Single-instance startup, lifecycle
├── MainWindow.xaml               Metro UI: header, tab bar, WebView2 views
├── MainWindow.xaml.cs            Hot corner hook, animation, WebView2, weather, battery
├── app.manifest                  Execution level
└── *.csproj                      .NET 8 / WPF project, WebView2 package
```

## License

Add a license of your choice (for example MIT) before publishing.
