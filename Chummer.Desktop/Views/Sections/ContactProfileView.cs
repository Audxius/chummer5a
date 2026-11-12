/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Sections
{
    public sealed class ContactProfileView : UserControl
    {
        private ContactCardViewModel _card;
        private readonly Action _back;
        private Bitmap _art;
        private bool _closed, _inspect;
        private Grid _columns, _facts;
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public ContactCardViewModel Profile => _card;
        public ContactProfileView(ContactCardViewModel card, Action back)
        {
            _card = card; _back = back; Name = "ContactProfilePage";
            Draw();
            AttachedToVisualTree += async (_, _) => await LoadAsync();
            DetachedFromVisualTree += (_, _) => { _closed = true; _art?.Dispose(); _art = null; };
            SizeChanged += (_, _) => Resize();
        }
        private IBrush Brush(string key) => (IBrush)Application.Current.FindResource(key);
        private TextBlock Text(string value, int size = 14, bool muted = false) => new TextBlock
        { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(muted ? "MutedBrush" : "TextBrush") };
        private Border Panel(Control content) => new Border { Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(6),
            Background = Brush("PanelBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) };
        private async Task LoadAsync()
        {
            try
            {
                var initial = _card;
                var next = await initial.RefreshProfileAsync();
                var art = await next.LoadFullPortraitAsync();
                if (_closed || !ReferenceEquals(initial, _card)) { art?.Dispose(); return; }
                _card = next; _art?.Dispose(); _art = art; Draw();
            }
            catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        }
        private async Task RunAsync(Func<Task> action)
        {
            try
            {
                await action(); if (_closed) return;
                if (!await _card.ExistsAsync()) { _back(); return; }
                var next = await _card.RefreshProfileAsync();
                bool changed = _art == null || !ReferenceEquals(next.Portrait, _card.Portrait); _card = next;
                if (changed)
                {
                    var art = await next.LoadFullPortraitAsync();
                    if (_closed) { art?.Dispose(); return; }
                    _art?.Dispose(); _art = art;
                }
                Draw();
            }
            catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        }
        private Button ActionButton(string text, Func<Task> action, bool enabled = true)
        {
            var button = new Button { Content = text, IsEnabled = enabled };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await RunAsync(action); } finally { button.IsEnabled = enabled; } };
            return button;
        }
        private Border Rating(string label, int value, System.Collections.Generic.IReadOnlyList<bool> pips, bool connection)
        {
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(Text(label, 13, true));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 7 };
            var number = Text(value.ToString(), 25); number.FontWeight = FontWeight.SemiBold; row.Children.Add(number);
            var minus = ActionButton("−", () => (connection ? _card.ConnectionDownCommand : _card.LoyaltyDownCommand).ExecuteAsync(null), connection ? _card.CanDecreaseConnection : _card.CanDecreaseLoyalty);
            var plus = ActionButton("+", () => (connection ? _card.ConnectionUpCommand : _card.LoyaltyUpCommand).ExecuteAsync(null), connection ? _card.CanIncreaseConnection : _card.CanIncreaseLoyalty);
            minus.Classes.Add("step"); plus.Classes.Add("step"); Grid.SetColumn(minus, 2); Grid.SetColumn(plus, 3); row.Children.Add(minus); row.Children.Add(plus);
            content.Children.Add(row);
            var bar = new WrapPanel();
            foreach (bool on in pips) bar.Children.Add(new Border { Width = 7, Height = 7, Margin = new Thickness(0, 0, 3, 3), Background = Brush(on ? connection ? "CyanBrush" : "MagentaBrush" : "BorderBrush"), Opacity = on ? 1 : 0.3 });
            content.Children.Add(bar); return Panel(content);
        }
        private void Resize()
        {
            if (_columns != null) _columns.ColumnDefinitions[0].Width = new GridLength(Bounds.Width < 900 ? 230 : 360);
            if (_facts == null) return;
            int count = Bounds.Width < 900 ? 1 : 2;
            _facts.ColumnDefinitions = new ColumnDefinitions(count == 1 ? "*" : "*,*");
            _facts.RowDefinitions.Clear();
            for (int i = 0; i < _facts.Children.Count; i++)
            {
                if (i % count == 0) _facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetRow(_facts.Children[i], i / count); Grid.SetColumn(_facts.Children[i], i % count);
            }
        }
        private void Draw()
        {
            if (_status.Parent is Panel previous) previous.Children.Remove(_status);
            var root = new DockPanel { MaxWidth = _inspect ? double.PositiveInfinity : 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 20) };
            var title = new StackPanel { Spacing = 7 };
            var back = new Button { Content = _inspect ? "← Back to profile" : _card.IsPet ? "← Pets & cohorts" : "← Contacts", Classes = { "ghost" }, Padding = new Thickness(0, 3) };
            back.Click += (_, _) => { if (_inspect) { _inspect = false; Draw(); } else _back(); };
            title.Children.Add(back);
            title.Children.Add(new TextBlock { Text = _card.Name, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "ContactProfileName" });
            if (!_inspect) title.Children.Add(Text(string.Join(" · ", new[] { _card.Role, _card.Location }.Where(x => !string.IsNullOrWhiteSpace(x))), 14, true));
            heading.Children.Add(title);
            if (!_inspect)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
                actions.Children.Add(ActionButton("Edit profile…", () => _card.EditCommand.ExecuteAsync(this), _card.IsEditable));
                var more = new Button { Content = "More…" }; var menu = new MenuFlyout();
                var link = new MenuItem { Header = "Link character sheet…", IsEnabled = _card.IsEditable };
                link.Click += async (_, _) => await RunAsync(async () => { var file = await CharacterViewModel.PickContactFileAsync(TopLevel.GetTopLevel(this) as Window); if (file != null) await _card.LinkProfileAsync(file); });
                var delete = new MenuItem { Header = "Delete contact…", IsEnabled = _card.IsEditable, Foreground = Brush("RedBrush") };
                delete.Click += async (_, _) => await RunAsync(() => _card.DeleteCommand.ExecuteAsync(null));
                menu.Items.Add(link); menu.Items.Add(new Separator()); menu.Items.Add(delete); more.Flyout = menu; actions.Children.Add(more);
                Grid.SetColumn(actions, 1); heading.Children.Add(actions);
            }
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            if (_inspect)
            {
                root.Children.Add(new Image { Source = _art ?? _card.Portrait, Stretch = Stretch.Uniform, Name = "ContactArtworkViewer", Margin = new Thickness(0, 0, 0, 22) }); Content = root; return;
            }
            _columns = new Grid { ColumnDefinitions = new ColumnDefinitions("360,*"), ColumnSpacing = 22 };
            var left = new StackPanel { Spacing = 10 };
            if (_card.HasPortrait)
            {
                left.Children.Add(new Image { Source = _art ?? _card.Portrait, Stretch = Stretch.Uniform, Name = "ContactProfileArtwork" });
                var inspect = new Button { Content = "Enlarge artwork", HorizontalAlignment = HorizontalAlignment.Stretch };
                inspect.Click += (_, _) => { _inspect = true; Draw(); }; left.Children.Add(inspect);
            }
            else left.Children.Add(new Border { Height = 230, Background = Brush("PanelBrush"), CornerRadius = new CornerRadius(6), Child = new TextBlock { Text = _card.Initials, FontSize = 60, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var right = new StackPanel { Spacing = 22 };
            var ratings = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
            ratings.Children.Add(Rating("Connection", _card.Connection, _card.ConnectionPips, true));
            var loyalty = Rating("Loyalty", _card.Loyalty, _card.LoyaltyPips, false); Grid.SetColumn(loyalty, 1); ratings.Children.Add(loyalty); right.Children.Add(ratings);
            var details = new StackPanel { Spacing = 20 }; details.Children.Add(Text("Profile", 19));
            var facts = _facts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 22, RowSpacing = 16 };
            for (int i = 0; i < _card.Facts.Count; ++i)
            {
                if (i % 2 == 0) facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var fact = _card.Facts[i]; var cell = new StackPanel { Spacing = 4 }; cell.Children.Add(Text(fact.Label, 12, true)); cell.Children.Add(Text(fact.Value));
                Grid.SetRow(cell, i / 2); Grid.SetColumn(cell, i % 2); facts.Children.Add(cell);
            }
            details.Children.Add(facts);
            if (_card.HasTags) details.Children.Add(Text(string.Join(" · ", _card.Tags), 12, true));
            right.Children.Add(details);
            if (_card.HasNotes) { var notes = new StackPanel { Spacing = 10 }; notes.Children.Add(Text("Notes", 17)); notes.Children.Add(Text(_card.Notes)); right.Children.Add(Panel(notes)); }
            var sheet = new StackPanel { Spacing = 12 }; sheet.Children.Add(Text("Character sheet", 17));
            if (_card.LinkedStats.Count > 0)
            {
                var stats = new WrapPanel(); foreach (var stat in _card.LinkedStats) { var cell = new StackPanel { Width = 110, Spacing = 4, Margin = new Thickness(0, 0, 10, 12) }; cell.Children.Add(Text(stat.Label, 12, true)); cell.Children.Add(Text(stat.Value, 22)); stats.Children.Add(cell); } sheet.Children.Add(stats);
                if (_card.LinkedSkills.Count > 0) { sheet.Children.Add(Text("Trained skills", 14, true)); foreach (var skill in _card.LinkedSkills) sheet.Children.Add(Text(skill.Label + "  " + skill.Value)); }
                sheet.Children.Add(ActionButton("Open full character sheet", async () => { string path = Path.GetFullPath(_card.LinkedFile); if (!File.Exists(path)) throw new IOException("Linked sheet not found. Link its current location."); await MainWindowViewModel.Current.OpenCharacterFileAsync(path); }));
            }
            else if (!string.IsNullOrWhiteSpace(_card.LinkedFile))
            {
                sheet.Children.Add(Text("No character sheet linked.", 13, true));
                sheet.Children.Add(ActionButton("Link character sheet…", async () => { var file = await CharacterViewModel.PickContactFileAsync(TopLevel.GetTopLevel(this) as Window); if (file != null) await _card.LinkProfileAsync(file); }, _card.IsEditable));
            }
            if (_card.LinkedStats.Count > 0 || !string.IsNullOrWhiteSpace(_card.LinkedFile)) right.Children.Add(Panel(sheet));
            right.Children.Add(_status);
            _columns.Children.Add(left); Grid.SetColumn(right, 1); _columns.Children.Add(right); Resize();
            root.Children.Add(new ScrollViewer { Content = _columns, Padding = new Thickness(0, 0, 0, 24) }); Content = root;
        }
    }
}
