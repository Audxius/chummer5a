using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.XPath;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class TraditionWindow : Window
    {
        public TraditionOption Selected { get; private set; }
        public string CustomName { get; private set; }
        public string CustomDrain { get; private set; }
        public IReadOnlyList<string> CustomSpirits { get; private set; }

        public TraditionWindow(Character character, IReadOnlyList<TraditionOption> options)
        {
            Title = character.RESEnabled ? "Resonance stream" : "Magical tradition";
            Width = 680; Height = 650; MinHeight = 450; MinWidth = 550;
            var root = new DockPanel { Margin = new Thickness(24) };
            var body = new StackPanel { Spacing = 12 };
            var picker = new ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = new FuncDataTemplate<TraditionOption>((item, _) => new TextBlock { Text = item.Name }) };
            var details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var custom = new StackPanel { Spacing = 10 };
            var name = new TextBox { Text = character.MagicTradition.IsCustomTradition ? character.MagicTradition.Name : "", PlaceholderText = "Custom tradition name" };
            var drain = new TextBox { Text = character.MagicTradition.IsCustomTradition ? character.MagicTradition.DrainExpression : "{WIL} + {LOG}", PlaceholderText = "Drain expression, e.g. {WIL} + {LOG}" };
            custom.Children.Add(name); custom.Children.Add(drain);
            string[] current = { character.MagicTradition.SpiritCombat, character.MagicTradition.SpiritDetection, character.MagicTradition.SpiritHealth,
                character.MagicTradition.SpiritIllusion, character.MagicTradition.SpiritManipulation };
            var spirits = character.LoadDataXPath("traditions.xml").Select("/chummer/spirits/spirit/name").Cast<XPathNavigator>().Select(x => x.Value).OrderBy(x => x).ToList();
            var spiritChoices = new List<ComboBox>();
            int index = 0;
            foreach (string category in new[] { "Combat", "Detection", "Health", "Illusion", "Manipulation" })
            {
                var combo = new ComboBox { ItemsSource = spirits, SelectedItem = current[index++], HorizontalAlignment = HorizontalAlignment.Stretch };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
                row.Children.Add(new TextBlock { Text = category + " spirit", VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(combo, 1); row.Children.Add(combo); custom.Children.Add(row); spiritChoices.Add(combo);
            }
            picker.SelectionChanged += (_, _) =>
            {
                var choice = picker.SelectedItem as TraditionOption;
                custom.IsVisible = choice?.IsCustom == true;
                details.Text = choice == null ? "" : "Drain: " + choice.Drain + "\n" + string.Join(", ", choice.Spirits) + "\n" + choice.Source;
            };
            picker.SelectedItem = options.FirstOrDefault(x => x.Id == character.MagicTradition.SourceIDString) ?? options.FirstOrDefault();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
            var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var save = new Button { Content = "SET TRADITION", Classes = { "primary" } };
            save.Click += (_, _) =>
            {
                var choice = picker.SelectedItem as TraditionOption;
                if (choice == null) return;
                if (choice.IsCustom && (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(drain.Text)
                    || !CommonFunctions.IsCharacterAttributeXPathValidOrNull(drain.Text)))
                { error.Text = "Enter a name and a valid drain expression."; return; }
                Selected = choice; CustomName = name.Text; CustomDrain = drain.Text;
                CustomSpirits = spiritChoices.Select(x => x.SelectedItem as string ?? "").ToList();
                Close();
            };
            var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => Close();
            buttons.Children.Add(save); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            body.Children.Add(new TextBlock { Text = Title.ToUpperInvariant(), Classes = { "h2" } });
            body.Children.Add(picker); body.Children.Add(details); body.Children.Add(custom); body.Children.Add(error);
            root.Children.Add(new ScrollViewer { Content = body }); Content = root;
        }
    }
}
