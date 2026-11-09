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
using Chummer.Backend.Attributes;

namespace Chummer.Rules
{
    public enum RuleResult
    {
        Changed,
        NotAllowed,
        NotEnoughKarma,
        Cancelled,
        NotEnoughNuyen,
    }

    /// <summary>
    /// Raising and lowering attributes, lifted from the legacy AttributeControl (cmdImproveATT_Click, cmdBurnEdge_Click,
    /// BaseChangeTimer_Tick, KarmaChangeTimer_Tick) so every UI applies the same checks and prompts.
    /// </summary>
    public static class AttributeRules
    {
        /// <summary>Career mode: spend karma to raise an attribute by one (with the same confirmation as legacy Chummer).</summary>
        public static async Task<RuleResult> ImproveCareerAsync(Character objCharacter, CharacterAttrib objAttribute, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objAttribute == null)
                throw new ArgumentNullException(nameof(objAttribute));
            if (!await objAttribute.GetCanUpgradeCareerAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intCost = await objAttribute.GetUpgradeKarmaCostAsync(token).ConfigureAwait(false);
            if (intCost == -1)
                return RuleResult.NotAllowed;
            if (intCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false))
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NotEnoughKarma", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotEnoughKarma;
            }
            string strConfirm = StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpense", token: token).ConfigureAwait(false),
                await objAttribute.GetDisplayNameFormattedAsync(token).ConfigureAwait(false),
                await objAttribute.GetValueAsync(token).ConfigureAwait(false) + 1,
                intCost);
            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(strConfirm, token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await objAttribute.Upgrade(token: token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Career mode: permanently burn a point of Edge.</summary>
        public static async Task<RuleResult> BurnEdgeAsync(CharacterAttrib objAttribute, CancellationToken token = default)
        {
            if (objAttribute == null)
                throw new ArgumentNullException(nameof(objAttribute));
            if (await objAttribute.GetValueAsync(token).ConfigureAwait(false) <= 0)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_CannotBurnEdge", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_CannotBurnEdge", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Warning, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            if (await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_BurnEdge", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_BurnEdge", token: token).ConfigureAwait(false),
                    MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) != MessageResult.Yes)
                return RuleResult.Cancelled;
            await objAttribute.Degrade(token: token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Create mode: change the points bought from the priority table (Base) by <paramref name="intDelta"/>.</summary>
        public static Task<RuleResult> ChangeBaseAsync(Character objCharacter, CharacterAttrib objAttribute, int intDelta, CancellationToken token = default)
            => ChangeCreateAsync(objCharacter, objAttribute, intDelta, true, token);

        /// <summary>Create mode: change the points bought with karma by <paramref name="intDelta"/>.</summary>
        public static Task<RuleResult> ChangeKarmaAsync(Character objCharacter, CharacterAttrib objAttribute, int intDelta, CancellationToken token = default)
            => ChangeCreateAsync(objCharacter, objAttribute, intDelta, false, token);

        /// <summary>Create mode: raise by one, using priority points while they last and karma afterwards.</summary>
        public static async Task<RuleResult> RaiseCreateAsync(Character objCharacter, CharacterAttrib objAttribute, CancellationToken token = default)
        {
            if (await objAttribute.GetBaseUnlockedAsync(token).ConfigureAwait(false)
                && await objAttribute.GetBaseAsync(token).ConfigureAwait(false) < await objAttribute.GetPriorityMaximumAsync(token).ConfigureAwait(false)
                && await objAttribute.GetKarmaAsync(token).ConfigureAwait(false) == 0)
                return await ChangeBaseAsync(objCharacter, objAttribute, 1, token).ConfigureAwait(false);
            return await ChangeKarmaAsync(objCharacter, objAttribute, 1, token).ConfigureAwait(false);
        }

        /// <summary>Create mode: lower by one, removing karma points first.</summary>
        public static async Task<RuleResult> LowerCreateAsync(Character objCharacter, CharacterAttrib objAttribute, CancellationToken token = default)
        {
            if (await objAttribute.GetKarmaAsync(token).ConfigureAwait(false) > 0)
                return await ChangeKarmaAsync(objCharacter, objAttribute, -1, token).ConfigureAwait(false);
            return await ChangeBaseAsync(objCharacter, objAttribute, -1, token).ConfigureAwait(false);
        }

        private static async Task<RuleResult> ChangeCreateAsync(Character objCharacter, CharacterAttrib objAttribute, int intDelta, bool blnBase, CancellationToken token)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objAttribute == null)
                throw new ArgumentNullException(nameof(objAttribute));
            if (intDelta == 0 || await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            if (blnBase && !await objAttribute.GetBaseUnlockedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intBase = await objAttribute.GetBaseAsync(token).ConfigureAwait(false);
            int intKarma = await objAttribute.GetKarmaAsync(token).ConfigureAwait(false);
            int intNewBase = blnBase ? intBase + intDelta : intBase;
            int intNewKarma = blnBase ? intKarma : intKarma + intDelta;
            if (intNewBase < 0 || intNewKarma < 0)
                return RuleResult.NotAllowed;
            if (blnBase && intNewBase > await objAttribute.GetPriorityMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            if (!blnBase && intNewKarma > await objAttribute.GetKarmaMaximumAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            if (intDelta > 0)
            {
                // Same value the legacy control checks against the metatype maximum.
                int intOther = blnBase ? intNewKarma : intNewBase;
                int intChanged = blnBase ? intNewBase : intNewKarma;
                int intValue = Math.Max(
                                   intOther + await objAttribute.GetFreeBaseAsync(token).ConfigureAwait(false)
                                   + await objAttribute.GetRawMinimumAsync(token).ConfigureAwait(false)
                                   + await objAttribute.GetAttributeValueModifiersAsync(token).ConfigureAwait(false),
                                   await objAttribute.GetTotalMinimumAsync(token).ConfigureAwait(false))
                               + intChanged;
                if (!await CanBeMetatypeMaxAsync(objCharacter, objAttribute, intValue, token).ConfigureAwait(false))
                    return RuleResult.NotAllowed;
            }
            if (blnBase)
                await objAttribute.SetBaseAsync(intNewBase, token).ConfigureAwait(false);
            else
                await objAttribute.SetKarmaAsync(intNewKarma, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        private static async Task<bool> CanBeMetatypeMaxAsync(Character objCharacter, CharacterAttrib objAttribute, int intValue, CancellationToken token)
        {
            int intTotalMaximum = await objAttribute.GetTotalMaximumAsync(token).ConfigureAwait(false);
            if (intValue < intTotalMaximum || intTotalMaximum == 0)
                return true;
            if (await (await objCharacter.GetAttributeSectionAsync(token).ConfigureAwait(false))
                      .CanRaiseAttributeToMetatypeMax(objAttribute, token).ConfigureAwait(false))
                return true;
            await MessageBoxes.ShowAsync(
                StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                    await LanguageManager.GetStringAsync("Message_AttributeMaximum", token: token).ConfigureAwait(false),
                    await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetMaxNumberMaxAttributesCreateAsync(token).ConfigureAwait(false)),
                await LanguageManager.GetStringAsync("MessageTitle_Attribute", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
            return false;
        }
    }
}
