# Minch for Windows

A native Windows 11 client for [TorBox](https://torbox.app), built with WinUI 3 and the Windows App SDK.
This is the Windows sibling of the macOS SwiftUI app. The two are separate codebases on purpose, so each can feel at home on its own OS.

> **Status:** early. The code was written without a Windows machine and has not been compiled or run yet.
> CI on `windows-latest` produces the first real build.

## Features

- **Library** of torrents and web downloads, split into Active, Downloaded, Videos, Audio and Recent, with live progress, speeds, ETA and swarm info
- **Add** magnets, web links (hosters are checked against TorBox's list) or `.torrent` files. You can type, paste, drag and drop, press Ctrl+O, or set Minch as your `magnet:` handler. A cached badge and a cache-only option are included.
- **Download** to your PC with parallel range requests. Reveal finished files in Explorer.
- **Stream** in a built-in player: HLS via TorBox, with subtitle, audio and quality pickers, saved resume position, and optional scrobbling. Or hand off to VLC, mpv, MPC-HC or PotPlayer.
- **Manage** transfers: favorites, tags, rename, reannounce, stop seeding, delete
- **Command palette** (Ctrl+K) for fuzzy-searching actions, sections, transfers and files
- **Account** page with plan, stats and subscriptions. **Settings** covers TorBox preferences (seeding, zipped downloads and more) plus local preferences.
- Native Windows 11 look: Mica, a custom title bar, NavigationView, a tray icon with close-to-tray, toast notifications, light and dark themes, single-instance behaviour

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+K | Command palette |
| Ctrl+N | Focus the add box |
| Ctrl+O | Open a `.torrent` file |
| Ctrl+F | Search |
| Ctrl+R / F5 | Refresh |

## Building

Requirements: Windows 11 (build 22000+) and the **.NET 10 SDK**.
For XAML editing, use Visual Studio 2022 17.14+ or 2026 with the *WinUI application development* workload.

```powershell
dotnet test tests/Minch.Core.Tests
dotnet build src/Minch.App -p:Platform=x64
dotnet run --project src/Minch.App -p:Platform=x64

# self-contained, unpackaged build
dotnet publish src/Minch.App -c Release -p:Platform=x64 -r win-x64 -o publish/win-x64
```

Swap in `ARM64` / `win-arm64` for Snapdragon PCs. The output folder is portable: copy it anywhere and run `Minch.exe`.

## Where data lives

| What | Where |
|---|---|
| API key | Windows Credential Locker (resource `Minch`) |
| Settings | `%LOCALAPPDATA%\Minch\settings.json` |
| Transfer cache, playback positions | `%LOCALAPPDATA%\Minch\minch.db` (SQLite) |
| Downloads | `%USERPROFILE%\Downloads\Minch` by default, configurable |

The magnet handler is registered per user under `HKCU\Software\Classes\magnet`. Turning it off in Settings removes the registration, but only if Minch still owns it.

## Layout

```
src/Minch.Core        API client, sync, SQLite store, downloads, formatting (plain .NET, no Windows dependencies)
src/Minch.ViewModels  MVVM view models (CommunityToolkit.Mvvm), UI-framework agnostic
src/Minch.App         WinUI 3 app: windows, pages, Windows-specific services
tests/                xUnit tests for Core and ViewModels
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for more.
