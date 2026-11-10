/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public sealed class NaturalWeaponDesign
    {
        public string Name { get; set; } = "";
        public string Skill { get; set; } = "Unarmed Combat";
        public string DamageBase { get; set; } = "({STR}/2)";
        public int DamageModifier { get; set; }
        public string DamageType { get; set; } = "P";
        public int ArmorPenetration { get; set; }
        public int Reach { get; set; }
    }

    public static class NaturalWeaponRules
    {
        public static async Task<RuleResult> AddAsync(Character character, NaturalWeaponDesign design, CancellationToken token = default)
        {
            if (design == null || string.IsNullOrWhiteSpace(design.Name) || (design.DamageType != "P" && design.DamageType != "S")
                || design.DamageModifier < -10 || design.DamageModifier > 10 || design.ArmorPenetration < -10 || design.ArmorPenetration > 10
                || design.Reach < -10 || design.Reach > 10) return RuleResult.NotAllowed;
            if (design.DamageBase != "({STR}/2)" && design.DamageBase != "({STR})"
                && (!int.TryParse(design.DamageBase, NumberStyles.None, CultureInfo.InvariantCulture, out int fixedDamage) || fixedDamage < 1 || fixedDamage > 20)) return RuleResult.NotAllowed;
            var skills = await character.LoadDataXPathAsync("skills.xml", token: token).ConfigureAwait(false);
            if (skills.SelectSingleNode("/chummer/skills/skill[category='Combat Active' and name=" + (design.Skill ?? "").CleanXPath() + "]") == null) return RuleResult.NotAllowed;
            var powers = await character.LoadDataXPathAsync("critterpowers.xml", token: token).ConfigureAwait(false);
            var power = powers.SelectSingleNode("/chummer/powers/power[name='Natural Weapon']");
            if (power == null) return RuleResult.NotAllowed;
            var weapon = new Weapon(character); bool attached = false;
            try
            {
                weapon.Name = design.Name.Trim();
                weapon.Category = await LanguageManager.GetStringAsync("Tab_Critter", GlobalSettings.DefaultLanguage, token: token).ConfigureAwait(false);
                weapon.RangeType = "Melee"; weapon.Reach = design.Reach.ToString(CultureInfo.InvariantCulture); weapon.Accuracy = "Physical";
                weapon.Damage = design.DamageBase + (design.DamageModifier == 0 ? "" : design.DamageModifier.ToString("+0;-0", CultureInfo.InvariantCulture)) + design.DamageType;
                weapon.AP = design.ArmorPenetration.ToString("+0;-0;0", CultureInfo.InvariantCulture);
                weapon.Mode = "0"; weapon.RC = "0"; weapon.Concealability = "0"; weapon.Avail = "0"; weapon.Cost = "0"; weapon.Ammo = "0";
                weapon.UseSkill = design.Skill; weapon.Source = power.SelectSingleNode("source")?.Value ?? "SR5"; weapon.Page = power.SelectSingleNode("page")?.Value ?? "0";
                await weapon.CreateClipsAsync(token).ConfigureAwait(false);
                await character.Weapons.AddAsync(weapon, token).ConfigureAwait(false); attached = true;
                return RuleResult.Changed;
            }
            finally { if (!attached) await weapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
