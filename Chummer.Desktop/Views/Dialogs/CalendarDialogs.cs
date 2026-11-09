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

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Native SelectCalendarStart: year and ISO week, with the dates that week spans.</summary>
    public sealed class CalendarStartDialog : LedgerDialogBase
    {
        private readonly NumericUpDown _nudYear;
        private readonly NumericUpDown _nudWeek;
        private readonly TextBlock _txtSpan;

        public (int Year, int Week)? Result { get; private set; }

        public CalendarStartDialog(string strTitle, int intYear, int intWeek) : base(strTitle, 480)
        {
            _nudYear = new NumericUpDown { Minimum = 2, Maximum = 9998, Value = intYear, Increment = 1, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            _nudWeek = new NumericUpDown { Minimum = 1, Maximum = CalendarRules.WeeksInYear(intYear), Value = intWeek, Increment = 1, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            _txtSpan = new TextBlock { Foreground = (IBrush)Application.Current?.FindResource("CyanBrush") ?? Brushes.Cyan, TextWrapping = TextWrapping.Wrap };
            _nudYear.ValueChanged += (sender, args) =>
            {
                _nudWeek.Maximum = CalendarRules.WeeksInYear((int)(_nudYear.Value ?? 2));
                UpdateSpan();
            };
            _nudWeek.ValueChanged += (sender, args) => UpdateSpan();

            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" }, MinWidth = 90 };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true, MinWidth = 90 };
            cmdOk.Click += (sender, args) =>
            {
                Result = ((int)(_nudYear.Value ?? intYear), (int)(_nudWeek.Value ?? intWeek));
                Close();
            };
            cmdCancel.Click += (sender, args) => Close();
            Content = Frame(new StackPanel
            {
                Children =
                {
                    Heading(strTitle),
                    new TextBlock { Text = Tr.Get("Label_CalendarStart"), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
                    Fields((Tr.Get("Label_Year"), _nudYear), (Tr.Get("Label_Week"), _nudWeek)),
                    new TextBlock { Text = Tr.Get("Label_WeekSpansFollowingDates"), Classes = { "label" } },
                    _txtSpan,
                    ButtonRow(cmdOk, cmdCancel)
                }
            });
            UpdateSpan();
        }

        private void UpdateSpan() => _txtSpan.Text = CalendarRules.DateSpan((int)(_nudYear.Value ?? 2), (int)(_nudWeek.Value ?? 1));
    }

    /// <summary>Native EditNotes for a calendar week: free text plus the colour the notes are shown in.</summary>
    public sealed class WeekNotesDialog : LedgerDialogBase
    {
        private readonly TextBox _txtNotes;
        private System.Drawing.Color _objColor;
        private readonly List<Border> _lstSwatches = new List<Border>();

        public (string Notes, System.Drawing.Color Color)? Result { get; private set; }

        public WeekNotesDialog(string strTitle, string strNotes, System.Drawing.Color objColor) : base(strTitle, 600)
        {
            _objColor = objColor;
            _txtNotes = new TextBox
            {
                Text = strNotes ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 220,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            StackPanel pnlColors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            pnlColors.Children.Add(new TextBlock { Text = "COLOUR", Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            List<System.Drawing.Color> lstColors = new List<System.Drawing.Color>
            {
                ColorManager.HasNotesColorLight,
                System.Drawing.Color.FromArgb(0xD8, 0xE4, 0xF5),
                System.Drawing.Color.FromArgb(0x00, 0xE5, 0xFF),
                System.Drawing.Color.FromArgb(0xFF, 0x2E, 0x88),
                System.Drawing.Color.FromArgb(0xF3, 0xE6, 0x00),
                System.Drawing.Color.FromArgb(0x39, 0xFF, 0x88),
                System.Drawing.Color.FromArgb(0xFF, 0x3B, 0x4E)
            };
            if (objColor.A > 0 && !lstColors.Exists(x => x.ToArgb() == objColor.ToArgb()))
                lstColors.Insert(0, objColor);
            foreach (System.Drawing.Color objSwatchColor in lstColors)
            {
                Border objSwatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromRgb(objSwatchColor.R, objSwatchColor.G, objSwatchColor.B)),
                    BorderThickness = new Thickness(2),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Tag = objSwatchColor
                };
                objSwatch.PointerPressed += (sender, args) =>
                {
                    _objColor = objSwatchColor;
                    UpdateSwatches();
                };
                _lstSwatches.Add(objSwatch);
                pnlColors.Children.Add(objSwatch);
            }
            UpdateSwatches();

            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = false, Classes = { "primary" }, MinWidth = 90 };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true, MinWidth = 90 };
            cmdOk.Click += (sender, args) =>
            {
                Result = (_txtNotes.Text ?? string.Empty, _objColor);
                Close();
            };
            cmdCancel.Click += (sender, args) => Close();
            Content = Frame(new StackPanel { Children = { Heading(strTitle), _txtNotes, pnlColors, ButtonRow(cmdOk, cmdCancel) } });
            Opened += (sender, args) => _txtNotes.Focus();
        }

        private void UpdateSwatches()
        {
            foreach (Border objSwatch in _lstSwatches)
                objSwatch.BorderBrush = objSwatch.Tag is System.Drawing.Color objColor && objColor.ToArgb() == _objColor.ToArgb()
                    ? Brushes.White
                    : Brushes.Transparent;
        }
    }
}
