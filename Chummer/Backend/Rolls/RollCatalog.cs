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
using System.IO;
using System.Linq;
using System.Xml.XPath;
using NLog;

namespace Chummer.Rolls
{
    /// <summary>One rollable test: a core action from data/actions.xml or a house rule.</summary>
    public sealed class RollAction
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Dice { get; set; } = string.Empty;
        public string Limit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;

        /// <summary>Name of the house-rule book this came from, empty for core rules.</summary>
        public string Book { get; set; } = string.Empty;

        /// <summary>Extra search terms (aliases, section headings).</summary>
        public string Keywords { get; set; } = string.Empty;

        public bool IsRollable => !string.IsNullOrWhiteSpace(Dice) && !Dice.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
        public bool IsHouseRule => Book.Length > 0;

        /// <summary>Which context the roll needs (so the UI can ask for a weapon, vehicle...).</summary>
        public RollNeeds Needs
        {
            get
            {
                RollNeeds eReturn = RollNeeds.None;
                string strAll = Dice + " " + Limit;
                if (strAll.IndexOf("{Weapon:", StringComparison.OrdinalIgnoreCase) >= 0)
                    eReturn |= RollNeeds.Weapon;
                if (strAll.IndexOf("{Vehicle:", StringComparison.OrdinalIgnoreCase) >= 0)
                    eReturn |= RollNeeds.Vehicle;
                if (strAll.IndexOf("{Spell:", StringComparison.OrdinalIgnoreCase) >= 0)
                    eReturn |= RollNeeds.Spell;
                if (strAll.IndexOf("{Icon:", StringComparison.OrdinalIgnoreCase) >= 0)
                    eReturn |= RollNeeds.MatrixDevice;
                if (strAll.IndexOf("{Spirit:", StringComparison.OrdinalIgnoreCase) >= 0)
                    eReturn |= RollNeeds.Spirit;
                return eReturn;
            }
        }

