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
using System.Linq;
using System.Text;
using System.Xml.XPath;

namespace Chummer.Selection
{
    /// <summary>
    /// One browsable catalogue (metamagics, arts, martial arts...): which data nodes are offered, under what filter, and
    /// what "can this runner take it" means. Lifted from the WinForms selection forms so every UI offers the same list.
    /// </summary>
    public sealed class DataBrowser
    {
        private readonly Character _objCharacter;
        private readonly XPathNavigator _xmlRoot;
        private readonly string _strItemXPath;
        private readonly string _strFilter;

        public DataBrowser(Character objCharacter, XPathNavigator xmlRoot, string strItemXPath, string strFilter, string strLocalName)
        {
            _objCharacter = objCharacter;
            _xmlRoot = xmlRoot;
            _strItemXPath = strItemXPath;
            _strFilter = strFilter;
            LocalName = strLocalName ?? string.Empty;
        }

        /// <summary>What one entry is called ("Metamagic", "Art"...), used in titles and requirement messages.</summary>
        public string LocalName { get; }

        /// <summary>Entries (value = id), sorted by name; optionally only those whose requirements the runner meets.</summary>
        public List<ListItem> Options(bool blnOnlyAvailable)
        {
            List<ListItem> lstReturn = new List<ListItem>();
            if (_xmlRoot == null)
                return lstReturn;
            string strUnknown = LanguageManager.GetString("String_Unknown");
            foreach (XPathNavigator xmlItem in _xmlRoot.Select(_strItemXPath + (string.IsNullOrEmpty(_strFilter) ? string.Empty : "[" + _strFilter + "]")))
            {
                string strId = xmlItem.SelectSingleNode("id")?.Value;
                if (string.IsNullOrEmpty(strId))
                    continue;
                if (blnOnlyAvailable && !xmlItem.RequirementsMet(_objCharacter))
                    continue;
                lstReturn.Add(new ListItem(strId, xmlItem.SelectSingleNode("translate")?.Value ?? xmlItem.SelectSingleNode("name")?.Value ?? strUnknown));
            }
            lstReturn.Sort(CompareListItems.CompareNames);
            return lstReturn;
        }

        public XPathNavigator Node(string strId) => _xmlRoot?.TryGetNodeByNameOrId(_strItemXPath, strId);

        /// <summary>Source/page and any notes for the details pane.</summary>
        public string Details(string strId)
        {
            XPathNavigator xmlItem = Node(strId);
            if (xmlItem == null)
                return string.Empty;
            StringBuilder sbdReturn = new StringBuilder();
            sbdReturn.AppendLine(xmlItem.SelectSingleNode("translate")?.Value ?? xmlItem.SelectSingleNode("name")?.Value);
            string strSource = xmlItem.SelectSingleNode("source")?.Value;
            string strPage = xmlItem.SelectSingleNode("altpage")?.Value ?? xmlItem.SelectSingleNode("page")?.Value;
            if (!string.IsNullOrEmpty(strSource))
                sbdReturn.AppendLine(strSource + " " + strPage);
            foreach (string strElement in new[] { "category", "adeptway", "cost", "karmacost", "rating" })
            {
                string strValue = xmlItem.SelectSingleNode(strElement)?.Value;
                if (!string.IsNullOrEmpty(strValue))
                    sbdReturn.AppendLine(strElement + ": " + strValue);
            }
            if (xmlItem.SelectSingleNode("required") != null || xmlItem.SelectSingleNode("forbidden") != null)
                sbdReturn.AppendLine().AppendLine(xmlItem.RequirementsMet(_objCharacter) ? "✔ Requirements met" : "✖ Requirements not met");
            return sbdReturn.ToString().TrimEnd();
        }

        /// <summary>Final check on accept (shows the engine's own message when requirements fail).</summary>
        public bool Validate(string strId)
        {
            XPathNavigator xmlItem = Node(strId);
            return xmlItem != null && xmlItem.RequirementsMet(_objCharacter, strLocalName: LocalName);
        }

        // ---------- catalogues ----------

