/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chummer.Rules;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        [RelayCommand] private Task ReapplyAllImprovements() => ReapplyImprovementSourcesAsync();
        [RelayCommand] private Task ReapplyInventoryImprovements()
            => SelectedInventoryItem?.Item is IHasInternalId item ? ReapplyImprovementSourcesAsync(new[] { item.InternalId }) : Task.CompletedTask;

        internal async Task ReapplyImprovementSourcesAsync(IReadOnlyCollection<string> ids = null)
        {
            ImprovementReapplyResult result = null;
            try
            {
                await FlushInfoEditsAsync();
                await RunRuleAsync(async token =>
                {
                    result = await ImprovementReapplyRules.ApplyAsync(Character, ids, token);
                    return result.Outcome;
                });
            }
            catch (Exception ex)
            {
                SelectedContactProfile = null;
                Reload();
                await MessageBoxes.ShowAsync(ex.Message, "Could not refresh source bonuses", MessageButtons.OK, MessageIcon.Error);
                return;
            }
            finally { if (result?.Restored == true) { SelectedContactProfile = null; Reload(); } }
            if (!string.IsNullOrWhiteSpace(result?.OutdatedItems))
                await MessageBoxes.ShowAsync("These source definitions are unavailable. Their recorded effects were preserved:\n\n" + result.OutdatedItems,
                    "Unavailable source definitions", MessageButtons.OK, MessageIcon.Information);
        }

        internal async Task CheckPendingImprovementRepairsAsync()
        {
            var pending = await Character.TakeInternalIdsNeedingReapplyImprovementsAsync();
            if (pending == null || pending.Count == 0) return;
            if (await MessageBoxes.ShowAsync("This older character needs some item bonuses refreshed from the installed game data. Refresh them now?",
                "Character compatibility repair", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return;
            await ReapplyImprovementSourcesAsync(pending.Distinct(StringComparer.Ordinal).ToArray());
        }
    }
}
