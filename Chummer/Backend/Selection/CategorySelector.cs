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
using System.Xml.XPath;

namespace Chummer.Selection
{
    /// <summary>Option lists of the small category pickers, lifted from SelectSkillGroup / SelectSpellCategory / SelectWeaponCategory.</summary>
    public static class CategorySelector
    {
        public static List<ListItem> GetSkillGroups(Character objCharacter, string strForceValue, string strExcludeCategory)
        {
            List<ListItem> lstGroups = new List<ListItem>();
            if (!string.IsNullOrEmpty(strForceValue))
            {
                lstGroups.Add(new ListItem(strForceValue, strForceValue));
                return lstGroups;
            }
            XPathNavigator xmlDocument = XmlManager.LoadXPath("skills.xml", objCharacter?.Settings.EnabledCustomDataDirectoryPaths);
            string strExclude = string.Empty;
            if (!string.IsNullOrEmpty(strExcludeCategory))
            {
                string[] astrExcluded = strExcludeCategory.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (astrExcluded.Length > 0)
                    strExclude = "(" + string.Join(" and ", astrExcluded.Select(x => "category != " + x.CleanXPath())) + ") and ";
            }
            foreach (XPathNavigator objXmlGroup in xmlDocument.Select("/chummer/skillgroups/name"))
            {
                if (!string.IsNullOrEmpty(strExcludeCategory)
                    && xmlDocument.SelectSingleNode("/chummer/skills/skill[" + strExclude + "skillgroup = " + objXmlGroup.Value.CleanXPath() + "]") == null)
                    continue;
                lstGroups.Add(new ListItem(objXmlGroup.Value, objXmlGroup.SelectSingleNode("@translate")?.Value ?? objXmlGroup.Value));
            }
            lstGroups.Sort(CompareListItems.CompareNames);
            return lstGroups;
        }

        public static List<ListItem> GetSpellCategories(Character objCharacter, string strForceCategory, IEnumerable<string> lstExclude)
        {
            HashSet<string> setExclude = new HashSet<string>(lstExclude ?? Enumerable.Empty<string>());
            XPathNavigator xmlDocument = XmlManager.LoadXPath("spells.xml", objCharacter?.Settings.EnabledCustomDataDirectoryPaths);
            List<ListItem> lstReturn = new List<ListItem>();
            foreach (XPathNavigator objXmlCategory in !string.IsNullOrEmpty(strForceCategory)
                         ? xmlDocument.Select("/chummer/categories/category[. = " + strForceCategory.CleanXPath() + "]")
                         : xmlDocument.Select("/chummer/categories/category"))
            {
                if (setExclude.Contains(objXmlCategory.Value))
                    continue;
                lstReturn.Add(new ListItem(objXmlCategory.Value, objXmlCategory.SelectSingleNode("@translate")?.Value ?? objXmlCategory.Value));
            }
            return lstReturn;
        }

        public static List<ListItem> GetWeaponCategories(Character objCharacter, string strOnlyCategory, string strWeaponType)
        {
            XPathNavigator xmlDocument = XmlManager.LoadXPath("weapons.xml", objCharacter?.Settings.EnabledCustomDataDirectoryPaths);
            List<ListItem> lstReturn = new List<ListItem>();
            foreach (XPathNavigator objXmlCategory in !string.IsNullOrEmpty(strOnlyCategory)
                         ? xmlDocument.Select("/chummer/categories/category[. = " + strOnlyCategory.CleanXPath() + "]")
                         : xmlDocument.Select("/chummer/categories/category"))
            {
                if (!string.IsNullOrEmpty(strWeaponType) && objXmlCategory.Value != "Exotic Ranged Weapons")
                {
                    string strType = objXmlCategory.SelectSingleNode("@type")?.Value;
                    if (string.IsNullOrEmpty(strType) || strType != strWeaponType)
                        continue;
                }
                lstReturn.Add(new ListItem(objXmlCategory.Value, objXmlCategory.SelectSingleNode("@translate")?.Value ?? objXmlCategory.Value));
            }
            if (lstReturn.Count == 0 && (strOnlyCategory == "Cyberware" || strOnlyCategory == "Cyberweapon"))
                lstReturn.Add(new ListItem("Cyberware", LanguageManager.GetString("String_Cyberware")));
            return lstReturn;
        }
    }
}
