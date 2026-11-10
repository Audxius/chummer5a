/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public static partial class WeaponMountRules
    {
        public static bool IsWeaponAllowed(WeaponMount mount, XPathNavigator definition)
        {
            if (mount == null || definition == null || mount.IsWeaponsFull) return false;
            string category = definition.SelectSingleNode("category")?.Value ?? "";
            if (!string.IsNullOrEmpty(mount.AllowedWeaponCategories) && !mount.AllowedWeaponCategories.Split(',').Any(x => x.Trim() == category)) return false;
            return string.IsNullOrEmpty(mount.WeaponFilter) || definition.SelectSingleNode("self::*[(" + mount.WeaponFilter + ")]") != null;
        }

        /// <summary>Legacy CharacterShared.AddWeaponToWeaponMountAsync: parent links, compatibility, price and vehicle-weapon undo.</summary>
        public static async Task<PurchaseResult> PurchaseWeaponAsync(Character character, WeaponMount mount, PurchaseRequest request, CancellationToken token = default)
        {
            if (!ReferenceEquals(mount.CharacterObject, character) || mount.Parent == null
                || !await character.Vehicles.ContainsAsync(mount.Parent, token).ConfigureAwait(false)
                || !await mount.Parent.WeaponMounts.ContainsAsync(mount, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotAllowed);
            var document = await character.LoadDataAsync("weapons.xml", token: token).ConfigureAwait(false);
            var node = document.TryGetNodeByNameOrId("/chummer/weapons/weapon", request.ItemId,
                await character.Settings.BookXPathAsync(token: token).ConfigureAwait(false));
            if (node == null || !IsWeaponAllowed(mount, node.CreateNavigator())) return new PurchaseResult(RuleResult.NotAllowed);
            var weapon = new Weapon(character);
            var additional = new List<Weapon>(); bool committed = false;
            try
            {
                await weapon.SetParentMountAsync(mount, token).ConfigureAwait(false);
                await weapon.CreateAsync(node, additional, token: token).ConfigureAwait(false);
                if (weapon.InternalId.IsEmptyGuid()) return new PurchaseResult(RuleResult.Cancelled);
                weapon.DiscountCost = request.BlackMarket;
                if (request.Free) weapon.Cost = "0";
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                decimal cost = request.Free ? 0 : await weapon.GetTotalCostAsync(token).ConfigureAwait(false);
                if (career)
                {
                    cost = await PurchaseRules.StreetCostAsync(character, cost, request.Markup,
                        (await weapon.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!request.Free && !await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotEnoughNuyen);
                }
                await mount.Weapons.AddAsync(weapon, token).ConfigureAwait(false);
                foreach (Weapon child in additional)
                {
                    await child.SetParentMountAsync(mount, token).ConfigureAwait(false);
                    await mount.Weapons.AddAsync(child, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                ExpenseLogEntry expense = null;
                if (career && !request.Free)
                    expense = await PurchaseRules.ChargeAsync(character, cost, "String_ExpensePurchaseVehicleWeapon", await weapon.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                        NuyenExpenseType.AddVehicleWeapon, weapon.InternalId, 0, CancellationToken.None).ConfigureAwait(false);
                committed = true;
                return new PurchaseResult(RuleResult.Changed, weapon, cost, expense);
            }
            finally
            {
                if (!committed)
                {
                    foreach (Weapon child in additional) await child.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                    await weapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
