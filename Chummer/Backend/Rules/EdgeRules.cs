/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    /// <summary>
    /// Spending and regaining Edge points during play (legacy CharacterCareer cmdEdgeSpent_Click / cmdEdgeGained_Click),
    /// plus a full refresh at the start of a session.
    /// </summary>
    public static class EdgeRules
    {
        public static async Task<RuleResult> SpendAsync(Character objCharacter, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            int intTotal = await (await objCharacter.GetAttributeAsync("EDG", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false);
            if (await objCharacter.GetEdgeUsedAsync(token).ConfigureAwait(false) >= intTotal)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_CannotSpendEdge", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_CannotSpendEdge", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            await objCharacter.ModifyEdgeUsedAsync(1, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        public static async Task<RuleResult> RegainAsync(Character objCharacter, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (await objCharacter.GetEdgeUsedAsync(token).ConfigureAwait(false) <= 0)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_CannotRegainEdge", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_CannotRegainEdge", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            await objCharacter.ModifyEdgeUsedAsync(-1, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Regain every spent Edge point (start of a new session / after a rest).</summary>
        public static async Task<RuleResult> RefreshAsync(Character objCharacter, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            int intUsed = await objCharacter.GetEdgeUsedAsync(token).ConfigureAwait(false);
            if (intUsed <= 0)
                return RuleResult.NotAllowed;
            await objCharacter.ModifyEdgeUsedAsync(-intUsed, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }
    }
}
