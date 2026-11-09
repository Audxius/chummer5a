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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Enums;

namespace Chummer
{
    /// <summary>
    /// Applying a chosen settings profile to a character (lifted from the WinForms SelectBuildMethod dialog so every UI
    /// shares the rules, Phase 2 of the native port).
    /// </summary>
    public static class CharacterSettingsApplier
    {
        /// <summary>
        /// Switches the character to <paramref name="objSelected"/>, switching build methods when an existing creation-mode
        /// character changes between build methods (after asking the user).
        /// </summary>
        /// <param name="eStartingBuildMethod">Build method of the settings the character had when the choice started.</param>
        /// <returns>False if the user declined or the build method switch failed.</returns>
        public static async Task<bool> ApplyAsync(Character objCharacter, CharacterSettings objSelected, bool blnIgnoreRules,
            bool blnForExistingCharacter, CharacterBuildMethod eStartingBuildMethod, object objOwner = null, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSelected == null)
                throw new ArgumentNullException(nameof(objSelected));
            CharacterBuildMethod eSelectedBuildMethod = objSelected.BuildMethod;
            string strSelectedKey = (await SettingsManager.GetLoadedCharacterSettingsAsync(token).ConfigureAwait(false))
                                    .FirstOrDefault(x => ReferenceEquals(x.Value, objSelected)).Key;
            if (blnForExistingCharacter
                && !await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                && await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetBuildMethodAsync(token).ConfigureAwait(false)
                == await objCharacter.GetEffectiveBuildMethodAsync(token).ConfigureAwait(false)
                && eSelectedBuildMethod != eStartingBuildMethod)
            {
                if (await HostServices.Messages.ShowAsync(objOwner,
                        StringExtensions.FastFormat(
                            await LanguageManager.GetStringAsync("Message_SelectBP_SwitchBuildMethods", token: token).ConfigureAwait(false),
                            await LanguageManager.GetStringAsync("String_" + eSelectedBuildMethod, token: token).ConfigureAwait(false),
                            await LanguageManager.GetStringAsync("String_" + eStartingBuildMethod, token: token).ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync("MessageTitle_SelectBP_SwitchBuildMethods", token: token).ConfigureAwait(false),
                        MessageButtons.YesNo, MessageIcon.Warning, token: token).ConfigureAwait(false) != MessageResult.Yes)
                    return false;
                string strOldCharacterSettingsKey = await objCharacter.GetSettingsKeyAsync(token).ConfigureAwait(false);
                await objCharacter.SetSettingsKeyAsync(strSelectedKey, token).ConfigureAwait(false);
                // If the character is loading, make sure we only switch build methods after we've loaded, otherwise we might cause all sorts of nastiness
                if (objCharacter.IsLoading)
                    await objCharacter.EnqueuePostLoadAsyncMethodAsync(
                        x => objCharacter.SwitchBuildMethods(eStartingBuildMethod, eSelectedBuildMethod, strOldCharacterSettingsKey, x),
                        token).ConfigureAwait(false);
                else if (!await objCharacter.SwitchBuildMethods(eStartingBuildMethod, eSelectedBuildMethod, strOldCharacterSettingsKey, token)
                                            .ConfigureAwait(false))
                    return false;
            }
            else
            {
                await objCharacter.SetSettingsKeyAsync(strSelectedKey, token).ConfigureAwait(false);
            }

            await objCharacter.SetIgnoreRulesAsync(blnIgnoreRules, token).ConfigureAwait(false);
            return true;
        }
    }
}
