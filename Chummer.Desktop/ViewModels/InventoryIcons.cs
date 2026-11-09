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

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Picks a specific inventory icon ("Icon.Item.*", see tools/native/icons.txt) from an item's data category
    /// (Chummer/data/*.xml), with keyword overrides on the English name where a category is too broad.
    /// </summary>
    public static class InventoryIcons
    {
        private static bool Has(string strName, params string[] astrWords)
        {
            foreach (string strWord in astrWords)
            {
                if (strName.IndexOf(strWord, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Keyword overrides shared by every kind of item (a knife is a knife whether it is a weapon or a cyber-implant).</summary>
        private static string ByName(string strName)
        {
            if (Has(strName, "Katana", "Nodachi", "Wakizashi"))
                return "Icon.Item.Katana";
            if (Has(strName, "Throwing Knife", "Throwing Syringe"))
                return "Icon.Item.ThrowingKnife";
            if (Has(strName, "Shuriken", "Chakram"))
                return "Icon.Item.Shuriken";
            if (Has(strName, "Sword", "Rapier", "Sabre", "Saber", "Claymore", "Katzbalger", "Macuahuitl", "Blade:", "Machete"))
                return "Icon.Item.Sword";
            if (Has(strName, "Knife", "Dagger", "Stiletto", "Shiv", "Bayonet", "Fineblade", "Razor", "Spur"))
                return "Icon.Item.Knife";
            if (Has(strName, "Axe", "Hatchet", "Tomahawk", "Halberd", "Scythe", "Pick"))
                return "Icon.Item.Axe";
            if (Has(strName, "Claw"))
                return "Icon.Item.CyberClaws";
            if (Has(strName, "Knucks", "Knuckle"))
                return "Icon.Item.KnuckleDuster";
            if (Has(strName, "Whip", "Garrote"))
                return "Icon.Item.Whip";
            if (Has(strName, "Spear", "Pole Arm", "Lance", "Harpoon", "Javelin", "Pitchfork"))
                return "Icon.Item.Spear";
            if (Has(strName, "Staff"))
                return "Icon.Item.Staff";
            if (Has(strName, "Baseball", "Bat"))
                return "Icon.Item.Bat";
            if (Has(strName, "Shield"))
                return "Icon.Item.Shield";
            if (Has(strName, "Smoke"))
                return "Icon.Item.SmokeGrenade";
            if (Has(strName, "Flash-Bang", "Flash-Pak", "Flashbang", "Stun Grenade"))
                return "Icon.Item.FlashGrenade";
            if (Has(strName, "Grenade", "Molotov"))
                return "Icon.Item.Grenade";
            if (Has(strName, "Missile", "Rocket", "Torpedo"))
                return "Icon.Item.Launcher";
            return null;
        }

        public static string ForWeapon(string strCategory, string strName)
        {
            strName = strName ?? string.Empty;
            switch (strCategory ?? string.Empty)
            {
                case "Holdouts":
                    return "Icon.Item.Holdout";
                case "Light Pistols":
                    return "Icon.Item.LightPistol";
                case "Heavy Pistols":
                    return Has(strName, "Revolver", "Ruger", "Cavalier Deputy", "Colt Manhunter") ? "Icon.Item.Revolver" : "Icon.Item.HeavyPistol";
                case "Machine Pistols":
                    return "Icon.Item.MachinePistol";
                case "Submachine Guns":
                    return "Icon.Item.Smg";
                case "Assault Rifles":
                    return "Icon.Item.AssaultRifle";
                case "Carbines":
                    return "Icon.Item.Carbine";
                case "Sporting Rifles":
                    return "Icon.Item.SportingRifle";
                case "Sniper Rifles":
                    return "Icon.Item.SniperRifle";
                case "Shotguns":
                    return "Icon.Item.Shotgun";
                case "Light Machine Guns":
                case "Medium Machine Guns":
                case "Heavy Machine Guns":
                    return "Icon.Item.MachineGun";
                case "Assault Cannons":
                    return "Icon.Item.AssaultCannon";
                case "Grenade Launchers":
                case "Missile Launchers":
                    return "Icon.Item.Launcher";
                case "Flamethrowers":
                    return "Icon.Item.Flamethrower";
                case "Laser Weapons":
                    return "Icon.Item.LaserWeapon";
                case "Tasers":
                    return "Icon.Item.Taser";
                case "Bows":
                    return "Icon.Item.Bow";
                case "Crossbows":
                    return Has(strName, "Harpoon") ? "Icon.Item.Spear" : "Icon.Item.Crossbow";
                case "Underbarrel Weapons":
                    return Has(strName, "Shotgun") ? "Icon.Item.Shotgun" : ByName(strName) ?? "Icon.Item.Launcher";
                case "Unarmed":
                    return Has(strName, "Shock") ? "Icon.Item.Taser" : ByName(strName) ?? "Icon.Item.Fist";
                case "Blades":
                    return ByName(strName) ?? "Icon.Item.Sword";
                case "Clubs":
                    if (Has(strName, "Stun"))
                        return "Icon.Item.Monofilament";
                    return ByName(strName) ?? "Icon.Item.Club";
                case "Exotic Melee Weapons":
                    if (Has(strName, "Chainsaw", "Mono"))
                        return "Icon.Item.Monofilament";
                    return ByName(strName) ?? "Icon.Item.Club";
                case "Exotic Ranged Weapons":
                    if (Has(strName, "Grapple"))
                        return "Icon.Item.Grapple";
                    return ByName(strName) ?? "Icon.Item.LaserWeapon";
                case "Bio-Weapon":
                case "Cyberweapon":
                case "Quality":
                    return ByName(strName) ?? "Icon.Item.CyberClaws";
                case "Improvised Weapons":
                    return ByName(strName) ?? "Icon.Item.Bat";
                case "Micro-Drone Weapons":
                    return "Icon.Item.WeaponMount";
                case "Gear":
                    return ByName(strName) ?? "Icon.Item.Grenade";
                default:
                    return ByName(strName) ?? "Icon.Weapon";
            }
        }

        public static string ForArmor(string strCategory, string strName)
        {
            strName = strName ?? string.Empty;
            if (Has(strName, "Helmet", "Helm"))
                return "Icon.Item.Helmet";
            if (Has(strName, "Shield"))
                return "Icon.Item.Shield";
            if (Has(strName, "Full Body", "Body Armor (Full", "Milspec", "Military Grade", "Hardened"))
                return "Icon.Item.FullBodyArmor";
            if (Has(strName, "Hazmat", "Chemsuit", "Chem Suit", "Survival Suit"))
                return "Icon.Item.HazmatSuit";
            if (Has(strName, "Mask"))
                return "Icon.Item.Mask";
            if (Has(strName, "Glove"))
                return "Icon.Item.Gloves";
            if (Has(strName, "Shoe", "Boot", "Sock"))
                return "Icon.Item.Shoes";
            if (Has(strName, "Pants", "Trousers", "Jeans", "Leggings"))
                return "Icon.Item.Trousers";
            if (Has(strName, "Coat", "Duster", "Trench", "Cloak", "Poncho", "Cape"))
                return strCategory == "Cloaks" || Has(strName, "Cloak", "Cape", "Poncho") ? "Icon.Item.Cloak" : "Icon.Item.Coat";
            if (Has(strName, "Jacket"))
                return "Icon.Item.Jacket";
            if (Has(strName, "Vest", "Plate"))
                return "Icon.Item.Vest";
            switch (strCategory ?? string.Empty)
            {
                case "Clothing":
                case "High-Fashion Armor Clothing":
                    return "Icon.Item.Clothing";
                case "Cloaks":
                    return "Icon.Item.Cloak";
                case "Specialty Armor":
                    return "Icon.Item.FullBodyArmor";
                default:
                    return "Icon.Item.BodyArmor";
            }
        }

        public static string ForCyberware(string strCategory, string strName, bool blnBioware)
        {
            strName = strName ?? string.Empty;
            if (Has(strName, "Commlink"))
                return "Icon.Item.Commlink";
            if (Has(strName, "Cyberdeck"))
                return "Icon.Item.Cyberdeck";
            if (Has(strName, "Control Rig"))
                return "Icon.Item.Rcc";
            if (Has(strName, "Datajack", "Chipjack", "Skilljack", "Knowsoft", "Data Lock"))
                return "Icon.Item.Chip";
            if (Has(strName, "Smartlink", "Eye", "Vision", "Optic", "Retinal"))
                return "Icon.Item.Eyeware";
            if (Has(strName, "Ear", "Audio", "Hearing"))
                return "Icon.Item.Earware";
            if (Has(strName, "Leg", "Foot", "Feet"))
                return "Icon.Item.Cyberleg";
            if (Has(strName, "Arm", "Hand") && !Has(strName, "Armor"))
                return "Icon.Item.Cyberarm";
            if (Has(strName, "Muscle", "Toner", "Bone"))
                return "Icon.Item.Muscle";
            if (Has(strName, "Lung", "Trache", "Gills", "Oxygen"))
                return "Icon.Item.Lungs";
            if (Has(strName, "Dermal", "Orthoskin", "Armor"))
                return "Icon.Item.BodyArmor";
            if (Has(strName, "Injector"))
                return "Icon.Item.Syringe";
            if (Has(strName, "Cerebral", "Synaptic", "Brain", "Cortex", "Memory", "Reflex Recorder", "Math SPU"))
                return "Icon.Item.Headware";
            if (Has(strName, "Wired Reflexes", "Reaction Enhancers", "Move-by-Wire"))
                return "Icon.Item.Implant";
            string strWeapon = ByName(strName);
            if (strWeapon != null)
                return strWeapon;
            switch (strCategory ?? string.Empty)
            {
                case "Eyeware":
                    return "Icon.Item.Eyeware";
                case "Earware":
                    return "Icon.Item.Earware";
                case "Cyberlimb":
                case "Cyberlimb Accessory":
                case "Cyberlimb Enhancement":
                    return "Icon.Item.Cyberarm";
                case "Headware":
                    return "Icon.Item.Headware";
                case "Bodyware":
                    return "Icon.Item.Bodyware";
                case "Cyber Implant Weapon":
                case "Cyber Implant Weapon Accessory":
                case "Bio-Weapons":
                    return "Icon.Item.CyberClaws";
                case "Hard Nanoware":
                case "Soft Nanoware":
                case "Nanocybernetics":
                    return "Icon.Item.Nanoware";
                case "Auto Injector Mods":
                case "Chemical Gland Modifications":
                    return "Icon.Item.Syringe";
                case "Cultured":
                    return "Icon.Item.Headware";
                case "Orthoskin Upgrades":
                    return "Icon.Item.BodyArmor";
                case "Cosmetic Enhancement":
                case "Cosmetic Bioware":
                case "Biosculpting":
                    return "Icon.Item.Disguise";
                case "Cybersuite":
                case "Special Biodrone Cyberware":
                    return "Icon.Item.Implant";
                default:
                    return blnBioware ? "Icon.Item.Dna" : "Icon.Item.Implant";
            }
        }

        public static string ForGear(string strCategory, string strName)
        {
            strName = strName ?? string.Empty;
            string strLoop = strCategory ?? string.Empty;
            // Names that say exactly what the thing is beat broad categories like "Electronics Accessories" or "Survival Gear".
            if (Has(strName, "Medkit", "Med Kit", "First Aid", "Trauma Patch"))
                return "Icon.Item.Medkit";
            if (Has(strName, "Gas Mask", "Respirator", "Rebreather"))
                return "Icon.Item.GasMask";
            if (Has(strName, "Trodes", "Simrig", "Sim Module"))
                return "Icon.Item.Btl";
            if (Has(strName, "Glasses", "Contacts"))
                return "Icon.Item.Glasses";
            if (Has(strName, "Goggles", "Binoculars"))
                return Has(strName, "Binoculars") ? "Icon.Item.Binoculars" : "Icon.Item.Goggles";
            if (Has(strName, "Smartlink", "Image Link", "Low-Light", "Thermographic", "Vision"))
                return "Icon.Item.Eyeware";
            if (Has(strName, "Earbud", "Earpiece"))
                return "Icon.Item.Earbuds";
            if (Has(strName, "Headphones", "Headjammer"))
                return "Icon.Item.Headphones";
            if (Has(strName, "Camera"))
                return "Icon.Item.Camera";
            if (Has(strName, "Microphone", "Laser Microphone"))
                return "Icon.Item.Microphone";
            if (Has(strName, "Tool Kit", "Toolkit", "Tool Shop", "Tool Facility"))
                return "Icon.Item.Toolbox";
            if (Has(strName, "Credstick", "Certified Cred"))
                return "Icon.Item.Credstick";
            if (Has(strName, "Fake SIN", "SIN"))
                return "Icon.Item.IdCard";
            if (Has(strName, "License"))
                return "Icon.Item.License";
            if (Has(strName, "Spare Clip", "Speed Loader", "Magazine"))
                return "Icon.Item.Magazine";
            bool blnAmmo = strLoop.StartsWith("Ammunition", StringComparison.Ordinal);
            if (blnAmmo && Has(strName, "Arrow", "Bolt"))
                return "Icon.Item.Arrows";
            if (blnAmmo && Has(strName, "Shot", "Slug", "Shell"))
                return "Icon.Item.ShotgunAmmo";
            if (Has(strName, "Rope", "Myomeric", "Climbing"))
                return "Icon.Item.Rope";
            if (Has(strName, "Flashlight", "Flare"))
                return "Icon.Item.Flashlight";
            if (Has(strName, "Lockpick", "Autopicker", "Maglock Passkey", "Keycard"))
                return Has(strName, "Passkey", "Keycard") ? "Icon.Item.Keycard" : "Icon.Item.Lockpick";
            if (Has(strName, "Jammer"))
                return "Icon.Item.Jammer";
            if (Has(strName, "Radio", "Micro-Transceiver", "Transceiver"))
                return "Icon.Item.Radio";
            if (Has(strName, "Satellite", "Satlink"))
                return "Icon.Item.Satellite";
            if (Has(strName, "Tent", "Shelter"))
                return "Icon.Item.Tent";
            if (Has(strName, "Grapple"))
                return "Icon.Item.Grapple";
            if (Has(strName, "Smoke"))
                return "Icon.Item.SmokeGrenade";
            if (Has(strName, "Flash-Bang", "Flash-Pak"))
                return "Icon.Item.FlashGrenade";
            if (Has(strName, "Grenade"))
                return "Icon.Item.Grenade";
            switch (strLoop)
            {
                case "Commlinks":
                case "Commlink/Cyberdeck Form Factors":
                    return "Icon.Item.Commlink";
                case "Commlink Accessories":
                case "Commlink Apps":
                case "Matrix Accessories":
                    return "Icon.Item.Commlink";
                case "Cyberdecks":
                case "Cyberdeck Modules":
                case "Custom Cyberdeck Attributes":
                    return "Icon.Item.Cyberdeck";
                case "Cyberterminals":
                    return "Icon.Item.Terminal";
                case "Rigger Command Consoles":
                    return "Icon.Item.Rcc";
                case "Common Programs":
                case "Hacking Programs":
                case "Software":
                case "Software Tweaks":
                case "Autosofts":
                case "PI-Tac Programs":
                case "Paydata":
                    return "Icon.Item.Software";
                case "Skillsofts":
                case "Booster Chips":
                    return "Icon.Item.Chip";
                case "BTLs":
                    return "Icon.Item.Btl";
                case "Electronics Accessories":
                case "Electronic Modification":
                case "Electronic Parts":
                case "PI-Tac":
                case "Printing":
                case "RFID Tags":
                    return "Icon.Item.Electronics";
                case "Communications and Countermeasures":
                    return "Icon.Item.Jammer";
                case "Audio Devices":
                case "Audio Enhancements":
                    return "Icon.Item.Headphones";
                case "Vision Devices":
                case "Vision Enhancements":
                    return "Icon.Item.Glasses";
                case "Sensors":
                case "Sensor Functions":
                case "Sensor Housings":
                    return "Icon.Sensor";
                case "Security Devices":
                    return "Icon.Item.Camera";
                case "Breaking and Entering Gear":
                    return "Icon.Item.Lockpick";
                case "Ammunition":
                    return "Icon.Item.Bullets";
                case "Explosives":
                    return Has(strName, "Mine", "Claymore") ? "Icon.Item.Mine" : "Icon.Item.Dynamite";
                case "Biotech":
                    return Has(strName, "Patch") ? "Icon.Item.SlapPatch" : "Icon.Item.Medkit";
                case "Drugs":
                case "Custom Drugs":
                case "Drug Grades":
                    return "Icon.Item.Pills";
                case "Toxins":
                case "Chemicals":
                    return "Icon.Item.Toxin";
                case "Alchemical Tools":
                case "Alchemical Preperations":
                case "Magical Compounds":
                    return "Icon.Item.Potion";
                case "Magical Plants":
                    return "Icon.Item.Herbs";
                case "Magical Supplies":
                    return "Icon.Item.Candles";
                case "Formulae":
                    return "Icon.Item.Book";
                case "Foci":
                    return "Icon.Item.Amulet";
                case "Tools":
                case "Tools of the Trade":
                    return "Icon.Item.Toolbox";
                case "Survival Gear":
                    return "Icon.Item.Backpack";
                case "Grapple Gun":
                    return "Icon.Item.Grapple";
                case "ID/Credsticks":
                    return Has(strName, "Cred") ? "Icon.Item.Credstick" : "Icon.Item.IdCard";
                case "Contracts/Upkeep":
                case "Services":
                    return "Icon.Item.License";
                case "Currency":
                    return "Icon.Item.Cash";
                case "Disguises":
                case "Appearance Modification":
                    return "Icon.Item.Disguise";
                case "Food":
                case "Tir Tairngire Beverages":
                    return Has(strName, "Drink", "Beverage", "Wine", "Beer", "Soy") ? "Icon.Item.Drink" : "Icon.Item.Food";
                case "Musical Instruments":
                case "Entertainment":
                    return "Icon.Item.Guitar";
                case "Animals":
                case "Critter Gear":
                    return "Icon.Item.Paw";
                case "Armor Enhancements":
                    return "Icon.Item.Vest";
                case "Nanogear":
                    return "Icon.Item.Nanoware";
                case "Reporter Gear":
                    return "Icon.Item.PhotoCamera";
                case "Extraction Devices":
                    return "Icon.Item.Bag";
                case "Housewares":
                    return "Icon.Item.House";
                default:
                    if (strLoop.IndexOf("Focus", StringComparison.Ordinal) >= 0)
                        return "Icon.Item.Amulet";
                    return "Icon.Item.Backpack";
            }
        }

        public static string ForVehicle(string strCategory, string strName)
        {
            strName = strName ?? string.Empty;
            switch (strCategory ?? string.Empty)
            {
                case "Bikes":
                    return "Icon.Item.Motorcycle";
                case "Cars":
                    return "Icon.Item.Car";
                case "Trucks":
                    if (Has(strName, "Van", "Bulldog", "Roadmaster"))
                        return Has(strName, "Van") ? "Icon.Item.Van" : "Icon.Item.Truck";
                    return Has(strName, "Bus") ? "Icon.Item.Bus" : "Icon.Item.Truck";
                case "Municipal/Construction":
                    return Has(strName, "Bus") ? "Icon.Item.Bus" : "Icon.Item.Construction";
                case "Corpsec/Police/Military":
                    if (Has(strName, "Patrol", "Police", "Interceptor", "Cruiser"))
                        return "Icon.Item.PoliceCar";
                    if (Has(strName, "Roadmaster", "Command Center", "Truck"))
                        return "Icon.Item.Truck";
                    return "Icon.Item.Apc";
                case "Boats":
                    return Has(strName, "Sail", "Yacht") ? "Icon.Item.Sailboat" : "Icon.Item.Speedboat";
                case "Submarines":
                    return "Icon.Item.Submarine";
                case "Fixed-Wing Aircraft":
                    return Has(strName, "Fighter", "Jet", "Strike") ? "Icon.Item.Jet" : "Icon.Item.Airplane";
                case "LTAV":
                    return "Icon.Item.Zeppelin";
                case "Rotorcraft":
                    return "Icon.Item.Helicopter";
                case "VTOL/VSTOL":
                    return "Icon.Item.Vtol";
                case "Drones: Micro":
                    return "Icon.Item.DroneMicro";
                case "Drones: Mini":
                    return "Icon.Item.DroneMini";
                case "Drones: Small":
                    return "Icon.Item.DroneSmall";
                case "Drones: Medium":
                    return "Icon.Item.DroneMedium";
                case "Drones: Anthro":
                    return "Icon.Item.FullBodyArmor";
                case "Drones: Large":
                    return "Icon.Item.DroneLarge";
                case "Drones: Huge":
                    return "Icon.Item.DroneHuge";
                case "Drones: Missile":
                    return "Icon.Item.DroneMissile";
                default:
                    if (strCategory?.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Icon.Item.DroneSmall";
                    return "Icon.Item.Car";
            }
        }

        public static string ForLifestyle(string strBaseLifestyle, string strName)
        {
            string strAll = (strBaseLifestyle ?? string.Empty) + " " + (strName ?? string.Empty);
            if (Has(strAll, "Grid", "Subscription", "Matrix"))
                return "Icon.Item.Wifi";
            if (Has(strAll, "Street"))
                return "Icon.Item.Streets";
            if (Has(strAll, "Squatter", "Low", "Hospitalized", "Traveler"))
                return "Icon.Item.Hovel";
            if (Has(strAll, "Luxury"))
                return "Icon.Item.Mansion";
            if (Has(strAll, "High"))
                return "Icon.Item.Apartment";
            if (Has(strAll, "Entertainment", "Asset", "Service"))
                return "Icon.Item.Guitar";
            return "Icon.Item.House";
        }

        public static string ForDrug(string strName) => "Icon.Item.Pills";
    }
}
