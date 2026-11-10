using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Rules;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Desktop.Views;
using Chummer.Selection;
using Chummer.Backend.Enums;
using System.Collections.ObjectModel;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        [ObservableProperty] private string _creationBudget = string.Empty;
        [ObservableProperty] private string _creationErrors = string.Empty;
        [ObservableProperty] private bool _isCheckingCreation;
        [ObservableProperty] private bool _isMutatingModel;
        public bool CanInteract => !IsCheckingCreation && !IsMutatingModel;
        partial void OnIsCheckingCreationChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
        partial void OnIsMutatingModelChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
        private int _creationKarma;
        private bool _careerMode;
        private bool _lifeModuleMode;
        public bool IsCreateMode => !_careerMode;
        public bool IsLifeModuleBuild => _lifeModuleMode;
        public ObservableCollection<QualityViewModel> LifeModuleQualities { get; } = new ObservableCollection<QualityViewModel>();
        public bool HasCreationErrors => !string.IsNullOrEmpty(CreationErrors);
        partial void OnCreationErrorsChanged(string value) => OnPropertyChanged(nameof(HasCreationErrors));

        [RelayCommand]
        private async Task AddPacksKit()
        {
            if (Character.Created) return;
            while (true)
            {
                var kits = await Task.Run(() => PacksCatalog.LoadAsync(Character));
                var picker = new PacksWindow(kits);
                await DialogHost.ShowModalAsync(picker, null);
                if (picker.Selected == null) return;
                IsCheckingCreation = true;
                var progress = new Progress<string>(message => CreationErrors = message);
                try { await RunRuleAsync(t => PacksRules.AddAsync(Character, picker.Selected.Id, picker.Selected.Category, t, progress)); }
                finally
                {
                    // A kit can grant several independent objects before a later prompt
                    // is cancelled. Preserve the dirty state even when it stops partway.
                    MarkDirty(); Reload(); IsCheckingCreation = false; CreationErrors = "";
                }
                if (!picker.AddAgain) return;
            }
        }

        [RelayCommand]
        private async Task AddLifeModule()
        {
            if (!IsLifeModuleBuild) return;
            while (true)
            {
                var picker = new LifeModuleWindow(Character);
                await DialogHost.ShowModalAsync(picker, null);
                if (picker.Selected == null) return;
                await RunRuleAsync(t => LifeModuleRules.AddAsync(Character, picker.Selected.Id, picker.EnforceRequirements, t));
                if (!picker.AddAgain) return;
            }
        }

        [RelayCommand]
        private async Task BuyCreationNuyen()
        {
            if (Character.Created) return;
            decimal? amount = await PromptWindow.AskNumberAsync(null, "Karma spent on starting nuyen", 0, Character.TotalNuyenMaximumBP, true);
            if (amount == null) return;
            await RunRuleAsync(async t =>
            {
                await Character.SetNuyenBPAsync(amount.Value, t);
                return RuleResult.Changed;
            });
        }

        [RelayCommand]
        private async Task ChangeCreationPriorities()
        {
            if (Character.Created) return;
            var selector = await Task.Run(() => new MetatypeSelector(Character));
            selector.ExoticSpecificPicker = name => ChooseOrTypeWindow.ShowAsync(name, SkillRules.GetExoticSpecificOptions(Character, name), true);
            var window = new MetatypeWindow(selector, true);
            await DialogHost.ShowModalAsync(window, null);
            if (window.Accepted) { MarkDirty(); Reload(); }
        }

        private async Task<(string Karma, string Nuyen)> ReadCreationBudget()
        {
            _creationKarma = await CreationRules.CalculateKarmaAsync(Character).ConfigureAwait(false);
            var funds = Character.CalculateNuyenCreateMode();
            int attributePoints = Character.TotalAttributes - Character.AttributeSection.AttributeList.Sum(x => x.SpentPriorityPoints);
            int specialPoints = Character.TotalSpecial - Character.AttributeSection.SpecialAttributeList.Sum(x => x.SpentPriorityPoints);
            var skills = Character.SkillsSection;
            string budget = (Character.EffectiveBuildMethodUsesPriorityTables
                ? "ATTRIBUTES " + attributePoints + "   SPECIAL " + specialPoints + "   SKILLS " + skills.SkillPoints + "   GROUPS " + skills.SkillGroupPoints + "   " : "")
                + "KNOWLEDGE " + skills.KnowledgeSkillPointsRemain + "   KARMA " + _creationKarma;
            OnUi(() => CreationBudget = budget);
            return (_creationKarma.ToString(GlobalSettings.CultureInfo), funds.Item1.ToString(Character.Settings.NuyenFormat, GlobalSettings.CultureInfo) + "¥");
        }

        [RelayCommand]
        private async Task ValidateCreation()
        {
            IsCheckingCreation = true;
            try
            {
                var result = await Task.Run(() => CreationRules.ValidateAsync(Character));
                CreationErrors = result.IsValid ? "Build checks passed." : result.Errors;
            }
            finally { IsCheckingCreation = false; }
        }

        [RelayCommand]
        private async Task FinishCreation()
        {
            if (Character.Created) return;
            if (string.IsNullOrEmpty(Character.FileName))
            {
                await SaveAs();
                if (string.IsNullOrEmpty(Character.FileName)) return;
            }
            IsCheckingCreation = true;
            try
            {
                var result = await Task.Run(() => CreationRules.FinishCareerAsync(Character, StartingNuyenWindow.ShowAsync));
                if (result == RuleResult.Changed)
                {
                    MarkDirty();
                    await Save();
                    Reload();
                }
                else if (result == RuleResult.NotAllowed)
                {
                    var validation = await Task.Run(() => CreationRules.ValidateAsync(Character));
                    CreationErrors = validation.Errors;
                }
            }
            finally
            {
                IsCheckingCreation = false;
                OnPropertyChanged(nameof(IsCreateMode));
                OnPropertyChanged(nameof(IsCareerMode));
            }
        }
    }
}
