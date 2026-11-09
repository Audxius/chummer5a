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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    /// <summary>
    /// Adding, removing and re-levelling qualities, lifted from the legacy character forms (CharacterCreate / CharacterCareer
    /// cmdAddQuality_Click, RemoveQuality, nudQualityLevel_ValueChanged, UpdateQualityLevelValue and
    /// CharacterShared.RemoveAddedQualities) so every UI charges the same karma and writes the same expense entries.
    /// </summary>
    public static class QualityRules
    {
        // ---------- queries ----------

        /// <summary>
        /// Highest level the quality can be set to, or 0 when its level cannot be changed (UpdateQualityLevelValue: qualities
        /// from improvements or the metatype, unlevelled qualities).
        /// </summary>
        public static async Task<int> GetMaximumLevelsAsync(Quality objQuality, CancellationToken token = default)
        {
            if (objQuality == null)
                return 0;
            QualitySource eSource = await objQuality.GetOriginSourceAsync(token).ConfigureAwait(false);
            if (eSource == QualitySource.Improvement || eSource == QualitySource.QualityLevelImprovement || eSource == QualitySource.Metatype
                || await objQuality.GetLevelsAsync(token).ConfigureAwait(false) == 0)
                return 0;
            XPathNavigator objQualityNode = await objQuality.GetNodeXPathAsync(token).ConfigureAwait(false);
            string strLimitString = objQualityNode?.SelectSingleNodeAndCacheExpression("limit", token: token)?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(strLimitString)
                && objQualityNode.SelectSingleNodeAndCacheExpression("nolevels", token: token) == null
                && int.TryParse(strLimitString, out int intMaxRating))
                return intMaxRating;
            return 0;
        }

        /// <summary>Whether legacy Chummer lets the quality be removed at all (metatype and live improvement qualities cannot).</summary>
        public static async Task<bool> CanRemoveAsync(Character objCharacter, Quality objQuality, CancellationToken token = default)
        {
            switch (await objQuality.GetOriginSourceAsync(token).ConfigureAwait(false))
            {
                case QualitySource.Metatype:
                    return false;
                case QualitySource.Heritage:
                    return await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
                case QualitySource.Improvement:
                case QualitySource.QualityLevelImprovement:
                    string strId = objQuality.InternalId;
                    return !await objCharacter.Improvements.AnyAsync(x => x.ImproveType == Improvement.ImprovementType.SpecificQuality
                                                                          && x.ImprovedName == strId, token).ConfigureAwait(false);
                default:
                    return true;
            }
        }

        /// <summary>Career karma for one more level of a quality the runner already has (nudQualityLevel_ValueChanged).</summary>
        public static async Task<int> GetCareerLevelCostAsync(Character objCharacter, Quality objQuality, CancellationToken token = default)
        {
            if (await objQuality.GetBPAsync(token).ConfigureAwait(false) == 0 || !await objQuality.GetContributeToBPAsync(token).ConfigureAwait(false))
                return 0;
            XPathNavigator objXmlQuality = await objQuality.GetNodeXPathAsync(token).ConfigureAwait(false);
            if (objXmlQuality == null)
                return 0;
            int intQualityBP = await GetBaseKarmaAsync(objCharacter, objXmlQuality, await objQuality.GetTypeAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            int intKarmaCost = intQualityBP * await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
            if (!await objSettings.GetDontDoubleQualityPurchasesAsync(token).ConfigureAwait(false) && objQuality.DoubleCost)
                intKarmaCost *= 2;
            return intKarmaCost;
        }

        /// <summary>
        /// Career karma to get rid of a quality (all of its levels when <paramref name="blnCompleteDelete"/>): the buy-off cost of a
        /// negative quality (doubled unless the settings say otherwise), or the negative of a refund for positive qualities that refund.
        /// </summary>
        public static async Task<int> GetCareerRemovalCostAsync(Character objCharacter, Quality objQuality, bool blnCompleteDelete = true, CancellationToken token = default)
        {
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            if (await objQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Positive)
            {
                XPathNavigator objXmlDeleteQuality = await objQuality.GetNodeXPathAsync(token).ConfigureAwait(false);
                if (objXmlDeleteQuality?.SelectSingleNodeAndCacheExpression("refundkarmaonremove", token: token) == null)
                    return 0;
                int intRefund = objQuality.BP * await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                if (!await objSettings.GetDontDoubleQualityPurchasesAsync(token).ConfigureAwait(false) && objQuality.DoubleCost)
                    intRefund *= 2;
                return -intRefund;
            }
            int intKarmaCost = -(objQuality.BP * await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false));
            if (!await objSettings.GetDontDoubleQualityRefundsAsync(token).ConfigureAwait(false))
                intKarmaCost *= 2;
            if (blnCompleteDelete)
                intKarmaCost *= await objQuality.GetLevelsAsync(token).ConfigureAwait(false);
            return intKarmaCost;
        }

        private static async Task<int> GetBaseKarmaAsync(Character objCharacter, XPathNavigator objXmlQuality, QualityType eQualityType, CancellationToken token)
        {
            int intQualityBP = 0;
            objXmlQuality.TryGetInt32FieldQuickly("karma", ref intQualityBP);
            XPathNavigator xpnDiscountNode = objXmlQuality.SelectSingleNodeAndCacheExpression("costdiscount", token);
            if (xpnDiscountNode != null && await xpnDiscountNode.RequirementsMetAsync(objCharacter, token: token).ConfigureAwait(false))
            {
                int intTemp = 0;
                xpnDiscountNode.TryGetInt32FieldQuickly("value", ref intTemp);
                switch (eQualityType)
                {
                    case QualityType.Positive:
                        intQualityBP += intTemp;
                        break;
                    case QualityType.Negative:
                        intQualityBP -= intTemp;
                        break;
                }
            }
            return intQualityBP;
        }

        // ---------- adding ----------

        /// <summary>
        /// Adds <paramref name="intRating"/> levels of the quality with this id or name (cmdAddQuality_Click after the picker closed).
        /// Create mode enforces the quality karma limits; career mode charges karma (doubled unless the settings or the quality say
        /// otherwise) and writes the expense log, asking for the same confirmations as legacy Chummer.
        /// </summary>
        public static async Task<RuleResult> AddAsync(Character objCharacter, string strQualityId, int intRating = 1, bool blnFreeCost = false,
                                                      CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            XmlDocument objXmlDocument = await objCharacter.LoadDataAsync("qualities.xml", token: token).ConfigureAwait(false);
            XmlNode objXmlQuality = objXmlDocument.TryGetNodeByNameOrId("/chummer/qualities/quality", strQualityId);
            if (objXmlQuality == null)
                return RuleResult.NotAllowed;
            int intRatingToAdd = intRating;
            int intDummy = 0;
            if (objXmlQuality["nolevels"] == null && objXmlQuality.TryGetInt32FieldQuickly("limit", ref intDummy))
            {
                intRatingToAdd -= await (await objCharacter.GetQualitiesAsync(token).ConfigureAwait(false))
                    .CountAsync(async (x, t) =>
                        (await x.GetSourceIDStringAsync(t).ConfigureAwait(false)).Equals(strQualityId, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrEmpty(await x.GetSourceNameAsync(t).ConfigureAwait(false)), token).ConfigureAwait(false);
            }
            if (intRatingToAdd <= 0)
                return RuleResult.NotAllowed;
            return await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                ? await AddCareerAsync(objCharacter, objXmlQuality, intRatingToAdd, blnFreeCost, token).ConfigureAwait(false)
                : await AddCreateAsync(objCharacter, objXmlQuality, intRatingToAdd, blnFreeCost, token).ConfigureAwait(false);
        }

        // CharacterCreate.cmdAddQuality_Click
        private static async Task<RuleResult> AddCreateAsync(Character objCharacter, XmlNode objXmlQuality, int intRatingToAdd, bool blnFreeCost, CancellationToken token)
        {
            RuleResult eResult = RuleResult.Cancelled;
            for (int i = 1; i <= intRatingToAdd; ++i)
            {
                List<Weapon> lstWeapons = new List<Weapon>(1);
                Quality objQuality = new Quality(objCharacter);
                try
                {
                    await objQuality.CreateAsync(objXmlQuality, QualitySource.Selected, lstWeapons, token: token).ConfigureAwait(false);
                    if (objQuality.InternalId.IsEmptyGuid())
                    {
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                        break;
                    }
                    if (blnFreeCost)
                        await objQuality.SetBPAsync(0, token).ConfigureAwait(false);
                    if (!await CheckCreateLimitsAsync(objCharacter, objQuality, token).ConfigureAwait(false))
                    {
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                        return eResult == RuleResult.Changed ? eResult : RuleResult.NotAllowed;
                    }
                    await objCharacter.Qualities.AddAsync(objQuality, token).ConfigureAwait(false);
                    foreach (Weapon objWeapon in lstWeapons)
                        await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                    eResult = RuleResult.Changed;
                }
                catch
                {
                    await objQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
            return eResult;
        }

        /// <summary>Create mode: would this quality push the runner past the positive/negative quality karma limits? Shows legacy's message.</summary>
        private static async Task<bool> CheckCreateLimitsAsync(Character objCharacter, Quality objQuality, CancellationToken token)
        {
            if (!await objQuality.GetContributeToLimitAsync(token).ConfigureAwait(false) || await objCharacter.GetIgnoreRulesAsync(token).ConfigureAwait(false))
                return true;
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            int intMaxQualityAmount = await objSettings.GetQualityKarmaLimitAsync(token).ConfigureAwait(false);
            string strAmount = intMaxQualityAmount.ToString(GlobalSettings.CultureInfo)
                               + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                               + await LanguageManager.GetStringAsync("String_Karma", token: token).ConfigureAwait(false);
            int intBP = await objQuality.GetBPAsync(token).ConfigureAwait(false);
            if (await objQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Negative)
            {
                if (await objSettings.GetExceedNegativeQualitiesAsync(token).ConfigureAwait(false))
                    return true;
                intBP += await objCharacter.GetNegativeQualityLimitKarmaAsync(token).ConfigureAwait(false);
                if (intBP < intMaxQualityAmount * -1)
                {
                    await ShowInfoAsync(StringExtensions.FastFormat(await LanguageManager.GetStringAsync("Message_NegativeQualityLimit", token: token).ConfigureAwait(false), strAmount),
                                        "MessageTitle_NegativeQualityLimit", token).ConfigureAwait(false);
                    return false;
                }
                int intMetatypeBP = await objCharacter.GetMetatypeBPAsync(token).ConfigureAwait(false);
                if (intMetatypeBP < 0 && intBP + intMetatypeBP < intMaxQualityAmount * -1)
                {
                    await ShowInfoAsync(StringExtensions.FastFormat(await LanguageManager.GetStringAsync("Message_NegativeQualityAndMetatypeLimit", token: token).ConfigureAwait(false), strAmount),
                                        "MessageTitle_NegativeQualityLimit", token).ConfigureAwait(false);
                    return false;
                }
                return true;
            }
            if (await objSettings.GetExceedPositiveQualitiesAsync(token).ConfigureAwait(false))
                return true;
            intBP += await objCharacter.GetPositiveQualityLimitKarmaAsync(token).ConfigureAwait(false);
            if (intBP > intMaxQualityAmount)
            {
                await ShowInfoAsync(StringExtensions.FastFormat(await LanguageManager.GetStringAsync("Message_PositiveQualityLimit", token: token).ConfigureAwait(false), strAmount),
                                    "MessageTitle_PositiveQualityLimit", token).ConfigureAwait(false);
                return false;
            }
            return true;
        }

        // CharacterCareer.cmdAddQuality_Click
        private static async Task<RuleResult> AddCareerAsync(Character objCharacter, XmlNode objXmlQuality, int intRatingToAdd, bool blnFreeCost, CancellationToken token)
        {
            QualityType eQualityType = QualityType.Positive;
            string strTemp = string.Empty;
            if (objXmlQuality.TryGetStringFieldQuickly("category", ref strTemp))
                eQualityType = Quality.ConvertToQualityType(strTemp);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            RuleResult eResult = RuleResult.Cancelled;
            for (int i = 1; i <= intRatingToAdd; ++i)
            {
                // Positive Metagenic Qualities are free if you're a Changeling.
                if (objCharacter.MetagenicLimit > 0 && objXmlQuality["metagenic"]?.InnerText == bool.TrueString)
                    blnFreeCost = true;
                // The Beast's Way and the Spiritual Way get the Mentor Spirit for free.
                else if (objXmlQuality["name"]?.InnerText == "Mentor Spirit"
                         && await objCharacter.Qualities.AnyAsync(x => x.Name == "The Beast's Way" || x.Name == "The Spiritual Way", token).ConfigureAwait(false))
                    blnFreeCost = true;

                int intQualityBP = 0;
                if (!blnFreeCost)
                    intQualityBP = await GetBaseKarmaAsync(objCharacter, objXmlQuality.CreateNavigator(), eQualityType, token).ConfigureAwait(false);
                int intKarmaCost = intQualityBP * await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                if (!await objSettings.GetDontDoubleQualityPurchasesAsync(token).ConfigureAwait(false) && objXmlQuality["doublecareer"]?.InnerText != bool.FalseString)
                    intKarmaCost *= 2;

                if (eQualityType == QualityType.Positive)
                {
                    if (!blnFreeCost)
                    {
                        if (intKarmaCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false)
                            && objXmlQuality["stagedpurchase"]?.InnerText != bool.TrueString)
                        {
                            await ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                            break;
                        }
                        string strDisplayName = objXmlQuality["translate"]?.InnerText ?? objXmlQuality["name"]?.InnerText
                                                ?? await LanguageManager.GetStringAsync("String_Unknown", token: token).ConfigureAwait(false);
                        if (!await CommonFunctions.ConfirmKarmaExpenseAsync(
                                StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                                    await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                                    strDisplayName, intKarmaCost), token).ConfigureAwait(false))
                            break;
                    }
                }
                else if (await MessageBoxes.ShowAsync(
                             await LanguageManager.GetStringAsync("Message_AddNegativeQuality", token: token).ConfigureAwait(false),
                             await LanguageManager.GetStringAsync("MessageTitle_AddNegativeQuality", token: token).ConfigureAwait(false),
                             MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) == MessageResult.No)
                    break;

                List<Weapon> lstWeapons = new List<Weapon>(1);
                Quality objQuality = new Quality(objCharacter);
                try
                {
                    await objQuality.CreateAsync(objXmlQuality, QualitySource.Selected, lstWeapons, token: token).ConfigureAwait(false);
                    if (objQuality.InternalId.IsEmptyGuid())
                    {
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                        break;
                    }
                    if (await objQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Positive)
                    {
                        if (await objQuality.GetContributeToBPAsync(token).ConfigureAwait(false))
                            await AddExpenseAsync(objCharacter, -intKarmaCost, "String_ExpenseAddPositiveQuality", objQuality, token).ConfigureAwait(false);
                    }
                    else
                        await AddExpenseAsync(objCharacter, 0, "String_ExpenseAddNegativeQuality", objQuality, token).ConfigureAwait(false);
                    await objCharacter.Qualities.AddAsync(objQuality, token).ConfigureAwait(false);
                    foreach (Weapon objWeapon in lstWeapons)
                        await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                    eResult = RuleResult.Changed;
                }
                catch
                {
                    await objQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
            return eResult;
        }

        // The karma expense (and undo record) legacy Chummer writes when a quality is bought in career mode.
        private static async Task AddExpenseAsync(Character objCharacter, int intAmount, string strReasonKey, Quality objQuality, CancellationToken token)
        {
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(intAmount,
                              await LanguageManager.GetStringAsync(strReasonKey, token: token).ConfigureAwait(false)
                              + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                              + await objQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                              ExpenseType.Karma, DateTime.Now);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            if (intAmount != 0)
                await objCharacter.ModifyKarmaAsync(intAmount, token).ConfigureAwait(false);
            ExpenseUndo objUndo = new ExpenseUndo();
            objUndo.CreateKarma(KarmaExpenseType.AddQuality, objQuality.InternalId);
            objExpense.Undo = objUndo;
        }

        // ---------- levels ----------

        /// <summary>
        /// Sets the number of levels of a levelled quality (nudQualityLevel_ValueChanged): new levels are added one by one with the
        /// same checks and costs as adding the quality, removed levels go through <see cref="RemoveAsync"/> without confirmation.
        /// </summary>
        public static async Task<RuleResult> SetLevelsAsync(Character objCharacter, Quality objSelectedQuality, int intSelectedLevels, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objSelectedQuality == null)
                throw new ArgumentNullException(nameof(objSelectedQuality));
            int intMaximum = await GetMaximumLevelsAsync(objSelectedQuality, token).ConfigureAwait(false);
            if (intMaximum <= 0 || intSelectedLevels < 0 || intSelectedLevels > intMaximum)
                return RuleResult.NotAllowed;
            bool blnCreated = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            int intCurrentLevels = await objSelectedQuality.GetLevelsAsync(token).ConfigureAwait(false);
            RuleResult eResult = RuleResult.Cancelled;
            string strQualityLocalName = await LanguageManager.GetStringAsync("String_Quality", token: token).ConfigureAwait(false);
            // Adding new levels
            for (; intSelectedLevels > intCurrentLevels; ++intCurrentLevels)
            {
                XPathNavigator objXmlSelectedQuality = await objSelectedQuality.GetNodeXPathAsync(token).ConfigureAwait(false);
                if (objXmlSelectedQuality == null
                    || !await objXmlSelectedQuality.RequirementsMetAsync(objCharacter, strLocalName: strQualityLocalName, token: token).ConfigureAwait(false))
                    return eResult == RuleResult.Changed ? eResult : RuleResult.NotAllowed;

                int intKarmaCost = 0;
                QualityType eQualityType = await objSelectedQuality.GetTypeAsync(token).ConfigureAwait(false);
                if (blnCreated)
                {
                    bool blnFreeCost = await objSelectedQuality.GetBPAsync(token).ConfigureAwait(false) == 0
                                       || !await objSelectedQuality.GetContributeToBPAsync(token).ConfigureAwait(false);
                    intKarmaCost = await GetCareerLevelCostAsync(objCharacter, objSelectedQuality, token).ConfigureAwait(false);
                    if (eQualityType == QualityType.Positive)
                    {
                        if (!blnFreeCost)
                        {
                            if (intKarmaCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false) && !objSelectedQuality.StagedPurchase)
                            {
                                await ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                                return eResult == RuleResult.Changed ? eResult : RuleResult.NotEnoughKarma;
                            }
                            string strDisplayName = objXmlSelectedQuality.SelectSingleNodeAndCacheExpression("translate", token)?.Value
                                                    ?? objXmlSelectedQuality.SelectSingleNodeAndCacheExpression("name", token)?.Value
                                                    ?? await LanguageManager.GetStringAsync("String_Unknown", token: token).ConfigureAwait(false);
                            if (!await CommonFunctions.ConfirmKarmaExpenseAsync(
                                    StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                                        await LanguageManager.GetStringAsync("Message_ConfirmKarmaExpenseSpend", token: token).ConfigureAwait(false),
                                        strDisplayName, intKarmaCost), token).ConfigureAwait(false))
                                return eResult;
                        }
                    }
                    else if (await MessageBoxes.ShowAsync(
                                 await LanguageManager.GetStringAsync("Message_AddNegativeQuality", token: token).ConfigureAwait(false),
                                 await LanguageManager.GetStringAsync("MessageTitle_AddNegativeQuality", token: token).ConfigureAwait(false),
                                 MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) == MessageResult.No)
                        return eResult;
                }

                List<Weapon> lstWeapons = new List<Weapon>(1);
                Quality objQuality = new Quality(objCharacter);
                try
                {
                    await objQuality.CreateAsync(await objSelectedQuality.GetNodeAsync(token).ConfigureAwait(false), QualitySource.Selected, lstWeapons,
                                                 objSelectedQuality.Extra, token: token).ConfigureAwait(false);
                    if (objQuality.InternalId.IsEmptyGuid())
                    {
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                        return eResult == RuleResult.Changed ? eResult : RuleResult.NotAllowed;
                    }
                    await objQuality.SetBPAsync(await objSelectedQuality.GetBPAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                    await objQuality.SetContributeToLimitAsync(await objSelectedQuality.GetContributeToLimitAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                    if (blnCreated)
                    {
                        if (await objQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Positive)
                            await AddExpenseAsync(objCharacter, -intKarmaCost, "String_ExpenseAddPositiveQuality", objQuality, token).ConfigureAwait(false);
                        else
                            await AddExpenseAsync(objCharacter, 0, "String_ExpenseAddNegativeQuality", objQuality, token).ConfigureAwait(false);
                    }
                    else if (!await CheckCreateLimitsAsync(objCharacter, objQuality, token).ConfigureAwait(false))
                    {
                        await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                        return eResult == RuleResult.Changed ? eResult : RuleResult.NotAllowed;
                    }
                    await objCharacter.Qualities.AddAsync(objQuality, token).ConfigureAwait(false);
                    foreach (Weapon objWeapon in lstWeapons)
                        await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                    eResult = RuleResult.Changed;
                }
                catch
                {
                    await objQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            // Removing levels: drop the other instances first, the selected one last.
            Guid guiSourceID = objSelectedQuality.SourceID;
            string strExtra = await objSelectedQuality.GetExtraAsync(token).ConfigureAwait(false);
            string strSourceName = await objSelectedQuality.GetSourceNameAsync(token).ConfigureAwait(false);
            string strInternalId = objSelectedQuality.InternalId;
            bool blnDoRemoveQuality = false;
            for (; intSelectedLevels < intCurrentLevels; --intCurrentLevels)
            {
                Quality objInvisibleQuality = await objCharacter.Qualities.FirstOrDefaultAsync(
                    async (x, t) => x.SourceID == guiSourceID
                                    && await x.GetExtraAsync(t).ConfigureAwait(false) == strExtra
                                    && await x.GetSourceNameAsync(t).ConfigureAwait(false) == strSourceName
                                    && x.InternalId != strInternalId
                                    && !ReferenceEquals(x, objSelectedQuality), token).ConfigureAwait(false);
                if (objInvisibleQuality == null)
                {
                    blnDoRemoveQuality = true;
                    break;
                }
                if (await RemoveCoreAsync(objCharacter, objInvisibleQuality, false, false, token).ConfigureAwait(false) != RuleResult.Changed)
                    return eResult;
                eResult = RuleResult.Changed;
            }
            if (blnDoRemoveQuality && await RemoveCoreAsync(objCharacter, objSelectedQuality, false, false, token).ConfigureAwait(false) == RuleResult.Changed)
                eResult = RuleResult.Changed;
            return eResult;
        }

        // ---------- removing ----------

        /// <summary>
        /// Removes a quality with all its levels (cmdDeleteQuality_Click), with legacy's confirmation. In career mode buying off a
        /// negative quality costs karma (twice its value unless the settings say otherwise) and is logged as an expense.
        /// </summary>
        public static Task<RuleResult> RemoveAsync(Character objCharacter, Quality objQuality, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objQuality == null)
                throw new ArgumentNullException(nameof(objQuality));
            return RemoveCoreAsync(objCharacter, objQuality, true, true, token);
        }

        private static async Task<RuleResult> RemoveCoreAsync(Character objCharacter, Quality objQuality, bool blnConfirmDelete, bool blnCompleteDelete, CancellationToken token)
        {
            return await objCharacter.GetCreatedAsync(token).ConfigureAwait(false)
                ? await RemoveCareerAsync(objCharacter, objQuality, blnConfirmDelete, blnCompleteDelete, token).ConfigureAwait(false)
                : await RemoveCreateAsync(objCharacter, objQuality, blnConfirmDelete, blnCompleteDelete, token).ConfigureAwait(false);
        }

        // Shared by both modes: qualities granted by improvements that still exist cannot be removed.
        private static async Task<bool> CheckImprovementQualityAsync(Character objCharacter, Quality objSelectedQuality, CancellationToken token)
        {
            string strId = objSelectedQuality.InternalId;
            if (!await objCharacter.Improvements.AnyAsync(x => x.ImproveType == Improvement.ImprovementType.SpecificQuality && x.ImprovedName == strId, token).ConfigureAwait(false))
                return true;
            await MessageBoxes.ShowAsync(
                StringExtensions.FastFormat(await LanguageManager.GetStringAsync("Message_ImprovementQuality", token: token).ConfigureAwait(false),
                                            await objSelectedQuality.DisplaySourceNameAsync(GlobalSettings.Language, token).ConfigureAwait(false)),
                await LanguageManager.GetStringAsync("MessageTitle_MetavariantQuality", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
            return false;
        }

        // CharacterCareer.RemoveQuality
        private static async Task<RuleResult> RemoveCareerAsync(Character objCharacter, Quality objSelectedQuality, bool blnConfirmDelete, bool blnCompleteDelete, CancellationToken token)
        {
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            XPathNavigator objXmlDeleteQuality = await objSelectedQuality.GetNodeXPathAsync(token).ConfigureAwait(false);
            bool blnMetatypeQuality = false;
            switch (objSelectedQuality.OriginSource)
            {
                case QualitySource.Metatype:
                    await ShowInfoAsync(await LanguageManager.GetStringAsync("Message_MetavariantQuality", token: token).ConfigureAwait(false),
                                        "MessageTitle_MetavariantQuality", token).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                case QualitySource.Improvement:
                case QualitySource.QualityLevelImprovement:
                    if (!await CheckImprovementQualityAsync(objCharacter, objSelectedQuality, token).ConfigureAwait(false))
                        return RuleResult.NotAllowed;
                    break;
                case QualitySource.MetatypeRemovable:
                {
                    int intBP = 0;
                    if (await objSelectedQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Negative
                        || objXmlDeleteQuality?.SelectSingleNodeAndCacheExpression("refundkarmaonremove", token: token) != null)
                    {
                        int.TryParse(objXmlDeleteQuality?.SelectSingleNodeAndCacheExpression("karma", token: token)?.Value, NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out intBP);
                        intBP *= await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                        if (blnCompleteDelete)
                            intBP *= await objSelectedQuality.GetLevelsAsync(token).ConfigureAwait(false);
                        if (!await objSettings.GetDontDoubleQualityPurchasesAsync(token).ConfigureAwait(false) && objSelectedQuality.DoubleCost)
                            intBP *= 2;
                        if (objSelectedQuality.Type == QualityType.Positive)
                            intBP *= -1;
                    }
                    string strBP = intBP.ToString(GlobalSettings.CultureInfo)
                                   + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                                   + await LanguageManager.GetStringAsync("String_Karma", token: token).ConfigureAwait(false);
                    if (blnConfirmDelete && !await CommonFunctions.ConfirmDeleteAsync(
                            StringExtensions.FastFormat(
                                await LanguageManager.GetStringAsync(blnCompleteDelete ? "Message_DeleteMetatypeQuality" : "Message_LowerMetatypeQualityLevel", token: token).ConfigureAwait(false),
                                strBP), token).ConfigureAwait(false))
                        return RuleResult.Cancelled;
                    blnMetatypeQuality = true;
                    break;
                }
            }

            if (objSelectedQuality.Type == QualityType.Positive)
            {
                if (objXmlDeleteQuality?.SelectSingleNodeAndCacheExpression("refundkarmaonremove", token: token) != null)
                {
                    int intKarmaCost = objSelectedQuality.BP * await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                    if (!await objSettings.GetDontDoubleQualityPurchasesAsync(token).ConfigureAwait(false) && objSelectedQuality.DoubleCost)
                        intKarmaCost *= 2;
                    ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
                    objExpense.Create(intKarmaCost, StringExtensions.FastFormat(
                                          await LanguageManager.GetStringAsync("String_ExpenseSwapPositiveQuality", token: token).ConfigureAwait(false),
                                          await objSelectedQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                          await LanguageManager.GetStringAsync("String_Karma", token: token).ConfigureAwait(false)),
                                      ExpenseType.Karma, DateTime.Now, true);
                    await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                    await objCharacter.ModifyKarmaAsync(intKarmaCost, token).ConfigureAwait(false);
                    ExpenseUndo objUndo = new ExpenseUndo();
                    objUndo.CreateKarma(KarmaExpenseType.RemoveQuality, objSelectedQuality.SourceIDString);
                    objUndo.Extra = objSelectedQuality.Extra;
                    objExpense.Undo = objUndo;
                }
                else if (!blnMetatypeQuality && blnConfirmDelete && !await CommonFunctions.ConfirmDeleteAsync(
                             await LanguageManager.GetStringAsync(blnCompleteDelete ? "Message_DeletePositiveQualityCareer" : "Message_LowerPositiveQualityLevelCareer", token: token).ConfigureAwait(false),
                             token).ConfigureAwait(false))
                    return RuleResult.Cancelled;
            }
            else
            {
                // Make sure the character has enough Karma to buy off the Quality.
                int intTotalKarmaCost = await GetCareerRemovalCostAsync(objCharacter, objSelectedQuality, blnCompleteDelete, token).ConfigureAwait(false);
                if (intTotalKarmaCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false))
                {
                    await ShowNotEnoughKarmaAsync(token).ConfigureAwait(false);
                    return RuleResult.NotEnoughKarma;
                }
                if (!blnMetatypeQuality && blnConfirmDelete && !await CommonFunctions.ConfirmKarmaExpenseAsync(
                        StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync(blnCompleteDelete ? "Message_ConfirmKarmaExpenseRemove" : "Message_ConfirmKarmaExpenseLowerLevel", token: token).ConfigureAwait(false),
                            await objSelectedQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), intTotalKarmaCost), token).ConfigureAwait(false))
                    return RuleResult.Cancelled;
                ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
                objExpense.Create(-intTotalKarmaCost,
                                  await LanguageManager.GetStringAsync("String_ExpenseRemoveNegativeQuality", token: token).ConfigureAwait(false)
                                  + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                                  + await objSelectedQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                  ExpenseType.Karma, DateTime.Now);
                await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                await objCharacter.ModifyKarmaAsync(-intTotalKarmaCost, token).ConfigureAwait(false);
                ExpenseUndo objUndo = new ExpenseUndo();
                objUndo.CreateKarma(KarmaExpenseType.RemoveQuality, objSelectedQuality.SourceIDString);
                objUndo.Extra = objSelectedQuality.Extra;
                objExpense.Undo = objUndo;
            }

            // Remove any Critter Powers that are gained through the Quality (Infected).
            if (objXmlDeleteQuality?.SelectSingleNodeAndCacheExpression("powers/power", token: token) != null)
            {
                foreach (XPathNavigator objXmlPower in (await objCharacter.LoadDataXPathAsync("critterpowers.xml", token: token).ConfigureAwait(false))
                         .SelectAndCacheExpression("optionalpowers/optionalpower", token: token))
                {
                    string strExtra = objXmlPower.SelectSingleNodeAndCacheExpression("@select", token: token)?.Value ?? string.Empty;
                    CritterPower objRemovePower = await objCharacter.CritterPowers.FirstOrDefaultAsync(x => x.Name == objXmlPower.Value && x.Extra == strExtra, token).ConfigureAwait(false);
                    if (objRemovePower == null)
                        continue;
                    await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.CritterPower, objRemovePower.InternalId, token).ConfigureAwait(false);
                    await objCharacter.CritterPowers.RemoveAsync(objRemovePower, token).ConfigureAwait(false);
                }
            }

            if (objXmlDeleteQuality != null)
                await RemoveAddedQualitiesAsync(objCharacter, objXmlDeleteQuality.SelectAndCacheExpression("addqualities/addquality", token), token).ConfigureAwait(false);

            await objSelectedQuality.DeleteQualityAsync(blnCompleteDelete, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // CharacterCreate.RemoveQuality
        private static async Task<RuleResult> RemoveCreateAsync(Character objCharacter, Quality objSelectedQuality, bool blnConfirmDelete, bool blnCompleteDelete, CancellationToken token)
        {
            XmlNode objXmlDeleteQuality = await objSelectedQuality.GetNodeAsync(token: token).ConfigureAwait(false);
            switch (await objSelectedQuality.GetOriginSourceAsync(token).ConfigureAwait(false))
            {
                case QualitySource.Metatype:
                case QualitySource.Heritage:
                    await ShowInfoAsync(await LanguageManager.GetStringAsync("Message_MetavariantQuality", token: token).ConfigureAwait(false),
                                        "MessageTitle_MetavariantQuality", token).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                case QualitySource.Improvement:
                case QualitySource.QualityLevelImprovement:
                    if (!await CheckImprovementQualityAsync(objCharacter, objSelectedQuality, token).ConfigureAwait(false))
                        return RuleResult.NotAllowed;
                    break;
            }

            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            if (await objSelectedQuality.GetOriginSourceAsync(token).ConfigureAwait(false) == QualitySource.MetatypeRemovable)
            {
                int intBP = 0;
                if (await objSelectedQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Negative
                    && objXmlDeleteQuality?.TryGetInt32FieldQuickly("karma", ref intBP) == false)
                    intBP = -intBP;
                intBP *= await objSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                int intShowBP = intBP;
                if (blnCompleteDelete)
                    intShowBP *= await objSelectedQuality.GetLevelsAsync(token).ConfigureAwait(false);
                string strBP = intShowBP.ToString(GlobalSettings.CultureInfo)
                               + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                               + await LanguageManager.GetStringAsync("String_Karma", token: token).ConfigureAwait(false);
                if (blnConfirmDelete && !await CommonFunctions.ConfirmDeleteAsync(StringExtensions.FastFormat(
                        await LanguageManager.GetStringAsync(blnCompleteDelete ? "Message_DeleteMetatypeQuality" : "Message_LowerMetatypeQualityLevel", token: token).ConfigureAwait(false),
                        strBP), token).ConfigureAwait(false))
                    return RuleResult.Cancelled;

                // A removed metatype quality is replaced by an invisible one of the opposite type that keeps the karma balance.
                if (objXmlDeleteQuality != null)
                {
                    XmlNode xmlDeleteQualityNoBonus = objXmlDeleteQuality.Clone();
                    if (xmlDeleteQualityNoBonus["bonus"] != null)
                        xmlDeleteQualityNoBonus["bonus"].InnerText = string.Empty;
                    if (xmlDeleteQualityNoBonus["firstlevelbonus"] != null)
                        xmlDeleteQualityNoBonus["firstlevelbonus"].InnerText = string.Empty;
                    List<Weapon> lstWeapons = new List<Weapon>(1);
                    Quality objReplaceQuality = new Quality(objCharacter);
                    try
                    {
                        await objReplaceQuality.CreateAsync(xmlDeleteQualityNoBonus, QualitySource.MetatypeRemovedAtChargen, lstWeapons, token: token).ConfigureAwait(false);
                        await objReplaceQuality.SetBPAsync(-await objReplaceQuality.GetBPAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                        string strSeparator = await LanguageManager.GetStringAsync("String_Comma", token: token).ConfigureAwait(false)
                                              + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
                        if (await objSelectedQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.Positive)
                        {
                            objReplaceQuality.Type = QualityType.Negative;
                            if (!string.IsNullOrEmpty(objReplaceQuality.Extra))
                                objReplaceQuality.Extra += strSeparator;
                            objReplaceQuality.Extra += await LanguageManager.GetStringAsync("String_ExpenseRemovePositiveQuality", token: token).ConfigureAwait(false);
                        }
                        else
                        {
                            objReplaceQuality.Type = QualityType.Positive;
                            if (!string.IsNullOrEmpty(objReplaceQuality.Extra))
                                objReplaceQuality.Extra += strSeparator;
                            objReplaceQuality.Extra += await LanguageManager.GetStringAsync("String_ExpenseRemoveNegativeQuality", token: token).ConfigureAwait(false);
                        }
                        objReplaceQuality.AllowPrint = false;
                        await objReplaceQuality.SetContributeToLimitAsync(false, token).ConfigureAwait(false);
                        await objCharacter.Qualities.AddAsync(objReplaceQuality, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        await objReplaceQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
            }
            else
            {
                if (blnConfirmDelete && !await CommonFunctions.ConfirmDeleteAsync(
                        await LanguageManager.GetStringAsync(blnCompleteDelete ? "Message_DeleteQuality" : "Message_LowerQualityLevel", token: token).ConfigureAwait(false), token).ConfigureAwait(false))
                    return RuleResult.Cancelled;

                // Undoing the removal of a metatype quality brings the original quality back.
                if (await objSelectedQuality.GetOriginSourceAsync(token).ConfigureAwait(false) == QualitySource.MetatypeRemovedAtChargen)
                {
                    XPathNavigator xmlCharacterNode = await objCharacter.GetNodeXPathAsync(token: token).ConfigureAwait(false);
                    if (xmlCharacterNode != null)
                    {
                        XmlDocument xmlQualitiesDoc = await objCharacter.LoadDataAsync("qualities.xml", token: token).ConfigureAwait(false);
                        foreach (XPathNavigator objXmlQualityItem in xmlCharacterNode.Select(
                                     "qualities/*/quality[. = " + (await objSelectedQuality.GetNameAsync(token).ConfigureAwait(false)).CleanXPath() + "]"))
                        {
                            XmlNode objXmlQuality = xmlQualitiesDoc.TryGetNodeByNameOrId("/chummer/qualities/quality", objXmlQualityItem.Value);
                            string strForceValue = objXmlQualityItem.GetAttribute("select", string.Empty);
                            QualitySource objSource = objXmlQualityItem.GetAttribute("removable", string.Empty) == bool.TrueString
                                ? QualitySource.MetatypeRemovable
                                : QualitySource.Metatype;
                            Quality objQuality = new Quality(objCharacter);
                            try
                            {
                                await objQuality.CreateAsync(objXmlQuality, objSource, await objCharacter.GetWeaponsAsync(token).ConfigureAwait(false), strForceValue, token: token).ConfigureAwait(false);
                                await objCharacter.Qualities.AddAsync(objQuality, token).ConfigureAwait(false);
                            }
                            catch
                            {
                                await objQuality.DeleteQualityAsync(token: CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                        }
                    }
                }
            }

            if (await objSelectedQuality.GetTypeAsync(token).ConfigureAwait(false) == QualityType.LifeModule)
                objXmlDeleteQuality = Quality.GetNodeOverrideable(objSelectedQuality.SourceIDString,
                                                                  await objCharacter.LoadDataAsync("lifemodules.xml", token: token).ConfigureAwait(false));
            if (objXmlDeleteQuality != null)
                await RemoveAddedQualitiesAsync(objCharacter, objXmlDeleteQuality.SelectAndCacheExpressionAsNavigator("addqualities/addquality", token), token).ConfigureAwait(false);

            await objSelectedQuality.DeleteQualityAsync(blnCompleteDelete, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // CharacterShared.RemoveAddedQualities: old characters stored qualities added by other qualities as <addqualities>.
        private static async Task RemoveAddedQualitiesAsync(Character objCharacter, XPathNodeIterator objNodeList, CancellationToken token)
        {
            if (objNodeList == null || objNodeList.Count <= 0)
                return;
            foreach (XPathNavigator objNode in objNodeList)
            {
                Quality objQuality = await objCharacter.Qualities.FirstOrDefaultAsync(x => x.Name == objNode.Value, token).ConfigureAwait(false);
                if (objQuality == null)
                    continue;
                string strInternalId = objQuality.InternalId;
                await objQuality.DeleteQualityAsync(token: token).ConfigureAwait(false);
                await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.CritterPower, strInternalId, token).ConfigureAwait(false);
            }
        }

        // ---------- messages ----------

        internal static async Task ShowNotEnoughKarmaAsync(CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_NotEnoughKarma", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
        }

        internal static async Task ShowInfoAsync(string strMessage, string strTitleKey, CancellationToken token)
        {
            await MessageBoxes.ShowAsync(strMessage, await LanguageManager.GetStringAsync(strTitleKey, token: token).ConfigureAwait(false),
                                         MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
        }
    }
}
