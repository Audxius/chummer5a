/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chummer.Rules
{
    /// <summary>Short play-facing explanations; numeric modifiers come from this character's actual improvements.</summary>
    public static class QualityDescriptionRules
    {
        private static readonly Dictionary<string, string> Summaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["First Impression"] = "You make a stronger first impression in initial social encounters. The social bonus applies when meeting someone for the first time.",
            ["Analytical Mind"] = "You are especially good at recognizing patterns, analyzing evidence and solving puzzles. Apply the conditional Logic-related bonus when those tasks are involved.",
            ["Low-Light Vision"] = "Your vision works better in dim light. Apply the low-light vision rules to lighting penalties; this is not thermal vision.",
            ["Signature"] = "You habitually leave a recognizable calling card at your jobs, giving investigators a way to link incidents to you.",
            ["Codeblock"] = "Take a two-dice penalty on the Matrix action selected for this quality.",
            ["Distinctive Style"] = "Your recognizable appearance or manner makes you easier to identify and track. A disguise must conceal those distinctive features.",
            ["Practice, Practice, Practice"] = "Raises the applicable limit for the selected non-combat skill by one; it does not add a die to that skill's pool.",
            ["Too Pretty To Hit"] = "When using Full Defense, you may use Charisma in place of Willpower.",
            ["Ambidextrous"] = "You can use either hand without the usual off-hand penalty.",
            ["Blandness"] = "You are easy to overlook and difficult to describe accurately, making identification and tracking harder.",
            ["Photographic Memory"] = "You have an unusually accurate memory and receive the quality's benefit on tests to recall information.",
            ["Quick Healer"] = "You recover more readily from injuries. Apply the healing modifiers in the quality's rule reference.",
            ["Natural Athlete"] = "You have a natural advantage on running and gymnastics tasks.",
            ["Exceptional Attribute"] = "Raises the natural maximum of the chosen attribute; buying an additional attribute rating is still a separate advancement.",
            ["Lucky"] = "Raises your maximum Edge. Increasing the Edge rating itself is still a separate advancement.",
            ["High Pain Tolerance"] = "You can tolerate more injury before wound penalties interfere with your actions. The benefit depends on the quality's level."
        };

        public static string Description(Quality quality)
        {
            var node = quality.GetNode();
            string custom = node?["altdescription"]?.InnerText ?? node?["description"]?.InnerText;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            return Summaries.TryGetValue(quality.Name, out string summary) ? summary
                : "A situational quality. Check its rule reference for the specific triggers and exceptions.";
        }

        /// <summary>Read-only catalog preview: never creates a quality or applies improvements.</summary>
        public static string Description(System.Xml.XPath.XPathNavigator node)
        {
            if (node == null) return string.Empty;
            string custom = node.SelectSingleNode("altdescription")?.Value;
            if (string.IsNullOrWhiteSpace(custom)) custom = node.SelectSingleNode("description")?.Value;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            string name = node.SelectSingleNode("name")?.Value ?? string.Empty;
            return Summaries.TryGetValue(name, out string summary) ? summary
                : "No rules description is supplied by this data pack. Consult the source reference below for this quality's effects and exceptions.";
        }

        public static string PreviewModifiers(System.Xml.XPath.XPathNavigator node)
        {
            if (node == null) return string.Empty;
            var lines = new List<string>();
            foreach (System.Xml.XPath.XPathNavigator effect in node.Select("bonus/skill | bonus/skillcategory | bonus/skillattribute | bonus/notoriety"))
            {
                string amount = effect.Name == "notoriety" ? effect.Value : effect.SelectSingleNode("bonus")?.Value;
                if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)) continue;
                string target = effect.Name == "notoriety" ? "Notoriety" : effect.SelectSingleNode("name")?.Value;
                if (string.IsNullOrWhiteSpace(target)) continue;
                string condition = effect.SelectSingleNode("condition")?.Value;
                string label = effect.Name == "skillattribute" ? target + "-linked tests" : target;
                lines.Add(label + ": " + value.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture)
                    + (string.IsNullOrWhiteSpace(condition) ? "" : " (" + condition + ")"));
            }
            return string.Join("\n", lines);
        }

        public static string Modifiers(Character character, Quality quality)
        {
            var ids = character.Qualities.Where(x => x.SourceID == quality.SourceID && x.Extra == quality.Extra && x.SourceName == quality.SourceName)
                .Select(x => x.InternalId).ToArray();
            var effects = character.Improvements.Where(x => x.ImproveSource == Improvement.ImprovementSource.Quality
                && ids.Any(id => x.SourceName == id || x.SourceName.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase)) && x.Value != 0)
                .GroupBy(x => new { x.ImproveType, x.ImprovedName, Condition = x.CurrentDisplayCondition, x.Enabled });
            return string.Join("\n", effects.Select(group =>
            {
                string label = Regex.Replace(group.Key.ImproveType.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
                string target = string.IsNullOrEmpty(group.Key.ImprovedName) ? "" : " — " + character.TranslateExtra(group.Key.ImprovedName);
                string condition = string.IsNullOrWhiteSpace(group.Key.Condition) ? "" : " (" + group.Key.Condition + ")";
                return (group.Key.Enabled ? "" : "Inactive: ") + label + target + ": " + group.Sum(x => x.Value).ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) + condition;
            }));
        }
    }
}
