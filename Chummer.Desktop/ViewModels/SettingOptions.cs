/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Chummer.Desktop.Localization;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// One editable row of a settings page. Rows are generated from <see cref="CharacterSettingsSchema"/> and read/write a
    /// property of the edited <see cref="CharacterSettings"/> by name, so the ~150 legacy options need no hand-written controls.
    /// </summary>
    public abstract partial class SettingOption : ObservableObject
    {
        protected SettingOption(ISettingOptionHost objHost, string strLabelKey, string strSuffixKey = null, string strTipKey = null)
        {
            Host = objHost;
            Label = Text(strLabelKey);
            Suffix = Text(strSuffixKey);
            Tip = string.IsNullOrEmpty(strTipKey) ? null : Tr.Get(strTipKey);
        }

        protected ISettingOptionHost Host { get; }

        public string Label { get; }

        public string Suffix { get; }

        public bool HasSuffix => !string.IsNullOrEmpty(Suffix);

        public string Tip { get; }

        /// <summary>Property whose value enables this row ("!Name" negates), as legacy Enabled data bindings did.</summary>
        public string EnabledBy { get; set; }

        /// <summary>Property whose value shows this row ("!Name" negates), as legacy Visible data bindings did.</summary>
        public string VisibleBy { get; set; }

        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private bool _isVisible = true;

        /// <summary>Re-reads the value(s) from the settings object.</summary>
        public void Refresh()
        {
            IsEnabled = Host.IsEditable && Condition(EnabledBy);
            IsVisible = Condition(VisibleBy);
            RefreshValue();
        }

        protected abstract void RefreshValue();

        private bool Condition(string strProperty)
        {
            if (string.IsNullOrEmpty(strProperty))
                return true;
            bool blnNegate = strProperty[0] == '!';
            object objValue = Host.GetValue(blnNegate ? strProperty.Substring(1) : strProperty);
            bool blnValue = objValue is bool blnTemp && blnTemp;
            return blnNegate ? !blnValue : blnValue;
        }

        /// <summary>A language key, a literal in quotes ("%"), or nothing.</summary>
        internal static string Text(string strKey)
        {
            if (string.IsNullOrEmpty(strKey))
                return string.Empty;
            if (strKey.Length > 1 && strKey[0] == '"')
                return strKey.Trim('"');
            return Tr.Get(strKey);
        }
    }

    /// <summary>What the option rows need from the page that owns them.</summary>
    public interface ISettingOptionHost
    {
        bool IsEditable { get; }

        object GetValue(string strProperty);

        void SetValue(string strProperty, object objValue);
    }

    public sealed partial class BoolSettingOption : SettingOption
    {
        private readonly string _strProperty;
        private bool _blnRefreshing;

        public BoolSettingOption(ISettingOptionHost objHost, string strProperty, string strLabelKey, string strTipKey = null)
            : base(objHost, strLabelKey, null, strTipKey)
        {
            _strProperty = strProperty;
        }

        [ObservableProperty] private bool _value;

        partial void OnValueChanged(bool value)
        {
            if (!_blnRefreshing)
                Host.SetValue(_strProperty, value);
        }

        protected override void RefreshValue()
        {
            _blnRefreshing = true;
            try
            {
                Value = Host.GetValue(_strProperty) is bool blnValue && blnValue;
            }
            finally
            {
                _blnRefreshing = false;
            }
        }
    }

    public sealed partial class NumberSettingOption : SettingOption
    {
        private readonly string _strProperty;
        private bool _blnRefreshing;

        public NumberSettingOption(ISettingOptionHost objHost, string strProperty, string strLabelKey, string strSuffixKey,
                                   decimal decMinimum, decimal decMaximum, int intDecimals = 0, string strTipKey = null)
            : base(objHost, strLabelKey, strSuffixKey, strTipKey)
        {
            _strProperty = strProperty;
            _decMinimum = decMinimum;
            _decMaximum = decMaximum;
            Decimals = intDecimals;
        }

        public int Decimals { get; }

        public string FormatString => Decimals > 0 ? "N" + Decimals.ToString(CultureInfo.InvariantCulture) : "0";

        public decimal Increment => 1;

        /// <summary>Property that caps this one (legacy bound NumericUpDown.Maximum to e.g. MaxSkillRating).</summary>
        public string MaximumBy { get; set; }

        /// <summary>Property that floors this one (legacy bound NumericUpDown.Minimum to e.g. MinInitiativeDice).</summary>
        public string MinimumBy { get; set; }

        [ObservableProperty] private decimal _minimum;
        [ObservableProperty] private decimal _maximum;
        [ObservableProperty] private decimal? _value;

        private readonly decimal _decMinimum;
        private readonly decimal _decMaximum;

        partial void OnValueChanged(decimal? value)
        {
            if (_blnRefreshing || value == null)
                return;
            Host.SetValue(_strProperty, value.Value);
        }

        protected override void RefreshValue()
        {
            _blnRefreshing = true;
            try
            {
                Minimum = !string.IsNullOrEmpty(MinimumBy) ? Convert.ToDecimal(Host.GetValue(MinimumBy), CultureInfo.InvariantCulture) : _decMinimum;
                Maximum = !string.IsNullOrEmpty(MaximumBy) ? Convert.ToDecimal(Host.GetValue(MaximumBy), CultureInfo.InvariantCulture) : _decMaximum;
                object objValue = Host.GetValue(_strProperty);
                Value = objValue == null ? (decimal?)null : Convert.ToDecimal(objValue, CultureInfo.InvariantCulture);
            }
            finally
            {
                _blnRefreshing = false;
            }
        }
    }

    public sealed partial class TextSettingOption : SettingOption
    {
        private readonly string _strProperty;
        private readonly Func<string, bool> _funcValidate;
        private bool _blnRefreshing;

        public TextSettingOption(ISettingOptionHost objHost, string strProperty, string strLabelKey, string strSuffixKey = null,
                                 Func<string, bool> funcValidate = null, string strTipKey = null, bool blnMonospace = true)
            : base(objHost, strLabelKey, strSuffixKey, strTipKey)
        {
            _strProperty = strProperty;
            _funcValidate = funcValidate;
            IsExpression = blnMonospace;
        }

        public bool IsExpression { get; }

        [ObservableProperty] private string _value = string.Empty;
        [ObservableProperty] private bool _isInvalid;

        partial void OnValueChanged(string value)
        {
            IsInvalid = _funcValidate != null && !_funcValidate(value ?? string.Empty);
            if (!_blnRefreshing)
                Host.SetValue(_strProperty, value ?? string.Empty);
        }

        public bool Validate() => _funcValidate == null || _funcValidate(Host.GetValue(_strProperty) as string ?? string.Empty);

        protected override void RefreshValue()
        {
            _blnRefreshing = true;
            try
            {
                Value = Host.GetValue(_strProperty) as string ?? string.Empty;
            }
            finally
            {
                _blnRefreshing = false;
            }
        }
    }

    /// <summary>A drop-down; the list and the read/write of the selection are supplied by the page.</summary>
    public sealed partial class ChoiceSettingOption : SettingOption
    {
        private readonly Func<string> _funcGet;
        private readonly Action<string> _funcSet;
        private bool _blnRefreshing;

        public ChoiceSettingOption(ISettingOptionHost objHost, string strLabelKey, Func<string> funcGet, Action<string> funcSet, string strTipKey = null)
            : base(objHost, strLabelKey, null, strTipKey)
        {
            _funcGet = funcGet;
            _funcSet = funcSet;
        }

        public ObservableCollection<ListItem> Items { get; } = new ObservableCollection<ListItem>();

        [ObservableProperty] private ListItem? _selected;

        public void SetItems(IEnumerable<ListItem> lstItems)
        {
            _blnRefreshing = true;
            try
            {
                Items.Clear();
                foreach (ListItem objItem in lstItems)
                    Items.Add(objItem);
            }
            finally
            {
                _blnRefreshing = false;
            }
            Refresh();
        }

        partial void OnSelectedChanged(ListItem? value)
        {
            if (_blnRefreshing || value == null)
                return;
            string strValue = value.Value.Value?.ToString() ?? string.Empty;
            if (strValue != _funcGet())
                _funcSet(strValue);
        }

        protected override void RefreshValue()
        {
            _blnRefreshing = true;
            try
            {
                string strCurrent = _funcGet() ?? string.Empty;
                ListItem? objMatch = null;
                foreach (ListItem objItem in Items)
                {
                    if (objItem.Value?.ToString() == strCurrent)
                    {
                        objMatch = objItem;
                        break;
                    }
                }
                Selected = objMatch;
            }
            finally
            {
                _blnRefreshing = false;
            }
        }
    }

    /// <summary>"[x] Lower Agility by [ 1 ]" – a switch that enables a number (legacy check box + NumericUpDown pairs).</summary>
    public sealed partial class ToggleNumberSettingOption : SettingOption
    {
        private readonly string _strToggleProperty;
        private bool _blnRefreshing;

        public ToggleNumberSettingOption(ISettingOptionHost objHost, string strToggleProperty, string strLabelKey, NumberSettingOption objNumber,
                                         string strTipKey = null)
            : base(objHost, strLabelKey, null, strTipKey)
        {
            _strToggleProperty = strToggleProperty;
            Number = objNumber;
            Number.EnabledBy = strToggleProperty;
        }

        public NumberSettingOption Number { get; }

        [ObservableProperty] private bool _value;

        partial void OnValueChanged(bool value)
        {
            if (!_blnRefreshing)
                Host.SetValue(_strToggleProperty, value);
        }

        protected override void RefreshValue()
        {
            _blnRefreshing = true;
            try
            {
                Value = Host.GetValue(_strToggleProperty) is bool blnValue && blnValue;
            }
            finally
            {
                _blnRefreshing = false;
            }
            Number.Refresh();
        }
    }

    /// <summary>"Initiative: min [ ] max [ ]" – the legacy initiative dice grid.</summary>
    public sealed class RangeSettingOption : SettingOption
    {
        public RangeSettingOption(ISettingOptionHost objHost, string strLabelKey, NumberSettingOption objMinimum, NumberSettingOption objMaximum)
            : base(objHost, strLabelKey)
        {
            Minimum = objMinimum;
            Maximum = objMaximum;
        }

        public NumberSettingOption Minimum { get; }

        public NumberSettingOption Maximum { get; }

        /// <summary>Text before the first box; empty for a min/max pair.</summary>
        public string PrefixText { get; set; } = string.Empty;

        /// <summary>Text between the boxes; "–" for a min/max pair.</summary>
        public string BetweenText { get; set; } = "–";

        public bool HasPrefix => !string.IsNullOrEmpty(PrefixText);

        public string Tooltip => HasPrefix ? null : Tr.Get("Label_CreateImprovementMinimum") + " – " + Tr.Get("Label_CreateImprovementMaximum");

        protected override void RefreshValue()
        {
            Minimum.Refresh();
            Maximum.Refresh();
        }
    }

    /// <summary>A set of check boxes under one label (redliner limbs, allowed cyberware grades).</summary>
    public sealed class FlagsSettingOption : SettingOption
    {
        public FlagsSettingOption(ISettingOptionHost objHost, string strLabelKey) : base(objHost, strLabelKey)
        {
        }

        public ObservableCollection<FlagItem> Flags { get; } = new ObservableCollection<FlagItem>();

        protected override void RefreshValue()
        {
            foreach (FlagItem objFlag in Flags)
                objFlag.Refresh(Host.IsEditable);
        }
    }

    public sealed partial class FlagItem : ObservableObject
    {
        private readonly Func<bool> _funcGet;
        private readonly Action<bool> _funcSet;
        private bool _blnRefreshing;
        private readonly bool _canEdit;

        public FlagItem(string strLabel, Func<bool> funcGet, Action<bool> funcSet, bool canEdit = true)
        {
            Label = strLabel;
            _funcGet = funcGet;
            _funcSet = funcSet;
            _canEdit = canEdit;
        }

        public string Label { get; }

        [ObservableProperty] private bool _value;
        [ObservableProperty] private bool _isEnabled = true;

        partial void OnValueChanged(bool value)
        {
            if (!_blnRefreshing)
                _funcSet(value);
        }

        public void Refresh(bool blnEditable)
        {
            _blnRefreshing = true;
            try
            {
                IsEnabled = blnEditable && _canEdit;
                Value = _funcGet();
            }
            finally
            {
                _blnRefreshing = false;
            }
        }
    }

    /// <summary>A line of explanatory text between rows ("Each encumbrance penalty tick does the following:").</summary>
    public sealed class NoteSettingOption : SettingOption
    {
        public NoteSettingOption(ISettingOptionHost objHost, string strLabelKey) : base(objHost, strLabelKey)
        {
        }

        protected override void RefreshValue()
        {
        }
    }

    /// <summary>A titled card of rows (legacy group box).</summary>
    public sealed class SettingGroup
    {
        public SettingGroup(string strTitleKey, IReadOnlyList<SettingOption> lstOptions, bool blnWide = false)
        {
            Title = SettingOption.Text(strTitleKey).ToUpper(GlobalSettings.CultureInfo);
            Options = lstOptions;
            IsWide = blnWide;
        }

        public string Title { get; }

        public IReadOnlyList<SettingOption> Options { get; }

        /// <summary>Wide cards span the page instead of sharing a row.</summary>
        public bool IsWide { get; }

        public double CardWidth => IsWide ? 1100 : 540;
    }

    internal static class SettingReflection
    {
        public static PropertyInfo Find(Type typOwner, string strProperty)
            => typOwner.GetProperty(strProperty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        public static object Coerce(object objValue, Type typTarget)
        {
            if (objValue == null)
                return null;
            if (typTarget.IsInstanceOfType(objValue))
                return objValue;
            if (typTarget.IsEnum)
                return objValue is string strValue ? Enum.Parse(typTarget, strValue) : Enum.ToObject(typTarget, objValue);
            return Convert.ChangeType(objValue, typTarget, CultureInfo.InvariantCulture);
        }
    }
}
