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
using System.IO;
using System.Linq;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Selection
{
    [TestClass]
    public class SelectorTests
    {
        private static Character s_objCharacter;

        [ClassInitialize]
        public static void Load(TestContext context)
        {
            s_objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma (Career).chum5")));
        }

        [ClassCleanup]
        public static void Cleanup() => s_objCharacter?.Dispose();

        private static List<string> Names(IEnumerable<ListItem> lstItems) => lstItems.Select(x => ((ValueTuple<string, bool>)x.Value).Item1).ToList();

        [TestMethod]
        public void SkillsByCategory()
        {
            List<string> lstNames = Names(SkillSelector.GetSkillOptions(s_objCharacter, new SkillSelectionFilter { IncludeCategory = "Combat Active" }, out _, out _));
            CollectionAssert.Contains(lstNames, "Pistols");
            CollectionAssert.DoesNotContain(lstNames, "Perception");
        }

        [TestMethod]
        public void SkillsLimitedAndExcluded()
        {
            List<string> lstNames = Names(SkillSelector.GetSkillOptions(s_objCharacter, new SkillSelectionFilter { LimitToSkill = "Pistols, Blades, Clubs", ExcludeSkill = "Clubs" }, out _, out _));
            CollectionAssert.AreEquivalent(new[] { "Pistols", "Blades" }, lstNames);
        }

        [TestMethod]
        public void SkillsByMinimumRating()
        {
            List<string> lstNames = Names(SkillSelector.GetSkillOptions(s_objCharacter, new SkillSelectionFilter { MinimumRating = 1 }, out _, out _));
            Assert.IsTrue(lstNames.Count > 0);
            foreach (string strName in lstNames)
                Assert.IsTrue((s_objCharacter.SkillsSection.GetActiveSkill(strName)?.TotalBaseRating ?? 0) >= 1, strName);
        }

        [TestMethod]
        public void ExoticSpecifics()
        {
            List<ListItem> lstSpecifics = SkillSelector.GetExoticSpecifics(s_objCharacter, "Exotic Ranged Weapon", new SkillSelectionFilter());
            Assert.IsTrue(lstSpecifics.Count > 0);
        }

        [TestMethod]
        public void Catalogues()
        {
            DataBrowser objMartial = DataBrowser.MartialArt(s_objCharacter);
            List<ListItem> lstAll = objMartial.Options(false);
            Assert.IsTrue(lstAll.Count > 10);
            Assert.IsTrue(objMartial.Options(true).Count <= lstAll.Count);
            StringAssert.Contains(objMartial.Details(lstAll[0].Value.ToString()), lstAll[0].Name);
            foreach (SelectArtMode eMode in new[] { SelectArtMode.Art, SelectArtMode.Enhancement, SelectArtMode.Enchantment, SelectArtMode.Ritual })
                Assert.IsTrue(DataBrowser.Art(s_objCharacter, eMode).Options(false).Count > 0, eMode.ToString());
        }

        [TestMethod]
        public void Categories()
        {
            Assert.IsTrue(CategorySelector.GetSkillGroups(s_objCharacter, null, null).Any(x => x.Value.ToString() == "Firearms"));
            Assert.AreEqual(1, CategorySelector.GetSkillGroups(s_objCharacter, "Firearms", null).Count);
            List<ListItem> lstSpells = CategorySelector.GetSpellCategories(s_objCharacter, null, new[] { "Combat" });
            Assert.IsTrue(lstSpells.Count > 0);
            Assert.IsFalse(lstSpells.Any(x => x.Value.ToString() == "Combat"));
            Assert.IsTrue(CategorySelector.GetWeaponCategories(s_objCharacter, null, "melee").Count > 0);
            Assert.AreEqual("Cyberware", CategorySelector.GetWeaponCategories(s_objCharacter, "Cyberware", null).Single().Value);
        }
    }
}
