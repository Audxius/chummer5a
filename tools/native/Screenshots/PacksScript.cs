using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Chummer;
using Chummer.Backend.Enums;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using Chummer.Selection;

internal static class PacksScript
{
    private sealed class ConsoleProgress : IProgress<string> { public void Report(string value) => Console.WriteLine(value); }
    public static async Task RunAsync(MainWindow window, MainWindowViewModel shell, string output, Func<Task> settle)
    {
        const string key = "packs-review.xml";
        using var template = new CharacterSettings();
        var settings = new CharacterSettings(template, false, key) { BuildMethod = CharacterBuildMethod.Karma, BuildKarma = 800 };
        settings.BooksWritable.Add("SR5"); settings.OnPropertyChanged(nameof(CharacterSettings.Books));
        SettingsManager.LoadedCharacterSettingsAsModifiable[key] = settings;
        var character = new Character { SettingsKey = key, Alias = "PACKS test" };
        await Task.Run(async () =>
        {
            var selector = new MetatypeSelector(character); selector.OnHeritageOrTalentChanged();
            selector.Metatype = selector.GetMetatypes().First(x => selector.GetDetails(x.Value.ToString()).Name == "Human").Value.ToString();
            selector.OnMetatypeChanged();
            if (!await selector.ApplyAsync()) throw new InvalidOperationException("Metatype selection failed.");
        });
        await CharacterRegistry.OpenCharacters.AddAsync(character); await shell.OpenCharacterAsync(character);
        var runner = (CharacterViewModel)shell.CurrentPage;
        var spellDesign = new CustomSpellDesign { Name = "Prismatic burst", Range = "LOS", Area = true, NumberOfEffects = 2 };
        spellDesign.SetModifier(3, true); spellDesign.SetModifier(4, true);
        var spellPicker = new CustomSpellWindow(spellDesign); spellPicker.Show(); await settle();
        Save(spellPicker, output, "22a-custom-spell-designer"); spellPicker.Close();
        character.IgnoreRules = true;
        await runner.RunRuleAsync(t => CustomSpellRules.AddAsync(character, spellDesign, t));
        character.IgnoreRules = false;
        runner.ShowSectionCommand.Execute(CharacterSection.Abilities); await settle();
        Save(window, output, "22b-custom-spell");
        var kits = await Task.Run(() => PacksCatalog.LoadAsync(character));
        var picker = new PacksWindow(kits); picker.Search("Intro Runner"); picker.Show(); await settle();
        Save(picker, output, "21a-packs-picker"); picker.Close();
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Intro Runner Pack", "Core Packs", t, new ConsoleProgress()));
        runner.ShowSectionCommand.Execute(CharacterSection.Inventory); await settle();
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Glasses");
        var glasses = character.Gear.Single(x => x.Name == "Glasses");
        await runner.RunRuleAsync(async t =>
        {
            var location = await EquipmentLocationRules.CreateAsync(character, glasses, "Backpack", t);
            return await EquipmentLocationRules.AssignAsync(character, glasses, location, t);
        });
        await settle(); Save(window, output, "21b-packs-inventory");
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Basic Combat Eyes Pack", "Cyber Packs", t));
        await settle(); Save(window, output, "21c-packs-implants");
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Racing Bike Pack", "Vehicle Packs", t));
        await settle(); Save(window, output, "21d-packs-vehicle");
        if (character.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Quantity != 100 || character.Vehicles.Count != 1)
            throw new InvalidOperationException("PACKS items were not granted correctly.");
        Console.WriteLine("PACKS grants verified: " + character.Gear.Count + " gear, " + character.Weapons.Count + " weapons, " + character.Cyberware.Count + " implants, " + character.Vehicles.Count + " vehicles.");
    }
    private static void Save(Window window, string directory, string name)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"));
        Console.WriteLine("saved " + name);
    }
}
