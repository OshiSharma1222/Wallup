# Wallup

> Your wallpaper is your to-do list. Click the desktop, drop a task, get on with your day.

Left-click empty desktop, type a task, hit Enter. It drops onto the desktop as a small
glass chip you can drag anywhere, tick off, set an alarm on, or delete. See
[docs/IDEA.md](docs/IDEA.md) for the product thinking.

**Status: v0.2.** The core loop works end to end. See *What is verified* below for exactly
what has been seen working and what has not.

## Stack

C# / .NET 9 / WPF, Windows-only. The whole product risk is Win32 desktop integration,
which is why the stack is the one with first-class P/Invoke.

## Architecture

The central constraint, learned the hard way: **the wallpaper layer receives no mouse
input.** `SHELLDLL_DefView` covers the entire desktop and eats every click before it can
reach anything painted behind it. Dragging, ticking, alarms and delete buttons are all
impossible there.

So tasks are not pixels on the wallpaper. Each task is its own real window.

| Piece | File | Job |
| --- | --- | --- |
| Chip | `Views/ChipWindow.xaml` | One window per task. Drag to move, double-click to edit, tick, alarm, delete. |
| Composer | `Views/ComposerWindow.xaml` | Opens at the cursor on a desktop click. Takes one line, then gets out of the way. |
| Chip host | `Views/ChipHost.cs` | Keeps chip windows in sync with the task list and fires alarms. |
| Gesture | `Interop/DesktopClickHook.cs` | Global `WH_MOUSE_LL` hook. Swallows a left-click on empty desktop. |
| Desktop layer | `Interop/DesktopWindow.cs` | Pins chips just above Progman: over the wallpaper, under real windows. |
| Glass | `Interop/Glass.cs` | Windows 11 acrylic via DWM. |
| Adaptive tone | `Views/AdaptiveGlass.cs` | Samples the wallpaper behind each window and picks dark or light glass. |
| Sampler | `Interop/WallpaperSampler.cs` | Mean Rec. 709 luma of the wallpaper under a screen rectangle. |

### Adaptive glass

Each window samples the wallpaper behind its own rectangle and merges either
`Views/GlassDark.xaml` or `Views/GlassLight.xaml` into its resources, so a chip on a dark
patch is smoked with white text while one on a bright patch is frosted with dark ink.
Every colour in `Theme.xaml` is a `DynamicResource` for exactly this reason - a
`StaticResource` would bake in whichever palette happened to load first.

The sampler reads the wallpaper bitmap rather than grabbing the screen, because by the
time a chip asks the question it is already on screen and would sample itself.

### Three traps worth knowing

- **Never pin to `HWND_BOTTOM`.** The bottom of the z-order is *below* Progman, which puts
  the window under the desktop and makes it invisible. Insert directly above Progman
  instead, and re-assert it on `WM_WINDOWPOSCHANGING`, because clicking a window normally
  raises it.
- **Acrylic needs a non-layered window.** `AllowsTransparency="True"` makes a WPF window
  layered, and DWM refuses to draw a backdrop behind a layered window. These windows use
  `AllowsTransparency="False"` with a transparent background instead. This is the usual
  reason acrylic silently does nothing.
- **Swallow both halves of the click.** Letting the button-up through hands focus back to
  the shell, which deactivates the composer the instant it opens; it flashes and vanishes,
  and the *next* click appears to open it late.

### The click conflict

A plain left-click on empty desktop opens Wallup and is swallowed. **Hold Shift to pass
the click through untouched.** That escape hatch matters: clicking bare desktop is also
how you deselect icons and start a rubber-band selection, and this hook would otherwise
eat both. A global hook with no way out is hostile.

## Running it

```
dotnet build
dotnet run --project src/Wallup
```

Diagnostics, because shell integration fails invisibly rather than loudly:

```
Wallup.exe --diagnose    # dump the shell window tree and exit
```

Logs go to `%LOCALAPPDATA%\Wallup\logs\wallup.log`; a line there records whether acrylic
was accepted. Tasks and settings live in `%APPDATA%\Wallup\`.

## What is verified

Seen working on Windows 11 build 26200, in screenshots:

- [x] Chips render on the desktop with real acrylic, above the wallpaper
- [x] Glass tone adapts per chip to the wallpaper behind it
- [x] Left-click empty desktop opens the composer at the cursor, and it stays open
- [x] Type + Enter drops a new chip at that exact spot
- [x] Tasks persist across restarts, including position
- [x] Checkbox state and strikethrough

Built but **not yet confirmed by a human**, because they need real hover and drag:

- [ ] Dragging a chip to reposition it
- [ ] Double-click to edit a chip in place
- [ ] Clock button, setting an alarm, the chip pulsing when it comes due
- [ ] Delete button
- [ ] Settings panel sliders

## Known gaps

- Clicking a desktop *icon* also opens Wallup. Both hit `SysListView32`; telling them apart
  needs `LVM_HITTEST`. Until then, Shift+click is the way to select an icon.
- Left-click is a busy gesture. Deselect and rubber-band selection are unavailable on bare
  desktop without holding Shift.
- `Settings.Opacity`, `FontSize` and `ChipWidth` are stored and edited but not yet applied
  to live chips.
- `HideCompleted` is stored but not yet acted on.
- The sampler assumes the wallpaper is scaled to fill, which is the Windows default. Tile
  and Centre make the mapping approximate.
- Single monitor. Multi-monitor placement is untested.
- An Explorer restart may strip the z-order pinning; there is no watcher for it yet.
