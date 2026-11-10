using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Chummer;
using Chummer.Backend.Enums;
using Chummer.Backend.Equipment;
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
        var vehicle = character.Vehicles.Single();
        var mountPicker = new WeaponMountWindow(character, vehicle, _ => Task.FromResult(new PurchaseResult(RuleResult.Cancelled)));
        mountPicker.Show(); await mountPicker.LoadAsync(); await settle();
        if (mountPicker.Quote == null) throw new InvalidOperationException("Weapon mount preview was empty.");
        var mountConfiguration = mountPicker.Configuration;
        Save(mountPicker, output, "24a-weapon-mount-builder"); mountPicker.Close();
        WeaponMount mount = null;
        await runner.RunRuleAsync(async t =>
        {
            var purchase = await WeaponMountRules.PurchaseAsync(character, vehicle, mountConfiguration, t);
            mount = purchase.Item as WeaponMount; return purchase.Result;
        });
        if (mount == null) throw new InvalidOperationException("Weapon mount purchase failed.");
        await settle();
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).SelectMany(x => x.Children).First(x => x.IconKey == "Icon.Item.WeaponMount");
        await settle(); Save(window, output, "24b-weapon-mount-inventory");
        string pistol = character.LoadData("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[name='Ares Predator V']/id").InnerText;
        await runner.RunRuleAsync(async t => (await PurchaseRules.PurchaseAsync(character,
            new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = pistol, Parent = mount }, t)).Result);
        await settle(); Save(window, output, "24c-mounted-weapon");
        if (mount.Weapons.Count != 1) throw new InvalidOperationException("Mounted weapon was not attached.");
        settings.BooksWritable.Add("R5"); settings.OnPropertyChanged(nameof(CharacterSettings.Books));
        var modStore = StoreWindow.Create(character, new[] { new StoreSection(EquipmentKind.WeaponMountMod, "Mount modifications", mount) }, null);
        modStore.Show(); await settle(); Save(modStore, output, "24d-mount-mod-store"); modStore.Close();
        await runner.RunRuleAsync(async t => (await PurchaseRules.PurchaseAsync(character,
            new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, ItemId = "6741d080-0e40-4941-a7b5-85ab3e1a9cb2", Parent = mount }, t)).Result);
        await settle(); Save(window, output, "24e-mount-mod-inventory");
        if (mount.Mods.Count != 1) throw new InvalidOperationException("Mount modification was not attached.");
        var retrofit = new WeaponMountWindow(character, vehicle, async configuration =>
        {
            PurchaseResult result = null;
            await runner.RunRuleAsync(async t => { result = await WeaponMountRules.EditAsync(character, mount, configuration, t); return result.Result; });
            return result;
        }, mount);
        retrofit.Show(); await retrofit.LoadAsync();
        var retrofitInput = retrofit.Configuration; retrofitInput.ControlId = "70f99fae-470f-442c-a607-ef3465e27f37";
        await retrofit.ConfigureAsync(retrofitInput); await settle();
        if (retrofit.Quote?.Cost != -500m) throw new InvalidOperationException("Mount retrofit did not quote the expected downgrade difference.");
        Save(retrofit, output, "30a-mount-retrofit"); await retrofit.SaveAsync(); await settle(); Save(window, output, "30b-retrofitted-mount");
        var exporter = new PacksExportWindow(character); exporter.Show(); await exporter.GenerateAsync("Street starter kit"); await settle();
        Save(exporter, output, "25-packs-export");
        if (!exporter.Output.Contains("Street starter kit")) throw new InvalidOperationException("PACKS export was empty.");
        exporter.Close();
        var limits = new LimitModifiersWindow(runner); limits.Show(); await limits.ReloadAsync();
        await limits.SaveAsync("Careful aim", "Physical", 2, "While aiming"); await settle();
        Save(limits, output, "26-limit-modifiers"); limits.Close();
        if (!character.LimitModifiers.Any(x => x.Name == "Careful aim")) throw new InvalidOperationException("Limit modifier was not saved.");
        var natural = new NaturalWeaponWindow(runner); natural.Show(); await natural.LoadAsync(); await settle();
        if (natural.SelectedSkill != "Unarmed Combat") throw new InvalidOperationException("Default natural-weapon skill was not selected.");
        Save(natural, output, "27a-natural-weapon-builder");
        await natural.SaveAsync(new NaturalWeaponDesign { Name = "Talons", DamageBase = "({STR})", DamageModifier = 2, ArmorPenetration = -3, Reach = 1 });
        await settle(); Save(window, output, "27b-natural-weapon-inventory");
        if (!character.Weapons.Any(x => x.Name == "Talons")) throw new InvalidOperationException("Natural weapon was not saved.");
        var suites = new CyberwareSuiteWindow(runner); suites.Show(); await suites.LoadAsync(); await suites.CreateAsync("Combat eyes bundle"); await settle();
        Save(suites, output, "28-cyberware-suite"); suites.Close();
        var recipe = new CustomDrugDesign { Name = "Clear thinker" };
        recipe.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" });
        recipe.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Smoothtalk", Level = 1 });
        var drugBuilder = new CustomDrugWindow(runner); drugBuilder.Show(); await drugBuilder.ConfigureAsync(recipe); await settle();
        if (drugBuilder.Quote == null) throw new InvalidOperationException("Custom drug preview failed.");
        Save(drugBuilder, output, "29a-custom-drug-builder"); await drugBuilder.PurchaseAsync(); await settle();
        if (!character.Drugs.Any(x => x.Name == "Clear thinker")) throw new InvalidOperationException("Custom drug purchase failed.");
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Clear thinker");
        await settle(); Save(window, output, "29b-custom-drug-inventory");
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
