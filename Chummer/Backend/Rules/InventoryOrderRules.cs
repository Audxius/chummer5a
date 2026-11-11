/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public static class InventoryOrderRules
    {
        public static bool CanOrder(Character character, object item)
            => item is ICanSort && Groups(character).Any(g => g.Any(x => ReferenceEquals(x, item)));

        private static IEnumerable<object[]> Descendants(object[] group)
        {
            yield return group;
            foreach (var item in group)
            {
                var children = new List<object[]>();
                if (item is Weapon weapon) { children.Add(weapon.WeaponAccessories.Cast<object>().ToArray()); children.Add(weapon.Children.Cast<object>().ToArray()); }
                if (item is WeaponAccessory accessory) children.Add(accessory.GearChildren.Cast<object>().ToArray());
                if (item is Armor armor) { children.Add(armor.ArmorMods.Cast<object>().ToArray()); children.Add(armor.GearChildren.Cast<object>().ToArray()); }
                if (item is ArmorMod armorMod) children.Add(armorMod.GearChildren.Cast<object>().ToArray());
                if (item is Cyberware implant) { children.Add(implant.Children.Cast<object>().ToArray()); children.Add(implant.GearChildren.Cast<object>().ToArray()); }
                if (item is Gear gear) children.Add(gear.Children.Cast<object>().ToArray());
                if (item is Vehicle vehicle) { children.Add(vehicle.Mods.Cast<object>().ToArray()); children.Add(vehicle.Weapons.Cast<object>().ToArray()); children.Add(vehicle.WeaponMounts.Cast<object>().ToArray()); children.Add(vehicle.GearChildren.Cast<object>().ToArray()); }
                if (item is WeaponMount mount) { children.Add(mount.Mods.Cast<object>().ToArray()); children.Add(mount.Weapons.Cast<object>().ToArray()); }
                if (item is VehicleMod vehicleMod) { children.Add(vehicleMod.Weapons.Cast<object>().ToArray()); children.Add(vehicleMod.Cyberware.Cast<object>().ToArray()); }
                foreach (var child in children)
                    foreach (var descendants in Descendants(child)) yield return descendants;
            }
        }

        private static IEnumerable<object[]> Groups(Character character)
        {
            var roots = new[] { character.Weapons.Cast<object>().ToArray(), character.Armor.Cast<object>().ToArray(),
                character.Cyberware.Cast<object>().ToArray(), character.Gear.Cast<object>().ToArray(), character.Vehicles.Cast<object>().ToArray(),
                character.Drugs.Cast<object>().ToArray(), character.Lifestyles.Cast<object>().ToArray() };
            foreach (var root in roots)
                foreach (var group in Descendants(root)) yield return group;
        }

        /// <summary>Persist a complete sibling group without moving attachments or changing ownership.</summary>
        public static async Task<RuleResult> ReorderAsync(Character character, IReadOnlyList<object> order, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (order == null || order.Count == 0) return RuleResult.NotAllowed;
            var locker = await character.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
            try
            {
                var roots = Groups(character).FirstOrDefault(g => g.Any(x => ReferenceEquals(x, order[0])));
                if (roots == null || roots.Length != order.Count || order.Any(x => !(x is ICanSort))
                    || roots.Any(x => order.Count(y => ReferenceEquals(x, y)) != 1)) return RuleResult.NotAllowed;
                token.ThrowIfCancellationRequested();
                bool changed = false;
                for (int i = 0; i < order.Count; ++i)
                {
                    var item = (ICanSort)order[i];
                    changed |= item.SortOrder != i + 1;
                    item.SortOrder = i + 1;
                }
                return changed ? RuleResult.Changed : RuleResult.Cancelled;
            }
            finally { await locker.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
