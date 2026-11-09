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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Attributes;
using Chummer.Backend.Equipment;
using Chummer.Backend.Skills;

namespace Chummer.Selection
{
    /// <summary>Attribute range of a metatype/metavariant (min/max/augmented max), as shown on the legacy selection forms.</summary>
    public readonly struct MetatypeAttributeRange
    {
        public MetatypeAttributeRange(string strAbbrev, string strMin, string strMax, string strAug)
        {
            Abbrev = strAbbrev;
            Minimum = strMin;
            Maximum = strMax;
            Augmented = strAug;
        }

        public string Abbrev { get; }
        public string Minimum { get; }
        public string Maximum { get; }
        public string Augmented { get; }

        /// <summary>"1/6 (9)" like the legacy labels.</summary>
        public string Display => Minimum + "/" + Maximum + " (" + Augmented + ")";
    }

    /// <summary>Everything the legacy metatype forms show for the highlighted metatype/metavariant.</summary>
    public sealed class MetatypeDetails
    {
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<MetatypeAttributeRange> Attributes { get; set; } = Array.Empty<MetatypeAttributeRange>();
        public string Karma { get; set; } = string.Empty;
        public int SpecialAttributePoints { get; set; }
        public IReadOnlyList<string> Qualities { get; set; } = Array.Empty<string>();
        public string Source { get; set; } = string.Empty;

        /// <summary>The metavariant is not available at the chosen Heritage priority (legacy "Message_MetatypeNotSupported").</summary>
        public bool NotSupported { get; set; }
    }

    /// <summary>The free skill / skill group picks that come with a Magic/Resonance priority.</summary>
    public sealed class TalentSkillChoice
    {
        public int Count { get; set; }
        public int Rating { get; set; }
        public bool IsSkillGroup { get; set; }
        public string SkillType { get; set; } = string.Empty;
        public IReadOnlyList<ListItem> Options { get; set; } = Array.Empty<ListItem>();
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Metatype choice for creation mode, lifted from the legacy SelectMetatypePriority (Priority / Sum-to-Ten) and
    /// SelectMetatypeKarma (Karma) forms. The selection is held as plain state; the lists and details are derived from it
    /// exactly like the forms' Populate*/Refresh* methods, and <see cref="ApplyAsync"/> is the forms' MetatypeSelected.
    /// </summary>
    public sealed class MetatypeSelector
    {
        public const string Heritage = "Heritage";
        public const string Talent = "Talent";
        public const string Attributes = "Attributes";
        public const string Skills = "Skills";
        public const string Resources = "Resources";

        /// <summary>Priority categories in the order of the legacy form's combo boxes (and of ManagePriorityItems).</summary>
        public static readonly IReadOnlyList<string> PriorityCategories = new[] { Heritage, Attributes, Talent, Skills, Resources };

        private static readonly string[] s_astrAttributes = { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL" };

        private readonly Character _objCharacter;
        private readonly string _strXmlFile;
        private readonly XPathNavigator _xmlBaseMetatypeDataNode;
        private readonly XPathNavigator _xmlBasePriorityDataNode;
        private readonly XPathNavigator _xmlBaseSkillDataNode;
        private readonly XPathNavigator _xmlBaseQualityDataNode;
        private readonly Dictionary<string, int> _dicSumtoTenValues = new Dictionary<string, int>(5);
        private readonly List<string> _lstPriorities;
        private readonly Dictionary<string, string> _dicPriority = new Dictionary<string, string>();
        private readonly string[] _astrSkills = new string[3];

        public MetatypeSelector(Character objCharacter, string strXmlFile = null)
        {
            _objCharacter = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
            if (string.IsNullOrEmpty(_objCharacter.SettingsKey))
                _objCharacter.SettingsKey = GlobalSettings.DefaultCharacterSetting;
            UsesPriorityTables = _objCharacter.EffectiveBuildMethodUsesPriorityTables;
            BuildMethod = _objCharacter.EffectiveBuildMethod;
            // SelectMetatypeKarma reads critters.xml for critters, SelectMetatypePriority is always given metatypes.xml.
            _strXmlFile = strXmlFile ?? (!UsesPriorityTables && _objCharacter.IsCritter ? "critters.xml" : "metatypes.xml");
            _xmlBaseMetatypeDataNode = _objCharacter.LoadDataXPath(_strXmlFile).SelectSingleNodeAndCacheExpression("/chummer");
            _xmlBasePriorityDataNode = _objCharacter.LoadDataXPath("priorities.xml").SelectSingleNodeAndCacheExpression("/chummer");
            _xmlBaseSkillDataNode = _objCharacter.LoadDataXPath("skills.xml").SelectSingleNodeAndCacheExpression("/chummer");
            _xmlBaseQualityDataNode = _objCharacter.LoadDataXPath("qualities.xml").SelectSingleNodeAndCacheExpression("/chummer");

            if (_xmlBasePriorityDataNode != null)
            {
                foreach (XPathNavigator xmlNode in _xmlBasePriorityDataNode.SelectAndCacheExpression("priortysumtotenvalues/*"))
                    _dicSumtoTenValues[xmlNode.Name] = xmlNode.ValueAsInt;
            }
            if (_dicSumtoTenValues.Count == 0)
            {
                _dicSumtoTenValues.Add("A", 4);
                _dicSumtoTenValues.Add("B", 3);
                _dicSumtoTenValues.Add("C", 2);
                _dicSumtoTenValues.Add("D", 1);
                _dicSumtoTenValues.Add("E", 0);
            }
            string strPriorityArray = _objCharacter.Settings.PriorityArray;
            _lstPriorities = !string.IsNullOrEmpty(strPriorityArray)
                ? strPriorityArray.Select(c => char.ToUpperInvariant(c).ToString()).ToList()
                : new List<string> { "A", "B", "C", "D", "E" };
            foreach (string strPriority in _lstPriorities)
                if (!_dicSumtoTenValues.ContainsKey(strPriority))
                    _dicSumtoTenValues.Add(strPriority, 0);

            PossessionMethods = new[] { new ListItem("Inhabitation", TranslateCritterPower("Inhabitation")), new ListItem("Possession", TranslateCritterPower("Possession")) }
                .OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            CurrentPossessionMethod = _objCharacter.CritterPowers.Select(x => x.Name)
                                                   .FirstOrDefault(y => PossessionMethods.Any(x => y.Equals(x.Value.ToString(), StringComparison.OrdinalIgnoreCase)));
            LoadInitialState();
        }

        public Character Character => _objCharacter;
        public bool UsesPriorityTables { get; }
        public CharacterBuildMethod BuildMethod { get; }
        public bool IsSumToTen => BuildMethod == CharacterBuildMethod.SumtoTen;

        /// <summary>Priority letters available under the character's settings (e.g. "ABCDE" or a custom array).</summary>
        public IReadOnlyList<string> PriorityLetters => _lstPriorities;

        public string Category { get; set; } = string.Empty;
        public string Metatype { get; set; } = string.Empty;
        public string Metavariant { get; set; } = Utils.GuidEmptyString;
        public string TalentValue { get; set; } = string.Empty;
        public int Force { get; set; } = 1;
        public bool PossessionBased { get; set; }
        public string PossessionMethod { get; set; } = "Possession";
        public IReadOnlyList<ListItem> PossessionMethods { get; }
        public string CurrentPossessionMethod { get; }

        /// <summary>Picks the specific of an exotic skill chosen as a talent skill (legacy SelectExoticSkill). Return null to cancel.</summary>
        public Func<string, Task<string>> ExoticSpecificPicker { get; set; }

        public string GetPriority(string strCategory) => _dicPriority.TryGetValue(strCategory, out string strValue) ? strValue : string.Empty;

        public string GetSkill(int intIndex) => _astrSkills[intIndex] ?? string.Empty;

        public void SetSkill(int intIndex, string strSkill) => _astrSkills[intIndex] = strSkill ?? string.Empty;

        private string PriorityTableXPath => _objCharacter.Settings.PriorityTable.CleanXPath();

        private string BookXPath => _objCharacter.Settings.BookXPath();

        private string TranslateCritterPower(string strPower)
        {
            if (GlobalSettings.Language == GlobalSettings.DefaultLanguage)
                return strPower;
            return _objCharacter.LoadDataXPath("critterpowers.xml")
                                .SelectSingleNode("/chummer/powers/power[name = " + strPower.CleanXPath() + "]/translate")?.Value ?? strPower;
        }

        #region Priorities

        /// <summary>The options (value + display name) of one priority category, as the legacy combo boxes list them.</summary>
        public List<ListItem> GetPriorityOptions(string strCategory)
        {
            List<ListItem> lstItems = new List<ListItem>(5);
            XPathNavigator xmlBasePrioritiesNode = _xmlBasePriorityDataNode?.SelectSingleNodeAndCacheExpression("priorities");
            if (xmlBasePrioritiesNode == null)
                return lstItems;
            XPathNodeIterator objItems = xmlBasePrioritiesNode.Select("priority[category = " + strCategory.CleanXPath() + " and prioritytable = " + PriorityTableXPath + "]");
            if (objItems.Count == 0)
                objItems = xmlBasePrioritiesNode.Select("priority[category = " + strCategory.CleanXPath() + " and not(prioritytable)]");
            foreach (XPathNavigator objXmlPriority in objItems)
            {
                string strValue = objXmlPriority.SelectSingleNodeAndCacheExpression("value")?.Value;
                if (!string.IsNullOrEmpty(strValue) && _lstPriorities.Contains(strValue))
                    lstItems.Add(new ListItem(strValue, objXmlPriority.SelectSingleNodeAndCacheExpression("translate")?.Value
                                                        ?? objXmlPriority.SelectSingleNodeAndCacheExpression("name")?.Value
                                                        ?? LanguageManager.GetString("String_Unknown")));
            }
            lstItems.Sort(CompareListItems.CompareNames);
            return lstItems;
        }

        /// <summary>
        /// Sets a priority. In Priority mode another category holding the same letter gets the missing one, like the legacy
        /// ManagePriorityItems; Sum-to-Ten allows repeats and is checked on apply.
        /// </summary>
        public void SetPriority(string strCategory, string strValue)
        {
            _dicPriority[strCategory] = strValue ?? string.Empty;
            if (BuildMethod == CharacterBuildMethod.Priority)
                ManagePriorityItems(strCategory);
            OnHeritageOrTalentChanged();
        }

        private List<string> GetMissingPriorities()
        {
            Dictionary<string, int> dicCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string strPriority in _lstPriorities)
                dicCounts[strPriority] = dicCounts.TryGetValue(strPriority, out int intCount) ? intCount + 1 : 1;
            foreach (string strCategory in PriorityCategories)
            {
                string strSelected = GetPriority(strCategory);
                if (!string.IsNullOrEmpty(strSelected) && dicCounts.TryGetValue(strSelected, out int intCount) && intCount > 0)
                    dicCounts[strSelected] = intCount - 1;
            }
            List<string> lstMissing = new List<string>();
            foreach (string strPriority in _lstPriorities)
            {
                if (dicCounts.TryGetValue(strPriority, out int intCount) && intCount > 0)
                {
                    lstMissing.Add(strPriority);
                    dicCounts[strPriority] = intCount - 1;
                }
            }
            return lstMissing;
        }

