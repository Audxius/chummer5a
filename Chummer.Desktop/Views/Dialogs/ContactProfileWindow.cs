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

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class ContactProfileWindow : Window
    {
        private ContactCardViewModel _card;
        private Bitmap _artwork;
        private bool _closed;
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        public ContactCardViewModel Profile => _card;
        public ContactProfileWindow(ContactCardViewModel card)
        {
            _card = card; Title = card.Name; Width = 1000; Height = 780; MinWidth = 700; MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Draw();
            Opened += async (_, _) =>
            {
                try
                {
                    var initial = _card;
                    var art = await initial.LoadFullPortraitAsync();
                    if (_closed || !ReferenceEquals(initial, _card)) art?.Dispose(); else { _artwork = art; Draw(); }
                }
                catch (Exception ex) { _status.Text = "Unable to read portrait: " + ex.Message; }
            };
            Closed += (_, _) => { _closed = true; _artwork?.Dispose(); };
        }
        private static TextBlock Text(string value, int size = 14) => new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        private Button Action(string title, Func<Task> action, bool enabled = true)
        {
            var button = new Button { Content = title, IsEnabled = enabled, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await action();
                    if (!_closed)
                    {
                        var next = await _card.RefreshProfileAsync();
                        bool reloadArt = _artwork == null || !ReferenceEquals(next.Portrait, _card.Portrait);
                        _card = next;
                        if (reloadArt)
                        {
                            var art = await next.LoadFullPortraitAsync();
                            if (_closed) { art?.Dispose(); return; }
                            var previous = _artwork; _artwork = art; Draw(); previous?.Dispose();
                        }
                        else Draw();
                    }
                }
                catch (Exception ex) { _status.Text = ex.Message; }
                finally { button.IsEnabled = enabled; }
            };
            return button;
        }
        private void Rating(StackPanel panel, string name, int value, System.Collections.Generic.IReadOnlyList<bool> pips, Func<Task> down, Func<Task> up, bool canDown, bool canUp, IBrush brush)
        {
            panel.Children.Add(Text(name + "  " + value, 17));
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (bool on in pips) bar.Children.Add(new Border { Width = 12, Height = 9, Background = on ? brush : new SolidColorBrush(Color.Parse("#60768F")), Opacity = on ? 1 : 0.3 });
            panel.Children.Add(bar);
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            actions.Children.Add(Action("−", down, canDown)); actions.Children.Add(Action("+", up, canUp)); panel.Children.Add(actions);
        }
        private void Draw()
        {
            if (_status.Parent is Panel oldParent) oldParent.Children.Remove(_status);
            var body = new StackPanel { Spacing = 16 };
            body.Children.Add(new TextBlock { Text = _card.Name, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "ContactProfileName" });
            body.Children.Add(Text(_card.Subtitle));
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), ColumnSpacing = 28 };
            var left = new StackPanel { Spacing = 12 };
            if (_card.HasPortrait) left.Children.Add(new Image { Source = _artwork ?? _card.Portrait, Stretch = Stretch.Uniform, MaxHeight = 500, Name = "ContactProfileArtwork" });
            else left.Children.Add(new Border { Width = 280, Height = 280, CornerRadius = new CornerRadius(140), Background = new SolidColorBrush(Color.Parse("#101823")),
                Child = new TextBlock { Text = _card.Initials, FontSize = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            if (_card.HasPortrait) left.Children.Add(Action("Inspect artwork…", async () =>
            {
                var viewer = new Window { Title = _card.Name + " — artwork", Width = 1100, Height = 900, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new Image { Source = _artwork ?? _card.Portrait, Stretch = Stretch.Uniform, Margin = new Thickness(12) } };
                await viewer.ShowDialog(this);
            }));
            var right = new StackPanel { Spacing = 10 };
            Rating(right, "Connection", _card.Connection, _card.ConnectionPips, () => _card.ConnectionDownCommand.ExecuteAsync(null), () => _card.ConnectionUpCommand.ExecuteAsync(null), _card.CanDecreaseConnection, _card.CanIncreaseConnection, new SolidColorBrush(Color.Parse("#00E5FF")));
            Rating(right, "Loyalty", _card.Loyalty, _card.LoyaltyPips, () => _card.LoyaltyDownCommand.ExecuteAsync(null), () => _card.LoyaltyUpCommand.ExecuteAsync(null), _card.CanDecreaseLoyalty, _card.CanIncreaseLoyalty, new SolidColorBrush(Color.Parse("#FF2E88")));
            foreach (var fact in _card.Facts) right.Children.Add(Text(fact.Label + ": " + fact.Value));
            if (_card.HasTags) right.Children.Add(Text(string.Join(" · ", _card.Tags)));
            if (_card.HasNotes) right.Children.Add(Text(_card.Notes));
            if (_card.LinkedStats.Count > 0)
            {
                right.Children.Add(Text("Attributes", 18));
                foreach (var stat in _card.LinkedStats) right.Children.Add(Text(stat.Label + "  " + stat.Value));
                right.Children.Add(Text("Skills", 18));
                foreach (var skill in _card.LinkedSkills) right.Children.Add(Text(skill.Label + "  " + skill.Value));
                if (_card.LinkedSkills.Count == 0) right.Children.Add(Text("No trained skills recorded."));
            }
            else right.Children.Add(Text("Link a character sheet to see full game stats."));
            var actions = new WrapPanel();
            actions.Children.Add(Action("Edit profile…", () => _card.EditCommand.ExecuteAsync(this), _card.IsEditable));
            actions.Children.Add(Action("Link character sheet…", async () => { var file = await CharacterViewModel.PickContactFileAsync(this); if (file != null) await _card.LinkProfileAsync(file); }, _card.IsEditable));
            if (!string.IsNullOrEmpty(_card.LinkedFile)) actions.Children.Add(Action("Open full character sheet", async () =>
            {
                string file = Path.GetFullPath(_card.LinkedFile);
                if (!File.Exists(file)) { _status.Text = "Linked character file was not found. Link its current location."; return; }
                Close(); await MainWindowViewModel.Current.OpenCharacterFileAsync(file);
            }));
            actions.Children.Add(Action("Delete…", async () => { await _card.DeleteCommand.ExecuteAsync(null); if (!await _card.ExistsAsync()) Close(); }, _card.IsEditable));
            right.Children.Add(actions); right.Children.Add(_status);
            columns.Children.Add(left); Grid.SetColumn(right, 1); columns.Children.Add(right); body.Children.Add(columns);
            var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => Close(); body.Children.Add(close);
            Content = new ScrollViewer { Content = body, Margin = new Thickness(24) };
        }
    }
}
