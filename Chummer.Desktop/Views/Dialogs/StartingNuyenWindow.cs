using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Backend.Equipment;
using Chummer.Desktop.Services;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class StartingNuyenWindow : Window
    {
        private readonly ComboBox _lifestyle = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly NumericUpDown _roll = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, FormatString = "0" };
        private readonly TextBlock _formula = new TextBlock();
        private readonly Button _reroll = new Button { Content = "ROLL DICE" };
        public decimal? StartingNuyen { get; private set; }

        public StartingNuyenWindow(IReadOnlyList<Lifestyle> lifestyles)
        {
            Title = "Starting nuyen"; Width = 560; Height = 340; CanResize = false;
            _lifestyle.ItemsSource = lifestyles;
            _lifestyle.ItemTemplate = new FuncDataTemplate<Lifestyle>((item, _) => new TextBlock { Text = item.CurrentDisplayName });
            var body = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
            body.Children.Add(new TextBlock { Text = "LIFESTYLE // STARTING CASH", Classes = { "h2" } });
            body.Children.Add(_lifestyle); body.Children.Add(_roll); body.Children.Add(_formula);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var accept = new Button { Content = "START CAREER", Classes = { "primary" } };
            var cancel = new Button { Content = "CANCEL" };
            accept.Click += (_, _) =>
            {
                StartingNuyen = (_roll.Value ?? 0) * ((_lifestyle.SelectedItem as Lifestyle)?.Multiplier ?? 0);
                Close();
            };
            cancel.Click += (_, _) => Close();
            _reroll.Click += async (_, _) => await RollAsync();
            _roll.ValueChanged += (_, _) => UpdateFormula();
            _lifestyle.SelectionChanged += async (_, _) =>
            {
                int dice = (_lifestyle.SelectedItem as Lifestyle)?.Dice ?? 0;
                _roll.Minimum = 0; _roll.Maximum = dice * 6; _roll.Minimum = dice;
                _reroll.IsEnabled = dice > 0;
                await RollAsync();
            };
            buttons.Children.Add(_reroll); buttons.Children.Add(accept); buttons.Children.Add(cancel);
            body.Children.Add(buttons); Content = body;
            _lifestyle.SelectedItem = lifestyles.OrderByDescending(x => x.ExpectedValue).FirstOrDefault();
        }

        private async Task RollAsync()
        {
            int dice = (_lifestyle.SelectedItem as Lifestyle)?.Dice ?? 0;
            int sum = 0;
            for (int i = 0; i < dice; ++i) sum += await Utils.GlobalRandom.NextD6ModuloBiasRemovedAsync();
            _roll.Value = sum; UpdateFormula();
        }

        private void UpdateFormula()
        {
            var lifestyle = _lifestyle.SelectedItem as Lifestyle;
            decimal multiplier = lifestyle?.Multiplier ?? 0;
            _formula.Text = lifestyle?.Dice + "d6: " + (_roll.Value ?? 0) + " × " + multiplier + "¥ = " + ((_roll.Value ?? 0) * multiplier) + "¥";
        }

        public static Task<decimal?> ShowAsync(IReadOnlyList<Lifestyle> lifestyles, CancellationToken token = default)
            => ModalHelper.RunModalAsync(async () =>
            {
                var window = new StartingNuyenWindow(lifestyles);
                await DialogHost.ShowModalAsync(window, null);
                return window.StartingNuyen;
            }, token);
    }
}
