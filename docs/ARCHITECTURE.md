# Architecture

Minch for Windows has three layers. Only the top one knows about Windows.

```
┌──────────────────────────────────────────────┐
│ Minch.App (WinUI 3, net10.0-windows)         │  windows, pages, XAML, tray, toasts,
│   Views/  Services/  Helpers/                │  Credential Locker, registry, pickers
├──────────────────────────────────────────────┤
│ Minch.ViewModels (net10.0)                   │  ObservableObjects + RelayCommands,
│   Session, Library, Player, Settings, …      │  no XAML types
├──────────────────────────────────────────────┤
│ Minch.Core (net10.0)                         │  TorBox API, SQLite store, sync,
│   Api  Persistence  Sync  Downloads  …       │  downloads, formatting
└──────────────────────────────────────────────┘
```

Core and ViewModels are plain `net10.0`, so `dotnet test` runs without the Windows App SDK.

## Core

- **`Api/TorBoxClient`**: a thin HTTP client over the `{success, detail, error, data}` envelope.
  - Failures are mapped to `ApiException` with an `ApiErrorKind`: Auth, Quota, Validation, Transient, Network or Decoding.
  - Web downloads get a `webdl:` ID prefix so they live in the same ID space as torrents.
  - File IDs are `{transferId}:{fileId}`.
- **`Persistence/TransferStore`**: a SQLite cache of transfers and files. It holds local-only state (favorites, tags, hidden, local path, playback position) and raises `Changed`.
- **`Sync/SyncEngine` + `PollingScheduler`**: merge remote lists into the store. Polling is faster while anything is active and backs off on errors or rate limits.
- **`Downloads/DownloadManager`**:
  - Resolves `requestdl` links and downloads to `{folder}\{transfer}\{path}`.
  - Uses `RangeDownloadAccelerator`, which plans parallel `Range` requests with `RangePlanner`.
  - Writes to a partial file and moves it into place when done. Sanitizes names for Windows (`FileNameSanitizer`).
- **`Media/MediaCapability`**: decides whether a file plays in the built-in player (Media Foundation), streams, or prefers an external player.
- **`Settings`**: `AppSettings` (a record) persisted by `JsonSettingsStore`.
- **`Updates/UpdateChecker`**: checks GitHub Releases for this repo.
- **`Services/Abstractions`**: the interfaces the app implements. These cover secret store, UI dispatcher, notifier, clipboard, shell, external players and protocol registrar.

## ViewModels

- **`SessionViewModel`** is the root. It owns:
  - Sign-in state and the banners.
  - Polling.
  - The child VMs: `LibraryViewModel`, `SettingsViewModel`, `AccountViewModel` and `CommandPaletteViewModel`.
- **`LibraryViewModel`**:
  - Rebuilds the visible list from the store. Rebuilds are coalesced on the dispatcher.
  - Diffs the list into a stable `ObservableCollection`, so expanded rows keep their state.
  - Runs the add bar: classification, debounced cached check, and `.torrent` loading.
- **`PlayerViewModel`** prepares a local file or a TorBox HLS stream. Changing tracks re-creates the stream.
- All VMs reach platform features through `AppServices`, so they can be tested without WinUI.

## App

- **`Program.Main`**: custom entry point (`DISABLE_XAML_GENERATED_MAIN`) that enforces a single instance through `AppInstance.FindOrRegisterForKey`. A second launch, such as clicking a magnet link, redirects its arguments to the running instance and exits.
- **`MainWindow`**:
  - Mica, the `TitleBar` control with search and the palette button, and keyboard accelerators.
  - Window placement is persisted.
  - With close-to-tray on, closing the window hides it.
- **`Views/ShellView`**: a `NavigationView` that hosts `LibraryPage`, `AccountPage` and `SettingsPage` in a `Frame`, plus the InfoBar banners.
- **`Views/PlayerWindow`**: a separate window with `MediaPlayerElement`. HLS goes through `AdaptiveMediaSource`. The window saves the resume position periodically.
- **`Services/`**: the Windows implementations of the Core abstractions.
  - `PasswordVault`, `AppNotificationManager`, H.NotifyIcon tray, registry `magnet:` handler, clipboard.
  - Explorer `/select`, and external-player discovery through known paths, App Paths and PATH.

## Conventions

- `x:Bind` everywhere. Logic in bindings goes through static helpers in `Helpers/Bind.cs`, because WinUI's x:Bind function syntax is limited.
- UI work is marshalled through `IUiDispatcher` (backed by `DispatcherQueue`). Core never touches UI threads.
- Packaging is unpackaged and self-contained (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`). There is no MSIX, so the output folder runs anywhere.
