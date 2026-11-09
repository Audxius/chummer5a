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
using System.IO;
using System.Linq;
using Chummer.Backend.Equipment;
using Chummer.Rolls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rolls
{
    [TestClass]
    public class DiceExpressionEvaluatorTests
    {
        private static Character s_objCharacter;
        private static RollCatalog s_objCatalog;

        [ClassInitialize]
        public static void Load(TestContext context)
        {
            string strPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "Barrett.chum5");
            s_objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(strPath));
            s_objCatalog = RollCatalog.Load(s_objCharacter);
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            s_objCharacter?.Dispose();
        }

        private static RollContext NewContext()
        {
            return new RollContext(s_objCharacter) { ActionLookup = s_objCatalog.Find };
        }

        [TestMethod]
        [DataRow("2 * 3 + 4", 10)]
        [DataRow("(1 + 2) * 3", 9)]
        [DataRow("7 / 2", 3)]
        [DataRow("10 - 2 - 3", 5)]
        [DataRow("{Max: {2} or {5}}", 5)]
        [DataRow("{Min: {2}, {5}}", 2)]
        [DataRow("{If: {3} gte 0; Then: {2 * 3}; Else: {1}}", 6)]
        [DataRow("{If: {-3} gte 0; Then: {2 * 3}; Else: {1}}", 1)]
        [DataRow("None", 0)]
        public void Arithmetic(string strExpression, int intExpected)
        {
            Assert.AreEqual(intExpected, DiceExpressionEvaluator.Evaluate(strExpression, NewContext()).Value);
        }

        [TestMethod]
        public void AttributesAndImprovements()
        {
            int intExpected = s_objCharacter.GetAttribute("REA").TotalValue + s_objCharacter.GetAttribute("INT").TotalValue
                              + (int)ImprovementManager.ValueOf(s_objCharacter, Improvement.ImprovementType.Dodge);
            RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(s_objCatalog.Find("Melee Defense"), NewContext());
            Assert.AreEqual(intExpected, objEvaluation.Pool.Value, objEvaluation.Pool.Breakdown);
            Assert.AreEqual(0, objEvaluation.Pool.Unknowns.Count);
            Assert.IsFalse(objEvaluation.IsOpposed);
        }

        [TestMethod]
        public void LimitsAndThresholds()
        {
            RollAction objAction = new RollAction { Name = "Test", DisplayName = "Test", Dice = "{AGI} + 1 v. {Threshold: 2}", Limit = "{Physical}" };
            RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, NewContext());
            Assert.AreEqual(s_objCharacter.GetAttribute("AGI").TotalValue + 1, objEvaluation.Pool.Value);
            Assert.AreEqual(2, objEvaluation.Threshold);
            Assert.AreEqual(s_objCharacter.LimitPhysical, objEvaluation.Limit);
            Assert.IsFalse(objEvaluation.IsOpposed);
        }

        [TestMethod]
        public void OpposedTestsAndActionReferences()
        {
            RollAction objAction = new RollAction { Name = "Hit", DisplayName = "Hit", Dice = "{Action: Melee Defense} + 2 v. {Action: Melee Defense}" };
            RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, NewContext());
            RollEvaluation objDefense = DiceExpressionEvaluator.EvaluateTest(s_objCatalog.Find("Melee Defense"), NewContext());
            Assert.AreEqual(objDefense.Pool.Value + 2, objEvaluation.Pool.Value);
            Assert.AreEqual("Melee Defense", objEvaluation.OpposedBy);
        }

        [TestMethod]
        public void UnknownTokensAreReportedAndOverridable()
        {
            RollContext objContext = NewContext();
            DiceExpressionResult objResult = DiceExpressionEvaluator.Evaluate("{Target: Sensors} + 1", objContext);
            Assert.AreEqual(1, objResult.Value);
            CollectionAssert.Contains(objResult.Unknowns, "Target: Sensors");
            objContext.Overrides["Target: Sensors"] = 4;
            Assert.AreEqual(5, DiceExpressionEvaluator.Evaluate("{Target: Sensors} + 1", objContext).Value);
        }

        [TestMethod]
        public void WeaponContext()
        {
            Weapon objWeapon = s_objCharacter.Weapons.FirstOrDefault(x => x.Skill != null);
            if (objWeapon == null)
                Assert.Inconclusive("Test character has no weapon with a skill.");
            RollContext objContext = NewContext();
            objContext.Weapon = objWeapon;
            RollAction objAction = new RollAction { Name = "Attack", DisplayName = "Attack", Dice = "{AGI} + {Weapon: Skill}", Limit = "{Weapon: Accuracy}" };
            RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, objContext);
            Assert.AreEqual(0, objEvaluation.Pool.Unknowns.Count, objEvaluation.Pool.Breakdown);
            Assert.AreEqual(objWeapon.GetTotalAccuracy(), objEvaluation.Limit);
        }

        [TestMethod]
        public void EveryCatalogActionEvaluates()
        {
            Assert.IsTrue(s_objCatalog.Actions.Count(x => x.IsRollable) > 200);
            foreach (RollAction objAction in s_objCatalog.Actions.Where(x => x.IsRollable))
            {
                RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, NewContext());
                Assert.IsNotNull(objEvaluation.Pool, objAction.Name);
            }
        }

        [TestMethod]
        public void Roll20Formatting()
        {
            RollAction objAction = s_objCatalog.Find("Melee Defense");
            RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, NewContext());
            Roll20Formatter objFormatter = new Roll20Formatter();
            string strNormal = objFormatter.Format(objAction, objEvaluation, "Barrett");
            StringAssert.Contains(strNormal, "{{Hits=[[" + objEvaluation.Pool.Value + "d6>5]]}}");
            StringAssert.StartsWith(strNormal, "&{template:default} {{name=Barrett: Melee Defense}}");
            string strEdge = objFormatter.Format(objAction, objEvaluation, "Barrett", true, 3, -1);
            StringAssert.Contains(strEdge, "[[" + (objEvaluation.Pool.Value + 2) + "d6!>6>5]]");
        }

        [TestMethod]
        public void HouseRulesLoadAndEvaluate()
        {
            System.Collections.Generic.List<RollAction> lstHouse = s_objCatalog.Actions.Where(x => x.IsHouseRule).ToList();
            Assert.IsTrue(lstHouse.Count > 0, "No house rules found in " + string.Join(", ", RollCatalog.HouseRuleFolders()));
            System.Text.StringBuilder sbdUnknowns = new System.Text.StringBuilder();
            foreach (RollAction objAction in lstHouse.Where(x => x.IsRollable))
            {
                RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(objAction, NewContext());
                foreach (string strUnknown in objEvaluation.Pool.Unknowns)
                    sbdUnknowns.AppendLine(objAction.Book + " / " + objAction.Name + ": " + strUnknown);
            }
            // Unresolved tokens are expected for item context (weapon, vehicle...) and table-chosen skills; logged for review.
            System.Console.WriteLine("House rules: " + lstHouse.Count + System.Environment.NewLine + sbdUnknowns);
        }

        [TestMethod]
        public void SearchRanksNameMatchesFirst()
        {
            RollAction objFirst = s_objCatalog.Search("melee def").FirstOrDefault();
            Assert.IsNotNull(objFirst);
            Assert.AreEqual("Melee Defense", objFirst.Name);
        }
    }
}
