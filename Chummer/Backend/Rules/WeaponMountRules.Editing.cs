/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public static partial class WeaponMountRules
    {
        private static async Task<bool> CanEditAsync(Character character, WeaponMount mount, CancellationToken token)
            => mount != null && !mount.IncludedInVehicle && ReferenceEquals(mount.CharacterObject, character) && mount.Parent != null
               && await character.Vehicles.ContainsAsync(mount.Parent, token).ConfigureAwait(false)
               && await mount.Parent.WeaponMounts.ContainsAsync(mount, token).ConfigureAwait(false);

        private static async Task<bool> WeaponsFitAsync(WeaponMount owned, WeaponMount proposed, CancellationToken token)
        {
            proposed.AllowedWeapons = owned.AllowedWeapons;
            var roots = (await owned.Weapons.ToListAsync(token).ConfigureAwait(false))
                .Where(x => string.IsNullOrEmpty(x.ParentID) || owned.Weapons.DeepFindById(x.ParentID) == null).ToList();
            if (roots.Count > proposed.WeaponCapacity) return false;
            foreach (var weapon in roots)
            {
                if (!string.IsNullOrEmpty(proposed.AllowedWeaponCategories) && !proposed.AllowedWeaponCategories.Split(',').Any(x => x.Trim() == weapon.Category)) return false;
                if (!string.IsNullOrEmpty(proposed.AllowedWeapons) && !proposed.AllowedWeapons.Split(',').Any(x => x.Trim() == weapon.Name)) return false;
                if (!string.IsNullOrEmpty(proposed.WeaponFilter))
                {
                    var node = await weapon.GetNodeXPathAsync(token: token).ConfigureAwait(false);
                    if (node == null || node.SelectSingleNode("self::*[(" + proposed.WeaponFilter + ")]") == null) return false;
                }
            }
            return true;
        }

        public static async Task<WeaponMountQuote> QuoteEditAsync(Character character, WeaponMount owned, WeaponMountConfiguration input, CancellationToken token = default)
        {
            if (!await CanEditAsync(character, owned, token).ConfigureAwait(false)) return null;
            var build = await BuildAsync(character, owned.Parent, input, true, token).ConfigureAwait(false);
            if (build.Mount == null) return null;
            var proposed = build.Mount;
            try
            {
                if (!await WeaponsFitAsync(owned, proposed, token).ConfigureAwait(false)) return null;
                int slots = await proposed.GetCalculatedSlotsAsync(token).ConfigureAwait(false)
                    + await owned.Mods.SumAsync(x => !x.IncludedInVehicle, (x, t) => x.GetCalculatedSlotsAsync(t), token: token).ConfigureAwait(false);
                decimal total = await proposed.GetTotalCostAsync(token).ConfigureAwait(false)
                    + await owned.Weapons.SumAsync((x, t) => x.GetTotalCostAsync(t), token).ConfigureAwait(false);
                decimal parentCost = await proposed.GetOwnCostAsync(token).ConfigureAwait(false);
                var availability = await proposed.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                int value = availability.Value; char suffix = availability.Suffix;
                foreach (var mod in await owned.Mods.ToListAsync(token).ConfigureAwait(false))
                {
                    total += await mod.GetTotalCostForMountAsync(slots, parentCost, token).ConfigureAwait(false);
                    if (mod.IncludedInVehicle || !mod.Equipped) continue;
                    var modAvail = await mod.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                    if (modAvail.AddToParent) value += modAvail.Value;
                    if (modAvail.Suffix == 'F' || modAvail.Suffix == 'R' && suffix != 'F') suffix = modAvail.Suffix;
                }
                decimal delta = total - await owned.GetTotalCostAsync(token).ConfigureAwait(false);
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                if (career) delta = await PurchaseRules.StreetCostAsync(character, delta, input.Markup, suffix, token).ConfigureAwait(false);
                return new WeaponMountQuote { Name = await proposed.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), Cost = delta,
                    Slots = slots, WeaponCapacity = proposed.WeaponCapacity, AllowedWeapons = proposed.AllowedWeaponCategories,
                    Availability = new AvailabilityValue(Math.Max(0, value), suffix, false).ToString(), VariableCost = build.Variable,
                    CanAfford = !career || delta <= await character.GetNuyenAsync(token).ConfigureAwait(false) };
            }
            finally { await proposed.DisposeAsync().ConfigureAwait(false); }
        }

        public static async Task<PurchaseResult> EditAsync(Character character, WeaponMount owned, WeaponMountConfiguration input, CancellationToken token = default)
        {
            if (!await CanEditAsync(character, owned, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotAllowed);
            var build = await BuildAsync(character, owned.Parent, input, false, token).ConfigureAwait(false);
            if (build.Mount == null) return new PurchaseResult(build.Result);
            var proposed = build.Mount; var snapshot = new WeaponMount(character, owned.Parent);
            var oldOptions = Array.Empty<WeaponMountOption>();
            snapshot.CopyConfigurationFrom(owned); bool applied = false, committed = false;
            try
            {
                oldOptions = (await owned.WeaponMountOptions.ToListAsync(token).ConfigureAwait(false)).ToArray();
                if (!await WeaponsFitAsync(owned, proposed, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotAllowed);
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                decimal before = await owned.Parent.GetTotalCostAsync(token).ConfigureAwait(false);
                string oldName = await owned.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false);
                var options = await proposed.WeaponMountOptions.ToListAsync(token).ConfigureAwait(false);
                await proposed.WeaponMountOptions.ClearAsync(token).ConfigureAwait(false);
                applied = true; owned.CopyConfigurationFrom(proposed);
                await owned.WeaponMountOptions.ClearAsync(token).ConfigureAwait(false);
                await owned.WeaponMountOptions.AddRangeAsync(options, token).ConfigureAwait(false);
                var settings = await character.GetSettingsAsync(token).ConfigureAwait(false);
                if (career && await settings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
                {
                    var vehicle = owned.Parent;
                    bool over = await settings.BookEnabledAsync("R5", token).ConfigureAwait(false)
                        ? vehicle.IsDrone && await settings.GetDroneModsAsync(token).ConfigureAwait(false)
                            ? await vehicle.GetDroneModSlotsUsedAsync(token).ConfigureAwait(false) > await vehicle.GetDroneModSlotsAsync(token).ConfigureAwait(false)
                            : await vehicle.OverR5CapacityAsync("Weapons", token).ConfigureAwait(false)
                        : await vehicle.GetSlotsUsedAsync(token).ConfigureAwait(false) > await vehicle.GetSlotsAsync(token).ConfigureAwait(false);
                    if (over) { await PurchaseRules.ShowCapacityReachedAsync(token).ConfigureAwait(false); return new PurchaseResult(RuleResult.NotAllowed); }
                }
                decimal cost = await owned.Parent.GetTotalCostAsync(token).ConfigureAwait(false) - before;
                ExpenseLogEntry expense = null;
                if (career)
                {
                    cost = await PurchaseRules.StreetCostAsync(character, cost, input.Markup, (await owned.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotEnoughNuyen);
                }
                token.ThrowIfCancellationRequested();
                if (career)
                {
                    string newName = await owned.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false);
                    if (newName != oldName) newName = oldName + " -> " + newName;
                    string description = string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("String_ExpenseVehicleRetrofit", token: token).ConfigureAwait(false), newName);
                    expense = new ExpenseLogEntry(character); expense.Create(-cost, description, ExpenseType.Nuyen, DateTime.Now);
                    var undo = new ExpenseUndo(); undo.CreateNuyen(NuyenExpenseType.ModifyVehicleWeaponMount, owned.InternalId); expense.Undo = undo;
                    await character.ExpenseEntries.AddWithSortAsync(expense, token: CancellationToken.None).ConfigureAwait(false);
                    await character.ModifyNuyenAsync(-cost, CancellationToken.None).ConfigureAwait(false);
                }
                committed = true; return new PurchaseResult(RuleResult.Changed, owned, cost, expense);
            }
            finally
            {
                if (applied && !committed)
                {
                    owned.CopyConfigurationFrom(snapshot);
                    await owned.WeaponMountOptions.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                    await owned.WeaponMountOptions.AddRangeAsync(oldOptions, CancellationToken.None).ConfigureAwait(false);
                }
                await proposed.DisposeAsync().ConfigureAwait(false); await snapshot.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
