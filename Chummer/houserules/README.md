# House rules for the Dice Terminal

Each `*.xml` file here is one of the house-rule books (`~/Desktop/SR BOOKS/*.docx`), turned into rolls using the same
`<action>` schema as `data/actions.xml`. The Dice Terminal loads every file in this folder, plus any in
`~/.config/chummer5/houserules/`. To add or fix a roll, edit the XML or drop a new file in either folder. Nothing needs
rebuilding if you use the config folder.

| File | Book | Rolls |
|---|---|---|
| SR craft.xml | Shadowrun 5e crafting reworked | 113 |
| SR networks.xml | Shadowrun 5e matrix reworked | 134 |
| SR kungfu.xml | Shadowrun 5e combat reworked | 35 (the vehicle chapter is shared with the rigger book and lives there) |
| SR rigged.xml | Reworked Rigger Handbook | 24 |
| SR magic.xml | Shadowrun 5e magic reworked | 17 |

## Dice syntax

`{AGI} + {Pistols}`, `{Weapon: Accuracy}`, `{Vehicle: Speed}`, `{Icon: Sleaze}`, `{Spell: Force}`, `{Max: {a} or {b}}`,
`v. {Threshold: 3}` or `v. plain text describing the opposition`. A token the sheet can't know, such as
`{Appropriate Skill}` or a target's stat, becomes a fill-in box on the roll page.

## Guesses made during extraction (please review)

The books often name a test without spelling out the pool. In those cases:

- **Core SR5 pools were used.** Crash program: LOG + Cybercombat [Attack] v. INT + Firewall. Matrix Perception:
  INT + Computer [Data Processing]. Hide: INT + Electronic Warfare [Sleaze]. Leadership: CHA + Leadership [Social].
- **Gunnery** uses AGI + Gunnery [Accuracy]. Jumped-in riggers in core SR5 would use LOG instead.
- **"Appropriate mechanics test"** is LOG + the best of Automotive / Aeronautics / Nautical Mechanic [Mental].
- **"Spellcasting / counterspelling"** is MAG + skill, and Assensing is INT + Assensing [Astral].
- **Tradition attribute** (Spellcrafting, Astral Weapon Attack) comes from the runner's recorded drain expression. A unique LOG / CHA / INT attribute is resolved automatically; missing or ambiguous custom traditions require an explicit value rather than silently choosing the highest stat.
- **Ammunition crafting** is LOG + Armorer. "Armoury check" with no attribute is LOG + Armorer.
- **Networks:** a bare `[N]` after a pool was read as a **threshold**, not a limit, because that is how the book uses it.
  AI Depth is `{DEP}`.
- **Resistance rolls** against someone else's spell list the enemy-Force limit in the notes, not as a computed limit.
- **Skipped:** fixed damage dice (Fd6, 1d6+1), GM tables (3d6 drill failure, infection tables), reroll effects and
  initiative formulas. Roll20 handles these directly.
