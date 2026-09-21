# Wallup

> Your wallpaper is your to-do list. Right-click, drop a task, get on with your day.

Wallup renders your tasks onto the desktop wallpaper, behind the icons, and lets you add
one by right-clicking empty desktop. See [docs/IDEA.md](docs/IDEA.md) for the product
thinking.

**Status: v0.1 in progress.** The desktop-layer spike is done and passing on Windows 11
build 26200. See [docs/SPIKE-WORKERW.md](docs/SPIKE-WORKERW.md) for what it proved.

## Stack

C# / .NET 9 / WPF, Windows-only. The entire product risk is Win32 desktop-layer surgery,
which is why the stack is the one with first-class P/Invoke and a readable reference
implementation ([Lively Wallpaper](https://github.com/rocksdanister/lively)) to compare
against. Cross-platform frameworks buy nothing here: the desktop-layer mechanism has to be
rewritten per OS regardless.

## Architecture

The wallpaper layer cannot receive mouse input, because `SHELLDLL_DefView` covers the
whole desktop and eats every click before it reaches anything behind it. So the gesture is
split across two windows plus a hook:

| Piece | File | Job |
| --- | --- | --- |
| Ambient layer | `Views/AmbientWindow.xaml` | Render-only, parented into the shell, painted behind the icons. Never takes input. |
| Task box | `Views/TaskBoxWindow.xaml` | Normal top-level window. Appears at the cursor, takes focus, closes when it loses it. |
| Gesture | `Interop/DesktopRightClickHook.cs` | Global `WH_MOUSE_LL` hook. Detects a right-click on empty desktop and swallows it. |
| Shell surgery | `Interop/DesktopLayer.cs` | Finds the wallpaper host and slots us in behind the icons. |

Both windows bind to the same `TaskListViewModel`, which is why an edit in the box shows
up on the wallpaper with nothing being regenerated.

### The right-click conflict

Plain right-click on empty desktop opens Wallup and the Windows context menu is
suppressed. **Hold Shift to get the normal Windows menu.** That escape hatch matters: a
global hook that eats right-clicks with no way out is hostile.

## Running it

```
dotnet build
dotnet run --project src/Wallup
```

Two diagnostic modes, because a window parented into the wrong place is invisible rather
than broken — it just never appears, with no error anywhere:

```
Wallup.exe --diagnose    # dump the shell window tree and exit
Wallup.exe --selftest    # attach for real, report where we landed, exit
```

`--selftest` is the one that matters. It reports the z-order of the ambient window against
`SHELLDLL_DefView` and prints one of:

- `OK - behind the desktop icons.`
- `IN FRONT - on the wallpaper layer but painted over the icons.`
- `FAIL - not parented into the shell at all.`

Logs go to `%LOCALAPPDATA%\Wallup\logs\wallup.log`. Tasks and settings live in
`%APPDATA%\Wallup\`.

## Where the data lives

| Path | Contents |
| --- | --- |
| `%APPDATA%\Wallup\tasks.json` | The task list. Written via temp-file-and-replace. |
| `%APPDATA%\Wallup\settings.json` | Opacity, text size, box position. |
| `%LOCALAPPDATA%\Wallup\logs\wallup.log` | Shell probe results and attach outcomes. |

## v0.1 checklist

- [x] Task box renders on the wallpaper layer, behind desktop icons
- [x] Right-click desktop opens a task box at the cursor
- [x] Add, edit, check off, delete a task
- [x] Tasks persist locally between sessions
- [x] Basic customization: opacity, font size, box position
- [ ] Survive an Explorer restart without a manual reattach
- [ ] Multi-monitor placement

## Known gaps

- **Explorer restart orphans the ambient window.** Tray menu has a manual *Reattach to
  wallpaper*. Watching for the shell's `TaskbarCreated` message is the real fix.
- **Right-clicking a desktop icon also opens Wallup.** The hit test sees `SysListView32`
  for both empty space and icons; it needs a `LVM_HITTEST` to tell them apart.
- **Single monitor only.** Placement is computed against the Progman rect.
