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
using System.Text;
using System.Xml.XPath;

namespace Chummer.Selection
{
    /// <summary>Which metagenic qualities the quality list shows (SelectQuality's chkMetagenic / chkNotMetagenic).</summary>
    public enum MetagenicFilter
    {
        Any,
        OnlyMetagenic,
        NoMetagenic,
    }

    /// <summary>Everything the quality picker can filter on.</summary>
    public sealed class QualityFilter
    {
        /// <summary>Category value from <see cref="QualitySelector.Categories"/> ("Show All" for every category).</summary>
        public string Category { get; set; } = QualitySelector.ShowAll;

        public string Search { get; set; } = string.Empty;

        /// <summary>Only qualities whose requirements the runner meets (chkLimitList).</summary>
        public bool OnlyAvailable { get; set; } = true;

        public MetagenicFilter Metagenic { get; set; }

        /// <summary>Exact karma value (nudValueBP); 0 = not used.</summary>
        public int ValueKarma { get; set; }

        /// <summary>Karma range (nudMinimumBP / nudMaximumBP); both 0 = not used.</summary>
        public int MinimumKarma { get; set; }

        public int MaximumKarma { get; set; }
    }

    /// <summary>
    /// The quality list, cost label and acceptance check of the legacy SelectQuality form (BuildQualityList, UpdateCostLabel,
    /// AcceptForm), so every UI offers the same qualities at the same displayed cost.
    /// </summary>
    public sealed class QualitySelector
    {
        public const string ShowAll = "Show All";

        private readonly Character _objCharacter;
        private readonly XPathNavigator _xmlBaseQualityDataNode;
        private readonly XPathNavigator _xmlMetatypeQualityRestrictionNode;
        private readonly List<ListItem> _lstCategories = new List<ListItem>();

        public QualitySelector(Character objCharacter, string strIgnoreQuality = "")
        {
            _objCharacter = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
            IgnoreQuality = strIgnoreQuality ?? string.Empty;
            _xmlBaseQualityDataNode = objCharacter.LoadDataXPath("qualities.xml").SelectSingleNodeAndCacheExpression("/chummer");
            _xmlMetatypeQualityRestrictionNode = objCharacter.GetNodeXPath()?.SelectSingleNodeAndCacheExpression("qualityrestriction");
            foreach (XPathNavigator objXmlCategory in _xmlBaseQualityDataNode.SelectAndCacheExpression("categories/category"))
            {
                string strInnerText = objXmlCategory.Value;
                _lstCategories.Add(new ListItem(strInnerText, objXmlCategory.SelectSingleNodeAndCacheExpression("@translate")?.Value ?? strInnerText));
            }
            if (_lstCategories.Count > 0)
                _lstCategories.Insert(0, new ListItem(ShowAll, LanguageManager.GetString("String_ShowAll")));
        }

        /// <summary>A quality the runner has that is ignored for forbidden checks (swapping qualities).</summary>
        public string IgnoreQuality { get; }

        /// <summary>The default metagenic filter: SelectQuality ticks "not metagenic" unless the runner may take metagenic qualities.</summary>
        public MetagenicFilter DefaultMetagenicFilter => _objCharacter.MetagenicLimit == 0 ? MetagenicFilter.NoMetagenic : MetagenicFilter.Any;

        public IReadOnlyList<ListItem> Categories => _lstCategories;

        public XPathNavigator Node(string strId) => string.IsNullOrEmpty(strId) ? null : _xmlBaseQualityDataNode.TryGetNodeByNameOrId("qualities/quality", strId);

