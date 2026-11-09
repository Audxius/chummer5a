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
using System.Xml;
using Chummer.Backend.Equipment;
using Chummer.Backend.Skills;

namespace Chummer.Rules
{
    /// <summary>
    /// What the player entered in the expense dialog (legacy CreateExpense form). <see cref="Amount"/> is the final amount,
    /// i.e. for nuyen already multiplied by the dialog's percentage field (legacy CreateExpense.cmdOK_Click).
    /// </summary>
    public sealed class ExpenseInput
    {
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public bool Refund { get; set; }
        /// <summary>"Working for the Man/People": also convert karma into nuyen (or back).</summary>
        public bool KarmaNuyenExchange { get; set; }
        /// <summary>"Show in Career Karma/Nuyen" (only meaningful together with <see cref="KarmaNuyenExchange"/>).</summary>
        public bool ForceCareerVisible { get; set; }
    }

    /// <summary>
    /// Career-mode karma and nuyen bookkeeping, lifted from the legacy CharacterCareer form: cmdKarmaGained_Click,
    /// cmdKarmaSpent_Click, cmdNuyenGained_Click, cmdNuyenSpent_Click (manual entries), lstKarma_DoubleClick /
    /// lstNuyen_DoubleClick (editing an entry), tsUndoKarmaExpense_Click / tsUndoNuyenExpense_Click (undo), plus the
    /// karma/nuyen exchange check from CreateExpense.cmdOK_Click.
    /// </summary>
    public static class ExpenseRules
    {
        // ---------- manual entries ----------

        /// <summary>The label of the "Working for the ..." checkbox the legacy form shows for each of the four buttons.</summary>
        public static string ExchangeLabelKey(ExpenseType eType, bool blnGain)
            => eType == ExpenseType.Karma
                ? blnGain ? "String_WorkingForThePeople" : "String_WorkingForTheMan"
                : blnGain ? "String_WorkingForTheMan" : "String_WorkingForThePeople";

