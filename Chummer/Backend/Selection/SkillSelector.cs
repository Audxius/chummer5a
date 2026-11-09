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
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Skills;

namespace Chummer.Selection
{
    /// <summary>Filters for choosing a skill (what the SelectSkill dialog was told).</summary>
    public sealed class SkillSelectionFilter
    {
        public string IncludeCategory { get; set; } = string.Empty;
        public string ExcludeCategory { get; set; } = string.Empty;
        public string IncludeSkillGroup { get; set; } = string.Empty;
        public string ExcludeSkillGroup { get; set; } = string.Empty;
        public string LimitToSkill { get; set; } = string.Empty;
        public string ExcludeSkill { get; set; } = string.Empty;
        public string LinkedAttribute { get; set; } = string.Empty;
        public string ForceSkill { get; set; } = string.Empty;
        public int MinimumRating { get; set; }
        public int MaximumRating { get; set; } = int.MaxValue;

        /// <summary>XPath condition built from a &lt;limittocategories&gt; node ("category = 'a' or category = 'b'").</summary>
        public string LimitToCategories { get; private set; } = string.Empty;

        public void SetLimitToCategories(XmlNode xmlNode)
        {
            XmlNodeList xmlCategoryList = xmlNode?.SelectNodes("category");
            if (xmlCategoryList == null)
                return;
            StringBuilder sbdLimit = new StringBuilder();
            foreach (XmlNode objNode in xmlCategoryList)
                sbdLimit.Append("category = ").Append(objNode.InnerText.CleanXPath()).Append(" or ");
            if (sbdLimit.Length > 0)
                sbdLimit.Length -= 4;
            LimitToCategories = sbdLimit.ToString();
        }
    }

    /// <summary>
    /// The option lists behind "choose a skill" prompts, lifted from the WinForms SelectSkill dialog so every UI offers
    /// exactly the same choices. Values are (skill name, is exotic); an exotic skill needs a specific as well, chosen
    /// from <see cref="GetExoticSpecifics"/>, and the final selection is "Name (Specific)".
    /// </summary>
    public static class SkillSelector
    {
        private static IEnumerable<string> Split(string strList) => strList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());

        private static void AppendAnyOf(StringBuilder sbdFilter, string strList, string strField)
        {
            if (string.IsNullOrEmpty(strList))
                return;
            sbdFilter.Append(" and (").Append(string.Join(" or ", Split(strList).Select(x => strField + " = " + x.CleanXPath()))).Append(')');
        }

        private static void AppendNoneOf(StringBuilder sbdFilter, string strList, string strField)
        {
            if (string.IsNullOrEmpty(strList))
                return;
            sbdFilter.Append(" and (").Append(string.Join(" and ", Split(strList).Select(x => strField + " != " + x.CleanXPath()))).Append(')');
        }

        /// <returns>(forced exotic?, exotic base name) plus the option list, sorted by display name.</returns>
        public static List<ListItem> GetSkillOptions(Character objCharacter, SkillSelectionFilter objFilter, out bool blnForcedExotic, out string strForcedExoticName)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            objFilter = objFilter ?? new SkillSelectionFilter();
            blnForcedExotic = false;
            strForcedExoticName = string.Empty;
            XPathNavigator xmlDocument = objCharacter.LoadDataXPath("skills.xml");
            string strBooks = objCharacter.Settings.BookXPath();
            XPathNodeIterator objXmlSkillList;
            if (!string.IsNullOrEmpty(objFilter.ForceSkill))
            {
                (blnForcedExotic, strForcedExoticName) = ExoticSkill.IsExoticSkillNameTuple(objCharacter, objFilter.ForceSkill);
                objXmlSkillList = blnForcedExotic
                    ? xmlDocument.Select("/chummer/skills/skill[name = " + strForcedExoticName.CleanXPath() + " and exotic = 'True' and " + strBooks + "]")
                    : xmlDocument.Select("/chummer/skills/skill[name = " + objFilter.ForceSkill.CleanXPath() + " and not(exotic = 'True') and " + strBooks + "]");
            }
            else if (!string.IsNullOrEmpty(objFilter.LimitToCategories))
            {
                objXmlSkillList = xmlDocument.Select("/chummer/skills/skill[" + objFilter.LimitToCategories + " and " + strBooks + "]");
            }
            else
            {
                StringBuilder sbdFilter = new StringBuilder();
                // Without a minimum rating exotic skills are offered normally (a second choice picks the specific).
                if (objFilter.MinimumRating > 0)
                    sbdFilter.Append("not(exotic = 'True') and ");
                sbdFilter.Append(strBooks);
                AppendAnyOf(sbdFilter, objFilter.IncludeCategory, "category");
                AppendNoneOf(sbdFilter, objFilter.ExcludeCategory, "category");
                AppendAnyOf(sbdFilter, objFilter.IncludeSkillGroup, "skillgroup");
                AppendNoneOf(sbdFilter, objFilter.ExcludeSkillGroup, "skillgroup");
                AppendAnyOf(sbdFilter, objFilter.LinkedAttribute, "attribute");
                AppendAnyOf(sbdFilter, objFilter.LimitToSkill, "name");
                AppendNoneOf(sbdFilter, objFilter.ExcludeSkill, "name");
                objXmlSkillList = xmlDocument.Select("/chummer/skills/skill[" + sbdFilter + "]");
            }

