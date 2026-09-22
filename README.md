# Wallup

> Your wallpaper is your to-do list. Click the desktop, drop a task, get on with your day.

Left-click empty desktop, type a task, hit Enter. It drops onto the desktop as a small
glass chip you can drag anywhere, tick off, set an alarm on, or delete. See
[docs/IDEA.md](docs/IDEA.md) for the product thinking.

**Status: v0.3.** See *What is verified* below for exactly what has been seen working and
what has not. v0.2 claimed a working loop it did not have: chips were being pinned
*underneath* the desktop, so a task vanished the moment anything touched its z-order.

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
| Desktop layer | `Interop/DesktopWindow.cs` | Pins chips just above the desktop: over the wallpaper, under real windows. |
| Focus | `Interop/ForegroundWindow.cs` | Hands the composer the keyboard, which a background process may not normally take. |
| Glass | `Views/GlassCapsule.cs` | The pill. Refracts the wallpaper it is covering. |
| Adaptive tone | `Views/AdaptiveGlass.cs` | Samples the wallpaper behind each window and picks dark or light glass. |
| Wallpaper | `Interop/Wallpaper.cs` | The wallpaper as a picture: the patch behind a screen rectangle, and how bright it is. |
| Acrylic | `Interop/Glass.cs` | DWM backdrop, for the settings panel only. |

### The glass

A chip is a capsule of clear glass lying on the wallpaper. It is not the system's acrylic:
DWM can only frost a rectangle, in the system's own tint, and it refuses outright to draw
behind the per-pixel-alpha window a capsule shape requires.

So `GlassCapsule` paints the wallpaper itself. It works out which patch of the picture it
is covering and draws that patch back, gently magnified and softened, with a much harder
magnification in a band around the rim - which is what a real lens does to whatever lies
behind its thick edge, and the single cue that makes a flat shape read as glass. On top go
a sheer wash so text stays readable, a top sheen, a faint colour fringe, and a lit outline.

Because a chip sits directly on the desktop, the patch it paints is exactly the patch it
hides, so the glass is genuinely see-through even though the window is opaque. Moving a
chip only moves two brush viewboxes, so the refraction follows a drag for free.

The settings panel is the exception: it floats over other apps, where the wallpaper is
*not* what is behind it, so it keeps the DWM backdrop in `Interop/Glass.cs`.

### Adaptive tone

Each window samples the wallpaper behind its own rectangle and merges either
`Views/GlassDark.xaml` or `Views/GlassLight.xaml` into its resources, so a chip on a dark
patch is smoked with white text while one on a bright patch is frosted with dark ink.
Every colour in `Theme.xaml` is a `DynamicResource` for exactly this reason - a
`StaticResource` would bake in whichever palette happened to load first.

`Wallpaper` reads the wallpaper file rather than grabbing the screen, because by the time a
chip asks what is behind it, it is already on screen and a grab would capture the chip.

### Four traps worth knowing

- **`SetWindowPos` names the window that goes *above* yours.** Passing Progman therefore
  files the window *under* the desktop, where it is invisible - and re-asserting that on
  `WM_WINDOWPOSCHANGING` made a chip disappear the moment it was clicked. `HWND_BOTTOM`
  lands in the same place for the same reason. Walk the z-order to find the lowest window
  that is *not* the desktop and insert after that one instead.
- **A background process cannot take the keyboard.** The gesture swallows its own click,
  so Windows sees no input for us and `SetForegroundWindow` quietly does nothing: the
  composer appears, the caret blinks elsewhere, and everything typed goes to the app
  behind. Joining the foreground thread's input queue with `AttachThreadInput` for the
  length of the call is what makes it work - measured, not guessed.
- **Acrylic needs a non-layered window.** `AllowsTransparency="True"` makes a WPF window
  layered, and DWM refuses to draw a backdrop behind one. That trade is why chips refract
  the wallpaper themselves and only the settings panel uses acrylic.
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

Driven end to end on Windows 11 build 26200 at 150% scale, with synthetic input on a real
desktop, reading the live window z-order and `tasks.json` after each step:

- [x] Left-click bare desktop opens the composer at the cursor, and it takes the keyboard
- [x] Type + Enter drops a chip whose glass lands exactly on the click point
- [x] A chip sits directly above Progman and below every ordinary app window
- [x] Clicking a chip leaves it on the desktop layer instead of burying it behind the
      wallpaper. This is the v0.2 bug, gone.
- [x] Chips render as glass capsules that refract the wallpaper behind them
- [x] Dragging a chip moves it, and the refraction and the tone follow it - dragged from
      black artwork onto a bright face, the same chip went from smoked to frosted
- [x] Tick, strikethrough, and the completion time, saved
- [x] Double-click to edit in place, Enter to keep it, saved
- [x] Clock button, alarm popup, and the alarm time, saved
- [x] Delete button removes the chip and the task
- [x] Tasks reload at their saved positions across restarts

Not yet exercised:

- [ ] The alarm actually firing, and the chip pulsing when it does
- [ ] Settings panel sliders
- [ ] Shift+click passing a desktop click through
- [ ] Two chips overlapping each other

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
- The glass refracts the *wallpaper*, so a chip overlapping another chip shows the
  wallpaper rather than the chip underneath.