        /// <summary>
        /// Legacy CreateExpense.cmdOK_Click: with the exchange box ticked in nuyen mode, the amount must be a multiple of the
        /// nuyen-per-karma rate (Working for the People). Shows the same message as legacy and returns false if it is not.
        /// </summary>
        public static async Task<bool> ValidateNuyenExchangeAsync(Character objCharacter, decimal decAmount, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            decimal decRate = await objSettings.GetNuyenPerBPWftPAsync(token).ConfigureAwait(false);
            if (decRate == 0)
                return true;
            decimal decDividend = decAmount / decRate;
            if (decimal.Floor(decDividend) == decimal.Ceiling(decDividend))
                return true;
            await MessageBoxes.ShowAsync(
                StringExtensions.FastFormat(
                    await LanguageManager.GetStringAsync("Message_KarmaNuyenExchange", token: token).ConfigureAwait(false),
                    decRate.ToString(await objSettings.GetNuyenFormatAsync(token).ConfigureAwait(false), GlobalSettings.CultureInfo)
                    + await LanguageManager.GetStringAsync("String_NuyenSymbol", token: token).ConfigureAwait(false)),
                await LanguageManager.GetStringAsync("MessageTitle_KarmaNuyenExchange", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
            return false;
        }

        /// <summary>Legacy cmdKarmaGained_Click.</summary>
        public static async Task<RuleResult> GainKarmaAsync(Character objCharacter, ExpenseInput objInput, CancellationToken token = default)
        {
            CheckArguments(objCharacter, objInput);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(objInput.Amount, objInput.Reason, ExpenseType.Karma, objInput.Date, objInput.Refund);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            objExpense.Undo = new ExpenseUndo().CreateKarma(KarmaExpenseType.ManualAdd, string.Empty);
            await objCharacter.ModifyKarmaAsync(objInput.Amount.ToInt32(), token).ConfigureAwait(false);

            if (objInput.KarmaNuyenExchange)
            {
                // Legacy logs the amount at the "for the People" rate but moves the nuyen at the "for the Man" rate.
                objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(-objInput.Amount * await objSettings.GetNuyenPerBPWftPAsync(token).ConfigureAwait(false),
                    objInput.Reason, ExpenseType.Nuyen, objInput.Date);
                objExpense.ForceCareerVisible = objInput.ForceCareerVisible;
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                objExpense.Undo = new ExpenseUndo().CreateNuyen(NuyenExpenseType.ManualSubtract, string.Empty);
                await objCharacter.ModifyNuyenAsync(-objInput.Amount * await objSettings.GetNuyenPerBPWftMAsync(token).ConfigureAwait(false), token)
                                  .ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        /// <summary>Legacy cmdKarmaSpent_Click.</summary>
        public static async Task<RuleResult> SpendKarmaAsync(Character objCharacter, ExpenseInput objInput, CancellationToken token = default)
        {
            CheckArguments(objCharacter, objInput);
            // Make sure the Karma expense would not put the character's remaining Karma amount below 0.
            if (await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) < objInput.Amount)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NotEnoughKarma", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotEnoughKarma;
            }
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(objInput.Amount * -1, objInput.Reason, ExpenseType.Karma, objInput.Date, objInput.Refund);
            objExpense.ForceCareerVisible = objInput.ForceCareerVisible;
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            objExpense.Undo = new ExpenseUndo().CreateKarma(KarmaExpenseType.ManualSubtract, string.Empty);
            await objCharacter.ModifyKarmaAsync(-objInput.Amount.ToInt32(), token).ConfigureAwait(false);

            if (objInput.KarmaNuyenExchange)
            {
                decimal decRate = await objSettings.GetNuyenPerBPWftMAsync(token).ConfigureAwait(false);
                objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(objInput.Amount * decRate, objInput.Reason, ExpenseType.Nuyen, objInput.Date);
                objExpense.ForceCareerVisible = objInput.ForceCareerVisible;
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                objExpense.Undo = new ExpenseUndo().CreateNuyen(NuyenExpenseType.ManualSubtract, string.Empty);
                await objCharacter.ModifyNuyenAsync(objInput.Amount * decRate, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        /// <summary>Legacy cmdNuyenGained_Click.</summary>
        public static async Task<RuleResult> GainNuyenAsync(Character objCharacter, ExpenseInput objInput, CancellationToken token = default)
        {
            CheckArguments(objCharacter, objInput);
            if (objInput.KarmaNuyenExchange && !await ValidateNuyenExchangeAsync(objCharacter, objInput.Amount, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(objInput.Amount, objInput.Reason, ExpenseType.Nuyen, objInput.Date);
            await objExpense.SetRefundAsync(objInput.Refund, token).ConfigureAwait(false);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            objExpense.Undo = new ExpenseUndo().CreateNuyen(NuyenExpenseType.ManualAdd, string.Empty);
            await objCharacter.ModifyNuyenAsync(objInput.Amount, token).ConfigureAwait(false);

            if (objInput.KarmaNuyenExchange)
            {
                int intAmount = (objInput.Amount / await objSettings.GetNuyenPerBPWftMAsync(token).ConfigureAwait(false)).ToInt32();
                objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(-intAmount, objInput.Reason, ExpenseType.Karma, objInput.Date, objInput.Refund);
                objExpense.ForceCareerVisible = objInput.ForceCareerVisible;
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                objExpense.Undo = new ExpenseUndo().CreateKarma(KarmaExpenseType.ManualSubtract, string.Empty);
                await objCharacter.ModifyKarmaAsync(-intAmount, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        /// <summary>Legacy cmdNuyenSpent_Click.</summary>
        public static async Task<RuleResult> SpendNuyenAsync(Character objCharacter, ExpenseInput objInput, CancellationToken token = default)
        {
            CheckArguments(objCharacter, objInput);
            if (objInput.KarmaNuyenExchange && !await ValidateNuyenExchangeAsync(objCharacter, objInput.Amount, token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            // Make sure the Nuyen expense would not put the character's remaining Nuyen amount below 0.
            if (await objCharacter.GetNuyenAsync(token).ConfigureAwait(false) < objInput.Amount)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_NotEnoughNuyen", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NotEnoughNuyen", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(objInput.Amount * -1, objInput.Reason, ExpenseType.Nuyen, objInput.Date);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            objExpense.Undo = new ExpenseUndo().CreateNuyen(NuyenExpenseType.ManualSubtract, string.Empty);
            await objCharacter.ModifyNuyenAsync(-objInput.Amount, token).ConfigureAwait(false);

            if (objInput.KarmaNuyenExchange)
            {
                int intAmount = (objInput.Amount / await objSettings.GetNuyenPerBPWftPAsync(token).ConfigureAwait(false)).ToInt32();
                objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(intAmount, objInput.Reason, ExpenseType.Karma, objInput.Date, objInput.Refund);
                objExpense.ForceCareerVisible = objInput.ForceCareerVisible;
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                objExpense.Undo = new ExpenseUndo().CreateKarma(KarmaExpenseType.ManualSubtract, string.Empty);
                await objCharacter.ModifyKarmaAsync(intAmount, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }

        private static void CheckArguments(Character objCharacter, ExpenseInput objInput)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objInput == null)
                throw new ArgumentNullException(nameof(objInput));
        }

        // ---------- editing ----------

        /// <summary>Legacy lstKarma/lstNuyen_DoubleClick: only manual entries let the player change the amount.</summary>
        public static bool CanEditAmount(ExpenseLogEntry objExpense)
        {
            if (objExpense?.Undo == null)
                return false;
            return objExpense.Type == ExpenseType.Karma
                ? objExpense.Undo.KarmaType == KarmaExpenseType.ManualAdd || objExpense.Undo.KarmaType == KarmaExpenseType.ManualSubtract
                : objExpense.Undo.NuyenType == NuyenExpenseType.ManualAdd || objExpense.Undo.NuyenType == NuyenExpenseType.ManualSubtract;
        }

        /// <summary>
        /// Legacy lstKarma_DoubleClick / lstNuyen_DoubleClick after the dialog closed with OK: for manual entries a changed amount
        /// also moves the character's karma/nuyen by the difference; reason and date are always taken over.
        /// </summary>
        public static async Task<RuleResult> EditExpenseAsync(Character objCharacter, ExpenseLogEntry objExpense, decimal decNewAmount,
                                                              string strReason, DateTime datDate, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objExpense == null)
                throw new ArgumentNullException(nameof(objExpense));
            bool blnAllowEdit = CanEditAmount(objExpense);
            if (objExpense.Type == ExpenseType.Karma)
            {
                int intOldAmount = objExpense.Amount.ToInt32();
                int intNewAmount = decNewAmount.ToInt32();
                if (blnAllowEdit && intOldAmount != intNewAmount)
                {
                    await objExpense.SetAmountAsync(intNewAmount, token).ConfigureAwait(false);
                    await objCharacter.ModifyKarmaAsync(intNewAmount - intOldAmount, token).ConfigureAwait(false);
                }
            }
            else
            {
                decimal decOldAmount = objExpense.Amount;
                if (blnAllowEdit && decOldAmount != decNewAmount)
                {
                    await objExpense.SetAmountAsync(decNewAmount, token).ConfigureAwait(false);
                    await objCharacter.ModifyNuyenAsync(decNewAmount - decOldAmount, token).ConfigureAwait(false);
                }
            }
            objExpense.Reason = strReason;
            objExpense.Date = datDate;
            // Legacy MakeDirtyWithCharacterUpdate: reason/date changes raise nothing on their own.
            await objCharacter.OnPropertyChangedAsync(objExpense.Type == ExpenseType.Karma ? nameof(Character.CareerKarma) : nameof(Character.CareerNuyen), token)
                              .ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- undo ----------

        /// <summary>Undo a karma or nuyen expense entry (legacy tsUndoKarmaExpense_Click / tsUndoNuyenExpense_Click).</summary>
        public static Task<RuleResult> UndoExpenseAsync(Character objCharacter, ExpenseLogEntry objExpense, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            return objExpense?.Type == ExpenseType.Nuyen
                ? UndoNuyenExpenseAsync(objCharacter, objExpense, token)
                : UndoKarmaExpenseAsync(objCharacter, objExpense, token);
        }

        private static async Task<bool> ConfirmUndoAsync(Character objCharacter, ExpenseLogEntry objExpense, CancellationToken token)
        {
            if (objExpense?.Undo == null)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_UndoNoHistory", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NoUndoHistory", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return false;
            }
            // Both legacy handlers test the karma type here (also for nuyen entries).
            if (objExpense.Undo.KarmaType == KarmaExpenseType.ImproveInitiateGrade)
            {
                string strUndoId = objExpense.Undo.ObjectId;
                int intMaxGrade = 0;
                await objCharacter.InitiationGrades.ForEachAsync(x => intMaxGrade = Math.Max(intMaxGrade, x.Grade), token).ConfigureAwait(false);
                InitiationGrade objGrade = await objCharacter.InitiationGrades.FirstOrDefaultAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false);
                if (objGrade != null && objGrade.Grade < intMaxGrade)
                {
                    await MessageBoxes.ShowAsync(
                        await LanguageManager.GetStringAsync("Message_UndoNotHighestGrade", token: token).ConfigureAwait(false),
                        await LanguageManager.GetStringAsync("MessageTitle_NotHighestGrade", token: token).ConfigureAwait(false),
                        MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                    return false;
                }
            }
            return await MessageBoxes.ShowAsync(
                       await LanguageManager.GetStringAsync("Message_UndoExpense", token: token).ConfigureAwait(false),
                       await LanguageManager.GetStringAsync("MessageTitle_UndoExpense", token: token).ConfigureAwait(false),
                       MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) != MessageResult.No;
        }

        private static Task ShowBrokenSkillGroupAsync(CancellationToken token)
            => ShowWarningAsync("Message_UndoBrokenSkillGroup", "MessageTitle_UndoBrokenSkillGroup", token);

        private static async Task ShowWarningAsync(string strMessageKey, string strTitleKey, CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync(strMessageKey, token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync(strTitleKey, token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Warning, token: token).ConfigureAwait(false);
        }

        /// <summary>Legacy tsUndoKarmaExpense_Click.</summary>
        public static async Task<RuleResult> UndoKarmaExpenseAsync(Character objCharacter, ExpenseLogEntry objExpense, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (!await ConfirmUndoAsync(objCharacter, objExpense, token).ConfigureAwait(false))
                return RuleResult.Cancelled;

            string strUndoId = objExpense.Undo.ObjectId;
            switch (objExpense.Undo.KarmaType)
            {
                case KarmaExpenseType.ImproveAttribute:
                    {
                        Backend.Attributes.CharacterAttrib objAttribute = await objCharacter.GetAttributeAsync(strUndoId, token: token).ConfigureAwait(false);
                        if (objAttribute != null)
                            await objAttribute.Degrade(token: token).ConfigureAwait(false);
                        break;
                    }
                case KarmaExpenseType.AddPowerPoint:
                    await objCharacter.ModifyMysticAdeptPowerPointsAsync(-1, token).ConfigureAwait(false);
                    break;

                case KarmaExpenseType.AddQuality:
                    for (int i = await objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        Quality objQuality = await objCharacter.Qualities.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objQuality.InternalId == strUndoId)
                            await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.AddSpell:
                    for (int i = await objCharacter.Spells.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.Spells.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        Spell objSpell = await objCharacter.Spells.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objSpell.InternalId == strUndoId)
                            await objSpell.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.SkillSpec:
                case KarmaExpenseType.AddSpecialization:
                    {
                        SkillSpecialization objSpec = objCharacter.SkillsSection.KnowledgeSkills
                                                                  .SelectMany(x => x.Specializations)
                                                                  .FirstOrDefault(x => x.InternalId == strUndoId)
                                                      ?? objCharacter.SkillsSection.Skills
                                                                     .SelectMany(x => x.Specializations)
                                                                     .FirstOrDefault(x => x.InternalId == strUndoId);
                        if (objSpec != null)
                            await objSpec.Parent.Specializations.RemoveAsync(objSpec, token).ConfigureAwait(false);
                        break;
                    }
                case KarmaExpenseType.ImproveSkillGroup:
                    {
                        // Old characters may have had the expense added as the Name instead of guid.
                        SkillGroup objGroup = await objCharacter.SkillsSection.SkillGroups
                                                                .FirstOrDefaultAsync(g => g.InternalId == strUndoId || g.Name == strUndoId, token)
                                                                .ConfigureAwait(false);
                        if (objGroup != null)
                        {
                            if (!await objGroup.GetKarmaUnbrokenAsync(token).ConfigureAwait(false))
                            {
                                await ShowBrokenSkillGroupAsync(token).ConfigureAwait(false);
                                return RuleResult.NotAllowed;
                            }
                            --objGroup.Karma;
                        }
                        break;
                    }
                case KarmaExpenseType.AddSkill:
                    {
                        Skill objSkill = await objCharacter.SkillsSection.Skills.FirstOrDefaultAsync(s => s.InternalId == strUndoId, token).ConfigureAwait(false);
                        if (objSkill != null)
                        {
                            if (await objSkill.GetAllowDeleteAsync(token).ConfigureAwait(false))
                                await objCharacter.SkillsSection.Skills.RemoveAsync(objSkill, token).ConfigureAwait(false);
                            else
                            {
                                await objSkill.SetBasePointsAsync(0, token).ConfigureAwait(false);
                                await objSkill.SetKarmaPointsAsync(0, token).ConfigureAwait(false);
                            }
                            break;
                        }
                        KnowledgeSkill objKnowledgeSkill = await objCharacter.SkillsSection.KnowledgeSkills
                                                                             .FirstOrDefaultAsync(s => s.InternalId == strUndoId, token).ConfigureAwait(false);
                        if (objKnowledgeSkill != null)
                        {
                            if (await objKnowledgeSkill.GetAllowDeleteAsync(token).ConfigureAwait(false))
                                await objCharacter.SkillsSection.KnowledgeSkills.RemoveAsync(objKnowledgeSkill, token).ConfigureAwait(false);
                            else
                            {
                                await objKnowledgeSkill.SetBasePointsAsync(0, token).ConfigureAwait(false);
                                await objKnowledgeSkill.SetKarmaPointsAsync(0, token).ConfigureAwait(false);
                            }
                            break;
                        }
                        // Old characters may have incorrectly had their ExpenseType set to AddSkill rather than ImproveSkillGroup.
                        SkillGroup objGroup = await objCharacter.SkillsSection.SkillGroups
                                                                .FirstOrDefaultAsync(g => g.InternalId == strUndoId, token).ConfigureAwait(false);
                        if (objGroup != null)
                        {
                            if (!await objGroup.GetKarmaUnbrokenAsync(token).ConfigureAwait(false))
                            {
                                await ShowBrokenSkillGroupAsync(token).ConfigureAwait(false);
                                return RuleResult.NotAllowed;
                            }
                            --objGroup.Karma;
                        }
                        break;
                    }
                case KarmaExpenseType.ImproveSkill:
                    {
                        Skill objSkill = await objCharacter.SkillsSection.Skills.FirstOrDefaultAsync(s => s.InternalId == strUndoId, token).ConfigureAwait(false)
                                         ?? await objCharacter.SkillsSection.KnowledgeSkills.FirstOrDefaultAsync(s => s.InternalId == strUndoId, token).ConfigureAwait(false);
                        if (objSkill != null)
                        {
                            IAsyncDisposable objLocker = await objSkill.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
                            try
                            {
                                token.ThrowIfCancellationRequested();
                                await objSkill.SetKarmaAsync(await objSkill.GetKarmaAsync(token).ConfigureAwait(false) - 1, token: token).ConfigureAwait(false);
                            }
                            finally
                            {
                                await objLocker.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        break;
                    }
                case KarmaExpenseType.AddMetamagic:
                    for (int i = await objCharacter.Metamagics.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.Metamagics.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        Metamagic objMetamagic = await objCharacter.Metamagics.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objMetamagic.InternalId == strUndoId)
                            await objMetamagic.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.ImproveInitiateGrade:
                    for (int i = await objCharacter.InitiationGrades.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.InitiationGrades.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        InitiationGrade objGrade = await objCharacter.InitiationGrades.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objGrade.InternalId == strUndoId)
                            await objGrade.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.AddMartialArt:
                    for (int i = await objCharacter.MartialArts.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.MartialArts.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        MartialArt objMartialArt = await objCharacter.MartialArts.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objMartialArt.InternalId == strUndoId)
                            await objMartialArt.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.AddMartialArtTechnique:
                    for (int i = await objCharacter.MartialArts.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.MartialArts.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        MartialArt objMartialArt = await objCharacter.MartialArts.GetValueAtAsync(i, token).ConfigureAwait(false);
                        for (int j = await objMartialArt.Techniques.GetCountAsync(token).ConfigureAwait(false) - 1; j >= 0; --j)
                        {
                            if (j >= await objMartialArt.Techniques.GetCountAsync(token).ConfigureAwait(false))
                                continue;
                            MartialArtTechnique objTechnique = await objMartialArt.Techniques.GetValueAtAsync(j, token).ConfigureAwait(false);
                            if (objTechnique.InternalId == strUndoId)
                                await objTechnique.RemoveAsync(false, token).ConfigureAwait(false);
                        }
                    }
                    break;

                case KarmaExpenseType.AddComplexForm:
                    for (int i = await objCharacter.ComplexForms.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.ComplexForms.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        ComplexForm objComplexForm = await objCharacter.ComplexForms.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objComplexForm.InternalId == strUndoId)
                            await objComplexForm.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                case KarmaExpenseType.BindFocus:
                    await UndoBindFocusAsync(objCharacter, strUndoId, token).ConfigureAwait(false);
                    break;

                case KarmaExpenseType.JoinGroup:
                    // Remove the character from their Group.
                    objCharacter.GroupMember = false;
                    break;

                case KarmaExpenseType.LeaveGroup:
                    // Put the character back in their Group.
                    objCharacter.GroupMember = true;
                    break;

                case KarmaExpenseType.RemoveQuality:
                    {
                        // Add the Quality back to the character.
                        List<Weapon> lstWeapons = new List<Weapon>(1);
                        Quality objAddQuality = new Quality(objCharacter);
                        try
                        {
                            XmlDocument objXmlQualityDocument = await objCharacter.LoadDataAsync("qualities.xml", token: token).ConfigureAwait(false);
                            XmlNode objXmlQualityNode = objXmlQualityDocument.TryGetNodeByNameOrId("/chummer/qualities/quality", strUndoId);
                            await objAddQuality.CreateAsync(objXmlQualityNode, QualitySource.Selected, lstWeapons, objExpense.Undo.Extra, token: token)
                                               .ConfigureAwait(false);
                            await objCharacter.Qualities.AddAsync(objAddQuality, token).ConfigureAwait(false);
                            foreach (Weapon objWeapon in lstWeapons)
                                await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                        }
                        catch
                        {
                            await objAddQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                            throw;
                        }
                        break;
                    }
                case KarmaExpenseType.AddCritterPower:
                    for (int i = await objCharacter.CritterPowers.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                    {
                        if (i >= await objCharacter.CritterPowers.GetCountAsync(token).ConfigureAwait(false))
                            continue;
                        CritterPower objPower = await objCharacter.CritterPowers.GetValueAtAsync(i, token).ConfigureAwait(false);
                        if (objPower.InternalId == strUndoId)
                            await objPower.RemoveAsync(false, token).ConfigureAwait(false);
                    }
                    break;

                // ManualAdd, ManualSubtract, QuickeningMetamagic and the types legacy has no special handling for
                // (AddAIProgram, AddAIAdvancedProgram, SpiritFettering) only refund the karma.
            }

            // Refund the Karma amount and remove the Expense Entry.
            await objCharacter.ModifyKarmaAsync(-objExpense.Amount.ToInt32(), token).ConfigureAwait(false);
            await objCharacter.ExpenseEntries.RemoveAsync(objExpense, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>
        /// Legacy BindFocus undo: it unticks the focus in the foci tree, whose AfterCheck handler (treFoci_AfterCheck) unbonds
        /// the gear, removes its improvements and drops the focus; stacked foci are unbonded and lose their improvements.
        /// </summary>
        private static async Task UndoBindFocusAsync(Character objCharacter, string strUndoId, CancellationToken token)
        {
            bool blnFound = false;
            for (int i = await objCharacter.Foci.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
            {
                if (i >= await objCharacter.Foci.GetCountAsync(token).ConfigureAwait(false))
                    continue;
                Focus objFocus = await objCharacter.Foci.GetValueAtAsync(i, token).ConfigureAwait(false);
                if (objFocus == null || objFocus.InternalId != strUndoId)
                    continue;
                blnFound = true;
                await UnbindFocusAsync(objCharacter, objFocus, token).ConfigureAwait(false);
            }

            if (!blnFound)
            {
                for (int i = await objCharacter.StackedFoci.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                {
                    if (i >= await objCharacter.StackedFoci.GetCountAsync(token).ConfigureAwait(false))
                        continue;
                    StackedFocus objStack = await objCharacter.StackedFoci.GetValueAtAsync(i, token).ConfigureAwait(false);
                    if (objStack.InternalId != strUndoId)
                        continue;
                    blnFound = true;
                    objStack.Bonded = false;
                    await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.StackedFocus,
                                                                     objStack.InternalId, token).ConfigureAwait(false);
                }
            }

            // Legacy fix for bad ID in undo entry (used the focus' gear object's ID instead of the focus')
            if (!blnFound)
            {
                for (int i = await objCharacter.Foci.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                {
                    if (i >= await objCharacter.Foci.GetCountAsync(token).ConfigureAwait(false))
                        continue;
                    Focus objFocus = await objCharacter.Foci.GetValueAtAsync(i, token).ConfigureAwait(false);
                    if (objFocus?.GearObject?.InternalId != strUndoId)
                        continue;
                    await UnbindFocusAsync(objCharacter, objFocus, token).ConfigureAwait(false);
                }
            }
        }

        private static async Task UnbindFocusAsync(Character objCharacter, Focus objFocus, CancellationToken token)
        {
            Gear objGear = objFocus.GearObject;
            if (objGear == null)
                return;
            objGear.Bonded = false;
            await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.Gear, objGear.InternalId, token).ConfigureAwait(false);
            await objCharacter.Foci.RemoveAsync(objFocus, token).ConfigureAwait(false);
        }

        /// <summary>Legacy tsUndoNuyenExpense_Click.</summary>
        public static async Task<RuleResult> UndoNuyenExpenseAsync(Character objCharacter, ExpenseLogEntry objExpense, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (!await ConfirmUndoAsync(objCharacter, objExpense, token).ConfigureAwait(false))
                return RuleResult.Cancelled;

            string strUndoId = objExpense.Undo.ObjectId;
            if (!string.IsNullOrEmpty(strUndoId))
            {
                switch (objExpense.Undo.NuyenType)
                {
                    case NuyenExpenseType.AddCyberware:
                        {
                            Cyberware objCyberware = await objCharacter.Cyberware.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false)
                                                     ?? (await objCharacter.Vehicles.FindVehicleCyberwareAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objCyberware != null)
                                await objCyberware.DeleteCyberwareAsync(blnIncreaseEssenceHole: false, token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddGear:
                        {
                            // If the gear was already deleted manually we will not be able to locate it here.
                            Gear objGear = await objCharacter.Gear.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false)
                                           ?? (await objCharacter.Vehicles.FindVehicleGearAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objGear == null)
                                break;
                            await objGear.SetQuantityAsync(objGear.Quantity - objExpense.Undo.Qty, token).ConfigureAwait(false);
                            if (objGear.Quantity <= 0)
                                await objGear.DeleteGearAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddDrug:
                        {
                            Drug objDrug = await objCharacter.Drugs.FirstOrDefaultAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false)
                                           ?? await FindNestedDrugAsync(await objCharacter.GetCyberwareAsync(token).ConfigureAwait(false), strUndoId, token).ConfigureAwait(false);
                            if (objDrug == null)
                                break;
                            decimal decQty = objExpense.Undo.Qty;
                            if (decQty <= 0)
                                decQty = 1;
                            objDrug.Quantity -= decQty;
                            if (objDrug.Quantity <= 0)
                                await objDrug.RemoveAsync(false, token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicle:
                        {
                            Vehicle objVehicle = await objCharacter.Vehicles.FindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objVehicle != null)
                                await objVehicle.DeleteVehicleAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicleMod:
                        {
                            VehicleMod objVehicleMod = (await objCharacter.Vehicles.FindVehicleModAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objVehicleMod != null)
                                await objVehicleMod.DeleteVehicleModAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicleGear:
                        {
                            Gear objGear = (await objCharacter.Vehicles.FindVehicleGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? await objCharacter.Gear.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false)
                                           ?? (await objCharacter.Cyberware.FindCyberwareGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? (await objCharacter.Weapons.FindWeaponGearAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objGear != null)
                            {
                                await objGear.SetQuantityAsync(objGear.Quantity - objExpense.Undo.Qty, token).ConfigureAwait(false);
                                if (objGear.Quantity <= 0)
                                    await objGear.DeleteGearAsync(token: token).ConfigureAwait(false);
                            }
                            break;
                        }
                    case NuyenExpenseType.AddVehicleWeapon:
                        {
                            Weapon objWeapon = (await objCharacter.Vehicles.FindVehicleWeaponAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                               ?? await objCharacter.Weapons.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objWeapon != null)
                                await objWeapon.DeleteWeaponAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicleWeaponAccessory:
                        {
                            WeaponAccessory objAccessory = await objCharacter.Vehicles.FindVehicleWeaponAccessoryAsync(strUndoId, token).ConfigureAwait(false)
                                                           ?? await objCharacter.Weapons.FindWeaponAccessoryAsync(strUndoId, token).ConfigureAwait(false);
                            if (objAccessory != null)
                                await objAccessory.DeleteWeaponAccessoryAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicleWeaponMount:
                        {
                            WeaponMount objWeaponMount = (await objCharacter.Vehicles.FindVehicleWeaponMountAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objWeaponMount != null)
                                await objWeaponMount.DeleteWeaponMountAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddVehicleWeaponMountMod:
                        {
                            VehicleMod objVehicleMod = (await objCharacter.Vehicles.FindVehicleWeaponMountModAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objVehicleMod != null)
                                await objVehicleMod.DeleteVehicleModAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.ModifyVehicleWeaponMount:
                        await MessageBoxes.ShowAsync(
                            await LanguageManager.GetStringAsync("Message_UndoExpenseNotSupported", token: token).ConfigureAwait(false),
                            await LanguageManager.GetStringAsync("String_NotSupported", token: token).ConfigureAwait(false),
                            MessageButtons.OK, MessageIcon.Warning, token: token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;

                    case NuyenExpenseType.AddArmor:
                        {
                            Armor objArmor = await objCharacter.Armor.FindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objArmor != null)
                                await objArmor.DeleteArmorAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddArmorMod:
                        {
                            ArmorMod objArmorMod = await objCharacter.Armor.FindArmorModAsync(strUndoId, token).ConfigureAwait(false);
                            if (objArmorMod != null)
                                await objArmorMod.DeleteArmorModAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddWeapon:
                        {
                            Weapon objWeapon = await objCharacter.Weapons.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false)
                                               ?? (await objCharacter.Vehicles.FindVehicleWeaponAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objWeapon != null)
                                await objWeapon.DeleteWeaponAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddWeaponAccessory:
                        {
                            WeaponAccessory objAccessory = await objCharacter.Weapons.FindWeaponAccessoryAsync(strUndoId, token).ConfigureAwait(false)
                                                           ?? await objCharacter.Vehicles.FindVehicleWeaponAccessoryAsync(strUndoId, token).ConfigureAwait(false);
                            if (objAccessory != null)
                                await objAccessory.DeleteWeaponAccessoryAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.IncreaseLifestyle:
                        {
                            Lifestyle objLifestyle = await objCharacter.Lifestyles.FirstOrDefaultAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false);
                            if (objLifestyle != null)
                                await objLifestyle.ModifyIncrementsAsync(-1, token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddArmorGear:
                        {
                            Gear objGear = (await objCharacter.Armor.FindArmorGearAsync(strUndoId, token).ConfigureAwait(false)).Item1;
                            if (objGear != null)
                            {
                                await objGear.SetQuantityAsync(objGear.Quantity - objExpense.Undo.Qty, token).ConfigureAwait(false);
                                if (objGear.Quantity <= 0)
                                    await objGear.DeleteGearAsync(token: token).ConfigureAwait(false);
                            }
                            break;
                        }
                    case NuyenExpenseType.AddVehicleModCyberware:
                        {
                            Cyberware objCyberware = (await objCharacter.Vehicles.FindVehicleCyberwareAsync(x => x.InternalId == strUndoId, token).ConfigureAwait(false)).Item1
                                                     ?? await objCharacter.Cyberware.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objCyberware != null)
                                await objCyberware.DeleteCyberwareAsync(blnIncreaseEssenceHole: false, token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddCyberwareGear:
                        {
                            Gear objGear = (await objCharacter.Cyberware.FindCyberwareGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? (await objCharacter.Vehicles.FindVehicleGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? await objCharacter.Gear.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objGear != null)
                                await objGear.DeleteGearAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    case NuyenExpenseType.AddWeaponGear:
                        {
                            Gear objGear = (await objCharacter.Weapons.FindWeaponGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? (await objCharacter.Vehicles.FindVehicleGearAsync(strUndoId, token).ConfigureAwait(false)).Item1
                                           ?? await objCharacter.Gear.DeepFindByIdAsync(strUndoId, token).ConfigureAwait(false);
                            if (objGear != null)
                                await objGear.DeleteGearAsync(token: token).ConfigureAwait(false);
                            break;
                        }
                    // ManualAdd, ManualSubtract and the types legacy has no special handling for (AddWeaponMod,
                    // AddVehicleWeaponMod, ImproveInitiateGrade) only refund the nuyen.
                }
            }

            // Refund the Nuyen amount and remove the Expense Entry.
            await objCharacter.ModifyNuyenAsync(-objExpense.Amount, token).ConfigureAwait(false);
            await objCharacter.ExpenseEntries.RemoveAsync(objExpense, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Drugs nested under a Chemical Gland (DrugChildren) rather than Character.Drugs.</summary>
        private static async Task<Drug> FindNestedDrugAsync(ThreadSafeObservableCollection<Cyberware> lstWare, string strId, CancellationToken token)
        {
            foreach (Cyberware objWare in lstWare)
            {
                Drug objFound = await (await objWare.GetDrugChildrenAsync(token).ConfigureAwait(false))
                                      .FirstOrDefaultAsync(x => x.InternalId == strId, token).ConfigureAwait(false);
                if (objFound != null)
                    return objFound;
                objFound = await FindNestedDrugAsync(await objWare.GetChildrenAsync(token).ConfigureAwait(false), strId, token).ConfigureAwait(false);
                if (objFound != null)
                    return objFound;
            }
            return null;
        }
    }
}
