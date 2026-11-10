using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public sealed class LifeModuleOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Stage { get; set; }
        public int Karma { get; set; }
        public string Source { get; set; }
    }

    public static class LifeModuleRules
    {
        public static IReadOnlyList<ListItem> Stages(Character character)
            => character.LoadDataXPath("lifemodules.xml").Select("/chummer/stages/stage").Cast<XPathNavigator>()
                .OrderBy(x => int.TryParse(x.GetAttribute("order", ""), out int order) ? order : int.MaxValue)
                .Select(x => new ListItem(x.Value, x.GetAttribute("translate", "") is string translated && translated.Length > 0 ? translated : x.Value)).ToList();

        public static string NextStage(Character character)
        {
            var stages = Stages(character);
            foreach (var stage in stages.Take(4))
                if (!character.Qualities.Any(x => x.Type == QualityType.LifeModule && x.Stage == stage.Value.ToString())) return stage.Value.ToString();
            return stages.LastOrDefault().Value?.ToString() ?? string.Empty;
        }

        public static async Task<IReadOnlyList<LifeModuleOption>> OptionsAsync(Character character, string stage = null, bool enforceRequirements = true, CancellationToken token = default)
        {
            var document = await character.LoadDataAsync("lifemodules.xml", token: token).ConfigureAwait(false);
            var result = new List<LifeModuleOption>();
            string filter = character.Settings.BookXPath();
            foreach (XmlNode module in document.SelectNodes("/chummer/modules/module[" + filter + "]"))
            {
                if (!string.IsNullOrEmpty(stage) && module["stage"]?.InnerText != stage) continue;
                await AddOptionsAsync(module, string.Empty, document, result, character, enforceRequirements, token).ConfigureAwait(false);
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        private static async Task AddOptionsAsync(XmlNode node, string parent, XmlDocument document, List<LifeModuleOption> result,
            Character character, bool enforce, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (enforce && !await node.CreateNavigator().RequirementsMetAsync(character, token: token).ConfigureAwait(false)) return;
            string name = node["translate"]?.InnerText ?? node["name"]?.InnerText ?? "";
            string display = parent.Length == 0 ? name : parent + " / " + name;
            string id = node["id"]?.InnerText;
            if (!string.IsNullOrEmpty(id) && node["selectable"]?.InnerText != bool.FalseString)
            {
                XmlNode resolved = Quality.GetNodeOverrideable(id, document);
                result.Add(new LifeModuleOption { Id = id, Name = display, Stage = resolved["stage"]?.InnerText ?? "",
                    Karma = int.TryParse(resolved["karma"]?.InnerText, out int cost) ? cost * character.Settings.KarmaQuality : 0,
                    Source = (resolved["source"]?.InnerText + " " + (resolved["altpage"]?.InnerText ?? resolved["page"]?.InnerText)).Trim() });
            }
            foreach (XmlNode version in node.SelectNodes("versions/version[(" + character.Settings.BookXPath() + ") or not(source)]"))
                await AddOptionsAsync(version, display, document, result, character, enforce, token).ConfigureAwait(false);
        }

        /// <summary>Legacy CharacterCreate.cmdAddLifeModule: create the inherited module as a quality and add granted weapons.</summary>
        public static async Task<RuleResult> AddAsync(Character character, string id, bool enforceRequirements = true, CancellationToken token = default)
        {
            if (character.Created || character.EffectiveBuildMethod != CharacterBuildMethod.LifeModule) return RuleResult.NotAllowed;
            if (!(await OptionsAsync(character, null, enforceRequirements, token).ConfigureAwait(false)).Any(x => x.Id == id)) return RuleResult.NotAllowed;
            XmlNode module = Quality.GetNodeOverrideable(id, await character.LoadDataAsync("lifemodules.xml", token: token).ConfigureAwait(false));
            var weapons = new List<Weapon>();
            var quality = new Quality(character);
            try
            {
                await quality.CreateAsync(module, QualitySource.LifeModule, weapons, token: token).ConfigureAwait(false);
                if (quality.InternalId.IsEmptyGuid())
                {
                    await quality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                    return RuleResult.Cancelled;
                }
                await character.Qualities.AddAsync(quality, token).ConfigureAwait(false);
                foreach (Weapon weapon in weapons) await character.Weapons.AddAsync(weapon, token).ConfigureAwait(false);
                return RuleResult.Changed;
            }
            catch
            {
                await character.Qualities.RemoveAsync(quality, CancellationToken.None).ConfigureAwait(false);
                await quality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
    }
}
