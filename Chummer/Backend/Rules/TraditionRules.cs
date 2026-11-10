/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Chummer.Backend.Uniques;

namespace Chummer.Rules
{
    public sealed class TraditionOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Drain { get; set; }
        public string Source { get; set; }
        public IReadOnlyList<string> Spirits { get; set; }
        public bool IsCustom => Id == Tradition.CustomMagicalTraditionGuidString;
    }

    public static class TraditionRules
    {
        public static IReadOnlyList<TraditionOption> Options(Character character)
        {
            return character.LoadDataXPath(character.RESEnabled ? "streams.xml" : "traditions.xml")
                .Select("/chummer/traditions/tradition[" + character.Settings.BookXPath() + "]")
                .Cast<XPathNavigator>().Select(node => new TraditionOption
                {
                    Id = node.SelectSingleNode("id")?.Value,
                    Name = node.SelectSingleNode("translate")?.Value ?? node.SelectSingleNode("name")?.Value,
                    Drain = node.SelectSingleNode("drain")?.Value ?? string.Empty,
                    Source = (node.SelectSingleNode("source")?.Value + " " + (node.SelectSingleNode("altpage")?.Value ?? node.SelectSingleNode("page")?.Value)).Trim(),
                    Spirits = node.Select("spirits/*").Cast<XPathNavigator>().Select(x => x.Value).ToList()
                }).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>Legacy cboTradition_SelectedIndexChanged and custom tradition bindings.</summary>
        public static async Task<RuleResult> SetAsync(Character character, string id, string customName = null,
            string customDrain = null, IReadOnlyList<string> customSpirits = null, CancellationToken token = default)
        {
            if (!character.ShouldHaveTradition || string.IsNullOrEmpty(id)) return RuleResult.NotAllowed;
            if (!Options(character).Any(x => x.Id == id)) return RuleResult.NotAllowed;
            bool custom = id == Tradition.CustomMagicalTraditionGuidString;
            if (custom && (string.IsNullOrWhiteSpace(customName) || string.IsNullOrWhiteSpace(customDrain)
                || !CommonFunctions.IsCharacterAttributeXPathValidOrNull(customDrain))) return RuleResult.NotAllowed;
            var node = (await character.LoadDataAsync(character.RESEnabled ? "streams.xml" : "traditions.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/traditions/tradition", id);
            if (node == null) return RuleResult.NotAllowed;
            Tradition tradition = await character.GetMagicTraditionAsync(token).ConfigureAwait(false);
            if (!await tradition.CreateAsync(node, character.RESEnabled, token: token).ConfigureAwait(false)) return RuleResult.Cancelled;
            if (custom)
            {
                await tradition.SetNameAsync(customName.Trim(), token).ConfigureAwait(false);
                await tradition.SetDrainExpressionAsync(customDrain.Trim(), token).ConfigureAwait(false);
                if (customSpirits != null && customSpirits.Count == 5)
                {
                    tradition.SpiritCombat = customSpirits[0]; tradition.SpiritDetection = customSpirits[1];
                    tradition.SpiritHealth = customSpirits[2]; tradition.SpiritIllusion = customSpirits[3]; tradition.SpiritManipulation = customSpirits[4];
                }
            }
            return RuleResult.Changed;
        }
    }
}