        /// <summary>Qualities (value = id) matching the filter, sorted by name.</summary>
        public List<ListItem> Options(QualityFilter objFilter)
        {
            if (objFilter == null)
                objFilter = new QualityFilter();
            string strCategory = objFilter.Category ?? string.Empty;
            string strSearch = objFilter.Search ?? string.Empty;
            CharacterSettings objSettings = _objCharacter.Settings;
            bool blnCreated = _objCharacter.Created;
            StringBuilder sbdFilter = new StringBuilder(objSettings.BookXPath());
            if (!string.IsNullOrEmpty(strCategory) && strCategory != ShowAll && (GlobalSettings.SearchInCategoryOnly || strSearch.Length == 0))
            {
                sbdFilter.Append(" and category = ").Append(strCategory.CleanXPath());
            }
            else
            {
                List<string> lstCategoryFilters = _lstCategories.Select(x => x.Value.ToString()).Where(x => !string.IsNullOrEmpty(x))
                                                                .Select(x => "category = " + x.CleanXPath()).ToList();
                if (lstCategoryFilters.Count > 0)
                    sbdFilter.Append(" and (").Append(string.Join(" or ", lstCategoryFilters)).Append(')');
            }

            switch (objFilter.Metagenic)
            {
                case MetagenicFilter.OnlyMetagenic:
                    sbdFilter.Append(" and (metagenic = 'True' or required/oneof[contains(., 'Changeling')])");
                    break;
                case MetagenicFilter.NoMetagenic:
                    sbdFilter.Append(" and not(metagenic = 'True') and not(required/oneof[contains(., 'Changeling')])");
                    break;
            }

            bool blnDoubleCareer = blnCreated && !objSettings.DontDoubleQualityPurchases;
            if (objFilter.ValueKarma != 0)
            {
                string strValueBP = objFilter.ValueKarma.ToString(GlobalSettings.InvariantCultureInfo);
                if (blnDoubleCareer && objFilter.ValueKarma > 0)
                {
                    string strValueBPHalved = (objFilter.ValueKarma / 2m).ToString(GlobalSettings.InvariantCultureInfo);
                    sbdFilter.Append(" and ((doublecareer = 'False' and (karma = ").Append(strValueBP)
                             .Append(" or (not(nolevels) and limit != 'False' and (karma mod ").Append(strValueBP)
                             .Append(") = 0 and karma * karma * limit <= karma * ").Append(strValueBP)
                             .Append("))) or (not(doublecareer = 'False') and (karma = ").Append(strValueBPHalved)
                             .Append(" or (not(nolevels) and limit != 'False' and (karma mod ").Append(strValueBPHalved)
                             .Append(") = 0 and karma * karma * limit <= karma * ").Append(strValueBPHalved).Append("))))");
                }
                else
                {
                    sbdFilter.Append(" and (karma = ").Append(strValueBP)
                             .Append(" or (not(nolevels) and limit != 'False' and (karma mod ").Append(strValueBP)
                             .Append(") = 0 and karma * karma * limit <= karma * ").Append(strValueBP).Append("))");
                }
            }
            else if (objFilter.MinimumKarma != 0 || objFilter.MaximumKarma != 0)
            {
                int intMinimumBP = Math.Min(objFilter.MinimumKarma, objFilter.MaximumKarma);
                int intMaximumBP = Math.Max(objFilter.MinimumKarma, objFilter.MaximumKarma);
                if (intMinimumBP < 0 == intMaximumBP < 0)
                    sbdFilter.Append(" and (").Append(KarmaRange(intMaximumBP, intMinimumBP, blnDoubleCareer)).Append(')');
                else
                    sbdFilter.Append(" and ((").Append(KarmaRange(intMaximumBP, 0, blnDoubleCareer)).Append(") or (")
                             .Append(KarmaRange(-1, intMinimumBP, blnDoubleCareer)).Append("))");
            }

            if (!string.IsNullOrEmpty(strSearch))
                sbdFilter.Append(" and ").Append(CommonFunctions.GenerateSearchXPath(strSearch));

            string strCategoryLower = string.IsNullOrEmpty(strCategory) || strCategory == ShowAll ? "*" : strCategory.ToLowerInvariant();
            List<ListItem> lstReturn = new List<ListItem>();
            foreach (XPathNavigator objXmlQuality in _xmlBaseQualityDataNode.Select("qualities/quality[" + sbdFilter + "]"))
            {
                string strLoopName = objXmlQuality.SelectSingleNodeAndCacheExpression("name")?.Value;
                if (string.IsNullOrEmpty(strLoopName))
                    continue;
                if (_xmlMetatypeQualityRestrictionNode != null
                    && _xmlMetatypeQualityRestrictionNode.SelectSingleNode(strCategoryLower + "/quality[. = " + strLoopName.CleanXPath() + "]") == null)
                    continue;
                if (objFilter.OnlyAvailable && !objXmlQuality.RequirementsMet(_objCharacter, strIgnoreQuality: IgnoreQuality))
                    continue;
                lstReturn.Add(new ListItem(objXmlQuality.SelectSingleNodeAndCacheExpression("id")?.Value ?? string.Empty,
                                           objXmlQuality.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strLoopName));
            }
            lstReturn.Sort(CompareListItems.CompareNames);
            return lstReturn;
        }