            List<ListItem> lstSkills = new List<ListItem>();
            foreach (XPathNavigator objXmlSkill in objXmlSkillList)
            {
                string strXmlSkillName = objXmlSkill.SelectSingleNode("name")?.Value;
                Skill objExistingSkill = objCharacter.SkillsSection.GetActiveSkill(strXmlSkillName);
                if (objExistingSkill == null)
                {
                    if (objFilter.MinimumRating > 0)
                        continue;
                }
                else if (objExistingSkill.TotalBaseRating < objFilter.MinimumRating || objExistingSkill.TotalBaseRating > objFilter.MaximumRating)
                    continue;
                lstSkills.Add(new ListItem(new ValueTuple<string, bool>(strXmlSkillName, objXmlSkill.SelectSingleNode("exotic")?.Value == bool.TrueString),
                                           objXmlSkill.SelectSingleNode("translate")?.Value ?? strXmlSkillName));
            }

            // With a minimum rating, the character's own exotic skills that qualify are offered.
            if (objFilter.MinimumRating > 0)
            {
                HashSet<string> setAddedExotics = new HashSet<string>();
                foreach (Skill objSkill in objCharacter.SkillsSection.Skills.ToList())
                {
                    if (!objSkill.IsExoticSkill || !(objSkill is ExoticSkill objExoticSkill))
                        continue;
                    string strLoopName = objSkill.Name;
                    if (setAddedExotics.Contains(strLoopName))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.ForceSkill) && objFilter.ForceSkill != objExoticSkill.DictionaryKey)
                        continue;
                    int intBaseRating = objSkill.TotalBaseRating;
                    if (intBaseRating < objFilter.MinimumRating || intBaseRating > objFilter.MaximumRating)
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.IncludeCategory) && !Split(objFilter.IncludeCategory).Contains(objSkill.SkillCategory))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.ExcludeCategory) && Split(objFilter.ExcludeCategory).Contains(objSkill.SkillCategory))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.IncludeSkillGroup) && !Split(objFilter.IncludeSkillGroup).Contains(objSkill.SkillGroup))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.ExcludeSkillGroup) && Split(objFilter.ExcludeSkillGroup).Contains(objSkill.SkillGroup))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.LimitToSkill) && !Split(objFilter.LimitToSkill).Contains(strLoopName))
                        continue;
                    if (!string.IsNullOrEmpty(objFilter.ExcludeSkill) && Split(objFilter.ExcludeSkill).Contains(strLoopName))
                        continue;
                    lstSkills.Add(new ListItem(new ValueTuple<string, bool>(strLoopName, true), objExoticSkill.CurrentDisplayName));
                    setAddedExotics.Add(strLoopName);
                }
            }

            lstSkills.Sort(CompareListItems.CompareNames);
            return lstSkills;
        }

        /// <summary>Possible specifics for an exotic skill (weapons using it, its listed specs, and the character's own).</summary>
        public static List<ListItem> GetExoticSpecifics(Character objCharacter, string strSkillName, SkillSelectionFilter objFilter)
        {
            objFilter = objFilter ?? new SkillSelectionFilter();
            List<ListItem> lstReturn = new List<ListItem>();
            if (string.IsNullOrEmpty(strSkillName))
                return lstReturn;
            if (objFilter.MinimumRating <= 0)
            {
                CharacterSettings objSettings = objCharacter.Settings;
                foreach (XPathNavigator xmlWeapon in objCharacter.LoadDataXPath("weapons.xml")
                                                                 .Select("/chummer/weapons/weapon[(category = " + (strSkillName + "s").CleanXPath()
                                                                         + " or useskill = " + strSkillName.CleanXPath() + ") and "
                                                                         + objSettings.BookXPath(false) + "]"))
                {
                    string strName = xmlWeapon.SelectSingleNode("name")?.Value;
                    if (!string.IsNullOrEmpty(strName))
                        lstReturn.Add(new ListItem(strName, xmlWeapon.SelectSingleNode("translate")?.Value ?? strName));
                }
                foreach (XPathNavigator xmlSpec in objCharacter.LoadDataXPath("skills.xml")
                                                               .Select("/chummer/skills/skill[name = " + strSkillName.CleanXPath() + " and "
                                                                       + objSettings.BookXPath() + "]/specs/spec"))
                {
                    string strName = xmlSpec.Value;
                    if (!string.IsNullOrEmpty(strName))
                        lstReturn.Add(new ListItem(strName, xmlSpec.SelectSingleNode("@translate")?.Value ?? strName));
                }
            }
            foreach (Skill objSkill in objCharacter.SkillsSection.Skills.ToList())
            {
                if (objSkill.Name != strSkillName || !(objSkill is ExoticSkill objExoticSkill))
                    continue;
                string strSpecific = objExoticSkill.Specific;
                if (objFilter.MinimumRating > 0 || objFilter.MaximumRating < int.MaxValue)
                {
                    int intTotalBaseRating = objSkill.TotalBaseRating;
                    if (intTotalBaseRating < objFilter.MinimumRating)
                        continue;
                    if (intTotalBaseRating > objFilter.MaximumRating)
                    {
                        lstReturn.RemoveAll(x => x.Value.ToString() == strSpecific);
                        continue;
                    }
                }
                lstReturn.Add(new ListItem(strSpecific, objExoticSkill.CurrentDisplaySpecific));
            }
            lstReturn.Sort(Comparer<ListItem>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name)));
            return lstReturn;
        }
    }
}
