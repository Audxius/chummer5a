using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Enums;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Selection;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private async Task NewRunner()
        {
            var presets = await SettingsManager.GetLoadedCharacterSettingsAsync();
            var choice = await PromptWindow.ChooseAsync(null, "New runner // rules preset", presets
                .OrderBy(x => x.Value.Name).Select(x => new ListItem(x.Key, x.Value.Name)).ToList(), GlobalSettings.DefaultCharacterSetting);
            if (choice == null) return;
            var character = new Character { SettingsKey = choice.Value.Value.ToString() };
            bool opened = false;
            try
            {
                character.Karma = character.Settings.BuildKarma;
                var selector = await Task.Run(() => new MetatypeSelector(character));
                selector.ExoticSpecificPicker = name => ChooseOrTypeWindow.ShowAsync(name, SkillRules.GetExoticSpecificOptions(character, name), true);
                var window = new MetatypeWindow(selector);
                await DialogHost.ShowModalAsync(window, null);
                if (!window.Accepted) return;
                await CharacterRegistry.OpenCharacters.AddAsync(character);
                await OpenCharacterAsync(character);
                opened = true;
                ((CharacterViewModel)CurrentPage).MarkDirty();
                if (character.EffectiveBuildMethod == CharacterBuildMethod.LifeModule)
                    ((CharacterViewModel)CurrentPage).ShowSectionCommand.Execute(CharacterSection.Qualities);
            }
            finally
            {
                if (!opened)
                {
                    await CharacterRegistry.OpenCharacters.RemoveAsync(character);
                    await character.DisposeAsync();
                }
            }
        }
    }
}
