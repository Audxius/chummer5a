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
using Chummer.Backend.Enums;
using Chummer.Backend.Uniques;

namespace Chummer.Selection
{
    /// <summary>One optional choice the spell picker offers (SelectSpell's Limited / Extended / Alchemical / Free check boxes).</summary>
    public sealed class SpellChoice
    {
        public SpellChoice(bool blnVisible, bool blnForced, bool blnEnabled = true)
        {
            Visible = blnVisible;
            Forced = blnForced;
            Enabled = blnEnabled && !blnForced;
        }

        public bool Visible { get; }

        /// <summary>Always on (an Alchemical Preparation spell, a pure adept's touch spell...).</summary>
        public bool Forced { get; }

        public bool Enabled { get; }
    }

    /// <summary>The check boxes SelectSpell.UpdateSpellInfo shows for one spell.</summary>
    public sealed class SpellChoices
    {
        public SpellChoice Limited { get; internal set; } = new SpellChoice(true, false);
        public SpellChoice Extended { get; internal set; } = new SpellChoice(false, false);
        public SpellChoice Alchemical { get; internal set; } = new SpellChoice(true, false);
        public SpellChoice Free { get; internal set; } = new SpellChoice(false, false);
        public bool IsRitual { get; internal set; }
    }

    /// <summary>
    /// Option lists and per-item choices of the magic pickers that are not plain catalogues: spell options (SelectSpell),
    /// martial art techniques (SelectMartialArtTechnique) and spirit/sprite types (CharacterShared.RebuildSpiritList).
    /// </summary>
    public static class MagicSelectors
    {
        public static SpellChoices SpellOptions(Character objCharacter, string strSpellId, bool blnFreeOnly)
        {
            SpellChoices objReturn = new SpellChoices();
            XPathNavigator xmlSpell = objCharacter.LoadDataXPath("spells.xml").TryGetNodeByNameOrId("/chummer/spells/spell", strSpellId);
            if (xmlSpell == null)
                return objReturn;
            string strCategory = xmlSpell.SelectSingleNodeAndCacheExpression("category")?.Value ?? string.Empty;
            string strDescriptors = xmlSpell.SelectSingleNodeAndCacheExpression("descriptor")?.Value ?? string.Empty;
            List<string> lstDescriptors = strDescriptors.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToList();
            objReturn.IsRitual = strCategory == "Rituals";
            if (lstDescriptors.Contains("ALCHEMICAL PREPARATION"))
                objReturn.Alchemical = new SpellChoice(true, true);
            else if (objReturn.IsRitual)
                objReturn.Alchemical = new SpellChoice(false, false);
            if (lstDescriptors.Contains("EXTENDED AREA"))
                objReturn.Extended = new SpellChoice(true, true);
            else if (strCategory == "Detection" && objCharacter.Settings.ExtendAnyDetectionSpell)
                objReturn.Extended = new SpellChoice(true, false);

            (bool blnCanTouchOnlySpellBeFree, bool blnCanGenericSpellBeFree) = objCharacter.AllowFreeSpells();
            string strRange = xmlSpell.SelectSingleNodeAndCacheExpression("range")?.Value ?? string.Empty;
            bool blnTouch = strRange == "T" || strRange == "T (A)";
            if (objCharacter.AdeptEnabled && !objCharacter.MagicianEnabled && blnCanTouchOnlySpellBeFree && blnTouch)
            {
                // Barehanded adepts always get their touch spells free.
                objReturn.Free = new SpellChoice(true, true);
                objReturn.Limited = new SpellChoice(true, false, objCharacter.Settings.AllowLimitedSpellsForBareHandedAdept);
            }
            else
            {
                bool blnCanBeFree = blnCanGenericSpellBeFree || (blnCanTouchOnlySpellBeFree && blnTouch);
                objReturn.Free = new SpellChoice(blnCanBeFree, blnCanBeFree && blnFreeOnly);
            }
            return objReturn;
        }

