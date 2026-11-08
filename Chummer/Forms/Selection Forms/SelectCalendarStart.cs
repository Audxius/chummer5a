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
using System.Windows.Forms;

namespace Chummer
{
    public partial class SelectCalendarStart : Form
    {
        private readonly string _strCachedDateSpanFormat;
        private int _intSelectedYear = DateTime.UtcNow.Year + 62;
        private int _intSelectedWeek = 1;

        #region Control Events

        public SelectCalendarStart()
        {
            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            this.UpdateParentForToolTipControls();

            _strCachedDateSpanFormat = LanguageManager.GetString("String_DateSpan");
            nudYear.Value = _intSelectedYear;
            nudWeek.Value = _intSelectedWeek;
            nudWeek.Maximum = _intSelectedYear.IsYearLongYear() ? 53 : 52;

            nudYear.ValueChanged += nudYear_ValueChanged;
            nudWeek.ValueChanged += UpdateDateSpan;
        }

        public SelectCalendarStart(CalendarWeek objWeek)
        {
            if (objWeek == null)
                throw new ArgumentNullException(nameof(objWeek));
            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            this.UpdateParentForToolTipControls();

            _strCachedDateSpanFormat = LanguageManager.GetString("String_DateSpan");
            nudYear.Value = objWeek.Year;
            nudWeek.Value = objWeek.Week;
            nudWeek.Maximum = objWeek.IsLongYear ? 53 : 52;

            nudYear.ValueChanged += nudYear_ValueChanged;
            nudWeek.ValueChanged += UpdateDateSpan;
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        #endregion Control Events

        #region Methods

        /// <summary>
        /// Accept the selected values and close the form.
        /// </summary>
        private void AcceptForm()
        {
            _intSelectedYear = nudYear.ValueAsInt;
            _intSelectedWeek = nudWeek.ValueAsInt;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void nudYear_ValueChanged(object sender, EventArgs e)
        {
            nudWeek.Maximum = nudYear.ValueAsInt.IsYearLongYear() ? 53 : 52;
            UpdateDateSpan(sender, e);
        }


        private void UpdateDateSpan(object sender, EventArgs e)
        {
            string strFormat = _strCachedDateSpanFormat;
            int intYear = nudYear.ValueAsInt;
            int intWeek = nudWeek.ValueAsInt;
            // ISO 8601: week 1 is the week (Monday to Sunday) that contains January 4th.
            // The previous hand-rolled month/day arithmetic could produce invalid dates and crash (issue #5259).
            if (intYear < 2 || intYear > 9998 || intWeek < 1)
            {
                lblDateSpan.Text = string.Empty;
                return;
            }
            DateTime datJan4 = new DateTime(intYear, 1, 4);
            int intDaysSinceMonday = ((int)datJan4.DayOfWeek + 6) % 7;
            DateTime datStart = datJan4.AddDays(-intDaysSinceMonday + (intWeek - 1) * 7);
            DateTime datEnd = datStart.AddDays(6);
            lblDateSpan.Text = StringExtensions.FastFormat(strFormat,
                datStart.ToString("D", GlobalSettings.CultureInfo), datEnd.ToString("D", GlobalSettings.CultureInfo));
        }

        #endregion Methods

        #region Properties

        /// <summary>
        /// Selected year.
        /// </summary>
        public int SelectedYear => _intSelectedYear;

        /// <summary>
        /// Selected week.
        /// </summary>
        public int SelectedWeek => _intSelectedWeek;

        #endregion Properties
    }
}
