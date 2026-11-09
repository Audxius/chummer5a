/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Chummer.Backend.Skills;

namespace Chummer.Rules
{
    /// <summary>
    /// Picks one value out of <paramref name="lstOptions"/> (or a free-text one when the host allows it); null or empty cancels.
    /// </summary>
    public delegate Task<string> SkillChoiceHandler(IReadOnlyList<ListItem> lstOptions, CancellationToken token);

    /// <summary>
    /// Everything the legacy skills tab lets you change, lifted from Controls/Skills (SkillControl, KnowledgeSkillControl,
    /// SkillGroupControl, SkillsTabUserControl) and Forms/Selection Forms (SelectSkillSpec, SelectExoticSkill), so every UI
    /// applies the same checks, prompts and expense entries. The karma bookkeeping itself (ExpenseLogEntry + ExpenseUndo)
    /// stays in the engine's Skill.Upgrade / SkillGroup.Upgrade / Skill.AddSpecialization, exactly as legacy calls them.
    /// </summary>
    public static class SkillRules
    {
        // ---------------------------------------------------------------- career mode

        /// <summary>
        /// Career mode: spend karma to raise a skill (active, exotic or knowledge) by one.
        /// SkillControl.btnCareerIncrease_Click / KnowledgeSkillControl.btnCareerIncrease_Click.
        /// </summary>
        public static async Task<RuleResult> ImproveCareerAsync(Character objCharacter, Skill objSkill, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            // Legacy only shows the button when the skill can be upgraded at all (KnowledgeSkill.AllowUpgrade) ...
            if (objSkill is KnowledgeSkill objKnowledge && !await objKnowledge.GetAllowUpgradeAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intCost = await objSkill.GetUpgradeKarmaCostAsync(token).ConfigureAwait(false);
            if (intCost == -1)
                return RuleResult.NotAllowed;
            // ... and only enables it when Skill.CanUpgradeCareer (enough karma and below the maximum).
            if (!await objSkill.GetCanUpgradeCareerAsync(token).ConfigureAwait(false))
                return intCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false)
                    ? await NotEnoughKarmaAsync(token).ConfigureAwait(false)
                    : RuleResult.NotAllowed;
            string strConfirm = StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpense", token: token).ConfigureAwait(false),
                await objSkill.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                await objSkill.GetRatingAsync(token).ConfigureAwait(false) + 1,
                intCost,
                objSkill is KnowledgeSkill objTyped ? await GetDisplayKnowledgeTypeAsync(objCharacter, objTyped, token).ConfigureAwait(false) : string.Empty);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(strConfirm, token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await objSkill.Upgrade(token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Career mode: spend karma to raise a skill group by one. SkillGroupControl.btnCareerIncrease_Click.</summary>
        public static async Task<RuleResult> ImproveGroupCareerAsync(Character objCharacter, SkillGroup objGroup, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objGroup == null)
                throw new ArgumentNullException(nameof(objGroup));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intCost = await objGroup.GetUpgradeKarmaCostAsync(token).ConfigureAwait(false);
            if (intCost < 0 || await objGroup.GetRatingAsync(token).ConfigureAwait(false) >= await objGroup.GetRatingMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            if (!await objGroup.GetCareerCanIncreaseAsync(token).ConfigureAwait(false))
                return intCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false)
                    ? await NotEnoughKarmaAsync(token).ConfigureAwait(false)
                    : RuleResult.NotAllowed;
            string strConfirm = StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpense", token: token).ConfigureAwait(false),
                await objGroup.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                await objGroup.GetRatingAsync(token).ConfigureAwait(false) + 1,
                intCost);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(strConfirm, token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await objGroup.Upgrade(token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>
        /// Karma a new specialization costs, computed exactly like Skill.AddSpecialization charges it (knowledge or active
        /// specialization price, plus SkillCategorySpecializationKarmaCost(-Multiplier) improvements).
        /// </summary>
        public static async Task<int> GetSpecializationKarmaCostAsync(Character objCharacter, Skill objSkill, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            int intPrice = objSkill.IsKnowledgeSkill
                ? await objSettings.GetKarmaKnowledgeSpecializationAsync(token).ConfigureAwait(false)
                : await objSettings.GetKarmaSpecializationAsync(token).ConfigureAwait(false);
            int intTotalBaseRating = await objSkill.GetTotalBaseRatingAsync(token).ConfigureAwait(false);
            decimal decExtraSpecCost = 0;
            decimal decSpecCostMultiplier = 1.0m;
            foreach (Improvement objImprovement in await ImprovementManager.GetCachedImprovementListForValueOfAsync(objCharacter, Improvement.ImprovementType.SkillCategorySpecializationKarmaCost, objSkill.SkillCategory, true, token).ConfigureAwait(false))
            {
                if (objImprovement.Minimum <= intTotalBaseRating)
                    decExtraSpecCost += objImprovement.Value;
            }
            foreach (Improvement objImprovement in await ImprovementManager.GetCachedImprovementListForValueOfAsync(objCharacter, Improvement.ImprovementType.SkillCategorySpecializationKarmaCostMultiplier, objSkill.SkillCategory, true, token).ConfigureAwait(false))
            {
                if (objImprovement.Minimum <= intTotalBaseRating)
                    decSpecCostMultiplier *= objImprovement.Value / 100.0m;
            }
            if (decSpecCostMultiplier != 1.0m)
                intPrice = (intPrice * decSpecCostMultiplier + decExtraSpecCost).StandardRound();
            else
                intPrice += decExtraSpecCost.StandardRound();
            return intPrice;
        }

        /// <summary>Synchronous <see cref="GetSpecializationKarmaCostAsync"/> for snapshot code that runs on a worker thread.</summary>
        public static int GetSpecializationKarmaCost(Character objCharacter, Skill objSkill)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            int intPrice = objSkill.IsKnowledgeSkill ? objCharacter.Settings.KarmaKnowledgeSpecialization : objCharacter.Settings.KarmaSpecialization;
            int intTotalBaseRating = objSkill.TotalBaseRating;
            decimal decExtraSpecCost = 0;
            decimal decSpecCostMultiplier = 1.0m;
            foreach (Improvement objImprovement in ImprovementManager.GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.SkillCategorySpecializationKarmaCost, objSkill.SkillCategory, true))
            {
                if (objImprovement.Minimum <= intTotalBaseRating)
                    decExtraSpecCost += objImprovement.Value;
            }
            foreach (Improvement objImprovement in ImprovementManager.GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.SkillCategorySpecializationKarmaCostMultiplier, objSkill.SkillCategory, true))
            {
                if (objImprovement.Minimum <= intTotalBaseRating)
                    decSpecCostMultiplier *= objImprovement.Value / 100.0m;
            }
            if (decSpecCostMultiplier != 1.0m)
                intPrice = (intPrice * decSpecCostMultiplier + decExtraSpecCost).StandardRound();
            else
                intPrice += decExtraSpecCost.StandardRound();
            return intPrice;
        }