        /// <summary>Techniques the runner can learn for a martial art (only the art's own list unless it allows all techniques).</summary>
        public static DataBrowser Techniques(Character objCharacter, MartialArt objMartialArt)
        {
            XPathNavigator xmlBaseChummerNode = objCharacter.LoadDataXPath("martialarts.xml").SelectSingleNodeAndCacheExpression("/chummer");
            HashSet<string> setAllowedTechniques = new HashSet<string>();
            XPathNavigator xmlMartialArtNode = objMartialArt.GetNodeXPath();
            List<string> lstKnown = objMartialArt.Techniques.ToList().Select(x => x.Name).ToList();
            if (xmlMartialArtNode != null)
            {
                if (!xmlMartialArtNode.NodeExists("alltechniques"))
                {
                    foreach (XPathNavigator xmlTechnique in xmlMartialArtNode.SelectAndCacheExpression("techniques/technique"))
                    {
                        if (!lstKnown.Contains(xmlTechnique.Value))
                            setAllowedTechniques.Add(xmlTechnique.Value);
                    }
                }
                else if (lstKnown.Count == 0)
                {
                    foreach (XPathNavigator xmlTechnique in xmlBaseChummerNode.Select("techniques/technique[(" + objCharacter.Settings.BookXPath() + ")]"))
                    {
                        string strTechniqueName = xmlTechnique.SelectSingleNodeAndCacheExpression("name")?.Value;
                        if (!string.IsNullOrEmpty(strTechniqueName) && !lstKnown.Contains(strTechniqueName))
                            setAllowedTechniques.Add(strTechniqueName);
                    }
                }
            }
            return new DataBrowser(objCharacter, xmlBaseChummerNode, "techniques/technique", objCharacter.Settings.BookXPath(),
                                   LanguageManager.GetString("String_Technique"))
            {
                Predicate = x => setAllowedTechniques.Contains(x.SelectSingleNodeAndCacheExpression("name")?.Value ?? string.Empty),
                AvailabilityCheck = x => x.RequirementsMet(objCharacter, objMartialArt),
                ValidateRequirements = false,
            };
        }

        /// <summary>Spirit or sprite types the runner's tradition/stream offers (value = critter name).</summary>
        public static List<ListItem> SpiritTypes(Character objCharacter, SpiritType eType)
        {
            List<ListItem> lstCritters = new List<ListItem>();
            Tradition objTradition = objCharacter.MagicTradition;
            if (objTradition == null)
                return lstCritters;
            bool blnTechnomancer = objTradition.Type == TraditionType.RES;
            if (blnTechnomancer != (eType == SpiritType.Sprite) || objTradition.Type == TraditionType.None)
                return lstCritters;
            XPathNavigator objXmlDocument = objCharacter.LoadDataXPath(blnTechnomancer ? "streams.xml" : "traditions.xml");
            HashSet<string> setLimitCategories = new HashSet<string>(ImprovementManager
                .GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.LimitSpiritCategory).Select(x => x.ImprovedName));

            void Add(string strSpiritName)
            {
                if (string.IsNullOrWhiteSpace(strSpiritName) || lstCritters.Any(x => x.Value.ToString() == strSpiritName))
                    return;
                XPathNavigator objXmlCritterNode = objXmlDocument.SelectSingleNode("/chummer/spirits/spirit[name = " + strSpiritName.CleanXPath() + "]");
                lstCritters.Add(new ListItem(strSpiritName, objXmlCritterNode?.SelectSingleNodeAndCacheExpression("translate")?.Value ?? strSpiritName));
            }

            if (objTradition.IsCustomTradition)
            {
                foreach (string strSpirit in new[] { objTradition.SpiritCombat, objTradition.SpiritDetection, objTradition.SpiritHealth, objTradition.SpiritIllusion, objTradition.SpiritManipulation })
                {
                    if (setLimitCategories.Count == 0 || setLimitCategories.Contains(strSpirit))
                        Add(strSpirit);
                }
            }
            else
            {
                XPathNavigator objDataNode = objTradition.GetNodeXPath();
                if (objDataNode?.SelectSingleNodeAndCacheExpression("spirits/spirit[. = \"All\"]") != null)
                {
                    if (setLimitCategories.Count == 0)
                    {
                        foreach (XPathNavigator objXmlCritterNode in objXmlDocument.SelectAndCacheExpression("/chummer/spirits/spirit"))
                            Add(objXmlCritterNode.SelectSingleNodeAndCacheExpression("name")?.Value);
                    }
                    else
                    {
                        foreach (string strSpiritName in setLimitCategories)
                            Add(strSpiritName);
                    }
                }
                else if (objDataNode != null)
                {
                    foreach (XPathNavigator objXmlSpirit in objDataNode.SelectAndCacheExpression("spirits/*"))
                    {
                        if (setLimitCategories.Count == 0 || setLimitCategories.Contains(objXmlSpirit.Value))
                            Add(objXmlSpirit.Value);
                    }
                }
            }
            // Spirits and sprites the runner gets from improvements.
            if (objCharacter.MAGEnabled)
            {
                foreach (Improvement objImprovement in ImprovementManager.GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.AddSpirit))
                    Add(objImprovement.ImprovedName);
            }
            if (objCharacter.RESEnabled)
            {
                foreach (Improvement objImprovement in ImprovementManager.GetCachedImprovementListForValueOf(objCharacter, Improvement.ImprovementType.AddSprite))
                    Add(objImprovement.ImprovedName);
            }
            return lstCritters;
        }
    }
}
