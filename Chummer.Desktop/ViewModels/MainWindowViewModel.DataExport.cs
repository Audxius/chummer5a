using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Services;
using Chummer.Desktop.Views;
using Chummer.Printing;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private async Task ExportGameData()
        {
            var presets = await SettingsManager.GetLoadedCharacterSettingsAsync();
            var choice = await PromptWindow.ChooseAsync(null, "Export game data // rules preset",
                presets.Select(x => new ListItem(x.Key, x.Value.Name)).OrderBy(x => x.Name).ToList(), (CurrentPage as CharacterViewModel)?.Character.SettingsKey);
            if (choice == null || !presets.TryGetValue(choice.Value.Value.ToString(), out var settings)) return;
            var language = await PromptWindow.ChooseAsync(null, "Export language", CharacterExporter.Languages().ToList(), GlobalSettings.Language);
            if (language == null) return;
            var owner = ModalHelper.MainWindow;
            if (owner == null) return;
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export merged game data", SuggestedFileName = "Chummer Data.zip", DefaultExtension = "zip",
                FileTypeChoices = new[] { new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } } }
            });
            if (file == null) return;
            IsBusy = true;
            try
            {
                var progress = new Progress<int>(completed => Status = "Exporting data: " + completed + " / " + (Utils.BasicDataFileNames.Count + 1));
                using var archive = new MemoryStream();
                await Task.Run(() => DataArchiveExporter.ExportAsync(settings, archive, language.Value.Value.ToString(), progress));
                archive.Position = 0;
                using var output = await file.OpenWriteAsync(); output.SetLength(0);
                await archive.CopyToAsync(output); Status = "Game data exported.";
            }
            catch (Exception ex) { Status = "Data export failed: " + ex.Message; }
            finally { IsBusy = false; }
        }
    }
}
