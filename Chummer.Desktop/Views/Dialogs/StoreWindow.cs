/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Chummer.Desktop.Services;
using Chummer.Rules;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>One shop section the store window can switch between (e.g. Cyberware / Bioware).</summary>
    public sealed class StoreSection
    {
        public StoreSection(EquipmentKind eKind, string strTitle, object objParent = null)
        {
            Kind = eKind;
            Title = strTitle;
            Parent = objParent;
        }

        public EquipmentKind Kind { get; }
        public string Title { get; }
        public object Parent { get; }
    }

    /// <summary>
    /// The store: one reusable window for every legacy "Add ..." picker (SelectGear, SelectWeapon, SelectArmor,
    /// SelectCyberware, SelectVehicle, SelectDrug, SelectLifestyle, SelectWeaponAccessory, SelectArmorMod, SelectVehicleMod).
    /// Categories on the left, a sortable result list with kind-specific columns, details and purchase options on the right.
    /// Purchases go through <see cref="PurchaseRules"/>; the window stays open for "buy &amp; add another".
    /// </summary>
    public sealed class StoreWindow : Window
    {
        private static readonly IBrush s_brsText = new SolidColorBrush(Color.Parse("#FFD8E4F5"));
        private static readonly IBrush s_brsMuted = new SolidColorBrush(Color.Parse("#FF7C8CA6"));
        private static readonly IBrush s_brsCyan = new SolidColorBrush(Color.Parse("#FF00E5FF"));
        private static readonly IBrush s_brsMagenta = new SolidColorBrush(Color.Parse("#FFFF2E88"));
        private static readonly IBrush s_brsYellow = new SolidColorBrush(Color.Parse("#FFF3E600"));
        private static readonly IBrush s_brsGreen = new SolidColorBrush(Color.Parse("#FF39FF88"));
        private static readonly IBrush s_brsRed = new SolidColorBrush(Color.Parse("#FFFF3B4E"));
        private static readonly IBrush s_brsBorder = new SolidColorBrush(Color.Parse("#FF1F2C3D"));
        private static readonly IBrush s_brsPanel = new SolidColorBrush(Color.Parse("#FF101823"));
        private static readonly IBrush s_brsRaised = new SolidColorBrush(Color.Parse("#FF16212F"));
        private static readonly IBrush s_brsSurface = new SolidColorBrush(Color.Parse("#FF0C1118"));

        private readonly Character _objCharacter;
        private readonly Func<PurchaseResult, Task> _funcPurchased;
        private readonly Func<PurchaseRequest, Task<PurchaseResult>> _funcPurchase;
        private readonly IReadOnlyList<StoreSection> _lstSections;
        private readonly bool _blnCareer;
        private readonly string _strNuyenFormat;
        private readonly string _strNuyen;
        private readonly int _intMaxAvail;

        private StoreSection _objSection;
        private EquipmentCatalog _objCatalog;
        private List<CatalogRow> _lstRows = new List<CatalogRow>();
        private int _intSortColumn = -1;
        private bool _blnSortDescending;
        private int _intListVersion;
        private int _intQuoteVersion;
        private bool _blnSuppress;
        private ItemQuote _objQuote;
        private ItemOptions _objOptions;

        // Layout pieces
        private readonly TextBlock _txtTitle = new TextBlock { Classes = { "h2" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        private readonly TextBlock _txtNuyen = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold, Foreground = s_brsGreen, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _pnlSections = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly ListBox _lstCategories = new ListBox();
        private readonly TextBox _txtSearch = new TextBox { PlaceholderText = "Search the catalogue…" };
        private readonly CheckBox _chkAffordable = new CheckBox { Content = "Only what I can afford" };
        private readonly CheckBox _chkHideOverAvail = new CheckBox();
        private readonly Grid _grdHeader = new Grid { Margin = new Thickness(10, 0, 22, 0) };
        private readonly ListBox _lstItems = new ListBox();
        private readonly TextBlock _txtCount = new TextBlock { Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _txtHidden = new TextBlock { Classes = { "label" }, Foreground = s_brsYellow, VerticalAlignment = VerticalAlignment.Center };

        // Details
        private readonly TextBlock _txtItemName = new TextBlock { FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = s_brsText };
        private readonly TextBlock _txtItemCategory = new TextBlock { FontSize = 11, LetterSpacing = 1.5, Foreground = s_brsCyan };
        private readonly Grid _grdStats = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 4, ColumnSpacing = 14 };
        private readonly TextBlock _txtAvail = new TextBlock { FontWeight = FontWeight.Bold };
        private readonly TextBlock _txtAvailTest = new TextBlock { Foreground = s_brsMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _txtSource = new TextBlock { Foreground = s_brsMuted, FontSize = 12 };
        private readonly StackPanel _pnlDetails = new StackPanel { Spacing = 10 };
        private readonly TextBlock _txtEmpty = new TextBlock { Text = "Pick something from the list.", Foreground = s_brsMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };

        // Options
        private readonly Grid _grdOptions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6, ColumnSpacing = 12 };
        private readonly NumericUpDown _nudRating = new NumericUpDown { Minimum = 0, Maximum = 1, Value = 1, FormatString = "0", MinWidth = 120 };
        private readonly NumericUpDown _nudQuantity = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 1, MinWidth = 120 };
        private readonly ComboBox _cboGrade = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _cboMount = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _cboExtraMount = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly NumericUpDown _nudMarkup = new NumericUpDown { Minimum = -100, Maximum = 10000, Value = 0, FormatString = "0.##", Increment = 5, MinWidth = 120 };
        private readonly NumericUpDown _nudEssDiscount = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, FormatString = "0", MinWidth = 120 };
        private readonly NumericUpDown _nudUsedDiscount = new NumericUpDown { Minimum = 20, Maximum = 50, Value = 20, FormatString = "0.##", MinWidth = 120 };
        private readonly TextBox _txtLifestyleName = new TextBox();
        private readonly NumericUpDown _nudPercentage = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = 100, FormatString = "0.##", MinWidth = 120 };
        private readonly NumericUpDown _nudRoommates = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, FormatString = "0", MinWidth = 120 };
        private readonly CheckBox _chkFree = new CheckBox { Content = "Free (no cost)" };
        private readonly CheckBox _chkBlackMarket = new CheckBox { Content = "Black market discount (−10%)" };
        private readonly CheckBox _chkDoItYourself = new CheckBox { Content = "Do it yourself (½ price)" };
        private readonly CheckBox _chkStack = new CheckBox { Content = "Stack with identical item", IsChecked = true };
        private readonly CheckBox _chkUsed = new CheckBox { Content = "Used vehicle" };
        private readonly CheckBox _chkSplitRent = new CheckBox { Content = "Split rent with roommates" };
        private readonly List<(Control Label, Control Editor, Func<bool> Visible)> _lstOptionRows = new List<(Control, Control, Func<bool>)>();

        // Checkout
        private readonly TextBlock _txtPrice = new TextBlock { FontSize = 26, FontWeight = FontWeight.Bold, Foreground = s_brsCyan, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock _txtPriceLabel = new TextBlock { Text = "PRICE", Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _txtAfter = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock _txtStatus = new TextBlock { FontSize = 12, Foreground = s_brsGreen, TextWrapping = TextWrapping.Wrap };
        private readonly Button _cmdBuy = new Button { Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 10) };
        private readonly Button _cmdBuyAgain = new Button { Content = "BUY & ADD ANOTHER", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        private readonly Button _cmdClose = new Button { Content = "CLOSE", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };

        public int PurchaseCount { get; private set; }

        private StoreWindow(Character objCharacter, IReadOnlyList<StoreSection> lstSections, Func<PurchaseResult, Task> funcPurchased, Func<PurchaseRequest, Task<PurchaseResult>> funcPurchase = null)
        {
            _objCharacter = objCharacter;
            _lstSections = lstSections;
            _funcPurchased = funcPurchased;
            _funcPurchase = funcPurchase;
            _blnCareer = objCharacter.Created;
            _strNuyenFormat = objCharacter.Settings.NuyenFormat;
            _strNuyen = LanguageManager.GetString("String_NuyenSymbol");
            _intMaxAvail = objCharacter.Settings.MaximumAvailability;
            Title = "Chummer — Store";
            Width = 1360;
            Height = 820;
            MinWidth = 1040;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            _chkHideOverAvail.Content = "Hide items over availability " + _intMaxAvail.ToString(GlobalSettings.CultureInfo);
            _chkHideOverAvail.IsChecked = !_blnCareer && GlobalSettings.HideItemsOverAvailLimit;
            Content = BuildLayout();
            Wire();
            KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                    Close();
            };
        }

        /// <summary>Opens the store; <paramref name="funcPurchased"/> runs after every successful purchase.</summary>
        public static async Task<int> ShowAsync(object owner, Character objCharacter, IReadOnlyList<StoreSection> lstSections, Func<PurchaseResult, Task> funcPurchased, Func<PurchaseRequest, Task<PurchaseResult>> funcPurchase = null)
        {
            StoreWindow objWindow = Create(objCharacter, lstSections, funcPurchased, funcPurchase);
            Window objOwner = ModalHelper.ResolveOwner(owner);
            if (objOwner != null && objOwner.IsVisible)
                await objWindow.ShowDialog(objOwner).ConfigureAwait(true);
            else
            {
                TaskCompletionSource<bool> objClosed = new TaskCompletionSource<bool>();
                objWindow.Closed += (sender, args) => objClosed.TrySetResult(true);
                objWindow.Show();
                await objClosed.Task.ConfigureAwait(true);
            }
            return objWindow.PurchaseCount;
        }

        /// <summary>Builds the window without showing it (the screenshot harness shows it non-modally).</summary>
        public static StoreWindow Create(Character objCharacter, IReadOnlyList<StoreSection> lstSections, Func<PurchaseResult, Task> funcPurchased, Func<PurchaseRequest, Task<PurchaseResult>> funcPurchase = null)
        {
            StoreWindow objWindow = new StoreWindow(objCharacter, lstSections, funcPurchased, funcPurchase);
            objWindow.Opened += async (sender, args) =>
            {
                await objWindow.SwitchSectionAsync(lstSections[0]).ConfigureAwait(true);
                objWindow._txtSearch.Focus();
            };
            return objWindow;
        }

        /// <summary>Harness / scripting hooks.</summary>
        public Task OpenSectionAsync(int intIndex) => SwitchSectionAsync(_lstSections[intIndex]);

        public async Task SelectAsync(string strCategory, string strSearch, string strItemName)
        {
            _blnSuppress = true;
            if (!string.IsNullOrEmpty(strCategory))
                _lstCategories.SelectedItem = _lstCategories.ItemsSource?.OfType<ListItem>().FirstOrDefault(x => x.Value?.ToString() == strCategory);
            _txtSearch.Text = strSearch ?? string.Empty;
            _blnSuppress = false;
            await RefreshListAsync().ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strItemName))
                _lstItems.SelectedItem = _lstRows.FirstOrDefault(x => x.Name == strItemName) ?? _lstRows.FirstOrDefault(x => x.Name.StartsWith(strItemName, StringComparison.OrdinalIgnoreCase));
        }

        #region Layout

        private static TextBlock Label(string strText) => new TextBlock { Text = strText.ToUpperInvariant(), Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };

        private static Border Panel(Control objChild, bool blnLit = false, Thickness? objPadding = null) => new Border
        {
            Classes = { "panel" },
            BorderBrush = blnLit ? s_brsCyan : s_brsBorder,
            BorderThickness = blnLit ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            Padding = objPadding ?? new Thickness(12),
            Child = objChild,
        };

        private Control BuildLayout()
        {
            // Header: title, section switch, wallet
            Border brdWallet = new Border
            {
                Classes = { "chip" },
                Padding = new Thickness(12, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { Label(_blnCareer ? "Nuyen" : "Nuyen left (build)"), _txtNuyen }
                }
            };
            Grid grdHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
            StackPanel pnlTitle = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "STREET MARKET", FontSize = 24, FontWeight = FontWeight.Bold, LetterSpacing = 4, Foreground = s_brsText },
                    _txtTitle,
                }
            };
            grdHeader.Children.Add(pnlTitle);
            _pnlSections.Margin = new Thickness(28, 0, 0, 0);
            _pnlSections.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_pnlSections, 1);
            grdHeader.Children.Add(_pnlSections);
            Grid.SetColumn(brdWallet, 2);
            brdWallet.VerticalAlignment = VerticalAlignment.Center;
            grdHeader.Children.Add(brdWallet);

            // Left: categories
            _lstCategories.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            DockPanel pnlLeft = new DockPanel();
            TextBlock lblCategories = Label("Categories");
            lblCategories.Margin = new Thickness(4, 0, 0, 8);
            DockPanel.SetDock(lblCategories, Dock.Top);
            pnlLeft.Children.Add(lblCategories);
            pnlLeft.Children.Add(_lstCategories);

            // Middle: search, filters, header, list
            _lstItems.ItemTemplate = new FuncDataTemplate<CatalogRow>((x, _) => BuildRow(x), true);
            WrapPanel pnlFilters = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            _chkAffordable.Margin = new Thickness(0, 0, 18, 0);
            pnlFilters.Children.Add(_chkAffordable);
            if (!_blnCareer)
                pnlFilters.Children.Add(_chkHideOverAvail);
            Grid grdFooter = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 8, 4, 0) };
            grdFooter.Children.Add(_txtCount);
            Grid.SetColumn(_txtHidden, 1);
            grdFooter.Children.Add(_txtHidden);
            Border brdHeader = new Border { BorderBrush = s_brsBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = _grdHeader };
            DockPanel pnlMiddle = new DockPanel();
            DockPanel.SetDock(_txtSearch, Dock.Top);
            DockPanel.SetDock(pnlFilters, Dock.Top);
            DockPanel.SetDock(brdHeader, Dock.Top);
            DockPanel.SetDock(grdFooter, Dock.Bottom);
            pnlMiddle.Children.Add(_txtSearch);
            pnlMiddle.Children.Add(pnlFilters);
            pnlMiddle.Children.Add(brdHeader);
            pnlMiddle.Children.Add(grdFooter);
            pnlMiddle.Children.Add(_lstItems);

            // Right: details, options, checkout
            BuildOptions();
            Border brdAvail = new Border
            {
                Background = s_brsRaised,
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(2),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Label("Availability"), _txtAvail } },
                        _txtAvailTest,
                    }
                }
            };
            _pnlDetails.Children.Add(new StackPanel { Spacing = 4, Children = { _txtItemCategory, _txtItemName, _txtSource } });
            _pnlDetails.Children.Add(_grdStats);
            _pnlDetails.Children.Add(brdAvail);
            _pnlDetails.Children.Add(new Border { Height = 1, Background = s_brsBorder, Margin = new Thickness(0, 4) });
            _pnlDetails.Children.Add(Label("Options"));
            _pnlDetails.Children.Add(_grdOptions);
            _pnlDetails.IsVisible = false;

            Grid grdPrice = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grdPrice.Children.Add(_txtPriceLabel);
            Grid.SetColumn(_txtPrice, 1);
            grdPrice.Children.Add(_txtPrice);
            StackPanel pnlCheckout = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    grdPrice,
                    _txtAfter,
                    _cmdBuy,
                    _cmdBuyAgain,
                    _cmdClose,
                    _txtStatus,
                }
            };
            Border brdCheckout = new Border
            {
                BorderBrush = s_brsMagenta,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 12, 0, 0),
                Margin = new Thickness(0, 10, 0, 0),
                Child = pnlCheckout,
            };
            DockPanel pnlRight = new DockPanel();
            DockPanel.SetDock(brdCheckout, Dock.Bottom);
            pnlRight.Children.Add(brdCheckout);
            pnlRight.Children.Add(new ScrollViewer { Content = new Panel { Children = { _pnlDetails, _txtEmpty } }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

            Grid grdBody = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*,370"), ColumnSpacing = 12 };
            Border brdLeft = Panel(pnlLeft, false, new Thickness(8, 12));
            Border brdMiddle = Panel(pnlMiddle);
            Border brdRight = Panel(pnlRight, true, new Thickness(16, 14));
            Grid.SetColumn(brdMiddle, 1);
            Grid.SetColumn(brdRight, 2);
            grdBody.Children.Add(brdLeft);
            grdBody.Children.Add(brdMiddle);
            grdBody.Children.Add(brdRight);

            DockPanel pnlRoot = new DockPanel { Margin = new Thickness(18, 14, 18, 18) };
            DockPanel.SetDock(grdHeader, Dock.Top);
            pnlRoot.Children.Add(grdHeader);
            pnlRoot.Children.Add(grdBody);
            return new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#FF0A0F16"), 0), new GradientStop(Color.Parse("#FF07090D"), 1) }
                },
                BorderBrush = s_brsBorder,
                BorderThickness = new Thickness(0, 2, 0, 0),
                Child = pnlRoot,
            };
        }

        private void AddOption(string strLabel, Control objEditor, Func<bool> funcVisible)
        {
            Control objLabel = strLabel == null ? (Control)new Panel() : Label(strLabel);
            int intRow = _grdOptions.RowDefinitions.Count;
            _grdOptions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(objLabel, intRow);
            Grid.SetRow(objEditor, intRow);
            if (strLabel == null)
                Grid.SetColumnSpan(objEditor, 2);
            else
                Grid.SetColumn(objEditor, 1);
            _grdOptions.Children.Add(objLabel);
            _grdOptions.Children.Add(objEditor);
            _lstOptionRows.Add((objLabel, objEditor, funcVisible));
        }

        private bool Is(params EquipmentKind[] aeKinds) => _objSection != null && aeKinds.Contains(_objSection.Kind);

        private void BuildOptions()
        {
            AddOption("Rating", _nudRating, () => _objOptions != null && _objOptions.MaxRating > 0 && (_objOptions.MaxRating > 1 || _objOptions.MinRating != _objOptions.MaxRating));
            AddOption("Quantity", _nudQuantity, () => _objOptions?.HasQuantity == true);
            AddOption("Grade", _cboGrade, () => _objCatalog?.HasGrades == true);
            AddOption("Mount", _cboMount, () => Is(EquipmentKind.WeaponAccessory) && _cboMount.ItemCount > 0);
            AddOption("Extra mount", _cboExtraMount, () => Is(EquipmentKind.WeaponAccessory) && _cboExtraMount.ItemCount > 1);
            AddOption("Essence discount %", _nudEssDiscount, () => _objCatalog?.IsWare == true && _objCharacter.Settings.AllowCyberwareESSDiscounts);
            AddOption("Lifestyle name", _txtLifestyleName, () => Is(EquipmentKind.Lifestyle));
            AddOption("Percentage %", _nudPercentage, () => Is(EquipmentKind.Lifestyle));
            AddOption("Roommates", _nudRoommates, () => Is(EquipmentKind.Lifestyle));
            AddOption(null, _chkSplitRent, () => Is(EquipmentKind.Lifestyle));
            AddOption("Street markup %", _nudMarkup, () => _blnCareer && !Is(EquipmentKind.Lifestyle));
            AddOption("Used discount %", _nudUsedDiscount, () => Is(EquipmentKind.Vehicle) && _chkUsed.IsChecked == true);
            AddOption(null, _chkUsed, () => Is(EquipmentKind.Vehicle));
            AddOption(null, _chkDoItYourself, () => _objOptions?.CanDoItYourself == true);
            AddOption(null, _chkStack, () => _blnCareer && Is(EquipmentKind.Gear, EquipmentKind.GearPlugin));
            AddOption(null, _chkBlackMarket, () => _objCatalog?.CanBlackMarket == true && !Is(EquipmentKind.Lifestyle));
            AddOption(null, _chkFree, () => !Is(EquipmentKind.Lifestyle));
        }

        private void UpdateOptionVisibility()
        {
            foreach ((Control objLabel, Control objEditor, Func<bool> funcVisible) in _lstOptionRows)
            {
                bool blnVisible = funcVisible();
                objLabel.IsVisible = blnVisible;
                objEditor.IsVisible = blnVisible;
            }
        }

        private IBrush AvailBrush(int intValue, char chrSuffix)
        {
            if (chrSuffix == 'F')
                return s_brsRed;
            if (intValue > _intMaxAvail)
                return s_brsMagenta;
            if (chrSuffix == 'R')
                return s_brsYellow;
            return s_brsGreen;
        }

        private string ColumnDefinitionString()
        {
            List<string> lstColumns = new List<string> { "*" };
            if (_objCatalog != null)
                lstColumns.AddRange(_objCatalog.Columns.Select(x => x.Width.ToString(CultureInfo.InvariantCulture)));
            if (_objSection?.Kind != EquipmentKind.Lifestyle)
                lstColumns.Add("56");
            lstColumns.Add("110");
            return string.Join(",", lstColumns);
        }

        private Control BuildRow(CatalogRow objRow)
        {
            Grid grdRow = new Grid { ColumnDefinitions = new ColumnDefinitions(ColumnDefinitionString()), ColumnSpacing = 6 };
            StackPanel pnlName = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            pnlName.Children.Add(new TextBlock { Text = objRow.Name, Foreground = objRow.Unaffordable ? s_brsMuted : s_brsText, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            pnlName.Children.Add(new TextBlock { Text = objRow.CategoryDisplay + "  ·  " + objRow.Source, Foreground = s_brsMuted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            grdRow.Children.Add(pnlName);
            int intColumn = 1;
            foreach (string strStat in objRow.Stats)
            {
                TextBlock txtStat = new TextBlock { Text = strStat, Foreground = s_brsText, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(txtStat, strStat);
                Grid.SetColumn(txtStat, intColumn++);
                grdRow.Children.Add(txtStat);
            }
            if (_objSection?.Kind != EquipmentKind.Lifestyle)
            {
                TextBlock txtAvail = new TextBlock
                {
                    Text = string.IsNullOrEmpty(objRow.Avail) ? "–" : objRow.Avail,
                    Foreground = AvailBrush(objRow.AvailValue, objRow.AvailSuffix),
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(txtAvail, intColumn++);
                grdRow.Children.Add(txtAvail);
            }
            TextBlock txtCost = new TextBlock
            {
                Text = objRow.CostText,
                Foreground = objRow.Unaffordable ? s_brsRed : s_brsCyan,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(txtCost, intColumn);
            grdRow.Children.Add(txtCost);
            return grdRow;
        }

        private void BuildHeader()
        {
            _grdHeader.Children.Clear();
            _grdHeader.ColumnDefinitions = new ColumnDefinitions(ColumnDefinitionString());
            _grdHeader.ColumnSpacing = 6;
            List<string> lstHeaders = new List<string> { "ITEM" };
            lstHeaders.AddRange(_objCatalog.Columns.Select(x => x.Header));
            if (_objSection.Kind != EquipmentKind.Lifestyle)
                lstHeaders.Add("AVAIL");
            lstHeaders.Add(_objSection.Kind == EquipmentKind.Lifestyle ? "RENT" : "COST");
            for (int i = 0; i < lstHeaders.Count; ++i)
            {
                int intSortKey = i - 1; // -1 = name
                bool blnActive = _intSortColumn == intSortKey;
                Button cmdHeader = new Button
                {
                    Content = lstHeaders[i] + (blnActive ? _blnSortDescending ? " ▼" : " ▲" : string.Empty),
                    Classes = { "ghost" },
                    Padding = new Thickness(0, 2),
                    FontSize = 11,
                    LetterSpacing = 1.5,
                    Foreground = blnActive ? s_brsCyan : s_brsMuted,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == lstHeaders.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                };
                cmdHeader.Click += (sender, args) =>
                {
                    if (_intSortColumn == intSortKey)
                        _blnSortDescending = !_blnSortDescending;
                    else
                    {
                        _intSortColumn = intSortKey;
                        _blnSortDescending = false;
                    }
                    BuildHeader();
                    ApplySort();
                };
                Grid.SetColumn(cmdHeader, i);
                _grdHeader.Children.Add(cmdHeader);
            }
        }

        #endregion

        #region Behaviour

        private void Wire()
        {
            _lstCategories.SelectionChanged += async (sender, args) =>
            {
                if (!_blnSuppress)
                    await RefreshListAsync().ConfigureAwait(true);
            };
            DispatcherTimer tmrSearch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            tmrSearch.Tick += async (sender, args) =>
            {
                tmrSearch.Stop();
                await RefreshListAsync().ConfigureAwait(true);
            };
            _txtSearch.TextChanged += (sender, args) =>
            {
                if (_blnSuppress)
                    return;
                tmrSearch.Stop();
                tmrSearch.Start();
            };
            _chkAffordable.IsCheckedChanged += async (sender, args) => await RefreshListAsync().ConfigureAwait(true);
            _chkHideOverAvail.IsCheckedChanged += async (sender, args) => await RefreshListAsync().ConfigureAwait(true);
            _lstItems.SelectionChanged += async (sender, args) => await SelectionChangedAsync().ConfigureAwait(true);
            _lstItems.DoubleTapped += async (sender, args) => await BuyAsync(false).ConfigureAwait(true);
            _cboGrade.SelectionChanged += async (sender, args) =>
            {
                if (_blnSuppress)
                    return;
                await RefreshListAsync().ConfigureAwait(true);
            };
            foreach (NumericUpDown nudLoop in new[] { _nudRating, _nudQuantity, _nudMarkup, _nudEssDiscount, _nudUsedDiscount, _nudPercentage, _nudRoommates })
                nudLoop.ValueChanged += async (sender, args) => await RequoteAsync().ConfigureAwait(true);
            foreach (CheckBox chkLoop in new[] { _chkFree, _chkBlackMarket, _chkDoItYourself, _chkUsed })
                chkLoop.IsCheckedChanged += async (sender, args) =>
                {
                    UpdateOptionVisibility();
                    await RequoteAsync().ConfigureAwait(true);
                };
            _cboMount.SelectionChanged += async (sender, args) => await RequoteAsync().ConfigureAwait(true);
            _cmdBuy.Click += async (sender, args) => await BuyAsync(false).ConfigureAwait(true);
            _cmdBuyAgain.Click += async (sender, args) => await BuyAsync(true).ConfigureAwait(true);
            _cmdClose.Click += (sender, args) => Close();
        }

        private void UpdateWallet()
        {
            decimal decNuyen = _objCharacter.GetAvailableNuyen();
            _txtNuyen.Text = decNuyen.ToString(_strNuyenFormat, GlobalSettings.CultureInfo) + _strNuyen;
            _txtNuyen.Foreground = decNuyen < 0 ? s_brsRed : s_brsGreen;
        }

        private async Task SwitchSectionAsync(StoreSection objSection)
        {
            _objSection = objSection;
            _txtTitle.Text = objSection.Title.ToUpperInvariant() + (objSection.Parent is IHasName objParent ? "  ·  FOR " + objParent.CurrentDisplayName.ToUpperInvariant() : string.Empty);
            Title = "Store — " + objSection.Title;
            _pnlSections.Children.Clear();
            if (_lstSections.Count > 1)
            {
                foreach (StoreSection objLoop in _lstSections)
                {
                    Button cmdSection = new Button { Content = objLoop.Title.ToUpperInvariant() };
                    if (objLoop == objSection)
                        cmdSection.Classes.Add("primary");
                    cmdSection.Click += async (sender, args) => await SwitchSectionAsync(objLoop).ConfigureAwait(true);
                    _pnlSections.Children.Add(cmdSection);
                }
            }
            UpdateWallet();
            (EquipmentCatalog objCatalog, List<ListItem> lstCategories, List<ListItem> lstGrades) = await Task.Run(() =>
            {
                EquipmentCatalog objNew = new EquipmentCatalog(_objCharacter, objSection.Kind, objSection.Parent);
                return (objNew, objNew.GetCategories(), objNew.GetGrades());
            }).ConfigureAwait(true);
            _objCatalog = objCatalog;
            _intSortColumn = -1;
            _blnSortDescending = false;
            _blnSuppress = true;
            _lstCategories.ItemsSource = lstCategories;
            _lstCategories.SelectedIndex = lstCategories.Count > 0 ? 0 : -1;
            _lstCategories.IsVisible = true;
            _cboGrade.ItemsSource = lstGrades;
            _cboGrade.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            // Legacy default grade: the first one offered ("Standard").
            _cboGrade.SelectedIndex = lstGrades.Count > 0 ? 0 : -1;
            _txtSearch.Text = string.Empty;
            _blnSuppress = false;
            BuildHeader();
            await RefreshListAsync().ConfigureAwait(true);
        }

        private CatalogQuery CurrentQuery() => new CatalogQuery
        {
            Category = !GlobalSettings.SearchInCategoryOnly && !string.IsNullOrWhiteSpace(_txtSearch.Text)
                ? string.Empty : (_lstCategories.SelectedItem as ListItem?)?.Value?.ToString() ?? string.Empty,
            Search = _txtSearch.Text ?? string.Empty,
            OnlyAffordable = _chkAffordable.IsChecked == true,
            HideOverAvailLimit = _chkHideOverAvail.IsChecked == true,
            Free = _chkFree.IsChecked == true,
            Markup = _nudMarkup.Value ?? 0,
            BlackMarket = _chkBlackMarket.IsChecked == true,
            GradeId = (_cboGrade.SelectedItem as ListItem?)?.Value?.ToString() ?? string.Empty,
        };

        private async Task RefreshListAsync()
        {
            if (_objCatalog == null)
                return;
            int intVersion = Interlocked.Increment(ref _intListVersion);
            EquipmentCatalog objCatalog = _objCatalog;
            CatalogQuery objQuery = CurrentQuery();
            string strOldSelected = (_lstItems.SelectedItem as CatalogRow)?.Id;
            _txtCount.Text = "Searching…";
            CatalogListing objListing;
            try
            {
                objListing = await Task.Run(() => objCatalog.GetListing(objQuery)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _txtCount.Text = "Could not list items: " + ex.Message;
                return;
            }
            if (intVersion != _intListVersion)
                return;
            _lstRows = objListing.Rows;
            _txtHidden.Text = objListing.HiddenText;
            _txtCount.Text = _lstRows.Count.ToString(GlobalSettings.CultureInfo) + (_lstRows.Count == 1 ? " item" : " items");
            ApplySort(strOldSelected);
        }

        private static decimal? Numeric(string strValue)
        {
            if (string.IsNullOrEmpty(strValue))
                return null;
            int intEnd = 0;
            if (intEnd < strValue.Length && (strValue[0] == '-' || strValue[0] == '+' || strValue[0] == '['))
                ++intEnd;
            while (intEnd < strValue.Length && (char.IsDigit(strValue[intEnd]) || strValue[intEnd] == '.'))
                ++intEnd;
            string strNumber = strValue.Substring(0, intEnd).Trim('[', '+');
            return decimal.TryParse(strNumber, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decValue) ? decValue : (decimal?)null;
        }

        private void ApplySort(string strSelectId = null)
        {
            if (strSelectId == null)
                strSelectId = (_lstItems.SelectedItem as CatalogRow)?.Id;
            IEnumerable<CatalogRow> lstSorted;
            int intStats = _objCatalog?.Columns.Count ?? 0;
            bool blnHasAvail = _objSection?.Kind != EquipmentKind.Lifestyle;
            if (_intSortColumn < 0)
                lstSorted = _lstRows.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
            else if (_intSortColumn < intStats)
            {
                int intIndex = _intSortColumn;
                lstSorted = _lstRows.OrderBy(x => Numeric(x.Stats.ElementAtOrDefault(intIndex)) ?? decimal.MinValue)
                                    .ThenBy(x => x.Stats.ElementAtOrDefault(intIndex), StringComparer.CurrentCultureIgnoreCase);
            }
            else if (_intSortColumn == intStats && blnHasAvail)
                lstSorted = _lstRows.OrderBy(x => x.AvailValue).ThenBy(x => x.AvailSuffix);
            else
                lstSorted = _lstRows.OrderBy(x => x.Cost);
            List<CatalogRow> lstRows = lstSorted.ToList();
            if (_blnSortDescending)
                lstRows.Reverse();
            _lstItems.ItemsSource = lstRows;
            if (!string.IsNullOrEmpty(strSelectId))
                _lstItems.SelectedItem = lstRows.FirstOrDefault(x => x.Id == strSelectId);
        }

        private async Task SelectionChangedAsync()
        {
            if (!(_lstItems.SelectedItem is CatalogRow objRow) || _objCatalog == null)
            {
                _pnlDetails.IsVisible = false;
                _txtEmpty.IsVisible = true;
                _objQuote = null;
                UpdateCheckout();
                return;
            }
            EquipmentCatalog objCatalog = _objCatalog;
            ItemOptions objOptions = await Task.Run(() => objCatalog.GetOptions(objRow.Id)).ConfigureAwait(true);
            if (!ReferenceEquals(_lstItems.SelectedItem, objRow))
                return;
            _objOptions = objOptions;
            _blnSuppress = true;
            try
            {
                int intOldRating = (int)(_nudRating.Value ?? 1);
                _nudRating.Minimum = objOptions.MinRating;
                _nudRating.Maximum = Math.Max(objOptions.MinRating, objOptions.MaxRating);
                _nudRating.Value = Math.Max(objOptions.MinRating, Math.Min(objOptions.MaxRating, Math.Max(intOldRating, objOptions.MinRating)));
                _nudRating.IsEnabled = objOptions.MaxRating != objOptions.MinRating;
                _nudQuantity.Minimum = objOptions.QuantityMinimum;
                _nudQuantity.Increment = objOptions.QuantityStep;
                _nudQuantity.FormatString = objOptions.QuantityDecimals > 0 ? "0." + new string('0', objOptions.QuantityDecimals) : "0";
                _nudQuantity.Value = objOptions.QuantityStep > 1 ? objOptions.QuantityStep : Math.Max(objOptions.QuantityMinimum, 1);
                _chkBlackMarket.IsEnabled = objOptions.CanBlackMarket;
                // Legacy: pre-ticked when "assume black market" is set and the category qualifies; never ticked when it doesn't.
                if (!objOptions.CanBlackMarket)
                    _chkBlackMarket.IsChecked = false;
                else if (_chkBlackMarket.IsChecked != true)
                    _chkBlackMarket.IsChecked = GlobalSettings.AssumeBlackMarket;
                if (!objOptions.CanDoItYourself)
                    _chkDoItYourself.IsChecked = false;
                _cboMount.ItemsSource = objOptions.Mounts;
                _cboMount.SelectedIndex = objOptions.Mounts.Count > 0 ? 0 : -1;
                _cboExtraMount.ItemsSource = objOptions.ExtraMounts;
                int intExtra = 0;
                if (objOptions.ExtraMounts.Count > 1 && objOptions.Mounts.Count > 0 && objOptions.Mounts[0] != "None" && objOptions.ExtraMounts[0] == objOptions.Mounts[0])
                    intExtra = 1;
                _cboExtraMount.SelectedIndex = objOptions.ExtraMounts.Count > 0 ? intExtra : -1;
                if (_objSection.Kind == EquipmentKind.Lifestyle)
                    _txtLifestyleName.Text = objRow.Name;
                _txtLifestyleName.IsEnabled = true;
            }
            finally
            {
                _blnSuppress = false;
            }
            _txtItemName.Text = objRow.Name;
            _txtItemCategory.Text = (objRow.CategoryDisplay ?? string.Empty).ToUpperInvariant();
            _pnlDetails.IsVisible = true;
            _txtEmpty.IsVisible = false;
            UpdateOptionVisibility();
            await RequoteAsync().ConfigureAwait(true);
        }

        private PurchaseRequest CurrentRequest()
        {
            if (!(_lstItems.SelectedItem is CatalogRow objRow) || _objSection == null)
                return null;
            return new PurchaseRequest
            {
                Kind = _objSection.Kind,
                ItemId = objRow.Id,
                Parent = _objSection.Parent,
                Rating = _objOptions != null && _objOptions.MaxRating > 0 ? (int)(_nudRating.Value ?? 0) : 0,
                Quantity = _objOptions?.HasQuantity == true ? _nudQuantity.Value ?? 1 : 1,
                Free = _chkFree.IsChecked == true,
                BlackMarket = _chkBlackMarket.IsChecked == true && _chkBlackMarket.IsEnabled && _objCatalog?.CanBlackMarket == true,
                Markup = _blnCareer ? _nudMarkup.Value ?? 0 : 0,
                GradeId = (_cboGrade.SelectedItem as ListItem?)?.Value?.ToString() ?? string.Empty,
                DoItYourself = _chkDoItYourself.IsChecked == true,
                Stack = _chkStack.IsChecked == true,
                EssenceDiscount = (int)(_nudEssDiscount.Value ?? 0),
                Mount = _cboMount.SelectedItem as string ?? string.Empty,
                ExtraMount = _cboExtraMount.SelectedItem as string ?? string.Empty,
                UsedVehicle = _chkUsed.IsChecked == true,
                UsedVehicleDiscount = _nudUsedDiscount.Value ?? 20,
                LifestyleName = _txtLifestyleName.Text ?? string.Empty,
                Percentage = _nudPercentage.Value ?? 100,
                Roommates = (int)(_nudRoommates.Value ?? 0),
                SplitCostWithRoommates = _chkSplitRent.IsChecked == true,
            };
        }

        private async Task RequoteAsync()
        {
            if (_blnSuppress)
                return;
            PurchaseRequest objRequest = CurrentRequest();
            if (objRequest == null || _objCatalog == null)
                return;
            int intVersion = Interlocked.Increment(ref _intQuoteVersion);
            EquipmentCatalog objCatalog = _objCatalog;
            ItemQuote objQuote;
            try
            {
                objQuote = await Task.Run(() => objCatalog.QuoteAsync(objRequest)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _txtStatus.Foreground = s_brsRed;
                _txtStatus.Text = "Could not price this item: " + ex.Message;
                return;
            }
            if (intVersion != _intQuoteVersion)
                return;
            _objQuote = objQuote;
            _grdStats.Children.Clear();
            _grdStats.RowDefinitions.Clear();
            foreach (KeyValuePair<string, string> kvpStat in objQuote.Stats)
            {
                int intRow = _grdStats.RowDefinitions.Count;
                _grdStats.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TextBlock txtLabel = Label(kvpStat.Key);
                TextBlock txtValue = new TextBlock { Text = kvpStat.Value, Foreground = s_brsText, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(txtLabel, intRow);
                Grid.SetRow(txtValue, intRow);
                Grid.SetColumn(txtValue, 1);
                _grdStats.Children.Add(txtLabel);
                _grdStats.Children.Add(txtValue);
            }
            _txtSource.Text = string.IsNullOrEmpty(objQuote.Source) ? string.Empty : "SOURCE  " + objQuote.Source;
            bool blnLifestyle = _objSection.Kind == EquipmentKind.Lifestyle;
            _txtAvail.Text = blnLifestyle ? "–" : objQuote.Avail;
            _txtAvail.Foreground = AvailBrush(objQuote.AvailValue, objQuote.AvailSuffix);
            string strTest = objQuote.AvailTest;
            _txtAvailTest.Text = blnLifestyle ? string.Empty
                : (objQuote.AvailValue > _intMaxAvail ? "Above the campaign limit of " + _intMaxAvail.ToString(GlobalSettings.CultureInfo) + ". " : string.Empty)
                  + (string.IsNullOrEmpty(strTest) ? string.Empty : "Availability test: " + strTest);
            _txtAvailTest.IsVisible = _txtAvailTest.Text.Length > 0;
            UpdateCheckout();
        }

        private void UpdateCheckout()
        {
            UpdateWallet();
            bool blnHasItem = _objQuote != null && _lstItems.SelectedItem is CatalogRow;
            _cmdBuy.IsEnabled = blnHasItem;
            _cmdBuyAgain.IsEnabled = blnHasItem;
            if (!blnHasItem)
            {
                _txtPrice.Text = "—";
                _txtAfter.Text = string.Empty;
                _cmdBuy.Content = _objSection?.Kind == EquipmentKind.Lifestyle ? "ADD LIFESTYLE" : "BUY";
                return;
            }
            bool blnLifestyle = _objSection.Kind == EquipmentKind.Lifestyle;
            if (blnLifestyle)
            {
                _txtPriceLabel.Text = "RENT / MONTH";
                _txtPrice.Text = _objQuote.PriceText;
                _txtAfter.Text = _blnCareer ? "Nothing is paid now — pay rent month by month from the lifestyle." : "Counts against your starting nuyen.";
                _txtAfter.Foreground = s_brsMuted;
                _cmdBuy.Content = "ADD LIFESTYLE";
                _cmdBuyAgain.Content = "ADD & ADD ANOTHER";
                return;
            }
            _txtPriceLabel.Text = _blnCareer ? "PRICE" : "COST (BUILD)";
            _txtPrice.Text = _objQuote.PriceText;
            _txtPrice.Foreground = _objQuote.CanAfford ? s_brsCyan : s_brsRed;
            decimal decAfter = _objQuote.NuyenAfter;
            _txtAfter.Text = (_objQuote.IsVariable ? "Price is variable — you'll be asked for the exact amount. " : string.Empty)
                             + "Nuyen left after purchase: " + decAfter.ToString(_strNuyenFormat, GlobalSettings.CultureInfo) + _strNuyen;
            _txtAfter.Foreground = decAfter < 0 ? s_brsRed : s_brsMuted;
            _cmdBuy.Content = "BUY  ·  " + _objQuote.PriceText;
            _cmdBuyAgain.Content = "BUY & ADD ANOTHER";
        }

        private async Task BuyAsync(bool blnAddAnother)
        {
            PurchaseRequest objRequest = CurrentRequest();
            if (objRequest == null)
                return;
            string strName = (_lstItems.SelectedItem as CatalogRow)?.Name ?? string.Empty;
            _cmdBuy.IsEnabled = false;
            _cmdBuyAgain.IsEnabled = false;
            PurchaseResult objResult;
            try
            {
                objResult = _funcPurchase != null ? await _funcPurchase(objRequest).ConfigureAwait(true)
                    : await Task.Run(() => PurchaseRules.PurchaseAsync(_objCharacter, objRequest)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                objResult = new PurchaseResult(RuleResult.Cancelled);
            }
            catch (Exception ex)
            {
                await MessageBoxes.ShowAsync(ex.Message, "Purchase failed", MessageButtons.OK, MessageIcon.Error).ConfigureAwait(true);
                objResult = new PurchaseResult(RuleResult.NotAllowed);
            }
            if (objResult.Succeeded)
            {
                ++PurchaseCount;
                if (_funcPurchased != null)
                    await _funcPurchased(objResult).ConfigureAwait(true);
                _txtStatus.Foreground = s_brsGreen;
                _txtStatus.Text = "✔ " + strName + (objResult.Cost > 0 ? " — paid " + objResult.Cost.ToString(_strNuyenFormat, GlobalSettings.CultureInfo) + _strNuyen : " added");
                if (!blnAddAnother)
                {
                    Close();
                    return;
                }
                // Prices and "affordable" flags change with the wallet.
                await RefreshListAsync().ConfigureAwait(true);
                await RequoteAsync().ConfigureAwait(true);
            }
            else
            {
                _txtStatus.Foreground = s_brsRed;
                switch (objResult.Result)
                {
                    case RuleResult.NotEnoughNuyen:
                        _txtStatus.Text = "✖ Not enough nuyen for " + strName + ".";
                        break;
                    case RuleResult.Cancelled:
                        _txtStatus.Text = "Purchase cancelled.";
                        break;
                    default:
                        _txtStatus.Text = "✖ " + strName + " could not be added.";
                        break;
                }
            }
            UpdateCheckout();
        }

        #endregion
    }
}
