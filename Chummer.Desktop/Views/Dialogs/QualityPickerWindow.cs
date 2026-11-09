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
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>What the quality picker returns (SelectQuality's SelectedQuality / SelectedRating / FreeCost / AddAgain).</summary>
    public sealed class QualityPick
    {
        public string Id { get; set; }
        public int Rating { get; set; } = 1;
        public bool Free { get; set; }
        public bool AddAgain { get; set; }
    }

    /// <summary>
    /// Native SelectQuality: category, search, "only what I can take", metagenic and karma filters on the left; the cost
    /// (doubled in career mode like legacy), source, requirements, level and free toggle of the highlighted quality on the right.
    /// </summary>
    public sealed class QualityPickerWindow : HudWindow
    {
        private sealed class Row
        {
            public Row(ListItem objItem, string strCost, bool blnPositive)
            {
                Item = objItem;
                Cost = strCost;
                Positive = blnPositive;
            }

            public ListItem Item { get; }
            public string Name => Item.Name;
            public string Id => Item.Value?.ToString();
            public string Cost { get; }
            public bool Positive { get; }
        }

        private static string s_strLastCategory = QualitySelector.ShowAll;

        private readonly QualitySelector _objSelector;
        private QualityPick _objResult;

        public QualityPickerWindow(Character objCharacter) : base("Add quality", 940)
        {
            _objSelector = new QualitySelector(objCharacter);
            bool blnCareer = objCharacter.Created;

            ComboBox cboCategory = new ComboBox { ItemsSource = _objSelector.Categories, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
            cboCategory.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            cboCategory.SelectedItem = _objSelector.Categories.FirstOrDefault(x => x.Value?.ToString() == s_strLastCategory);
            if (cboCategory.SelectedItem == null && _objSelector.Categories.Count > 0)
                cboCategory.SelectedIndex = 0;
            TextBox txtSearch = new TextBox { PlaceholderText = Tr.Get("String_Search") };
            CheckBox chkAvailable = new CheckBox { Content = "Only what this runner can take", IsChecked = true };
            List<ListItem> lstMetagenic = new List<ListItem>
            {
                new ListItem(MetagenicFilter.Any, "All qualities"),
                new ListItem(MetagenicFilter.OnlyMetagenic, "Only metagenic"),
                new ListItem(MetagenicFilter.NoMetagenic, "No metagenic"),
            };
            ComboBox cboMetagenic = new ComboBox { ItemsSource = lstMetagenic, MinWidth = 150 };
            cboMetagenic.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            cboMetagenic.SelectedItem = lstMetagenic.First(x => (MetagenicFilter)x.Value == _objSelector.DefaultMetagenicFilter);
            NumericUpDown nudMin = new NumericUpDown { Minimum = -100, Maximum = 100, Value = 0, Increment = 1, Width = 110, FormatString = "0" };
            NumericUpDown nudMax = new NumericUpDown { Minimum = -100, Maximum = 100, Value = 0, Increment = 1, Width = 110, FormatString = "0" };
            ToolTip.SetTip(nudMin, "Lowest karma value to list (0 and 0 = any)");
            ToolTip.SetTip(nudMax, "Highest karma value to list (0 and 0 = any)");

            ListBox lstChoices = new ListBox { Height = 440 };
            lstChoices.ItemTemplate = new FuncDataTemplate<Row>((x, _) =>
            {
                Grid grdRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                grdRow.Children.Add(new TextBlock { Text = x.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                TextBlock txtCost = new TextBlock
                {
                    Text = x.Cost,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush(x.Positive ? "GreenBrush" : "RedBrush"),
                };
                Grid.SetColumn(txtCost, 1);
                grdRow.Children.Add(txtCost);
                return grdRow;
            });
            TextBlock txtCount = new TextBlock { Classes = { "label" } };

            // Details pane
            TextBlock txtName = new TextBlock { FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
            Border bdrType = new Border { Classes = { "chip" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 1) };
            TextBlock txtType = new TextBlock { FontSize = 10, FontWeight = FontWeight.Bold };
            bdrType.Child = txtType;
            TextBlock txtCostBig = new TextBlock { FontSize = 30, FontWeight = FontWeight.Black };
            TextBlock txtCostLabel = new TextBlock { Classes = { "label" }, Text = blnCareer ? "KARMA COST (CAREER)" : "KARMA" };
            TextBlock txtSource = new TextBlock { Classes = { "muted" } };
            TextBlock txtRequirements = new TextBlock { TextWrapping = TextWrapping.Wrap };
            NumericUpDown nudRating = new NumericUpDown { Minimum = 1, Maximum = 1, Value = 1, Increment = 1, Width = 120, FormatString = "0" };
            TextBlock lblRating = new TextBlock { Text = "LEVELS", Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
            StackPanel pnlRating = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { lblRating, nudRating } };
            CheckBox chkFree = new CheckBox { Content = "Free (no karma)" };
            ToolTip.SetTip(chkFree, "Take the quality without paying karma (granted by the GM, a story reward...)");
            TextBlock txtHint = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            StackPanel pnlDetails = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(18, 0, 0, 0),
                Children = { txtName, bdrType, new StackPanel { Spacing = 0, Children = { txtCostLabel, txtCostBig } }, txtSource, txtRequirements, pnlRating, chkFree, txtHint }
            };

            Button cmdOk = new Button { Content = "ADD", IsDefault = true, Classes = { "primary" }, MinWidth = 90 };
            Button cmdAgain = new Button { Content = "ADD & PICK ANOTHER" };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };

            bool blnRefreshing = false;
            void UpdateDetails()
            {
                if (!(lstChoices.SelectedItem is Row objRow))
                {
                    pnlDetails.IsVisible = false;
                    cmdOk.IsEnabled = cmdAgain.IsEnabled = false;
                    return;
                }
                pnlDetails.IsVisible = true;
                cmdOk.IsEnabled = cmdAgain.IsEnabled = true;
                string strId = objRow.Id;
                txtName.Text = objRow.Name;
                txtType.Text = objRow.Positive ? "POSITIVE" : "NEGATIVE";
                txtType.Foreground = Brush(objRow.Positive ? "GreenBrush" : "RedBrush");
                int intMax = _objSelector.MaximumRating(strId);
                blnRefreshing = true;
                nudRating.Maximum = intMax;
                if (nudRating.Value > intMax)
                    nudRating.Value = intMax;
                blnRefreshing = false;
                pnlRating.IsVisible = intMax > 1;
                int intRating = (int)(nudRating.Value ?? 1);
                txtCostBig.Text = _objSelector.CostText(strId, intRating, chkFree.IsChecked == true);
                txtCostBig.Foreground = Brush(objRow.Positive ? "GreenBrush" : "RedBrush");
                txtSource.Text = "Source  " + _objSelector.SourceText(strId);
                bool blnMet = _objSelector.RequirementsMet(strId);
                txtRequirements.Text = blnMet ? "✔ Requirements met" : "✖ Requirements not met";
                txtRequirements.Foreground = Brush(blnMet ? "GreenBrush" : "RedBrush");
                txtHint.Text = blnCareer
                    ? objRow.Positive
                        ? "Career mode: positive qualities cost twice their karma value unless the quality or your settings say otherwise."
                        : "Career mode: a new negative quality gives no karma; buying it off later costs karma."
                    : "Creation: qualities count against the positive / negative quality karma limits.";
            }

            void Rebuild()
            {
                QualityFilter objFilter = new QualityFilter
                {
                    Category = (cboCategory.SelectedItem as ListItem?)?.Value?.ToString() ?? QualitySelector.ShowAll,
                    Search = txtSearch.Text ?? string.Empty,
                    OnlyAvailable = chkAvailable.IsChecked == true,
                    Metagenic = cboMetagenic.SelectedItem is ListItem objMeta ? (MetagenicFilter)objMeta.Value : MetagenicFilter.Any,
                    MinimumKarma = (int)(nudMin.Value ?? 0),
                    MaximumKarma = (int)(nudMax.Value ?? 0),
                };
                string strOld = (lstChoices.SelectedItem as Row)?.Id;
                List<Row> lstRows = _objSelector.Options(objFilter).Select(x =>
                {
                    string strId = x.Value?.ToString();
                    return new Row(x, _objSelector.CostText(strId, 1, false), _objSelector.IsPositive(strId));
                }).ToList();
                lstChoices.ItemsSource = lstRows;
                lstChoices.SelectedItem = lstRows.FirstOrDefault(x => x.Id == strOld);
                txtCount.Text = lstRows.Count.ToString(GlobalSettings.CultureInfo) + " QUALITIES";
                UpdateDetails();
            }

            cboCategory.SelectionChanged += (sender, args) => Rebuild();
            txtSearch.TextChanged += (sender, args) => Rebuild();
            chkAvailable.IsCheckedChanged += (sender, args) => Rebuild();
            cboMetagenic.SelectionChanged += (sender, args) => Rebuild();
            nudMin.ValueChanged += (sender, args) => Rebuild();
            nudMax.ValueChanged += (sender, args) => Rebuild();
            lstChoices.SelectionChanged += (sender, args) => UpdateDetails();
            nudRating.ValueChanged += (sender, args) =>
            {
                if (!blnRefreshing)
                    UpdateDetails();
            };
            chkFree.IsCheckedChanged += (sender, args) => UpdateDetails();

            void Accept(bool blnAgain)
            {
                if (!(lstChoices.SelectedItem is Row objRow) || !_objSelector.Validate(objRow.Id))
                    return;
                s_strLastCategory = (cboCategory.SelectedItem as ListItem?)?.Value?.ToString() ?? QualitySelector.ShowAll;
                _objResult = new QualityPick { Id = objRow.Id, Rating = (int)(nudRating.Value ?? 1), Free = chkFree.IsChecked == true, AddAgain = blnAgain };
                Close();
            }
            cmdOk.Click += (sender, args) => Accept(false);
            cmdAgain.Click += (sender, args) => Accept(true);
            lstChoices.DoubleTapped += (sender, args) => Accept(false);
            cmdCancel.Click += (sender, args) => Close();

            Grid grdFilters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            grdFilters.Children.Add(cboCategory);
            txtSearch.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(txtSearch, 1);
            grdFilters.Children.Add(txtSearch);
            StackPanel pnlFilters2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    chkAvailable, cboMetagenic,
                    new TextBlock { Text = "KARMA", Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) },
                    nudMin, new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center }, nudMax,
                }
            };
            Grid.SetRow(pnlFilters2, 1);
            Grid.SetColumnSpan(pnlFilters2, 2);
            grdFilters.Children.Add(pnlFilters2);

            Grid grdBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            grdBody.Children.Add(new StackPanel { Spacing = 4, Children = { lstChoices, txtCount } });
            ScrollViewer scrDetails = new ScrollViewer { Content = pnlDetails, MaxHeight = 470 };
            Grid.SetColumn(scrDetails, 1);
            grdBody.Children.Add(scrDetails);

            Content = Frame(new StackPanel
            {
                Spacing = 10,
                Children = { Heading(blnCareer ? "Add quality  //  career" : "Add quality"), grdFilters, grdBody, ButtonRow(cmdOk, cmdAgain, cmdCancel) }
            });
            Opened += (sender, args) => txtSearch.Focus();
            Rebuild();
        }

        private static IBrush Brush(string strKey)
            => Application.Current != null && Application.Current.TryGetResource(strKey, Application.Current.ActualThemeVariant, out object objBrush) && objBrush is IBrush objReturn
                ? objReturn
                : Brushes.White;

        /// <summary>Shows the picker; null when cancelled.</summary>
        public static async Task<QualityPick> ShowAsync(object owner, Character objCharacter)
        {
            QualityPickerWindow objWindow = new QualityPickerWindow(objCharacter);
            await DialogHost.ShowModalAsync(objWindow, owner).ConfigureAwait(true);
            return objWindow._objResult;
        }
    }
}
