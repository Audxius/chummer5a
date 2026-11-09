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
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Rolls;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>One hit in the global search palette.</summary>
    public sealed class PaletteEntry
    {
        public PaletteEntry(string strTitle, string strSubtitle, string strIconKey, Action funcActivate)
        {
            Title = strTitle ?? string.Empty;
            Subtitle = strSubtitle ?? string.Empty;
            IconKey = strIconKey;
            Activate = funcActivate;
        }

        public string Title { get; }
        public string Subtitle { get; }
        public string IconKey { get; }
        public Action Activate { get; }
    }

    /// <summary>
    /// Ctrl+K: one search box over every open runner (skills, items, abilities, contacts, qualities), every roll in the
    /// Dice Terminal, and the app's own pages.
    /// </summary>
    public sealed partial class PaletteViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _objShell;
        private List<PaletteEntry> _lstEntries = new List<PaletteEntry>();

        public PaletteViewModel(MainWindowViewModel objShell)
        {
            _objShell = objShell;
        }

        public ObservableCollection<PaletteEntry> Results { get; } = new ObservableCollection<PaletteEntry>();

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private string _query = string.Empty;
        [ObservableProperty] private PaletteEntry _selected;

        partial void OnQueryChanged(string value) => Filter();

        [RelayCommand]
        private void Open()
        {
            _lstEntries = BuildEntries();
            Query = string.Empty;
            Filter();
            IsOpen = true;
        }

        [RelayCommand]
        private void Close() => IsOpen = false;

        [RelayCommand]
        private void Activate(PaletteEntry objEntry)
        {
            objEntry = objEntry ?? Selected ?? Results.FirstOrDefault();
            if (objEntry == null)
                return;
            IsOpen = false;
            objEntry.Activate?.Invoke();
        }

        public void Move(int intDelta)
        {
            if (Results.Count == 0)
                return;
            int intIndex = Selected == null ? -1 : Results.IndexOf(Selected);
            intIndex = Math.Max(0, Math.Min(Results.Count - 1, intIndex + intDelta));
            Selected = Results[intIndex];
        }

        private List<PaletteEntry> BuildEntries()
        {
            List<PaletteEntry> lstReturn = new List<PaletteEntry>
            {
                new PaletteEntry("Character roster", "page", "Icon.Roster", () => _objShell.ShowRosterCommand.Execute(null)),
                new PaletteEntry("Dice Terminal", "page  //  Ctrl+R", "Icon.Dice", () => _objShell.ShowDiceCommand.Execute(null)),
                new PaletteEntry("Open character file…", "file  //  Ctrl+O", "Icon.Open", () => _objShell.OpenFileCommand.Execute(null)),
            };
            foreach (CharacterViewModel objCharacter in _objShell.OpenCharacters)
            {
                CharacterViewModel objLoop = objCharacter;
                lstReturn.Add(new PaletteEntry(objLoop.Name, "runner", "Icon.Overview", () => _objShell.CurrentPage = objLoop));
                lstReturn.AddRange(objLoop.PaletteEntries());
            }
            RollCatalog objCatalog = _objShell.Dice.Catalog;
            if (objCatalog != null)
            {
                foreach (RollAction objAction in objCatalog.Actions.Where(x => x.IsRollable))
                {
                    RollAction objLoop = objAction;
                    lstReturn.Add(new PaletteEntry(objLoop.DisplayName, "roll  //  " + (objLoop.IsHouseRule ? objLoop.Book : (objLoop.Source + " " + objLoop.Page).Trim()),
                                                   "Icon.Dice", () => _objShell.Dice.ShowRoll(objLoop)));
                }
            }
            return lstReturn;
        }

        private void Filter()
        {
            string[] astrWords = (Query ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<PaletteEntry> lstHits = astrWords.Length == 0
                ? _lstEntries.Take(12)
                : _lstEntries.Select(x => new { Entry = x, Score = Score(x, astrWords) })
                             .Where(x => x.Score > 0)
                             .OrderByDescending(x => x.Score)
                             .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                             .Take(40)
                             .Select(x => x.Entry);
            Results.Clear();
            foreach (PaletteEntry objEntry in lstHits)
                Results.Add(objEntry);
            Selected = Results.FirstOrDefault();
        }

        private static int Score(PaletteEntry objEntry, string[] astrWords)
        {
            int intScore = 0;
            foreach (string strWord in astrWords)
            {
                if (objEntry.Title.StartsWith(strWord, StringComparison.CurrentCultureIgnoreCase))
                    intScore += 100;
                else if (objEntry.Title.IndexOf(" " + strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intScore += 70;
                else if (objEntry.Title.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intScore += 50;
                else if (objEntry.Subtitle.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intScore += 20;
                else
                    return 0;
            }
            return intScore;
        }
    }
}
