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
using Chummer.Selection;

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
                case SelectOptionalPowerRequest objOptional:
                {
                    Character objCharacter = objOptional.ConstructorArguments.FirstOrDefault() as Character;
                    ValueTuple<string, string>[] aobjPairs = objOptional.ConstructorArguments.Count > 1 ? objOptional.ConstructorArguments[1] as ValueTuple<string, string>[] : null;
                    if (objCharacter == null || aobjPairs == null || aobjPairs.Length == 0)
                        return DialogOutcome.Cancel;
                    List<ListItem> lstPowers = aobjPairs.Select(x => new ListItem(x, string.IsNullOrEmpty(x.Item2)
                                                                ? objCharacter.TranslateExtra(x.Item1)
                                                                : objCharacter.TranslateExtra(x.Item1) + " (" + objCharacter.TranslateExtra(x.Item2) + ")")).ToList();
                    ValueTuple<string, string> objPicked;
                    if (lstPowers.Count == 1)
                        objPicked = aobjPairs[0];
                    else
                    {
                        ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objOptional.Description ?? Tr.Get("Title_SelectOptionalPower"), lstPowers, null).ConfigureAwait(true);
                        if (objChoice == null)
                            return DialogOutcome.Cancel;
                        objPicked = (ValueTuple<string, string>)objChoice.Value.Value;
                    }
                    objOptional.SetResult(nameof(SelectOptionalPowerRequest.SelectedPower), objPicked.Item1);
                    objOptional.SetResult(nameof(SelectOptionalPowerRequest.SelectedPowerExtra), objPicked.Item2);
                    return DialogOutcome.OK;
                }
                case SelectDiceHitsRequest objHits:
                {
                    int intDice = 0;
                    for (int i = objHits.Operations.Count - 1; i >= 0; --i)
                    {
                        DialogOperation objOperation = objHits.Operations[i];
                        if (objOperation.IsCall && objOperation.Name == "SetDiceAsync" && objOperation.Arguments.Length > 0 && objOperation.Arguments[0] is int intLoop)
                        {
                            intDice = intLoop;
                            break;
                        }
                    }
                    decimal? decHits = await PromptWindow.AskNumberAsync(owner, (objHits.Description ?? objHits.Text ?? "Hits") + "  (" + intDice.ToString(GlobalSettings.CultureInfo) + "d6)",
                                                                         0, Math.Max(0, intDice), true).ConfigureAwait(true);
                    if (decHits == null)
                        return DialogOutcome.Cancel;
                    objHits.SetResult(nameof(SelectDiceHitsRequest.Result), (int)decHits.Value);
                    return DialogOutcome.OK;
                }
                case SelectMetamagicRequest objMetamagic:
                {
                    Character objCharacter = objMetamagic.ConstructorArguments.FirstOrDefault() as Character;
                    InitiationGrade objGrade = objMetamagic.ConstructorArguments.Count > 1 ? objMetamagic.ConstructorArguments[1] as InitiationGrade : null;
                    if (objCharacter == null || objGrade == null)
                        return DialogOutcome.Cancel;
                    string strId = await BrowseIdAsync(owner, DataBrowser.Metamagic(objCharacter, objGrade)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objMetamagic.SetResult(nameof(SelectMetamagicRequest.SelectedMetamagic), strId);
                    return DialogOutcome.OK;
                }
                case SelectArtRequest objArt:
                {
                    Character objCharacter = objArt.ConstructorArguments.FirstOrDefault() as Character;
                    SelectArtMode eMode = objArt.ConstructorArguments.Count > 1 && objArt.ConstructorArguments[1] is SelectArtMode eTemp ? eTemp : SelectArtMode.Art;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    string strId = await BrowseIdAsync(owner, DataBrowser.Art(objCharacter, eMode)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objArt.SetResult(nameof(SelectArtRequest.SelectedItem), strId);
                    return DialogOutcome.OK;
                }
                case SelectMartialArtRequest objMartialArt:
                {
                    Character objCharacter = objMartialArt.ConstructorArguments.FirstOrDefault() as Character;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    string strId = await BrowseIdAsync(owner, DataBrowser.MartialArt(objCharacter)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objMartialArt.SetResult(nameof(SelectMartialArtRequest.SelectedMartialArt), strId);
                    objMartialArt.SetResult(nameof(SelectMartialArtRequest.AddAgain), false);
                    return DialogOutcome.OK;
                }
                case SelectMentorSpiritRequest objMentor:
                {
                    Character objCharacter = objMentor.ConstructorArguments.FirstOrDefault() as Character;
                    string strFile = objMentor.ConstructorArguments.Count > 1 ? objMentor.ConstructorArguments[1] as string : null;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    DataBrowser objBrowser = DataBrowser.Mentor(objCharacter, strFile);
                    string strId;
                    string strForced = objMentor.ForcedMentor;
                    ListItem objForced = string.IsNullOrEmpty(strForced) ? default : objBrowser.Options(true).FirstOrDefault(x => objBrowser.Node(x.Value.ToString())?.SelectSingleNode("name")?.Value == strForced);
                    if (objForced.Value != null)
                        strId = objForced.Value.ToString();
                    else
                        strId = await BrowseIdAsync(owner, objBrowser).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    (List<ListItem> lstSet1, List<ListItem> lstSet2) = objBrowser.MentorChoices(strId);
                    string strChoice1 = lstSet1.Count == 0 ? string.Empty : await ChooseValueAsync(owner, LanguageManager.GetString("Label_SelectMentor_ChooseOne"), lstSet1, true).ConfigureAwait(true);
                    if (strChoice1 == null)
                        return DialogOutcome.Cancel;
                    string strChoice2 = lstSet2.Count == 0 ? string.Empty : await ChooseValueAsync(owner, LanguageManager.GetString("Label_SelectMentor_ChooseOne"), lstSet2, true).ConfigureAwait(true);
                    if (strChoice2 == null)
                        return DialogOutcome.Cancel;
                    objMentor.SetResult(nameof(SelectMentorSpiritRequest.SelectedMentor), strId);
                    objMentor.SetResult(nameof(SelectMentorSpiritRequest.Choice1), strChoice1);
                    objMentor.SetResult(nameof(SelectMentorSpiritRequest.Choice2), strChoice2);
                    return DialogOutcome.OK;
                }
                case SelectPowerRequest objPower:
                {
                    Character objCharacter = objPower.ConstructorArguments.FirstOrDefault() as Character;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    string strId = await BrowseIdAsync(owner, DataBrowser.Power(objCharacter, objPower.ForBonus, objPower.IgnoreLimits, objPower.LimitToPowers,
                                                                                objPower.LimitToRating, objPower.PointsPerLevel)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objPower.SetResult(nameof(SelectPowerRequest.SelectedPower), strId);
                    return DialogOutcome.OK;
                }
                case SelectComplexFormRequest objForm:
                {
                    Character objCharacter = objForm.ConstructorArguments.FirstOrDefault() as Character;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    string strId = await BrowseIdAsync(owner, DataBrowser.ComplexForm(objCharacter)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objForm.SetResult(nameof(SelectComplexFormRequest.SelectedComplexForm), strId);
                    return DialogOutcome.OK;
                }
                case SelectAIProgramRequest objProgram:
                {
                    Character objCharacter = objProgram.ConstructorArguments.FirstOrDefault() as Character;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    bool blnAdvanced = objProgram.ConstructorArguments.Count <= 1 || !(objProgram.ConstructorArguments[1] is bool blnA) || blnA;
                    bool blnInherent = objProgram.ConstructorArguments.Count > 2 && objProgram.ConstructorArguments[2] is bool blnI && blnI;
                    string strId = await BrowseIdAsync(owner, DataBrowser.AIProgram(objCharacter, blnAdvanced, blnInherent)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objProgram.SetResult(nameof(SelectAIProgramRequest.SelectedProgram), strId);
                    return DialogOutcome.OK;
                }
                case SelectSpellRequest objSpell:
                {
                    Character objCharacter = objSpell.ConstructorArguments.FirstOrDefault() as Character;
                    if (objCharacter == null)
                        return DialogOutcome.Cancel;
                    // A forced spell is accepted without asking, like the original.
                    string strId = !string.IsNullOrEmpty(objSpell.ForceSpellName)
                        ? objSpell.ForceSpellName
                        : await BrowseIdAsync(owner, DataBrowser.Spell(objCharacter, objSpell.LimitCategory, objSpell.IgnoreRequirements)).ConfigureAwait(true);
                    if (strId == null)
                        return DialogOutcome.Cancel;
                    objSpell.SetResult(nameof(SelectSpellRequest.SelectedSpell), strId);
                    return DialogOutcome.OK;
                }
                case SelectSkillRequest objSkill:
                    return await ChooseSkillAsync(objSkill, owner).ConfigureAwait(true);
                case SelectSkillGroupRequest objGroup:
                {
                    Character objCharacter = objGroup.ConstructorArguments.FirstOrDefault() as Character;
                    List<ListItem> lstGroups = CategorySelector.GetSkillGroups(objCharacter, objGroup.OnlyGroup, objGroup.ExcludeCategory);
                    string strChoice = await ChooseValueAsync(owner, objGroup.Description ?? Tr.Get("Title_SelectSkillGroup"), lstGroups, true).ConfigureAwait(true);
                    if (strChoice == null)
                        return DialogOutcome.Cancel;
                    objGroup.SetResult(nameof(SelectSkillGroupRequest.SelectedSkillGroup), strChoice);
                    return DialogOutcome.OK;
                }
                case SelectSpellCategoryRequest objSpellCategory:
                {
                    Character objCharacter = objSpellCategory.ConstructorArguments.FirstOrDefault() as Character;
                    List<ListItem> lstCategories = CategorySelector.GetSpellCategories(objCharacter, null,
                                                                                         LastCallArgument<IEnumerable<string>>(objSpellCategory, "SetExcludeCategories"));
                    string strChoice = await ChooseValueAsync(owner, objSpellCategory.Description ?? Tr.Get("Title_SelectSpellCategory"), lstCategories, true).ConfigureAwait(true);
                    if (strChoice == null)
                        return DialogOutcome.Cancel;
                    objSpellCategory.SetResult(nameof(SelectSpellCategoryRequest.SelectedCategory), strChoice);
                    return DialogOutcome.OK;
                }
                case SelectWeaponCategoryRequest objWeaponCategory:
                {
                    Character objCharacter = objWeaponCategory.ConstructorArguments.FirstOrDefault() as Character;
                    List<ListItem> lstCategories = CategorySelector.GetWeaponCategories(objCharacter, objWeaponCategory.OnlyCategory, objWeaponCategory.WeaponType);
                    // Like the original: no categories confirms an empty choice, one confirms it without asking.
                    string strChoice = lstCategories.Count == 0
                        ? string.Empty
                        : await ChooseValueAsync(owner, objWeaponCategory.Description ?? Tr.Get("Title_SelectWeaponCategory"), lstCategories, true).ConfigureAwait(true);
                    if (strChoice == null)
                        return DialogOutcome.Cancel;
                    objWeaponCategory.SetResult(nameof(SelectWeaponCategoryRequest.SelectedCategory), strChoice);
                    return DialogOutcome.OK;
                }
                default:
                    await MessageBoxes.ShowAsync(
                        "This choice (" + objRequest.DialogName + ") is not available in Chummer Native yet. The operation was cancelled.",
                        "Chummer Native", MessageButtons.OK, MessageIcon.Information).ConfigureAwait(true);
                    return DialogOutcome.Cancel;
            }
        }

        /// <summary>Asks for one value from a list; a single option is taken without asking when allowed.</summary>
        private static async Task<string> ChooseValueAsync(object owner, string strTitle, List<ListItem> lstItems, bool blnAutoSelectSingle)
        {
            if (lstItems.Count == 0)
                return null;
            if (lstItems.Count == 1 && blnAutoSelectSingle)
                return lstItems[0].Value?.ToString();
            ListItem? objChoice = await PromptWindow.ChooseAsync(owner, strTitle, lstItems, null).ConfigureAwait(true);
            return objChoice?.Value?.ToString();
        }

        private static async Task<string> BrowseIdAsync(object owner, DataBrowser objBrowser)
        {
            ListItem? objChoice = await PromptWindow.BrowseAsync(owner, string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("Title_SelectGeneric"), objBrowser.LocalName),
                                                                 objBrowser.Options, x => objBrowser.Details(x.Value?.ToString()),
                                                                 x => objBrowser.Validate(x.Value?.ToString())).ConfigureAwait(true);
            return objChoice?.Value?.ToString();
        }

        private static async Task<DialogOutcome> ChooseSkillAsync(SelectSkillRequest objRequest, object owner)
        {
            Character objCharacter = objRequest.ConstructorArguments.FirstOrDefault() as Character;
            string strSource = objRequest.ConstructorArguments.Count > 1 ? objRequest.ConstructorArguments[1] as string ?? string.Empty : string.Empty;
            if (objCharacter == null)
                return DialogOutcome.Cancel;
            SkillSelectionFilter objFilter = new SkillSelectionFilter
            {
                IncludeCategory = objRequest.OnlyCategory ?? string.Empty,
                ExcludeCategory = objRequest.ExcludeCategory ?? string.Empty,
                IncludeSkillGroup = objRequest.OnlySkillGroup ?? string.Empty,
                ExcludeSkillGroup = objRequest.ExcludeSkillGroup ?? string.Empty,
                LimitToSkill = objRequest.LimitToSkill ?? string.Empty,
                ExcludeSkill = objRequest.ExcludeSkill ?? string.Empty,
                LinkedAttribute = objRequest.LinkedAttribute ?? string.Empty,
                MinimumRating = objRequest.MinimumRating,
                MaximumRating = objRequest.MaximumRating == 0 ? int.MaxValue : objRequest.MaximumRating,
            };
            objFilter.SetLimitToCategories(objRequest.LimitToCategories);
            List<ListItem> lstSkills = SkillSelector.GetSkillOptions(objCharacter, objFilter, out bool _, out string _);
            if (lstSkills.Count == 0)
            {
                await MessageBoxes.ShowAsync(string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("Message_Improvement_EmptySelectionListNamed"), strSource),
                                             "Chummer Native", MessageButtons.OK, MessageIcon.Information).ConfigureAwait(true);
                return DialogOutcome.Cancel;
            }
            ListItem objSkill;
            if (lstSkills.Count == 1)
                objSkill = lstSkills[0];
            else
            {
                ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objRequest.Description ?? Tr.Get("Title_SelectSkill"), lstSkills, null).ConfigureAwait(true);
                if (objChoice == null)
                    return DialogOutcome.Cancel;
                objSkill = objChoice.Value;
            }
            (string strName, bool blnExotic) = (ValueTuple<string, bool>)objSkill.Value;
            if (!blnExotic)
            {
                objRequest.SetResult(nameof(SelectSkillRequest.SelectedSkill), strName);
                return DialogOutcome.OK;
            }
            // Exotic skills need a specific (which weapon, which vehicle...).
            List<ListItem> lstSpecifics = SkillSelector.GetExoticSpecifics(objCharacter, strName, objFilter);
            string strSpecific;
            if (lstSpecifics.Count == 1)
                strSpecific = lstSpecifics[0].Value?.ToString();
            else if (lstSpecifics.Count > 1)
            {
                ListItem? objChoice = await PromptWindow.ChooseAsync(owner, objSkill.Name, lstSpecifics, null).ConfigureAwait(true);
                strSpecific = objChoice?.Value?.ToString();
            }
            else if (objFilter.MinimumRating <= 0)
                strSpecific = await PromptWindow.AskTextAsync(owner, objSkill.Name, string.Empty).ConfigureAwait(true);
            else
            {
                await MessageBoxes.ShowAsync(string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("Message_Improvement_EmptySelectionListNamed"), strSource),
                                             "Chummer Native", MessageButtons.OK, MessageIcon.Information).ConfigureAwait(true);
                return DialogOutcome.Cancel;
            }
            if (string.IsNullOrEmpty(strSpecific))
                return DialogOutcome.Cancel;
            objRequest.SetResult(nameof(SelectSkillRequest.SelectedSkill), strName + " (" + strSpecific + ")");
            return DialogOutcome.OK;
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
