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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Attributes;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    /// <summary>
    /// Spells, adept powers, complex forms, AI programs, initiation/submersion, metamagics/echoes, martial art techniques and
    /// spirits/sprites, lifted from the legacy character forms (CharacterCreate / CharacterCareer cmdAddSpell_Click,
    /// cmdAddComplexForm_Click, cmdAddAIProgram_Click, cmdAddMetamagic_Click, tsMetamagicAddMetamagic_Click,
    /// tsMartialArtsAddTechnique_Click, PowersTabUserControl, CharacterShared.AddSpirit/AddSprite/DeleteSpirit).
    /// The pickers run in the UI; these methods take the chosen id and apply legacy's costs, checks and expense entries.
    /// </summary>
    public static class MagicRules
    {
        // ---------- spells ----------

        /// <summary>Career karma for a new spell of this kind (legacy uses the "Preparations" / "Rituals" / "Spells" costs).</summary>
        public static Task<int> GetSpellCostAsync(Character objCharacter, bool blnAlchemical, bool blnRitual, CancellationToken token = default)
            => objCharacter.SpellKarmaCostAsync(blnAlchemical ? "Preparations" : blnRitual ? "Rituals" : "Spells", token);

        /// <summary>
        /// Career check before the spell picker opens: the runner needs the karma for a spell unless one can be free.
        /// Returns whether the picker may open and whether only free spells can be taken (SelectSpell.FreeOnly).
        /// </summary>
        public static async Task<(bool CanLearn, bool FreeOnly)> CheckCanLearnSpellAsync(Character objCharacter, CancellationToken token = default)
        {
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return (true, false);
            (bool blnCanTouchOnlySpellBeFree, bool blnCanGenericSpellBeFree) = await objCharacter.AllowFreeSpellsAsync(token).ConfigureAwait(false);
            int intSpellKarmaCost = await objCharacter.SpellKarmaCostAsync("Spells", token).ConfigureAwait(false);
            int intKarma = await objCharacter.GetKarmaAsync(token).ConfigureAwait(false);
            if (intKarma < intSpellKarmaCost && !(blnCanTouchOnlySpellBeFree || blnCanGenericSpellBeFree))
            {
                await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                return (false, false);
            }
            return (true, intKarma < intSpellKarmaCost);
        }

        /// <summary>Learns a spell (cmdAddSpell_Click). Career mode charges karma unless it is taken as a free spell.</summary>
        public static async Task<RuleResult> LearnSpellAsync(Character objCharacter, string strSpellId, bool blnLimited = false, bool blnExtended = false,
                                                             bool blnAlchemical = false, bool blnFreeBonus = false, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            XmlNode objXmlSpell = (await objCharacter.LoadDataAsync("spells.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/spells/spell", strSpellId);
            if (objXmlSpell == null)
                return RuleResult.NotAllowed;
            bool blnCreated = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            Spell objSpell = new Spell(objCharacter);
            try
            {
                await objSpell.CreateAsync(objXmlSpell, string.Empty, blnLimited, blnExtended, blnAlchemical, token: token).ConfigureAwait(false);
                if (objSpell.InternalId.IsEmptyGuid())
                {
                    await objSpell.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                }
                if (blnFreeBonus)
                {
                    objSpell.FreeBonus = true;
                    // Barehanded Adept
                    if (await objCharacter.GetAdeptEnabledAsync(token).ConfigureAwait(false)
                        && !await objCharacter.GetMagicianEnabledAsync(token).ConfigureAwait(false)
                        && (objSpell.Range == "T" || objSpell.Range == "T (A)"))
                        objSpell.BarehandedAdept = true;
                }
                else if (blnCreated)
                {
                    int intSpellKarmaCost = await GetSpellCostAsync(objCharacter, objSpell.Alchemical, objSpell.Category == "Rituals", token).ConfigureAwait(false);
                    if (await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) < intSpellKarmaCost)
                    {
                        await objSpell.DisposeAsync().ConfigureAwait(false);
                        await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                        return RuleResult.NotEnoughKarma;
                    }
                    if (!await CommonFunctions.ConfirmKarmaExpenseAsync(
                            StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                                await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                                await objSpell.GetCurrentDisplayNameAsync(token).ConfigureAwait(false), intSpellKarmaCost), token).ConfigureAwait(false))
                    {
                        await objSpell.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                        return RuleResult.Cancelled;
                    }
                    await AddKarmaExpenseAsync(objCharacter, intSpellKarmaCost, "String_ExpenseLearnSpell",
                                               await objSpell.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                               KarmaExpenseType.AddSpell, objSpell.InternalId, token).ConfigureAwait(false);
                }
                await objCharacter.Spells.AddAsync(objSpell, token).ConfigureAwait(false);
                return RuleResult.Changed;
            }
            catch
            {
                await objSpell.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        // ---------- complex forms ----------

        /// <summary>The complex form limit (twice RES plus improvements) and, in career mode, the karma for one more form.</summary>
        public static async Task<bool> CheckCanAddComplexFormAsync(Character objCharacter, CancellationToken token = default)
        {
            bool blnCreated = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            bool blnIgnore = blnCreated
                ? await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetIgnoreComplexFormLimitAsync(token).ConfigureAwait(false)
                : await objCharacter.GetIgnoreRulesAsync(token).ConfigureAwait(false);
            if (!blnIgnore && await objCharacter.ComplexForms.GetCountAsync(token).ConfigureAwait(false)
                >= await (await objCharacter.GetAttributeAsync("RES", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false) * 2
                + await ImprovementManager.ValueOfAsync(objCharacter, Improvement.ImprovementType.ComplexFormLimit, token: token).ConfigureAwait(false))
            {
                await QualityRules.ShowInfoAsync(await LanguageManager.GetStringAsync("Message_ComplexFormLimit", token: token).ConfigureAwait(false),
                                                 "MessageTitle_ComplexFormLimit", token).ConfigureAwait(false);
                return false;
            }
            if (blnCreated && await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) < await objCharacter.GetComplexFormKarmaCostAsync(token).ConfigureAwait(false))
            {
                await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                return false;
            }
            return true;
        }

        /// <summary>Learns a complex form (cmdAddComplexForm_Click); career mode charges the complex form karma cost.</summary>
        public static async Task<RuleResult> AddComplexFormAsync(Character objCharacter, string strId, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            XmlNode objXmlComplexForm = (await objCharacter.LoadDataAsync("complexforms.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/complexforms/complexform", strId);
            if (objXmlComplexForm == null)
                return RuleResult.NotAllowed;
            int intCost = await objCharacter.GetComplexFormKarmaCostAsync(token).ConfigureAwait(false);
            ComplexForm objComplexForm = new ComplexForm(objCharacter);
            try
            {
                await objComplexForm.CreateAsync(objXmlComplexForm, token: token).ConfigureAwait(false);
                if (objComplexForm.InternalId.IsEmptyGuid())
                {
                    await objComplexForm.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                }
                await objCharacter.ComplexForms.AddAsync(objComplexForm, token).ConfigureAwait(false);
            }
            catch
            {
                await objComplexForm.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.Changed;
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(
                    StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                        await objComplexForm.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), intCost), token).ConfigureAwait(false))
            {
                await objComplexForm.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                return RuleResult.Cancelled;
            }
            await AddKarmaExpenseAsync(objCharacter, intCost, "String_ExpenseLearnComplexForm",
                                       await objComplexForm.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                       KarmaExpenseType.AddComplexForm, objComplexForm.InternalId, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- AI programs ----------

        /// <summary>Career check before the program picker: enough karma for the cheaper program kind. Returns whether advanced programs are affordable.</summary>
        public static async Task<(bool CanAdd, bool AdvancedAllowed)> CheckCanAddAIProgramAsync(Character objCharacter, CancellationToken token = default)
        {
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return (true, true);
            int intCost = await objCharacter.GetAIProgramKarmaCostAsync(token).ConfigureAwait(false);
            int intAdvancedCost = await objCharacter.GetAIAdvancedProgramKarmaCostAsync(token).ConfigureAwait(false);
            int intKarma = await objCharacter.GetKarmaAsync(token).ConfigureAwait(false);
            if (intKarma < Math.Min(intCost, intAdvancedCost))
            {
                await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                return (false, false);
            }
            return (true, intKarma >= intAdvancedCost);
        }

        /// <summary>Adds an AI program (cmdAddAIProgram_Click), asking for the text a program needs and charging karma in career mode.</summary>
        public static async Task<RuleResult> AddAIProgramAsync(Character objCharacter, string strId, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            XmlNode objXmlProgram = (await objCharacter.LoadDataAsync("programs.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/programs/program", strId);
            if (objXmlProgram == null)
                return RuleResult.NotAllowed;
            string strExtra = string.Empty;
            if (objXmlProgram.SelectSingleNodeAndCacheExpressionAsNavigator("bonus/selecttext", token) != null)
            {
                string strDescription = StringExtensions.FastFormat(
                    await LanguageManager.GetStringAsync("String_Improvement_SelectText", token: token).ConfigureAwait(false),
                    objXmlProgram["translate"]?.InnerText ?? objXmlProgram["name"]?.InnerText
                    ?? await LanguageManager.GetStringAsync("String_Unknown", token: token).ConfigureAwait(false));
                using (ThreadSafeDialog<SelectTextRequest> frmPickText = await ThreadSafeDialog<SelectTextRequest>.GetAsync(
                           () => new SelectTextRequest { Description = strDescription }, token).ConfigureAwait(false))
                {
                    if (await frmPickText.ShowDialogSafeAsync(objCharacter, token).ConfigureAwait(false) == DialogOutcome.Cancel)
                        return RuleResult.Cancelled;
                    strExtra = frmPickText.MyForm.SelectedValue;
                }
            }
            bool blnCreated = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            AIProgram objProgram = new AIProgram(objCharacter);
            await objProgram.CreateAsync(objXmlProgram, strExtra, token: token).ConfigureAwait(false);
            if (objProgram.InternalId.IsEmptyGuid())
                return RuleResult.NotAllowed;
            if (!blnCreated)
            {
                await objCharacter.AIPrograms.AddAsync(objProgram, token).ConfigureAwait(false);
                return RuleResult.Changed;
            }
            bool blnAdvanced = objProgram.IsAdvancedProgram;
            int intCost = blnAdvanced
                ? await objCharacter.GetAIAdvancedProgramKarmaCostAsync(token).ConfigureAwait(false)
                : await objCharacter.GetAIProgramKarmaCostAsync(token).ConfigureAwait(false);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(
                    StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                        await objProgram.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), intCost), token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await objCharacter.AIPrograms.AddAsync(objProgram, token).ConfigureAwait(false);
            await AddKarmaExpenseAsync(objCharacter, intCost, "String_ExpenseLearnProgram",
                                       await objProgram.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                       blnAdvanced ? KarmaExpenseType.AddAIAdvancedProgram : KarmaExpenseType.AddAIProgram, objProgram.InternalId, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- adept powers ----------

        /// <summary>Adds an adept power (PowersTabUserControl.cmdAddPower_Click); its cost is in power points, not karma.</summary>
        public static async Task<RuleResult> AddPowerAsync(Character objCharacter, string strId, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            XmlNode objXmlPower = (await objCharacter.LoadDataAsync("powers.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/powers/power", strId);
            if (objXmlPower == null)
                return RuleResult.NotAllowed;
            Power objPower = new Power(objCharacter);
            try
            {
                if (await objPower.CreateAsync(objXmlPower, token: token).ConfigureAwait(false))
                {
                    await objCharacter.Powers.AddAsync(objPower, token).ConfigureAwait(false);
                    return RuleResult.Changed;
                }
                await objPower.DeletePowerAsync(token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            catch
            {
                await objPower.DeletePowerAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Highest paid rating the power's spinner allows (total maximum minus free levels).</summary>
        public static async Task<int> GetPowerMaximumRatingAsync(Power objPower, CancellationToken token = default)
            => Math.Max(await objPower.GetTotalMaximumLevelsAsync(token).ConfigureAwait(false) - await objPower.GetFreeLevelsAsync(token).ConfigureAwait(false), 0);

        /// <summary>Changes the paid rating of a levelled power (the rating spinner of the powers table).</summary>
        public static async Task<RuleResult> SetPowerRatingAsync(Power objPower, int intRating, CancellationToken token = default)
        {
            if (objPower == null)
                throw new ArgumentNullException(nameof(objPower));
            if (!await objPower.GetLevelsEnabledAsync(token).ConfigureAwait(false) || intRating < 0
                || intRating > await GetPowerMaximumRatingAsync(objPower, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            if (await objPower.GetRatingAsync(token).ConfigureAwait(false) == intRating)
                return RuleResult.NotAllowed;
            await objPower.SetRatingAsync(intRating, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Whether the power table's delete button is enabled (powers with free levels come from something else).</summary>
        public static async Task<bool> CanRemovePowerAsync(Power objPower, CancellationToken token = default)
            => await objPower.GetFreeLevelsAsync(token).ConfigureAwait(false) == 0;

        /// <summary>Deletes a power like the powers table's delete button (no confirmation in legacy either).</summary>
        public static async Task<RuleResult> RemovePowerAsync(Power objPower, CancellationToken token = default)
        {
            if (objPower == null)
                throw new ArgumentNullException(nameof(objPower));
            if (!await CanRemovePowerAsync(objPower, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            await objPower.DeletePowerAsync(token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- initiation / submersion ----------

        /// <summary>Karma for the next initiation or submersion grade with the chosen reductions (cmdAddMetamagic_Click).</summary>
        public static async Task<int> GetInitiationCostAsync(Character objCharacter, bool blnGroup, bool blnOrdeal, bool blnSchooling, CancellationToken token = default)
        {
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            bool blnMagic = await objCharacter.GetMAGEnabledAsync(token).ConfigureAwait(false);
            int intGrade = blnMagic
                ? await objCharacter.GetInitiateGradeAsync(token).ConfigureAwait(false)
                : await objCharacter.GetSubmersionGradeAsync(token).ConfigureAwait(false);
            decimal decMultiplier = 1.0m;
            if (blnGroup)
                decMultiplier -= blnMagic
                    ? await objSettings.GetKarmaMAGInitiationGroupPercentAsync(token).ConfigureAwait(false)
                    : await objSettings.GetKarmaRESInitiationGroupPercentAsync(token).ConfigureAwait(false);
            if (blnOrdeal)
                decMultiplier -= blnMagic
                    ? await objSettings.GetKarmaMAGInitiationOrdealPercentAsync(token).ConfigureAwait(false)
                    : await objSettings.GetKarmaRESInitiationOrdealPercentAsync(token).ConfigureAwait(false);
            if (blnSchooling)
                decMultiplier -= blnMagic
                    ? await objSettings.GetKarmaMAGInitiationSchoolingPercentAsync(token).ConfigureAwait(false)
                    : await objSettings.GetKarmaRESInitiationSchoolingPercentAsync(token).ConfigureAwait(false);
            return ((await objSettings.GetKarmaInitiationFlatAsync(token).ConfigureAwait(false)
                     + (intGrade + 1) * await objSettings.GetKarmaInitiationAsync(token).ConfigureAwait(false)) * decMultiplier).StandardRound();
        }

        /// <summary>
        /// Initiates (magic) or submerges (resonance) one grade (cmdAddMetamagic_Click). The grade cannot exceed MAG (and MAGAdept
        /// for mystic adepts with the second-MAG option) or RES. Career mode charges karma, plus 10,000¥ for schooling (magic only).
        /// </summary>
        public static async Task<RuleResult> InitiateAsync(Character objCharacter, bool blnGroup, bool blnOrdeal, bool blnSchooling, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            bool blnCreated = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            bool blnMagic = await objCharacter.GetMAGEnabledAsync(token).ConfigureAwait(false);
            if (!blnMagic && !await objCharacter.GetRESEnabledAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intGrade;
            if (blnMagic)
            {
                intGrade = await objCharacter.GetInitiateGradeAsync(token).ConfigureAwait(false);
                if (intGrade + 1 > await (await objCharacter.GetAttributeAsync("MAG", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false)
                    || await objSettings.GetMysAdeptSecondMAGAttributeAsync(token).ConfigureAwait(false)
                    && await objCharacter.GetIsMysticAdeptAsync(token).ConfigureAwait(false)
                    && intGrade + 1 > await (await objCharacter.GetAttributeAsync("MAGAdept", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false))
                {
                    await QualityRules.ShowInfoAsync(await LanguageManager.GetStringAsync("Message_CannotIncreaseInitiateGrade", token: token).ConfigureAwait(false),
                                                     "MessageTitle_CannotIncreaseInitiateGrade", token).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                }
            }
            else
            {
                intGrade = await objCharacter.GetSubmersionGradeAsync(token).ConfigureAwait(false);
                if (intGrade + 1 > await (await objCharacter.GetAttributeAsync("RES", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false))
                {
                    await QualityRules.ShowInfoAsync(await LanguageManager.GetStringAsync("Message_CannotIncreaseSubmersionGrade", token: token).ConfigureAwait(false),
                                                     "MessageTitle_CannotIncreaseSubmersionGrade", token).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                }
            }

            ExpenseLogEntry objExpense = null;
            bool blnPayNuyen = false;
            if (blnCreated)
            {
                int intKarmaExpense = await GetInitiationCostAsync(objCharacter, blnGroup, blnOrdeal, blnSchooling, token).ConfigureAwait(false);
                if (intKarmaExpense > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false))
                {
                    await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                    return RuleResult.NotEnoughKarma;
                }
                string strGradeName = await LanguageManager.GetStringAsync(blnMagic ? "String_InitiateGrade" : "String_SubmersionGrade", token: token).ConfigureAwait(false);
                if (blnMagic && blnSchooling)
                {
                    if (await objCharacter.GetNuyenAsync(token).ConfigureAwait(false) < 10000)
                    {
                        await QualityRules.ShowInfoAsync(await LanguageManager.GetStringAsync("Message_NotEnoughNuyen", token: token).ConfigureAwait(false),
                                                         "MessageTitle_NotEnoughNuyen", token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;
                    }
                    if (!await CommonFunctions.ConfirmKarmaExpenseAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_ConfirmKarmaandNuyenExpense", token: token).ConfigureAwait(false),
                            strGradeName, intGrade + 1, intKarmaExpense,
                            10000.ToString(await objSettings.GetNuyenFormatAsync(token).ConfigureAwait(false), GlobalSettings.CultureInfo)
                            + await LanguageManager.GetStringAsync("String_NuyenSymbol", token: token).ConfigureAwait(false)), token).ConfigureAwait(false))
                        return RuleResult.Cancelled;
                    blnPayNuyen = true;
                }
                else if (!await CommonFunctions.ConfirmKarmaExpenseAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                             await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpense", token: token).ConfigureAwait(false),
                             strGradeName, intGrade + 1, intKarmaExpense), token).ConfigureAwait(false))
                    return RuleResult.Cancelled;

                string strSpace = await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
                objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(intKarmaExpense * -1,
                                  await LanguageManager.GetStringAsync(blnMagic ? "String_ExpenseInitiateGrade" : "String_ExpenseSubmersionGrade", token: token).ConfigureAwait(false)
                                  + strSpace + intGrade.ToString(GlobalSettings.CultureInfo) + strSpace + "->" + strSpace
                                  + (intGrade + 1).ToString(GlobalSettings.CultureInfo), ExpenseType.Karma, DateTime.Now);
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                await objCharacter.ModifyKarmaAsync(-intKarmaExpense, token).ConfigureAwait(false);
            }

            InitiationGrade objGrade = new InitiationGrade(objCharacter);
            await objGrade.CreateAsync(intGrade + 1, !blnMagic, blnGroup, blnOrdeal, blnSchooling, token).ConfigureAwait(false);
            await objCharacter.InitiationGrades.AddWithSortAsync(objGrade, token: token).ConfigureAwait(false);
            if (objExpense != null)
            {
                ExpenseUndo objUndo = new ExpenseUndo();
                objUndo.CreateKarma(KarmaExpenseType.ImproveInitiateGrade, objGrade.InternalId);
                objExpense.Undo = objUndo;
            }
            if (blnPayNuyen)
            {
                string strSpace = await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
                ExpenseLogEntry objNuyenExpense = new ExpenseLogEntry(objCharacter);
                objNuyenExpense.Create(-10000, await LanguageManager.GetStringAsync("String_ExpenseInitiateGrade", token: token).ConfigureAwait(false)
                                               + strSpace + intGrade.ToString(GlobalSettings.CultureInfo) + strSpace + "->" + strSpace
                                               + (intGrade + 1).ToString(GlobalSettings.CultureInfo), ExpenseType.Nuyen, DateTime.Now);
                await objCharacter.ExpenseEntries.AddWithSortAsync(objNuyenExpense, token: token).ConfigureAwait(false);
                await objCharacter.ModifyNuyenAsync(-10000, token).ConfigureAwait(false);
                ExpenseUndo objNuyenUndo = new ExpenseUndo();
                objNuyenUndo.CreateNuyen(NuyenExpenseType.ImproveInitiateGrade, objGrade.InternalId, 10000);
                objNuyenExpense.Undo = objNuyenUndo;
            }
            return RuleResult.Changed;
        }

        // ---------- metamagics / echoes ----------

        /// <summary>
        /// Career check before the metamagic picker (tsMetamagicAddMetamagic_Click): each grade includes one metamagic/echo (or
        /// ritual/enchantment); more cost karma, which is confirmed first. Returns whether the picker may open and whether to pay.
        /// </summary>
        public static async Task<(bool CanAdd, bool PayWithKarma)> CheckCanAddMetamagicAsync(Character objCharacter, InitiationGrade objGrade, CancellationToken token = default)
        {
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return (true, false);
            int intGrade = objGrade.Grade;
            bool blnPayWithKarma = await objCharacter.Metamagics.AnyAsync(x => x.Grade == intGrade, token).ConfigureAwait(false)
                                   || await objCharacter.Spells.AnyAsync(x => x.Grade == intGrade, token).ConfigureAwait(false);
            if (!blnPayWithKarma)
                return (true, false);
            int intCost = await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetKarmaMetamagicAsync(token).ConfigureAwait(false);
            if (await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) < intCost)
            {
                await QualityRules.ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                return (false, true);
            }
            bool blnMagic = await objCharacter.GetMAGEnabledAsync(token).ConfigureAwait(false);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                    await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync(blnMagic ? "String_Metamagic" : "String_Echo", token: token).ConfigureAwait(false), intCost), token).ConfigureAwait(false))
                return (false, true);
            return (true, true);
        }

        /// <summary>Adds the chosen metamagic/echo to a grade, charging the extra-metamagic karma when <paramref name="blnPayWithKarma"/>.</summary>
        public static async Task<RuleResult> AddMetamagicAsync(Character objCharacter, InitiationGrade objGrade, string strId, bool blnPayWithKarma, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objGrade == null)
                throw new ArgumentNullException(nameof(objGrade));
            bool blnEcho = objGrade.Technomancer;
            XmlNode objXmlMetamagic = (await objCharacter.LoadDataAsync(blnEcho ? "echoes.xml" : "metamagic.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId(blnEcho ? "/chummer/echoes/echo" : "/chummer/metamagics/metamagic", strId);
            if (objXmlMetamagic == null)
                return RuleResult.NotAllowed;
            Metamagic objNewMetamagic = new Metamagic(objCharacter);
            await objNewMetamagic.CreateAsync(objXmlMetamagic, blnEcho ? Improvement.ImprovementSource.Echo : Improvement.ImprovementSource.Metamagic, token: token).ConfigureAwait(false);
            objNewMetamagic.Grade = objGrade.Grade;
            if (objNewMetamagic.InternalId.IsEmptyGuid())
                return RuleResult.NotAllowed;
            await objCharacter.Metamagics.AddAsync(objNewMetamagic, token).ConfigureAwait(false);
            if (blnPayWithKarma && await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
            {
                int intCost = await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetKarmaMetamagicAsync(token).ConfigureAwait(false);
                string strType = await LanguageManager.GetStringAsync(objNewMetamagic.SourceType == Improvement.ImprovementSource.Echo ? "String_Echo" : "String_Metamagic", token: token).ConfigureAwait(false);
                ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(intCost * -1, strType + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                                                        + await objNewMetamagic.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), ExpenseType.Karma, DateTime.Now);
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                ExpenseUndo objUndo = new ExpenseUndo();
                objUndo.CreateKarma(KarmaExpenseType.AddMetamagic, objNewMetamagic.InternalId);
                objExpense.Undo = objUndo;
                await objCharacter.ModifyKarmaAsync(-intCost, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        // ---------- martial arts ----------

        /// <summary>Career karma for the next technique of a martial art: the first one comes with the art.</summary>
        public static async Task<int> GetTechniqueCostAsync(Character objCharacter, MartialArt objMartialArt, CancellationToken token = default)
        {
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return 0;
            return await objMartialArt.Techniques.GetCountAsync(token).ConfigureAwait(false) > 0
                ? await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetKarmaTechniqueAsync(token).ConfigureAwait(false)
                : 0;
        }

        /// <summary>Learns a technique of a martial art (tsMartialArtsAddTechnique_Click; career mode logs the karma like legacy, without asking).</summary>
        public static async Task<RuleResult> AddTechniqueAsync(Character objCharacter, MartialArt objMartialArt, string strId, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objMartialArt == null)
                throw new ArgumentNullException(nameof(objMartialArt));
            XmlNode xmlTechnique = (await objCharacter.LoadDataAsync("martialarts.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/techniques/technique", strId);
            if (xmlTechnique == null)
                return RuleResult.NotAllowed;
            MartialArtTechnique objTechnique = new MartialArtTechnique(objCharacter);
            await objTechnique.CreateAsync(xmlTechnique, token).ConfigureAwait(false);
            if (objTechnique.InternalId.IsEmptyGuid())
                return RuleResult.NotAllowed;
            int intKarmaCost = await GetTechniqueCostAsync(objCharacter, objMartialArt, token).ConfigureAwait(false);
            await objMartialArt.Techniques.AddAsync(objTechnique, token).ConfigureAwait(false);
            if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                await AddKarmaExpenseAsync(objCharacter, intKarmaCost, "String_ExpenseLearnTechnique",
                                           await objTechnique.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                                           KarmaExpenseType.AddMartialArtTechnique, objTechnique.InternalId, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- spirits and sprites ----------

        /// <summary>
        /// Adds a spirit (CharacterShared.AddSpirit) or sprite (AddSprite) of the given type at the runner's maximum force/level.
        /// At the bound spirit limit a spirit starts unbound; sprites cannot exceed the registered sprite limit.
        /// </summary>
        public static async Task<RuleResult> AddSpiritAsync(Character objCharacter, SpiritType eType, string strSpiritType, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            bool blnIgnoreRules = await objCharacter.GetIgnoreRulesAsync(token).ConfigureAwait(false);
            bool blnAtBoundLimit = false;
            if (eType == SpiritType.Spirit)
            {
                if (!blnIgnoreRules)
                {
                    int intBoundCount = await objCharacter.Spirits.CountAsync(
                        async (x, t) => await x.GetEntityTypeAsync(t).ConfigureAwait(false) == SpiritType.Spirit
                                        && await x.GetBoundAsync(t).ConfigureAwait(false) && !await x.GetFetteredAsync(t).ConfigureAwait(false)
                                        && await x.GetCountsAgainstBoundSpiritLimitAsync(t).ConfigureAwait(false), token).ConfigureAwait(false);
                    blnAtBoundLimit = intBoundCount >= await objCharacter.GetBoundSpiritLimitAsync(token).ConfigureAwait(false);
                }
            }
            else if (!blnIgnoreRules && await objCharacter.Spirits.CountAsync(
                         async (x, t) => await x.GetEntityTypeAsync(t).ConfigureAwait(false) == SpiritType.Sprite
                                         && await x.GetBoundAsync(t).ConfigureAwait(false) && !await x.GetFetteredAsync(t).ConfigureAwait(false), token).ConfigureAwait(false)
                     >= await objCharacter.GetRegisteredSpriteLimitAsync(token).ConfigureAwait(false))
            {
                CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
                string strExpression = await objCharacter.ProcessAttributesInXPathForTooltipAsync(
                    await objSettings.GetRegisteredSpriteExpressionAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                await QualityRules.ShowInfoAsync(StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                                                     await LanguageManager.GetStringAsync("Message_RegisteredSpriteLimit", token: token).ConfigureAwait(false),
                                                     strExpression, await objCharacter.GetRegisteredSpriteLimitAsync(token).ConfigureAwait(false)),
                                                 "MessageTitle_RegisteredSpriteLimit", token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }

            Spirit objSpirit = new Spirit(objCharacter);
            try
            {
                await objSpirit.SetEntityTypeAsync(eType, token).ConfigureAwait(false);
                await objSpirit.SetForceAsync(eType == SpiritType.Spirit
                                                  ? await objCharacter.GetMaxSpiritForceAsync(token).ConfigureAwait(false)
                                                  : await objCharacter.GetMaxSpriteLevelAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                if (blnAtBoundLimit)
                    await objSpirit.SetBoundAsync(false, token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strSpiritType))
                    await objSpirit.SetNameAsync(strSpiritType, token).ConfigureAwait(false);
                await objCharacter.Spirits.AddAsync(objSpirit, token: token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await objCharacter.Spirits.RemoveAsync(objSpirit, token: token).ConfigureAwait(false);
                }
                catch
                {
                    //swallow this
                }
                await objSpirit.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return RuleResult.Changed;
        }

        /// <summary>Releases a spirit or sprite (CharacterShared.DeleteSpirit), with legacy's confirmation; fettering is undone first.</summary>
        public static async Task<RuleResult> RemoveSpiritAsync(Character objCharacter, Spirit objSpirit, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSpirit == null)
                throw new ArgumentNullException(nameof(objSpirit));
            bool blnIsSpirit = await objSpirit.GetEntityTypeAsync(token).ConfigureAwait(false) == SpiritType.Spirit;
            if (!await CommonFunctions.ConfirmDeleteAsync(
                    await LanguageManager.GetStringAsync(blnIsSpirit ? "Message_DeleteSpirit" : "Message_DeleteSprite", token: token).ConfigureAwait(false), token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await objSpirit.SetFetteredAsync(false, token).ConfigureAwait(false); // Fettered spirits consume MAG.
            await objCharacter.Spirits.RemoveAsync(objSpirit, token: token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Changes services owed / tasks owed (SpiritControl.nudServices: never below zero).</summary>
        public static async Task<RuleResult> ChangeServicesAsync(Spirit objSpirit, int intDelta, CancellationToken token = default)
        {
            int intNew = await objSpirit.GetServicesOwedAsync(token).ConfigureAwait(false) + intDelta;
            if (intNew < 0)
                return RuleResult.NotAllowed;
            await objSpirit.SetServicesOwedAsync(intNew, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Changes force / level (SpiritControl.nudForce: 1 up to the runner's maximum spirit force or sprite level).</summary>
        public static async Task<RuleResult> ChangeForceAsync(Character objCharacter, Spirit objSpirit, int intDelta, CancellationToken token = default)
        {
            int intNew = await objSpirit.GetForceAsync(token).ConfigureAwait(false) + intDelta;
            int intMax = await objSpirit.GetEntityTypeAsync(token).ConfigureAwait(false) == SpiritType.Spirit
                ? await objCharacter.GetMaxSpiritForceAsync(token).ConfigureAwait(false)
                : await objCharacter.GetMaxSpriteLevelAsync(token).ConfigureAwait(false);
            if (intNew < 1 || intNew > intMax)
                return RuleResult.NotAllowed;
            await objSpirit.SetForceAsync(intNew, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- shared ----------

        private static async Task AddKarmaExpenseAsync(Character objCharacter, int intCost, string strReasonKey, string strName, KarmaExpenseType eUndoType,
                                                       string strObjectId, CancellationToken token)
        {
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(intCost * -1,
                              await LanguageManager.GetStringAsync(strReasonKey, token: token).ConfigureAwait(false)
                              + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false) + strName,
                              ExpenseType.Karma, DateTime.Now);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            await objCharacter.ModifyKarmaAsync(-intCost, token).ConfigureAwait(false);
            ExpenseUndo objUndo = new ExpenseUndo();
            objUndo.CreateKarma(eUndoType, strObjectId);
            objExpense.Undo = objUndo;
        }
    }
}
