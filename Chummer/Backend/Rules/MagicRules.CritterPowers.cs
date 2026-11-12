/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public sealed class CritterPowerChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Details { get; set; }
        public bool HasRating { get; set; }
        public decimal PowerPoints { get; set; }
    }
    public static partial class MagicRules
    {
        /// <summary>Optional/restored powers, category limits, physical-presence alternatives and requirements from SelectCritterPower.</summary>
        public static List<CritterPowerChoice> CritterPowerOptions(Character character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            XPathNavigator root = character.LoadDataXPath("critterpowers.xml");
            XPathNavigator metatype = character.GetNodeXPath();
            if (metatype != null && character.MetavariantGuid != Guid.Empty)
                metatype = metatype.TryGetNodeById("metavariants/metavariant", character.MetavariantGuid) ?? metatype;
            var whitelist = new HashSet<string>(StringComparer.Ordinal);
            var optional = metatype?.SelectSingleNode("optionalpowers");
            if (optional != null)
            {
                foreach (XPathNavigator node in optional.Select("power")) whitelist.Add(node.Value);
                bool physical = character.CritterPowers.Any(x => x.Name == "Materialization" || x.Name == "Possession" || x.Name == "Inhabitation");
                foreach (XPathNavigator power in metatype.Select("powers/power"))
                {
                    if (character.CritterPowers.Any(x => x.Name == power.Value) || physical) continue;
                    whitelist.Add(power.Value);
                    if (power.Value == "Materialization") { whitelist.Add("Possession"); whitelist.Add("Inhabitation"); }
                }
            }
            var allowed = ImprovementManager.GetCachedImprovementListForValueOf(character, Improvement.ImprovementType.AllowCritterPowerCategory);
            var limited = ImprovementManager.GetCachedImprovementListForValueOf(character, Improvement.ImprovementType.LimitCritterPowerCategory);
            var categories = new List<string>();
            foreach (XPathNavigator category in root.Select("/chummer/categories/category"))
            {
                string name = category.Value;
                bool expresslyAllowed = allowed.Any(x => name.Contains(x.ImprovedName)) && category.GetAttribute("whitelist", "") == bool.TrueString;
                if (expresslyAllowed || limited.Any(x => name.Contains(x.ImprovedName)) || !limited.Any(x => !name.Contains(x.ImprovedName))) categories.Add(name);
            }
            string books = character.Settings.BookXPath();
            var choices = new List<CritterPowerChoice>();
            foreach (XPathNavigator node in root.Select("/chummer/powers/power[" + (string.IsNullOrEmpty(books) ? "true()" : books) + "]"))
            {
                string name = node.SelectSingleNode("name")?.Value ?? "";
                string category = node.SelectSingleNode("category")?.Value ?? "";
                if (categories.Count > 0 && !categories.Any(x => category.Contains(x)) && !(categories.Contains("Toxic Critter Powers") && node.SelectSingleNode("toxic")?.Value == bool.TrueString)) continue;
                if (!categories.Contains("Toxic Critter Powers") && node.SelectSingleNode("toxic")?.Value == bool.TrueString) continue;
                if (whitelist.Count > 0 && !whitelist.Contains(name) || !node.RequirementsMet(character)) continue;
                decimal points = 0;
                if (character.Metatype == "Free Spirit" && !character.IsCritter && optional != null)
                    decimal.TryParse(optional.SelectSingleNode("power[. = " + name.CleanXPath() + "]/@cost")?.Value, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out points);
                choices.Add(new CritterPowerChoice { Id = node.SelectSingleNode("id")?.Value, Name = node.SelectSingleNode("translate")?.Value ?? name,
                    HasRating = node.SelectSingleNode("rating") != null, PowerPoints = points,
                    Details = string.Join(" · ", new[] { category, node.SelectSingleNode("type")?.Value, node.SelectSingleNode("action")?.Value, node.SelectSingleNode("range")?.Value, node.SelectSingleNode("duration")?.Value }.Where(x => !string.IsNullOrWhiteSpace(x)))
                        + "\nKarma: " + (node.SelectSingleNode("karma")?.Value ?? "0") + "\n" + node.SelectSingleNode("source")?.Value + " " + node.SelectSingleNode("page")?.Value
                        + "\n\n" + (node.SelectSingleNode("altnotes")?.Value ?? node.SelectSingleNode("notes")?.Value ?? "") });
            }
            return choices.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        public static async Task<RuleResult> AddCritterPowerAsync(Character character, string id, int rating = 0, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (character == null) throw new ArgumentNullException(nameof(character));
            CritterPowerChoice choice = CritterPowerOptions(character).FirstOrDefault(x => x.Id == id);
            if (choice == null || (choice.HasRating ? rating < 1 || rating > 100 : rating != 0)) return RuleResult.NotAllowed;
            var node = (await character.LoadDataAsync("critterpowers.xml", token: token).ConfigureAwait(false)).TryGetNodeByNameOrId("/chummer/powers/power", id);
            var power = new CritterPower(character);
            bool retained = false;
            try
            {
                await power.CreateAsync(node, rating, token: token).ConfigureAwait(false);
                if (power.InternalId.IsEmptyGuid()) return RuleResult.Cancelled;
                power.PowerPoints = choice.PowerPoints;
                if (character.Created)
                {
                    if (power.Karma > character.Karma) { await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false); return RuleResult.NotEnoughKarma; }
                    if (!await CommonFunctions.ConfirmKarmaExpenseAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false), power.CurrentDisplayName, power.Karma), token).ConfigureAwait(false)) return RuleResult.Cancelled;
                }
                token.ThrowIfCancellationRequested();
                await character.CritterPowers.AddAsync(power, CancellationToken.None).ConfigureAwait(false);
                if (character.Created) await AddKarmaExpenseAsync(character, power.Karma, "String_ExpensePurchaseCritterPower", power.CurrentDisplayNameShort, KarmaExpenseType.AddCritterPower, power.InternalId, CancellationToken.None).ConfigureAwait(false);
                retained = true; return RuleResult.Changed;
            }
            finally { if (!retained) await power.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
