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
using Chummer.Backend.Equipment;
using Chummer.Backend.Skills;
using Chummer.Backend.Attributes;

namespace Chummer.Rolls
{
    /// <summary>
    /// What a roll is made with: the character plus whatever item/spell the roll is about.
    /// </summary>
    public sealed class RollContext
    {
        public RollContext(Character objCharacter)
        {
            Character = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
        }

        public Character Character { get; }
        public Weapon Weapon { get; set; }
        public Vehicle Vehicle { get; set; }
        public Spell Spell { get; set; }
        public int SpellForce { get; set; }
        public IHasMatrixAttributes MatrixDevice { get; set; }
        public Spirit Spirit { get; set; }
        public Gear Gear { get; set; }

        /// <summary>
        /// Values the user supplies for things the engine cannot know (e.g. "Target: Physical" = 4), keyed by token text.
        /// </summary>
        public IDictionary<string, int> Overrides { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Looks up other actions referenced as {Action: Name} (the roll catalog).
        /// </summary>
        public Func<string, RollAction> ActionLookup { get; set; }
    }

    /// <summary>One labelled contribution to a dice pool, for the breakdown tooltip.</summary>
    public readonly struct RollTerm
    {
        public RollTerm(string strLabel, int intValue, bool blnUnknown = false)
        {
            Label = strLabel;
            Value = intValue;
            IsUnknown = blnUnknown;
        }

        public string Label { get; }
        public int Value { get; }
        public bool IsUnknown { get; }

        public override string ToString() => IsUnknown ? Label + " ?" : Label + " " + Value.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class DiceExpressionResult
    {
        public int Value { get; internal set; }
        public List<RollTerm> Terms { get; } = new List<RollTerm>();

        /// <summary>Tokens that could not be resolved (counted as 0); the user can supply them as overrides.</summary>
        public List<string> Unknowns { get; } = new List<string>();

        public string Breakdown => string.Join(" + ", Terms.Select(x => x.ToString())) + (Terms.Count > 0 ? " = " : string.Empty) + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Result of evaluating a full test ("attacker v. defender").
    /// </summary>
    public sealed class RollEvaluation
    {
        public DiceExpressionResult Pool { get; internal set; }
        public int? Limit { get; internal set; }
        public int? Threshold { get; internal set; }

        /// <summary>Human-readable opposition ("Melee Defense", "BOD + WIL"), empty for success tests.</summary>
        public string OpposedBy { get; internal set; } = string.Empty;

        public bool IsOpposed => OpposedBy.Length > 0;
    }

    /// <summary>
    /// Evaluates Chummer's dice expression language (data/actions.xml, house rule files) against a character:
    /// arithmetic (+ - * / parentheses), tokens in braces ({AGI}, {Pistols}, {Weapon: Accuracy}, {Improvement Value: Dodge},
    /// {Max: {a} or {b}}, {Min: ...}, {If: {x} gte 0; Then: {...}; Else: {...}}), and " v. " separating the opposition.
    /// </summary>
    public static class DiceExpressionEvaluator
    {
        private static readonly HashSet<string> s_setAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "MAGAdept", "RES", "DEP", "ESS"
        };

        public static RollEvaluation EvaluateTest(RollAction objAction, RollContext objContext)
        {
            if (objAction == null)
                throw new ArgumentNullException(nameof(objAction));
            RollEvaluation objReturn = new RollEvaluation();
            string strDice = objAction.Dice ?? string.Empty;
            int intSplit = IndexOfTopLevel(strDice, " v. ");
            string strActor = intSplit >= 0 ? strDice.Substring(0, intSplit) : strDice;
            string strOpposition = intSplit >= 0 ? strDice.Substring(intSplit + 4).Trim() : string.Empty;
            objReturn.Pool = Evaluate(strActor, objContext);
            if (!string.IsNullOrWhiteSpace(objAction.Limit))
            {
                DiceExpressionResult objLimit = Evaluate(objAction.Limit, objContext);
                if (objLimit.Unknowns.Count == 0 && objLimit.Terms.Count > 0)
                    objReturn.Limit = objLimit.Value;
            }
            if (strOpposition.Length > 0)
            {
                if (strOpposition.StartsWith("{Threshold:", StringComparison.OrdinalIgnoreCase) && strOpposition.EndsWith("}", StringComparison.Ordinal)
                    && FindMatchingBrace(strOpposition, 0) == strOpposition.Length - 1)
                {
                    DiceExpressionResult objThreshold = Evaluate(strOpposition.Substring(11, strOpposition.Length - 12).Trim(), objContext);
                    if (objThreshold.Unknowns.Count == 0)
                        objReturn.Threshold = objThreshold.Value;
                    else
                        objReturn.OpposedBy = "Threshold: " + Describe(strOpposition.Substring(11, strOpposition.Length - 12).Trim());
                }
                else
                    objReturn.OpposedBy = Describe(strOpposition);
            }
            return objReturn;
        }

        public static DiceExpressionResult Evaluate(string strExpression, RollContext objContext)
        {
            DiceExpressionResult objResult = new DiceExpressionResult();
            if (string.IsNullOrWhiteSpace(strExpression) || strExpression.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
                return objResult;
            Parser objParser = new Parser(strExpression, objContext, objResult, 0);
            objResult.Value = (int)Math.Floor(objParser.ParseExpression());
            return objResult;
        }

        /// <summary>Plain-language rendering of an expression ("{Action: Melee Defense} + 2" -> "Melee Defense + 2").</summary>
        public static string Describe(string strExpression)
        {
            StringBuilder sbdReturn = new StringBuilder(strExpression.Length);
            foreach (char chrLoop in strExpression)
            {
                if (chrLoop != '{' && chrLoop != '}')
                    sbdReturn.Append(chrLoop);
            }
            return sbdReturn.ToString()
                            .Replace("Action: ", string.Empty).Replace("Improvement Value: ", string.Empty)
                            .Replace("Target: ", "target's ").Trim();
        }

        internal static int IndexOfTopLevel(string strText, string strNeedle)
        {
            int intDepth = 0;
            for (int i = 0; i <= strText.Length - strNeedle.Length; ++i)
            {
                char chrLoop = strText[i];
                if (chrLoop == '{')
                    ++intDepth;
                else if (chrLoop == '}')
                    --intDepth;
                else if (intDepth == 0 && string.CompareOrdinal(strText, i, strNeedle, 0, strNeedle.Length) == 0)
                    return i;
            }
            return -1;
        }

        internal static int FindMatchingBrace(string strText, int intOpen)
        {
            int intDepth = 0;
            for (int i = intOpen; i < strText.Length; ++i)
            {
                if (strText[i] == '{')
                    ++intDepth;
                else if (strText[i] == '}' && --intDepth == 0)
                    return i;
            }
            return -1;
        }

        private sealed class Parser
        {
            private readonly string _strText;
            private readonly RollContext _objContext;
            private readonly DiceExpressionResult _objResult;
            private readonly int _intDepth;
            private int _intPos;

            public Parser(string strText, RollContext objContext, DiceExpressionResult objResult, int intDepth)
            {
                _strText = strText;
                _objContext = objContext;
                _objResult = objResult;
                _intDepth = intDepth;
            }

            private void SkipSpaces()
            {
                while (_intPos < _strText.Length && char.IsWhiteSpace(_strText[_intPos]))
                    ++_intPos;
            }

            public double ParseExpression()
            {
                double dblValue = ParseTerm();
                while (true)
                {
                    SkipSpaces();
                    if (_intPos >= _strText.Length)
                        return dblValue;
                    char chrOp = _strText[_intPos];
                    if (chrOp == '+')
                    {
                        ++_intPos;
                        dblValue += ParseTerm();
                    }
                    else if (chrOp == '-')
                    {
                        ++_intPos;
                        dblValue -= ParseTerm();
                    }
                    else
                        return dblValue;
                }
            }

            private double ParseTerm()
            {
                double dblValue = ParseFactor();
                while (true)
                {
                    SkipSpaces();
                    if (_intPos >= _strText.Length)
                        return dblValue;
                    char chrOp = _strText[_intPos];
                    if (chrOp == '*')
                    {
                        ++_intPos;
                        dblValue *= ParseFactor();
                    }
                    else if (chrOp == '/')
                    {
                        ++_intPos;
                        double dblDivisor = ParseFactor();
                        dblValue = dblDivisor == 0 ? 0 : Math.Floor(dblValue / dblDivisor);
                    }
                    else
                        return dblValue;
                }
            }

            private double ParseFactor()
            {
                SkipSpaces();
                if (_intPos >= _strText.Length)
                    return 0;
                char chrLoop = _strText[_intPos];
                if (chrLoop == '-')
                {
                    ++_intPos;
                    return -ParseFactor();
                }
                if (chrLoop == '(')
                {
                    ++_intPos;
                    double dblValue = ParseExpression();
                    SkipSpaces();
                    if (_intPos < _strText.Length && _strText[_intPos] == ')')
                        ++_intPos;
                    return dblValue;
                }
                if (chrLoop == '{')
                {
                    int intClose = FindMatchingBrace(_strText, _intPos);
                    if (intClose < 0)
                    {
                        _intPos = _strText.Length;
                        return 0;
                    }
                    string strToken = _strText.Substring(_intPos + 1, intClose - _intPos - 1).Trim();
                    _intPos = intClose + 1;
                    return ResolveToken(strToken);
                }
                if (char.IsDigit(chrLoop) || chrLoop == '.')
                {
                    int intStart = _intPos;
                    while (_intPos < _strText.Length && (char.IsDigit(_strText[_intPos]) || _strText[_intPos] == '.'))
                        ++_intPos;
                    double.TryParse(_strText.Substring(intStart, _intPos - intStart), NumberStyles.Float, CultureInfo.InvariantCulture, out double dblNumber);
                    return dblNumber;
                }
                // Free text (e.g. "Hits" in house rules): skip one word, contributes nothing.
                int intWordStart = _intPos;
                while (_intPos < _strText.Length && !"+-*/(){} ".Contains(_strText[_intPos]))
                    ++_intPos;
                if (_intPos == intWordStart)
                    ++_intPos;
                return 0;
            }

            private double Sub(string strExpression)
            {
                if (_intDepth > 12)
                    return 0; // runaway recursion through {Action: ...} references
                return new Parser(strExpression, _objContext, _objResult, _intDepth + 1).ParseExpression();
            }

            private double SubQuiet(string strExpression)
            {
                // Evaluate without recording terms (for conditions and Max/Min alternatives).
                DiceExpressionResult objScratch = new DiceExpressionResult();
                double dblValue = new Parser(strExpression, _objContext, objScratch, _intDepth + 1).ParseExpression();
                _objResult.Unknowns.AddRange(objScratch.Unknowns.Where(x => !_objResult.Unknowns.Contains(x)));
                return dblValue;
            }

            private double Record(string strLabel, double dblValue)
            {
                _objResult.Terms.Add(new RollTerm(strLabel, (int)Math.Floor(dblValue)));
                return dblValue;
            }

            private double Unknown(string strToken)
            {
                if (!_objResult.Unknowns.Contains(strToken))
                    _objResult.Unknowns.Add(strToken);
                _objResult.Terms.Add(new RollTerm(Describe(strToken), 0, true));
                return 0;
            }

            private double ResolveToken(string strToken)
            {
                if (_objContext.Overrides.TryGetValue(strToken, out int intOverride))
                    return Record(Describe(strToken), intOverride);

                int intColon = strToken.IndexOf(':');
                string strHead = intColon > 0 ? strToken.Substring(0, intColon).Trim() : strToken;
                string strArgument = intColon > 0 ? strToken.Substring(intColon + 1).Trim() : string.Empty;
                Character objCharacter = _objContext.Character;

                if (intColon < 0)
                {
                    if (s_setAttributes.Contains(strToken))
                    {
                        if (strToken.Equals("ESS", StringComparison.OrdinalIgnoreCase))
                            return Record("ESS", (double)objCharacter.Essence());
                        CharacterAttrib objAttribute = objCharacter.GetAttribute(strToken.ToUpperInvariant() == "MAGADEPT" ? "MAGAdept" : strToken.ToUpperInvariant());
                        return objAttribute == null ? Unknown(strToken) : Record(strToken.ToUpperInvariant(), objAttribute.TotalValue);
                    }
                    switch (strToken.ToUpperInvariant())
                    {
                        case "PHYSICAL": return Record("Physical limit", objCharacter.LimitPhysical);
                        case "MENTAL": return Record("Mental limit", objCharacter.LimitMental);
                        case "SOCIAL": return Record("Social limit", objCharacter.LimitSocial);
                        case "ASTRAL": return Record("Astral limit", objCharacter.LimitAstral);
                    }
                    // A plain number or nested expression in braces, e.g. {2 * {Target: Gear: Concealability}}
                    if (strToken.IndexOfAny(new[] { '{', '+', '*', '/' }) >= 0 || strToken.All(x => char.IsDigit(x) || x == ' ' || x == '-'))
                        return Sub(strToken);
                    return ResolveSkill(strToken);
                }

                switch (strHead.ToUpperInvariant())
                {
                    case "MAX":
                    case "MIN":
                    {
                        string[] astrParts = SplitTopLevel(strArgument, " or ");
                        if (astrParts.Length == 1)
                            astrParts = SplitTopLevel(strArgument, ",");
                        double dblBest = 0;
                        bool blnFirst = true;
                        int intBest = 0;
                        for (int i = 0; i < astrParts.Length; ++i)
                        {
                            double dblLoop = SubQuiet(astrParts[i]);
                            if (blnFirst || (strHead.Equals("Max", StringComparison.OrdinalIgnoreCase) ? dblLoop > dblBest : dblLoop < dblBest))
                            {
                                dblBest = dblLoop;
                                intBest = i;
                                blnFirst = false;
                            }
                        }
                        return Record(Describe(astrParts.Length > 0 ? astrParts[intBest] : strArgument), dblBest);
                    }
                    case "IF":
                    {
                        // {If: <cond>; Then: <expr>; Else: <expr>}
                        string[] astrParts = SplitTopLevel(strArgument, ";");
                        string strCondition = astrParts.Length > 0 ? astrParts[0] : string.Empty;
                        string strThen = astrParts.Skip(1).FirstOrDefault(x => x.TrimStart().StartsWith("Then:", StringComparison.OrdinalIgnoreCase))?.Trim().Substring(5) ?? "0";
                        string strElse = astrParts.Skip(1).FirstOrDefault(x => x.TrimStart().StartsWith("Else:", StringComparison.OrdinalIgnoreCase))?.Trim().Substring(5) ?? "0";
                        return EvaluateCondition(strCondition) ? Sub(strThen) : Sub(strElse);
                    }
                    case "ACTION":
                    {
                        RollAction objReferenced = _objContext.ActionLookup?.Invoke(strArgument);
                        if (objReferenced == null || string.IsNullOrWhiteSpace(objReferenced.Dice))
                            return Unknown(strToken);
                        string strDice = objReferenced.Dice;
                        int intSplit = IndexOfTopLevel(strDice, " v. ");
                        return Sub(intSplit >= 0 ? strDice.Substring(0, intSplit) : strDice);
                    }
                    case "IMPROVEMENT VALUE":
                    {
                        if (!Enum.TryParse(strArgument, true, out Improvement.ImprovementType eType))
                            return Unknown(strToken);
                        return Record(strArgument, (double)ImprovementManager.ValueOf(objCharacter, eType));
                    }
                    case "THRESHOLD":
                        return Sub(strArgument);
                    case "WEAPON":
                        return ResolveWeapon(strToken, strArgument);
                    case "VEHICLE":
                        return ResolveVehicle(strToken, strArgument);
                    case "SPELL":
                        return ResolveSpell(strToken, strArgument);
                    case "ICON":
                    {
                        IHasMatrixAttributes objDevice = _objContext.MatrixDevice;
                        if (objDevice == null)
                            return Unknown(strToken);
                        string strAttribute = strArgument.Equals("Rating", StringComparison.OrdinalIgnoreCase) ? "Device Rating" : strArgument;
                        return Record(strArgument, objDevice.GetTotalMatrixAttribute(strAttribute));
                    }
                    case "SPIRIT":
                        return _objContext.Spirit != null && strArgument.Equals("Force", StringComparison.OrdinalIgnoreCase)
                            ? Record("Spirit Force", _objContext.Spirit.Force)
                            : Unknown(strToken);
                    case "SKILL":
                        return ResolveSkill(strArgument);
                    default:
                        // Target: ..., Gear: ..., anything else the engine cannot know.
                        return Unknown(strToken);
                }
            }

            private bool EvaluateCondition(string strCondition)
            {
                string[] astrOperators = { " gte ", " lte ", " gt ", " lt ", " eq ", " ne " };
                foreach (string strOperator in astrOperators)
                {
                    int intIndex = IndexOfTopLevel(strCondition, strOperator);
                    if (intIndex < 0)
                        continue;
                    double dblLeft = SubQuiet(strCondition.Substring(0, intIndex));
                    double dblRight = SubQuiet(strCondition.Substring(intIndex + strOperator.Length));
                    switch (strOperator.Trim())
                    {
                        case "gte": return dblLeft >= dblRight;
                        case "lte": return dblLeft <= dblRight;
                        case "gt": return dblLeft > dblRight;
                        case "lt": return dblLeft < dblRight;
                        case "eq": return Math.Abs(dblLeft - dblRight) < 0.0001;
                        default: return Math.Abs(dblLeft - dblRight) >= 0.0001;
                    }
                }
                return SubQuiet(strCondition) != 0;
            }

            private double ResolveSkill(string strName)
            {
                Character objCharacter = _objContext.Character;
                Skill objSkill = objCharacter.SkillsSection.GetActiveSkill(strName)
                                 ?? objCharacter.SkillsSection.Skills.FirstOrDefault(x => x.CurrentDisplayName.Equals(strName, StringComparison.OrdinalIgnoreCase))
                                 ?? objCharacter.SkillsSection.KnowledgeSkills.FirstOrDefault(x => x.Name.Equals(strName, StringComparison.OrdinalIgnoreCase)
                                                                                                   || x.CurrentDisplayName.Equals(strName, StringComparison.OrdinalIgnoreCase));
                if (objSkill == null)
                    return Unknown(strName);
                int intRating = objSkill.Rating;
                if (intRating > 0)
                    return Record(objSkill.CurrentDisplayName, intRating);
                // SR5 defaulting: untrained skills that allow it roll attribute - 1.
                return objSkill.Default ? Record(objSkill.CurrentDisplayName + " (defaulting)", -1) : Unknown(strName + " (untrained, cannot default)");
            }

            private double ResolveWeapon(string strToken, string strArgument)
            {
                Weapon objWeapon = _objContext.Weapon;
                if (objWeapon == null)
                    return Unknown(strToken);
                switch (strArgument.ToUpperInvariant())
                {
                    case "ACCURACY":
                    case "LIMIT":
                        return Record(objWeapon.CurrentDisplayName + " accuracy", objWeapon.GetTotalAccuracy());
                    case "SKILL":
                    {
                        Skill objSkill = objWeapon.Skill;
                        if (objSkill == null)
                            return Unknown(strToken);
                        int intRating = objSkill.Rating;
                        return intRating > 0 || !objSkill.Default
                            ? Record(objSkill.CurrentDisplayName, intRating)
                            : Record(objSkill.CurrentDisplayName + " (defaulting)", -1);
                    }
                    case "DV":
                        return Record("DV", LeadingInt(objWeapon.DisplayDamage));
                    case "AP":
                        return Record("AP", LeadingInt(objWeapon.DisplayTotalAP));
                    case "REACH":
                        return Record("Reach", objWeapon.TotalReach);
                    default:
                        return Unknown(strToken);
                }
            }

            private double ResolveVehicle(string strToken, string strArgument)
            {
                Vehicle objVehicle = _objContext.Vehicle;
                if (objVehicle == null)
                    return Unknown(strToken);
                switch (strArgument.ToUpperInvariant())
                {
                    case "HANDLING": return Record("Handling", LeadingInt(objVehicle.TotalHandling));
                    case "SPEED": return Record("Speed", LeadingInt(objVehicle.TotalSpeed));
                    case "ACCELERATION":
                    case "ACCEL": return Record("Acceleration", LeadingInt(objVehicle.TotalAccel));
                    case "SENSOR": return Record("Sensor", objVehicle.CalculatedSensor);
                    case "PILOT": return Record("Pilot", objVehicle.Pilot);
                    case "BODY": return Record("Body", objVehicle.TotalBody);
                    case "ARMOR": return Record("Armor", objVehicle.TotalArmor);
                    case "SKILL":
                    {
                        // Piloting skill matching the vehicle's category.
                        string strCategory = objVehicle.Category ?? string.Empty;
                        string strSkill = strCategory.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0 ? "Pilot Ground Craft"
                            : strCategory.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0 || strCategory.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                              || strCategory.IndexOf("Submarine", StringComparison.OrdinalIgnoreCase) >= 0 ? "Pilot Watercraft"
                            : strCategory.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0 || strCategory.IndexOf("Plane", StringComparison.OrdinalIgnoreCase) >= 0
                              || strCategory.IndexOf("Helicopter", StringComparison.OrdinalIgnoreCase) >= 0 || strCategory.IndexOf("Rotor", StringComparison.OrdinalIgnoreCase) >= 0
                              || strCategory.IndexOf("VTOL", StringComparison.OrdinalIgnoreCase) >= 0 ? "Pilot Aircraft"
                            : "Pilot Ground Craft";
                        return ResolveSkill(strSkill);
                    }
                    default:
                        return Unknown(strToken);
                }
            }

            private double ResolveSpell(string strToken, string strArgument)
            {
                switch (strArgument.ToUpperInvariant())
                {
                    case "FORCE":
                        return _objContext.SpellForce > 0 ? Record("Force", _objContext.SpellForce) : Unknown(strToken);
                    case "SPELLCASTING":
                        return ResolveSkill("Spellcasting");
                    case "DRAIN":
                    {
                        Spell objSpell = _objContext.Spell;
                        if (objSpell == null || _objContext.SpellForce <= 0)
                            return Unknown(strToken);
                        // Drain values are written relative to Force, e.g. "F-2".
                        string strDv = objSpell.DisplayDv(GlobalSettings.DefaultLanguage).Replace(" ", string.Empty);
                        int intForceIndex = strDv.IndexOf('F');
                        if (intForceIndex >= 0)
                            strDv = strDv.Substring(0, intForceIndex) + _objContext.SpellForce.ToString(CultureInfo.InvariantCulture) + strDv.Substring(intForceIndex + 1);
                        DiceExpressionResult objScratch = Evaluate(strDv, _objContext);
                        return Record("Drain", Math.Max(2, objScratch.Value));
                    }
                    default:
                        return Unknown(strToken);
                }
            }

            private static int LeadingInt(string strValue)
            {
                if (string.IsNullOrEmpty(strValue))
                    return 0;
                int i = 0;
                while (i < strValue.Length && !char.IsDigit(strValue[i]) && strValue[i] != '-' && strValue[i] != '+')
                    ++i;
                int intStart = i;
                if (i < strValue.Length && (strValue[i] == '-' || strValue[i] == '+'))
                    ++i;
                while (i < strValue.Length && char.IsDigit(strValue[i]))
                    ++i;
                return int.TryParse(strValue.Substring(intStart, i - intStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int intReturn) ? intReturn : 0;
            }

            private static string[] SplitTopLevel(string strText, string strSeparator)
            {
                List<string> lstParts = new List<string>();
                int intStart = 0;
                while (true)
                {
                    int intIndex = IndexOfTopLevel(strText.Substring(intStart), strSeparator);
                    if (intIndex < 0)
                    {
                        lstParts.Add(strText.Substring(intStart).Trim());
                        return lstParts.ToArray();
                    }
                    lstParts.Add(strText.Substring(intStart, intIndex).Trim());
                    intStart += intIndex + strSeparator.Length;
                }
            }
        }
    }
}
