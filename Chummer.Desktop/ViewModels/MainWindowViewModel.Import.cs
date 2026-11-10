using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Services;
using Chummer.Desktop.Views;
using Chummer.Selection;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private async Task ImportHeroLab()
        {
            var owner = ModalHelper.MainWindow;
            if (owner == null) return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import HeroLab portfolio", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("HeroLab portfolio") { Patterns = new[] { "*.por" } } }
            });
            string path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            IsBusy = true;
            Character character = null;
            bool opened = false;
            try
            {
                Status = "Reading HeroLab portfolio…";
                var entries = await HeroLabPortfolio.ReadAsync(path);
                if (entries.Count == 0) { Status = "No character statblocks were found in this portfolio."; return; }
                var selected = await PromptWindow.ChooseAsync(null, "Import which runner?", entries.Select(x => new ListItem(x.CharacterId, x.Description)).ToList(), null);
                if (selected == null) return;
                var settings = await SettingsManager.GetLoadedCharacterSettingsAsync();
                var preset = await PromptWindow.ChooseAsync(null, "Imported runner's rules preset", settings.Select(x => new ListItem(x.Key, x.Value.Name)).OrderBy(x => x.Name).ToList(),
                    GlobalSettings.DefaultCharacterSetting);
                if (preset == null) return;
                character = new Character();
                Status = "Importing runner…";
                if (!await Task.Run(() => character.LoadFromHeroLabFileAsync(path, selected.Value.Value.ToString(), preset.Value.Value.ToString())))
                { Status = "The engine could not import this character."; return; }
                await character.SetFileNameAsync(string.Empty);
                await CharacterRegistry.OpenCharacters.AddAsync(character);
                await OpenCharacterAsync(character);
                opened = true;
                ((CharacterViewModel)CurrentPage).MarkDirty();
                Status = "Runner imported. Save it as a Chummer character.";
            }
            catch (Exception ex) { Status = "HeroLab import failed: " + ex.Message; }
            finally
            {
                IsBusy = false;
                if (character != null && !opened)
                {
                    await CharacterRegistry.OpenCharacters.RemoveAsync(character);
                    await character.DisposeAsync();
                }
            }
        }
    }
}