        public static DataBrowser Metamagic(Character objCharacter, InitiationGrade objGrade)
        {
            bool blnTechnomancer = objGrade.Technomancer;
            XPathNavigator xmlDocument = objCharacter.LoadDataXPath(blnTechnomancer ? "echoes.xml" : "metamagic.xml");
            string strFilter = objCharacter.Settings.BookXPath();
            if (objCharacter.MAGEnabled)
            {
                bool blnIsMagician = objCharacter.MagicianEnabled;
                if (blnIsMagician != objCharacter.AdeptEnabled)
                    strFilter += blnIsMagician ? " and magician = " + bool.TrueString.CleanXPath() : " and adept = " + bool.TrueString.CleanXPath();
            }
            List<string> lstLimits = ImprovementManager.GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.MetamagicLimit)
                                                       .Where(x => x.Rating == objGrade.Grade && !string.IsNullOrEmpty(x.ImprovedName))
                                                       .Select(x => x.ImprovedName).ToList();
            if (lstLimits.Count > 0)
                strFilter += " and (" + string.Join(" or ", lstLimits.Select(x => "name = " + x.CleanXPath())) + ")";
            return new DataBrowser(objCharacter, xmlDocument, blnTechnomancer ? "/chummer/echoes/echo" : "/chummer/metamagics/metamagic", strFilter,
                                   LanguageManager.GetString(blnTechnomancer ? "String_Echo" : "String_Metamagic"));
        }

        public static DataBrowser Art(Character objCharacter, SelectArtMode eMode)
        {
            string strBooks = objCharacter.Settings.BookXPath();
            switch (eMode)
            {
                case SelectArtMode.Enhancement:
                    return new DataBrowser(objCharacter, objCharacter.LoadDataXPath("powers.xml").SelectSingleNode("/chummer/enhancements"), "enhancement", strBooks,
                                           LanguageManager.GetString("String_Enhancement"));
                case SelectArtMode.Enchantment:
                    return new DataBrowser(objCharacter, objCharacter.LoadDataXPath("spells.xml").SelectSingleNode("/chummer/spells"), "spell",
                                           "category = 'Enchantments' and (" + strBooks + ")", LanguageManager.GetString("String_Enchantment"));
                case SelectArtMode.Ritual:
                    return new DataBrowser(objCharacter, objCharacter.LoadDataXPath("spells.xml").SelectSingleNode("/chummer/spells"), "spell",
                                           "category = 'Rituals' and (" + strBooks + ")", LanguageManager.GetString("String_Ritual"));
                default:
                    return new DataBrowser(objCharacter, objCharacter.LoadDataXPath("metamagic.xml").SelectSingleNode("/chummer/arts"), "art", strBooks,
                                           LanguageManager.GetString("String_Art"));
            }
        }

        public static DataBrowser Mentor(Character objCharacter, string strXmlFile)
        {
            return new DataBrowser(objCharacter, objCharacter.LoadDataXPath(string.IsNullOrEmpty(strXmlFile) ? "mentors.xml" : strXmlFile).SelectSingleNode("/chummer"),
                                   "mentors/mentor", objCharacter.Settings.BookXPath(),
                                   LanguageManager.GetString(strXmlFile == "paragons.xml" ? "String_Paragon" : "String_MentorSpirit"));
        }

        /// <summary>The two sets of choices a mentor offers (filtered to adept/magician options the runner can use).</summary>
        public (List<ListItem> Set1, List<ListItem> Set2) MentorChoices(string strId)
        {
            List<ListItem> lstSet1 = new List<ListItem>();
            List<ListItem> lstSet2 = new List<ListItem>();
            XPathNavigator xmlChoices = Node(strId)?.SelectSingleNode("choices");
            if (xmlChoices == null)
                return (lstSet1, lstSet2);
            foreach (XPathNavigator objChoice in xmlChoices.Select("choice"))
            {
                string strName = objChoice.SelectSingleNode("name")?.Value ?? string.Empty;
                if ((!_objCharacter.AdeptEnabled && strName.StartsWith("Adept:", System.StringComparison.Ordinal))
                    || (!_objCharacter.MagicianEnabled && strName.StartsWith("Magician:", System.StringComparison.Ordinal)))
                    continue;
                ListItem objItem = new ListItem(strName, objChoice.SelectSingleNode("translate")?.Value ?? strName);
                if (objChoice.SelectSingleNode("@set")?.Value == "2")
                    lstSet2.Add(objItem);
                else
                    lstSet1.Add(objItem);
            }
            return (lstSet1, lstSet2);
        }

        public static DataBrowser MartialArt(Character objCharacter, bool blnShowQualities = false)
        {
            string strFilter = objCharacter.Settings.BookXPath()
                               + (blnShowQualities ? " and isquality = " + bool.TrueString.CleanXPath() : " and not(isquality = " + bool.TrueString.CleanXPath() + ")");
            return new DataBrowser(objCharacter, objCharacter.LoadDataXPath("martialarts.xml").SelectSingleNode("/chummer/martialarts"), "martialart", strFilter,
                                   LanguageManager.GetString("String_MartialArt"));
        }
    }
}
