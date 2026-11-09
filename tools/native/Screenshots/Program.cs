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
                        // Clicking a skill opens its roll in the Dice Terminal.
                        SkillViewModel objSkill = objCharacter.SkillCategories.SelectMany(x => x.AllSkills).FirstOrDefault(x => x.HasSpecPool)
                                                  ?? objCharacter.SkillCategories.SelectMany(x => x.AllSkills).FirstOrDefault(x => x.IsRollable);
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
                return 0;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        Console.WriteLine("screenshots in " + strOut);
        return 0;
    }

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
