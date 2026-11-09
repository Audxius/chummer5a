using System.Linq;
// Usage: Screenshots <output dir> <character file>...
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chummer;
using Chummer.Desktop;
using Chummer.Desktop.Services;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;

internal static class Program
{
    private static int Main(string[] args)
    {
        string strOut = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
        Directory.CreateDirectory(strOut);
        DesktopServices.Register();
        HostServices.Messages = new LoggingMessageService();
        HostServices.Dialogs = new AutoDialogService();
        using (HeadlessUnitTestSession objSession = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder)))
        {
            objSession.Dispatch(async () =>
            {
                MainWindowViewModel objViewModel = new MainWindowViewModel();
                MainWindow objWindow = new MainWindow { DataContext = objViewModel, Width = 1440, Height = 900 };
                objWindow.Show();
                for (int i = 1; i < args.Length; ++i)
                    await GlobalSettings.MostRecentlyUsedCharacters.InsertAsync(0, Path.GetFullPath(args[i]));
                await objViewModel.Roster.RefreshAsync();
                await Settle();
                Save(objWindow, strOut, "01-roster");
                if (args.Length > 1)
                {
                    await objViewModel.OpenCharacterFileAsync(Path.GetFullPath(args[1]));
                    await Settle();
                    Save(objWindow, strOut, "02-overview");
                    if (objViewModel.CurrentPage is CharacterViewModel objDamaged && objDamaged.PhysicalBoxes.Count > 3)
                    {
                        objDamaged.PhysicalBoxes[3].ToggleCommand.Execute(null);
                        await Settle();
                        await Settle();
                        Save(objWindow, strOut, "02b-damage");
                        Console.WriteLine("damage: " + objDamaged.PhysicalLabel + " dirty=" + objDamaged.IsDirty);
                        objDamaged.Character.PhysicalCMFilled = 0;
                        await Settle();
                    }
                    if (objViewModel.CurrentPage is CharacterViewModel objCharacter)
                    {
                        foreach (CharacterSection eSection in new[] { CharacterSection.Attributes, CharacterSection.Skills, CharacterSection.Qualities, CharacterSection.Inventory, CharacterSection.Abilities, CharacterSection.Network, CharacterSection.Notes })
                        {
                            objCharacter.ShowSectionCommand.Execute(eSection);
                            await Settle();
                            Save(objWindow, strOut, "03-" + eSection.ToString().ToLowerInvariant());
                        }
                        // ===== Inventory: slot grid, detail panel and session actions =====
                        objCharacter.ShowSectionCommand.Execute(CharacterSection.Inventory);
                        await Settle();
                        System.Collections.Generic.List<InventoryItemViewModel> lstAllSlots()
                            => objCharacter.InventoryGroups.SelectMany(x => x.Items).SelectMany(x => x.SelfAndDescendants()).ToList();
                        int intInventoryShot = 0;
                        foreach (string strPick in new[] { "Katana", "Armor Jacket", "Meta Link", "Harley", "Roadmaster" })
                        {
                            InventoryItemViewModel objPick = lstAllSlots().FirstOrDefault(x => x.Name.Contains(strPick, StringComparison.OrdinalIgnoreCase));
                            if (objPick == null)
                                continue;
                            objCharacter.SelectInventoryItemCommand.Execute(objPick);
                            await Settle();
                            Save(objWindow, strOut, "08-inventory-" + (++intInventoryShot).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + strPick.Replace(' ', '-').ToLowerInvariant());
                        }
                        InventoryItemViewModel objGun = lstAllSlots().FirstOrDefault(x => x.UsesAmmo && x.AmmoRemaining > 0);
                        if (objGun != null)
                        {
                            objCharacter.SelectInventoryItemCommand.Execute(objGun);
                            await Settle();
                            Save(objWindow, strOut, "08-inventory-gun");
                            int intBefore = objGun.AmmoRemaining;
                            await objCharacter.FireWeaponCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            Console.WriteLine("fire " + objGun.Name + ": " + intBefore + " -> " + objCharacter.SelectedInventoryItem?.AmmoRemaining + " dirty=" + objCharacter.IsDirty);
                            Save(objWindow, strOut, "08-inventory-gun-fired");
                        }
                        InventoryItemViewModel objEquip = lstAllSlots().FirstOrDefault(x => x.CanEquip && x.Kind == InventoryKind.Weapon);
                        if (objEquip != null)
                        {
                            objCharacter.SelectInventoryItemCommand.Execute(objEquip);
                            await Settle();
                            bool blnBefore = objEquip.IsEquipped;
                            await objCharacter.ToggleEquippedCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            Console.WriteLine("equip " + objEquip.Name + ": " + blnBefore + " -> " + objCharacter.SelectedInventoryItem?.IsEquipped + ", loadout " + objCharacter.LoadoutSlots.Count);
                            Save(objWindow, strOut, "08-inventory-equip-toggled");
                        }
                        InventoryItemViewModel objUse = lstAllSlots().FirstOrDefault(x => x.IsConsumable && x.Quantity > 1);
                        if (objUse != null)
                        {
                            objCharacter.SelectInventoryItemCommand.Execute(objUse);
                            await Settle();
                            decimal decBefore = objUse.Quantity;
                            await objCharacter.UseOneCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            Console.WriteLine("use one " + objUse.Name + ": " + decBefore + " -> " + objCharacter.SelectedInventoryItem?.Quantity);
                        }
                        objCharacter.InventoryKind = InventoryKind.Armor;
                        await Settle();
                        Save(objWindow, strOut, "08-inventory-armor-tab");
                        objCharacter.InventoryKind = null;
                        await Settle();
                        // ===== end inventory =====
                        // Editing: raise the first attribute that can be raised and report the karma spent.
                        objCharacter.ShowSectionCommand.Execute(CharacterSection.Attributes);
                        await Settle();
                        AttributeViewModel objRaise = objCharacter.Attributes.Concat(objCharacter.SpecialAttributes).FirstOrDefault(x => x.CanQuickRaise);
                        if (objRaise != null)
                        {
                            string strKarmaBefore = objCharacter.Karma;
                            await objRaise.QuickRaiseCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            AttributeViewModel objAfter = objCharacter.Attributes.Concat(objCharacter.SpecialAttributes).First(x => x.Abbrev == objRaise.Abbrev);
                            Console.WriteLine("raise " + objRaise.Abbrev + ": " + objRaise.Display + " -> " + objAfter.Display + ", karma " + strKarmaBefore + " -> " + objCharacter.Karma);
                            Save(objWindow, strOut, "03b-attribute-raised");
                        }
                        // ---- LEDGER (career karma/nuyen log, undo, calendar) ----
                        if (objCharacter.Character.Created)
                            await LedgerScript.RunAsync(objWindow, objCharacter, strOut);
                        // ---- end LEDGER ----
                        // ===== Skills section: sorting, grouping and karma edits (SkillRules) =====
                        await SkillsSteps(objWindow, objViewModel, objCharacter, strOut);
                        // ===== end skills =====
                        // Clicking a skill opens its roll in the Dice Terminal.
                        SkillViewModel objSkill = objCharacter.AllSkills.FirstOrDefault(x => x.HasSpecPool)
                                                  ?? objCharacter.AllSkills.FirstOrDefault(x => x.IsRollable);
                        if (objSkill != null)
                        {
                            await objCharacter.RollCommand.ExecuteAsync(objSkill.HasSpecPool ? objSkill.SpecRollName : objSkill.RollName);
                            await Settle();
                            Save(objWindow, strOut, "04a-skill-roll");
                            Console.WriteLine("skill roll " + objSkill.Name + " pool " + objSkill.Pool + "/" + objSkill.SpecPool + ": " + objViewModel.Dice.Formula);
                        }
                        await objCharacter.RollCommand.ExecuteAsync(Chummer.Rolls.RollCatalog.InitiativeRoll);
                        await Settle();
                        Save(objWindow, strOut, "04b-initiative");
                        Console.WriteLine("initiative: " + objViewModel.Dice.Formula);
                        await objCharacter.RollCommand.ExecuteAsync(Chummer.Rolls.RollCatalog.DamageResistanceRoll);
                        await Settle();
                        Console.WriteLine("soak: " + objViewModel.Dice.Formula);
                        objViewModel.ShowDiceCommand.Execute(null);
                        await Settle();
                        Save(objWindow, strOut, "04-dice");
                        foreach (string strQuery in new[] { "escape", "dodge" })
                        {
                            objViewModel.Dice.Query = strQuery;
                            await Settle();
                            if (objViewModel.Dice.Results.Count > 0)
                                objViewModel.Dice.SelectedRoll = objViewModel.Dice.Results[0];
                            await Settle();
                            Save(objWindow, strOut, "05-dice-" + strQuery);
                            Console.WriteLine(strQuery + ": " + objViewModel.Dice.Results.Count + " results; formula: " + objViewModel.Dice.Formula);
                        }
                        objViewModel.Palette.OpenCommand.Execute(null);
                        objViewModel.Palette.Query = "bolt";
                        await Settle();
                        Save(objWindow, strOut, "06-search");
                        Console.WriteLine("search: " + string.Join(" | ", objViewModel.Palette.Results.Select(x => x.Title + " [" + x.Subtitle + "]")));
                        objViewModel.Palette.ActivateCommand.Execute(null);
                        await Settle();
                        Save(objWindow, strOut, "07-search-jump");
                        // ==== Store (purchase flows) ====
                        await StoreHarness.RunAsync(objCharacter, strOut, Settle);
                        // ==== end store ====
                        // ===== Qualities & Abilities editing (QualityRules / MagicRules) =====
                        await QualitiesAbilitiesDemo(objWindow, objCharacter, strOut, "10");
                        if (args.Length > 2)
                        {
                            await objViewModel.OpenCharacterFileAsync(Path.GetFullPath(args[2]));
                            await Settle();
                            if (objViewModel.CurrentPage is CharacterViewModel objSecond)
                                await QualitiesAbilitiesDemo(objWindow, objSecond, strOut, "20");
                        }
                        // ===== end Qualities & Abilities =====
                    }
                }
                if (args.Length > 2)
                {
                    // ===== Skills section on a create-mode character =====
                    objViewModel.Palette.CloseCommand.Execute(null);
                    await objViewModel.OpenCharacterFileAsync(Path.GetFullPath(args[2]));
                    await Settle();
                    if (objViewModel.CurrentPage is CharacterViewModel objCreate)
                    {
                        objCreate.ShowSectionCommand.Execute(CharacterSection.Skills);
                        await Settle();
                        Save(objWindow, strOut, "09a-skills-create");
                        SkillViewModel objRaise = objCreate.AllSkills.FirstOrDefault(x => x.CanKarmaUp && x.Rating > 0);
                        if (objRaise != null)
                        {
                            int intBefore = objRaise.KarmaPoints;
                            await objRaise.KarmaUpCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            Console.WriteLine("create karma+1 " + objRaise.Name + ": " + intBefore + " -> " + objCreate.AllSkills.First(x => x.Skill == objRaise.Skill).KarmaPoints);
                            Save(objWindow, strOut, "09b-skills-create-raised");
                        }
                    }
                    // ===== end skills create =====
                }
                return 0;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        Console.WriteLine("screenshots in " + strOut);
        return 0;
    }

    // ===== Skills section steps =====
    private static async Task SkillsSteps(MainWindow objWindow, MainWindowViewModel objViewModel, CharacterViewModel objCharacter, string strOut)
    {
        objCharacter.ShowSectionCommand.Execute(CharacterSection.Skills);
        await Settle();
        Save(objWindow, strOut, "08a-skills-by-pool");
        Console.WriteLine("skills by pool: " + string.Join(", ", objCharacter.SkillRows.OfType<SkillViewModel>().Take(8).Select(x => x.Name + " " + x.PoolText)));
        objCharacter.SortSkillsCommand.Execute(SkillSortMode.Name);
        await Settle();
        Save(objWindow, strOut, "08b-skills-by-name");
        objCharacter.SortSkillsCommand.Execute(SkillSortMode.DicePool);
        objCharacter.GroupSkillsByCategory = true;
        await Settle();
        Save(objWindow, strOut, "08c-skills-grouped");
        objCharacter.GroupSkillsByCategory = false;
        await Settle();
        // Career edits: improve the cheapest knowledge skill, then buy a specialization if karma allows.
        SkillViewModel objKnow = objCharacter.AllSkills.Where(x => x.IsKnowledge && x.CanImprove).OrderBy(x => x.ImproveCost).FirstOrDefault();
        if (objKnow != null)
        {
            string strBefore = objCharacter.Karma;
            await objKnow.ImproveCommand.ExecuteAsync(null);
            await Settle();
            await Settle();
            SkillViewModel objAfter = objCharacter.AllSkills.First(x => x.Skill == objKnow.Skill);
            Console.WriteLine("improve knowledge " + objKnow.Name + ": " + objKnow.Rating + " -> " + objAfter.Rating + ", karma " + strBefore + " -> " + objCharacter.Karma
                              + " // expense: " + objCharacter.Character.ExpenseEntries.ToList().OrderByDescending(x => x.Date).First().Reason);
        }
        SkillViewModel objSpec = objCharacter.AllSkills.FirstOrDefault(x => x.IsCareer && x.ShowAddSpec && x.CanAddSpec && x.Rating > 0);
        if (objSpec != null)
        {
            string strBefore = objCharacter.Karma;
            await objCharacter.RunSkillRuleAsync(t => Chummer.Rules.SkillRules.AddSpecializationCareerAsync(objCharacter.Character, objSpec.Skill,
                (x, _) => Task.FromResult(x.Count > 0 ? x[0].Value.ToString() : "Harness"), t));
            await Settle();
            await Settle();
            Console.WriteLine("add spec to " + objSpec.Name + ": " + objCharacter.AllSkills.First(x => x.Skill == objSpec.Skill).Specialization + ", karma " + strBefore + " -> " + objCharacter.Karma);
        }
        else
            Console.WriteLine("add spec: no affordable specialization at karma " + objCharacter.Karma);
        Save(objWindow, strOut, "08d-skills-after-edit");
    }
    // ===== end skills steps =====
    // ===== Qualities & Abilities editing (QualityRules / MagicRules) =====
    private static string DataId(Character objCharacter, string strFile, string strXPath, string strName)
        => objCharacter.LoadDataXPath(strFile).SelectSingleNode(strXPath + "[name = " + strName.CleanXPath() + "]/id")?.Value;

    private static async Task SaveDialog(Avalonia.Controls.Window objDialog, string strDir, string strName)
    {
        objDialog.Show();
        await Settle();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        objDialog.CaptureRenderedFrame()?.Save(Path.Combine(strDir, strName + ".png"));
        Console.WriteLine("saved " + strName);
        objDialog.Close();
        await Settle();
    }

    private static async Task QualitiesAbilitiesDemo(MainWindow objWindow, CharacterViewModel objCharacter, string strOut, string strPrefix)
    {
        Character objChar = objCharacter.Character;
        objCharacter.ShowSectionCommand.Execute(CharacterSection.Qualities);
        await Settle();
        Save(objWindow, strOut, strPrefix + "a-qualities");
        await SaveDialog(new Chummer.Desktop.Views.Dialogs.QualityPickerWindow(objChar), strOut, strPrefix + "b-quality-picker");
        string strName = new[] { "Analytical Mind", "Catlike", "Bilingual", "Photographic Memory" }.FirstOrDefault(x => objChar.Qualities.All(y => y.Name != x));
        string strKarma = objCharacter.Karma;
        await objCharacter.RunRuleAsync(t => Chummer.Rules.QualityRules.AddAsync(objChar, DataId(objChar, "qualities.xml", "/chummer/qualities/quality", strName), 1, false, t));
        await Settle();
        await Settle();
        Console.WriteLine("add quality " + strName + ": karma " + strKarma + " -> " + objCharacter.Karma);
        QualityViewModel objLevel = objCharacter.PositiveQualities.Concat(objCharacter.NegativeQualities).FirstOrDefault(x => x.CanLevelUp);
        if (objLevel != null)
        {
            strKarma = objCharacter.Karma;
            await objLevel.LevelUpCommand.ExecuteAsync(null);
            await Settle();
            await Settle();
            Console.WriteLine("level up " + objLevel.Name + " " + objLevel.LevelText + ": karma " + strKarma + " -> " + objCharacter.Karma);
        }
        QualityViewModel objBuyOff = objCharacter.NegativeQualities.Where(x => x.CanRemove).OrderBy(x => x.RemovalCost).FirstOrDefault();
        if (objBuyOff != null)
        {
            strKarma = objCharacter.Karma;
            await objBuyOff.RemoveCommand.ExecuteAsync(null);
            await Settle();
            await Settle();
            Console.WriteLine("remove " + objBuyOff.Name + " (" + objBuyOff.RemoveTip + "): karma " + strKarma + " -> " + objCharacter.Karma);
        }
        Save(objWindow, strOut, strPrefix + "c-qualities-edited");

        objCharacter.ShowSectionCommand.Execute(CharacterSection.Abilities);
        await Settle();
        Save(objWindow, strOut, strPrefix + "d-abilities");
        if (objChar.MagicianEnabled)
        {
            await SaveDialog(new Chummer.Desktop.Views.Dialogs.SpellPickerWindow(objChar, false), strOut, strPrefix + "e-spell-picker");
            string strSpell = new[] { "Clout", "Fireball", "Detect Life", "Armor" }.FirstOrDefault(x => objChar.Spells.All(y => y.Name != x));
            strKarma = objCharacter.Karma;
            await objCharacter.RunRuleAsync(t => Chummer.Rules.MagicRules.LearnSpellAsync(objChar, DataId(objChar, "spells.xml", "/chummer/spells/spell", strSpell), token: t));
            await Settle();
            Console.WriteLine("learn spell " + strSpell + ": karma " + strKarma + " -> " + objCharacter.Karma);
            System.Collections.Generic.List<ListItem> lstTypes = Chummer.Selection.MagicSelectors.SpiritTypes(objChar, Chummer.Backend.Enums.SpiritType.Spirit);
            Console.WriteLine("spirit types: " + string.Join(", ", lstTypes.Select(x => x.Name)));
            await objCharacter.RunRuleAsync(t => Chummer.Rules.MagicRules.AddSpiritAsync(objChar, Chummer.Backend.Enums.SpiritType.Spirit, lstTypes.Count > 0 ? lstTypes[0].Value.ToString() : string.Empty, t));
            await Settle();
        }
        if (objChar.MAGEnabled || objChar.RESEnabled)
        {
            await SaveDialog(new Chummer.Desktop.Views.Dialogs.InitiationWindow(objChar), strOut, strPrefix + "f-initiation");
            strKarma = objCharacter.Karma;
            await objCharacter.RunRuleAsync(t => Chummer.Rules.MagicRules.InitiateAsync(objChar, false, false, false, t));
            await Settle();
            Console.WriteLine("initiate: grade " + objChar.InitiateGrade + ", karma " + strKarma + " -> " + objCharacter.Karma);
        }
        if (objChar.AdeptEnabled)
        {
            await objCharacter.RunRuleAsync(t => Chummer.Rules.MagicRules.AddPowerAsync(objChar, DataId(objChar, "powers.xml", "/chummer/powers/power", "Combat Sense"), t));
            await Settle();
        }
        await Settle();
        Save(objWindow, strOut, strPrefix + "g-abilities-edited");
    }
    // ===== end Qualities & Abilities =====

    private static async Task Settle()
    {
        for (int i = 0; i < 20; ++i)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Save(MainWindow objWindow, string strDir, string strName)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap objFrame = objWindow.CaptureRenderedFrame();
        objFrame?.Save(Path.Combine(strDir, strName + ".png"));
        Console.WriteLine("saved " + strName);
    }
}
