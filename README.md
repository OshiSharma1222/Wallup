# Wallup

> Your wallpaper is your to-do list. Click the desktop, drop a task, get on with your day.

Double right-click empty desktop or press Ctrl+Alt, type a task, hit Enter. It drops onto the desktop as a small
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
| Composer | `Views/ComposerWindow.xaml` | Opens at the cursor on either gesture. Takes one line, then gets out of the way. |
| Today and settings | `Views/SettingsWindow.xaml` | Today's to-do and done lists, the choice of gesture, and the appearance sliders. |
| Chip host | `Views/ChipHost.cs` | Keeps chip windows in sync with the task list and fires alarms. |
| Mouse gesture | `Interop/DesktopClickHook.cs` | Global `WH_MOUSE_LL` hook. Catches a double right-click on empty desktop. |
| Key gesture | `Interop/CtrlAltHook.cs` | Global `WH_KEYBOARD_LL` hook. Catches Ctrl+Alt pressed on their own. |
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

### Five traps worth knowing

- **`SetWindowPos` names the window that goes *above* yours.** Passing Progman therefore
  files the window *under* the desktop, where it is invisible - and re-asserting that on
  `WM_WINDOWPOSCHANGING` made a chip disappear the moment it was clicked. `HWND_BOTTOM`
  lands in the same place for the same reason. Walk the z-order to find the lowest window
  that is *not* the desktop and insert after that one instead.
- **The desktop moves, and does not tell you.** Show Desktop (Win+D) can raise Progman
  to the top of the normal band. A chip that only guards its own z-order never hears
  about it and ends up under the wallpaper exactly when the user looks for it. Chips are
  therefore *owned* by Progman: the window manager keeps an owned window above its
  owner and carries it along when the owner is raised.
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

### The gestures

Two ways open the task box, and the window lets you keep either or both:

- **Double right-click** on empty desktop opens it where you clicked. A single right-click
  is held back for the double-click time, then handed to the desktop, so its menu still
  appears, a beat late.
- **Ctrl+Alt**, pressed and let go with nothing else, opens it at the pointer from
  anywhere. It fires on the release, and any other key pressed in between cancels it,
  because Ctrl+Alt is the start of many real shortcuts. The fake Ctrl that AltGr sends
  is ignored, so AltGr on its own does nothing. The keyboard hook is only installed while
  Ctrl+Alt is switched on, and it never swallows a key.

Either gesture pressed again cancels the box. A plain left-click used to open it too, and
was retired: it swallowed every click on bare desktop, deselecting icons and rubber-band
selection included. It now only matters while the box is open, when clicking the desktop
puts the box away and keeps what was typed. **Shift passes any click through untouched.**

### Finished tasks

Ticking a task takes its chip off the desktop, but the task is kept until the end of the
day so the window can list it under *Done*. Unticking it there puts the chip back.
Finished tasks from earlier days are dropped at the next start.

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

- [x] The composer takes the keyboard when it opens (seen with the retired left-click)
- [x] Ctrl+Alt on its own opens the composer at the pointer and a second press closes
      it; Ctrl+Alt+T leaves it alone (synthetic keys)
- [x] Type + Enter drops a chip whose glass lands exactly on the click point
- [x] A chip sits directly above Progman and below every ordinary app window
- [x] Clicking a chip leaves it on the desktop layer instead of burying it behind the
      wallpaper. This is the v0.2 bug, gone.
- [x] Show Desktop (Win+D) leaves the chips on the wallpaper, clickable, and they drop
      back under the apps when those are restored
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
- [ ] The today and settings window: both lists, ticking from it, the gesture switch and
      the sliders
- [ ] Double right-click opening the composer, and a single one still reaching the desktop
- [ ] Shift+click passing a desktop click through
- [ ] Two chips overlapping each other

## Known gaps

- Double right-clicking a desktop *icon* also opens Wallup. Both hit `SysListView32`;
  telling them apart needs `LVM_HITTEST`.
- Ctrl+Alt with a mouse click in between (a shortcut in some apps) still counts as the
  gesture; the keyboard hook cannot see the mouse.
- The sampler assumes the wallpaper is scaled to fill, which is the Windows default. Tile
  and Centre make the mapping approximate.
- Single monitor. Multi-monitor placement is untested.
- An Explorer restart may strip the z-order pinning; there is no watcher for it yet.
- The glass refracts the *wallpaper*, so a chip overlapping another chip shows the
  wallpaper rather than the chip underneath.
