# Chummer Native: status

Branch `native` on https://github.com/Audxius/chummer5a. The plan is in `~/.claude/plans/quiet-knitting-platypus.md`.

## Run it

- **Desktop:** double-click `~/Desktop/Chummer Native.desktop`.
- **Terminal:** run `tools/native/chummer-native.sh`.

It builds on first run and whenever the sources change. It uses native Wayland when `WAYLAND_DISPLAY` is set (niri, KDE Plasma Wayland). Set `CHUMMER_BACKEND=x11` to force X11.

## What works today

| Area | State |
|---|---|
| Engine on Linux (net8, no WinForms, no Wine) | ✅ All 34 test characters load and save. |
| Native Wayland window | ✅ Checked on niri: libwayland, EGL, xkbcommon. |
| Roster, open file, MRU, save / dirty prompt | ✅ |
| **Overview** | ✅ Condition monitors you can click (mark or heal damage), initiative, armor, Edge, movement, limits, derived stats, attributes, qualities. |
| Attributes, Skills (grouped, icons, search), Qualities, Notes | ✅ View only |
| **Inventory** | ✅ Icon grid for weapons, armor, cyber/bioware, gear, vehicles, drugs and lifestyles. Category chips, search, and hover cards with stats, contents, cost, availability and source. View only. |
| **Abilities** | ✅ Spells, adept powers, complex forms, AI programs, martial arts, critter powers, initiation and metamagic. View only. |
| **Contacts** | ✅ Contacts, enemies, pets, spirits and sprites. View only. |
| **Global search (Ctrl+K)** | ✅ Searches every open runner's items, abilities, skills, contacts and qualities, plus every roll, and jumps straight to it. |
| **Dice Terminal (Ctrl+R)** | ✅ Details below. |
| Engine prompts (choose item / number / text / attribute / limit / side / build method) | ✅ Native prompt window. |
| Other prompts (metatype priority, spell / power / art pickers…) | ⏳ Show "not available yet" and cancel the operation. |
| Creating and editing characters (buy gear, raise skills, karma) | ⏳ Not yet. Phases 2, 6–10 in the plan. Use legacy Chummer for these for now. |
| Settings, print / sheets / PDF export | ⏳ Phases 11–12 |

## Dice Terminal

1. Pick a runner.
2. Type what the GM said ("escape", "dodge", "hack", "ram", "drain"…).
3. Click a result. A Roll20 formula is now on your clipboard.

The pool is calculated from the runner's attributes, skills (with defaulting), limits, improvements and wound modifiers. If you choose a weapon, vehicle, spell + Force, device or spirit, its values are used too. Values the sheet can't know, such as a target's stat or "your chosen skill", become fill-in boxes. You can also add a situational modifier or turn on **Edge**, which explodes sixes and drops the limit.

Example of what gets pasted:

```
&{template:default} {{name=Soma: Escape Combat}} {{Hits=[[2d6>5]]}} {{Opposed by=fastest enemy craft's Piloting + Speed}}
```

Rolls come from two places:

- **Core rules:** 270 rolls from `data/actions.xml`.
- **House rules:** 323 rolls from your friend's books, in `Chummer/houserules/*.xml`.

**Please have your friend skim `Chummer/houserules/README.md`.** It lists every place where the books named a test without giving the dice pool, and what was assumed. To fix or add rolls, edit the XML, or drop files into `~/.config/chummer5/houserules/`. Your clipboard templates and favourites are saved in `~/.config/chummer5`.

## Safety net

- **Parity baseline:** a CI workflow (`.github/workflows/parity-baseline.yml`) captures golden output from the *original* Windows engine so the port can be diffed against it. The latest capture run was still in progress when this file was written.
- **Engine and roll tests:** `nix shell nixpkgs#dotnet-sdk_8 -c dotnet test Chummer.Core.Tests`
- **Screenshots of every screen:** `tools/native/Screenshots`, which runs headless.
