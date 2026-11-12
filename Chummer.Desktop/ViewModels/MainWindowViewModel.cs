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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Localization;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Shell: navigation rail (roster, dice terminal), open character tabs, status line.
    /// </summary>
    public sealed partial class MainWindowViewModel : ViewModelBase
    {
        public static MainWindowViewModel Current { get; private set; }

        public MainWindowViewModel()
        {
            Current = this;
            Roster = new RosterViewModel(this);
            Dice = new DiceTerminalViewModel(this);
            Palette = new PaletteViewModel(this);
            _currentPage = Roster;
        }

        public RosterViewModel Roster { get; }

        public DiceTerminalViewModel Dice { get; }

        public PaletteViewModel Palette { get; }

        public ObservableCollection<CharacterViewModel> OpenCharacters { get; } = new ObservableCollection<CharacterViewModel>();

        [ObservableProperty]
        private ViewModelBase _currentPage;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        public bool IsRosterActive => ReferenceEquals(CurrentPage, Roster);

        public bool IsDiceActive => ReferenceEquals(CurrentPage, Dice);

        partial void OnCurrentPageChanged(ViewModelBase value)
        {
            OnPropertyChanged(nameof(IsRosterActive));
            OnPropertyChanged(nameof(IsDiceActive));
        }

        public string Title => "CHUMMER // NATIVE";

        public void Start()
        {
            Status = Tr.Get("String_Loading");
            _ = Roster.RefreshAsync().ContinueWith(_ => OnUi(() => Status = string.Empty), TaskScheduler.Default);
        }

        [RelayCommand]
        private void ShowRoster() => CurrentPage = Roster;

        [RelayCommand]
        private void ShowDice()
        {
            Dice.Activate();
            CurrentPage = Dice;
        }

        [RelayCommand]
        private void ShowCharacter(CharacterViewModel objCharacter)
        {
            if (objCharacter != null)
                CurrentPage = objCharacter;
        }

        [RelayCommand]
        private async Task CloseCharacter(CharacterViewModel objCharacter)
        {
            if (objCharacter == null)
                return;
            if (!await objCharacter.ConfirmCloseAsync())
                return;
            OpenCharacters.Remove(objCharacter);
            if (ReferenceEquals(CurrentPage, objCharacter))
                CurrentPage = (ViewModelBase)OpenCharacters.LastOrDefault() ?? Roster;
            await objCharacter.DisposeCharacterAsync();
        }

        [RelayCommand]
        private async Task OpenFile()
        {
            Window objMain = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (objMain == null)
                return;
            System.Collections.Generic.IReadOnlyList<IStorageFile> lstFiles = await objMain.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Tr.Get("Label_Open"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Chummer") { Patterns = new[] { "*.chum5", "*.chum5lz" } },
                }
            });
            IStorageFile objFile = lstFiles.FirstOrDefault();
            string strPath = objFile?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(strPath))
                await OpenCharacterFileAsync(strPath);
        }

        /// <summary>
        /// Loads (or switches to) a character file and shows it in its own tab.
        /// </summary>
        public async Task OpenCharacterFileAsync(string strPath)
        {
            CharacterViewModel objExisting = OpenCharacters.FirstOrDefault(x => string.Equals(x.FilePath, strPath, Utils.PathStringComparison));
            if (objExisting != null)
            {
                CurrentPage = objExisting;
                return;
            }
            IsBusy = true;
            Status = Tr.Get("String_Loading") + " " + Path.GetFileName(strPath);
            try
            {
                Character objCharacter = await HostServices.Shell.LoadCharacterAsync(strPath);
                if (objCharacter == null)
                    return;
                await OpenCharacterAsync(objCharacter);
                Dice.Activate();
                await GlobalSettings.MostRecentlyUsedCharacters.InsertAsync(0, strPath);
            }
            finally
            {
                IsBusy = false;
                Status = string.Empty;
            }
        }

        public async Task OpenCharacterAsync(Character objCharacter)
        {
            CharacterViewModel objViewModel = OpenCharacters.FirstOrDefault(x => ReferenceEquals(x.Character, objCharacter));
            bool opened = objViewModel == null;
            if (objViewModel == null)
            {
                objViewModel = await CharacterViewModel.CreateAsync(objCharacter);
                OpenCharacters.Add(objViewModel);
            }
            CurrentPage = objViewModel;
            if (opened) await objViewModel.CheckPendingImprovementRepairsAsync();
        }
    }
}
