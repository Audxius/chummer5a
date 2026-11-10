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
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// One quality card: the quality with all of its levels (legacy keeps one Quality object per level), its karma value,
    /// source, and the edit actions legacy Chummer allows for it (level spinner, delete / buy off).
    /// Built on a background thread; commands run the lifted <see cref="QualityRules"/>.
    /// </summary>
    public sealed class QualityViewModel
    {
        private readonly CharacterViewModel _objOwner;
        private readonly Quality _objQuality;

        private QualityViewModel(CharacterViewModel objOwner, Quality objQuality, int intLevels)
        {
            _objOwner = objOwner;
            _objQuality = objQuality;
            Character objCharacter = objOwner.Character;
            CultureInfo objCulture = GlobalSettings.CultureInfo;
            IsCareer = objCharacter.Created;
            Levels = Math.Max(1, intLevels);
            Name = objQuality.CurrentDisplayNameShort;
            Extra = string.IsNullOrEmpty(objQuality.Extra) ? string.Empty : objCharacter.TranslateExtra(objQuality.Extra);
            IsPositive = objQuality.Type == QualityType.Positive;
            Type = objQuality.Type;
            SourceString objSource = objQuality.SourceDetail;
            Source = objSource.ToString();
            SourceTip = objSource.LanguageBookTooltip;
            Notes = (objQuality.Notes ?? string.Empty).Trim();
            int intKarmaQuality = objCharacter.Settings.KarmaQuality;
            Karma = objQuality.ContributeToBP ? objQuality.BP * intKarmaQuality * Levels : 0;
            MaximumLevels = QualityRules.GetMaximumLevelsAsync(objQuality).GetAwaiter().GetResult();
            CanRemove = QualityRules.CanRemoveAsync(objCharacter, objQuality).GetAwaiter().GetResult();
            switch (objQuality.OriginSource)
            {
                case QualitySource.Metatype:
                case QualitySource.MetatypeRemovable:
                case QualitySource.MetatypeRemovedAtChargen:
                    Origin = "METATYPE";
                    break;
                case QualitySource.Heritage:
                    Origin = "HERITAGE";
                    break;
                case QualitySource.Improvement:
                case QualitySource.QualityLevelImprovement:
                    string strFrom = objQuality.DisplaySourceName(GlobalSettings.Language);
                    Origin = string.IsNullOrEmpty(strFrom) ? "GRANTED" : "FROM " + strFrom.ToUpper(objCulture);
                    break;
                case QualitySource.LifeModule:
                    Origin = "LIFE MODULE";
                    break;
            }
            if (IsCareer)
            {
                if (MaximumLevels > 0 && Levels < MaximumLevels)
                {
                    NextLevelCost = QualityRules.GetCareerLevelCostAsync(objCharacter, objQuality).GetAwaiter().GetResult();
                }
                if (CanRemove)
                    RemovalCost = QualityRules.GetCareerRemovalCostAsync(objCharacter, objQuality).GetAwaiter().GetResult();
            }
            RemoveCommand = new AsyncRelayCommand(() => _objOwner.RunRuleAsync(t => QualityRules.RemoveAsync(_objOwner.Character, _objQuality, t)));
            LevelUpCommand = new AsyncRelayCommand(() => _objOwner.RunRuleAsync(t => QualityRules.SetLevelsAsync(_objOwner.Character, _objQuality, Levels + 1, t)));
            LevelDownCommand = new AsyncRelayCommand(() => _objOwner.RunRuleAsync(t => QualityRules.SetLevelsAsync(_objOwner.Character, _objQuality, Levels - 1, t)));
        }

        /// <summary>One card per quality, levels folded together like the legacy quality tree.</summary>
        public static List<QualityViewModel> FromCharacter(CharacterViewModel objOwner)
        {
            List<Quality> lstQualities = objOwner.Character.Qualities.ToList();
            return lstQualities.GroupBy(x => (x.SourceID, x.Extra ?? string.Empty, x.SourceName ?? string.Empty, x.Type))
                               .Select(x => new QualityViewModel(objOwner, x.First(), x.Count()))
                               .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                               .ToList();
        }

        public string Name { get; }
        public string Extra { get; }
        public bool HasExtra => Extra.Length > 0;
        public QualityType Type { get; }
        public bool IsPositive { get; }
        public bool IsNegative => Type == QualityType.Negative;
        public bool IsCareer { get; }
        public string Source { get; }
        public string SourceTip { get; }
        public bool HasSource => !string.IsNullOrEmpty(Source);
        public string Notes { get; }
        public bool HasNotes => Notes.Length > 0;
        public string Origin { get; } = string.Empty;
        public bool HasOrigin => Origin.Length > 0;

        /// <summary>Karma value of the quality (all levels): positive costs, negative gives.</summary>
        public int Karma { get; }

        public string KarmaText => Karma == 0 ? "0" : (Karma > 0 ? "+" : "−") + Math.Abs(Karma).ToString(GlobalSettings.CultureInfo);

        public int Levels { get; }
        public int MaximumLevels { get; }
        public bool HasLevels => MaximumLevels > 1;
        public string LevelText => Levels.ToString(GlobalSettings.CultureInfo) + " / " + MaximumLevels.ToString(GlobalSettings.CultureInfo);
        public bool CanLevelUp => HasLevels && Levels < MaximumLevels;
        public bool CanLevelDown => HasLevels && Levels > 1;
        public int NextLevelCost { get; }
        public string LevelUpTip => !CanLevelUp
            ? "Already at the highest level"
            : IsCareer
                ? IsPositive
                    ? "Take level " + (Levels + 1).ToString(GlobalSettings.CultureInfo) + " for " + NextLevelCost.ToString(GlobalSettings.CultureInfo) + " karma"
                    : "Take level " + (Levels + 1).ToString(GlobalSettings.CultureInfo) + " (no karma in career mode)"
                : "Raise to level " + (Levels + 1).ToString(GlobalSettings.CultureInfo);
        public string LevelDownTip => !CanLevelDown
            ? "Use remove to drop the quality"
            : IsCareer && !IsPositive
                ? "Buy off one level"
                : "Drop to level " + (Levels - 1).ToString(GlobalSettings.CultureInfo);

        public bool CanRemove { get; }

        /// <summary>Career karma to remove (buy-off for negatives; negative when a positive quality refunds).</summary>
        public int RemovalCost { get; }

        public string RemoveTip => !CanRemove
            ? Origin.Length > 0 ? "Comes with " + Origin.ToLowerInvariant().Replace("from ", string.Empty) + " and cannot be removed" : "Cannot be removed"
            : IsCareer
                ? IsPositive
                    ? RemovalCost < 0 ? "Remove (refunds " + (-RemovalCost).ToString(GlobalSettings.CultureInfo) + " karma)" : "Remove (no refund in career mode)"
                    : "Buy off for " + RemovalCost.ToString(GlobalSettings.CultureInfo) + " karma"
                : "Remove";

        public string KarmaTip => Type == QualityType.LifeModule ? "Life module cost: " + Math.Abs(Karma).ToString(GlobalSettings.CultureInfo) + " karma" : IsCareer
            ? (IsPositive ? "Karma value " : "Bonus karma ") + Math.Abs(Karma).ToString(GlobalSettings.CultureInfo) + (CanRemove && !IsPositive ? "  //  buy-off " + RemovalCost.ToString(GlobalSettings.CultureInfo) + " karma" : string.Empty)
            : (IsPositive ? "Costs " : "Gives ") + Math.Abs(Karma).ToString(GlobalSettings.CultureInfo) + " karma at creation";

        public string TypeLabel => Type == QualityType.Positive ? "POSITIVE QUALITY" : Type == QualityType.Negative ? "NEGATIVE QUALITY" : Type.ToString().ToUpperInvariant();

        public IAsyncRelayCommand RemoveCommand { get; }
        public IAsyncRelayCommand LevelUpCommand { get; }
        public IAsyncRelayCommand LevelDownCommand { get; }
    }
}
