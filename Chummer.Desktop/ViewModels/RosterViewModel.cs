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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Localization;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Character roster: favorites, recently opened files and the watch folder, shown as cards.
    /// </summary>
    public sealed partial class RosterViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _objMain;
        private List<RosterEntryViewModel> _lstAll = new List<RosterEntryViewModel>();

        public RosterViewModel(MainWindowViewModel objMain)
        {
            _objMain = objMain;
        }

        public ObservableCollection<RosterEntryViewModel> Entries { get; } = new ObservableCollection<RosterEntryViewModel>();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isEmpty;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        public async Task RefreshAsync()
        {
            List<(string Path, RosterSource Source)> lstFiles = new List<(string, RosterSource)>();
            HashSet<string> setSeen = new HashSet<string>(Utils.PathStringComparer);
            foreach (string strFile in GlobalSettings.FavoriteCharacters.ToList())
            {
                if (setSeen.Add(strFile))
                    lstFiles.Add((strFile, RosterSource.Favorite));
            }
            foreach (string strFile in GlobalSettings.MostRecentlyUsedCharacters.ToList())
            {
                if (setSeen.Add(strFile))
                    lstFiles.Add((strFile, RosterSource.Recent));
            }
            string strWatchFolder = GlobalSettings.CharacterRosterPath;
            if (!string.IsNullOrEmpty(strWatchFolder) && Directory.Exists(strWatchFolder))
            {
                foreach (string strFile in Directory.EnumerateFiles(strWatchFolder, "*.chum5*", SearchOption.AllDirectories))
                {
                    if (setSeen.Add(strFile))
                        lstFiles.Add((strFile, RosterSource.WatchFolder));
                }
            }

            List<RosterEntryViewModel> lstEntries = new List<RosterEntryViewModel>(lstFiles.Count);
            foreach ((string strPath, RosterSource eSource) in lstFiles)
            {
                if (!File.Exists(strPath))
                    continue;
                CharacterCache objCache = new CharacterCache();
                try
                {
                    await objCache.LoadFromFileAsync(strPath).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    continue;
                }
                lstEntries.Add(new RosterEntryViewModel(_objMain, objCache, strPath, eSource));
            }

            OnUi(() =>
            {
                _lstAll = lstEntries;
                ApplyFilter();
            });
        }

        private void ApplyFilter()
        {
            string strFilter = SearchText?.Trim() ?? string.Empty;
            Entries.Clear();
            foreach (RosterEntryViewModel objEntry in _lstAll)
            {
                if (strFilter.Length == 0 || objEntry.SearchText.IndexOf(strFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    Entries.Add(objEntry);
            }
            IsEmpty = Entries.Count == 0;
        }

        [RelayCommand]
        private Task OpenFile() => _objMain.OpenFileCommand.ExecuteAsync(null);
    }

    public enum RosterSource
    {
        Favorite,
        Recent,
        WatchFolder
    }

    public sealed partial class RosterEntryViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _objMain;

        public RosterEntryViewModel(MainWindowViewModel objMain, CharacterCache objCache, string strPath, RosterSource eSource)
        {
            _objMain = objMain;
            FilePath = strPath;
            Source = eSource;
            Name = !string.IsNullOrWhiteSpace(objCache.CharacterAlias)
                ? objCache.CharacterAlias
                : !string.IsNullOrWhiteSpace(objCache.CharacterName) ? objCache.CharacterName : Path.GetFileNameWithoutExtension(strPath);
            RealName = objCache.CharacterName ?? string.Empty;
            Metatype = string.IsNullOrEmpty(objCache.Metavariant) || objCache.Metavariant == "None"
                ? objCache.Metatype ?? string.Empty
                : objCache.Metatype + " (" + objCache.Metavariant + ")";
            Mode = Tr.Get(objCache.Created ? "Title_CareerMode" : "Title_CreateMode").ToUpperInvariant();
            Karma = objCache.Karma ?? string.Empty;
            PlayerName = objCache.PlayerName ?? string.Empty;
            Error = objCache.ErrorText ?? string.Empty;
            MugshotImage objMugshot = objCache.Mugshot;
            if (objMugshot != null)
            {
                try
                {
                    using (MemoryStream objStream = new MemoryStream(objMugshot.EncodedBytes))
                        Portrait = Bitmap.DecodeToWidth(objStream, 192);
                }
                catch (Exception)
                {
                    Portrait = null; // unreadable picture: show the placeholder
                }
            }
            SearchText = string.Join(" ", Name, RealName, Metatype, PlayerName, Path.GetFileName(strPath));
        }

        public string FilePath { get; }
        public RosterSource Source { get; }
        public string Name { get; }
        public string RealName { get; }
        public string Metatype { get; }
        public string Mode { get; }
        public string Karma { get; }
        public bool HasKarma => !string.IsNullOrEmpty(Karma);
        public string PlayerName { get; }
        public string Error { get; }
        public Bitmap Portrait { get; }
        public bool HasPortrait => Portrait != null;
        public bool IsFavorite => Source == RosterSource.Favorite;
        public string SearchText { get; }

        [RelayCommand]
        private Task Open() => _objMain.OpenCharacterFileAsync(FilePath);
    }
}
