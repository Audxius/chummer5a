/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public sealed class WeaponMountConfiguration
    {
        public string SizeId { get; set; }
        public string ControlId { get; set; }
        public string FlexibilityId { get; set; }
        public string VisibilityId { get; set; }
        public bool Free { get; set; }
        public bool BlackMarket { get; set; }
        public decimal Markup { get; set; }
    }
    public sealed class WeaponMountChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
    }
    public sealed class WeaponMountQuote
    {
        public string Name { get; set; }
        public string Availability { get; set; }
        public string AllowedWeapons { get; set; }
        public int Slots { get; set; }
        public int WeaponCapacity { get; set; }
        public decimal Cost { get; set; }
        public bool VariableCost { get; set; }
        public bool CanAfford { get; set; }
    }

    /// <summary>Legacy CreateWeaponMount and character form purchase flow, shared by native hosts.</summary>
    public static partial class WeaponMountRules
    {
        public static async Task<IReadOnlyList<WeaponMountChoice>> OptionsAsync(Character character, Vehicle vehicle, string category, string sizeId = null, CancellationToken token = default)
        {
            if (!ReferenceEquals(vehicle.CharacterObject, character)) return Array.Empty<WeaponMountChoice>();
            var document = await character.LoadDataAsync("vehicles.xml", token: token).ConfigureAwait(false);
            var size = string.IsNullOrEmpty(sizeId) ? null : document.TryGetNodeByNameOrId("/chummer/weaponmounts/weaponmount", sizeId);
            var vehicleNode = await vehicle.GetNodeXPathAsync(token: token).ConfigureAwait(false);
            string filter = "category = " + category.CleanXPath() + " and " + await character.Settings.BookXPathAsync(token: token).ConfigureAwait(false);
            if (!vehicle.IsDrone && await character.Settings.GetDroneModsAsync(token).ConfigureAwait(false)) filter += " and not(optionaldrone)";
            var result = new List<WeaponMountChoice>();
            foreach (XmlNode node in document.SelectNodes("/chummer/weaponmounts/weaponmount[" + filter + "]"))
            {
                token.ThrowIfCancellationRequested();
                var forbidden = node.SelectSingleNode("forbidden/vehicledetails")?.CreateNavigator();
                var required = node.SelectSingleNode("required/vehicledetails")?.CreateNavigator();
                if (forbidden != null && vehicleNode != null && await vehicleNode.ProcessFilterOperationNodeAsync(forbidden, false, token).ConfigureAwait(false)) continue;
                if (required != null && (vehicleNode == null || !await vehicleNode.ProcessFilterOperationNodeAsync(required, false, token).ConfigureAwait(false))) continue;
                if (category != "Size" && size != null && !OptionAllowed(size, category, node["name"]?.InnerText)) continue;
                result.Add(new WeaponMountChoice { Id = node["id"]?.InnerText, Name = node["translate"]?.InnerText ?? node["name"]?.InnerText ?? "",
                    Source = (node["source"]?.InnerText + " " + (node["altpage"]?.InnerText ?? node["page"]?.InnerText)).Trim() });
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }
        private static bool OptionAllowed(XmlNode size, string category, string name)
        {
            string element = category.ToLowerInvariant();
            XmlNode Details(string kind)
            {
                var parent = size[kind];
                return parent?["weaponmountdetails"] ?? (parent?.SelectNodes("control|flexibility|visibility")?.Count > 0 ? parent : null);
            }
            var forbidden = Details("forbidden"); var required = Details("required");
            if (forbidden?.SelectNodes(element).Cast<XmlNode>().Any(x => x.InnerText == name) == true) return false;
            return required == null || required.SelectNodes(element).Cast<XmlNode>().Any(x => x.InnerText == name);
        }
        public static async Task<bool> CanBlackMarketAsync(Character character, CancellationToken token = default)
            => character.GenerateBlackMarketMappings((await character.LoadDataXPathAsync("vehicles.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/weaponmountcategories")).Contains("Size");

        private static async Task<(WeaponMount Mount, bool Variable, RuleResult Result)> BuildAsync(Character character, Vehicle vehicle, WeaponMountConfiguration input, bool preview, CancellationToken token)
        {
            if (!ReferenceEquals(vehicle.CharacterObject, character) || !await character.Vehicles.ContainsAsync(vehicle, token).ConfigureAwait(false) || input.Markup < -100)
                return (null, false, RuleResult.NotAllowed);
            var selections = new[] { ("Size", input.SizeId), ("Control", input.ControlId), ("Flexibility", input.FlexibilityId), ("Visibility", input.VisibilityId) };
            foreach (var selection in selections)
                if (!(await OptionsAsync(character, vehicle, selection.Item1, input.SizeId, token).ConfigureAwait(false)).Any(x => x.Id == selection.Item2)) return (null, false, RuleResult.NotAllowed);
            if (input.BlackMarket && !await CanBlackMarketAsync(character, token).ConfigureAwait(false)) return (null, false, RuleResult.NotAllowed);
            var document = await character.LoadDataAsync("vehicles.xml", token: token).ConfigureAwait(false);
            bool variable = false;
            XmlNode Node(string id)
            {
                var node = document.TryGetNodeByNameOrId("/chummer/weaponmounts/weaponmount", id).CloneNode(true);
                string cost = node["cost"]?.InnerText ?? "";
                if (cost.StartsWith("Variable(", StringComparison.Ordinal))
                {
                    variable = true;
                    if (preview)
                    {
                        string minimum = cost.Substring(9).TrimEnd(')').Split('-')[0].TrimEnd('+');
                        if (!decimal.TryParse(minimum, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)) throw new FormatException("Invalid variable mount cost.");
                        node["cost"].InnerText = value.ToString(CultureInfo.InvariantCulture);
                    }
                }
                if (preview && string.IsNullOrWhiteSpace(node["notes"]?.InnerText) && string.IsNullOrWhiteSpace(node["altnotes"]?.InnerText))
                {
                    var notes = node["notes"];
                    if (notes == null) { notes = node.OwnerDocument.CreateElement("notes"); node.AppendChild(notes); }
                    notes.InnerText = " ";
                    if (node["altnotes"] != null) node["altnotes"].InnerText = " ";
                }
                return node;
            }
            var mount = new WeaponMount(character, vehicle);
            try
            {
                await mount.CreateAsync(Node(input.SizeId), token).ConfigureAwait(false);
                if (mount.InternalId.IsEmptyGuid()) { await mount.DisposeAsync().ConfigureAwait(false); return (null, variable, RuleResult.Cancelled); }
                foreach (string id in new[] { input.ControlId, input.FlexibilityId, input.VisibilityId })
                {
                    var option = new WeaponMountOption(character);
                    if (!await option.CreateAsync(Node(id), token).ConfigureAwait(false))
                    { await mount.DisposeAsync().ConfigureAwait(false); return (null, variable, RuleResult.Cancelled); }
                    await mount.WeaponMountOptions.AddAsync(option, token).ConfigureAwait(false);
                }
                mount.DiscountCost = input.BlackMarket; mount.FreeCost = input.Free;
                return (mount, variable, RuleResult.Changed);
            }
            catch { await mount.DisposeAsync().ConfigureAwait(false); throw; }
        }
        public static async Task<WeaponMountQuote> QuoteAsync(Character character, Vehicle vehicle, WeaponMountConfiguration input, CancellationToken token = default)
        {
            var build = await BuildAsync(character, vehicle, input, true, token).ConfigureAwait(false);
            if (build.Mount == null) return null;
            try
            {
                decimal cost = await CostAsync(character, build.Mount, input.Markup, token).ConfigureAwait(false);
                return new WeaponMountQuote { Name = await build.Mount.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                    Cost = cost, Availability = await build.Mount.GetDisplayTotalAvailAsync(token).ConfigureAwait(false),
                    Slots = await build.Mount.GetCalculatedSlotsAsync(token).ConfigureAwait(false), WeaponCapacity = build.Mount.WeaponCapacity,
                    AllowedWeapons = build.Mount.AllowedWeaponCategories, VariableCost = build.Variable,
                    CanAfford = input.Free || !await character.GetCreatedAsync(token).ConfigureAwait(false) || cost <= await character.GetNuyenAsync(token).ConfigureAwait(false) };
            }
            finally { await build.Mount.DisposeAsync().ConfigureAwait(false); }
        }
        private static async Task<decimal> CostAsync(Character character, WeaponMount mount, decimal markup, CancellationToken token)
        {
            decimal cost = await mount.GetTotalCostAsync(token).ConfigureAwait(false);
            return await character.GetCreatedAsync(token).ConfigureAwait(false)
                ? await PurchaseRules.StreetCostAsync(character, cost, markup, (await mount.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false) : cost;
        }
        public static async Task<PurchaseResult> PurchaseAsync(Character character, Vehicle vehicle, WeaponMountConfiguration input, CancellationToken token = default)
        {
            var build = await BuildAsync(character, vehicle, input, false, token).ConfigureAwait(false);
            if (build.Mount == null) return new PurchaseResult(build.Result);
            var mount = build.Mount; bool committed = false;
            try
            {
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                decimal cost = await CostAsync(character, mount, input.Markup, token).ConfigureAwait(false);
                if (career && !input.Free && !await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotEnoughNuyen);
                await vehicle.WeaponMounts.AddAsync(mount, token).ConfigureAwait(false);
                var settings = await character.GetSettingsAsync(token).ConfigureAwait(false);
                if (career && await settings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
                {
                    bool over = await settings.BookEnabledAsync("R5", token).ConfigureAwait(false)
                        ? vehicle.IsDrone && await settings.GetDroneModsAsync(token).ConfigureAwait(false)
                            ? await vehicle.GetDroneModSlotsUsedAsync(token).ConfigureAwait(false) > await vehicle.GetDroneModSlotsAsync(token).ConfigureAwait(false)
                            : await vehicle.OverR5CapacityAsync("Weapons", token).ConfigureAwait(false)
                        : await vehicle.GetSlotsUsedAsync(token).ConfigureAwait(false) > await vehicle.GetSlotsAsync(token).ConfigureAwait(false);
                    if (over) { await PurchaseRules.ShowCapacityReachedAsync(token).ConfigureAwait(false); return new PurchaseResult(RuleResult.NotAllowed); }
                }
                token.ThrowIfCancellationRequested();
                ExpenseLogEntry expense = null;
                if (career && !input.Free)
                    expense = await PurchaseRules.ChargeAsync(character, cost, "String_ExpensePurchaseVehicleWeaponMount", await mount.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                        NuyenExpenseType.AddVehicleWeaponMount, mount.InternalId, 0, CancellationToken.None).ConfigureAwait(false);
                committed = true;
                return new PurchaseResult(RuleResult.Changed, mount, cost, expense);
            }
            finally { if (!committed) await mount.DeleteWeaponMountAsync(token: CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