        private void ManagePriorityItems(string strChangedCategory)
        {
            for (int i = 0; i < 5; ++i)
            {
                if (PriorityCategories.Any(x => string.IsNullOrEmpty(GetPriority(x))))
                    return;
                List<string> lstMissing = GetMissingPriorities();
                if (lstMissing.Count == 0 || (i > 0 && lstMissing.Count <= 1))
                    return;
                string strSelected = GetPriority(strChangedCategory);
                if (string.IsNullOrEmpty(strSelected))
                    return;
                string strOther = PriorityCategories.FirstOrDefault(x => x != strChangedCategory && GetPriority(x) == strSelected);
                if (strOther != null)
                    _dicPriority[strOther] = lstMissing[0];
                if (lstMissing.Count <= 1)
                    return;
            }
        }

        public int GetSumToTenValue(string strPriority)
            => !string.IsNullOrEmpty(strPriority) && _dicSumtoTenValues.TryGetValue(strPriority, out int intValue) ? intValue : 0;

        /// <summary>Current Sum-to-Ten total (legacy SumToTen).</summary>
        public int SumToTen => PriorityCategories.Sum(x => GetSumToTenValue(GetPriority(x)));

        public int SumToTenTarget => _objCharacter.Settings.SumtoTen;

        /// <summary>The priority node for a category/letter, picking between per-table entries the way the legacy form does.</summary>
        private XPathNavigator FindPriorityNode(string strCategory, string strValue, Func<XPathNavigator, bool> funcDisambiguate)
        {
            if (_xmlBasePriorityDataNode == null)
                return null;
            XPathNodeIterator xmlList = _xmlBasePriorityDataNode.Select("priorities/priority[category = " + strCategory.CleanXPath() + " and value = "
                                                                         + (strValue ?? string.Empty).CleanXPath()
                                                                         + " and (not(prioritytable) or prioritytable = " + PriorityTableXPath + ")]");
            foreach (XPathNavigator xmlNode in xmlList)
            {
                if (xmlList.Count == 1 || funcDisambiguate(xmlNode))
                    return xmlNode;
            }
            return null;
        }

        private static bool HasPriorityTable(XPathNavigator x) => x.SelectSingleNodeAndCacheExpression("prioritytable") != null;
        private static bool HasGameplayOption(XPathNavigator x) => x.SelectSingleNodeAndCacheExpression("gameplayoption") != null;
        private static bool HasPriorityTableAndGameplayOption(XPathNavigator x) => HasPriorityTable(x) && HasGameplayOption(x);

        private XPathNavigator HeritagePriorityNode => FindPriorityNode(Heritage, GetPriority(Heritage), HasPriorityTable);
        private XPathNavigator TalentPriorityNode => FindPriorityNode(Talent, GetPriority(Talent), HasPriorityTable);

        /// <summary>Short summary of what one priority letter gives in one category, for the A–E table.</summary>
        public string DescribePriority(string strCategory, string strValue)
        {
            XPathNavigator xmlNode = FindPriorityNode(strCategory, strValue, strCategory == Heritage || strCategory == Talent ? (Func<XPathNavigator, bool>)HasPriorityTable : HasPriorityTableAndGameplayOption);
            string strName = xmlNode?.SelectSingleNodeAndCacheExpression("translate")?.Value ?? xmlNode?.SelectSingleNodeAndCacheExpression("name")?.Value ?? string.Empty;
            // Names look like "A - 24 (12) Attributes"; the letter is already the column.
            int intDash = strName.IndexOf(" - ", StringComparison.Ordinal);
            if (intDash >= 0 && intDash <= 2)
                strName = strName.Substring(intDash + 3);
            return strName;
        }

        /// <summary>Talents (Magician, Adept, ...) offered at a Talent priority letter regardless of metatype, for the table.</summary>
        public List<string> DescribeTalents(string strValue)
        {
            List<string> lstReturn = new List<string>();
            XPathNavigator xmlNode = FindPriorityNode(Talent, strValue, HasPriorityTable);
            if (xmlNode == null)
                return lstReturn;
            foreach (XPathNavigator xmlTalent in xmlNode.SelectAndCacheExpression("talents/talent"))
                lstReturn.Add(xmlTalent.SelectSingleNodeAndCacheExpression("translate")?.Value ?? xmlTalent.SelectSingleNodeAndCacheExpression("name")?.Value ?? string.Empty);
            return lstReturn;
        }

        #endregion Priorities

        #region Metatypes

        private void LoadInitialState()
        {
            if (UsesPriorityTables)
            {
                string[] astrExisting =
                {
                    _objCharacter.MetatypePriority, _objCharacter.AttributesPriority, _objCharacter.SpecialPriority,
                    _objCharacter.SkillsPriority, _objCharacter.ResourcesPriority
                };
                bool blnHasExisting = astrExisting.Any(x => !string.IsNullOrEmpty(x)) || !string.IsNullOrEmpty(_objCharacter.TalentPriority);
                for (int i = 0; i < PriorityCategories.Count; ++i)
                {
                    List<ListItem> lstOptions = GetPriorityOptions(PriorityCategories[i]);
                    string strValue = blnHasExisting && !string.IsNullOrEmpty(astrExisting[i])
                        ? lstOptions.FirstOrDefault(x => x.Value.ToString() == astrExisting[i][0].ToString(GlobalSettings.InvariantCultureInfo)).Value?.ToString()
                        : null;
                    _dicPriority[PriorityCategories[i]] = strValue ?? lstOptions.FirstOrDefault().Value?.ToString() ?? string.Empty;
                }
                if (!blnHasExisting && BuildMethod == CharacterBuildMethod.Priority)
                {
                    foreach (string strCategory in PriorityCategories)
                        ManagePriorityItems(strCategory);
                }
            }

            string strOldCategory = _objCharacter.MetatypeCategory;
            List<ListItem> lstCategories = GetCategories();
            Category = lstCategories.Any(x => x.Value.ToString() == strOldCategory) ? strOldCategory : lstCategories.FirstOrDefault().Value?.ToString() ?? string.Empty;
            List<ListItem> lstMetatypes = GetMetatypes();
            string strOldMetatype = _objCharacter.MetatypeGuid.ToString("D", GlobalSettings.InvariantCultureInfo);
            ListItem objOld = lstMetatypes.FirstOrDefault(x => x.Value.ToString().Equals(strOldMetatype, StringComparison.OrdinalIgnoreCase)
                                                           || x.Value.ToString() == _objCharacter.Metatype);
            Metatype = objOld.Value?.ToString() ?? lstMetatypes.FirstOrDefault().Value?.ToString() ?? string.Empty;
            if (UsesPriorityTables && objOld.Value == null)
            {
                // A fresh priority character starts on Human like the legacy default selection.
                ListItem objHuman = lstMetatypes.FirstOrDefault(x => x.Name == "Human");
                if (objHuman.Value != null)
                    Metatype = objHuman.Value.ToString();
            }
            string strOldMetavariant = _objCharacter.MetavariantGuid.ToString("D", GlobalSettings.InvariantCultureInfo);
            List<ListItem> lstMetavariants = GetMetavariants();
            Metavariant = lstMetavariants.FirstOrDefault(x => x.Value.ToString().Equals(strOldMetavariant, StringComparison.OrdinalIgnoreCase)).Value?.ToString()
                          ?? Utils.GuidEmptyString;
            if (UsesPriorityTables)
            {
                List<ListItem> lstTalents = GetTalents();
                string strOldTalent = _objCharacter.TalentPriority;
                TalentValue = lstTalents.FirstOrDefault(x => x.Value?.ToString() == strOldTalent && !string.IsNullOrEmpty(strOldTalent)).Value?.ToString()
                              ?? lstTalents.FirstOrDefault().Value?.ToString() ?? string.Empty;
                int i = 0;
                foreach (string strSkill in _objCharacter.PriorityBonusSkillList.ToList())
                {
                    if (i >= 3)
                        break;
                    string strLoop = strSkill;
                    if (ExoticSkill.IsExoticSkillName(_objCharacter, strLoop))
                    {
                        int intParenthesesIndex = strLoop.IndexOf(" (", StringComparison.OrdinalIgnoreCase);
                        if (intParenthesesIndex > 0)
                            strLoop = strLoop.Substring(0, intParenthesesIndex);
                    }
                    _astrSkills[i++] = strLoop;
                }
                NormalizeSkills();
            }
            if (!string.IsNullOrEmpty(CurrentPossessionMethod))
            {
                PossessionBased = true;
                PossessionMethod = CurrentPossessionMethod;
            }
        }

        /// <summary>Re-validates dependent choices after the Heritage / Talent priority, category or metatype changed.</summary>
        public void OnHeritageOrTalentChanged()
        {
            List<ListItem> lstCategories = GetCategories();
            if (lstCategories.Count > 0 && lstCategories.All(x => x.Value.ToString() != Category))
                Category = lstCategories[0].Value.ToString();
            OnCategoryChanged();
        }

        public void OnCategoryChanged()
        {
            List<ListItem> lstMetatypes = GetMetatypes();
            if (lstMetatypes.All(x => x.Value.ToString() != Metatype))
                Metatype = lstMetatypes.FirstOrDefault().Value?.ToString() ?? string.Empty;
            OnMetatypeChanged();
        }

        public void OnMetatypeChanged()
        {
            List<ListItem> lstMetavariants = GetMetavariants();
            if (lstMetavariants.All(x => x.Value.ToString() != Metavariant))
                Metavariant = lstMetavariants.FirstOrDefault().Value?.ToString() ?? Utils.GuidEmptyString;
            OnMetavariantChanged();
        }

        public void OnMetavariantChanged()
        {
            if (!UsesPriorityTables)
                return;
            List<ListItem> lstTalents = GetTalents();
            if (lstTalents.All(x => x.Value?.ToString() != TalentValue))
                TalentValue = lstTalents.FirstOrDefault().Value?.ToString() ?? string.Empty;
            NormalizeSkills();
        }