        /// <summary>
        /// The specializations offered for a skill: its listed specs and, for combat skills, the weapons that use it
        /// (SelectSpec.SelectSpec_Load, without the leading "Custom" entry: hosts offer free text themselves).
        /// </summary>
        public static List<ListItem> GetSpecializationOptions(Character objCharacter, Skill objSkill)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            XPathNavigator xmlSkills = objCharacter.LoadDataXPath("skills.xml");
            string strSkillName = objSkill.Name;
            XPathNavigator xmlParentSkill = objSkill.IsKnowledgeSkill
                ? xmlSkills.TryGetNodeByNameOrId("/chummer/knowledgeskills/skill", strSkillName)
                  ?? xmlSkills.SelectSingleNode("/chummer/knowledgeskills/skill[translate = " + strSkillName.CleanXPath() + "]")
                : xmlSkills.TryGetNodeByNameOrId("/chummer/skills/skill", strSkillName, objCharacter.Settings.BookXPath());
            List<ListItem> lstItems = new List<ListItem>();
            XPathNodeIterator xmlSpecList = xmlParentSkill?.Select("specs/spec");
            if (xmlSpecList?.Count > 0)
            {
                foreach (XPathNavigator xmlSpec in xmlSpecList)
                {
                    string strInnerText = xmlSpec.Value;
                    lstItems.Add(new ListItem(strInnerText, xmlSpec.SelectSingleNode("@translate")?.Value ?? strInnerText));
                }
            }
            lstItems.Sort();
            if (objSkill.SkillCategory == "Combat Active")
            {
                // Weapons of the skill's category or that list one of its specs.
                string strFilter = "(category = " + strSkillName.CleanXPath()
                                   + string.Concat(lstItems.Select(x =>
                                   {
                                       string strLoop = x.Value.ToString().CleanXPath();
                                       return " or spec = " + strLoop + " or spec2 = " + strLoop;
                                   }))
                                   + ") and ";
                List<ListItem> lstWeapons = new List<ListItem>();
                foreach (XPathNavigator xmlWeapon in objCharacter.LoadDataXPath("weapons.xml")
                                                                 .Select("/chummer/weapons/weapon[" + strFilter + objCharacter.Settings.BookXPath() + "]"))
                {
                    string strName = xmlWeapon.SelectSingleNode("name")?.Value;
                    if (!string.IsNullOrEmpty(strName))
                        lstWeapons.Add(new ListItem(strName, xmlWeapon.SelectSingleNode("translate")?.Value ?? strName));
                }
                lstWeapons.Sort();
                lstItems.AddRange(lstWeapons);
            }
            // Don't offer what the runner already has.
            HashSet<string> setOwned = new HashSet<string>(objSkill.Specializations.ToList().Select(x => x.Name), StringComparer.Ordinal);
            lstItems.RemoveAll(x => setOwned.Contains(x.Value.ToString()));
            return lstItems;
        }

