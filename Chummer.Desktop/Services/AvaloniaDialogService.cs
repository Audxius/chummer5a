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
using Chummer.Desktop.Localization;
using Chummer.Desktop.Views;

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// Answers the engine's mid-operation prompts with the app's generic prompt window. The specialised pickers
    /// (spells, powers, metamagics...) arrive with the generic browser component (Phase 6 of the port plan).
    /// </summary>
    public sealed class AvaloniaDialogService : IDialogService
    {
        public DialogOutcome Show(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objRequest == null)
                throw new ArgumentNullException(nameof(objRequest));
            if (objRequest.Opacity == 0)
                return DialogOutcome.OK;
            return ModalHelper.RunModal(() => ShowCore(objRequest, owner));
        }

        public Task<DialogOutcome> ShowAsync(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            if (objRequest == null)
                throw new ArgumentNullException(nameof(objRequest));
            if (objRequest.Opacity == 0)
                return Task.FromResult(DialogOutcome.OK);
            return ModalHelper.RunModalAsync(() => ShowCore(objRequest, owner), token);
        }

        private static async Task<DialogOutcome> ShowCore(DialogRequest objRequest, object owner)
        {
            switch (objRequest)
            {
                case SelectItemRequest objItem:
                {
                    List<ListItem> lstItems = LastCallArgument<IEnumerable<ListItem>>(objItem, "SetGeneralItemsMode")?.ToList()
                                              ?? LastCallArgument<IEnumerable<ListItem>>(objItem, "SetDropdownItemsMode")?.ToList()
                                              ?? new List<ListItem>();
                    string strForced = LastCallArgument<string>(objItem, "ForceItem");
                    ListItem objForced = lstItems.FirstOrDefault(x => x.Value?.ToString() == strForced);
                    if (!string.IsNullOrEmpty(strForced) && objForced.Value != null)
                    {
                        Accept(objItem, objForced);
                        return DialogOutcome.OK;
                    }
                    if (lstItems.Count == 1 && objItem.AllowAutoSelect)
                    {
                        Accept(objItem, lstItems[0]);
                        return DialogOutcome.OK;
                    }
                    ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objItem.Description ?? Tr.Get("Title_SelectItem"), lstItems,
                                                                         objItem.SelectedItem).ConfigureAwait(true);
                    if (objChoice == null)
                        return DialogOutcome.Cancel;
                    Accept(objItem, objChoice.Value);
                    return DialogOutcome.OK;
                }
                case SelectAttributeRequest objAttribute:
                {
                    string[] astrAbbrevs = objAttribute.ConstructorArguments.FirstOrDefault() as string[] ?? Array.Empty<string>();
                    List<ListItem> lstItems = astrAbbrevs.Select(x => new ListItem(x, LanguageManager.GetString("String_Attribute" + x + "Short"))).ToList();
                    ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objAttribute.Description ?? Tr.Get("Title_SelectAttribute"), lstItems, null).ConfigureAwait(true);
                    if (objChoice == null)
                        return DialogOutcome.Cancel;
                    objAttribute.SetResult(nameof(SelectAttributeRequest.SelectedAttribute), objChoice.Value.Value?.ToString());
                    return DialogOutcome.OK;
                }
                case SelectLimitRequest objLimit:
                {
                    string[] astrLimits = objLimit.ConstructorArguments.FirstOrDefault() as string[] ?? Array.Empty<string>();
                    List<ListItem> lstItems = astrLimits.Select(x => new ListItem(x, LanguageManager.GetString("String_Limit" + x + "Short", false) is string s && s.Length > 0 ? s : x)).ToList();
                    ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objLimit.Description ?? Tr.Get("Title_SelectLimit"), lstItems, null).ConfigureAwait(true);
                    if (objChoice == null)
                        return DialogOutcome.Cancel;
                    objLimit.SetResult(nameof(SelectLimitRequest.SelectedLimit), objChoice.Value.Value?.ToString());
                    objLimit.SetResult(nameof(SelectLimitRequest.SelectedDisplayLimit), objChoice.Value.Name);
                    return DialogOutcome.OK;
                }
                case SelectSideRequest objSide:
                {
                    string strForced = LastCallArgument<string>(objSide, "ForceValue");
                    if (!string.IsNullOrEmpty(strForced))
                    {
                        objSide.SetResult(nameof(SelectSideRequest.SelectedSide), strForced);
                        return DialogOutcome.OK;
                    }
                    List<ListItem> lstItems = new List<ListItem>
                    {
                        new ListItem("Left", LanguageManager.GetString("String_Improvement_SideLeft")),
                        new ListItem("Right", LanguageManager.GetString("String_Improvement_SideRight")),
                    };
                    ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objSide.Description ?? Tr.Get("Title_SelectSide"), lstItems, null).ConfigureAwait(true);
                    if (objChoice == null)
                        return DialogOutcome.Cancel;
                    objSide.SetResult(nameof(SelectSideRequest.SelectedSide), objChoice.Value.Value?.ToString());
                    return DialogOutcome.OK;
                }
                case SelectNumberRequest objNumber:
                {
                    decimal? decValue = await PromptWindow.AskNumberAsync(owner, objNumber.Description ?? Tr.Get("Title_SelectNumber"),
                                                                          objNumber.Minimum, objNumber.Maximum, objNumber.AllowCancel).ConfigureAwait(true);
                    if (decValue == null)
                        return DialogOutcome.Cancel;
                    objNumber.SetResult(nameof(SelectNumberRequest.SelectedValue), decValue.Value);
                    return DialogOutcome.OK;
                }
                case SelectTextRequest objText:
                {
                    string strValue = await PromptWindow.AskTextAsync(owner, objText.Description ?? Tr.Get("Title_SelectText"), objText.SelectedValue).ConfigureAwait(true);
                    if (strValue == null)
                        return DialogOutcome.Cancel;
                    if (objText.PreventXPathErrors)
                        strValue = strValue.Replace('"', '\'');
                    objText.SetResult(nameof(SelectTextRequest.SelectedValue), strValue);
                    return DialogOutcome.OK;
                }
                case SelectBuildMethodRequest objBuildMethod:
                {
                    Character objCharacter = objBuildMethod.ConstructorArguments.FirstOrDefault() as Character;
                    bool blnForExisting = objBuildMethod.ConstructorArguments.Count > 1 && objBuildMethod.ConstructorArguments[1] is bool blnTemp && blnTemp;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    System.Collections.Generic.IReadOnlyDictionary<string, CharacterSettings> dicSettings = await SettingsManager.GetLoadedCharacterSettingsAsync().ConfigureAwait(true);
                    List<ListItem> lstItems = dicSettings.OrderBy(x => x.Value.CurrentDisplayName, StringComparer.CurrentCulture)
                                                         .Select(x => new ListItem(x.Key, x.Value.CurrentDisplayName + " — " + Tr.Get("String_" + x.Value.BuildMethod)))
                                                         .ToList();
                    CharacterSettings objCurrent = await objCharacter.GetSettingsAsync().ConfigureAwait(true);
                    string strCurrentKey = await objCharacter.GetSettingsKeyAsync().ConfigureAwait(true);
                    ListItem? objChoice = await PromptWindow.ChooseAsync(owner, Tr.Get("Title_SelectBP"), lstItems, strCurrentKey).ConfigureAwait(true);
                    if (objChoice == null || !dicSettings.TryGetValue(objChoice.Value.Value?.ToString() ?? string.Empty, out CharacterSettings objSelected))
                        return DialogOutcome.Cancel;
                    return await CharacterSettingsApplier.ApplyAsync(objCharacter, objSelected, false, blnForExisting, objCurrent.BuildMethod, owner).ConfigureAwait(true)
                        ? DialogOutcome.OK
                        : DialogOutcome.Cancel;
                }
                default:
                    await MessageBoxes.ShowAsync(
                        "This choice (" + objRequest.DialogName + ") is not available in Chummer Native yet. The operation was cancelled.",
                        "Chummer Native", MessageButtons.OK, MessageIcon.Information).ConfigureAwait(true);
                    return DialogOutcome.Cancel;
            }
        }

        private static void Accept(SelectItemRequest objRequest, ListItem objItem)
        {
            objRequest.SetResult(nameof(SelectItemRequest.SelectedItem), objItem.Value?.ToString());
            objRequest.SetResult(nameof(SelectItemRequest.SelectedName), objItem.Name);
        }

        private static T LastCallArgument<T>(DialogRequest objRequest, string strMethod) where T : class
        {
            for (int i = objRequest.Operations.Count - 1; i >= 0; --i)
            {
                DialogOperation objOperation = objRequest.Operations[i];
                if (objOperation.IsCall && objOperation.Name == strMethod && objOperation.Arguments.Length > 0)
                    return objOperation.Arguments[0] as T;
            }
            return null;
        }
    }
}