        // SelectQuality.BuildQualityList's karma range expression.
        private static string KarmaRange(int intMax, int intMin, bool blnDoubleCareer)
        {
            string strMax = intMax.ToString(GlobalSettings.InvariantCultureInfo);
            string strMin = intMin.ToString(GlobalSettings.InvariantCultureInfo);
            string strMostExtremeValue = (intMax > 0 ? intMax : intMin).ToString(GlobalSettings.InvariantCultureInfo);
            string strValueDiff = (intMax > 0 ? intMax - intMin : intMin - intMax).ToString(GlobalSettings.InvariantCultureInfo);
            if (blnDoubleCareer)
            {
                return "((doublecareer = 'False' or karma < 0) and ((karma >= " + strMin + " and karma <= " + strMax
                       + ") or (not(nolevels) and limit != 'False' and karma * karma <= karma * " + strMostExtremeValue
                       + " and (karma * (" + strMostExtremeValue + " mod karma) <= karma * " + strValueDiff
                       + ") and ((karma >= 0 and karma * limit >= " + strMin + ") or (karma < 0 and karma * limit <= " + strMax
                       + "))))) or (not(doublecareer = 'False' or karma < 0) and ((2 * karma >= " + strMin + " and 2 * karma <= " + strMax
                       + ") or (not(nolevels) and limit != 'False' and 2 * karma * karma <= karma * " + strMostExtremeValue
                       + " and (2 * karma * (" + strMostExtremeValue + " mod (2 * karma)) <= karma * " + strValueDiff
                       + ") and ((karma >= 0 and 2 * karma * limit >= " + strMin + ") or (karma < 0 and 2 * karma * limit <= " + strMax + ")))))";
            }
            return "(karma >= " + strMin + " and karma <= " + strMax + ") or (not(nolevels) and limit != 'False' and karma * karma <= karma * "
                   + strMostExtremeValue + " and (karma * (" + strMostExtremeValue + " mod karma) <= karma * " + strValueDiff
                   + ") and ((karma >= 0 and karma * limit >= " + strMin + ") or (karma < 0 and karma * limit <= " + strMax + ")))";
        }

        /// <summary>Highest rating the picker allows (the quality's limit when it has levels, otherwise 1).</summary>
        public int MaximumRating(string strId)
        {
            XPathNavigator xmlQuality = Node(strId);
            int intMaxRating = int.MaxValue;
            if (xmlQuality != null && xmlQuality.TryGetInt32FieldQuickly("limit", ref intMaxRating)
                                   && xmlQuality.SelectSingleNodeAndCacheExpression("nolevels") == null)
                return Math.Max(1, intMaxRating);
            return 1;
        }

        public bool HasLevels(string strId) => MaximumRating(strId) > 1;

        public bool IsPositive(string strId)
            => !string.Equals(Node(strId)?.SelectSingleNodeAndCacheExpression("category")?.Value, "Negative", StringComparison.OrdinalIgnoreCase);

