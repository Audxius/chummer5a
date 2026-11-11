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
        private static IEnumerable<object> Roots(Character character, object item)
        {
            if (item is Weapon) return character.Weapons.Cast<object>();
            if (item is Armor) return character.Armor.Cast<object>();
            if (item is Cyberware) return character.Cyberware.Cast<object>();
            if (item is Gear) return character.Gear.Cast<object>();
            if (item is Vehicle) return character.Vehicles.Cast<object>();
            if (item is Drug) return character.Drugs.Cast<object>();
            if (item is Lifestyle) return character.Lifestyles.Cast<object>();
            return Enumerable.Empty<object>();
        }

        public static bool CanOrder(Character character, object item)
            => item is ICanSort && Roots(character, item).Any(x => ReferenceEquals(x, item));

        /// <summary>Persist a complete root inventory group without moving attachments or changing ownership.</summary>
        public static async Task<RuleResult> ReorderAsync(Character character, IReadOnlyList<object> order, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (order == null || order.Count == 0) return RuleResult.NotAllowed;
            var locker = await character.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
            try
            {
                var roots = Roots(character, order[0]).ToList();
                if (roots.Count != order.Count || order.Any(x => !(x is ICanSort))
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
