using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer;

/// <summary>
/// Unattended answers for screenshot runs: settings prompts pick the "Standard" profile, anything else is cancelled.
/// </summary>
internal sealed class AutoDialogService : IDialogService
{
    public DialogOutcome Show(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        => ShowAsync(objRequest, owner, token).GetAwaiter().GetResult();

    public async Task<DialogOutcome> ShowAsync(DialogRequest objRequest, object owner = null, CancellationToken token = default)
    {
        Console.WriteLine("[dialog] " + objRequest.DialogName);
        if (Environment.GetEnvironmentVariable("CHUMMER_REVIEW_PACKS") == "1" && objRequest is SelectWeaponCategoryRequest)
        {
            objRequest.SetResult(nameof(SelectWeaponCategoryRequest.SelectedCategory), "Light Pistols");
            return DialogOutcome.OK;
        }
        if (Environment.GetEnvironmentVariable("CHUMMER_REVIEW_PACKS") == "1" && objRequest is SelectTextRequest)
        {
            objRequest.SetResult(nameof(SelectTextRequest.SelectedValue), "Seattle");
            return DialogOutcome.OK;
        }
        if (objRequest.Opacity == 0)
            return DialogOutcome.OK;
        if (objRequest is SelectBuildMethodRequest && objRequest.ConstructorArguments.FirstOrDefault() is Character objCharacter)
        {
            IReadOnlyDictionary<string, CharacterSettings> dicSettings = await SettingsManager.GetLoadedCharacterSettingsAsync(token);
            CharacterSettings objStandard = dicSettings.Values.FirstOrDefault(x => x.Name == "Standard") ?? dicSettings.Values.First();
            bool blnExisting = objRequest.ConstructorArguments.Count > 1 && objRequest.ConstructorArguments[1] is bool b && b;
            CharacterSettings objCurrent = await objCharacter.GetSettingsAsync(token);
            return await CharacterSettingsApplier.ApplyAsync(objCharacter, objStandard, false, blnExisting, objCurrent.BuildMethod, owner, token)
                ? DialogOutcome.OK
                : DialogOutcome.Cancel;
        }
        return DialogOutcome.Cancel;
    }
}