        public override string ToString() => DisplayName;
    }

    [Flags]
    public enum RollNeeds
    {
        None = 0,
        Weapon = 1,
        Vehicle = 2,
        Spell = 4,
        MatrixDevice = 8,
        Spirit = 16,
    }

    /// <summary>
    /// Everything the Dice Terminal can roll: data/actions.xml (merged with enabled custom data) plus house-rule files
    /// (any *.xml with the same &lt;actions&gt; schema in the "houserules" folder next to the app or in the user's config folder).
    /// </summary>
    public sealed class RollCatalog
    {
        private static readonly Lazy<Logger> s_objLogger = new Lazy<Logger>(LogManager.GetCurrentClassLogger);
        private static Logger Log => s_objLogger.Value;

        private readonly List<RollAction> _lstActions = new List<RollAction>();
        private readonly Dictionary<string, RollAction> _dicByName = new Dictionary<string, RollAction>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RollAction> Actions => _lstActions;

        public RollAction Find(string strName)
        {
            if (string.IsNullOrWhiteSpace(strName))
                return null;
            return _dicByName.TryGetValue(strName.Trim(), out RollAction objReturn) ? objReturn : null;
        }

        public void Add(RollAction objAction)
        {
            if (objAction == null)
                throw new ArgumentNullException(nameof(objAction));
            _lstActions.Add(objAction);
            // House rules may deliberately shadow a core action of the same name; core lookups via {Action: X} prefer the core one.
            if (!_dicByName.ContainsKey(objAction.Name) || (_dicByName[objAction.Name].IsHouseRule && !objAction.IsHouseRule))
                _dicByName[objAction.Name] = objAction;
        }

        /// <summary>All folders searched for house-rule files.</summary>
        public static IEnumerable<string> HouseRuleFolders()
        {
            yield return Path.Combine(Utils.GetStartupPath, "houserules");
            string strConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(strConfig))
                strConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            yield return Path.Combine(strConfig, "chummer5", "houserules");
        }

        public static RollCatalog Load(Character objCharacter)
        {
            RollCatalog objReturn = new RollCatalog();
            XPathNavigator xmlCore = objCharacter != null
                ? objCharacter.LoadDataXPath("actions.xml")
                : XmlManager.LoadXPath("actions.xml");
            objReturn.AddFrom(xmlCore, string.Empty, true);
            foreach (string strFolder in HouseRuleFolders())
            {
                if (!Directory.Exists(strFolder))
                    continue;
                foreach (string strFile in Directory.EnumerateFiles(strFolder, "*.xml").OrderBy(x => x, StringComparer.Ordinal))
                {
                    try
                    {
                        XPathDocument xmlDocument = new XPathDocument(strFile);
                        objReturn.AddFrom(xmlDocument.CreateNavigator(), Path.GetFileNameWithoutExtension(strFile), false);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        Utils.BreakIfDebug();
                        Log.Warn(ex, "Could not read house rule file " + strFile);
                    }
                }
            }
            return objReturn;
        }

        public void AddFrom(XPathNavigator xmlRoot, string strDefaultBook, bool blnTranslate)
        {
            if (xmlRoot == null)
                return;
            foreach (XPathNavigator xmlAction in xmlRoot.Select("/chummer/actions/action"))
            {
                string strName = xmlAction.SelectSingleNode("name")?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(strName))
                    continue;
                string strBook = xmlAction.SelectSingleNode("book")?.Value ?? strDefaultBook;
                RollAction objAction = new RollAction
                {
                    Id = xmlAction.SelectSingleNode("id")?.Value ?? string.Empty,
                    Name = strName,
                    DisplayName = (blnTranslate ? xmlAction.SelectSingleNode("translate")?.Value : null) ?? strName,
                    Dice = xmlAction.SelectSingleNode("test/dice")?.Value ?? string.Empty,
                    Limit = xmlAction.SelectSingleNode("test/limit")?.Value ?? string.Empty,
                    Description = xmlAction.SelectSingleNode("test/bonusstring")?.Value
                                  ?? xmlAction.SelectSingleNode("notes")?.Value ?? string.Empty,
                    Category = xmlAction.SelectSingleNode("category")?.Value ?? string.Empty,
                    ActionType = xmlAction.SelectSingleNode("type")?.Value ?? string.Empty,
                    Source = xmlAction.SelectSingleNode("source")?.Value ?? string.Empty,
                    Page = xmlAction.SelectSingleNode("page")?.Value ?? string.Empty,
                    Book = strBook,
                    Keywords = xmlAction.SelectSingleNode("keywords")?.Value ?? xmlAction.SelectSingleNode("section")?.Value ?? string.Empty,
                };
                Add(objAction);
            }
        }

        /// <summary>
        /// Ranked search: every query word must match name, keywords, category, book or description; name hits rank first.
        /// </summary>
        public IEnumerable<RollAction> Search(string strQuery, bool blnRollableOnly = true)
        {
            IEnumerable<RollAction> lstPool = blnRollableOnly ? _lstActions.Where(x => x.IsRollable) : _lstActions;
            if (string.IsNullOrWhiteSpace(strQuery))
                return lstPool.OrderBy(x => x.IsHouseRule ? 0 : 1).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase);
            string[] astrWords = strQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return lstPool.Select(x => new { Action = x, Score = Score(x, astrWords) })
                          .Where(x => x.Score > 0)
                          .OrderByDescending(x => x.Score)
                          .ThenBy(x => x.Action.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                          .Select(x => x.Action);
        }

        private static int Score(RollAction objAction, string[] astrWords)
        {
            int intScore = 0;
            foreach (string strWord in astrWords)
            {
                int intWord = 0;
                if (objAction.DisplayName.StartsWith(strWord, StringComparison.CurrentCultureIgnoreCase))
                    intWord = 100;
                else if (objAction.DisplayName.IndexOf(" " + strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 80;
                else if (objAction.DisplayName.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 60;
                else if (objAction.Keywords.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 40;
                else if (objAction.Category.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0
                         || objAction.Book.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 30;
                else if (objAction.Dice.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 20;
                else if (objAction.Description.IndexOf(strWord, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    intWord = 10;
                if (intWord == 0)
                    return 0;
                intScore += intWord;
            }
            return intScore;
        }
    }

    /// <summary>
    /// Builds the text pasted into Roll20 chat. Templates use placeholders:
    /// {name} {pool} {limit} {threshold} {opposed} {breakdown} {character}. Hits are counted with "&gt;5" (5s and 6s).
    /// </summary>
    public sealed class Roll20Formatter
    {
        public const string DefaultTemplate = "&{template:default} {{name={character}: {name}}} {{Hits=[[{pool}d6>5]]}}{limitrow}{thresholdrow}{opposedrow}";
        public const string DefaultEdgeTemplate = "&{template:default} {{name={character}: {name} (Edge)}} {{Hits=[[{pool}d6!>6>5]]}}{thresholdrow}{opposedrow} {{Note=Edge: no limit, sixes explode}}";

        public string Template { get; set; } = DefaultTemplate;
        public string EdgeTemplate { get; set; } = DefaultEdgeTemplate;

        public string Format(RollAction objAction, RollEvaluation objEvaluation, string strCharacterName, bool blnEdge = false, int intEdge = 0, int intModifier = 0)
        {
            if (objAction == null)
                throw new ArgumentNullException(nameof(objAction));
            if (objEvaluation == null)
                throw new ArgumentNullException(nameof(objEvaluation));
            int intPool = Math.Max(0, objEvaluation.Pool.Value + intModifier + (blnEdge ? intEdge : 0));
            string strLimit = objEvaluation.Limit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            string strThreshold = objEvaluation.Threshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            string strTemplate = blnEdge ? EdgeTemplate : Template;
            return strTemplate
                   .Replace("{limitrow}", strLimit.Length > 0 ? " {{Limit=" + strLimit + "}}" : string.Empty)
                   .Replace("{thresholdrow}", strThreshold.Length > 0 ? " {{Threshold=" + strThreshold + "}}" : string.Empty)
                   .Replace("{opposedrow}", objEvaluation.IsOpposed ? " {{Opposed by=" + objEvaluation.OpposedBy + "}}" : string.Empty)
                   .Replace("{character}", Sanitize(strCharacterName))
                   .Replace("{name}", Sanitize(objAction.DisplayName))
                   .Replace("{pool}", intPool.ToString(CultureInfo.InvariantCulture))
                   .Replace("{limit}", strLimit)
                   .Replace("{threshold}", strThreshold)
                   .Replace("{opposed}", Sanitize(objEvaluation.OpposedBy))
                   .Replace("{breakdown}", Sanitize(objEvaluation.Pool.Breakdown));
        }

        // Roll20 template rows break on "}}" and "{{"; keep labels plain.
        private static string Sanitize(string strText) => (strText ?? string.Empty).Replace("{", "(").Replace("}", ")").Replace("\n", " ");
    }
}