        /// <summary>The cost label SelectQuality shows (karma, doubled in career unless the quality or the settings say otherwise).</summary>
        public string CostText(string strId, int intRating, bool blnFree)
        {
            XPathNavigator xmlQuality = Node(strId);
            if (xmlQuality == null)
                return string.Empty;
            if (blnFree)
                return 0.ToString(GlobalSettings.CultureInfo);
            string strKarma = xmlQuality.SelectSingleNodeAndCacheExpression("karma")?.Value ?? string.Empty;
            if (strKarma.StartsWith("Variable(", StringComparison.Ordinal))
            {
                int intMin;
                int intMax = int.MaxValue;
                string strCost = strKarma.TrimStartOnce("Variable(", true).TrimEndOnce(')');
                if (strCost.Contains('-'))
                {
                    string[] astrValues = strCost.Split('-');
                    int.TryParse(astrValues[0], NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out intMin);
                    int.TryParse(astrValues.Length > 1 ? astrValues[1] : string.Empty, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out intMax);
                }
                else
                    int.TryParse(strCost.FastEscape('+'), NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out intMin);
                return intMax == int.MaxValue
                    ? intMin.ToString(GlobalSettings.CultureInfo)
                    : intMin.ToString(GlobalSettings.CultureInfo) + " - " + intMax.ToString(GlobalSettings.CultureInfo);
            }
            int intKarmaCost = KarmaCost(strId, intRating);
            string strReturn = intKarmaCost.ToString(GlobalSettings.CultureInfo);
            if (!_objCharacter.Created)
            {
                int intFreeSpells = _objCharacter.FreeSpells;
                if (intFreeSpells > 0 && xmlQuality.SelectSingleNodeAndCacheExpression("canbuywithspellpoints")?.Value == bool.TrueString)
                {
                    int intSpellCost = _objCharacter.SpellKarmaCost("Spell");
                    if (intSpellCost > 0)
                    {
                        int intSpellPoints = intKarmaCost.DivRem(intSpellCost, out int intRemainder);
                        if (intSpellPoints > intFreeSpells)
                        {
                            intRemainder = intSpellCost * (intSpellPoints - intFreeSpells);
                            intSpellPoints = intFreeSpells;
                        }
                        strReturn = intRemainder.ToString(GlobalSettings.CultureInfo) + " + " + intSpellPoints.ToString(GlobalSettings.CultureInfo)
                                    + " " + LanguageManager.GetString("String_SpellPoints");
                    }
                }
            }
            return strReturn;
        }

        /// <summary>Karma value of <paramref name="intRating"/> levels as shown by the picker (career doubling and cost discounts included).</summary>
        public int KarmaCost(string strId, int intRating)
        {
            XPathNavigator xmlQuality = Node(strId);
            if (xmlQuality == null)
                return 0;
            int.TryParse(xmlQuality.SelectSingleNodeAndCacheExpression("karma")?.Value, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out int intBP);
            XPathNavigator xmlCostDiscount = xmlQuality.SelectSingleNodeAndCacheExpression("costdiscount");
            if (xmlCostDiscount != null && xmlCostDiscount.RequirementsMet(_objCharacter))
            {
                XPathNavigator xmlValueNode = xmlCostDiscount.SelectSingleNodeAndCacheExpression("value");
                if (xmlValueNode != null)
                {
                    int intValue = xmlValueNode.ValueAsInt;
                    switch (xmlQuality.SelectSingleNodeAndCacheExpression("category")?.Value.ToUpperInvariant())
                    {
                        case "POSITIVE":
                            intBP += intValue;
                            break;
                        case "NEGATIVE":
                            intBP -= intValue;
                            break;
                    }
                }
            }
            CharacterSettings objSettings = _objCharacter.Settings;
            if (_objCharacter.Created && !objSettings.DontDoubleQualityPurchases
                                      && xmlQuality.SelectSingleNodeAndCacheExpression("doublecareer")?.Value != bool.FalseString)
                intBP *= 2;
            return intBP * Math.Max(1, intRating) * objSettings.KarmaQuality;
        }

        /// <summary>Source and page ("SR5 123") of a quality.</summary>
        public string SourceText(string strId)
        {
            XPathNavigator xmlQuality = Node(strId);
            if (xmlQuality == null)
                return string.Empty;
            string strSource = xmlQuality.SelectSingleNodeAndCacheExpression("source")?.Value ?? LanguageManager.GetString("String_Unknown");
            string strPage = xmlQuality.SelectSingleNodeAndCacheExpression("altpage")?.Value
                             ?? xmlQuality.SelectSingleNodeAndCacheExpression("page")?.Value ?? LanguageManager.GetString("String_Unknown");
            return SourceString.GetSourceString(strSource, strPage, GlobalSettings.Language, GlobalSettings.CultureInfo, _objCharacter).ToString();
        }

        /// <summary>Requirements summary for the details pane.</summary>
        public bool RequirementsMet(string strId) => Node(strId)?.RequirementsMet(_objCharacter, strIgnoreQuality: IgnoreQuality) == true;

        /// <summary>SelectQuality.AcceptForm's check: shows the engine's message when the runner does not qualify.</summary>
        public bool Validate(string strId)
        {
            XPathNavigator objNode = Node(strId);
            return objNode != null && objNode.RequirementsMet(_objCharacter, strLocalName: LanguageManager.GetString("String_Quality"), strIgnoreQuality: IgnoreQuality);
        }
    }
}
