/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Attributes;
using Chummer.Selection;

namespace Chummer.Rules
{
    public sealed class CustomImprovementDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Help { get; set; }
        public IReadOnlyList<string> Fields { get; set; }
        public string Selection { get; set; }
        public string SelectionFile { get; set; }
        public string SelectionXPath { get; set; }
    }

    public sealed class CustomImprovementInput
    {
        public string DefinitionId { get; set; }
        public string Name { get; set; }
        public string Group { get; set; } = string.Empty;
        public string Selection { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public int AugmentedMaximum { get; set; }
        public bool ApplyToRating { get; set; }
        public bool Free { get; set; }
    }

    public static class CustomImprovementRules
    {
        public static IReadOnlyList<CustomImprovementDefinition> Definitions(Character character)
        {
            return character.LoadDataXPath("improvements.xml").Select("/chummer/improvements/improvement")
                .Cast<XPathNavigator>().Select(node =>
                {
                    XPathNavigator selection = node.Select("fields/field").Cast<XPathNavigator>().FirstOrDefault(x => x.Value.StartsWith("Select", StringComparison.OrdinalIgnoreCase));
                    return new CustomImprovementDefinition
                    {
                        Id = node.SelectSingleNode("id")?.Value,
                        Name = node.SelectSingleNode("translate")?.Value ?? node.SelectSingleNode("name")?.Value,
                        Help = node.SelectSingleNode("altpage")?.Value ?? node.SelectSingleNode("page")?.Value,
                        Fields = node.Select("fields/field").Cast<XPathNavigator>().Select(x => x.Value).ToList(),
                        Selection = selection?.Value,
                        SelectionFile = selection?.GetAttribute("xml", ""),
                        SelectionXPath = selection?.GetAttribute("xpath", "")
                    };
                }).Where(x => !string.IsNullOrEmpty(x.Id)).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        public static IReadOnlyList<ListItem> SelectionOptions(Character character, CustomImprovementDefinition definition)
        {
            string file, path;
            switch (definition.Selection)
            {
                case "SelectAttribute":
                case "SelectPhysicalAttribute":
                case "SelectMentalAttribute":
                case "SelectSpecialAttribute":
                    IEnumerable<string> attributes = AttributeSection.AttributeStrings.Where(x => x != "ESS");
                    if (definition.Selection == "SelectPhysicalAttribute") attributes = AttributeSection.PhysicalAttributes;
                    else if (definition.Selection == "SelectMentalAttribute") attributes = AttributeSection.MentalAttributes;
                    else if (definition.Selection == "SelectSpecialAttribute")
                        attributes = attributes.Where(x => !AttributeSection.PhysicalAttributes.Contains(x) && !AttributeSection.MentalAttributes.Contains(x));
                    else attributes = attributes.Where(x => x != "MAG" || character.MAGEnabled)
                        .Where(x => x != "MAGAdept" || character.MAGEnabled && character.IsMysticAdept && character.Settings.MysAdeptSecondMAGAttribute)
                        .Where(x => x != "RES" || character.RESEnabled).Where(x => x != "DEP" || character.DEPEnabled);
                    return attributes.Select(x => new ListItem(x, x)).ToList();
                case "SelectActionDicePool": file = "actions.xml"; path = "/chummer/actions/action"; break;
                case "SelectSkill":
                    return SkillSelector.GetSkillOptions(character, new SkillSelectionFilter(), out _, out _)
                        .Select(x => new ListItem(((ValueTuple<string, bool>)x.Value).Item1, x.Name)).ToList();
                case "SelectKnowSkill": file = "skills.xml"; path = "/chummer/knowledgeskills/skill"; break;
                case "SelectSkillCategory": file = "skills.xml"; path = "/chummer/categories/category"; break;
                case "SelectSkillGroup": file = "skills.xml"; path = "/chummer/skillgroups/name"; break;
                case "SelectSpell": file = "spells.xml"; path = "/chummer/spells/spell"; break;
                case "SelectSpellCategory": file = "spells.xml"; path = "/chummer/categories/category"; break;
                case "SelectWeaponCategory": file = "weapons.xml"; path = "/chummer/categories/category"; break;
                case "SelectAdeptPower": file = "powers.xml"; path = "/chummer/powers/power"; break;
                case "SelectComplexForm": file = "complexforms.xml"; path = "/chummer/complexforms/complexform"; break;
                case "SelectMetamagic": file = "metamagic.xml"; path = "/chummer/metamagics/metamagic"; break;
                case "SelectEcho": file = "echoes.xml"; path = "/chummer/echoes/echo"; break;
                case "SelectXPath": file = definition.SelectionFile; path = definition.SelectionXPath; break;
                default: return Array.Empty<ListItem>();
            }
            var values = character.LoadDataXPath(file).Select(path).Cast<XPathNavigator>().Select(node =>
            {
                string name = node.SelectSingleNode("name")?.Value ?? node.Value;
                string translated = node.SelectSingleNode("translate")?.Value ?? node.GetAttribute("translate", "");
                return new ListItem(name, string.IsNullOrEmpty(translated) ? name : translated);
            }).ToList();
            if (definition.Selection == "SelectKnowSkill")
                values.AddRange(character.SkillsSection.KnowledgeSkills.Select(x => new ListItem(x.Name, x.CurrentDisplayName)));
            return values.GroupBy(x => x.Value?.ToString()).Select(x => x.First()).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>Build the same bonus XML as CreateImprovement.AcceptForm and let the engine apply its effects.</summary>
        public static async Task<RuleResult> SaveAsync(Character character, CustomImprovementInput input, Improvement existing = null, CancellationToken token = default)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Name) || existing != null && !existing.Custom) return RuleResult.NotAllowed;
            bool wasEnabled = existing?.Enabled ?? true;
            XPathNavigator definition = (await character.LoadDataXPathAsync("improvements.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/improvements/improvement", input.DefinitionId, blnIdIsGuid: false);
            string element = definition?.SelectSingleNode("internal")?.Value;
            if (string.IsNullOrEmpty(element)) return RuleResult.NotAllowed;
            if (definition.Select("fields/field").Cast<XPathNavigator>().Any(x => x.Value.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrEmpty(input.Selection)) return RuleResult.NotAllowed;
            string xml = definition.SelectSingleNode("xml")?.Value ?? string.Empty;
            xml = xml.Replace("{val}", input.Value.ToString(CultureInfo.InvariantCulture))
                .Replace("{min}", input.Minimum.ToString(CultureInfo.InvariantCulture)).Replace("{max}", input.Maximum.ToString(CultureInfo.InvariantCulture))
                .Replace("{aug}", input.AugmentedMaximum.ToString(CultureInfo.InvariantCulture)).Replace("{percent}", input.AugmentedMaximum.ToString(CultureInfo.InvariantCulture))
                .Replace("{free}", input.Free.ToString(CultureInfo.InvariantCulture)).Replace("{select}", SecurityElement.Escape(input.Selection) ?? string.Empty)
                .Replace("{applytorating}", input.ApplyToRating ? "<applytorating>True</applytorating>" : string.Empty);
            XmlDocument document = new XmlDocument { XmlResolver = null };
            XmlElement bonus = document.CreateElement("bonus"); document.AppendChild(bonus);
            XmlElement node = document.CreateElement(element); bonus.AppendChild(node);
            foreach (XPathNavigator attribute in definition.Select("xml/@*")) node.SetAttribute(attribute.LocalName, attribute.Value);
            node.InnerXml = xml;
            string id = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
            if (!await ImprovementManager.CreateImprovementsAsync(character, Improvement.ImprovementSource.Custom, id, bonus,
                strFriendlyName: input.Name, token: token).ConfigureAwait(false)) return RuleResult.Cancelled;
            var created = character.Improvements.Where(x => x.SourceName == id).ToList();
            if (created.Count == 0) return RuleResult.NotAllowed;
            foreach (Improvement improvement in created)
            {
                improvement.CustomName = input.Name.Trim(); improvement.CustomId = input.DefinitionId; improvement.Custom = true;
                improvement.CustomGroup = input.Group ?? string.Empty;
                if (existing != null)
                {
                    await improvement.SetNotesAsync(await existing.GetNotesAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                    improvement.SortOrder = existing.SortOrder;
                }
            }
            if (existing != null)
            {
                await ImprovementManager.RemoveImprovementsAsync(character, Improvement.ImprovementSource.Custom, existing.SourceName, token).ConfigureAwait(false);
                if (!wasEnabled) await ImprovementManager.DisableImprovementsAsync(character, created, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        public static Task SetEnabledAsync(Character character, Improvement improvement, bool enabled, CancellationToken token = default)
        {
            if (improvement == null || !improvement.Custom) return Task.CompletedTask;
            var linked = character.Improvements.Where(x => x.SourceName == improvement.SourceName && x.ImproveSource == Improvement.ImprovementSource.Custom).ToList();
            return enabled ? ImprovementManager.EnableImprovementsAsync(character, linked, token) : ImprovementManager.DisableImprovementsAsync(character, linked, token);
        }

        public static async Task<RuleResult> RemoveAsync(Character character, Improvement improvement, CancellationToken token = default)
        {
            if (improvement == null || !improvement.Custom) return RuleResult.NotAllowed;
            if (!await CommonFunctions.ConfirmDeleteAsync(await LanguageManager.GetStringAsync("Message_DeleteImprovement", token: token).ConfigureAwait(false), token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await ImprovementManager.RemoveImprovementsAsync(character, Improvement.ImprovementSource.Custom, improvement.SourceName, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }
    }
}
