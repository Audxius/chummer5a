/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>The Edge pool during play: spend, regain, refresh (legacy career Condition Monitor tab).</summary>
    public sealed partial class CharacterViewModel
    {
        [ObservableProperty] private int _edgeTotal;
        [ObservableProperty] private int _edgeUsed;
        [ObservableProperty] private IReadOnlyList<bool> _edgePips = Array.Empty<bool>();

        public int EdgeRemainingValue => Math.Max(0, EdgeTotal - EdgeUsed);
        public string EdgePoolText => EdgeRemainingValue.ToString(GlobalSettings.CultureInfo) + " / " + EdgeTotal.ToString(GlobalSettings.CultureInfo);
        public bool CanSpendEdge => EdgeRemainingValue > 0;
        public bool CanRegainEdge => EdgeUsed > 0;
        public bool IsCareerMode => _careerMode;

        partial void OnEdgeTotalChanged(int value) => NotifyEdge();
        partial void OnEdgeUsedChanged(int value) => NotifyEdge();

        private void NotifyEdge()
        {
            OnPropertyChanged(nameof(EdgeRemainingValue));
            OnPropertyChanged(nameof(EdgePoolText));
            OnPropertyChanged(nameof(CanSpendEdge));
            OnPropertyChanged(nameof(CanRegainEdge));
        }

        /// <summary>Called from RefreshSummary (background thread).</summary>
        private void RefreshEdge()
        {
            int intTotal = Character.GetAttribute("EDG")?.TotalValue ?? 0;
            int intUsed = Character.EdgeUsed;
            List<bool> lstPips = Enumerable.Range(0, Math.Max(0, intTotal)).Select(x => x < intTotal - intUsed).ToList();
            OnUi(() =>
            {
                EdgeTotal = intTotal;
                EdgeUsed = intUsed;
                EdgePips = lstPips;
            });
        }

        [RelayCommand]
        private Task SpendEdge() => RunEdgeAsync(EdgeRules.SpendAsync);

        [RelayCommand]
        private Task RegainEdge() => RunEdgeAsync(EdgeRules.RegainAsync);

        [RelayCommand]
        private Task RefreshEdgePool() => RunEdgeAsync(EdgeRules.RefreshAsync);

        private Task RunEdgeAsync(Func<Character, System.Threading.CancellationToken, Task<RuleResult>> funcRule)
            => RunRuleAsync(t => funcRule(Character, t));

        /// <summary>Spends one Edge point without prompting (used by the Dice Terminal when an Edge roll is copied). Returns false if none left.</summary>
        internal async Task<bool> TrySpendEdgeQuietlyAsync()
        {
            bool blnSpent = false;
            await RunRuleAsync(async token =>
            {
                int intTotal = (await Character.GetAttributeAsync("EDG", token: token).ConfigureAwait(false)).TotalValue;
                if (await Character.GetEdgeUsedAsync(token).ConfigureAwait(false) >= intTotal)
                    return RuleResult.NotAllowed;
                await Character.ModifyEdgeUsedAsync(1, token).ConfigureAwait(false);
                blnSpent = true;
                return RuleResult.Changed;
            }).ConfigureAwait(true);
            return blnSpent;
        }
    }
}