        /// <summary>
        /// Career mode: buy a new specialization. SkillControl.btnAddSpec_Click / KnowledgeSkillControl.btnAddSpec_Click:
        /// confirm the karma expense first, then pick the specialization (from the list or as free text).
        /// </summary>
        public static async Task<RuleResult> AddSpecializationCareerAsync(Character objCharacter, Skill objSkill, SkillChoiceHandler funcChoose, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (funcChoose == null)
                throw new ArgumentNullException(nameof(funcChoose));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false) || !await objSkill.GetCanHaveSpecsAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            // The legacy button is disabled unless Skill.CanAffordSpecialization.
            if (!await objSkill.GetCanAffordSpecializationAsync(token).ConfigureAwait(false))
                return await NotEnoughKarmaAsync(token).ConfigureAwait(false);
            int intPrice = await GetSpecializationKarmaCostAsync(objCharacter, objSkill, token).ConfigureAwait(false);
            string strConfirm = StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSkillSpecialization", token: token).ConfigureAwait(false), intPrice);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(strConfirm, token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            string strSpec = await funcChoose(GetSpecializationOptions(objCharacter, objSkill), token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(strSpec))
                return RuleResult.Cancelled;
            int intKarmaBefore = await objCharacter.GetKarmaAsync(token).ConfigureAwait(false);
            await objSkill.AddSpecialization(strSpec.Trim(), token).ConfigureAwait(false);
            return await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) != intKarmaBefore ? RuleResult.Changed : RuleResult.NotEnoughKarma;
        }

        /// <summary>
        /// Career mode: add a knowledge skill or language. SkillsTabUserControl.btnKnowledge_Click: pick (or type) the
        /// name, offer to make it a native language while slots are free, and add it at rating 0 (raise it with karma after).
        /// </summary>
        public static async Task<RuleResult> AddKnowledgeSkillCareerAsync(Character objCharacter, SkillChoiceHandler funcChooseName, SkillChoiceHandler funcChooseType = null,
                                                                          CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (funcChooseName == null)
                throw new ArgumentNullException(nameof(funcChooseName));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            SkillsSection objSection = await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false);
            string strSelectedSkill = await funcChooseName(await objSection.GetMyDefaultKnowledgeSkillsAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(strSelectedSkill))
                return RuleResult.Cancelled;
            KnowledgeSkill objSkill = new KnowledgeSkill(objCharacter, false);
            try
            {
                await objSkill.SetDefaultAttributeAsync("LOG", token).ConfigureAwait(false);
                await objSkill.SetWritableNameAsync(strSelectedSkill.Trim(), token).ConfigureAwait(false);
                // Native addition: a name the data files don't know has no type; legacy leaves the type box empty for the
                // user to fill in on the row, we ask right away (the row still lets you change it while legacy does).
                if (funcChooseType != null && string.IsNullOrEmpty(await objSkill.GetTypeAsync(token).ConfigureAwait(false)))
                {
                    string strType = await funcChooseType(await objSection.GetMyKnowledgeTypesAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(strType))
                        await objSkill.SetTypeAsync(strType, token).ConfigureAwait(false);
                }
                if (await objSection.GetHasAvailableNativeLanguageSlotsAsync(token).ConfigureAwait(false)
                    && (await objSkill.GetIsLanguageAsync(token).ConfigureAwait(false)
                        || string.IsNullOrEmpty(await objSkill.GetTypeAsync(token).ConfigureAwait(false))))
                {
                    MessageResult eResult = await MessageBoxes.ShowAsync(
                        StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_NewNativeLanguageSkill", token: token).ConfigureAwait(false),
                            1 + await ImprovementManager.ValueOfAsync(objCharacter, Improvement.ImprovementType.NativeLanguageLimit, token: token).ConfigureAwait(false),
                            await objSkill.GetWritableNameAsync(token).ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync("Tip_Skill_NativeLanguage", token: token).ConfigureAwait(false),
                        MessageButtons.YesNoCancel, MessageIcon.Question, token: token).ConfigureAwait(false);
                    switch (eResult)
                    {
                        case MessageResult.Cancel:
                            await objSkill.RemoveAsync(CancellationToken.None).ConfigureAwait(false);
                            return RuleResult.Cancelled;
                        case MessageResult.Yes:
                            if (!await objSkill.GetIsLanguageAsync(token).ConfigureAwait(false))
                                await objSkill.SetTypeAsync("Language", token).ConfigureAwait(false);
                            await objSkill.SetIsNativeLanguageAsync(true, token).ConfigureAwait(false);
                            break;
                    }
                }
                await objSection.KnowledgeSkills.AddAsync(objSkill, token).ConfigureAwait(false);
            }
            catch
            {
                await objSkill.RemoveAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return RuleResult.Changed;
        }

        /// <summary>Create mode: add a blank knowledge skill (LOG-linked) to be named on its row. SkillsTabUserControl.btnKnowledge_Click.</summary>
        public static async Task<KnowledgeSkill> AddKnowledgeSkillCreateAsync(Character objCharacter, string strName = null, string strType = null, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return null;
            KnowledgeSkill objSkill = new KnowledgeSkill(objCharacter, false);
            try
            {
                await objSkill.SetDefaultAttributeAsync("LOG", token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(strName))
                    await objSkill.SetWritableNameAsync(strName.Trim(), token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strType) && string.IsNullOrEmpty(await objSkill.GetTypeAsync(token).ConfigureAwait(false)))
                    await objSkill.SetTypeAsync(strType, token).ConfigureAwait(false);
                await (await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false)).KnowledgeSkills.AddAsync(objSkill, token).ConfigureAwait(false);
            }
            catch
            {
                await objSkill.RemoveAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return objSkill;
        }

        /// <summary>Delete a knowledge skill (KnowledgeSkillControl.cmdDelete_Click) or an exotic skill (SkillControl.cmdDelete_Click).</summary>
        public static async Task<RuleResult> DeleteAsync(Character objCharacter, Skill objSkill, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (!await objSkill.GetAllowDeleteAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            string strKey = objSkill.IsKnowledgeSkill ? "Message_DeleteKnowledgeSkill" : objSkill.IsExoticSkill ? "Message_DeleteExoticSkill" : "Message_DeleteSkill";
            if (!await CommonFunctions.ConfirmDeleteAsync(await LanguageManager.GetStringAsync(strKey, token: token).ConfigureAwait(false), token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            SkillsSection objSection = await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false);
            if (objSkill is KnowledgeSkill objKnowledge)
                await objSection.KnowledgeSkills.RemoveAsync(objKnowledge, token).ConfigureAwait(false);
            else
                await objSection.Skills.RemoveAsync(objSkill, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Exotic active skills in the data files (SelectExoticSkill.SelectExoticSkill_Load).</summary>
        public static List<ListItem> GetExoticSkillOptions(Character objCharacter)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            List<ListItem> lstSkills = new List<ListItem>();
            foreach (XPathNavigator xmlSkill in objCharacter.LoadDataXPath("skills.xml").Select("/chummer/skills/skill[exotic = " + bool.TrueString.CleanXPath() + "]"))
            {
                string strName = xmlSkill.SelectSingleNode("name")?.Value;
                if (!string.IsNullOrEmpty(strName))
                    lstSkills.Add(new ListItem(strName, xmlSkill.SelectSingleNode("translate")?.Value ?? strName));
            }
            lstSkills.Sort(CompareListItems.CompareNames);
            return lstSkills;
        }

        /// <summary>
        /// Specifics for a new exotic skill: weapons of its category or using it, plus its listed specs, minus the ones the
        /// runner already has (SelectExoticSkill.BuildList).
        /// </summary>
        public static List<ListItem> GetExoticSpecificOptions(Character objCharacter, string strExoticSkill)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            List<ListItem> lstReturn = new List<ListItem>();
            if (string.IsNullOrEmpty(strExoticSkill))
                return lstReturn;
            CharacterSettings objSettings = objCharacter.Settings;
            foreach (XPathNavigator xmlWeapon in objCharacter.LoadDataXPath("weapons.xml")
                                                             .Select("/chummer/weapons/weapon[(category = " + (strExoticSkill + "s").CleanXPath()
                                                                     + " or useskill = " + strExoticSkill.CleanXPath() + ") and "
                                                                     + objSettings.BookXPath(false) + "]"))
            {
                string strName = xmlWeapon.SelectSingleNode("name")?.Value;
                if (!string.IsNullOrEmpty(strName))
                    lstReturn.Add(new ListItem(strName, xmlWeapon.SelectSingleNode("translate")?.Value ?? strName));
            }
            foreach (XPathNavigator xmlSpec in objCharacter.LoadDataXPath("skills.xml")
                                                           .Select("/chummer/skills/skill[name = " + strExoticSkill.CleanXPath() + " and "
                                                                   + objSettings.BookXPath() + "]/specs/spec"))
            {
                string strName = xmlSpec.Value;
                if (!string.IsNullOrEmpty(strName))
                    lstReturn.Add(new ListItem(strName, xmlSpec.SelectSingleNode("@translate")?.Value ?? strName));
            }
            HashSet<string> setExisting = new HashSet<string>(objCharacter.SkillsSection.Skills.ToList().OfType<ExoticSkill>()
                                                                          .Where(x => x.Name == strExoticSkill).Select(x => x.Specific));
            lstReturn.RemoveAll(x => setExisting.Contains(x.Value.ToString()));
            lstReturn.Sort(Comparer<ListItem>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name)));
            return lstReturn;
        }

        /// <summary>
        /// Add an exotic skill (SkillsTabUserControl.btnExotic_Click). In career mode it is bought straight away at rating 1;
        /// the karma check happens after the skill exists so bonuses that target the specific skill apply, and the skill is
        /// removed again when the runner cannot afford it.
        /// </summary>
        public static async Task<RuleResult> AddExoticSkillAsync(Character objCharacter, string strExoticSkill, string strSpecific, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (string.IsNullOrEmpty(strExoticSkill) || string.IsNullOrWhiteSpace(strSpecific))
                return RuleResult.Cancelled;
            strSpecific = await objCharacter.ReverseTranslateExtraAsync(strSpecific.Trim(), token: token).ConfigureAwait(false);
            SkillsSection objSection = await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false);
            ExoticSkill objSkill = await objSection.AddExoticSkillAsync(strExoticSkill, strSpecific, token).ConfigureAwait(false);
            if (objSkill == null)
                return RuleResult.NotAllowed;
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                && await objSkill.GetUpgradeKarmaCostAsync(token).ConfigureAwait(false) > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false))
            {
                await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                await objSection.Skills.RemoveAsync(objSkill, token).ConfigureAwait(false);
                return RuleResult.NotEnoughKarma;
            }
            await objSkill.Upgrade(token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------------------------------------------------------------- create mode

        /// <summary>Synchronous <see cref="CanChangeBaseAsync"/>.</summary>
        public static bool CanChangeBase(Character objCharacter, Skill objSkill)
        {
            if (objCharacter.Created)
                return false;
            if (objSkill is KnowledgeSkill objKnowledge)
                return objCharacter.SkillsSection.HasKnowledgePoints && objKnowledge.AllowUpgrade;
            return objCharacter.EffectiveBuildMethodUsesPriorityTables && objSkill.BaseUnlocked;
        }

        /// <summary>Synchronous <see cref="CanChangeKarmaAsync"/>.</summary>
        public static bool CanChangeKarma(Character objCharacter, Skill objSkill)
        {
            if (objCharacter.Created)
                return false;
            return objSkill is KnowledgeSkill objKnowledge ? objKnowledge.AllowUpgrade : objSkill.KarmaUnlocked;
        }

        /// <summary>Create mode: whether the priority/skill-point box (nudSkill) is shown and editable for this skill.</summary>
        public static async Task<bool> CanChangeBaseAsync(Character objCharacter, Skill objSkill, CancellationToken token = default)
        {
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return false;
            if (objSkill is KnowledgeSkill objKnowledge)
                return await (await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false)).GetHasKnowledgePointsAsync(token).ConfigureAwait(false)
                       && await objKnowledge.GetAllowUpgradeAsync(token).ConfigureAwait(false);
            return await objCharacter.GetEffectiveBuildMethodUsesPriorityTablesAsync(token).ConfigureAwait(false)
                   && await objSkill.GetBaseUnlockedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Create mode: whether the karma box (nudKarma) is editable for this skill.</summary>
        public static async Task<bool> CanChangeKarmaAsync(Character objCharacter, Skill objSkill, CancellationToken token = default)
        {
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return false;
            if (objSkill is KnowledgeSkill objKnowledge)
                return await objKnowledge.GetAllowUpgradeAsync(token).ConfigureAwait(false);
            return await objSkill.GetKarmaUnlockedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Create mode: change the skill points spent (nudSkill: FreeBase ≤ value ≤ RatingMaximum).</summary>
        public static async Task<RuleResult> ChangeBaseAsync(Character objCharacter, Skill objSkill, int intDelta, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (intDelta == 0 || !await CanChangeBaseAsync(objCharacter, objSkill, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intOld = await objSkill.GetBaseAsync(token).ConfigureAwait(false);
            int intNew = intOld + intDelta;
            if (intNew < await objSkill.GetFreeBaseAsync(token).ConfigureAwait(false) || intNew > await objSkill.GetRatingMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetBaseAsync(intNew, token).ConfigureAwait(false);
            return await objSkill.GetBaseAsync(token).ConfigureAwait(false) != intOld ? RuleResult.Changed : RuleResult.NotAllowed;
        }

        /// <summary>Create mode: change the karma-bought levels (nudKarma: FreeKarma ≤ value ≤ RatingMaximum).</summary>
        public static async Task<RuleResult> ChangeKarmaAsync(Character objCharacter, Skill objSkill, int intDelta, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (intDelta == 0 || !await CanChangeKarmaAsync(objCharacter, objSkill, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intOld = await objSkill.GetKarmaAsync(token).ConfigureAwait(false);
            int intNew = intOld + intDelta;
            if (intNew < await objSkill.GetFreeKarmaAsync(token).ConfigureAwait(false) || intNew > await objSkill.GetRatingMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetKarmaAsync(intNew, token).ConfigureAwait(false);
            return await objSkill.GetKarmaAsync(token).ConfigureAwait(false) != intOld ? RuleResult.Changed : RuleResult.NotAllowed;
        }

        /// <summary>Create mode: change a skill group's group points (SkillGroupControl.nudSkill: enabled while BaseUnbroken, 0 ≤ value ≤ RatingMaximum).</summary>
        public static async Task<RuleResult> ChangeGroupBaseAsync(Character objCharacter, SkillGroup objGroup, int intDelta, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objGroup == null)
                throw new ArgumentNullException(nameof(objGroup));
            if (intDelta == 0 || await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                              || !await objCharacter.GetEffectiveBuildMethodUsesPriorityTablesAsync(token).ConfigureAwait(false)
                              || !await objGroup.GetBaseUnbrokenAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intOld = await objGroup.GetBaseAsync(token).ConfigureAwait(false);
            int intNew = intOld + intDelta;
            if (intNew < 0 || intNew > await objGroup.GetRatingMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objGroup.SetBaseAsync(intNew, token).ConfigureAwait(false);
            return await objGroup.GetBaseAsync(token).ConfigureAwait(false) != intOld ? RuleResult.Changed : RuleResult.NotAllowed;
        }

        /// <summary>Create mode: change a skill group's karma levels (SkillGroupControl.nudKarma: enabled while KarmaUnbroken, 0 ≤ value ≤ RatingMaximum).</summary>
        public static async Task<RuleResult> ChangeGroupKarmaAsync(Character objCharacter, SkillGroup objGroup, int intDelta, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objGroup == null)
                throw new ArgumentNullException(nameof(objGroup));
            if (intDelta == 0 || await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                              || !await objGroup.GetKarmaUnbrokenAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intOld = await objGroup.GetKarmaAsync(token).ConfigureAwait(false);
            int intNew = intOld + intDelta;
            if (intNew < 0 || intNew > await objGroup.GetRatingMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objGroup.SetKarmaAsync(intNew, token).ConfigureAwait(false);
            return await objGroup.GetKarmaAsync(token).ConfigureAwait(false) != intOld ? RuleResult.Changed : RuleResult.NotAllowed;
        }

        /// <summary>
        /// Create mode: set (or with an empty value, clear) the runner's chosen specialization — the cboSpec box on the
        /// legacy row (Skill.SetTopMostDisplaySpecializationAsync; free specializations from bonuses are kept).
        /// </summary>
        public static async Task<RuleResult> SetSpecializationCreateAsync(Character objCharacter, Skill objSkill, string strSpec, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false) || !await objSkill.GetCanHaveSpecsAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetTopMostDisplaySpecializationAsync(strSpec?.Trim() ?? string.Empty, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Create mode: pay for the specialization with karma instead of skill points (chkKarma).</summary>
        public static async Task<RuleResult> SetBuyWithKarmaAsync(Character objCharacter, Skill objSkill, bool blnValue, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false) || !await objSkill.GetCanHaveSpecsAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetBuyWithKarmaAsync(blnValue, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------------------------------------------------------------- knowledge skill details

        /// <summary>Rename a knowledge skill while KnowledgeSkill.AllowNameChange (cboName).</summary>
        public static async Task<RuleResult> RenameKnowledgeSkillAsync(KnowledgeSkill objSkill, string strName, CancellationToken token = default)
        {
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (string.IsNullOrWhiteSpace(strName) || !await objSkill.GetAllowNameChangeAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetWritableNameAsync(strName.Trim(), token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Change a knowledge skill's type while KnowledgeSkill.AllowTypeChange (cboType).</summary>
        public static async Task<RuleResult> SetKnowledgeTypeAsync(KnowledgeSkill objSkill, string strType, CancellationToken token = default)
        {
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (string.IsNullOrEmpty(strType) || !await objSkill.GetAllowTypeChangeAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetTypeAsync(strType, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>
        /// Create mode: toggle "native language" (chkNativeLanguage: shown for languages, enabled when it is already native
        /// or a native slot is free).
        /// </summary>
        public static async Task<RuleResult> SetNativeLanguageAsync(Character objCharacter, KnowledgeSkill objSkill, bool blnNative, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSkill == null)
                throw new ArgumentNullException(nameof(objSkill));
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false) || !await objSkill.GetIsLanguageAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            bool blnIsNative = await objSkill.GetIsNativeLanguageAsync(token).ConfigureAwait(false);
            if (blnIsNative == blnNative)
                return RuleResult.NotAllowed;
            if (blnNative && !await (await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false)).GetHasAvailableNativeLanguageSlotsAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objSkill.SetIsNativeLanguageAsync(blnNative, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        private static async Task<string> GetDisplayKnowledgeTypeAsync(Character objCharacter, KnowledgeSkill objSkill, CancellationToken token)
        {
            string strType = await objSkill.GetTypeAsync(token).ConfigureAwait(false);
            foreach (ListItem objItem in await (await objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false)).GetMyKnowledgeTypesAsync(token).ConfigureAwait(false))
            {
                if (objItem.Value?.ToString() == strType)
                    return objItem.Name;
            }
            return strType;
        }

        private static async Task<RuleResult> NotEnoughKarmaAsync(CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_NotEnoughKarma", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
            return RuleResult.NotEnoughKarma;
        }
    }
}
