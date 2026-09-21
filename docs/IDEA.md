# Wallup

> Your wallpaper is your to-do list. Right-click, drop a task, get on with your day.

## The one-liner

Wallup fuses your desktop wallpaper and your to-do list into one surface. Right-click
anywhere on the wallpaper and a task box appears, native to the desktop, blending into
your background. Tasks are interactive, live-editable, and deeply customizable.

## The problem

To-do apps are a separate window you have to open, so they get out of sight and out of
mind. Existing "wallpaper to-do" tools fall into three camps, and each is missing
something:

1. **Static wallpaper generators** (Wallpaper Todo, Notion-to-wallpaper). Pretty, but the
   wallpaper is a rendered image. You cannot edit tasks in place, you edit them elsewhere
   and regenerate.
2. **Tray/widget tools** (YYNote, TickTick, Themia). Interactive, but the interaction is a
   tray icon or a widget picker, and customization is shallow.
3. **Power-user engines** (Rainmeter). Deeply customizable, but requires editing config
   files and has a steep learning curve.

No single tool is wallpaper-native, interactive, AND friendly-to-customize at once.

## The insight

The right-click on the empty desktop is the most natural place to add a task, because
that is where your attention already is when the desktop is showing. Make that gesture the
whole product, then make the result beautiful and yours.

## What makes Wallup different

- **Wallpaper-native gesture.** Right-click the desktop itself spawns the task box. No tray
  menu, no separate app window to hunt for.
- **Interactive, not regenerated.** Edit, check off, reorder tasks live on the wallpaper
  layer. Nothing regenerates behind the scenes.
- **Customization without config files.** Fonts, glass/blur, opacity, position, and
  per-wallpaper themes through a friendly settings UI, not `.ini` editing.

## Target user

People who live on their desktop and want their tasks ambient and always in view, without
adopting a heavy productivity system. Aesthetic-minded users who theme their desktops.

## MVP scope (v0.1)

Ship the smallest thing that proves the core gesture feels good.

- [x] Right-click desktop opens a task box at the cursor
- [x] Add, edit, check off, delete a task
- [x] Tasks persist locally between sessions
- [x] Task box renders on the wallpaper layer (behind windows, visible on desktop)
- [x] Basic customization: opacity, font size, box position

## Later (post-MVP)

- Per-wallpaper themes (task style follows the current wallpaper)
- Multiple boxes / task groups pinned to different desktop spots
- Blur / glassmorphism styling
- Due dates, priorities, recurring tasks
- Sync across devices
- Multi-monitor support

## Open technical questions

Resolved by the spike — see [SPIKE-WORKERW.md](SPIKE-WORKERW.md) for the evidence.

- ~~**Platform first?**~~ Windows. Confirmed working on Windows 11 build 26200.
- ~~**Rendering the wallpaper layer.**~~ Done, but not via the documented WorkerW trick,
  which does not work on Windows 11. Progman keeps the icons and the wallpaper as its own
  children, so we parent into Progman and insert behind `SHELLDLL_DefView`.
- ~~**Stack.**~~ C# / .NET 9 / WPF. The whole risk is Win32 interop, and that is where
  C# and the Lively reference implementation pay off.
- ~~**Right-click conflict.**~~ Replace it, with an escape hatch: plain right-click opens
  Wallup, Shift+right-click gives the normal Windows menu.

Still open:

- **Interactivity on the wallpaper layer is impossible.** `SHELLDLL_DefView` covers the
  desktop and eats every click, so the ambient layer can only ever render. All editing
  happens in a separate window above the icons. This makes "edit tasks live on the
  wallpaper" a two-window illusion rather than a literal one — worth checking whether it
  still feels right when dogfooding.
- **Telling an icon click from an empty-desktop click.** Both land on `SysListView32`, so
  right-clicking an icon currently opens Wallup too. Needs `LVM_HITTEST`.
- **Surviving an Explorer restart.** The attach is lost and needs a manual reattach today.

## Prior art to study

Install and use these as a real user before building. The gap that annoys you is the
product.

- **Themia** - closest to the right-click-desktop flow
- **YYNote** - transparent wallpaper-blended to-do widget
- **TickTick** - interactive desktop task widget with opacity control
- **Rainmeter** - the customization ceiling, and the friction to avoid
- **Wallpaper Todo** / Notion-to-wallpaper - the static-image approach

## Next steps

1. ~~Spike: capture a right-click on the Windows desktop layer and draw a box there.~~ Done.
2. ~~Decide the stack based on what the spike proves.~~ C# / .NET 9 / WPF.
3. ~~Build the MVP checklist above.~~ Built; the gesture still needs a human to confirm it
   feels right.
4. Dogfood for a week, compare against Themia and YYNote.
5. Close the three gaps above: icon hit-testing, Explorer restart, multi-monitor.
