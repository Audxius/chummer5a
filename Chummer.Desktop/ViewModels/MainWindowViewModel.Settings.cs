using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private Task GlobalOptions() => DialogHost.ShowModalAsync(new GlobalOptionsWindow(), null);

        [RelayCommand]
        private async Task EditCharacterSettings()
        {
            CharacterViewModel runner = CurrentPage as CharacterViewModel;
            CharacterSettings source = runner?.Character.Settings;
            if (source == null)
            {
                var presets = await SettingsManager.GetLoadedCharacterSettingsAsync();
                var choice = await PromptWindow.ChooseAsync(null, "Character rules preset", presets.Select(x => new ListItem(x.Key, x.Value.Name)).ToList(), null);
                if (choice == null || !presets.TryGetValue(choice.Value.Value.ToString(), out source)) return;
            }
            using var editor = await Task.Run(() => CharacterSettingsEditorViewModel.CreateAsync(source));
            var window = new CharacterSettingsWindow(editor, async name =>
            {
                editor.Settings.Name = name;
                string file = "native-" + Guid.NewGuid().ToString("N") + ".xml";
                if (!await Task.Run(() => editor.Settings.Save(file, true))) return false;
                var preset = await Task.Run(() =>
                {
                    var copy = new CharacterSettings();
                    copy.CopyValues(editor.Settings);
                    return copy;
                });
                SettingsManager.LoadedCharacterSettingsAsModifiable[file] = preset;
                if (runner != null)
                {
                    await Task.Run(() => runner.Character.SetSettingsKeyAsync(file));
                    runner.MarkDirty(); runner.Reload();
                }
                return true;
            });
            await DialogHost.ShowModalAsync(window, null);
        }
    }
}
