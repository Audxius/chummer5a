/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class NotebookPageViewModel : ObservableObject
    {
        private readonly NotebookViewModel _owner;
        public Guid Id { get; }
        [ObservableProperty] private string _title;
        [ObservableProperty] private string _body;
        public NotebookPageViewModel(NotebookViewModel owner, NotebookPageData page)
        { _owner = owner; Id = page.Id; _title = page.Title; _body = page.Body; }
        partial void OnTitleChanged(string value) => _owner.PageChanged();
        partial void OnBodyChanged(string value) => _owner.PageChanged();
    }

    public sealed partial class NotebookViewModel : ObservableObject
    {
        private readonly CharacterInfoViewModel _info;
        private CancellationTokenSource _delay;
        private bool _changed;
        public ObservableCollection<NotebookPageViewModel> Pages { get; } = new ObservableCollection<NotebookPageViewModel>();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedPage))]
        private NotebookPageViewModel _selectedPage;
        public bool HasSelectedPage => SelectedPage != null;
        public bool CanMoveUp => SelectedPage != null && Pages.IndexOf(SelectedPage) > 0;
        public bool CanMoveDown => SelectedPage != null && Pages.IndexOf(SelectedPage) < Pages.Count - 1;

        public NotebookViewModel(CharacterInfoViewModel info) { _info = info; Load(info.Notes); }
        internal void Load(string text)
        {
            _delay?.Cancel(); _changed = false;
            Guid? selected = SelectedPage?.Id;
            Pages.Clear(); foreach (var page in NotebookCodec.Parse(text)) Pages.Add(new NotebookPageViewModel(this, page));
            SelectedPage = Pages.FirstOrDefault(x => x.Id == selected) ?? Pages.FirstOrDefault();
            NotifyNavigation();
        }
        partial void OnSelectedPageChanged(NotebookPageViewModel value) => NotifyNavigation();
        private void NotifyNavigation() { OnPropertyChanged(nameof(CanMoveUp)); OnPropertyChanged(nameof(CanMoveDown)); }
        internal void PageChanged()
        {
            _changed = true; _info.MarkNotebookDirty();
            _delay?.Cancel(); _delay?.Dispose(); _delay = new CancellationTokenSource();
            _ = SaveAfterPauseAsync(_delay.Token);
        }
        private async Task SaveAfterPauseAsync(CancellationToken token)
        {
            try { await Task.Delay(200, token); if (!token.IsCancellationRequested) CommitPending(); }
            catch (OperationCanceledException) { }
        }
        internal void CommitPending()
        {
            _delay?.Cancel();
            if (!_changed) return;
            _changed = false;
            _info.SetNotebookNotes(NotebookCodec.Serialize(Pages.Select(x => new NotebookPageData(x.Id, x.Title, x.Body))));
        }
        [RelayCommand]
        private void AddPage()
        {
            int number = 1; while (Pages.Any(x => x.Title == "Note " + number)) ++number;
            var page = new NotebookPageViewModel(this, new NotebookPageData(Guid.NewGuid(), "Note " + number, ""));
            Pages.Add(page); SelectedPage = page; PageChanged(); NotifyNavigation();
        }
        [RelayCommand]
        private async Task DeletePage()
        {
            var page = SelectedPage; if (page == null) return;
            if (!string.IsNullOrWhiteSpace(page.Body) && !await CommonFunctions.ConfirmDeleteAsync("Delete note \"" + page.Title + "\"?")) return;
            int index = Pages.IndexOf(page); Pages.Remove(page);
            SelectedPage = Pages.Count == 0 ? null : Pages[Math.Min(index, Pages.Count - 1)];
            PageChanged(); NotifyNavigation();
        }
        [RelayCommand] private void MoveUp() => Move(-1);
        [RelayCommand] private void MoveDown() => Move(1);
        private void Move(int direction)
        {
            int index = SelectedPage == null ? -1 : Pages.IndexOf(SelectedPage), target = index + direction;
            if (index < 0 || target < 0 || target >= Pages.Count) return;
            Pages.Move(index, target); PageChanged(); NotifyNavigation();
        }
    }
}
