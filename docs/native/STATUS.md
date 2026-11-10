# Chummer Native: status

Branch `native` on https://github.com/Audxius/chummer5a. The plan is in `~/.claude/plans/quiet-knitting-platypus.md`.

## Run it

- **Desktop:** double-click `~/Desktop/Chummer Native.desktop`.
- **Terminal:** run `tools/native/chummer-native.sh`.

It builds on first run and whenever the sources change. Custom character settings files (for example from your GM) go in `~/.config/chummer5/settings/`. It uses native Wayland when `WAYLAND_DISPLAY` is set (niri, KDE Plasma Wayland). Set `CHUMMER_BACKEND=x11` to force X11.

## What works today

| Area | State |
|---|---|
| Engine on Linux (net8, no WinForms, no Wine) | ✅ All 34 test characters load and save. |
| Native Wayland window | ✅ Checked on niri: libwayland, EGL, xkbcommon. |
| Roster, open file, MRU, save / dirty prompt | ✅ |
| **Overview** | ✅ Condition monitors you can click (mark or heal damage), initiative (click initiative, soak, composure, judge intentions, memory or lift/carry to roll them), armor, Edge, movement, limits, derived stats, attributes, qualities. |
| **Attributes** | ✅ Raise with karma in career mode (same cost, confirmation and expense-log entry as legacy), burn Edge; priority/karma +/- in create mode. |
| **Skills** | Sort by dice pool, name, rating or category; optional category grouping. Roll skills and specializations, advance skills/groups with karma, add specializations, knowledge/language and exotic skills, edit creation allocations. |
| **Qualities** | Add qualities with ratings/free options; remove or buy off, change levels, edit notes. Career costs use the extracted legacy rules. |
| **Inventory** | Item-specific icons, slot grid, equipped loadout, selection details, equip/wireless/commlink toggles, weapon ammunition, quantity, armor damage, notes, rename, sell and remove. Empty categories also have add buttons. Clickable vehicle physical and equipment matrix damage monitors. |
| **Store** | Buy weapons, armor, gear, cyber/bioware, vehicles, drugs and lifestyles. Ratings, grades, quantities, availability/affordability filters and markup. Add owned-item mods/accessories/plugins and pay lifestyle rent from inventory details. |
| **Abilities** | Add/remove spells, adept powers, complex forms, AI programs, martial arts and techniques; initiation/submersion and metamagic/echo selection. Choose magical traditions and resonance streams; edit custom traditions and allocate mystic adept power points during creation. Specialized editing is still incomplete; see remaining work below. |
| **Custom spells** | Native designer with category modifiers, restrictions, type/range/duration, area, limited spells and calculated drain. Creation limits, career spending, expense undo and save/reload covered by tests. |
| **PACKS kits** | Search/category/contents picker during creation. Legacy application of qualities, martial arts, forms/programs/spells, spirits, lifestyles, funds and nested equipment. Native section-based authoring, XML preview and export; existing files retain their kits and reject duplicate names. Nested gear, quantities and accessories tested and authoring screen rendered. Legacy skill export remains unsupported. |
| **Vehicle weapon mounts** | Native size/control/flexibility/visibility builder, nested mount inventory, mounted weapons and modification store. Career costs, parent-dependent mod prices, funds/capacity rollback, ownership, save/reload and expense undo tested. Builder, store and resulting inventory rendered. Existing mount-option editing remains incomplete. |
| **Equipment locations** | Assign/clear, create, rename and delete locations for top-level gear, armor, weapons, vehicles and vehicle gear. Scope checks protect plugins and other equipment categories. Movement and save/reload tested; native details show location and management actions. |
| **XML amendment editor** | Load base data and amendment files, edit/format, apply with the game amendment engine, inspect result and named changes, save amendments and handle unsaved edits. Replace/add/remove and input isolation tested; rendered preview checked. |
| **Contacts** | ✅ Dossier cards: role, location, connection/loyalty bars with +/-, type, metatype, payment, vice, personal life, family/blackmail/group tags, notes. Enemies and pets separate; spirits and sprites below. |
| **Global search (Ctrl+K)** | ✅ Searches every open runner's items, abilities, skills, contacts and qualities, plus every roll, and jumps straight to it. |
| **Dice Terminal (Ctrl+R)** | ✅ Details below. |
| Engine prompts | ✅ Native for item, number, text, attribute, limit, side, build method, skill (including exotic specifics), skill group, spell / weapon category, optional power, dice hits, metamagic / echo, art / enhancement / enchantment / ritual, martial art, mentor spirit / paragon, adept power, complex form, AI program and spell. Option lists are lifted from the original forms into `Chummer/Backend/Selection`. |
| Remaining prompts | ⏳ Metatype (priority / karma) belongs to the creation wizard, and armor mods arrive with editing. Until then these say "not available yet" and cancel. |
| **Ledger** | Career karma/nuyen entries, edit, undo, expense charts and calendar. |
| **Info & Notes** | Editable identity, appearance, background, description, notes and GM notes. Portrait set/remove in the header. |
| **New runner (Ctrl+N)** | Priority, Sum-to-Ten, karma and Life Module profiles; metatype/talent selection, editable priorities, karma-to-nuyen allocation, calculated build budgets and legacy build checks. Life Module picker includes stages, nested versions, requirements and inherited engine bonuses. Finish into career mode with carryover confirmations, lifestyle starting cash, starting expenses and essence modifier preservation. Complete Windows parity remains unverified. |
| **Custom Improvements** | Dedicated tab: add/edit engine-defined improvement types, name/group/target/value/range options, enable/disable linked effects, notes and removal. Attribute effects, edit state and metadata have regression tests; not every improvement type has an individual parity test. |
| **Master Index** | Search entries from the legacy index's 29 source data files; book/type filters, notes and page references, link a source PDF, adjust page offset and open it using the configured or system reader. |
| **Character rules** | Searchable editor for creation options, karma costs and house rules, including custom presets such as Beggers.xml. Source-book selection and custom-directory activation/loading order. Edits use an isolated copy, then save a new preset and apply it to the current runner. |
| **Global options** | Staged preferences for karma/delete confirmations, expense dates, conversion backups, store filtering, printed skills/expenses/notes and custom-data behavior. Save persists through the native JSON settings store. Other legacy global options are not exposed yet. |
| **Print / export (Ctrl+P)** | XSLT sheets, browser preview, save HTML and PDF via headless Chromium. Save As: Ctrl+Shift+S. |
| **Character export (Ctrl+E)** | JSON, print XML and installed export XSLT formats, including Squad Manager; language selection, preview, copy and save. |
| **Game data export** | ZIP of selected preset and merged game XML, including enabled custom data. |
| **Hero Lab import** | Read portfolio character list and import with the existing engine into a chosen preset. ZIP reader tested; real portfolio end-to-end parity remains unverified. |

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

