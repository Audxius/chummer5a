# Native replacement verification

This is a workflow checklist, not a completion percentage. A native control, a successful build, and passing engine tests establish different things; none alone establishes drop-in parity.

The generated [legacy event inventory](legacy-actions.tsv) records the source location, control, event, handler, label and translation key of 1,999 event bindings in legacy Forms and Controls. Regenerate with `perl tools/native/inventory-actions.pl > docs/native/legacy-actions.tsv`. Shared handlers, navigation and rendering events mean those rows are not 1,999 independent features. Detailed handler-to-native mapping remains to be reviewed.

| Workflow | Native implementation | Verification or remaining gap |
|---|---|---|
| Load/save existing runners | Roster, open, MRU, save/save as | Engine fixtures and scratch-character screen checks; Windows golden comparison pending |
| Creation methods | Priority, Sum-to-Ten, Karma, Life Modules | Metatype tests; module inheritance/accounting/removal; headless creation and career conversion |
| Build accounting and validation | Extracted legacy calculations and validator | Budget/overspend/ignore-rules tests; complete cross-method parity pending |
| Career conversion | Starting cash, carryovers, ledger, essence preservation | Cancellation, expenses, save/reload and headless transition tested |
| Attributes and skills | Creation allocation and career advancement; specializations, knowledge, exotic skills | Regression tests and screen checks; full action-by-action audit pending |
| Standard purchases | Weapons, armor, gear, cyber/bio, vehicles, drugs, lifestyles; modifications | Purchase and expense tests; specialized mount/suite/custom drug tools missing |
| Inventory session actions | Equip/wireless/commlink, ammo, quantities, damage, notes, rename/sell/remove; locations | Engine tests and scratch screenshots; location movement/scope/persistence tested; reordering, transfers and upgrades incomplete |
| PACKS application | Native kit search/category/contents picker; extracted legacy multi-section application | Equipment, nested plugin, ammo, implant and vehicle regression tests and rendered workflow pass |
| PACKS authoring | Native section selection, XML preview and export using legacy serializer; appends to existing kit files and rejects duplicate names | Nested gear, quantities, accessory and section-filter tests and rendered workflow under verification; legacy skill export remains unsupported |
| Custom spell creation | Native designer reproduces legacy modifiers, validation and drain | Five-category drain checks; career costs, expense undo and save/reload tested; designer and created-spell screens checked |
| Weapon mount creation | Native size/control/flexibility/visibility builder, mounted weapons and mount-mod store | Quote, career costs, capacity/funds rollback, ownership, expense undo and save/reload tests; builder/store/inventory rendered; editing existing mount options remains incomplete |
| Cyberware suites | Author owned implants, preview nested contents/cost/essence, open suite XML, install with grade checks and discounts | Nested suite/save tests, malformed-item and insufficient-funds rollback tests; authoring screen rendered; original suite expense has no undo |
| Natural weapons | Native combat-skill/damage/AP/reach designer matching legacy | Skill/input validation, zero cost and save/reload/removal tested; builder and created weapon rendered |
| Custom drugs | Component/level recipe editor with grades, live description/price, purchase, extra doses and inventory effects | Foundation/level/limit conflicts, free creation, career funds, saved recipes/grade, expense undo and same-name effect isolation tested; builder and inventory rendered |
| Qualities and abilities | Add/remove/levels; spells, powers, CFs, AI, martial arts, initiation | Rules tests and screenshots; reapply-improvement and specialized editing audit pending |
| Traditions and streams | Standard/custom selection and mystic adept PP | Regression test and rendered editor |
| Custom improvements | Type/target/value/options, edit, enable/disable, notes, delete | Attribute effect lifecycle and linked metadata tests; full type-by-type parity pending |
| Contacts/spirits/pets | Editors and actions | Screen checks; comprehensive handler audit pending |
| Limits | Derived limits and custom-modifier manager with conditions/notes, add/edit/remove | Identity, metadata, save/reload and automatic-modifier edit protection tested; automatic improvement-only modifier presentation audit remains |
| Ledger/calendar | Add/edit/undo and charts/calendar | Expense and calendar regression tests; screen checks |
| Dice tools | Dice Terminal and roll lookup | Roll tests; legacy dice-roller/glitch and initiative-tool audit pending |
| Character settings | Isolated editing, new presets, books, installed custom directories/order | Isolation and XML roundtrip assertions; complete legacy settings audit pending |
| Global options | Selected session/store/print/data preferences | Many legacy options remain unexposed |
| Master Index | Search, filters, source reference, PDF association and page offset | Catalog test and screenshot; external PDF launch not exercised |
| Character print/export | XSLT/HTML/PDF, JSON/XML/Squad, language selection | Engine outputs tested; rendered JSON preview; complete multilingual golden comparison pending |
| Data export | Selected preset and merged game XML ZIP | Archive contents and leave-open behavior tested |
| Hero Lab import | Portfolio selection and existing engine importer | CP850 portfolio reader tested; real portfolio end-to-end verification pending |
| XML editor | Native amendment editor with base/result/change views, formatting, open/save and dirty-close handling | Real add/replace/remove operations and input isolation tested; rendered preview verified |
| Plugins | Legacy plugin functionality | Compatibility/replacement audit pending |
| Distribution | Source-based native launcher with content fingerprint | Fingerprint regression checks pass; reproducible Nix packaging pending |
| Windows engine parity | Captured750 Windows outputs, all750 compared locally | All saves/XML/JSON/Squad pass explicit policies;49 rendered-sheet ordering differences remain; see parity-linux.md; corrected Windows rerun blocked by Actions disabled |

Only mark a workflow fully verified after exercising its costs, prompts, cancellation, expenses/undo where applicable, and saved state against the legacy specification. Test fixtures must use scratch characters; the user's original character is never a test target.
