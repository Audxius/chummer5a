using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class LifeModuleWindow : Window
    {
        private readonly Character _character;
        private readonly ComboBox _stage = new ComboBox { Width = 230 };
        private readonly CheckBox _available = new CheckBox { Content = "Only eligible modules", IsChecked = true };
        private readonly TextBox _search = new TextBox { PlaceholderText = "Search life modules and versions…" };
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private IReadOnlyList<LifeModuleOption> _options = Array.Empty<LifeModuleOption>();
        private int _version;
        private Task _pending;
        private string _pendingStage;
        private bool _pendingEnforce;
        public LifeModuleOption Selected { get; private set; }
        public bool EnforceRequirements => _available.IsChecked == true;
        public bool AddAgain { get; private set; }

        public LifeModuleWindow(Character character)
        {
            _character = character; Title = "Life Modules"; Width = 1050; Height = 750; MinWidth = 800; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 16) };
            toolbar.Children.Add(_search); Grid.SetColumn(_stage, 1); toolbar.Children.Add(_stage); Grid.SetColumn(_available, 2); toolbar.Children.Add(_available);
            var stages = new[] { new ListItem("", "All stages") }.Concat(LifeModuleRules.Stages(character)).ToList();
            _stage.ItemsSource = stages; _stage.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            _stage.SelectedItem = stages.FirstOrDefault(x => x.Value?.ToString() == LifeModuleRules.NextStage(character));
            DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
            var add = new Button { Content = "ADD MODULE", IsEnabled = false, Classes = { "primary" } };
            var again = new Button { Content = "ADD & PICK ANOTHER", IsEnabled = false }; var cancel = new Button { Content = "CANCEL" };
            void Accept(bool another) { if (_list.SelectedItem is LifeModuleOption module) { Selected = module; AddAgain = another; Close(); } }
            add.Click += (_, _) => Accept(false); again.Click += (_, _) => Accept(true); cancel.Click += (_, _) => Close();
            footer.Children.Add(add); footer.Children.Add(again); footer.Children.Add(cancel);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280"), ColumnSpacing = 20 };
            _list.ItemTemplate = new FuncDataTemplate<LifeModuleOption>((item, _) => new TextBlock { Text = item?.Name ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            layout.Children.Add(_list); Grid.SetColumn(_details, 1); layout.Children.Add(_details); root.Children.Add(layout); Content = root;
            _list.SelectionChanged += (_, _) =>
            {
                var selected = _list.SelectedItem as LifeModuleOption;
                _details.Text = selected == null ? "Choose a module or version." : selected.Name + "\n\n" + selected.Stage + "\n" + selected.Karma + " karma\n" + selected.Source;
                add.IsEnabled = again.IsEnabled = selected != null;
            };
            _search.TextChanged += (_, _) => Filter();
            _stage.SelectionChanged += async (_, _) => await RefreshAsync();
            _available.IsCheckedChanged += async (_, _) => await RefreshAsync();
            Opened += async (_, _) => await RefreshAsync();
        }

        public Task RefreshAsync()
        {
            string stage = (_stage.SelectedItem as ListItem?)?.Value?.ToString(); bool enforce = EnforceRequirements;
            if (_pending != null && !_pending.IsCompleted && stage == _pendingStage && enforce == _pendingEnforce)
                return _pending;
            _pendingStage = stage; _pendingEnforce = enforce;
            return _pending = RefreshCoreAsync(stage, enforce, ++_version);
        }

        private async Task RefreshCoreAsync(string stage, bool enforce, int version)
        {
            _options = Array.Empty<LifeModuleOption>();
            _list.ItemsSource = _options;
            _details.Text = "Loading modules…";
            try
            {
                var options = await Task.Run(() => LifeModuleRules.OptionsAsync(_character, stage, enforce));
                if (version != _version) return;
                _options = options; Filter();
            }
            catch (Exception ex) { if (version == _version) _details.Text = ex.Message; }
        }

        private void Filter()
        {
            var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _list.ItemsSource = _options.Where(x => words.All(word => x.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToList();
            _list.SelectedIndex = 0;
            if (_list.SelectedItem == null) _details.Text = "No modules match these filters.";
        }
    }
}