        /// <summary>Keeps the talent skill picks valid and distinct, like ProcessTalentsIndexChanged.</summary>
        public void NormalizeSkills()
        {
            TalentSkillChoice objChoice = GetTalentSkillChoice();
            List<string> lstValues = objChoice.Options.Select(x => x.Value.ToString()).ToList();
            for (int i = 0; i < 3; ++i)
            {
                if (i >= objChoice.Count || lstValues.Count == 0)
                {
                    _astrSkills[i] = string.Empty;
                    continue;
                }
                string strCurrent = _astrSkills[i];
                bool blnDuplicate = !string.IsNullOrEmpty(strCurrent) && _astrSkills.Take(i).Contains(strCurrent) && !ExoticSkill.IsExoticSkillName(_objCharacter, strCurrent);
                if (string.IsNullOrEmpty(strCurrent) || !lstValues.Contains(strCurrent) || blnDuplicate)
                    _astrSkills[i] = lstValues.FirstOrDefault(x => !_astrSkills.Take(i).Contains(x)) ?? lstValues[0];
            }
        }

        /// <summary>Metatype categories (legacy LoadMetatypes in Priority mode; "Show All" first in Karma mode).</summary>
        public List<ListItem> GetCategories()
        {
            List<ListItem> lstCategory = new List<ListItem>();
            HashSet<string> setSeen = new HashSet<string>();
            XPathNavigator xmlHeritage = UsesPriorityTables ? HeritagePriorityNode : null;
            foreach (XPathNavigator objXmlCategory in _xmlBaseMetatypeDataNode.SelectAndCacheExpression("categories/category"))
            {
                string strInnerText = objXmlCategory.Value;
                if (string.IsNullOrEmpty(strInnerText) || !setSeen.Add(strInnerText))
                    continue;
                bool blnKeep = false;
                foreach (XPathNavigator objXmlMetatype in _xmlBaseMetatypeDataNode.Select("metatypes/metatype[category = " + strInnerText.CleanXPath() + " and " + BookXPath + "]"))
                {
                    if (!UsesPriorityTables
                        || xmlHeritage?.TryGetNodeByNameOrId("metatypes/metatype", objXmlMetatype.SelectSingleNodeAndCacheExpression("id")?.Value) != null
                        || xmlHeritage?.TryGetNodeByNameOrId("metatypes/metatype", objXmlMetatype.SelectSingleNodeAndCacheExpression("name")?.Value) != null)
                    {
                        blnKeep = true;
                        break;
                    }
                }
                if (blnKeep)
                    lstCategory.Add(new ListItem(strInnerText, objXmlCategory.SelectSingleNodeAndCacheExpression("@translate")?.Value ?? strInnerText));
            }
            lstCategory.Sort(CompareListItems.CompareNames);
            if (!UsesPriorityTables)
                lstCategory.Insert(0, new ListItem("Show All", LanguageManager.GetString("String_ShowAll")));
            return lstCategory;
        }

        /// <summary>Metatypes of the current category (legacy PopulateMetatypes); value is the id (or name for priority-only entries).</summary>
        public List<ListItem> GetMetatypes(string strSearch = null)
        {
            List<ListItem> lstMetatype = new List<ListItem>();
            if (string.IsNullOrEmpty(Category) && string.IsNullOrEmpty(strSearch))
                return lstMetatype;
            if (UsesPriorityTables)
            {
                XPathNavigator xmlHeritage = HeritagePriorityNode;
                if (xmlHeritage == null)
                    return lstMetatype;
                foreach (XPathNavigator objXmlMetatype in _xmlBaseMetatypeDataNode.Select("metatypes/metatype[" + BookXPath + " and category = " + Category.CleanXPath() + "]"))
                {
                    string strId = objXmlMetatype.SelectSingleNodeAndCacheExpression("id")?.Value;
                    string strName = objXmlMetatype.SelectSingleNodeAndCacheExpression("name")?.Value;
                    if (!string.IsNullOrEmpty(strId) && xmlHeritage.TryGetNodeByNameOrId("metatypes/metatype", strId) != null)
                        lstMetatype.Add(new ListItem(strId, objXmlMetatype.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strName ?? LanguageManager.GetString("String_Unknown")));
                    else if (!string.IsNullOrEmpty(strName) && xmlHeritage.TryGetNodeByNameOrId("metatypes/metatype", strName) != null)
                        lstMetatype.Add(new ListItem(strName, objXmlMetatype.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strName));
                }
            }
            else
            {
                StringBuilder sbdFilter = new StringBuilder(BookXPath);
                if (!string.IsNullOrEmpty(Category) && Category != "Show All" && (GlobalSettings.SearchInCategoryOnly || string.IsNullOrEmpty(strSearch)))
                    sbdFilter.Append(" and category = ").Append(Category.CleanXPath());
                if (!string.IsNullOrEmpty(strSearch))
                    sbdFilter.Append(" and ").Append(CommonFunctions.GenerateSearchXPath(strSearch));
                foreach (XPathNavigator xmlMetatype in _xmlBaseMetatypeDataNode.Select("metatypes/metatype[" + sbdFilter + "]"))
                {
                    string strId = xmlMetatype.SelectSingleNodeAndCacheExpression("id")?.Value;
                    if (!string.IsNullOrEmpty(strId))
                        lstMetatype.Add(new ListItem(strId, xmlMetatype.SelectSingleNodeAndCacheExpression("translate")?.Value
                                                            ?? xmlMetatype.SelectSingleNodeAndCacheExpression("name")?.Value
                                                            ?? LanguageManager.GetString("String_Unknown")));
                }
            }
            lstMetatype.Sort(CompareListItems.CompareNames);
            return lstMetatype;
        }

        private XPathNavigator MetatypeNode(string strMetatype) => string.IsNullOrEmpty(strMetatype) ? null : _xmlBaseMetatypeDataNode.TryGetNodeByNameOrId("metatypes/metatype", strMetatype);

        /// <summary>Metavariants of a metatype ("None" first), legacy PopulateMetavariants.</summary>
        public List<ListItem> GetMetavariants(string strMetatype = null)
        {
            strMetatype = strMetatype ?? Metatype;
            List<ListItem> lstMetavariants = new List<ListItem> { new ListItem(Utils.GuidEmptyString, LanguageManager.GetString("String_None")) };
            XPathNavigator objXmlMetatype = MetatypeNode(strMetatype);
            if (objXmlMetatype == null)
                return lstMetavariants;
            XPathNavigator objXmlMetatypeBP = null;
            if (UsesPriorityTables)
            {
                objXmlMetatypeBP = HeritagePriorityNode?.TryGetNodeByNameOrId("metatypes/metatype", strMetatype);
                if (objXmlMetatypeBP == null)
                    return lstMetavariants;
            }
            foreach (XPathNavigator objXmlMetavariant in objXmlMetatype.Select("metavariants/metavariant[" + BookXPath + "]"))
            {
                string strId = objXmlMetavariant.SelectSingleNodeAndCacheExpression("id")?.Value;
                string strName = objXmlMetavariant.SelectSingleNodeAndCacheExpression("name")?.Value;
                string strDisplay = objXmlMetavariant.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strName ?? LanguageManager.GetString("String_Unknown");
                if (!UsesPriorityTables)
                {
                    if (!string.IsNullOrEmpty(strId))
                        lstMetavariants.Add(new ListItem(strId, strDisplay));
                }
                else if (!string.IsNullOrEmpty(strId) && objXmlMetatypeBP.TryGetNodeByNameOrId("metavariants/metavariant", strId) != null)
                    lstMetavariants.Add(new ListItem(strId, strDisplay));
                else if (!string.IsNullOrEmpty(strName) && objXmlMetatypeBP.TryGetNodeByNameOrId("metavariants/metavariant", strName) != null)
                    lstMetavariants.Add(new ListItem(strName, strDisplay));
            }
            return lstMetavariants;
        }

        /// <summary>Force / level box for force creatures (legacy nudForce): label, maximum, or null when not shown.</summary>
        public (string Label, int Maximum)? GetForceInfo(string strMetatype = null)
        {
            XPathNavigator objXmlMetatype = MetatypeNode(strMetatype ?? Metatype);
            if (objXmlMetatype == null)
                return null;
            string strEssMax = objXmlMetatype.SelectSingleNodeAndCacheExpression("essmax")?.Value ?? string.Empty;
            int intPos = strEssMax.IndexOf("D6", StringComparison.Ordinal);
            if (objXmlMetatype.SelectSingleNodeAndCacheExpression("forcecreature") == null && intPos == -1)
                return null;
            if (intPos != -1)
            {
                string strD6 = LanguageManager.GetString("String_D6");
                if (intPos > 0)
                {
                    --intPos;
                    return (strEssMax.Substring(intPos, 3).Replace("D6", strD6), char.GetNumericValue(strEssMax, intPos).StandardRound() * 6);
                }
                return (1.ToString(GlobalSettings.CultureInfo) + strD6, 6);
            }
            return (LanguageManager.GetString(objXmlMetatype.SelectSingleNodeAndCacheExpression("forceislevels") != null ? "String_Level" : "String_Force"), 100);
        }

        /// <summary>Spirits can be possession/inhabitation based (legacy chkPossessionBased, visible for "*Spirits" categories).</summary>
        public bool PossessionAvailable => !string.IsNullOrEmpty(Category) && Category.EndsWith("Spirits", StringComparison.Ordinal);

