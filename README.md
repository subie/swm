# swm — scrolling window manager for Windows

A keyboard-driven, single-row scrolling tiling window manager for Windows 10/11. Inspired by [niri](https://github.com/YaLTeR/niri) and xmonad: every window lives in one horizontal "reel" that scrolls under your monitors, with one window always focused. Multi-monitor (windows can straddle bezels), multi-virtual-desktop, AutoHotkey-driven keybindings, dmenu-style search.

## Components

| Project | What it is |
|---|---|
| `ScrollingWM/` | The daemon. Hooks Win32 events, owns the layout, exposes a named-pipe IPC. |
| `swmctl/` | Thin CLI client that sends commands over the pipe (`swmctl focus right`, etc.). |
| `swmsearch/` | WinForms dmenu-style app launcher. |
| `swm.ahk` | AutoHotkey v2 script binding hotkeys to `swmctl`. |
| `ScrollingWM.Tests/` | xUnit tests for layout, commands, and pure helpers. |

## Requirements

- **Windows 10 (1903+)** or **Windows 11**. Win11 22H2+ enables the focus border tint.
- **.NET 10 SDK** (Windows). Install with winget:
  ```powershell
  winget install Microsoft.DotNet.SDK.10
  ```
  (May take ~10 minutes silently — be patient.)
- **AutoHotkey v2.0+** for keybindings:
  ```powershell
  winget install AutoHotkey.AutoHotkey
  ```

NuGet packages (`Tomlyn`, `System.Drawing.Common`) are restored automatically by `dotnet build`. No other native dependencies.

## Build

```powershell
git clone https://github.com/<you>/swm.git
cd swm
dotnet build ScrollingWM.slnx -c Release
```

Run tests:

```powershell
dotnet test ScrollingWM.Tests
```

### Publish self-contained binaries (recommended for daily use)

This produces standalone `.exe`s that don't need the .NET SDK installed at runtime — useful for autostart or for sharing.

```powershell
dotnet publish ScrollingWM -c Release -r win-x64 --self-contained -o publish\daemon
dotnet publish swmctl      -c Release -r win-x64 --self-contained -o publish\swmctl
dotnet publish swmsearch   -c Release -r win-x64 --self-contained -o publish\swmsearch
```

## Configure

Copy the bundled sample to your config location:

```powershell
mkdir $HOME\.swm -ErrorAction SilentlyContinue
Copy-Item config.example.toml $HOME\.swm\config.toml
```

Or create `~/.swm/config.toml` (i.e. `%USERPROFILE%\.swm\config.toml`) by hand. The full sample lives at [`config.example.toml`](./config.example.toml); a minimal version:

```toml
# Hex color tints the focused window's border + title bar. Empty disables.
# Windows 11 22H2+ only.
focus_color = "#FF8C00"

# Float rules: any rule that matches floats the window on creation.
# Match fields are AND-combined. Globs: "*" wildcard, exact match otherwise.
# Each rule needs at least one of: exe, class, title.

# Standard Win32 dialog class — Save As, Open, About, etc.
[[float_rule]]
class = "#32770"

# UAC consent prompt
[[float_rule]]
exe = "consent.exe"

# UWP / WinUI host (Settings, Calculator, Store, ...). Their XAML CoreWindow
# doesn't reliably re-lay-out when the outer host is resized.
[[float_rule]]
exe = "ApplicationFrameHost.exe"

[[float_rule]]
exe = "Taskmgr.exe"
```

Non-resizable windows (those without `WS_THICKFRAME | WS_MAXIMIZEBOX`) are auto-floated regardless of rules — covers fixed-size dialogs, account pickers, and similar.

## Run

1. Start the daemon (foreground; Ctrl-C to stop and restore windows):
   ```powershell
   dotnet run --project ScrollingWM -c Release
   ```
   Or, if you ran `dotnet publish` above:
   ```powershell
   .\publish\daemon\ScrollingWM.exe
   ```
2. Run the AHK script (double-click `swm.ahk`). It auto-resolves `swmctl.exe` and `swmsearch.exe` from the standard `bin\Debug` / `bin\Release` / `publish\` locations relative to the script. Override either via the `SWMCTL_EXE` / `SWMSEARCH_EXE` environment variables.

### Autostart at logon

Drop a shortcut to `swm.ahk` in `shell:startup`. To start the daemon hidden at logon, register a Task Scheduler entry running at logon with:

```powershell
$exe = "$PWD\publish\daemon\ScrollingWM.exe"
$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-WindowStyle Hidden -Command `"Start-Process -WindowStyle Hidden '$exe'`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
Register-ScheduledTask -TaskName "swm" -Action $action -Trigger $trigger -RunLevel Highest
```

You can stop the daemon at any time with `Stop-Process -Name ScrollingWM` — it runs `Cleanup` on exit and restores tracked windows to their original positions.

## Keybindings (default)

Mod = `Ctrl+Alt`. Edit `swm.ahk` to remap.

| Keys | Action |
|---|---|
| `Mod+H` / `Mod+L` | Focus left / right |
| `Mod+,` / `Mod+.` | Focus first / last |
| `Mod+Shift+H` / `Mod+Shift+L` | Swap left / right |
| `Mod+Shift+,` / `Mod+Shift+.` | Move to start / end |
| `Mod+T` | Toggle float on focused window |
| `Mod+F` | Toggle fullscreen on focused window |
| `Mod+=` | Resize focused window to half-monitor width |
| `Mod+Enter` | Swap focused with monitor 0 slot 1 (xmonad-style "swap master") |
| `Mod+Shift+Enter` | Swap focused with monitor 1 slot 0 |
| `Mod+Space` | Open `swmsearch` launcher |

Drag a window with the mouse to place it under the cursor's tile (works for cross-monitor and cross-virtual-desktop drags too). Tab tears land where you drop them.

## Behaviors of note

- **Per-virtual-desktop strips.** Each virtual desktop has its own independent reel.
- **Mouse-follows-focus.** Focus commands warp the cursor to the focused tile's centre.
- **Drop-where-cursor-is.** Any window drop — tab tear, intra-strip drag, cross-monitor, cross-desktop — inserts at the slot the cursor is over.
- **Cleanup-onscreen guarantee.** On Ctrl-C, every tracked window is restored to the position it had when first seen by swm; if that position is no longer onscreen (monitor unplugged, etc.) it's recentered on the primary monitor.
- **Mid-drag adoption deferral.** New windows that appear while the mouse is held (tab tear) are tracked only after the drop, avoiding `SetWindowPos` races with the browser's drag loop.

## Project layout

```
ScrollingWM/
  Core/            # Pure logic: Strip, Layout, Commands, Restore, Rect
  Win32/           # P/Invoke + dispatcher: WindowOps, Monitors, Dispatcher, Applier
  Ipc/             # Named-pipe server
  Program.cs       # Entry point + main loop
swmctl/            # CLI client
swmsearch/         # Launcher
swm.ahk            # AutoHotkey bindings
swm-rescue.ps1     # Emergency: enumerate offscreen windows + bring them back
```

## Troubleshooting

- **Windows are stuck offscreen after a crash.** Run `swm-rescue.ps1` from PowerShell to recenter every visible top-level window.
- **`swmsearch` opens on the wrong monitor.** It targets the monitor of the currently focused window, not the cursor monitor. Move focus first.
- **A specific app won't tile correctly.** Add a `[[float_rule]]` matching its `exe`, `class`, or `title` (use Spy++ or `swmctl dump` to inspect tracked windows).

## License

MIT — see [`LICENSE`](./LICENSE).
