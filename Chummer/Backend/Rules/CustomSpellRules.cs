/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    public sealed class CustomSpellDesign
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "Combat";
        public string Type { get; set; } = "P";
        public string Range { get; set; } = "T";
        public string Duration { get; set; } = "I";
        public bool Area { get; set; }
        public bool Limited { get; set; }
        public bool Restricted { get; set; }
        public bool VeryRestricted { get; set; }
        public string Restriction { get; set; } = "";
        public int NumberOfEffects { get; set; } = 1;
        public HashSet<int> Modifiers { get; } = new HashSet<int>();

        public void SetModifier(int index, bool value)
        {
            if (!value) { Modifiers.Remove(index); return; }
            Modifiers.Add(index);
            int[][] groups = CustomSpellRules.ExclusiveGroups(Category);
            foreach (var group in groups)
                if (group.Contains(index)) foreach (int other in group) if (other != index) Modifiers.Remove(other);
            if (Category == "Combat")
            {
                if (index == 1) Modifiers.Remove(3);
                if (index == 3) { Modifiers.Remove(1); Modifiers.Add(2); }
                if (Modifiers.Contains(2)) Type = "P";
            }
            // Preserve the legacy modifier event's Active detection dependency.
            if (Category == "Detection" && Modifiers.Contains(4))
            { Modifiers.Remove(1); Modifiers.Remove(3); Modifiers.Add(2); }
        }
    }

    public sealed class CustomSpellModifier
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Drain { get; set; }
    }

    public static class CustomSpellRules
    {
        // CreateSpell.ChangeModifiers: indices are the original checkbox indices.
        private static readonly Dictionary<string, int[]> Costs = new Dictionary<string, int[]>
        {
            ["Combat"] = new[] { 0, 0, 2, 0, -1 },
            ["Detection"] = new[] { 0, 0, 0, 0, 0, 0, 1, 1, 2, 4, 1, 2, 4, 2 },
            ["Health"] = new[] { 0, 4, -2, 2, -2 },
            ["Illusion"] = new[] { -1, 0, -2, 0, 2 },
            ["Manipulation"] = new[] { -2, 0, 0, 0, 2, 2 }
        };
        private static string ModifierCategory(string category) => Costs.ContainsKey(category) ? category : "Combat";
        public static IReadOnlyList<CustomSpellModifier> Modifiers(string category)
        {
            string key = ModifierCategory(category);
            return Costs[key].Select((cost, index) => new CustomSpellModifier { Index = index + 1, Drain = cost,
                Name = LanguageManager.GetString("Checkbox_" + key + "Spell" + (index + 1).ToString(CultureInfo.InvariantCulture)) }).ToList();
        }
        internal static int[][] ExclusiveGroups(string category)
        {
            switch (category)
            {
                case "Combat": return new[] { new[] { 1, 2 }, new[] { 4, 5 } };
                case "Detection": return new[] { new[] { 1, 2, 3 }, new[] { 4, 5 } };
                case "Illusion": return new[] { new[] { 1, 2 }, new[] { 3, 4 } };
                case "Manipulation": return new[] { new[] { 1, 2, 3 }, new[] { 4, 5 } };
                default: return Array.Empty<int[]>();
            }
        }

        public static IReadOnlyList<string> Validate(CustomSpellDesign design)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(design.Name)) errors.Add(LanguageManager.GetString("Message_SpellName"));
            if ((design.Restricted || design.VeryRestricted) && string.IsNullOrWhiteSpace(design.Restriction)) errors.Add(LanguageManager.GetString("Message_SpellRestricted"));
            if (!Costs.ContainsKey(design.Category) && design.Category != "Rituals" && design.Category != "Enchantments") errors.Add("Choose a spell category.");
            if (design.Type != "M" && design.Type != "P") errors.Add("Choose Physical or Mana.");
            if (design.Range != "T" && design.Range != "LOS") errors.Add("Choose Touch or Line of sight.");
            if (!new[] { "I", "S", "P" }.Contains(design.Duration)) errors.Add("Choose a spell duration.");
            if (design.NumberOfEffects < 1 || design.NumberOfEffects > 10) errors.Add("Choose between 1 and 10 effects.");
            if (design.Restricted && design.VeryRestricted) errors.Add("Choose one restriction level.");
            if (design.Category == "Health" && design.Area) errors.Add("Health spells cannot affect an area.");
            if (design.Modifiers.Any(x => x < 1 || x > Costs[ModifierCategory(design.Category)].Length)) errors.Add("Invalid spell modifier.");
            foreach (var group in ExclusiveGroups(design.Category))
                if (group.Count(x => design.Modifiers.Contains(x)) > 1) errors.Add("Conflicting spell modifiers.");
            if (design.Category == "Combat" && design.Modifiers.Contains(2) && design.Type != "P") errors.Add("Indirect combat spells must be Physical.");
            if (design.Category == "Combat" && design.Modifiers.Contains(3) && !design.Modifiers.Contains(2)) errors.Add("Elemental combat spells must be Indirect.");
            Action<int[], string> require = (indices, key) => { if (!indices.Any(x => design.Modifiers.Contains(x))) errors.Add(LanguageManager.GetString(key)); };
            switch (design.Category)
            {
                case "Combat": require(new[] { 1, 2 }, "Message_CombatSpellRequirement1"); require(new[] { 4, 5 }, "Message_CombatSpellRequirement2"); break;
                case "Detection": require(new[] { 1, 2, 3 }, "Message_DetectionSpellRequirement1"); require(new[] { 4, 5 }, "Message_DetectionSpellRequirement2"); break;
                case "Illusion": require(new[] { 1, 2 }, "Message_IllusionSpellRequirement1"); require(new[] { 3, 4 }, "Message_IllusionSpellRequirement2"); break;
                case "Manipulation": require(new[] { 1, 2, 3 }, "Message_ManipulationSpellRequirement1"); require(new[] { 4, 5 }, "Message_ManipulationSpellRequirement2"); break;
            }
            return errors;
        }

        /// <summary>Legacy CreateSpell.CalculateDrain; Limited affects the engine's drain handling separately.</summary>
        public static string Drain(CustomSpellDesign design)
        {
            int value = design.Type == "M" ? 0 : 1;
            if (design.Range == "T") value -= 2;
            if (design.Area) value += 2;
            if (design.Restricted) --value;
            if (design.VeryRestricted) value -= 2;
            bool curative = design.Category == "Health" && design.Modifiers.Contains(1);
            if (design.Duration == "P" && !curative) value += 2;
            var costs = Costs[ModifierCategory(design.Category)];
            foreach (int index in design.Modifiers.Where(x => x > 0 && x <= costs.Length))
                value += costs[index - 1] * ((design.Category == "Combat" && index == 3 || design.Category == "Manipulation" && index == 6) ? design.NumberOfEffects : 1);
            string basis = curative ? "(" + LanguageManager.GetString("String_SpellDamageValue") + ")" : "(F/2)";
            return basis + (value == 0 ? "" : value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture));
        }

        public static string Descriptors(CustomSpellDesign design)
        {
            var result = new List<string>();
            Action<int, string> add = (index, label) => { if (design.Modifiers.Contains(index)) result.Add(label); };
            switch (design.Category)
            {
                case "Detection": add(4, "Active"); add(5, "Passive"); add(1, "Directional"); add(3, "Psychic"); add(2, design.Modifiers.Contains(14) ? "Extended Area" : "Area"); break;
                case "Health": add(4, "Negative"); break;
                case "Illusion": add(1, "Obvious"); add(2, "Realistic"); add(3, "Single-Sense"); add(4, "Multi-Sense"); if (design.Area) result.Add("Area"); break;
                case "Manipulation": add(1, "Environmental"); add(2, "Mental"); add(3, "Physical"); if (design.Area) result.Add("Area"); break;
                default: add(1, "Direct"); add(2, "Indirect"); add(3, "Elemental"); break;
            }
            return string.Join(", ", result);
        }

        public static async Task<RuleResult> AddAsync(Character character, CustomSpellDesign design, CancellationToken token = default)
        {
            var errors = Validate(design);
            if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(design));
            bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
            if (!career && !await character.GetIgnoreRulesAsync(token).ConfigureAwait(false))
            {
                int rating = 0;
                foreach (string name in new[] { "Spellcasting", "Ritual Spellcasting" })
                {
                    var skill = await character.SkillsSection.GetActiveSkillAsync(name, token).ConfigureAwait(false);
                    if (skill != null) rating = Math.Max(rating, await skill.GetRatingAsync(token).ConfigureAwait(false));
                }
                if (await character.Spells.GetCountAsync(token).ConfigureAwait(false) >= rating * 2 + await ImprovementManager.ValueOfAsync(character, Improvement.ImprovementType.SpellLimit, token: token).ConfigureAwait(false))
                    return RuleResult.NotAllowed;
            }
            var spell = new Spell(character) { Name = design.Name.Trim(), Source = "SM", Page = "159", Category = design.Category,
                Descriptors = Descriptors(design), Type = design.Type, Range = design.Range + (design.Area ? "(A)" : ""), Duration = design.Duration,
                Limited = design.Limited, DvBase = Drain(design), Damage = design.Category == "Combat" ? design.Modifiers.Contains(4) ? "P" : "S" : "",
                Extra = design.Restricted || design.VeryRestricted ? design.Restriction.Trim() : "" };
            try
            {
                if (career)
                {
                    int cost = await MagicRules.GetSpellCostAsync(character, false, design.Category == "Rituals", token).ConfigureAwait(false);
                    if (await character.GetKarmaAsync(token).ConfigureAwait(false) < cost)
                    { await spell.DisposeAsync().ConfigureAwait(false); await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false); return RuleResult.NotEnoughKarma; }
                    if (!await CommonFunctions.ConfirmKarmaExpenseAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                            await spell.GetCurrentDisplayNameAsync(token).ConfigureAwait(false), cost), token).ConfigureAwait(false))
                    { await spell.DisposeAsync().ConfigureAwait(false); return RuleResult.Cancelled; }
                    await MagicRules.AddKarmaExpenseAsync(character, cost, "String_ExpenseLearnSpell", await spell.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), KarmaExpenseType.AddSpell, spell.InternalId, token).ConfigureAwait(false);
                }
                await character.Spells.AddAsync(spell, token).ConfigureAwait(false);
                return RuleResult.Changed;
            }
            catch { await spell.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false); throw; }
        }
    }
}
