/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// The editable "Character Info" fields of legacy Chummer (identity, looks, concept/background/description, notes, GM notes).
    /// Edits are written straight to the character (the legacy text boxes were bound the same way).
    /// Long texts are stored as plain text; legacy Chummer reads plain text as well as RTF.
    /// </summary>
    public sealed partial class CharacterInfoViewModel : ObservableObject
    {
        private readonly CharacterViewModel _objOwner;
        private readonly Character _objCharacter;
        private bool _blnLoading;
        private Task _pendingWrites = Task.CompletedTask;
        private NotebookViewModel _notebook;
        private bool _notebookWrite;
        public NotebookViewModel Notebook => _notebook ??= new NotebookViewModel(this);
        internal Task FlushAsync() { _notebook?.CommitPending(); return _pendingWrites; }
        internal void MarkNotebookDirty() => _objOwner.MarkDirty();
        internal void SetNotebookNotes(string value)
        {
            _notebookWrite = true;
            try { Notes = value; }
            finally { _notebookWrite = false; }
        }

        public CharacterInfoViewModel(CharacterViewModel objOwner)
        {
            _objOwner = objOwner;
            _objCharacter = objOwner.Character;
            _blnLoading = true;
            try
            {
                CharacterName = _objCharacter.Name ?? string.Empty;
                Alias = _objCharacter.Alias ?? string.Empty;
                PlayerName = _objCharacter.PlayerName ?? string.Empty;
                Gender = _objCharacter.Gender ?? string.Empty;
                Age = _objCharacter.Age ?? string.Empty;
                Height = _objCharacter.Height ?? string.Empty;
                Weight = _objCharacter.Weight ?? string.Empty;
                Eyes = _objCharacter.Eyes ?? string.Empty;
                Hair = _objCharacter.Hair ?? string.Empty;
                Skin = _objCharacter.Skin ?? string.Empty;
                Concept = Plain(_objCharacter.Concept);
                Description = Plain(_objCharacter.Description);
                Background = Plain(_objCharacter.Background);
                Notes = Plain(_objCharacter.Notes);
                GameNotes = Plain(_objCharacter.GameNotes);
            }
            finally
            {
                _blnLoading = false;
            }
        }

        private static string Plain(string strText) => string.IsNullOrEmpty(strText) ? string.Empty : strText.RtfToPlainText();

        [ObservableProperty] private string _characterName;
        [ObservableProperty] private string _alias;
        [ObservableProperty] private string _playerName;
        [ObservableProperty] private string _gender;
        [ObservableProperty] private string _age;
        [ObservableProperty] private string _height;
        [ObservableProperty] private string _weight;
        [ObservableProperty] private string _eyes;
        [ObservableProperty] private string _hair;
        [ObservableProperty] private string _skin;
        [ObservableProperty] private string _concept;
        [ObservableProperty] private string _description;
        [ObservableProperty] private string _background;
        [ObservableProperty] private string _notes;
        [ObservableProperty] private string _gameNotes;

        partial void OnCharacterNameChanged(string value) => Write(x => x.Name = value);
        partial void OnAliasChanged(string value) => Write(x => x.Alias = value);
        partial void OnPlayerNameChanged(string value) => Write(x => x.PlayerName = value);
        partial void OnGenderChanged(string value) => Write(x => x.Gender = value);
        partial void OnAgeChanged(string value) => Write(x => x.Age = value);
        partial void OnHeightChanged(string value) => Write(x => x.Height = value);
        partial void OnWeightChanged(string value) => Write(x => x.Weight = value);
        partial void OnEyesChanged(string value) => Write(x => x.Eyes = value);
        partial void OnHairChanged(string value) => Write(x => x.Hair = value);
        partial void OnSkinChanged(string value) => Write(x => x.Skin = value);
        partial void OnConceptChanged(string value) => Write(x => x.Concept = value);
        partial void OnDescriptionChanged(string value) => Write(x => x.Description = value);
        partial void OnBackgroundChanged(string value) => Write(x => x.Background = value);
        partial void OnNotesChanged(string value)
        {
            if (!_notebookWrite) _notebook?.Load(value);
            Write(x => x.Notes = value);
        }
        partial void OnGameNotesChanged(string value) => Write(x => x.GameNotes = value);

        private void Write(Action<Character> funcWrite)
        {
            if (_blnLoading)
                return;
            _objOwner.MarkDirty();
            _pendingWrites = CommitAfterAsync(_pendingWrites, funcWrite);
        }

        private async Task CommitAfterAsync(Task previous, Action<Character> write)
        {
            await previous;
            await _objOwner.WriteInfoAsync(write);
        }
    }
}
