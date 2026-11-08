# Phase 2 input: game logic that moved to the UI side with the tree code

The UiExtractor run (tools/native/UiExtractor) moved every engine method with WinForms types in its signature to
Chummer/Forms/UiExtensions. Most are pure tree/menu building. The ones below also apply rules (improvements, prompts,
state changes, collection reordering). Phase 2 must split each: rules into an engine service, refresh in the view layer.

| File | Method | Signals |
|---|---|---|
| Characters/CharacterCacheUiExtensions.cs | `OnDefaultKeyDown` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveGearParentAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveGearNode` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveGearNodeAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveGearRoot` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveGearRootAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveLifestyleNode` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveLifestyleNodeAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveArmorNode` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveArmorNodeAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveArmorRoot` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveArmorRootAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveWeaponNode` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveWeaponNodeAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveWeaponRoot` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveWeaponRootAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveVehicleNode` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveVehicleNodeAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveVehicleGearParentAsync` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveImprovementRoot` | reorders |
| Characters/CharacterUiExtensions.cs | `MoveImprovementRootAsync` | reorders |
| Equipment/GearUiExtensions.cs | `ReaddImprovements` | state, improvements |
| Equipment/GearUiExtensions.cs | `RefreshSingleFocusRating` | prompts, state |
| Equipment/VehicleUiExtensions.cs | `CreateTreeNode` | reorders |
| Equipment/WeaponUiExtensions.cs | `Reload` | prompts, state |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleMods` | reorders |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleMods` | reorders |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleMods` | reorders |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleWeaponMounts` | reorders |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleWeaponMounts` | reorders |
| Interfaces/InternalIdUiExtensions.cs | `RefreshVehicleWeaponMounts` | reorders |
| Interfaces/MatrixAttributesUiExtensions.cs | `ProcessMatrixAttributeComboBoxChange` | state |
| Interfaces/MatrixAttributesUiExtensions.cs | `ProcessMatrixAttributeComboBoxChangeAsync` | state |

Also review by hand: `Equipment/WeaponUiExtensions.cs` `Unload` (ammo state), and every `Move*Node`/`Move*Root`/`Move*Parent`
overload in `CharacterUiExtensions.cs` (drag-and-drop reordering: needs engine-level "move item to index/location" operations).
Some "reorders" hits are only TreeNode collection inserts.
