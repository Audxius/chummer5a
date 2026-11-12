/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public sealed class ImprovementReapplyResult
    {
        public RuleResult Outcome { get; internal set; }
        public string OutdatedItems { get; internal set; } = string.Empty;
        public bool Restored { get; internal set; }
    }

    /// <summary>Legacy CharacterCreate/CharacterCareer improvement refresh with model rollback on failed selections.</summary>
    public static class ImprovementReapplyRules
    {
        public static async Task<ImprovementReapplyResult> ApplyAsync(Character character, IReadOnlyCollection<string> sourceIds = null, CancellationToken token = default)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            token.ThrowIfCancellationRequested();
            string originalFile = await character.GetFileNameAsync(token).ConfigureAwait(false);
            string snapshotDirectory = Path.Combine(Path.GetTempPath(), "chummer-reapply-" + Guid.NewGuid().ToString("N"));
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) Directory.CreateDirectory(snapshotDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else Directory.CreateDirectory(snapshotDirectory);
#else
            Directory.CreateDirectory(snapshotDirectory);
#endif
            string snapshot = Path.Combine(snapshotDirectory, "character.chum5");
            bool retainSnapshot = false;
            try
            {
                if (!await character.SaveAsync(snapshot, false, false, token: token).ConfigureAwait(false))
                    throw new IOException("Could not create a character snapshot before refreshing improvements.");
                var preserved = new HashSet<string>(StringComparer.Ordinal);
                var improvements = await character.Improvements.ToListAsync(token: token).ConfigureAwait(false);
                var owners = await character.GetItemsByInternalIdsAsync(improvements.Select(x => SourceBase(x.SourceName)), token: token).ConfigureAwait(false);
                foreach (var owner in owners)
                    if (owner is IHasXmlDataNode data && await data.GetNodeAsync(token: token).ConfigureAwait(false) == null)
                        foreach (var improvement in improvements.Where(x => SourceBase(x.SourceName) == owner.InternalId))
                            preserved.Add(improvement.SourceName);
                if (sourceIds != null)
                {
                    var scope = new HashSet<string>(sourceIds.Select(SourceBase), StringComparer.Ordinal);
                    foreach (string id in scope.ToArray()) { scope.Add(id + "Pair"); scope.Add(id + "Wireless"); }
                    foreach (var improvement in improvements)
                        if (scope.Contains(SourceBase(improvement.SourceName))) scope.Add(improvement.SourceName);
                    sourceIds = scope;
                }
                var outdated = new StringBuilder();
                try
                {
                    await ApplyCoreAsync(character, sourceIds, preserved, outdated, token).ConfigureAwait(false);
                    return new ImprovementReapplyResult { Outcome = RuleResult.Changed, OutdatedItems = outdated.ToString() };
                }
                catch (Exception failure)
                {
                    try
                    {
                        if (!await character.LoadAsync(snapshot, showWarnings: false, token: CancellationToken.None).ConfigureAwait(false))
                            throw new IOException("The character snapshot could not be restored.");
                        await character.SetFileNameAsync(originalFile, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception restoreFailure)
                    {
                        retainSnapshot = true;
                        throw new AggregateException("Improvement refresh failed. Recovery snapshot: " + snapshot, failure, restoreFailure);
                    }
                    if (failure is OperationCanceledException)
                        return new ImprovementReapplyResult { Outcome = RuleResult.Cancelled, Restored = true };
                    throw;
                }
            }
            finally
            {
                if (!retainSnapshot)
                {
                    if (File.Exists(snapshot)) File.Delete(snapshot);
                    if (Directory.Exists(snapshotDirectory) && Directory.GetFileSystemEntries(snapshotDirectory).Length == 0) Directory.Delete(snapshotDirectory);
                }
            }
        }

        private static string SourceBase(string name)
        {
            if (name != null && name.Length >= 36 && Guid.TryParse(name.Substring(0, 36), out Guid id)) return id.ToString("D");
            return name ?? string.Empty;
        }

        private static async Task<bool> CreateRequiredAsync(Character character, Improvement.ImprovementSource source, string sourceName,
            XmlNode bonus, int rating = 1, string friendlyName = "", bool addToCharacter = true, CancellationToken token = default)
        {
            if (!await ImprovementManager.CreateImprovementsAsync(character, source, sourceName, bonus, rating, friendlyName, addToCharacter, token).ConfigureAwait(false))
                throw new OperationCanceledException("Improvement selection was cancelled.");
            return true;
        }

        // Extracted model operations follow; UI tree rebuilding stays in the desktop layer.
        private static async Task ApplyCoreAsync(Character character, IReadOnlyCollection<string> lstInternalIdFilter, HashSet<string> preservedSources, StringBuilder sbdOutdatedItems, CancellationToken token)
        {
            Dictionary<string, string> dicAddSpiritForcedBySource = await ImprovementManager.SnapshotAddSpiritForcedValuesBySourceAsync(character, token).ConfigureAwait(false);
            // Wipe all improvements that we will reapply, this is mainly to eliminate orphaned improvements caused by certain bugs and also for a performance increase
            if (lstInternalIdFilter == null)
                await ImprovementManager.RemoveImprovementsAsync(character, await (await character.GetImprovementsAsync(token).ConfigureAwait(false)).ToListAsync(x => !preservedSources.Contains(x.SourceName) && (x.ImproveSource == Improvement.ImprovementSource.AIProgram || x.ImproveSource == Improvement.ImprovementSource.Armor || x.ImproveSource == Improvement.ImprovementSource.ArmorMod || x.ImproveSource == Improvement.ImprovementSource.Bioware || x.ImproveSource == Improvement.ImprovementSource.ComplexForm || x.ImproveSource == Improvement.ImprovementSource.CritterPower || x.ImproveSource == Improvement.ImprovementSource.Cyberware || x.ImproveSource == Improvement.ImprovementSource.Echo || x.ImproveSource == Improvement.ImprovementSource.Gear || x.ImproveSource == Improvement.ImprovementSource.MartialArt || x.ImproveSource == Improvement.ImprovementSource.MartialArtTechnique || x.ImproveSource == Improvement.ImprovementSource.Metamagic || x.ImproveSource == Improvement.ImprovementSource.Power || x.ImproveSource == Improvement.ImprovementSource.Quality || x.ImproveSource == Improvement.ImprovementSource.Spell || x.ImproveSource == Improvement.ImprovementSource.StackedFocus), token).ConfigureAwait(false), true, token: token).ConfigureAwait(false);
            else
                await ImprovementManager.RemoveImprovementsAsync(character, await (await character.GetImprovementsAsync(token).ConfigureAwait(false)).ToListAsync(x => !preservedSources.Contains(x.SourceName) && (lstInternalIdFilter.Contains(x.SourceName) && (x.ImproveSource == Improvement.ImprovementSource.AIProgram || x.ImproveSource == Improvement.ImprovementSource.Armor || x.ImproveSource == Improvement.ImprovementSource.ArmorMod || x.ImproveSource == Improvement.ImprovementSource.Bioware || x.ImproveSource == Improvement.ImprovementSource.ComplexForm || x.ImproveSource == Improvement.ImprovementSource.CritterPower || x.ImproveSource == Improvement.ImprovementSource.Cyberware || x.ImproveSource == Improvement.ImprovementSource.Echo || x.ImproveSource == Improvement.ImprovementSource.Gear || x.ImproveSource == Improvement.ImprovementSource.MartialArt || x.ImproveSource == Improvement.ImprovementSource.MartialArtTechnique || x.ImproveSource == Improvement.ImprovementSource.Metamagic || x.ImproveSource == Improvement.ImprovementSource.Power || x.ImproveSource == Improvement.ImprovementSource.Quality || x.ImproveSource == Improvement.ImprovementSource.Spell || x.ImproveSource == Improvement.ImprovementSource.StackedFocus)), token).ConfigureAwait(false), true, token: token).ConfigureAwait(false);
            // Refresh Qualities.
            // We cannot use foreach because qualities can add more qualities
            for (int j = 0; j < await character.Qualities.GetCountAsync(token).ConfigureAwait(false); j++)
            {
                Quality objQuality = await character.Qualities.GetValueAtAsync(j, token).ConfigureAwait(false);
                QualitySource eOriginSource = await objQuality.GetOriginSourceAsync(token).ConfigureAwait(false);
                if (eOriginSource == QualitySource.Improvement || eOriginSource == QualitySource.QualityLevelImprovement || eOriginSource == QualitySource.MetatypeRemovedAtChargen)
                    continue;
                // We're only re-apply improvements a list of items, not all of them
                if (lstInternalIdFilter?.Contains(objQuality.InternalId) == false)
                    continue;
                XmlNode objNode = await objQuality.GetNodeAsync(token: token).ConfigureAwait(false);
                if (objNode != null)
                {
                    string strSelected = objQuality.Extra;
                    if (string.IsNullOrEmpty(strSelected) && dicAddSpiritForcedBySource.TryGetValue(objQuality.InternalId, out string strSpiritForced))
                        strSelected = strSpiritForced;
                    objQuality.Bonus = objNode["bonus"];
                    if (objQuality.Bonus != null)
                    {
                        ImprovementManager.SetForcedValue(strSelected, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.Quality, objQuality.InternalId, objQuality.Bonus, 1, await objQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objQuality.Extra = strSelectedValue;
                        }
                    }

                    objQuality.FirstLevelBonus = objNode["firstlevelbonus"];
                    if (objQuality.FirstLevelBonus?.HasChildNodes == true)
                    {
                        string strCheckExtra = await objQuality.GetExtraAsync(token).ConfigureAwait(false);
                        string strCheckSourceName = await objQuality.GetSourceNameAsync(token).ConfigureAwait(false);
                        bool blnDoFirstLevel = true;
                        for (int k = 0; k < await character.Qualities.GetCountAsync(token).ConfigureAwait(false); ++k)
                        {
                            Quality objCheckQuality = await character.Qualities.GetValueAtAsync(k, token).ConfigureAwait(false);
                            if (j != k && objCheckQuality.SourceID == objQuality.SourceID && await objCheckQuality.GetExtraAsync(token).ConfigureAwait(false) == strCheckExtra && await objCheckQuality.GetSourceNameAsync(token).ConfigureAwait(false) == strCheckSourceName && (k < j || await objCheckQuality.GetOriginSourceAsync(token).ConfigureAwait(false) == QualitySource.Improvement || await objCheckQuality.GetOriginSourceAsync(token).ConfigureAwait(false) == QualitySource.QualityLevelImprovement || lstInternalIdFilter?.Contains(objCheckQuality.InternalId) == false))
                            {
                                blnDoFirstLevel = false;
                                break;
                            }
                        }

                        if (blnDoFirstLevel)
                        {
                            ImprovementManager.SetForcedValue(strSelected, character);
                            await CreateRequiredAsync(character, Improvement.ImprovementSource.Quality, objQuality.InternalId, objQuality.FirstLevelBonus, 1, await objQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                            string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                            if (!string.IsNullOrEmpty(strSelectedValue))
                            {
                                objQuality.Extra = strSelectedValue;
                            }
                        }
                    }

                    objQuality.NaturalWeaponsNode = objNode["naturalweapons"];
                    if (objQuality.NaturalWeaponsNode != null)
                    {
                        ImprovementManager.SetForcedValue(strSelected, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.Quality, objQuality.InternalId, objQuality.NaturalWeaponsNode, 1, await objQuality.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objQuality.Extra = strSelectedValue;
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objQuality.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                }
            }

            // Refresh Martial Art Techniques.
            await character.MartialArts.ForEachWithSideEffectsAsync(async (objMartialArt, t1) =>
            {
                XmlNode objMartialArtNode = await objMartialArt.GetNodeAsync(token: t1).ConfigureAwait(false);
                if (objMartialArtNode != null)
                {
                    // We're only re-apply improvements a list of items, not all of them
                    if (lstInternalIdFilter?.Contains(objMartialArt.InternalId) != false && objMartialArtNode["bonus"] != null)
                    {
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.MartialArt, objMartialArt.InternalId, objMartialArtNode["bonus"], 1, await objMartialArt.GetCurrentDisplayNameShortAsync(t1).ConfigureAwait(false), token: t1).ConfigureAwait(false);
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objMartialArt.GetCurrentDisplayNameShortAsync(t1).ConfigureAwait(false));
                }

                await objMartialArt.Techniques.ForEachWithSideEffectsAsync(async (objTechnique, t2) =>
                {
                    if (lstInternalIdFilter?.Contains(objTechnique.InternalId) == false)
                        return;
                    XmlNode objNode = await objTechnique.GetNodeAsync(token: t2).ConfigureAwait(false);
                    if (objNode != null)
                    {
                        if (objNode["bonus"] != null)
                            await CreateRequiredAsync(character, Improvement.ImprovementSource.MartialArtTechnique, objTechnique.InternalId, objNode["bonus"], 1, await objTechnique.GetCurrentDisplayNameAsync(t2).ConfigureAwait(false), token: t2).ConfigureAwait(false);
                    }
                    else
                    {
                        sbdOutdatedItems.AppendLine(await objTechnique.GetCurrentDisplayNameAsync(t2).ConfigureAwait(false));
                    }
                }, t1).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            // Refresh Spells.
            await character.Spells.ForEachWithSideEffectsAsync(async (objSpell, t) =>
            {
                if (lstInternalIdFilter?.Contains(objSpell.InternalId) == false)
                    return;
                XmlNode objNode = await objSpell.GetNodeAsync(token: t).ConfigureAwait(false);
                if (objNode != null)
                {
                    if (objNode["bonus"] != null)
                    {
                        ImprovementManager.SetForcedValue(objSpell.Extra, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.Spell, objSpell.InternalId, objNode["bonus"], 1, await objSpell.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false), token: t).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objSpell.Extra = strSelectedValue;
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objSpell.GetCurrentDisplayNameAsync(t).ConfigureAwait(false));
                }
            }, token).ConfigureAwait(false);
            // Refresh Adept Powers.
            await character.Powers.ForEachWithSideEffectsAsync(async (objPower, t) =>
            {
                if (lstInternalIdFilter?.Contains(objPower.InternalId) == false)
                    return;
                XmlNode objNode = await objPower.GetNodeAsync(token: t).ConfigureAwait(false);
                if (objNode != null)
                {
                    objPower.Bonus = objNode["bonus"];
                    if (objPower.Bonus != null)
                    {
                        ImprovementManager.SetForcedValue(objPower.Extra, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.Power, objPower.InternalId, objPower.Bonus, await objPower.GetTotalRatingAsync(t).ConfigureAwait(false), await objPower.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false), token: t).ConfigureAwait(false);
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objPower.GetCurrentDisplayNameAsync(t).ConfigureAwait(false));
                }
            }, token).ConfigureAwait(false);
            // Refresh Complex Forms.
            await character.ComplexForms.ForEachWithSideEffectsAsync(async (objComplexForm, t) =>
            {
                if (lstInternalIdFilter?.Contains(objComplexForm.InternalId) == false)
                    return;
                XmlNode objNode = await objComplexForm.GetNodeAsync(token: t).ConfigureAwait(false);
                if (objNode != null)
                {
                    if (objNode["bonus"] != null)
                    {
                        ImprovementManager.SetForcedValue(objComplexForm.Extra, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.ComplexForm, objComplexForm.InternalId, objNode["bonus"], 1, await objComplexForm.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false), token: t).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objComplexForm.Extra = strSelectedValue;
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objComplexForm.GetCurrentDisplayNameAsync(t).ConfigureAwait(false));
                }
            }, token).ConfigureAwait(false);
            // Refresh AI Programs and Advanced Programs
            await character.AIPrograms.ForEachWithSideEffectsAsync(async (objProgram, t) =>
            {
                if (lstInternalIdFilter?.Contains(objProgram.InternalId) == false)
                    return;
                XmlNode objNode = await objProgram.GetNodeAsync(token: t).ConfigureAwait(false);
                if (objNode != null)
                {
                    if (objNode["bonus"] != null)
                    {
                        ImprovementManager.SetForcedValue(objProgram.Extra, character);
                        await CreateRequiredAsync(character, Improvement.ImprovementSource.AIProgram, objProgram.InternalId, objNode["bonus"], 1, await objProgram.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false), token: t).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objProgram.Extra = strSelectedValue;
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objProgram.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false));
                }
            }, token).ConfigureAwait(false);
            // Refresh Critter Powers.
            await character.CritterPowers.ForEachWithSideEffectsAsync(async (objPower, t) =>
            {
                if (lstInternalIdFilter?.Contains(objPower.InternalId) == false)
                    return;
                XmlNode objNode = await objPower.GetNodeAsync(token: t).ConfigureAwait(false);
                if (objNode != null)
                {
                    objPower.Bonus = objNode["bonus"];
                    if (objPower.Bonus != null)
                    {
                        string strSelected = objPower.Extra;
                        if (!int.TryParse(strSelected, out int intRating))
                        {
                            intRating = 1;
                            ImprovementManager.SetForcedValue(strSelected, character);
                        }

                        await CreateRequiredAsync(character, Improvement.ImprovementSource.CritterPower, objPower.InternalId, objPower.Bonus, intRating, await objPower.GetCurrentDisplayNameShortAsync(t).ConfigureAwait(false), token: t).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue))
                        {
                            objPower.Extra = strSelectedValue;
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objPower.GetCurrentDisplayNameAsync(t).ConfigureAwait(false));
                }
            }, token).ConfigureAwait(false);
            // Refresh Metamagics and Echoes.
            // We cannot use foreach because metamagics/echoes can add more metamagics/echoes
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int j = 0; j < await character.Metamagics.GetCountAsync(token).ConfigureAwait(false); j++)
            {
                Metamagic objMetamagic = await character.Metamagics.GetValueAtAsync(j, token).ConfigureAwait(false);
                if (objMetamagic.Grade < 0)
                    continue;
                // We're only re-apply improvements a list of items, not all of them
                if (lstInternalIdFilter?.Contains(objMetamagic.InternalId) == false)
                    continue;
                XmlNode objNode = await objMetamagic.GetNodeAsync(token: token).ConfigureAwait(false);
                if (objNode != null)
                {
                    objMetamagic.Bonus = objNode["bonus"];
                    if (objMetamagic.Bonus != null)
                    {
                        await CreateRequiredAsync(character, objMetamagic.SourceType, objMetamagic.InternalId, objMetamagic.Bonus, 1, await objMetamagic.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                    }
                }
                else
                {
                    sbdOutdatedItems.AppendLine(await objMetamagic.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                }
            }

            // Refresh Cyberware and Bioware.
            Dictionary<Cyberware, int> dicPairableCyberwares = new Dictionary<Cyberware, int>(await (await character.GetCyberwareAsync(token).ConfigureAwait(false)).GetCountAsync(token).ConfigureAwait(false));
            foreach (Cyberware objCyberware in await (await character.GetCyberwareAsync(token).ConfigureAwait(false)).GetAllDescendantsAsync((x, t) => x.GetChildrenAsync(t), token).ConfigureAwait(false))
            {
                // We're only re-apply improvements a list of items, not all of them
                if (lstInternalIdFilter?.Contains(objCyberware.InternalId) != false)
                {
                    XmlNode objNode = await objCyberware.GetNodeAsync(token: token).ConfigureAwait(false);
                    if (objNode != null)
                    {
                        objCyberware.Bonus = objNode["bonus"];
                        objCyberware.WirelessBonus = objNode["wirelessbonus"];
                        objCyberware.PairBonus = objNode["pairbonus"];
                        if (!string.IsNullOrEmpty(objCyberware.Forced) && objCyberware.Forced != "Right" && objCyberware.Forced != "Left")
                            ImprovementManager.SetForcedValue(objCyberware.Forced, character);
                        if (objCyberware.Bonus != null)
                        {
                            await CreateRequiredAsync(character, objCyberware.SourceType, objCyberware.InternalId, objCyberware.Bonus, await objCyberware.GetRatingAsync(token).ConfigureAwait(false), await objCyberware.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                            string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                            if (!string.IsNullOrEmpty(strSelectedValue))
                                objCyberware.Extra = strSelectedValue;
                        }

                        if (!await objCyberware.GetIsModularCurrentlyEquippedAsync(token).ConfigureAwait(false))
                            await objCyberware.ChangeModularEquipAsync(false, token: token).ConfigureAwait(false);
                        else
                        {
                            await objCyberware.RefreshWirelessBonusesAsync(token).ConfigureAwait(false);
                            if (objCyberware.PairBonus != null)
                            {
                                Cyberware objMatchingCyberware = dicPairableCyberwares.Keys.FirstOrDefault(x => objCyberware.IncludePair.Contains(x.Name) && x.Extra == objCyberware.Extra);
                                if (objMatchingCyberware != null)
                                    ++dicPairableCyberwares[objMatchingCyberware];
                                else
                                    dicPairableCyberwares.Add(objCyberware, 1);
                            }
                        }

                        Guid guidSourceId = await objCyberware.GetSourceIDAsync(token).ConfigureAwait(false);
                    }
                    else
                    {
                        sbdOutdatedItems.AppendLine(await objCyberware.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                    }
                }

                foreach (Gear objGear in objCyberware.GearChildren)
                {
                    await objGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, token: token).ConfigureAwait(false);
                }
            }

            // Separate Pass for PairBonuses
            foreach (KeyValuePair<Cyberware, int> objItem in dicPairableCyberwares)
            {
                Cyberware objCyberware = objItem.Key;
                int intCyberwaresCount = objItem.Value;
                List<Cyberware> lstPairableCyberwares = await (await character.GetCyberwareAsync(token).ConfigureAwait(false)).DeepWhereAsync((x, t) => x.GetChildrenAsync(t), async (x, t) => objCyberware.IncludePair.Contains(x.Name) && x.Extra == objCyberware.Extra && await x.GetIsModularCurrentlyEquippedAsync(t).ConfigureAwait(false), token).ConfigureAwait(false);
                // Need to use slightly different logic if this cyberware has a location (Left or Right) and only pairs with itself because Lefts can only be paired with Rights and Rights only with Lefts
                if (!string.IsNullOrEmpty(objCyberware.Location) && objCyberware.IncludePair.All(x => x == objCyberware.Name))
                {
                    int intMatchLocationCount = 0;
                    int intNotMatchLocationCount = 0;
                    foreach (Cyberware objPairableCyberware in lstPairableCyberwares)
                    {
                        if (objPairableCyberware.Location != objCyberware.Location)
                            ++intNotMatchLocationCount;
                        else
                            ++intMatchLocationCount;
                    }

                    // Set the count to the total number of cyberwares in matching pairs, which would mean 2x the number of whichever location contains the fewest members (since every single one of theirs would have a pair)
                    intCyberwaresCount = Math.Min(intNotMatchLocationCount, intMatchLocationCount) * 2;
                }

                if (intCyberwaresCount <= 0)
                    continue;
                foreach (Cyberware objLoopCyberware in lstPairableCyberwares)
                {
                    if ((intCyberwaresCount & 1) == 0)
                    {
                        if (!string.IsNullOrEmpty(objCyberware.Forced) && objCyberware.Forced != "Right" && objCyberware.Forced != "Left")
                            ImprovementManager.SetForcedValue(objCyberware.Forced, character);
                        await CreateRequiredAsync(character, objLoopCyberware.SourceType, objLoopCyberware.InternalId + "Pair", objLoopCyberware.PairBonus, await objLoopCyberware.GetRatingAsync(token).ConfigureAwait(false), await objLoopCyberware.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                        string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                        if (!string.IsNullOrEmpty(strSelectedValue) && string.IsNullOrEmpty(objCyberware.Extra))
                            objCyberware.Extra = strSelectedValue;
                    }

                    --intCyberwaresCount;
                    if (intCyberwaresCount <= 0)
                        break;
                }
            }

            // Refresh Armors.
            await character.Armor.ForEachWithSideEffectsAsync(async (objArmor, t1) =>
            {
                // We're only re-apply improvements a list of items, not all of them
                if (lstInternalIdFilter?.Contains(objArmor.InternalId) != false)
                {
                    XmlNode objNode = await objArmor.GetNodeAsync(token: t1).ConfigureAwait(false);
                    if (objNode != null)
                    {
                        objArmor.Bonus = objNode["bonus"];
                        if (objArmor.Bonus != null && objArmor.Equipped)
                        {
                            ImprovementManager.SetForcedValue(objArmor.Extra, character);
                            await CreateRequiredAsync(character, Improvement.ImprovementSource.Armor, objArmor.InternalId, objArmor.Bonus, await objArmor.GetRatingAsync(t1).ConfigureAwait(false), await objArmor.GetCurrentDisplayNameShortAsync(t1).ConfigureAwait(false), token: t1).ConfigureAwait(false);
                            string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                            if (!string.IsNullOrEmpty(strSelectedValue))
                            {
                                objArmor.Extra = strSelectedValue;
                            }
                        }
                    }
                    else
                    {
                        sbdOutdatedItems.AppendLine(await objArmor.GetCurrentDisplayNameAsync(t1).ConfigureAwait(false));
                    }
                }

                await objArmor.ArmorMods.ForEachWithSideEffectsAsync(async (objMod, t2) =>
                {
                    // We're only re-apply improvements a list of items, not all of them
                    if (lstInternalIdFilter?.Contains(objMod.InternalId) != false)
                    {
                        XmlNode objChild = await objMod.GetNodeAsync(token: t2).ConfigureAwait(false);
                        if (objChild != null)
                        {
                            objMod.Bonus = objChild["bonus"];
                            if (objMod.Bonus != null && objMod.Equipped)
                            {
                                ImprovementManager.SetForcedValue(objMod.Extra, character);
                                await CreateRequiredAsync(character, Improvement.ImprovementSource.ArmorMod, objMod.InternalId, objMod.Bonus, await objMod.GetRatingAsync(t2).ConfigureAwait(false), await objMod.GetCurrentDisplayNameShortAsync(t2).ConfigureAwait(false), token: t2).ConfigureAwait(false);
                                string strSelectedValue = ImprovementManager.GetSelectedValue(character);
                                if (!string.IsNullOrEmpty(strSelectedValue))
                                {
                                    objMod.Extra = strSelectedValue;
                                }
                            }
                        }
                        else
                        {
                            sbdOutdatedItems.AppendLine(await objMod.GetCurrentDisplayNameAsync(t2).ConfigureAwait(false));
                        }
                    }

                    await objMod.GearChildren.ForEachWithSideEffectsAsync((objGear, t3) => objGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, token: t3), t2).ConfigureAwait(false);
                }, t1).ConfigureAwait(false);
                await objArmor.GearChildren.ForEachWithSideEffectsAsync((objGear, t2) => objGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, token: t2), t1).ConfigureAwait(false);
                await objArmor.RefreshWirelessBonusesAsync(t1).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            // Refresh Gear.
            await character.Gear.ForEachWithSideEffectsAsync(async (objGear, t) =>
            {
                await objGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, token: t).ConfigureAwait(false);
                await objGear.RefreshWirelessBonusesAsync(t).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            // Refresh Weapons Gear
            await character.Weapons.ForEachWithSideEffectsAsync(async (objWeapon, t1) =>
            {
                await objWeapon.WeaponAccessories.ForEachWithSideEffectsAsync((objAccessory, t2) => objAccessory.GearChildren.ForEachWithSideEffectsAsync((objGear, t3) => objGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, token: t3), t2), t1).ConfigureAwait(false);
                await objWeapon.RefreshWirelessBonusesAsync(t1).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        public static async Task ReapplyGearAsync(this Chummer.Backend.Equipment.Gear objThis, StringBuilder sbdOutdatedItems, IReadOnlyCollection<string> lstInternalIdFilter, Improvement.ImprovementSource eSource = Improvement.ImprovementSource.Gear, bool blnStackEquipped = true, CancellationToken token = default)
        {
            // We're only re-apply improvements a list of items, not all of them
            if (lstInternalIdFilter?.Contains(objThis.InternalId) != false)
            {
                XmlNode objNode = await objThis.GetNodeAsync(token: token).ConfigureAwait(false);
                if (objNode != null)
                {
                    if (objThis.Category == "Stacked Focus")
                    {
                        StackedFocus objStack = await objThis._objCharacter.StackedFoci.FindAsync(x => x.GearId == objThis.InternalId, token).ConfigureAwait(false);
                        if (objStack != null)
                        {
                            await objStack.Gear.ForEachWithSideEffectsAsync((objFociGear, t) => objFociGear.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, Improvement.ImprovementSource.StackedFocus, blnStackEquipped, t), token: token).ConfigureAwait(false);
                        }
                    }

                    objThis.Bonus = objNode["bonus"];
                    objThis.WirelessBonus = objNode["wirelessbonus"];
                    if (blnStackEquipped && objThis.Equipped)
                    {
                        if (objThis.Bonus != null)
                        {
                            ImprovementManager.SetForcedValue(objThis.Extra, objThis._objCharacter);
                            await CreateRequiredAsync(objThis._objCharacter, eSource, objThis.InternalId, objThis.Bonus, await objThis.GetRatingAsync(token).ConfigureAwait(false), await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                            string strSelectedValue = ImprovementManager.GetSelectedValue(objThis._objCharacter);
                            if (!string.IsNullOrEmpty(strSelectedValue))
                            {
                                objThis.Extra = strSelectedValue;
                            }
                        }

                        if (objThis.WirelessOn && objThis.WirelessBonus != null)
                        {
                            ImprovementManager.SetForcedValue(objThis.Extra, objThis._objCharacter);
                            if (await CreateRequiredAsync(objThis._objCharacter, eSource, objThis.InternalId, objThis.WirelessBonus, await objThis.GetRatingAsync(token).ConfigureAwait(false), await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false))
                            {
                                string strSelectedValue = ImprovementManager.GetSelectedValue(objThis._objCharacter);
                                if (!string.IsNullOrEmpty(strSelectedValue))
                                {
                                    objThis.Extra = strSelectedValue;
                                }
                            }
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems?.AppendLine(await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false));
                }
            }

            await objThis.Children.ForEachWithSideEffectsAsync((x, t) => x.ReapplyGearAsync(sbdOutdatedItems, lstInternalIdFilter, eSource, blnStackEquipped, t), token).ConfigureAwait(false);
        }
    }
}
