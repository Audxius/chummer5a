/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Chummer.Backend.Equipment;
using Chummer.Selection;

namespace Chummer.Rules
{
    public static partial class WeaponMountRules
    {
        public static bool IsModAllowed(Character character, WeaponMount mount, XPathNavigator definition)
        {
            if (mount?.Parent == null || definition == null || definition.SelectSingleNode("hide") != null) return false;
            var vehicle = mount.Parent;
            if (!vehicle.IsDrone && character.Settings.DroneMods && definition.SelectSingleNode("optionaldrone") != null) return false;
            var vehicleNode = vehicle.GetNodeXPath();
            var required = definition.SelectSingleNode("required/vehicledetails");
            var forbidden = definition.SelectSingleNode("forbidden/vehicledetails");
            if (required != null && (vehicleNode == null || !vehicleNode.ProcessFilterOperationNode(required, false))) return false;
            if (forbidden != null && vehicleNode != null && vehicleNode.ProcessFilterOperationNode(forbidden, false)) return false;
            return definition.RequirementsMet(character, vehicle);
        }

        public static async Task<PurchaseResult> PurchaseModAsync(Character character, PurchaseRequest request, CancellationToken token = default)
        {
            var mount = request.Parent as WeaponMount;
            if (mount?.Parent == null || !ReferenceEquals(mount.CharacterObject, character) || request.Markup < -100
                || !await character.Vehicles.ContainsAsync(mount.Parent, token).ConfigureAwait(false)
                || !await mount.Parent.WeaponMounts.ContainsAsync(mount, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotAllowed);
            var document = await character.LoadDataAsync("vehicles.xml", token: token).ConfigureAwait(false);
            var node = document.TryGetNodeByNameOrId("/chummer/weaponmountmods/mod", request.ItemId,
                await character.Settings.BookXPathAsync(token: token).ConfigureAwait(false));
            if (node == null || !IsModAllowed(character, mount, node.CreateNavigator())) return new PurchaseResult(RuleResult.NotAllowed);
            var catalog = new EquipmentCatalog(character, EquipmentKind.WeaponMountMod, mount);
            if (request.BlackMarket && !catalog.BlackMarketAllowedFor(request.ItemId)) return new PurchaseResult(RuleResult.NotAllowed);
            var mod = new VehicleMod(character);
            bool committed = false;
            try
            {
                await mod.SetWeaponMountParentAsync(mount, token).ConfigureAwait(false);
                await mod.CreateAsync(node, request.Rating, mount.Parent, token: token).ConfigureAwait(false);
                if (mod.InternalId.IsEmptyGuid()) return new PurchaseResult(RuleResult.Cancelled);
                mod.DiscountCost = request.BlackMarket;
                if (request.Free) mod.Cost = "0";
                decimal before = await mount.Parent.GetTotalCostAsync(token).ConfigureAwait(false);
                await mount.Mods.AddAsync(mod, token).ConfigureAwait(false);
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                var settings = character.Settings;
                if (career && await settings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
                {
                    bool over = await settings.BookEnabledAsync("R5", token).ConfigureAwait(false)
                        ? mount.Parent.IsDrone && await settings.GetDroneModsAsync(token).ConfigureAwait(false)
                            ? await mount.Parent.GetDroneModSlotsUsedAsync(token).ConfigureAwait(false) > await mount.Parent.GetDroneModSlotsAsync(token).ConfigureAwait(false)
                            : await mount.Parent.OverR5CapacityAsync("Weapons", token).ConfigureAwait(false)
                        : await mount.Parent.GetSlotsUsedAsync(token).ConfigureAwait(false) > await mount.Parent.GetSlotsAsync(token).ConfigureAwait(false);
                    if (over) { await PurchaseRules.ShowCapacityReachedAsync(token).ConfigureAwait(false); return new PurchaseResult(RuleResult.NotAllowed); }
                }
                decimal cost = request.Free ? 0 : await mount.Parent.GetTotalCostAsync(token).ConfigureAwait(false) - before;
                if (career)
                {
                    cost = await PurchaseRules.StreetCostAsync(character, cost, request.Markup,
                        (await mod.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!request.Free && !await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotEnoughNuyen);
                }
                token.ThrowIfCancellationRequested();
                ExpenseLogEntry expense = null;
                if (career && !request.Free)
                    expense = await PurchaseRules.ChargeAsync(character, cost, "String_ExpensePurchaseVehicleMod", await mod.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                        NuyenExpenseType.AddVehicleWeaponMountMod, mod.InternalId, 0, CancellationToken.None).ConfigureAwait(false);
                committed = true;
                return new PurchaseResult(RuleResult.Changed, mod, cost, expense);
            }
            finally { if (!committed) await mod.DeleteVehicleModAsync(token: CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