        /// <summary>Attribute ranges, karma, racial qualities and source of a metatype/metavariant (legacy RefreshSelectedMetatype).</summary>
        public MetatypeDetails GetDetails(string strMetatype = null, string strMetavariant = null)
        {
            strMetatype = strMetatype ?? Metatype;
            strMetavariant = strMetavariant ?? Metavariant;
            MetatypeDetails objReturn = new MetatypeDetails();
            XPathNavigator objXmlMetatype = MetatypeNode(strMetatype);
            if (objXmlMetatype == null)
                return objReturn;
            XPathNavigator objXmlMetavariant = string.IsNullOrEmpty(strMetavariant) || strMetavariant.IsEmptyGuid()
                ? null
                : objXmlMetatype.TryGetNodeByNameOrId("metavariants/metavariant", strMetavariant);
            XPathNavigator objNode = objXmlMetavariant ?? objXmlMetatype;
            objReturn.Name = objNode.SelectSingleNodeAndCacheExpression("translate")?.Value ?? objNode.SelectSingleNodeAndCacheExpression("name")?.Value ?? string.Empty;
            bool blnForce = !UsesPriorityTables && objXmlMetavariant != null && objXmlMetavariant.SelectSingleNodeAndCacheExpression("forcecreature") != null;
            if (!blnForce)
            {
                objReturn.Attributes = s_astrAttributes.Select(x => new MetatypeAttributeRange(x,
                    objNode.SelectSingleNode(x.ToLowerInvariant() + "min")?.Value ?? "0",
                    objNode.SelectSingleNode(x.ToLowerInvariant() + "max")?.Value ?? "0",
                    objNode.SelectSingleNode(x.ToLowerInvariant() + "aug")?.Value ?? "0")).ToList();
            }
            if (UsesPriorityTables)
            {
                XPathNavigator objXmlMetatypePriorityNode = HeritagePriorityNode?.TryGetNodeByNameOrId("metatypes/metatype", strMetatype);
                XPathNavigator objPriority = objXmlMetatypePriorityNode;
                if (objXmlMetavariant != null)
                {
                    objPriority = objXmlMetatypePriorityNode?.TryGetNodeByNameOrId("metavariants/metavariant", strMetavariant);
                    objReturn.NotSupported = objPriority == null;
                }
                objReturn.Karma = objPriority?.SelectSingleNodeAndCacheExpression("karma")?.Value ?? 0.ToString(GlobalSettings.CultureInfo);
                if (objPriority == null || !int.TryParse(objPriority.SelectSingleNodeAndCacheExpression("value")?.Value, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out int intSpecial))
                    intSpecial = 0;
                objReturn.SpecialAttributePoints = intSpecial + TalentSpecialAttributePoints(TalentValue);
            }
            else
                objReturn.Karma = objNode.SelectSingleNodeAndCacheExpression("karma")?.Value ?? 0.ToString(GlobalSettings.CultureInfo);

            Dictionary<string, int> dicQualities = new Dictionary<string, int>();
            foreach (XPathNavigator xmlQuality in objNode.SelectAndCacheExpression("qualities/*/quality"))
            {
                string strQuality = xmlQuality.Value;
                if (!GlobalSettings.Language.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                    strQuality = _xmlBaseQualityDataNode.TryGetNodeByNameOrId("qualities/quality", xmlQuality.Value)?.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strQuality;
                string strSelect = xmlQuality.SelectSingleNodeAndCacheExpression("@select")?.Value;
                if (!string.IsNullOrEmpty(strSelect))
                    strQuality += " (" + _objCharacter.TranslateExtra(strSelect) + ")";
                dicQualities[strQuality] = dicQualities.TryGetValue(strQuality, out int intCount) ? intCount + 1 : 1;
            }
            objReturn.Qualities = dicQualities.Select(x => x.Value > 1 ? x.Key + " " + x.Value.ToString(GlobalSettings.CultureInfo) : x.Key).ToList();
            string strSource = objNode.SelectSingleNodeAndCacheExpression("source")?.Value;
            string strPage = objNode.SelectSingleNodeAndCacheExpression("altpage")?.Value ?? objNode.SelectSingleNodeAndCacheExpression("page")?.Value;
            objReturn.Source = !string.IsNullOrEmpty(strSource) && !string.IsNullOrEmpty(strPage)
                ? SourceString.GetSourceString(strSource, strPage, GlobalSettings.Language, GlobalSettings.CultureInfo, _objCharacter).ToString()
                : LanguageManager.GetString("String_Unknown");
            return objReturn;
        }

        #endregion Metatypes

        #region Talents

        private XPathNavigator SelectedTalentNode(string strTalent = null)
        {
            strTalent = strTalent ?? TalentValue;
            if (string.IsNullOrEmpty(strTalent))
                return null;
            return TalentPriorityNode?.SelectSingleNode("talents/talent[value = " + strTalent.CleanXPath() + "]");
        }

        private int TalentSpecialAttributePoints(string strTalent)
            => int.TryParse(SelectedTalentNode(strTalent)?.SelectSingleNodeAndCacheExpression("specialattribpoints")?.Value, out int intTemp) ? intTemp : 0;

        /// <summary>Magic/Resonance types available at the Talent priority for the chosen metatype (legacy PopulateTalents).</summary>
        public List<ListItem> GetTalents()
        {
            List<ListItem> lstTalent = new List<ListItem>();
            XPathNavigator xmlTalentPriority = TalentPriorityNode;
            if (xmlTalentPriority != null)
            {
                string strBookXPath = BookXPath;
                foreach (XPathNavigator objXmlPriorityTalent in xmlTalentPriority.SelectAndCacheExpression("talents/talent"))
                {
                    XPathNavigator xmlQualitiesNode = objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("qualities");
                    if (xmlQualitiesNode != null && xmlQualitiesNode.SelectAndCacheExpression("quality").Cast<XPathNavigator>()
                                                                    .Any(x => _xmlBaseQualityDataNode.TryGetNodeByNameOrId("qualities/quality", x.Value, strBookXPath) == null))
                        continue;
                    XPathNavigator xmlForbiddenNode = objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("forbidden");
                    if (xmlForbiddenNode != null && MatchesOneOf(xmlForbiddenNode))
                        continue;
                    XPathNavigator xmlRequiredNode = objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("required");
                    if (xmlRequiredNode != null && !MatchesOneOf(xmlRequiredNode))
                        continue;
                    lstTalent.Add(new ListItem(objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("value")?.Value,
                                               objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("translate")?.Value
                                               ?? objXmlPriorityTalent.SelectSingleNodeAndCacheExpression("name")?.Value
                                               ?? LanguageManager.GetString("String_Unknown")));
                }
            }
            lstTalent.Sort(CompareListItems.CompareNames);
            return lstTalent;
        }

        private bool MatchesOneOf(XPathNavigator xmlNode)
        {
            foreach (XPathNavigator objXmlOneOf in xmlNode.SelectAndCacheExpression("oneof"))
            {
                foreach (XPathNavigator objXmlChild in objXmlOneOf.SelectChildren(XPathNodeType.Element))
                {
                    switch (objXmlChild.Name.ToUpperInvariant())
                    {
                        case "METATYPE" when objXmlChild.Value == Metatype:
                        case "METATYPECATEGORY" when objXmlChild.Value == Category:
                            return true;
                        case "METAVARIANT" when string.Equals(objXmlChild.Value, Metavariant, StringComparison.OrdinalIgnoreCase):
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The free skill picks of the selected talent (legacy ProcessTalentsIndexChanged).</summary>
        public TalentSkillChoice GetTalentSkillChoice()
        {
            TalentSkillChoice objReturn = new TalentSkillChoice();
            XPathNavigator xmlTalentNode = SelectedTalentNode();
            if (xmlTalentNode == null)
                return objReturn;
            string strSkillCount = xmlTalentNode.SelectSingleNodeAndCacheExpression("skillqty")?.Value
                                   ?? xmlTalentNode.SelectSingleNodeAndCacheExpression("skillgroupqty")?.Value ?? string.Empty;
            if (!int.TryParse(strSkillCount, out int intSkillCount) || intSkillCount <= 0)
                return objReturn;
            XPathNavigator xmlSkillTypeNode = xmlTalentNode.SelectSingleNodeAndCacheExpression("skilltype")
                                              ?? xmlTalentNode.SelectSingleNodeAndCacheExpression("skillgrouptype");
            string strSkillType = xmlSkillTypeNode?.Value ?? string.Empty;
            XPathNodeIterator objNodeList = xmlTalentNode.SelectAndCacheExpression("skillgroupchoices/skillgroup");
            XPathNodeIterator xmlSkillsList;
            switch (strSkillType.ToUpperInvariant())
            {
                case "MAGIC":
                    xmlSkillsList = _xmlBaseSkillDataNode.SelectAndCacheExpression("skills/skill[category = \"Magical Active\" or category = \"Pseudo-Magical Active\"]");
                    break;
                case "RESONANCE":
                    xmlSkillsList = _xmlBaseSkillDataNode.SelectAndCacheExpression("skills/skill[category = \"Resonance Active\" or skillgroup = \"Cracking\" or skillgroup = \"Electronics\"]");
                    break;
                case "MATRIX":
                    xmlSkillsList = _xmlBaseSkillDataNode.SelectAndCacheExpression("skills/skill[skillgroup = \"Cracking\" or skillgroup = \"Electronics\"]");
                    break;
                case "GROUPED":
                    xmlSkillsList = _xmlBaseSkillDataNode.Select(BuildFilter("skillgroups/name", objNodeList, ". = "));
                    break;
                case "SPECIFIC":
                    xmlSkillsList = _xmlBaseSkillDataNode.Select(BuildFilter("skills/skill", xmlTalentNode.SelectAndCacheExpression("skillchoices/skill"), "name = "));
                    break;
                case "XPATH":
                    string strXPath = xmlSkillTypeNode?.SelectSingleNodeAndCacheExpression("@xpath")?.Value;
                    xmlSkillsList = _xmlBaseSkillDataNode.Select(!string.IsNullOrEmpty(strXPath) ? "skills/skill[" + strXPath + "]" : "skills/skill");
                    strSkillType = "active";
                    break;
                default:
                    xmlSkillsList = _xmlBaseSkillDataNode.SelectAndCacheExpression("skills/skill");
                    break;
            }
            List<ListItem> lstSkills = new List<ListItem>();
            if (objNodeList.Count > 0)
            {
                foreach (XPathNavigator objXmlSkill in xmlSkillsList)
                    lstSkills.Add(new ListItem(objXmlSkill.Value, objXmlSkill.SelectSingleNodeAndCacheExpression("@translate")?.Value ?? objXmlSkill.Value));
            }
            else
            {
                foreach (XPathNavigator objXmlSkill in xmlSkillsList)
                {
                    string strName = objXmlSkill.SelectSingleNodeAndCacheExpression("name")?.Value ?? LanguageManager.GetString("String_Unknown");
                    lstSkills.Add(new ListItem(strName, objXmlSkill.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strName));
                }
            }
            lstSkills.Sort(CompareListItems.CompareNames);
            string strSkillVal = xmlTalentNode.SelectSingleNodeAndCacheExpression("skillval")?.Value
                                 ?? xmlTalentNode.SelectSingleNodeAndCacheExpression("skillgroupval")?.Value;
            objReturn.Count = Math.Min(3, intSkillCount);
            objReturn.Rating = int.TryParse(strSkillVal, out int intVal) ? intVal : 0;
            objReturn.IsSkillGroup = xmlTalentNode.SelectSingleNodeAndCacheExpression("skillval") == null && xmlTalentNode.SelectSingleNodeAndCacheExpression("skillgroupval") != null;
            objReturn.SkillType = strSkillType;
            objReturn.Options = lstSkills;
            objReturn.Description = string.Format(GlobalSettings.CultureInfo,
                string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("String_MetamagicSkillBase"), LanguageManager.GetString("String_MetamagicSkills")),
                strSkillCount, LanguageManager.GetString("String_MetamagicSkillType_" + strSkillType), strSkillVal);
            return objReturn;
        }

        private static string BuildFilter(string strBase, XPathNodeIterator objList, string strPrefix)
        {
            StringBuilder sbdFilter = new StringBuilder(strBase);
            if (objList.Count > 0)
            {
                sbdFilter.Append('[');
                foreach (XPathNavigator xmlLoop in objList)
                    sbdFilter.Append(strPrefix).Append(xmlLoop.Value.CleanXPath()).Append(" or ");
                sbdFilter.Length -= 4;
                sbdFilter.Append(']');
            }
            return sbdFilter.ToString();
        }

        /// <summary>Points to spend on special attributes for the current choices (legacy lblSpecialAttributes).</summary>
        public int SpecialAttributePoints => GetDetails().SpecialAttributePoints;

        #endregion Talents

        #region Apply

        /// <summary>
        /// Validates the choices and writes them to the character (legacy cmdOK_Click / MetatypeSelected of
        /// SelectMetatypePriority or SelectMetatypeKarma). Returns false if a message told the user what is missing.
        /// </summary>
        public Task<bool> ApplyAsync(CancellationToken token = default)
            => UsesPriorityTables ? ApplyPriorityAsync(token) : ApplyKarmaAsync(token);

        private static async Task ShowAsync(string strMessageKey, string strTitleKey, MessageIcon eIcon, CancellationToken token)
        {
            await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync(strMessageKey, token: token).ConfigureAwait(false),
                                         await LanguageManager.GetStringAsync(strTitleKey, token: token).ConfigureAwait(false),
                                         MessageButtons.OK, eIcon, token: token).ConfigureAwait(false);
        }

        private async Task<List<Quality>> CollectQualitiesToCheck(bool blnSkipHeritage, CancellationToken token)
        {
            List<Quality> lstQualitiesToCheck = new List<Quality>(await _objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false));
            await _objCharacter.Qualities.ForEachAsync(async (objQuality, t) =>
            {
                QualitySource eSource = await objQuality.GetOriginSourceAsync(t).ConfigureAwait(false);
                if (eSource == QualitySource.Improvement
                    || eSource == QualitySource.QualityLevelImprovement
                    || (blnSkipHeritage && eSource == QualitySource.Heritage)
                    || eSource == QualitySource.Metatype
                    || eSource == QualitySource.MetatypeRemovable
                    || eSource == QualitySource.MetatypeRemovedAtChargen)
                    return;
                XPathNavigator xmlBaseNode = await objQuality.GetNodeXPathAsync(token: t).ConfigureAwait(false);
                XPathNavigator xmlRestrictionNode = xmlBaseNode?.SelectSingleNodeAndCacheExpression("required", t);
                if (xmlRestrictionNode != null && (xmlRestrictionNode.SelectSingleNodeAndCacheExpression(".//metatype", t) != null
                                                   || xmlRestrictionNode.SelectSingleNodeAndCacheExpression(".//metavariant", t) != null))
                {
                    lstQualitiesToCheck.Add(objQuality);
                    return;
                }
                xmlRestrictionNode = xmlBaseNode?.SelectSingleNodeAndCacheExpression("forbidden", t);
                if (xmlRestrictionNode != null && (xmlRestrictionNode.SelectSingleNodeAndCacheExpression(".//metatype", t) != null
                                                   || xmlRestrictionNode.SelectSingleNodeAndCacheExpression(".//metavariant", t) != null))
                    lstQualitiesToCheck.Add(objQuality);
            }, token).ConfigureAwait(false);
            return lstQualitiesToCheck;
        }

        private async Task RemoveQualitiesFailingRequirements(List<Quality> lstQualitiesToCheck, CancellationToken token)
        {
            foreach (Quality objQuality in lstQualitiesToCheck)
            {
                XPathNavigator objLoopNode = await objQuality.GetNodeXPathAsync(token: token).ConfigureAwait(false);
                // Set strIgnoreQuality to quality's name to make sure limit counts are not an issue
                if (objLoopNode != null && !await objLoopNode.RequirementsMetAsync(_objCharacter, strIgnoreQuality: await objQuality.GetNameAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false))
                    await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
            }
        }

        private async Task<bool> ApplyKarmaAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string strSelectedMetatype = Metatype;
            XmlNode xmlMetatypesNode = (await _objCharacter.LoadDataAsync(_strXmlFile, token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/metatypes");
            XmlNode objXmlMetatype = string.IsNullOrEmpty(strSelectedMetatype) ? null : xmlMetatypesNode.TryGetNodeByNameOrId("metatype", strSelectedMetatype);
            if (objXmlMetatype == null)
            {
                await ShowAsync("Message_Metatype_SelectMetatype", "MessageTitle_Metatype_SelectMetatype", MessageIcon.Information, token).ConfigureAwait(false);
                return false;
            }
            string strSelectedMetatypeCategory = Category == "Show All" ? objXmlMetatype["category"]?.InnerText ?? Category : Category;
            string strSelectedMetavariant = string.IsNullOrEmpty(Metavariant) ? Utils.GuidEmptyString : Metavariant;
            XmlNode xmlQualities = (await _objCharacter.LoadDataAsync("qualities.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/qualities");
            XmlNode xmlPowers = (await _objCharacter.LoadDataAsync("critterpowers.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/powers");
            XmlNode xmlKnowledge = (await _objCharacter.LoadDataAsync("skills.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/knowledgeskills");

            IAsyncDisposable objLocker = await _objCharacter.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // Remove all priority-given qualities (relevant when switching from Priority/Sum-to-Ten to Karma)
                for (int i = await _objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                {
                    if (i >= await _objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false))
                        continue;
                    Quality objQuality = await _objCharacter.Qualities.GetValueAtAsync(i, token).ConfigureAwait(false);
                    if (await objQuality.GetOriginSourceAsync(token).ConfigureAwait(false) == QualitySource.Heritage)
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                }

                // If this is a Shapeshifter, a Metavariant must be selected. Default to Human if None is selected.
                if (strSelectedMetatypeCategory == "Shapeshifter" && strSelectedMetavariant.IsEmptyGuid())
                    strSelectedMetavariant = objXmlMetatype.SelectSingleNode("metavariants/metavariant[name = \"Human\"]/id")?.InnerText ?? Utils.GuidEmptyString;
                if (_objCharacter.MetatypeGuid.ToString("D", GlobalSettings.InvariantCultureInfo) != strSelectedMetatype
                    || _objCharacter.MetavariantGuid.ToString("D", GlobalSettings.InvariantCultureInfo) != strSelectedMetavariant)
                {
                    List<Quality> lstQualitiesToCheck = await CollectQualitiesToCheck(false, token).ConfigureAwait(false);
                    int intForce = GetForceInfo() != null ? Force : 0;
                    await _objCharacter.CreateAsync(strSelectedMetatypeCategory, strSelectedMetatype, strSelectedMetavariant, objXmlMetatype, intForce,
                                                    xmlQualities, xmlPowers, xmlKnowledge, PossessionAvailable && PossessionBased ? PossessionMethod : string.Empty,
                                                    token).ConfigureAwait(false);
                    await RemoveQualitiesFailingRequirements(lstQualitiesToCheck, token).ConfigureAwait(false);
                }

                // Flip all attribute, skill, and skill group points to karma levels (relevant when switching from Priority/Sum-to-Ten to Karma)
                await (await _objCharacter.AttributeSection.GetAttributesAsync(token).ConfigureAwait(false)).ForEachWithSideEffectsAsync(async (objAttrib, t) =>
                {
                    int intBase = await objAttrib.GetBaseAsync(t).ConfigureAwait(false);
                    await objAttrib.SetBaseAsync(0, t).ConfigureAwait(false);
                    await objAttrib.ModifyKarmaAsync(intBase, t).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
                await (await _objCharacter.SkillsSection.GetSkillsAsync(token).ConfigureAwait(false)).ForEachWithSideEffectsAsync(async (objSkill, t) =>
                {
                    int intBase = await objSkill.GetBasePointsAsync(t).ConfigureAwait(false);
                    await objSkill.SetBasePointsAsync(0, t).ConfigureAwait(false);
                    await objSkill.ModifyKarmaPointsAsync(intBase, t).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
                await (await _objCharacter.SkillsSection.GetSkillGroupsAsync(token).ConfigureAwait(false)).ForEachWithSideEffectsAsync(async (objGroup, t) =>
                {
                    int intBase = await objGroup.GetBasePointsAsync(t).ConfigureAwait(false);
                    await objGroup.SetBasePointsAsync(0, t).ConfigureAwait(false);
                    await objGroup.ModifyKarmaPointsAsync(intBase, t).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
            return true;
        }

        private async Task<bool> ApplyPriorityAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsSumToTen)
            {
                int intSumToTen = SumToTen;
                int intOldSumToTen = SumToTenTarget;
                if (intSumToTen != intOldSumToTen)
                {
                    await MessageBoxes.ShowAsync(string.Format(GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_SumtoTen", token: token).ConfigureAwait(false), intOldSumToTen, intSumToTen), token: token).ConfigureAwait(false);
                    return false;
                }
            }

            List<ListItem> lstTalents = GetTalents();
            if (lstTalents.Count == 0 || string.IsNullOrEmpty(TalentValue))
            {
                await ShowAsync("Message_Metatype_NoTalentChoices", "MessageTitle_Metatype_NoTalentChoices", MessageIcon.Warning, token).ConfigureAwait(false);
                return false;
            }
            if (lstTalents.All(x => x.Value?.ToString() != TalentValue))
            {
                await ShowAsync("Message_Metatype_SelectTalent", "MessageTitle_Metatype_SelectTalent", MessageIcon.Information, token).ConfigureAwait(false);
                return false;
            }

            TalentSkillChoice objSkillChoice = GetTalentSkillChoice();
            string strSkill1 = objSkillChoice.Count > 0 ? GetSkill(0) : string.Empty;
            string strSkill2 = objSkillChoice.Count > 1 ? GetSkill(1) : string.Empty;
            string strSkill3 = objSkillChoice.Count > 2 ? GetSkill(2) : string.Empty;
            if ((objSkillChoice.Count > 0 && string.IsNullOrEmpty(strSkill1))
                || (objSkillChoice.Count > 1 && string.IsNullOrEmpty(strSkill2))
                || (objSkillChoice.Count > 2 && string.IsNullOrEmpty(strSkill3)))
            {
                await ShowAsync("Message_Metatype_SelectSkill", "MessageTitle_Metatype_SelectSkill", MessageIcon.Information, token).ConfigureAwait(false);
                return false;
            }

            async Task<string> ResolveExotic(string strSkill)
            {
                if (!await ExoticSkill.IsExoticSkillNameAsync(_objCharacter, strSkill, token).ConfigureAwait(false) || strSkill.Contains(" ("))
                    return strSkill;
                string strSpecific = ExoticSpecificPicker != null ? await ExoticSpecificPicker(strSkill).ConfigureAwait(false) : null;
                return string.IsNullOrEmpty(strSpecific) ? null : strSkill + " (" + strSpecific + ")";
            }
            strSkill1 = await ResolveExotic(strSkill1).ConfigureAwait(false);
            if (strSkill1 == null)
                return false;
            strSkill2 = await ResolveExotic(strSkill2).ConfigureAwait(false);
            if (strSkill2 == null)
                return false;
            strSkill3 = await ResolveExotic(strSkill3).ConfigureAwait(false);
            if (strSkill3 == null)
                return false;

            if ((!string.IsNullOrEmpty(strSkill1) && (strSkill1 == strSkill2 || strSkill1 == strSkill3))
                || (!string.IsNullOrEmpty(strSkill2) && strSkill2 == strSkill3))
            {
                await ShowAsync("Message_Metatype_Duplicate", "MessageTitle_Metatype_Duplicate", MessageIcon.Information, token).ConfigureAwait(false);
                return false;
            }

            string strSelectedMetatype = Metatype;
            XmlNode xmlMetatypesNode = (await _objCharacter.LoadDataAsync(_strXmlFile, token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/metatypes");
            XmlNode objXmlMetatype = string.IsNullOrEmpty(strSelectedMetatype) ? null : xmlMetatypesNode.TryGetNodeByNameOrId("metatype", strSelectedMetatype);
            if (objXmlMetatype == null)
            {
                await ShowAsync("Message_Metatype_SelectMetatype", "MessageTitle_Metatype_SelectMetatype", MessageIcon.Information, token).ConfigureAwait(false);
                return false;
            }
            if (GetDetails().NotSupported)
            {
                await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync("Message_MetatypeNotSupported", token: token).ConfigureAwait(false),
                                             await LanguageManager.GetStringAsync("String_NotSupported", token: token).ConfigureAwait(false),
                                             MessageButtons.OK, MessageIcon.Error, token: token).ConfigureAwait(false);
                return false;
            }
            strSelectedMetatype = objXmlMetatype["id"]?.InnerText ?? Utils.GuidEmptyString;
            XmlNode xmlQualityDocumentQualitiesNode = (await _objCharacter.LoadDataAsync("qualities.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/qualities");
            XmlNode xmlPowers = (await _objCharacter.LoadDataAsync("critterpowers.xml", token: token).ConfigureAwait(false)).SelectSingleNode("/chummer/powers");

            IAsyncDisposable objLocker = await _objCharacter.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // Clear out all priority-only qualities that the character bought normally (relevant when switching from Karma to Priority/Sum-to-Ten)
                for (int i = await _objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                {
                    if (i >= await _objCharacter.Qualities.GetCountAsync(token).ConfigureAwait(false))
                        continue;
                    Quality objQuality = await _objCharacter.Qualities.GetValueAtAsync(i, token).ConfigureAwait(false);
                    if (objQuality.OriginSource == QualitySource.Selected
                        && (await objQuality.GetNodeXPathAsync(token: token).ConfigureAwait(false))?.SelectSingleNode("onlyprioritygiven") != null)
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                }

                string strSelectedMetavariant = Metavariant ?? Utils.GuidEmptyString;
                string strSelectedMetatypeCategory = Category;
                // If this is a Shapeshifter, a Metavariant must be selected. Default to Human if None is selected.
                if (strSelectedMetatypeCategory == "Shapeshifter" && strSelectedMetavariant.IsEmptyGuid())
                    strSelectedMetavariant = "Human";
                XmlNode objXmlMetavariant = objXmlMetatype.TryGetNodeByNameOrId("metavariants/metavariant", strSelectedMetavariant);
                strSelectedMetavariant = objXmlMetavariant?["id"]?.InnerText ?? Utils.GuidEmptyString;
                int intForce = GetForceInfo() != null ? Force : 0;

                if (_objCharacter.MetatypeGuid.ToString("D", GlobalSettings.InvariantCultureInfo) != strSelectedMetatype
                    || (await _objCharacter.GetMetavariantGuidAsync(token).ConfigureAwait(false)).ToString("D", GlobalSettings.InvariantCultureInfo) != strSelectedMetavariant)
                {
                    List<Quality> lstQualitiesToCheck = await CollectQualitiesToCheck(true, token).ConfigureAwait(false);
                    await _objCharacter.CreateAsync(strSelectedMetatypeCategory, strSelectedMetatype, strSelectedMetavariant, objXmlMetatype, intForce,
                                                    xmlQualityDocumentQualitiesNode, xmlPowers, null,
                                                    PossessionAvailable && PossessionBased ? PossessionMethod : string.Empty, token).ConfigureAwait(false);
                    await RemoveQualitiesFailingRequirements(lstQualitiesToCheck, token).ConfigureAwait(false);
                }

                string strOldSpecialPriority = await _objCharacter.GetSpecialPriorityAsync(token).ConfigureAwait(false);
                string strOldTalentPriority = await _objCharacter.GetTalentPriorityAsync(token).ConfigureAwait(false);

                // Check if skill choices have changed by comparing with current priority bonus skill list BEFORE clearing it
                List<string> lstCurrentPrioritySkills = new List<string>();
                await _objCharacter.PriorityBonusSkillList.ForEachAsync(x => lstCurrentPrioritySkills.Add(x), token).ConfigureAwait(false);
                List<string> lstNewPrioritySkills = new[] { strSkill1, strSkill2, strSkill3 }.Where(x => !string.IsNullOrEmpty(x)).ToList();
                bool blnSkillChoicesChanged = !lstCurrentPrioritySkills.SequenceEqual(lstNewPrioritySkills);

                await _objCharacter.SetMetatypePriorityAsync(GetPriority(Heritage), token).ConfigureAwait(false);
                await _objCharacter.SetAttributesPriorityAsync(GetPriority(Attributes), token).ConfigureAwait(false);
                await _objCharacter.SetSpecialPriorityAsync(GetPriority(Talent), token).ConfigureAwait(false);
                await _objCharacter.SetSkillsPriorityAsync(GetPriority(Skills), token).ConfigureAwait(false);
                await _objCharacter.SetResourcesPriorityAsync(GetPriority(Resources), token).ConfigureAwait(false);
                await _objCharacter.SetTalentPriorityAsync(TalentValue ?? string.Empty, token).ConfigureAwait(false);
                await _objCharacter.PriorityBonusSkillList.ClearAsync(token).ConfigureAwait(false);
                foreach (string strSkill in lstNewPrioritySkills)
                    await _objCharacter.PriorityBonusSkillList.AddAsync(strSkill, token: token).ConfigureAwait(false);

                string strPriorityTable = PriorityTableXPath;
                // Set starting nuyen
                XPathNavigator xmlResourcesPriority = FindPriorityNode(Resources, await _objCharacter.GetResourcesPriorityAsync(token).ConfigureAwait(false), HasPriorityTableAndGameplayOption);
                if (xmlResourcesPriority != null)
                {
                    decimal decResources = 0;
                    if (xmlResourcesPriority.TryGetDecFieldQuickly("resources", ref decResources))
                    {
                        await _objCharacter.SetNuyenAsync(decResources, token).ConfigureAwait(false);
                        await _objCharacter.SetStartingNuyenAsync(decResources, token).ConfigureAwait(false);
                    }
                }

                XmlNode charNode = strSelectedMetatypeCategory == "Shapeshifter" || strSelectedMetavariant.IsEmptyGuid()
                    ? objXmlMetatype
                    : objXmlMetavariant ?? objXmlMetatype;

                int intSpecialAttribPoints = 0;
                bool boolHalveAttributePriorityPoints = charNode.NodeExists("halveattributepoints");
                string strNewSpecialPriority = await _objCharacter.GetSpecialPriorityAsync(token).ConfigureAwait(false);
                string strNewTalentPriority = await _objCharacter.GetTalentPriorityAsync(token).ConfigureAwait(false);
                XPathNavigator xmlTalentPriorityNode = FindPriorityNode(Talent, strNewSpecialPriority, HasGameplayOption)
                    ?.SelectSingleNode("talents/talent[value = " + strNewTalentPriority.CleanXPath() + "]");
                if (strOldSpecialPriority != strNewSpecialPriority || strOldTalentPriority != strNewTalentPriority || blnSkillChoicesChanged)
                {
                    List<Quality> lstOldPriorityQualities = await (await _objCharacter.GetQualitiesAsync(token).ConfigureAwait(false))
                                                                  .ToListAsync(x => x.OriginSource == QualitySource.Heritage, token: token).ConfigureAwait(false);
                    List<Weapon> lstWeapons = new List<Weapon>(1);
                    bool blnRemoveFreeSkills = true;
                    if (xmlTalentPriorityNode != null)
                    {
                        string strUnlockSkillsFilter = string.Empty;
                        if (lstNewPrioritySkills.Count > 0)
                        {
                            strUnlockSkillsFilter = "bonus/unlockskills[contains(text(), ',') and (contains(text(), " + strSkill1.CleanXPath();
                            if (!string.IsNullOrEmpty(strSkill2))
                                strUnlockSkillsFilter += ") or contains(text(), " + strSkill2.CleanXPath();
                            if (!string.IsNullOrEmpty(strSkill3))
                                strUnlockSkillsFilter += ") or contains(text(), " + strSkill3.CleanXPath();
                            strUnlockSkillsFilter += "))]";
                        }

                        // Create the Qualities that come with the Talent.
                        foreach (XPathNavigator objXmlQualityItem in xmlTalentPriorityNode.SelectAndCacheExpression("qualities/quality", token))
                        {
                            XmlNode objXmlQuality = xmlQualityDocumentQualitiesNode.TryGetNodeByNameOrId("quality", objXmlQualityItem.Value);
                            Quality objQuality = new Quality(_objCharacter);
                            bool blnDoRemove = false;
                            Quality objExistingQuality;
                            try
                            {
                                string strForceValue = objXmlQualityItem.SelectSingleNodeAndCacheExpression("@select", token)?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(strForceValue) && !string.IsNullOrEmpty(strUnlockSkillsFilter))
                                {
                                    // If we have selected a priority entry where we get bonus ranks to a skill or skill group that isn't necessarily given to us,
                                    // then make sure that that skill is what is given to us in the end. Mainly relevant for aspected magicians.
                                    XmlNodeList xmlRelevantUnlocksNodesList = objXmlQuality.SelectNodes(strUnlockSkillsFilter);
                                    if (xmlRelevantUnlocksNodesList?.Count > 0)
                                    {
                                        List<string> lstToPush = new List<string>(xmlRelevantUnlocksNodesList.Count);
                                        foreach (XmlNode xmlLoopNode in xmlRelevantUnlocksNodesList)
                                        {
                                            string[] astrOptions = xmlLoopNode.InnerText.Split(',');
                                            if (!string.IsNullOrEmpty(strSkill1) && astrOptions.Contains(strSkill1))
                                                lstToPush.Add(strSkill1);
                                            else if (!string.IsNullOrEmpty(strSkill2) && astrOptions.Contains(strSkill2))
                                                lstToPush.Add(strSkill2);
                                            else if (!string.IsNullOrEmpty(strSkill3) && astrOptions.Contains(strSkill3))
                                                lstToPush.Add(strSkill3);
                                        }
                                        // Reverse order because we process bonus nodes from top to bottom, and this text will be saved in a FILO stack
                                        lstToPush.Reverse();
                                        ConcurrentStack<string> stkPushText = await _objCharacter.GetPushTextAsync(token).ConfigureAwait(false);
                                        foreach (string strToPush in lstToPush)
                                            stkPushText.Push(strToPush);
                                    }
                                }

                                await objQuality.CreateAsync(objXmlQuality, QualitySource.Heritage, lstWeapons, strForceValue, token: token).ConfigureAwait(false);
                                QualityType eQualityType = await objQuality.GetTypeAsync(token).ConfigureAwait(false);
                                objExistingQuality = await lstOldPriorityQualities.FirstOrDefaultAsync(
                                    async (x, t) => x.SourceID == objQuality.SourceID && x.Extra == objQuality.Extra
                                                                                        && await x.GetTypeAsync(t).ConfigureAwait(false) == eQualityType,
                                    token: token).ConfigureAwait(false);
                                if (objExistingQuality == null)
                                    await _objCharacter.Qualities.AddAsync(objQuality, token: token).ConfigureAwait(false);
                                else
                                    blnDoRemove = true;
                            }
                            catch
                            {
                                await objQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }

                            if (blnDoRemove)
                            {
                                lstOldPriorityQualities.Remove(objExistingQuality);
                                await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                            }
                        }

                        foreach (Quality objQuality in lstOldPriorityQualities)
                            await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);

                        // Set starting magic
                        int intTemp = 1;
                        int intMax = 0;
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("magic", ref intTemp))
                            intTemp = 1;
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("maxmagic", ref intMax))
                            intMax = Math.Max(await CommonFunctions.ExpressionToIntAsync(charNode["magmax"]?.InnerText, intForce, token: token).ConfigureAwait(false), intTemp);
                        await _objCharacter.MAG.AssignLimitsAsync(intTemp, intMax, intMax, token).ConfigureAwait(false);
                        await _objCharacter.SetFreeSpellsAsync(xmlTalentPriorityNode.TryGetInt32FieldQuickly("spells", ref intTemp) ? intTemp : 0, token).ConfigureAwait(false);
                        // Set starting resonance
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("resonance", ref intTemp))
                            intTemp = 1;
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("maxresonance", ref intMax))
                            intMax = Math.Max(await CommonFunctions.ExpressionToIntAsync(charNode["resmax"]?.InnerText, intForce, token: token).ConfigureAwait(false), intTemp);
                        await _objCharacter.RES.AssignLimitsAsync(intTemp, intMax, intMax, token).ConfigureAwait(false);
                        await _objCharacter.SetCFPLimitAsync(xmlTalentPriorityNode.TryGetInt32FieldQuickly("cfp", ref intTemp) ? intTemp : 0, token).ConfigureAwait(false);
                        // Set starting depth
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("depth", ref intTemp))
                            intTemp = 1;
                        if (!xmlTalentPriorityNode.TryGetInt32FieldQuickly("maxdepth", ref intMax))
                            intMax = Math.Max(await CommonFunctions.ExpressionToIntAsync(charNode["depmax"]?.InnerText, intForce, token: token).ConfigureAwait(false), intTemp);
                        await _objCharacter.DEP.AssignLimitsAsync(intTemp, intMax, intMax, token).ConfigureAwait(false);
                        await _objCharacter.SetAINormalProgramLimitAsync(xmlTalentPriorityNode.TryGetInt32FieldQuickly("ainormalprogramlimit", ref intTemp) ? intTemp : 0, token).ConfigureAwait(false);
                        await _objCharacter.SetAIAdvancedProgramLimitAsync(xmlTalentPriorityNode.TryGetInt32FieldQuickly("aiadvancedprogramlimit", ref intTemp) ? intTemp : 0, token).ConfigureAwait(false);

                        // Set Free Skills/Skill Groups
                        (int intFreeLevels, Improvement.ImprovementType eType) = FreeSkillLevels(xmlTalentPriorityNode);
                        blnRemoveFreeSkills = false;
                        await AddFreeSkills(intFreeLevels, eType, strSkill1, strSkill2, strSkill3, token).ConfigureAwait(false);

                        if (int.TryParse(xmlTalentPriorityNode.SelectSingleNodeAndCacheExpression("specialattribpoints", token)?.Value, out int intTalentSpecialAttribPoints))
                            intSpecialAttribPoints += intTalentSpecialAttribPoints;
                    }

                    if (blnRemoveFreeSkills)
                        await RemoveFreeSkillImprovements(token).ConfigureAwait(false);

                    // Handle skill choice changes even when talent priority doesn't change
                    if (blnSkillChoicesChanged && !blnRemoveFreeSkills && xmlTalentPriorityNode != null)
                    {
                        (int intFreeLevels, Improvement.ImprovementType eType) = FreeSkillLevels(xmlTalentPriorityNode);
                        if (intFreeLevels > 0)
                        {
                            await RemoveFreeSkillImprovements(token).ConfigureAwait(false);
                            await AddFreeSkills(intFreeLevels, eType, strSkill1, strSkill2, strSkill3, token).ConfigureAwait(false);
                        }
                    }
                    // Add any created Weapons to the character.
                    foreach (Weapon objWeapon in lstWeapons)
                        await _objCharacter.Weapons.AddAsync(objWeapon, token: token).ConfigureAwait(false);
                }

                // Set Special Attributes and Karma
                int intMetatypeBP = 0;
                XPathNavigator xmlBaseMetatypePriority = FindPriorityNode(Heritage, await _objCharacter.GetMetatypePriorityAsync(token).ConfigureAwait(false), HasPriorityTable);
                if (xmlBaseMetatypePriority != null)
                {
                    XPathNavigator objXmlMetatypePriorityNode = xmlBaseMetatypePriority.TryGetNodeByNameOrId("metatypes/metatype", strSelectedMetatype)
                                                                ?? xmlBaseMetatypePriority.TryGetNodeByNameOrId("metatypes/metatype", await _objCharacter.GetMetatypeAsync(token).ConfigureAwait(false));
                    if (!strSelectedMetavariant.IsEmptyGuid())
                        objXmlMetatypePriorityNode = objXmlMetatypePriorityNode?.TryGetNodeByNameOrId("metavariants/metavariant", strSelectedMetavariant)
                                                     ?? objXmlMetatypePriorityNode?.TryGetNodeByNameOrId("metavariants/metavariant", await _objCharacter.GetMetavariantAsync(token).ConfigureAwait(false));
                    if (objXmlMetatypePriorityNode != null)
                    {
                        if (int.TryParse(objXmlMetatypePriorityNode.SelectSingleNodeAndCacheExpression("value", token)?.Value, out int intTemp))
                            intSpecialAttribPoints += intTemp;
                        if (int.TryParse(objXmlMetatypePriorityNode.SelectSingleNodeAndCacheExpression("karma", token)?.Value, out int intTemp2))
                            intMetatypeBP += intTemp2;
                    }
                }
                // Legacy only adds the talent's special points while (re)creating the talent; keep them when nothing about it changed.
                if (!(strOldSpecialPriority != strNewSpecialPriority || strOldTalentPriority != strNewTalentPriority || blnSkillChoicesChanged)
                    && int.TryParse(xmlTalentPriorityNode?.SelectSingleNodeAndCacheExpression("specialattribpoints", token)?.Value, out int intKeptTalentPoints))
                    intSpecialAttribPoints += intKeptTalentPoints;

                await _objCharacter.SetSpecialAsync(intSpecialAttribPoints, token).ConfigureAwait(false);
                await _objCharacter.SetTotalSpecialAsync(intSpecialAttribPoints, token).ConfigureAwait(false);
                await _objCharacter.SetMetatypeBPAsync(intMetatypeBP, token).ConfigureAwait(false);

                // Set Attributes
                XPathNavigator objXmlAttributesPriority = FindPriorityNode(Attributes, await _objCharacter.GetAttributesPriorityAsync(token).ConfigureAwait(false), HasPriorityTableAndGameplayOption);
                if (objXmlAttributesPriority != null)
                {
                    int intAttributes = 0;
                    objXmlAttributesPriority.TryGetInt32FieldQuickly("attributes", ref intAttributes);
                    if (boolHalveAttributePriorityPoints)
                        intAttributes /= 2;
                    await _objCharacter.SetAttributesAsync(intAttributes, token).ConfigureAwait(false);
                    await _objCharacter.SetTotalAttributesAsync(intAttributes, token).ConfigureAwait(false);
                }

                // Set Skills and Skill Groups
                XPathNavigator objXmlSkillsPriority = FindPriorityNode(Skills, await _objCharacter.GetSkillsPriorityAsync(token).ConfigureAwait(false), HasPriorityTableAndGameplayOption);
                if (objXmlSkillsPriority != null)
                {
                    int intTemp = 0;
                    if (objXmlSkillsPriority.TryGetInt32FieldQuickly("skills", ref intTemp))
                        _objCharacter.SkillsSection.SkillPointsMaximum = intTemp;
                    if (objXmlSkillsPriority.TryGetInt32FieldQuickly("skillgroups", ref intTemp))
                        _objCharacter.SkillsSection.SkillGroupPointsMaximum = intTemp;
                }

                // Sprites can never have Physical Attributes
                if (await _objCharacter.GetDEPEnabledAsync(token).ConfigureAwait(false) || _objCharacter.Metatype.EndsWith("Sprite", StringComparison.Ordinal))
                {
                    await _objCharacter.BOD.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                    await _objCharacter.AGI.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                    await _objCharacter.REA.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                    await _objCharacter.STR.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                    await _objCharacter.MAG.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                    await _objCharacter.MAGAdept.AssignBaseKarmaLimitsAsync(0, 0, 0, 0, 0, token).ConfigureAwait(false);
                }

                // Set free contact points
                await _objCharacter.OnPropertyChangedAsync(nameof(Character.ContactPoints), token).ConfigureAwait(false);

                await ConvertKarmaToPointsAsync(token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
            return true;
        }

        private static (int Levels, Improvement.ImprovementType Type) FreeSkillLevels(XPathNavigator xmlTalentPriorityNode)
        {
            int intFreeLevels = 0;
            Improvement.ImprovementType eType = Improvement.ImprovementType.SkillBase;
            XPathNavigator objTalentSkillValNode = xmlTalentPriorityNode.SelectSingleNodeAndCacheExpression("skillval");
            if (objTalentSkillValNode == null || !int.TryParse(objTalentSkillValNode.Value, out intFreeLevels))
            {
                objTalentSkillValNode = xmlTalentPriorityNode.SelectSingleNodeAndCacheExpression("skillgroupval");
                if (objTalentSkillValNode != null && int.TryParse(objTalentSkillValNode.Value, out intFreeLevels))
                    eType = Improvement.ImprovementType.SkillGroupBase;
            }
            return (intFreeLevels, eType);
        }

        private async Task RemoveFreeSkillImprovements(CancellationToken token)
        {
            await ImprovementManager.RemoveImprovementsAsync(_objCharacter,
                await (await _objCharacter.GetImprovementsAsync(token).ConfigureAwait(false))
                      .ToListAsync(x => x.ImproveSource == Improvement.ImprovementSource.Heritage
                                        && (x.ImproveType == Improvement.ImprovementType.SkillBase || x.ImproveType == Improvement.ImprovementType.SkillGroupBase), token)
                      .ConfigureAwait(false), token: token).ConfigureAwait(false);
        }

        private async Task AddFreeSkills(int intFreeLevels, Improvement.ImprovementType eType, string strSkill1, string strSkill2, string strSkill3, CancellationToken token)
        {
            List<Improvement> lstOldFreeSkillImprovements = await ImprovementManager.GetCachedImprovementListForValueOfAsync(_objCharacter, eType, token: token).ConfigureAwait(false);
            lstOldFreeSkillImprovements.RemoveAll(x => x.ImproveSource != Improvement.ImprovementSource.Heritage);
            if (intFreeLevels == 0)
            {
                await ImprovementManager.RemoveImprovementsAsync(_objCharacter, lstOldFreeSkillImprovements, token: token).ConfigureAwait(false);
                return;
            }
            bool blnCommit = false;
            try
            {
                foreach (string strSkill in new[] { strSkill1, strSkill2, strSkill3 })
                {
                    if (string.IsNullOrEmpty(strSkill))
                        continue;
                    Improvement objOld = lstOldFreeSkillImprovements.Find(x => x.ImprovedName == strSkill && x.Value == intFreeLevels);
                    if (objOld != null)
                    {
                        lstOldFreeSkillImprovements.Remove(objOld);
                        continue;
                    }
                    blnCommit = true;
                    // Add exotic skills if we are increasing their base level
                    if (await ExoticSkill.IsExoticSkillNameAsync(_objCharacter, strSkill, token).ConfigureAwait(false)
                        && await _objCharacter.SkillsSection.GetActiveSkillAsync(strSkill, token).ConfigureAwait(false) == null)
                    {
                        string strSkillName = strSkill;
                        string strSkillSpecific = string.Empty;
                        int intParenthesesIndex = strSkillName.IndexOf(" (", StringComparison.OrdinalIgnoreCase);
                        if (intParenthesesIndex > 0)
                        {
                            strSkillSpecific = strSkillName.Substring(intParenthesesIndex + 2, strSkillName.Length - intParenthesesIndex - 3);
                            strSkillName = strSkillName.Substring(0, intParenthesesIndex);
                        }
                        await _objCharacter.SkillsSection.AddExoticSkillAsync(strSkillName, strSkillSpecific, token).ConfigureAwait(false);
                    }
                    await ImprovementManager.CreateImprovementAsync(_objCharacter, strSkill, Improvement.ImprovementSource.Heritage, string.Empty,
                                                                    eType, string.Empty, intFreeLevels, token: token).ConfigureAwait(false);
                }
                if (lstOldFreeSkillImprovements.Count > 0)
                    await ImprovementManager.RemoveImprovementsAsync(_objCharacter, lstOldFreeSkillImprovements, token: token).ConfigureAwait(false);
            }
            catch
            {
                if (blnCommit)
                    await ImprovementManager.RollbackAsync(_objCharacter, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            if (blnCommit)
                await ImprovementManager.CommitAsync(_objCharacter, token).ConfigureAwait(false);
        }

        /// <summary>
        /// If the character looks converted from Karma to Priority/Sum-to-Ten, moves karma-bought attributes, skills and groups
        /// onto points as efficiently as possible (tail of the legacy MetatypeSelected, quirks included).
        /// </summary>
        private async Task ConvertKarmaToPointsAsync(CancellationToken token)
        {
            bool blnDoSwitch = false;
            await _objCharacter.AttributeSection.AttributeList.ForEachWithBreakAsync(async (objAttribute, t) =>
            {
                if (await objAttribute.GetBaseAsync(t).ConfigureAwait(false) > 0)
                {
                    blnDoSwitch = false;
                    return false;
                }
                if (await objAttribute.GetKarmaAsync(t).ConfigureAwait(false) > 0)
                    blnDoSwitch = true;
                return true;
            }, token).ConfigureAwait(false);
            if (blnDoSwitch)
                await ShiftAttributesAsync(_objCharacter.AttributeSection.AttributeList, await _objCharacter.GetTotalAttributesAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);

            blnDoSwitch = false;
            await _objCharacter.AttributeSection.SpecialAttributeList.ForEachWithBreakAsync(async (objAttribute, t) =>
            {
                if (await objAttribute.GetBaseAsync(t).ConfigureAwait(false) > 0)
                {
                    blnDoSwitch = false;
                    return false;
                }
                if (await objAttribute.GetKarmaAsync(t).ConfigureAwait(false) > 0)
                    blnDoSwitch = true;
                return true;
            }, token).ConfigureAwait(false);
            if (blnDoSwitch)
                await ShiftAttributesAsync(_objCharacter.AttributeSection.SpecialAttributeList, await _objCharacter.GetTotalSpecialAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);

            blnDoSwitch = false;
            await (await _objCharacter.SkillsSection.GetSkillGroupsAsync(token).ConfigureAwait(false)).ForEachWithBreakAsync(async (objGroup, t) =>
            {
                if (await objGroup.GetBaseAsync(t).ConfigureAwait(false) > 0)
                {
                    blnDoSwitch = false;
                    return false;
                }
                if (await objGroup.GetKarmaAsync(t).ConfigureAwait(false) > 0)
                    blnDoSwitch = true;
                return true;
            }, token).ConfigureAwait(false);
            if (blnDoSwitch)
            {
                int intPointsSpent = 0;
                while (intPointsSpent < _objCharacter.SkillsSection.SkillGroupPointsMaximum)
                {
                    SkillGroup objGroupToShift = null;
                    await (await _objCharacter.SkillsSection.GetSkillGroupsAsync(token).ConfigureAwait(false)).ForEachWithSideEffectsAsync(async (objGroup, t) =>
                    {
                        if (await objGroup.GetKarmaAsync(t).ConfigureAwait(false) > 0
                            && (objGroupToShift == null || await objGroupToShift.GetRatingAsync(t).ConfigureAwait(false) < await objGroup.GetRatingAsync(t).ConfigureAwait(false)))
                            objGroupToShift = objGroup;
                    }, token).ConfigureAwait(false);
                    if (objGroupToShift == null)
                        break;
                    int intKarma = Math.Min(await objGroupToShift.GetKarmaAsync(token).ConfigureAwait(false), _objCharacter.SkillsSection.SkillGroupPointsMaximum - intPointsSpent);
                    await objGroupToShift.ModifyKarmaAsync(-intKarma, token).ConfigureAwait(false);
                    await objGroupToShift.ModifyBaseAsync(intKarma, token).ConfigureAwait(false);
                    intPointsSpent += intKarma;
                }
            }

            blnDoSwitch = false;
            await (await _objCharacter.SkillsSection.GetSkillsAsync(token).ConfigureAwait(false)).ForEachWithBreakAsync(async (objSkill, t) =>
            {
                if (await objSkill.GetBaseAsync(t).ConfigureAwait(false) > 0)
                {
                    blnDoSwitch = false;
                    return false;
                }
                if (await objSkill.GetKarmaAsync(t).ConfigureAwait(false) > 0)
                    blnDoSwitch = true;
                return true;
            }, token).ConfigureAwait(false);
            if (blnDoSwitch)
            {
                int intPointsSpent = 0;
                SkillsSection objSkillsSection = await _objCharacter.GetSkillsSectionAsync(token).ConfigureAwait(false);
                // Legacy caps this loop at the skill *group* points maximum.
                while (intPointsSpent < objSkillsSection.SkillGroupPointsMaximum)
                {
                    Skill objSkillToShift = null;
                    int intSkillToShiftKarma = 0;
                    await (await objSkillsSection.GetSkillsAsync(token).ConfigureAwait(false)).ForEachWithSideEffectsAsync(async (objSkill, t) =>
                    {
                        int intLoopKarma = await objSkill.GetKarmaAsync(t).ConfigureAwait(false);
                        if (intLoopKarma > 0 && (objSkillToShift == null || await objSkillToShift.GetRatingAsync(t).ConfigureAwait(false) < await objSkill.GetRatingAsync(t).ConfigureAwait(false)))
                        {
                            objSkillToShift = objSkill;
                            intSkillToShiftKarma = intLoopKarma;
                        }
                    }, token).ConfigureAwait(false);
                    if (objSkillToShift == null)
                        break;
                    int intKarma = Math.Min(intSkillToShiftKarma, objSkillsSection.SkillGroupPointsMaximum - intPointsSpent);
                    await objSkillToShift.SetKarmaAsync(intSkillToShiftKarma - intKarma, token).ConfigureAwait(false);
                    await objSkillToShift.SetBaseAsync(await objSkillToShift.GetBaseAsync(token).ConfigureAwait(false) + intKarma, token).ConfigureAwait(false);
                    intPointsSpent += intKarma;
                }
            }
        }

        private async Task ShiftAttributesAsync(ThreadSafeObservableCollection<CharacterAttrib> lstAttributes, int intTotal, CancellationToken token)
        {
            int intPointsSpent = 0;
            while (intPointsSpent < intTotal)
            {
                CharacterAttrib objAttributeToShift = null;
                await lstAttributes.ForEachWithSideEffectsAsync(async (objAttribute, t) =>
                {
                    if (await objAttribute.GetKarmaAsync(t).ConfigureAwait(false) > 0
                        && (objAttributeToShift == null || await objAttributeToShift.GetValueAsync(t).ConfigureAwait(false) < await objAttribute.GetValueAsync(t).ConfigureAwait(false)))
                        objAttributeToShift = objAttribute;
                }, token).ConfigureAwait(false);
                if (objAttributeToShift == null)
                    break;
                // Legacy uses the primary attribute total here even for special attributes.
                int intKarma = Math.Min(await objAttributeToShift.GetKarmaAsync(token).ConfigureAwait(false), await _objCharacter.GetTotalAttributesAsync(token).ConfigureAwait(false) - intPointsSpent);
                await objAttributeToShift.ModifyKarmaAsync(-intKarma, token).ConfigureAwait(false);
                await objAttributeToShift.ModifyBaseAsync(intKarma, token).ConfigureAwait(false);
                intPointsSpent += intKarma;
            }
        }

        #endregion Apply
    }
}
