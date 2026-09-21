# Spike: capturing the desktop layer on Windows 11

**Date:** 2026-09-21
**Machine:** Windows 11 Home Single Language, build 10.0.26200
**Question:** Can we draw a box on the wallpaper layer, behind the desktop icons, and
capture a right-click there?

**Answer:** Yes, but not the way every tutorial describes. The classic `WorkerW` trick
does not work on this build, and the standard way of checking whether it worked reports a
false failure.

## What the tutorials say

Send the undocumented message `0x052C` to `Progman`. The shell splits the desktop into two
top-level windows: one `WorkerW` hosting `SHELLDLL_DefView` (the icons), and a second,
empty `WorkerW` behind it. `SetParent` your window into the empty one and you are painting
on the wallpaper.

## What actually happens on build 26200

The split never happens. We tried all three known payload variants —
`(wParam=0, lParam=0)`, `(0x0D, 0x01)`, and `(0x0D, 0x00)` — and re-enumerated after each.
No new top-level `WorkerW` appeared in any case.

The machine does have fourteen top-level `WorkerW` windows, but every one of them is
invisible and none hosts the icon view. Positional indexing into that list — which several
tutorials suggest — would pick an invisible window and silently render nothing.

The real layout is that `Progman` never gives anything up:

```
Progman  (visible, hosts everything)
  +-- SHELLDLL_DefView   1920x1200   the icons
  +-- WorkerW            1920x1200   the wallpaper
```

The wallpaper `WorkerW` exists, but as a **child of Progman**, not as a top-level sibling.

## The two traps

### 1. `SetParent` cannot tell you whether it worked

`SetParent` returns the *previous* parent. For a top-level window that is legitimately
`NULL`, so a successful call and a failed call both return zero with `GetLastError() == 0`.
Checking the return value produces a false failure.

`GetParent` does not rescue you either. For a window without `WS_CHILD` it returns the
window's **owner**, not its parent — which for a reparented WPF window is still `NULL`.
We spent a debugging cycle convinced the reparent had silently been reverted.

`GetAncestor(hwnd, GA_PARENT)` is the only call that gives a straight answer.

### 2. Attaching succeeds and still puts you on the wrong side of the icons

`SetParent` inserts the new child at the **top** of its sibling z-order. In the modern
layout that means landing in front of `SHELLDLL_DefView` — on the wallpaper layer, but
painted over the icons. The attach reports success and the result is wrong in a way that
is invisible from inside the process.

Fix: after reparenting, `SetWindowPos(child, defView, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`
to insert directly behind the icon view. That lands us between the icons and the wallpaper:

```
[0] SHELLDLL_DefView   1920x1200
[1] Wallup             380x520      <-- us
[2] WorkerW            1920x1200    the wallpaper
```

## Verified result

`Wallup.exe --selftest` on this machine:

```
Strategy  : progman-child
Attached  : True
Hook      : installed
Parent    : 0x0001010E (Progman)
Verdict   : OK - behind the desktop icons.
```

## What this means for the product

- **Both layouts have to be supported.** `DesktopLayer.Resolve` tries the classic
  top-level `WorkerW` first and falls back to the Progman-child layout. Neither is
  hardcoded, and the `WorkerW` list is never indexed positionally.
- **The two-window architecture is mandatory, not a design preference.** Anything behind
  `SHELLDLL_DefView` gets zero mouse input, so the ambient layer can never be interactive.
  Editing has to happen in a separate top-level window above the icons.
- **The gesture needs a global hook.** There is no window message to listen for, because
  the clicks land on the shell's icon view, not on us.
- **This will break again.** The layout changed between Windows 10 and 11 and can change
  in any update. `--selftest` exists so the next break takes minutes to diagnose instead of
  a day.

## Still unverified

The hook installs and the classification logic is in place, but **whether right-clicking
the desktop actually opens the box has not been confirmed by a human**. That needs someone
at the machine: run `dotnet run --project src/Wallup`, right-click empty desktop, and check
that the box appears at the cursor and that Shift+right-click still gives the Windows menu.
