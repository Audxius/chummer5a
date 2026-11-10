/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public static class PacksRules
    {
        /// <summary>Legacy CharacterCreate.AddPACKSKit and its nested equipment helpers, without form/progress code.</summary>
        public static async Task<RuleResult> AddAsync(Character character, string kit, string category, CancellationToken token = default, IProgress<string> progress = null)
        {
            progress?.Report("Loading kit: " + kit);
            if (await character.GetCreatedAsync(token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            XmlNode node = (await character.LoadDataAsync("packs.xml", token: token).ConfigureAwait(false))
                .TryGetNodeByNameOrId("/chummer/packs/pack", kit, "category = " + category.CleanXPath());
            if (node == null || node["hide"] != null) return RuleResult.NotAllowed;
            return await new Application(character, progress).ApplyAsync(node, token).ConfigureAwait(false);
        }

        private sealed class Application
        {
            // Each engine mutation manages its own lock. A lock spanning the entire
            // kit can block native bindings while an improvement asks the UI a question.
            private readonly Character CharacterObject;
            private readonly IProgress<string> _progress;
            private CharacterSettings CharacterObjectSettings => CharacterObject.Settings;
            public Application(Character character, IProgress<string> progress) { CharacterObject = character; _progress = progress; }
            public async Task<RuleResult> ApplyAsync(XmlNode objXmlKit, CancellationToken token)
            {
                const bool blnCreateChildren = true;
                {
                    token.ThrowIfCancellationRequested();
                    // Update Qualities.
                    _progress?.Report("Adding Qualities");
                    XmlElement xmlQualities = objXmlKit["qualities"];
                    if (xmlQualities != null)
                    {
                        XmlDocument xmlQualityDocument = await CharacterObject.LoadDataAsync("qualities.xml", token: token)
                            .ConfigureAwait(false);

                        // Positive and Negative Qualities.
                        using (XmlNodeList xmlQualityList = xmlQualities.SelectNodes("*/quality"))
                        {
                            if (xmlQualityList?.Count > 0)
                            {
                                foreach (XmlNode objXmlQuality in xmlQualityList)
                                {
                                    XmlNode objXmlQualityNode = xmlQualityDocument.TryGetNodeByNameOrId(
                                        "/chummer/qualities/quality", objXmlQuality.InnerTextViaPool(token),
                                        await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));

                                    if (objXmlQualityNode == null)
                                        continue;
                                    string strForceValue =
                                            objXmlQuality.Attributes?["select"]?.InnerTextViaPool(token) ?? string.Empty;
                                    List<Weapon> lstWeapons = new List<Weapon>(1);
                                    Quality objQuality = new Quality(CharacterObject);
                                    try
                                    {
                                        await objQuality.CreateAsync(objXmlQualityNode, QualitySource.Selected, lstWeapons,
                                            strForceValue, token: token).ConfigureAwait(false);

                                        await CharacterObject.Qualities.AddAsync(objQuality, token).ConfigureAwait(false);

                                        // Add any created Weapons to the character.
                                        foreach (Weapon objWeapon in lstWeapons)
                                        {
                                            await CharacterObject.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                                        }
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

                    //TODO: PACKS SKILLS?

                    // Select a Martial Art.
                    XmlElement xmlSelectMartialArt = objXmlKit["selectmartialart"];
                    if (xmlSelectMartialArt != null)
                    {
                        string strForcedValue = xmlSelectMartialArt.Attributes["select"]?.InnerTextViaPool(token) ?? string.Empty;

                        using (ThreadSafeDialog<SelectMartialArtRequest> frmPickMartialArt = await ThreadSafeDialog<SelectMartialArtRequest>
                                   .GetAsync(() => new SelectMartialArtRequest(CharacterObject)
                                   {
                                       ForcedValue = strForcedValue
                                   }, token).ConfigureAwait(false))
                        {
                            if (await frmPickMartialArt.ShowDialogSafeAsync(HostServices.MainWindow, token).ConfigureAwait(false)
                                != DialogOutcome.Cancel)
                            {
                                // Open the Martial Arts XML file and locate the selected piece.
                                XmlDocument objXmlMartialArtDocument = await CharacterObject
                                    .LoadDataAsync("martialarts.xml", token: token)
                                    .ConfigureAwait(false);

                                XmlNode objXmlArt = objXmlMartialArtDocument.TryGetNodeByNameOrId(
                                    "/chummer/martialarts/martialart", frmPickMartialArt.MyForm.SelectedMartialArt);

                                MartialArt objMartialArt = new MartialArt(CharacterObject);
                                try
                                {
                                    await objMartialArt.CreateAsync(objXmlArt, token).ConfigureAwait(false);
                                    await CharacterObject.MartialArts.AddAsync(objMartialArt, token).ConfigureAwait(false);
                                }
                                catch
                                {
                                    await objMartialArt.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                    throw;
                                }
                            }
                        }
                    }

                    // Update Martial Arts.
                    _progress?.Report("Adding Martial Arts");
                    XmlElement xmlMartialArts = objXmlKit["martialarts"];
                    if (xmlMartialArts != null)
                    {
                        // Open the Martial Arts XML file and locate the selected art.
                        XmlDocument objXmlMartialArtDocument
                            = await CharacterObject.LoadDataAsync("martialarts.xml", token: token).ConfigureAwait(false);

                        using (XmlNodeList xmlMartialArtsList = xmlMartialArts.SelectNodes("martialart"))
                        {
                            if (xmlMartialArtsList?.Count > 0)
                            {
                                foreach (XmlNode objXmlArt in xmlMartialArtsList)
                                {
                                    XmlNode objXmlArtNode = objXmlMartialArtDocument.TryGetNodeByNameOrId(
                                            "/chummer/martialarts/martialart", objXmlArt["name"]?.InnerTextViaPool(token),
                                            await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                    if (objXmlArtNode == null)
                                        continue;
                                    MartialArt objArt = new MartialArt(CharacterObject);
                                    try
                                    {
                                        await objArt.CreateAsync(objXmlArtNode, token).ConfigureAwait(false);
                                        await CharacterObject.MartialArts.AddAsync(objArt, token).ConfigureAwait(false);

                                        // Check for Techniques.
                                        using (XmlNodeList xmlTechniquesList = objXmlArt.SelectNodes("techniques/technique"))
                                        {
                                            if (xmlTechniquesList?.Count > 0)
                                            {
                                                foreach (XmlNode xmlTechnique in xmlTechniquesList)
                                                {
                                                    MartialArtTechnique objTechnique = new MartialArtTechnique(CharacterObject);
                                                    XmlNode xmlTechniqueNode = objXmlMartialArtDocument.TryGetNodeByNameOrId(
                                                        "/chummer/techniques/technique", xmlTechnique["name"]?.InnerTextViaPool(token),
                                                        await CharacterObjectSettings.BookXPathAsync(token: token)
                                                            .ConfigureAwait(false));
                                                    await objTechnique.CreateAsync(xmlTechniqueNode, token)
                                                        .ConfigureAwait(false);
                                                    await objArt.Techniques.AddAsync(objTechnique, token).ConfigureAwait(false);
                                                }
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        await objArt.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                        throw;
                                    }
                                }
                            }
                        }
                    }

                    /*
                    // Update Adept Powers.
                    _progress?.Report("Adding Adept Powers");
                    if (objXmlKit["powers"] != null)
                    {
                        // Open the Powers XML file and locate the selected power.
                        XmlDocument objXmlPowerDocument = XmlManager.Load("powers.xml");

                        foreach (XmlNode objXmlPower in objXmlKit.SelectNodes("powers/power"))
                        {
                            //TODO: Fix
                        }
                    }
                    */

                    // Update Complex Forms.
                    _progress?.Report("Adding Complex Forms");
                    XmlElement xmlComplexForms = objXmlKit["complexforms"];
                    if (xmlComplexForms != null)
                    {
                        // Open the Programs XML file and locate the selected program.
                        XmlDocument objXmlComplexFormDocument
                            = await CharacterObject.LoadDataAsync("complexforms.xml", token: token).ConfigureAwait(false);
                        using (XmlNodeList xmlComplexFormsList = xmlComplexForms.SelectNodes("complexform"))
                        {
                            if (xmlComplexFormsList?.Count > 0)
                            {
                                foreach (XmlNode objXmlComplexForm in xmlComplexFormsList)
                                {
                                    XmlNode objXmlComplexFormNode =
                                        objXmlComplexFormDocument.TryGetNodeByNameOrId(
                                            "/chummer/complexforms/complexform", objXmlComplexForm["name"]?.InnerTextViaPool(token),
                                            await CharacterObjectSettings.BookXPathAsync(token: token)
                                                .ConfigureAwait(false));
                                    if (objXmlComplexFormNode != null)
                                    {
                                        ComplexForm objComplexForm = new ComplexForm(CharacterObject);
                                        try
                                        {
                                            await objComplexForm.CreateAsync(objXmlComplexFormNode, token: token)
                                                .ConfigureAwait(false);
                                            if (objComplexForm.InternalId.IsEmptyGuid())
                                            {
                                                await objComplexForm.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                                continue;
                                            }
                                            await CharacterObject.ComplexForms.AddAsync(objComplexForm, token)
                                                .ConfigureAwait(false);
                                        }
                                        catch
                                        {
                                            await objComplexForm.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                            throw;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Update AI Programs.
                    _progress?.Report("Adding AI Programs");
                    XmlElement xmlPrograms = objXmlKit["programs"];
                    if (xmlPrograms != null)
                    {
                        // Open the Programs XML file and locate the selected program.
                        XmlDocument objXmlProgramDocument
                            = await CharacterObject.LoadDataAsync("programs.xml", token: token).ConfigureAwait(false);
                        using (XmlNodeList xmlProgramsList = xmlPrograms.SelectNodes("program"))
                        {
                            if (xmlProgramsList?.Count > 0)
                            {
                                foreach (XmlNode objXmlProgram in xmlProgramsList)
                                {
                                    XmlNode objXmlProgramNode = objXmlProgramDocument.TryGetNodeByNameOrId(
                                        "/chummer/programs/program", objXmlProgram["name"]?.InnerTextViaPool(token),
                                        await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                    if (objXmlProgramNode != null)
                                    {
                                        AIProgram objProgram = new AIProgram(CharacterObject);
                                        await objProgram.CreateAsync(objXmlProgramNode, token: token).ConfigureAwait(false);
                                        await CharacterObject.AIPrograms.AddAsync(objProgram, token).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                    }

                    // Update Spells.
                    _progress?.Report("Adding Spells");
                    XmlElement xmlSpells = objXmlKit["spells"];
                    if (xmlSpells != null)
                    {
                        XmlDocument objXmlSpellDocument
                            = await CharacterObject.LoadDataAsync("spells.xml", token: token).ConfigureAwait(false);
                        using (XmlNodeList xmlSpellsList = xmlSpells.SelectNodes("spell"))
                        {
                            if (xmlSpellsList?.Count > 0)
                            {
                                foreach (XmlNode objXmlSpell in xmlSpellsList)
                                {
                                    string strCategory = objXmlSpell["category"]?.InnerTextViaPool(token);
                                    string strName = objXmlSpell["name"].InnerTextViaPool(token);
                                    // Make sure the Spell has not already been added to the character.
                                    if (await CharacterObject.Spells
                                            .AnyAsync(x => x.Name == strName && x.Category == strCategory, token)
                                            .ConfigureAwait(false))
                                        continue;
                                    XmlNode objXmlSpellNode = objXmlSpellDocument.TryGetNodeByNameOrId(
                                        "/chummer/spells/spell", strName,
                                        "category = " + strCategory.CleanXPath() + " and " + await CharacterObjectSettings
                                            .BookXPathAsync(token: token).ConfigureAwait(false));

                                    if (objXmlSpellNode == null)
                                        continue;

                                    string strForceValue = objXmlSpell.Attributes?["select"]?.InnerTextViaPool(token) ?? string.Empty;
                                    Spell objSpell = new Spell(CharacterObject);
                                    try
                                    {
                                        await objSpell.CreateAsync(objXmlSpellNode, strForceValue, token: token)
                                            .ConfigureAwait(false);
                                        await CharacterObject.Spells.AddAsync(objSpell, token).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        await objSpell.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                        throw;
                                    }
                                }
                            }
                        }
                    }

                    // Update Spirits.
                    _progress?.Report("Adding Spirits");
                    XmlElement xmlSpirits = objXmlKit["spirits"];
                    if (xmlSpirits != null)
                    {
                        using (XmlNodeList xmlSpiritsList = xmlSpirits.SelectNodes("spirit"))
                        {
                            if (xmlSpiritsList?.Count > 0)
                            {
                                foreach (XmlNode objXmlSpirit in xmlSpiritsList)
                                {
                                    int.TryParse(objXmlSpirit["force"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intForce);
                                    int.TryParse(objXmlSpirit["services"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intServices);
                                    Spirit objSpirit = new Spirit(CharacterObject);
                                    try
                                    {
                                        await objSpirit.SetEntityTypeAsync(SpiritType.Spirit, token).ConfigureAwait(false);
                                        await objSpirit.SetNameAsync(objXmlSpirit["name"].InnerTextViaPool(token), token).ConfigureAwait(false);
                                        await objSpirit.SetForceAsync(intForce, token).ConfigureAwait(false);
                                        await objSpirit.SetServicesOwedAsync(intServices, token).ConfigureAwait(false);
                                        await CharacterObject.Spirits.AddAsync(objSpirit, token).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            await CharacterObject.Spirits.RemoveAsync(objSpirit, token: token).ConfigureAwait(false);
                                        }
                                        catch
                                        {
                                            //swallow this
                                        }
                                        await objSpirit.DisposeAsync().ConfigureAwait(false);
                                        throw;
                                    }
                                }
                            }
                        }
                    }

                    // Update Lifestyles.
                    _progress?.Report("Adding Lifestyles");
                    XmlElement xmlLifestyles = objXmlKit["lifestyles"];
                    if (xmlLifestyles != null)
                    {
                        XmlDocument objXmlLifestyleDocument
                            = await CharacterObject.LoadDataAsync("lifestyles.xml", token: token).ConfigureAwait(false);

                        foreach (XmlNode objXmlLifestyle in xmlLifestyles.SelectNodes("lifestyle"))
                        {
                            // Create the Lifestyle.
                            // Custom kits saved without an advanced lifestyle have no <baselifestyle>, so fall back to <name>.
                            string strBaseLifestyle = (objXmlLifestyle["baselifestyle"] ?? objXmlLifestyle["name"])?.InnerTextViaPool(token);
                            if (string.IsNullOrEmpty(strBaseLifestyle))
                                continue;
                            XmlNode objXmlLifestyleNode = objXmlLifestyleDocument.TryGetNodeByNameOrId(
                                "/chummer/lifestyles/lifestyle", strBaseLifestyle);
                            if (objXmlLifestyleNode == null)
                                continue;
                            Lifestyle objLifestyle = new Lifestyle(CharacterObject);
                            try
                            {
                                await objLifestyle.CreateAsync(objXmlLifestyleNode, token).ConfigureAwait(false);
                                // This is an Advanced Lifestyle, so build it manually.
                                objLifestyle.CustomName = objXmlLifestyle["name"]?.InnerTextViaPool(token) ?? string.Empty;
                                int.TryParse(objXmlLifestyle["comforts"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intComforts);
                                objLifestyle.Comforts = intComforts;
                                int.TryParse(objXmlLifestyle["security"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intSecurity);
                                objLifestyle.Security = intSecurity;
                                int.TryParse(objXmlLifestyle["area"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intArea);
                                objLifestyle.Area = intArea;

                                foreach (XmlNode objXmlQuality in objXmlLifestyle.SelectNodes("qualities/quality"))
                                {
                                    LifestyleQuality lq = new LifestyleQuality(CharacterObject);
                                    try
                                    {
                                        await lq.CreateAsync(objXmlQuality, objLifestyle, CharacterObject, QualitySource.Selected,
                                            token: token).ConfigureAwait(false);
                                        await objLifestyle.LifestyleQualities.AddAsync(lq, token).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        await lq.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                        throw;
                                    }
                                }

                                // Add the Lifestyle to the character and Lifestyle Tree.
                                await CharacterObject.Lifestyles.AddAsync(objLifestyle, token).ConfigureAwait(false);
                            }
                            catch
                            {
                                await objLifestyle.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                        }
                    }

                    // Update NuyenBP.
                    _progress?.Report("Adding NuyenBP");
                    string strNuyenBP = objXmlKit["nuyenbp"]?.InnerTextViaPool(token);
                    if (!string.IsNullOrEmpty(strNuyenBP)
                        && decimal.TryParse(strNuyenBP, NumberStyles.Any, GlobalSettings.InvariantCultureInfo,
                            out decimal decAmount))
                    {
                        //if (_objCharacter.BuildMethod == CharacterBuildMethod.Karma)
                        //decAmount *= 2;

                        await CharacterObject.ModifyNuyenBPAsync(decAmount, token).ConfigureAwait(false);
                    }

                    XmlDocument objXmlGearDocument
                        = await CharacterObject.LoadDataAsync("gear.xml", token: token).ConfigureAwait(false);

                    // Update Armor.
                    _progress?.Report("Adding Armor");
                    XmlElement xmlArmors = objXmlKit["armors"];
                    if (xmlArmors != null)
                    {
                        XmlDocument objXmlArmorDocument
                            = await CharacterObject.LoadDataAsync("armor.xml", token: token).ConfigureAwait(false);
                        foreach (XmlNode objXmlArmor in xmlArmors.SelectNodes("armor"))
                        {
                            XmlNode objXmlArmorNode = objXmlArmorDocument.TryGetNodeByNameOrId(
                                "/chummer/armors/armor", objXmlArmor["name"].InnerTextViaPool(token),
                                await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                            if (objXmlArmorNode == null)
                                continue;
                            Armor objArmor = new Armor(CharacterObject);
                            List<Weapon> lstWeapons = new List<Weapon>(1);
                            try
                            {
                                int.TryParse(objXmlArmor["rating"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intRating);
                                await objArmor.CreateAsync(objXmlArmorNode,
                                    intRating, lstWeapons, false,
                                    blnCreateChildren, token: token).ConfigureAwait(false);
                                await CharacterObject.Armor.AddAsync(objArmor, token).ConfigureAwait(false);

                                // Look for Armor Mods.
                                foreach (XmlNode objXmlMod in objXmlArmor.SelectNodes("mods/mod"))
                                {
                                    XmlNode objXmlModNode = objXmlArmorDocument.TryGetNodeByNameOrId(
                                        "/chummer/mods/mod", objXmlMod["name"].InnerTextViaPool(token),
                                        await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                    if (objXmlModNode != null)
                                    {
                                        ArmorMod objMod = new ArmorMod(CharacterObject, objArmor);
                                        try
                                        {
                                            if (objXmlMod["rating"] != null)
                                                int.TryParse(objXmlMod["rating"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out intRating);
                                            await objMod.CreateAsync(objXmlModNode, intRating, lstWeapons, token: token)
                                                .ConfigureAwait(false);

                                            foreach (XmlNode objXmlGear in objXmlMod.SelectNodes("gears/gear"))
                                                await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objMod, blnCreateChildren,
                                                    token).ConfigureAwait(false);

                                            await objArmor.ArmorMods.AddAsync(objMod, token).ConfigureAwait(false);
                                        }
                                        catch
                                        {
                                            await objMod.DeleteArmorModAsync(token: CancellationToken.None).ConfigureAwait(false);
                                            throw;
                                        }
                                    }
                                }

                                foreach (Weapon objWeapon in lstWeapons)
                                    await CharacterObject.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);

                                foreach (XmlNode objXmlGear in objXmlArmor.SelectNodes("gears/gear"))
                                    await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objArmor, blnCreateChildren, token)
                                        .ConfigureAwait(false);
                            }
                            catch
                            {
                                foreach (Weapon objWeapon in lstWeapons)
                                    await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                await objArmor.DeleteArmorAsync(token: CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                        }
                    }

                    // Update Weapons.
                    _progress?.Report("Adding Weapons");
                    XmlElement xmlWeapons = objXmlKit["weapons"];
                    if (xmlWeapons != null)
                    {
                        XmlDocument objXmlWeaponDocument
                            = await CharacterObject.LoadDataAsync("weapons.xml", token: token).ConfigureAwait(false);

                        XmlNodeList xmlWeaponsList = xmlWeapons.SelectNodes("weapon");
                        {
                            foreach (XmlNode objXmlWeapon in xmlWeaponsList)
                            {

                                XmlNode objXmlWeaponNode = objXmlWeaponDocument.TryGetNodeByNameOrId(
                                    "/chummer/weapons/weapon", objXmlWeapon["name"].InnerTextViaPool(token),
                                    await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                if (objXmlWeaponNode != null)
                                {
                                    List<Weapon> lstWeapons = new List<Weapon>(1);
                                    Weapon objWeapon = new Weapon(CharacterObject);
                                    try
                                    {
                                        await objWeapon.CreateAsync(objXmlWeaponNode, lstWeapons, blnCreateChildren,
                                                token: token)
                                            .ConfigureAwait(false);
                                        await CharacterObject.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);

                                        // Look for Weapon Accessories.
                                        foreach (XmlNode objXmlAccessory in objXmlWeapon.SelectNodes("accessories/accessory"))
                                        {
                                            XmlNode objXmlAccessoryNode = objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                "/chummer/accessories/accessory", objXmlAccessory["name"].InnerTextViaPool(token),
                                                await CharacterObjectSettings.BookXPathAsync(token: token)
                                                    .ConfigureAwait(false));
                                            if (objXmlAccessoryNode == null)
                                                continue;
                                            string strMount = objXmlAccessory["mount"]?.InnerTextViaPool(token) ?? "Internal";
                                            string strExtraMount = objXmlAccessory["extramount"]?.InnerTextViaPool(token) ?? "None";
                                            WeaponAccessory objMod = new WeaponAccessory(CharacterObject);
                                            try
                                            {
                                                await objMod.CreateAsync(objXmlAccessoryNode,
                                                    new ValueTuple<string, string>(strMount, strExtraMount), 0,
                                                    false, blnCreateChildren, token: token).ConfigureAwait(false);
                                                await objMod.SetParentAsync(objWeapon, token).ConfigureAwait(false);

                                                await objWeapon.WeaponAccessories.AddAsync(objMod, token).ConfigureAwait(false);

                                                foreach (XmlNode objXmlGear in objXmlAccessory.SelectNodes("gears/gear"))
                                                    await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objMod,
                                                        blnCreateChildren,
                                                        token).ConfigureAwait(false);
                                            }
                                            catch
                                            {
                                                await objMod.DeleteWeaponAccessoryAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                throw;
                                            }
                                        }

                                        // Look for an Underbarrel Weapon.
                                        XmlElement xmlUnderbarrelNode = objXmlWeapon["underbarrel"];
                                        if (xmlUnderbarrelNode != null)
                                        {
                                            XmlNode objXmlUnderbarrelNode = objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                "/chummer/weapons/weapon", objXmlWeapon["underbarrel"].InnerTextViaPool(token),
                                                await CharacterObjectSettings.BookXPathAsync(token: token)
                                                    .ConfigureAwait(false));
                                            if (objXmlUnderbarrelNode == null)
                                            {
                                                List<Weapon> lstLoopWeapons = new List<Weapon>(1);
                                                Weapon objUnderbarrelWeapon = new Weapon(CharacterObject);
                                                try
                                                {
                                                    await objUnderbarrelWeapon.CreateAsync(objXmlUnderbarrelNode, lstLoopWeapons,
                                                        blnCreateChildren, token: token).ConfigureAwait(false);
                                                    await objWeapon.UnderbarrelWeapons.AddAsync(objUnderbarrelWeapon, token)
                                                        .ConfigureAwait(false);
                                                    if (!objWeapon.AllowAccessory)
                                                        objUnderbarrelWeapon.AllowAccessory = false;

                                                    foreach (Weapon objLoopWeapon in lstLoopWeapons)
                                                    {
                                                        if (!objWeapon.AllowAccessory)
                                                            objLoopWeapon.AllowAccessory = false;
                                                        await objWeapon.UnderbarrelWeapons.AddAsync(objLoopWeapon, token)
                                                            .ConfigureAwait(false);
                                                    }

                                                    foreach (XmlNode objXmlAccessory in xmlUnderbarrelNode.SelectNodes(
                                                                 "accessories/accessory"))
                                                    {
                                                        XmlNode objXmlAccessoryNode =
                                                            objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                                "/chummer/accessories/accessory", objXmlAccessory["name"].InnerTextViaPool(token),
                                                                await CharacterObjectSettings.BookXPathAsync(token: token)
                                                                    .ConfigureAwait(false));
                                                        if (objXmlAccessoryNode == null)
                                                            continue;
                                                        string strMount = objXmlAccessory["mount"]?.InnerTextViaPool(token) ?? "Internal";
                                                        string strExtraMount = objXmlAccessory["extramount"]?.InnerTextViaPool(token) ?? "None";
                                                        WeaponAccessory objMod = new WeaponAccessory(CharacterObject);
                                                        try
                                                        {
                                                            await objMod.CreateAsync(objXmlAccessoryNode,
                                                                new ValueTuple<string, string>(strMount, strExtraMount), 0, false,
                                                                blnCreateChildren, token: token).ConfigureAwait(false);
                                                            await objMod.SetParentAsync(objWeapon, token).ConfigureAwait(false);

                                                            await objUnderbarrelWeapon.WeaponAccessories.AddAsync(objMod, token)
                                                                .ConfigureAwait(false);

                                                            foreach (XmlNode objXmlGear in objXmlAccessory.SelectNodes("gears/gear"))
                                                                await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objMod,
                                                                    blnCreateChildren, token).ConfigureAwait(false);
                                                        }
                                                        catch
                                                        {
                                                            await objMod.DeleteWeaponAccessoryAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                            throw;
                                                        }
                                                    }
                                                }
                                                catch
                                                {
                                                    foreach (Weapon objLoopWeapon in lstLoopWeapons)
                                                        await objLoopWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                    await objUnderbarrelWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                    throw;
                                                }
                                            }
                                        }

                                        foreach (Weapon objLoopWeapon in lstWeapons)
                                        {
                                            await CharacterObject.Weapons.AddAsync(objLoopWeapon, token).ConfigureAwait(false);
                                        }
                                    }
                                    catch
                                    {
                                        foreach (Weapon objLoopWeapon in lstWeapons)
                                            await objLoopWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                        await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                        throw;
                                    }
                                }
                            }
                        }
                    }

                    XmlDocument objXmlCyberwareDocument
                        = await CharacterObject.LoadDataAsync("cyberware.xml", token: token).ConfigureAwait(false);
                    XmlDocument objXmlBiowareDocument
                        = await CharacterObject.LoadDataAsync("bioware.xml", token: token).ConfigureAwait(false);

                    // Update Cyberware.
                    _progress?.Report("Adding Cyberware");
                    XmlElement xmlCyberwares = objXmlKit["cyberwares"];
                    if (xmlCyberwares != null)
                    {
                        XmlNodeList xmlCyberwaresList = xmlCyberwares.SelectNodes("cyberware");
                        {
                            foreach (XmlNode objXmlCyberware in xmlCyberwaresList)
                            {

                                await AddPACKSCyberwareAsync(objXmlCyberwareDocument, objXmlBiowareDocument,
                                    objXmlGearDocument,
                                    objXmlCyberware, CharacterObject, blnCreateChildren, token).ConfigureAwait(false);
                            }
                        }
                    }

                    // Update Bioware.
                    _progress?.Report("Adding Bioware");
                    XmlElement xmlBiowares = objXmlKit["biowares"];
                    if (xmlBiowares != null)
                    {
                        XmlNodeList xmlBiowaresList = xmlBiowares.SelectNodes("bioware");
                        {
                            foreach (XmlNode objXmlBioware in xmlBiowaresList)
                            {

                                await AddPACKSCyberwareAsync(objXmlCyberwareDocument, objXmlBiowareDocument,
                                    objXmlGearDocument,
                                    objXmlBioware,
                                    CharacterObject, blnCreateChildren, token).ConfigureAwait(false);
                            }
                        }
                    }

                    // Update Gear.
                    _progress?.Report("Adding Gear");
                    XmlElement xmlGears = objXmlKit["gears"];
                    if (xmlGears != null)
                    {
                        XmlNodeList xmlGearsList = xmlGears.SelectNodes("gear");
                        {
                            foreach (XmlNode objXmlGear in xmlGearsList)
                            {

                                await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, CharacterObject, blnCreateChildren,
                                    token).ConfigureAwait(false);
                            }
                        }
                    }

                    // Update Vehicles.
                    _progress?.Report("Adding Vehicles");
                    XmlElement xmlVehicles = objXmlKit["vehicles"];
                    if (xmlVehicles != null)
                    {
                        XmlDocument objXmlVehicleDocument
                            = await CharacterObject.LoadDataAsync("vehicles.xml", token: token).ConfigureAwait(false);
                        XmlNodeList xmlVehiclesList = xmlVehicles.SelectNodes("vehicle");

                        {
                            foreach (XmlNode objXmlVehicle in xmlVehiclesList)
                            {

                                Gear objDefaultSensor = null;

                                XmlNode objXmlVehicleNode = objXmlVehicleDocument.TryGetNodeByNameOrId(
                                    "/chummer/vehicles/vehicle", objXmlVehicle["name"].InnerTextViaPool(token),
                                    await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                if (objXmlVehicleNode == null)
                                    continue;
                                Vehicle objVehicle = new Vehicle(CharacterObject);
                                try
                                {
                                    await objVehicle
                                        .CreateAsync(objXmlVehicleNode, blnCreateChildren: blnCreateChildren, token: token)
                                        .ConfigureAwait(false);
                                    await CharacterObject.Vehicles.AddAsync(objVehicle, token).ConfigureAwait(false);

                                    // Grab the default Sensor that comes with the Vehicle.
                                    foreach (Gear objSensorGear in objVehicle.GearChildren)
                                    {
                                        if (objSensorGear.Category == "Sensors" && objSensorGear.Cost == "0"
                                                                                && await objSensorGear.GetRatingAsync(token)
                                                                                    .ConfigureAwait(false) == 0)
                                        {
                                            objDefaultSensor = objSensorGear;
                                            break;
                                        }
                                    }

                                    // Add any Vehicle Mods.
                                    foreach (XmlNode objXmlMod in objXmlVehicle.SelectNodes("mods/mod"))
                                    {
                                        XmlNode objXmlModNode = objXmlVehicleDocument.TryGetNodeByNameOrId(
                                            "/chummer/mods/mod", objXmlMod["name"].InnerTextViaPool(token),
                                            await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                                        if (objXmlModNode == null)
                                            continue;
                                        int intRating = 0;
                                        objXmlMod.TryGetInt32FieldQuickly("rating", ref intRating);
                                        VehicleMod objMod = new VehicleMod(CharacterObject);
                                        try
                                        {
                                            await objMod.CreateAsync(objXmlModNode, intRating, objVehicle, token: token)
                                                .ConfigureAwait(false);
                                            await objVehicle.Mods.AddAsync(objMod, token).ConfigureAwait(false);

                                            foreach (XmlNode objXmlCyberware in objXmlMod.SelectNodes("cyberwares/cyberware"))
                                                await AddPACKSCyberwareAsync(objXmlCyberwareDocument, objXmlBiowareDocument,
                                                    objXmlGearDocument,
                                                    objXmlCyberware, objMod, blnCreateChildren, token).ConfigureAwait(false);
                                        }
                                        catch
                                        {
                                            await objMod.DeleteVehicleModAsync(token: CancellationToken.None).ConfigureAwait(false);
                                            throw;
                                        }
                                    }

                                    // Add any Vehicle Gear.
                                    foreach (XmlNode objXmlGear in objXmlVehicle.SelectNodes("gears/gear"))
                                    {
                                        Gear objGear =
                                            await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objVehicle,
                                                blnCreateChildren,
                                                token).ConfigureAwait(false);
                                        // If this is a Sensor, it will replace the Vehicle's base sensor, so remove it.
                                        if (objGear?.Category == "Sensors" && objGear.Cost == "0" &&
                                            await objGear.GetRatingAsync(token).ConfigureAwait(false) == 0)
                                        {
                                            await objVehicle.GearChildren.RemoveAsync(objDefaultSensor, token)
                                                .ConfigureAwait(false);
                                        }
                                    }

                                    // Add any Vehicle Weapons.
                                    if (objXmlVehicle["weapons"] != null)
                                    {
                                        XmlDocument objXmlWeaponDocument = await CharacterObject
                                            .LoadDataAsync("weapons.xml", token: token)
                                            .ConfigureAwait(false);

                                        foreach (XmlNode objXmlWeapon in objXmlVehicle.SelectNodes("weapons/weapon"))
                                        {
                                            List<Weapon> lstSubWeapons = new List<Weapon>(1);
                                            XmlNode objXmlWeaponNode = objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                "/chummer/weapons/weapon", objXmlWeapon["name"].InnerTextViaPool(token),
                                                await CharacterObjectSettings.BookXPathAsync(token: token)
                                                    .ConfigureAwait(false));
                                            if (objXmlWeaponNode == null)
                                                continue;
                                            Weapon objWeapon = new Weapon(CharacterObject);
                                            try
                                            {
                                                await objWeapon.SetParentVehicleAsync(objVehicle, token).ConfigureAwait(false);
                                                await objWeapon
                                                    .CreateAsync(objXmlWeaponNode, lstSubWeapons, blnCreateChildren, token: token)
                                                    .ConfigureAwait(false);

                                                // Find the first Weapon Mount in the Vehicle.
                                                await objVehicle.Mods.ForEachWithBreakAsync(async (objMod, t) =>
                                                {
                                                    if (objMod.Name.Contains("Weapon Mount")
                                                        || !string.IsNullOrEmpty(objMod.WeaponMountCategories)
                                                        && objMod.WeaponMountCategories.Contains(objWeapon.Category))
                                                    {
                                                        await objMod.Weapons.AddAsync(objWeapon, t).ConfigureAwait(false);
                                                        foreach (Weapon objSubWeapon in lstSubWeapons)
                                                            await objMod.Weapons.AddAsync(objSubWeapon, t)
                                                                .ConfigureAwait(false);
                                                        return false;
                                                    }

                                                    return true;
                                                }, token).ConfigureAwait(false);

                                                // Look for Weapon Accessories.
                                                foreach (XmlNode objXmlAccessory in objXmlWeapon.SelectNodes(
                                                             "accessories/accessory"))
                                                {
                                                    XmlNode objXmlAccessoryNode =
                                                        objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                            "/chummer/accessories/accessory", objXmlAccessory["name"].InnerTextViaPool(token),
                                                            await CharacterObjectSettings.BookXPathAsync(token: token)
                                                                .ConfigureAwait(false));
                                                    if (objXmlAccessoryNode == null)
                                                        continue;
                                                    string strMount = objXmlAccessory["mount"]?.InnerTextViaPool(token) ?? "Internal";
                                                    string strExtraMount = objXmlAccessory["extramount"]?.InnerTextViaPool(token) ?? "None";
                                                    WeaponAccessory objMod = new WeaponAccessory(CharacterObject);
                                                    try
                                                    {
                                                        await objMod.CreateAsync(objXmlAccessoryNode,
                                                            new ValueTuple<string, string>(strMount, strExtraMount),
                                                            0, false, blnCreateChildren, token: token).ConfigureAwait(false);
                                                        await objMod.SetParentAsync(objWeapon, token).ConfigureAwait(false);

                                                        await objWeapon.WeaponAccessories.AddAsync(objMod, token).ConfigureAwait(false);
                                                    }
                                                    catch
                                                    {
                                                        await objMod.DeleteWeaponAccessoryAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                        throw;
                                                    }
                                                }

                                                // Look for an Underbarrel Weapon.
                                                XmlElement xmlUnderbarrelNode = objXmlWeapon["underbarrel"];
                                                if (xmlUnderbarrelNode != null)
                                                {
                                                    XmlNode objXmlUnderbarrelNode =
                                                        objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                            "/chummer/weapons/weapon", objXmlWeapon["underbarrel"].InnerTextViaPool(token),
                                                            await CharacterObjectSettings.BookXPathAsync(token: token)
                                                                .ConfigureAwait(false));
                                                    if (objXmlUnderbarrelNode != null)
                                                    {
                                                        List<Weapon> lstLoopWeapons = new List<Weapon>(1);
                                                        Weapon objUnderbarrelWeapon = new Weapon(CharacterObject);
                                                        try
                                                        {
                                                            await objUnderbarrelWeapon.CreateAsync(objXmlUnderbarrelNode,
                                                                lstLoopWeapons,
                                                                blnCreateChildren, token: token).ConfigureAwait(false);
                                                            await objWeapon.UnderbarrelWeapons.AddAsync(objUnderbarrelWeapon, token)
                                                                .ConfigureAwait(false);
                                                            if (!objWeapon.AllowAccessory)
                                                                objUnderbarrelWeapon.AllowAccessory = false;

                                                            foreach (Weapon objLoopWeapon in lstLoopWeapons)
                                                            {
                                                                if (!objWeapon.AllowAccessory)
                                                                    objLoopWeapon.AllowAccessory = false;
                                                                await objWeapon.UnderbarrelWeapons.AddAsync(objLoopWeapon, token)
                                                                    .ConfigureAwait(false);
                                                            }

                                                            foreach (XmlNode objXmlAccessory in xmlUnderbarrelNode.SelectNodes(
                                                                         "accessories/accessory"))
                                                            {
                                                                XmlNode objXmlAccessoryNode = objXmlWeaponDocument.TryGetNodeByNameOrId(
                                                                    "/chummer/accessories/accessory", objXmlAccessory["name"].InnerTextViaPool(token),
                                                                    await CharacterObjectSettings.BookXPathAsync(token: token)
                                                                        .ConfigureAwait(false));
                                                                if (objXmlAccessoryNode == null)
                                                                    continue;
                                                                string strMount = objXmlAccessory["mount"]?.InnerTextViaPool(token) ?? "Internal";
                                                                string strExtraMount =
                                                                    objXmlAccessory["extramount"]?.InnerTextViaPool(token) ?? "None";
                                                                WeaponAccessory objMod = new WeaponAccessory(CharacterObject);
                                                                try
                                                                {
                                                                    await objMod.CreateAsync(objXmlAccessoryNode,
                                                                        new ValueTuple<string, string>(strMount, strExtraMount), 0, false,
                                                                        blnCreateChildren, token: token).ConfigureAwait(false);
                                                                    await objMod.SetParentAsync(objWeapon, token).ConfigureAwait(false);

                                                                    await objUnderbarrelWeapon.WeaponAccessories.AddAsync(objMod, token)
                                                                        .ConfigureAwait(false);

                                                                    foreach (XmlNode objXmlGear in
                                                                             objXmlAccessory.SelectNodes("gears/gear"))
                                                                        await AddPACKSGearAsync(objXmlGearDocument, objXmlGear, objMod,
                                                                            blnCreateChildren, token).ConfigureAwait(false);
                                                                }
                                                                catch
                                                                {
                                                                    await objMod.DeleteWeaponAccessoryAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                                    throw;
                                                                }
                                                            }
                                                        }
                                                        catch
                                                        {
                                                            foreach (Weapon objLoopWeapon in lstLoopWeapons)
                                                                await objLoopWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                            await objUnderbarrelWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                            throw;
                                                        }
                                                    }
                                                }
                                            }
                                            catch
                                            {
                                                foreach (Weapon objLoopWeapon in lstSubWeapons)
                                                    await objLoopWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                                                throw;
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    await objVehicle.DeleteVehicleAsync(CancellationToken.None).ConfigureAwait(false);
                                    throw;
                                }
                            }
                        }
                    }
                }

                return RuleResult.Changed;
            }

            private async Task<Gear> AddPACKSGearAsync(XmlDocument objXmlGearDocument, XmlNode objXmlGear, IHasName objParentObject,
                                      bool blnCreateChildren, CancellationToken token = default)
            {
                XmlNode objXmlGearNode = null;
                string strName = objXmlGear["name"]?.InnerTextViaPool(token);
                _progress?.Report("Adding gear: " + strName);
                if (!string.IsNullOrEmpty(strName))
                {
                    string strCategory = objXmlGear["category"]?.InnerTextViaPool(token);
                    if (!string.IsNullOrEmpty(strCategory))
                        objXmlGearNode = objXmlGearDocument.TryGetNodeByNameOrId(
                            "/chummer/gears/gear", strName,
                            await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false)
                                + " and category = " + strCategory.CleanXPath());
                    else
                        objXmlGearNode = objXmlGearDocument.TryGetNodeByNameOrId(
                            "/chummer/gears/gear", strName,
                            await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                }

                if (objXmlGearNode == null)
                    return null;

                int.TryParse(objXmlGear["rating"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intRating);
                decimal decQty = 1;
                string strQty = objXmlGear["qty"]?.InnerTextViaPool(token);
                if (!string.IsNullOrEmpty(strQty))
                {
                    decimal.TryParse(strQty, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decQty);
                }

                List<Weapon> lstWeapons = new List<Weapon>(1);
                string strForceValue = objXmlGear.SelectSingleNodeAndCacheExpressionAsNavigator("name/@select", token)?.Value ?? string.Empty;
                Gear objNewGear = new Gear(CharacterObject);
                try
                {
                    try
                    {
                        await objNewGear.CreateAsync(objXmlGearNode, intRating, lstWeapons, strForceValue, true, blnCreateChildren, objParent: objParentObject, token: token).ConfigureAwait(false);
                        if (objNewGear.InternalId.IsEmptyGuid())
                        {
                            await objNewGear.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                            foreach (Weapon weapon in lstWeapons) await weapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                            return null;
                        }
                        await objNewGear.SetQuantityAsync(decQty, token).ConfigureAwait(false);

                        switch (objParentObject)
                        {
                            case Character objParentCharacter:
                                await objParentCharacter.Gear.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;

                            case Gear objParentGear:
                                await objParentGear.Children.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;

                            case Armor objParentArmor:
                                await objParentArmor.GearChildren.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;

                            case ArmorMod objParentArmorMod:
                                await objParentArmorMod.GearChildren.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;

                            case WeaponAccessory objParentWeaponAccessory:
                                await objParentWeaponAccessory.GearChildren.AddAsync(objNewGear, token)
                                                              .ConfigureAwait(false);
                                break;

                            case Cyberware objParentCyberware:
                                await objParentCyberware.GearChildren.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;

                            case Vehicle objParentVehicle:
                                await objParentVehicle.GearChildren.AddAsync(objNewGear, token).ConfigureAwait(false);
                                break;
                        }

                        // Look for child components.
                        using (XmlNodeList xmlChildrenList = objXmlGear.SelectNodes("gears/gear"))
                        {
                            if (xmlChildrenList?.Count > 0)
                            {
                                foreach (XmlNode xmlChild in xmlChildrenList)
                                {
                                    await AddPACKSGearAsync(objXmlGearDocument, xmlChild, objNewGear, blnCreateChildren,
                                                            token).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    catch
                    {
                        await objNewGear.RemoveAsync(false, token).ConfigureAwait(false);
                        throw;
                    }
                }
                catch
                {
                    if (lstWeapons.Count > 0)
                    {
                        foreach (Weapon objWeapon in lstWeapons)
                        {
                            await objWeapon.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    throw;
                }

                // Add any Weapons created by the Gear.
                if (lstWeapons.Count > 0)
                {
                    try
                    {
                        foreach (Weapon objWeapon in lstWeapons)
                        {
                            await CharacterObject.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await objNewGear.RemoveAsync(false, token).ConfigureAwait(false);
                        throw;
                    }
                }

                return objNewGear;
            }

            private async Task AddPACKSCyberwareAsync(XmlDocument xmlCyberwareDocument, XmlDocument xmlBiowareDocument,
                                           XmlDocument xmlGearDocument, XmlNode xmlCyberware, object objParentObject,
                                           bool blnCreateChildren, CancellationToken token = default)
            {
                token.ThrowIfCancellationRequested();
                {
                    token.ThrowIfCancellationRequested();
                    Grade objGrade = await Grade.ConvertToCyberwareGradeAsync(xmlCyberware["grade"]?.InnerTextViaPool(token),
                        Improvement.ImprovementSource.Cyberware, CharacterObject, token).ConfigureAwait(false);

                    int.TryParse(xmlCyberware["rating"]?.InnerTextViaPool(token), NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intRating);

                    Improvement.ImprovementSource eSource = Improvement.ImprovementSource.Cyberware;
                    string strName = xmlCyberware["name"]?.InnerTextViaPool(token);
                    if (string.IsNullOrEmpty(strName))
                        return;

                    XmlNode objXmlCyberwareNode = xmlCyberwareDocument.TryGetNodeByNameOrId(
                        "/chummer/cyberwares/cyberware", strName,
                        await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                    if (objXmlCyberwareNode == null)
                    {
                        eSource = Improvement.ImprovementSource.Bioware;
                        objXmlCyberwareNode = xmlBiowareDocument.TryGetNodeByNameOrId(
                            "/chummer/biowares/bioware", strName,
                            await CharacterObjectSettings.BookXPathAsync(token: token).ConfigureAwait(false));
                        if (objXmlCyberwareNode == null)
                        {
                            return;
                        }
                    }

                    List<Weapon> lstWeapons = new List<Weapon>(1);
                    List<Vehicle> lstVehicles = new List<Vehicle>(1);
                    Cyberware objCyberware = new Cyberware(CharacterObject);
                    try
                    {
                        await objCyberware.CreateAsync(objXmlCyberwareNode, objGrade, eSource, intRating, lstWeapons,
                            lstVehicles,
                            true,
                            blnCreateChildren, token: token).ConfigureAwait(false);

                        switch (objParentObject)
                        {
                            case Character objParentCharacter:
                                await (await objParentCharacter.GetCyberwareAsync(token).ConfigureAwait(false)).AddAsync(objCyberware, token).ConfigureAwait(false);
                                break;

                            case Cyberware objParentCyberware:
                                await (await objParentCyberware.GetChildrenAsync(token).ConfigureAwait(false)).AddAsync(objCyberware, token).ConfigureAwait(false);
                                break;

                            case VehicleMod objParentVehicleMod:
                                await objParentVehicleMod.Cyberware.AddAsync(objCyberware, token).ConfigureAwait(false);
                                break;
                        }

                        // Add any children.
                        using (XmlNodeList xmlCyberwareList = xmlCyberware.SelectNodes("cyberwares/cyberware"))
                        {
                            if (xmlCyberwareList?.Count > 0)
                            {
                                foreach (XmlNode objXmlChild in xmlCyberwareList)
                                    await AddPACKSCyberwareAsync(xmlCyberwareDocument, xmlBiowareDocument,
                                        xmlGearDocument,
                                        objXmlChild,
                                        objCyberware, blnCreateChildren, token).ConfigureAwait(false);
                            }
                        }

                        using (XmlNodeList xmlGearList = xmlCyberware.SelectNodes("gears/gear"))
                        {
                            if (xmlGearList?.Count > 0)
                            {
                                foreach (XmlNode objXmlGear in xmlGearList)
                                    await AddPACKSGearAsync(xmlGearDocument, objXmlGear, objCyberware,
                                        blnCreateChildren,
                                        token).ConfigureAwait(false);
                            }
                        }

                        if (lstWeapons.Count > 0)
                        {
                            await lstWeapons.ForEachAsync((objWeapon, t) => CharacterObject.Weapons.AddAsync(objWeapon, t),
                                token).ConfigureAwait(false);
                        }

                        if (lstVehicles.Count > 0)
                        {
                            await lstVehicles.ForEachAsync(
                                (objVehicle, t) => CharacterObject.Vehicles.AddAsync(objVehicle, t),
                                token).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        if (lstWeapons.Count > 0)
                        {
                            foreach (Weapon objWeapon in lstWeapons)
                            {
                                await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                            }
                        }

                        if (lstVehicles.Count > 0)
                        {
                            foreach (Vehicle objVehicle in lstVehicles)
                            {
                                await objVehicle.DeleteVehicleAsync(token: CancellationToken.None).ConfigureAwait(false);
                            }
                        }

                        await objCyberware.DeleteCyberwareAsync(token: CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
            }

        }
    }
}