- **Parity baseline:** Run `42553325872` captured 750 Windows reference outputs, now bundled with [provenance](parity-baseline.json). Its detailed determinism log shows 9 passed and 2 rendering tests failed despite a green workflow; a UTF-8 marker comparison bug is corrected locally. Linux comparison covers all 68 saves with no gameplay-value differences under the documented policy. Exports/sheets remain under verification; no full parity claim yet.
- **Engine and roll tests:** `nix shell nixpkgs#dotnet-sdk_8 -c dotnet test Chummer.Core.Tests --filter 'FullyQualifiedName!~ParityGoldenTests'` (105 passed, including mount weapons/mods, PACKS authoring and custom limit modifiers).
- **Screenshots of every screen:** `tools/native/Screenshots`, which runs headless.

## Recovery from the November 9 handoff

All six saved worktree branches have been merged into `native`. Integration fixes retain the newer skills and attribute layouts, connect inventory add buttons to the store, expose mod/rent controls, and keep empty categories purchasable. The missing store screenshot harness is restored. Character rules and initial metatype selection now have native windows.

Headless checks use a scratch copy of Desperado, never the original. Screenshots exercised skills sorting and karma advancement, equipment toggles, store sections, qualities and abilities, ledger entries/undo and calendar changes. These checks do not establish full Windows engine parity or full feature parity.

Remaining major work: additional specialized creation workflows; remaining global settings; existing mount editing, custom drugs and suites; remaining equipment/ability operations; real Hero Lab portfolio verification; full Linux verification; Nix packaging. This is still **not a complete drop-in replacement** for legacy Chummer. No reliable percentage of feature completion has been established. The [workflow checklist](feature-matrix.md) and [generated legacy event inventory](legacy-actions.tsv) track the audit scope.

The creation budget and validator are extracted from `CharacterCreate.CalculateBPandRefreshBPDisplays` and `CheckCharacterValidity`. The career transition follows `ValidateCharacter` and `SaveCharacterAsCreated`; tests cover spending, rejected builds, cancellation, carryovers, ledger entries and save/reload. New runners now enter Save As when first saved or saved from the close prompt. Headless creation scenarios use synthetic runners under `/tmp`, and never modify a real character.
