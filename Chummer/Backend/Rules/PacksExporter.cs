/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public sealed class PacksExportOptions
    {
        public bool Attributes { get; set; } = true;
        public bool Qualities { get; set; } = true;
        public bool StartingNuyen { get; set; } = true;
        public bool MartialArts { get; set; } = true;
        public bool Spells { get; set; } = true;
        public bool ComplexForms { get; set; } = true;
        public bool Cyberware { get; set; } = true;
        public bool Lifestyle { get; set; } = true;
        public bool Armor { get; set; } = true;
        public bool Weapons { get; set; } = true;
        public bool Gear { get; set; } = true;
        public bool Vehicles { get; set; } = true;
    }

    /// <summary>The legacy CreatePACKSKit serializer, independent of the desktop and destination file.</summary>
    public static class PacksExporter
    {
        public static XmlDocument Append(XmlDocument existing, XmlDocument generated)
        {
            if (existing == null || existing.SelectSingleNode("/chummer/packs") == null)
                throw new ArgumentException("The selected file is not a PACKS document.", nameof(existing));
            if (generated?.SelectSingleNode("/chummer/packs/pack") == null)
                throw new ArgumentException("No kit was generated.", nameof(generated));
            var result = (XmlDocument)existing.CloneNode(true);
            var packs = result.SelectSingleNode("/chummer/packs");
            foreach (XmlNode pack in generated.SelectNodes("/chummer/packs/pack"))
            {
                string name = pack["name"]?.InnerText ?? "";
                string category = pack["category"]?.InnerText ?? "";
                if (packs.SelectSingleNode("pack[name=" + name.CleanXPath() + " and category=" + category.CleanXPath() + "]") != null)
                    throw new ArgumentException("A kit with this name and category already exists in the selected file.");
                packs.AppendChild(result.ImportNode(pack, true));
            }
            return result;
        }

        public static async Task<XmlDocument> ExportAsync(Character character, string name, PacksExportOptions options, CancellationToken token = default)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a kit name.", nameof(name));
            if (options == null) throw new ArgumentNullException(nameof(options));
            token.ThrowIfCancellationRequested();
            using (var stream = new MemoryStream())
            {
                using (var writer = Utils.GetStandardXmlWriter(stream))
                {
                    await writer.WriteStartDocumentAsync().ConfigureAwait(false);
                    await writer.WriteStartElementAsync("chummer").ConfigureAwait(false);
                    await writer.WriteStartElementAsync("packs").ConfigureAwait(false);
                    await WriteKitAsync(character, writer, name.Trim(), options, token).ConfigureAwait(false);
                    await writer.WriteEndElementAsync().ConfigureAwait(false);
                    await writer.WriteEndElementAsync().ConfigureAwait(false);
                    await writer.WriteEndDocumentAsync().ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    var document = new XmlDocument { XmlResolver = null };
                    stream.Position = 0;
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false }))
                        document.Load(reader);
                    return document;
                }
            }
        }

        private static async Task WriteKitAsync(Character character, XmlWriter objWriter, string name, PacksExportOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // <pack>
            await objWriter.WriteStartElementAsync("pack").ConfigureAwait(false);
            // <name />
            await objWriter.WriteElementStringAsync("name", name).ConfigureAwait(false);
            // <category />
            await objWriter.WriteElementStringAsync("category", "Custom").ConfigureAwait(false);

            // Export Attributes.
            if (options.Attributes)
            {
                int intBOD = await character.BOD.GetValueAsync().ConfigureAwait(false) -
                             (await character.BOD.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intAGI = await character.AGI.GetValueAsync().ConfigureAwait(false) -
                             (await character.AGI.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intREA = await character.REA.GetValueAsync().ConfigureAwait(false) -
                             (await character.REA.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intSTR = await character.STR.GetValueAsync().ConfigureAwait(false) -
                             (await character.STR.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intCHA = await character.CHA.GetValueAsync().ConfigureAwait(false) -
                             (await character.CHA.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intINT = await character.INT.GetValueAsync().ConfigureAwait(false) -
                             (await character.INT.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intLOG = await character.LOG.GetValueAsync().ConfigureAwait(false) -
                             (await character.LOG.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intWIL = await character.WIL.GetValueAsync().ConfigureAwait(false) -
                             (await character.WIL.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intEDG = await character.EDG.GetValueAsync().ConfigureAwait(false) -
                             (await character.EDG.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intMAG = await character.MAG.GetValueAsync().ConfigureAwait(false) -
                             (await character.MAG.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intMAGAdept = await character.MAGAdept.GetValueAsync().ConfigureAwait(false) -
                                  (await character.MAGAdept.GetMetatypeMinimumAsync()
                                      .ConfigureAwait(false) - 1);
                int intDEP = await character.DEP.GetValueAsync().ConfigureAwait(false) -
                             (await character.DEP.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                int intRES = await character.RES.GetValueAsync().ConfigureAwait(false) -
                             (await character.RES.GetMetatypeMinimumAsync().ConfigureAwait(false) - 1);
                // <attributes>
                await objWriter.WriteStartElementAsync("attributes").ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("bod", intBOD.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("agi", intAGI.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("rea", intREA.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("str", intSTR.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("cha", intCHA.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("int", intINT.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("log", intLOG.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("wil", intWIL.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                await objWriter
                    .WriteElementStringAsync("edg", intEDG.ToString(GlobalSettings.InvariantCultureInfo))
                    .ConfigureAwait(false);
                if (await character.GetMAGEnabledAsync().ConfigureAwait(false))
                {
                    await objWriter
                        .WriteElementStringAsync("mag", intMAG.ToString(GlobalSettings.InvariantCultureInfo))
                        .ConfigureAwait(false);
                    if (await (await character.GetSettingsAsync().ConfigureAwait(false)).GetMysAdeptSecondMAGAttributeAsync()
                            .ConfigureAwait(false) &&
                        await character.GetIsMysticAdeptAsync().ConfigureAwait(false))
                        await objWriter.WriteElementStringAsync("magadept",
                            intMAGAdept.ToString(GlobalSettings.InvariantCultureInfo)).ConfigureAwait(false);
                }

                if (await character.GetRESEnabledAsync().ConfigureAwait(false))
                    await objWriter
                        .WriteElementStringAsync("res", intRES.ToString(GlobalSettings.InvariantCultureInfo))
                        .ConfigureAwait(false);
                if (await character.GetDEPEnabledAsync().ConfigureAwait(false))
                    await objWriter
                        .WriteElementStringAsync("dep", intDEP.ToString(GlobalSettings.InvariantCultureInfo))
                        .ConfigureAwait(false);
                // </attributes>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Qualities.
            if (options.Qualities)
            {
                bool blnPositive = false;
                bool blnNegative = false;
                // Determine if Positive or Negative Qualities exist.
                await character.Qualities.ForEachWithBreakAsync(objQuality =>
                {
                    switch (objQuality.Type)
                    {
                        case QualityType.Positive:
                            blnPositive = true;
                            break;

                        case QualityType.Negative:
                            blnNegative = true;
                            break;
                    }

                    return !blnPositive || !blnNegative;
                }).ConfigureAwait(false);

                // <qualities>
                await objWriter.WriteStartElementAsync("qualities").ConfigureAwait(false);

                // Positive Qualities.
                if (blnPositive)
                {
                    // <positive>
                    await objWriter.WriteStartElementAsync("positive").ConfigureAwait(false);
                    await character.Qualities.ForEachAsync(async objQuality =>
                    {
                        if (objQuality.Type == QualityType.Positive)
                        {
                            // ReSharper disable AccessToDisposedClosure
                            await objWriter.WriteStartElementAsync("quality").ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(objQuality.Extra))
                                await objWriter.WriteAttributeStringAsync("select", objQuality.Extra)
                                    .ConfigureAwait(false);
                            objWriter.WriteValue(await objQuality.GetNameAsync().ConfigureAwait(false));
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                            // ReSharper restore AccessToDisposedClosure
                        }
                    }).ConfigureAwait(false);

                    // </positive>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }

                // Negative Qualities.
                if (blnPositive)
                {
                    // <negative>
                    await objWriter.WriteStartElementAsync("negative").ConfigureAwait(false);
                    await character.Qualities.ForEachAsync(async objQuality =>
                    {
                        if (objQuality.Type == QualityType.Negative)
                        {
                            // ReSharper disable AccessToDisposedClosure
                            await objWriter.WriteStartElementAsync("quality").ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(objQuality.Extra))
                                await objWriter.WriteAttributeStringAsync("select", objQuality.Extra)
                                    .ConfigureAwait(false);
                            objWriter.WriteValue(objQuality.Name);
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                            // ReSharper restore AccessToDisposedClosure
                        }
                    }).ConfigureAwait(false);

                    // </negative>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }

                // </qualities>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Starting Nuyen.
            if (options.StartingNuyen)
            {
                decimal decNuyenBP = await character.GetNuyenBPAsync().ConfigureAwait(false);
                if (!await character.GetEffectiveBuildMethodUsesPriorityTablesAsync().ConfigureAwait(false))
                    decNuyenBP /= 2.0m;
                await objWriter
                    .WriteElementStringAsync("nuyenbp",
                        decNuyenBP.ToString(GlobalSettings.InvariantCultureInfo)).ConfigureAwait(false);
            }

            /* TODO: Add support for active and knowledge skills and skill groups
            // Export Active Skills.
            if (options.ActiveSkills)
            {
                // <skills>
                await objWriter.WriteStartElementAsync("skills");

                // Active Skills.
                foreach (Skill objSkill in character.SkillsSection.Skills)
                {
                    if (!objSkill.IsKnowledgeSkill && objSkill.Rating > 0)
                    {
                        // <skill>
                        await objWriter.WriteStartElementAsync("skill");
                        await objWriter.WriteElementStringAsync("name", objSkill.Name);
                        await objWriter.WriteElementStringAsync("rating", objSkill.Rating.ToString());
                        if (!string.IsNullOrEmpty(objSkill.Specialization))
                            await objWriter.WriteElementStringAsync("spec", objSkill.Specialization);
                        // </skill>
                        await objWriter.WriteEndElementAsync();
                    }
                }

                // Skill Groups.
                foreach (SkillGroup objSkillGroup in character.SkillsSection.SkillGroups)
                {
                    if (objSkillGroup.BaseUnbroken && objSkillGroup.Rating > 0)
                    {
                        // <skillgroup>
                        await objWriter.WriteStartElementAsync("skillgroup");
                        await objWriter.WriteElementStringAsync("name", objSkillGroup.Name);
                        await objWriter.WriteElementStringAsync("rating", objSkillGroup.Rating.ToString());
                        // </skillgroup>
                        await objWriter.WriteEndElementAsync();
                    }
                }
                // </skills>
                await objWriter.WriteEndElementAsync();
            }

            // Export Knowledge Skills.
            if (options.KnowledgeSkills)
            {
                // <knowledgeskills>
                await objWriter.WriteStartElementAsync("knowledgeskills");
                foreach (KnowledgeSkill objSkill in character.SkillsSection.Skills.OfType<KnowledgeSkill>())
                {
                    // <skill>
                    await objWriter.WriteStartElementAsync("skill");
                    await objWriter.WriteElementStringAsync("name", objSkill.Name);
                    await objWriter.WriteElementStringAsync("rating", objSkill.Rating.ToString(GlobalSettings.InvariantCultureInfo));
                    if (!string.IsNullOrEmpty(objSkill.Specialization))
                        await objWriter.WriteElementStringAsync("spec", objSkill.Specialization);
                    await objWriter.WriteElementStringAsync("category", objSkill.SkillCategory);
                    // </skill>
                    await objWriter.WriteEndElementAsync();
                }

                // </knowledgeskills>
                await objWriter.WriteEndElementAsync();
            }
            */

            // Export Martial Arts.
            if (options.MartialArts)
            {
                // <martialarts>
                await objWriter.WriteStartElementAsync("martialarts").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.MartialArts.ForEachAsync(async objArt =>
                {
                    // <martialart>
                    await objWriter.WriteStartElementAsync("martialart").ConfigureAwait(false);
                    await objWriter.WriteElementStringAsync("name", objArt.Name).ConfigureAwait(false);
                    if (await objArt.Techniques.GetCountAsync().ConfigureAwait(false) > 0)
                    {
                        // <techniques>
                        await objWriter.WriteStartElementAsync("techniques").ConfigureAwait(false);
                        await objArt.Techniques
                            .ForEachAsync(objTechnique =>
                                objWriter.WriteElementStringAsync(
                                    "technique", objTechnique.Name))
                            .ConfigureAwait(false);
                        // </techniques>
                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    }

                    // </martialart>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure
                // </martialarts>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Spells.
            if (options.Spells)
            {
                // <spells>
                await objWriter.WriteStartElementAsync("spells").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.Spells.ForEachAsync(async objSpell =>
                {
                    await objWriter.WriteStartElementAsync("spell").ConfigureAwait(false);
                    await objWriter.WriteStartElementAsync("name").ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(objSpell.Extra))
                        await objWriter.WriteAttributeStringAsync("select", objSpell.Extra)
                            .ConfigureAwait(false);
                    objWriter.WriteValue(objSpell.Name);
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    await objWriter.WriteElementStringAsync("category", objSpell.Category)
                        .ConfigureAwait(false);
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </spells>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Complex Forms.
            if (options.ComplexForms)
            {
                // <programs>
                await objWriter.WriteStartElementAsync("complexforms").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.ComplexForms.ForEachAsync(async objComplexForm =>
                {
                    // <program>
                    await objWriter.WriteStartElementAsync("complexform").ConfigureAwait(false);
                    await objWriter.WriteStartElementAsync("name").ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(objComplexForm.Extra))
                        await objWriter.WriteAttributeStringAsync("select", objComplexForm.Extra)
                            .ConfigureAwait(false);
                    objWriter.WriteValue(objComplexForm.Name);
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    // </program>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </programs>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Cyberware/Bioware.
            if (options.Cyberware)
            {
                bool blnCyberware = false;
                bool blnBioware = false;
                await character.Cyberware.ForEachWithBreakAsync(objCharacterCyberware =>
                {
                    switch (objCharacterCyberware.SourceType)
                    {
                        case Improvement.ImprovementSource.Bioware:
                            blnBioware = true;
                            return blnCyberware;

                        case Improvement.ImprovementSource.Cyberware:
                            blnCyberware = true;
                            return blnBioware;
                    }

                    return true;
                }).ConfigureAwait(false);

                if (blnCyberware)
                {
                    // <cyberwares>
                    await objWriter.WriteStartElementAsync("cyberwares").ConfigureAwait(false);
                    // ReSharper disable AccessToDisposedClosure
                    await character.Cyberware.ForEachAsync(async objCyberware =>
                    {
                        if (objCyberware.SourceType == Improvement.ImprovementSource.Cyberware)
                        {
                            // <cyberware>
                            await objWriter.WriteStartElementAsync("cyberware").ConfigureAwait(false);
                            await objWriter.WriteElementStringAsync("name", objCyberware.Name)
                                .ConfigureAwait(false);
                            int intRating = await objCyberware.GetRatingAsync()
                                .ConfigureAwait(false);
                            if (intRating > 0)
                                await objWriter
                                    .WriteElementStringAsync(
                                        "rating",
                                        intRating.ToString(GlobalSettings.InvariantCultureInfo))
                                    .ConfigureAwait(false);
                            await objWriter.WriteElementStringAsync("grade",
                                    (await objCyberware.GetGradeAsync().ConfigureAwait(false)).Name)
                                .ConfigureAwait(false);
                            if (await (await objCyberware.GetChildrenAsync().ConfigureAwait(false)).GetCountAsync().ConfigureAwait(false) > 0)
                            {
                                // <cyberwares>
                                await objWriter.WriteStartElementAsync("cyberwares").ConfigureAwait(false);
                                await (await objCyberware.GetChildrenAsync().ConfigureAwait(false)).ForEachAsync(async objChildCyberware =>
                                {
                                    if (objChildCyberware.Capacity != "[*]")
                                    {
                                        // <cyberware/bioware>
                                        await objWriter
                                            .WriteStartElementAsync(
                                                objChildCyberware.SourceType
                                                == Improvement.ImprovementSource.Cyberware
                                                    ? "cyberware"
                                                    : "bioware")
                                            .ConfigureAwait(false);
                                        await objWriter.WriteElementStringAsync("name", objChildCyberware.Name)
                                            .ConfigureAwait(false);
                                        int intChildRating = await objChildCyberware.GetRatingAsync()
                                            .ConfigureAwait(false);
                                        if (intChildRating > 0)
                                            await objWriter
                                                .WriteElementStringAsync(
                                                    "rating",
                                                    intChildRating.ToString(
                                                        GlobalSettings.InvariantCultureInfo))
                                                .ConfigureAwait(false);

                                        if (await objChildCyberware.GearChildren.GetCountAsync()
                                                .ConfigureAwait(false) > 0)
                                            await WriteGear(objWriter, objChildCyberware.GearChildren)
                                                .ConfigureAwait(false);
                                        // </cyberware>
                                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                                    }
                                }).ConfigureAwait(false);

                                // </cyberwares>
                                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                            }

                            if (await objCyberware.GearChildren.GetCountAsync().ConfigureAwait(false) > 0)
                                await WriteGear(objWriter, objCyberware.GearChildren).ConfigureAwait(false);

                            // </cyberware>
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                    // ReSharper restore AccessToDisposedClosure

                    // </cyberwares>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }

                if (blnBioware)
                {
                    // <biowares>
                    await objWriter.WriteStartElementAsync("biowares").ConfigureAwait(false);
                    // ReSharper disable AccessToDisposedClosure
                    await character.Cyberware.ForEachAsync(async objCyberware =>
                    {
                        if (objCyberware.SourceType == Improvement.ImprovementSource.Bioware)
                        {
                            // <bioware>
                            await objWriter.WriteStartElementAsync("bioware").ConfigureAwait(false);
                            await objWriter.WriteElementStringAsync("name", objCyberware.Name)
                                .ConfigureAwait(false);
                            int intRating = await objCyberware.GetRatingAsync()
                                .ConfigureAwait(false);
                            if (intRating > 0)
                                await objWriter
                                    .WriteElementStringAsync(
                                        "rating",
                                        intRating.ToString(GlobalSettings.InvariantCultureInfo))
                                    .ConfigureAwait(false);
                            await objWriter.WriteElementStringAsync("grade",
                                    (await objCyberware.GetGradeAsync().ConfigureAwait(false)).Name)
                                .ConfigureAwait(false);
                            if (await (await objCyberware.GetChildrenAsync().ConfigureAwait(false)).GetCountAsync().ConfigureAwait(false) > 0)
                            {
                                // <cyberwares>
                                await objWriter.WriteStartElementAsync("cyberwares").ConfigureAwait(false);
                                await (await objCyberware.GetChildrenAsync().ConfigureAwait(false)).ForEachAsync(async objChildCyberware =>
                                {
                                    if (objChildCyberware.Capacity != "[*]")
                                    {
                                        // <cyberware>
                                        // <cyberware/bioware>
                                        await objWriter
                                            .WriteStartElementAsync(
                                                objChildCyberware.SourceType
                                                == Improvement.ImprovementSource.Cyberware
                                                    ? "cyberware"
                                                    : "bioware")
                                            .ConfigureAwait(false);
                                        await objWriter.WriteElementStringAsync("name", objChildCyberware.Name)
                                            .ConfigureAwait(false);
                                        int intChildRating = await objChildCyberware.GetRatingAsync()
                                            .ConfigureAwait(false);
                                        if (intChildRating > 0)
                                            await objWriter
                                                .WriteElementStringAsync(
                                                    "rating",
                                                    intChildRating.ToString(
                                                        GlobalSettings.InvariantCultureInfo))
                                                .ConfigureAwait(false);

                                        if (await objChildCyberware.GearChildren.GetCountAsync()
                                                .ConfigureAwait(false) > 0)
                                            await WriteGear(objWriter, objChildCyberware.GearChildren)
                                                .ConfigureAwait(false);
                                        // </cyberware>
                                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                                    }
                                }).ConfigureAwait(false);

                                // </cyberwares>
                                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                            }

                            if (await objCyberware.GearChildren.GetCountAsync().ConfigureAwait(false) > 0)
                                await WriteGear(objWriter, objCyberware.GearChildren).ConfigureAwait(false);

                            // </bioware>
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);
                    // ReSharper restore AccessToDisposedClosure

                    // </biowares>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }
            }

            // Export Lifestyle.
            if (options.Lifestyle)
            {
                // <lifestyles>
                await objWriter.WriteStartElementAsync("lifestyles").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.Lifestyles.ForEachAsync(async objLifestyle =>
                {
                    // <lifestyle>
                    await objWriter.WriteStartElementAsync("lifestyle").ConfigureAwait(false);
                    await objWriter.WriteElementStringAsync("name", await objLifestyle.GetNameAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    await objWriter
                        .WriteElementStringAsync(
                            "months",
                            (await objLifestyle.GetIncrementsAsync().ConfigureAwait(false)).ToString(
                                GlobalSettings.InvariantCultureInfo))
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(await objLifestyle.GetBaseLifestyleAsync().ConfigureAwait(false)))
                    {
                        // This is an Advanced Lifestyle, so write out its properties.
                        await objWriter
                            .WriteElementStringAsync(
                                "cost",
                                (await objLifestyle.GetCostAsync().ConfigureAwait(false)).ToString(GlobalSettings.InvariantCultureInfo)).ConfigureAwait(false);
                        await objWriter
                            .WriteElementStringAsync(
                                "dice", (await objLifestyle.GetDiceAsync().ConfigureAwait(false)).ToString(GlobalSettings.InvariantCultureInfo))
                            .ConfigureAwait(false);
                        await objWriter
                            .WriteElementStringAsync("multiplier",
                                (await objLifestyle.GetMultiplierAsync().ConfigureAwait(false)).ToString(GlobalSettings.InvariantCultureInfo)).ConfigureAwait(false);
                        await objWriter.WriteElementStringAsync("baselifestyle", await objLifestyle.GetBaseLifestyleAsync().ConfigureAwait(false))
                            .ConfigureAwait(false);
                        if (await objLifestyle.LifestyleQualities.GetCountAsync().ConfigureAwait(false) > 0)
                        {
                            // <qualities>
                            await objWriter.WriteStartElementAsync("qualities").ConfigureAwait(false);
                            await objLifestyle.LifestyleQualities.ForEachAsync(async objQuality =>
                                await objWriter.WriteElementStringAsync("quality", await objQuality.GetNameAsync().ConfigureAwait(false)).ConfigureAwait(false))
                                    .ConfigureAwait(false);
                            // </qualities>
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                        }
                    }

                    // </lifestyle>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </lifestyles>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Armor.
            if (options.Armor)
            {
                // <armors>
                await objWriter.WriteStartElementAsync("armors").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.Armor.ForEachAsync(async objArmor =>
                {
                    // <armor>
                    await objWriter.WriteStartElementAsync("armor").ConfigureAwait(false);
                    await objWriter.WriteElementStringAsync("name", objArmor.Name).ConfigureAwait(false);
                    if (await objArmor.ArmorMods.GetCountAsync().ConfigureAwait(false) > 0)
                    {
                        // <mods>
                        await objWriter.WriteStartElementAsync("mods").ConfigureAwait(false);
                        await objArmor.ArmorMods.ForEachAsync(async objMod =>
                        {
                            // <mod>
                            await objWriter.WriteStartElementAsync("mod").ConfigureAwait(false);
                            await objWriter.WriteElementStringAsync("name", objMod.Name).ConfigureAwait(false);
                            int intRating = await objMod.GetRatingAsync().ConfigureAwait(false);
                            if (intRating > 0)
                                await objWriter
                                    .WriteElementStringAsync(
                                        "rating", intRating.ToString(GlobalSettings.InvariantCultureInfo))
                                    .ConfigureAwait(false);
                            // </mod>
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                        }).ConfigureAwait(false);

                        // </mods>
                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    }

                    if (await objArmor.GearChildren.GetCountAsync().ConfigureAwait(false) > 0)
                        await WriteGear(objWriter, objArmor.GearChildren).ConfigureAwait(false);

                    // </armor>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </armors>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Weapons.
            if (options.Weapons)
            {
                // <weapons>
                await objWriter.WriteStartElementAsync("weapons").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.Weapons.ForEachAsync(async objWeapon =>
                {
                    // Don't attempt to export Cyberware and Gear Weapons since those are handled by those object types. The default Unarmed Attack Weapon should also not be exported.
                    if (objWeapon.Category != "Cyberware" && objWeapon.Category != "Gear"
                                                          && objWeapon.Name != "Unarmed Attack")
                    {
                        // <weapon>
                        await objWriter.WriteStartElementAsync("weapon").ConfigureAwait(false);
                        await objWriter.WriteElementStringAsync("name", objWeapon.Name).ConfigureAwait(false);

                        // Weapon Accessories.
                        if (await objWeapon.WeaponAccessories.GetCountAsync().ConfigureAwait(false) > 0)
                        {
                            // <accessories>
                            await objWriter.WriteStartElementAsync("accessories").ConfigureAwait(false);
                            await objWeapon.WeaponAccessories.ForEachAsync(async objAccessory =>
                            {
                                // Don't attempt to export items included in the Weapon.
                                if (!objAccessory.IncludedInWeapon)
                                {
                                    // <accessory>
                                    await objWriter.WriteStartElementAsync("accessory").ConfigureAwait(false);
                                    await objWriter.WriteElementStringAsync("name", objAccessory.Name)
                                        .ConfigureAwait(false);
                                    await objWriter.WriteElementStringAsync("mount", objAccessory.Mount)
                                        .ConfigureAwait(false);
                                    await objWriter
                                        .WriteElementStringAsync("extramount", objAccessory.ExtraMount)
                                        .ConfigureAwait(false);

                                    if (await objAccessory.GearChildren.GetCountAsync().ConfigureAwait(false) >
                                        0)
                                        await WriteGear(objWriter, objAccessory.GearChildren)
                                            .ConfigureAwait(false);

                                    // </accessory>
                                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                                }
                            }).ConfigureAwait(false);

                            // </accessories>
                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                        }

                        // Underbarrel Weapon.
                        if (await objWeapon.UnderbarrelWeapons.GetCountAsync().ConfigureAwait(false) > 0)
                        {
                            await objWeapon.UnderbarrelWeapons.ForEachAsync(objUnderbarrelWeapon =>
                                !objUnderbarrelWeapon.IncludedInWeapon
                                    ? objWriter
                                        .WriteElementStringAsync("underbarrel", objUnderbarrelWeapon.Name)
                                    : Task.CompletedTask).ConfigureAwait(false);
                        }

                        // </weapon>
                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </weapons>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // Export Gear.
            if (options.Gear)
            {
                await WriteGear(objWriter, character.Gear).ConfigureAwait(false);
            }

            // Export Vehicles.
            if (options.Vehicles)
            {
                // <vehicles>
                await objWriter.WriteStartElementAsync("vehicles").ConfigureAwait(false);
                // ReSharper disable AccessToDisposedClosure
                await character.Vehicles.ForEachAsync(async objVehicle =>
                {
                    bool blnWeapons = false;
                    // <vehicle>
                    await objWriter.WriteStartElementAsync("vehicle").ConfigureAwait(false);
                    await objWriter.WriteElementStringAsync("name", objVehicle.Name).ConfigureAwait(false);
                    if (await objVehicle.Mods.GetCountAsync().ConfigureAwait(false) > 0)
                    {
                        // <mods>
                        await objWriter.WriteStartElementAsync("mods").ConfigureAwait(false);
                        await objVehicle.Mods.ForEachAsync(async objVehicleMod =>
                        {
                            // Only write out the Mods that are not part of the base vehicle.
                            if (!objVehicleMod.IncludedInVehicle)
                            {
                                // <mod>
                                await objWriter.WriteStartElementAsync("mod").ConfigureAwait(false);
                                await objWriter.WriteElementStringAsync("name", objVehicleMod.Name)
                                    .ConfigureAwait(false);
                                int intRating = await objVehicleMod.GetRatingAsync().ConfigureAwait(false);
                                if (intRating > 0)
                                    await objWriter
                                        .WriteElementStringAsync(
                                            "rating",
                                            intRating.ToString(
                                                GlobalSettings.InvariantCultureInfo)).ConfigureAwait(false);
                                // </mod>
                                await objWriter.WriteEndElementAsync().ConfigureAwait(false);

                                // See if this is a Weapon Mount with Weapons.
                                if (await objVehicleMod.Weapons.GetCountAsync().ConfigureAwait(false) > 0)
                                    blnWeapons = true;
                            }
                            else
                            {
                                // See if this is a Weapon Mount with Weapons.
                                if (await objVehicleMod.Weapons.GetCountAsync().ConfigureAwait(false) > 0)
                                    blnWeapons = true;
                            }
                        }).ConfigureAwait(false);

                        // </mods>
                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    }

                    // If there are Weapons, add them.
                    if (blnWeapons)
                    {
                        // <weapons>
                        await objWriter.WriteStartElementAsync("weapons").ConfigureAwait(false);
                        await objVehicle.Mods.ForEachAsync(objVehicleMod =>
                        {
                            return objVehicleMod.Weapons.ForEachAsync(async objWeapon =>
                            {
                                // <weapon>
                                await objWriter.WriteStartElementAsync("weapon").ConfigureAwait(false);
                                await objWriter.WriteElementStringAsync("name", objWeapon.Name)
                                    .ConfigureAwait(false);

                                // Weapon Accessories.
                                if (await objWeapon.WeaponAccessories.GetCountAsync().ConfigureAwait(false) > 0)
                                {
                                    // <accessories>
                                    await objWriter.WriteStartElementAsync("accessories").ConfigureAwait(false);
                                    await objWeapon.WeaponAccessories.ForEachAsync(async objAccessory =>
                                    {
                                        // Don't attempt to export items included in the Weapon.
                                        if (!objAccessory.IncludedInWeapon)
                                        {
                                            // <accessory>
                                            await objWriter.WriteStartElementAsync("accessory")
                                                .ConfigureAwait(false);
                                            await objWriter.WriteElementStringAsync("name", objAccessory.Name)
                                                .ConfigureAwait(false);
                                            await objWriter.WriteElementStringAsync("mount", objAccessory.Mount)
                                                .ConfigureAwait(false);
                                            await objWriter
                                                .WriteElementStringAsync(
                                                    "extramount", objAccessory.ExtraMount)
                                                .ConfigureAwait(false);
                                            // </accessory>
                                            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                                        }
                                    }).ConfigureAwait(false);

                                    // </accessories>
                                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                                }

                                // Underbarrel Weapon.
                                if (await objWeapon.UnderbarrelWeapons.GetCountAsync().ConfigureAwait(false) >
                                    0)
                                {
                                    await objWeapon.UnderbarrelWeapons.ForEachAsync(objUnderbarrelWeapon =>
                                            objWriter
                                                .WriteElementStringAsync(
                                                    "underbarrel", objUnderbarrelWeapon.Name))
                                        .ConfigureAwait(false);
                                }

                                // </weapon>
                                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                            });
                        }).ConfigureAwait(false);

                        // </weapons>
                        await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                    }

                    // Gear.
                    if (await objVehicle.GearChildren.GetCountAsync().ConfigureAwait(false) > 0)
                    {
                        await WriteGear(objWriter, objVehicle.GearChildren).ConfigureAwait(false);
                    }

                    // </vehicle>
                    await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                // ReSharper restore AccessToDisposedClosure

                // </vehicles>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }

            // </pack>
            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
        }

        private static async Task WriteGear(XmlWriter objWriter, IEnumerable<Gear> lstGear, CancellationToken token = default)
        {
            // <gears>
            await objWriter.WriteStartElementAsync("gears", token: token).ConfigureAwait(false);
            foreach (Gear objGear in lstGear)
            {
                if (objGear.IncludedInParent)
                    continue;
                // <gear>
                await objWriter.WriteStartElementAsync("gear", token: token).ConfigureAwait(false);
                await objWriter.WriteStartElementAsync("name", token: token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(objGear.Extra))
                    await objWriter.WriteAttributeStringAsync("select", objGear.Extra, token: token).ConfigureAwait(false);
                objWriter.WriteValue(objGear.Name);
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
                await objWriter.WriteElementStringAsync("category", objGear.Category, token: token).ConfigureAwait(false);
                int intRating = await objGear.GetRatingAsync(token).ConfigureAwait(false);
                if (intRating > 0)
                    await objWriter.WriteElementStringAsync("rating", intRating.ToString(GlobalSettings.InvariantCultureInfo), token: token).ConfigureAwait(false);
                if (objGear.Quantity != 1)
                    await objWriter.WriteElementStringAsync("qty", objGear.Quantity.ToString(GlobalSettings.InvariantCultureInfo), token: token).ConfigureAwait(false);
                if (await objGear.Children.GetCountAsync(token).ConfigureAwait(false) > 0)
                    await WriteGear(objWriter, objGear.Children, token).ConfigureAwait(false);
                // </gear>
                await objWriter.WriteEndElementAsync().ConfigureAwait(false);
            }
            // </gears>
            await objWriter.WriteEndElementAsync().ConfigureAwait(false);
        }

    }
}
