/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public static class EquipmentLocationRules
    {
        /// <summary>Locations organize top-level items; assigning one never reparents a plugin or installed component.</summary>
        public static ICollection<Location> LocationsFor(Character character, object item)
        {
            if (!(item is IHasCharacterObject owned) || !ReferenceEquals(owned.CharacterObject, character)) return null;
            switch (item)
            {
                case Gear gear when character.Gear.Contains(gear): return character.GearLocations;
                case Gear gear when gear.Parent is Vehicle vehicle && character.Vehicles.Contains(vehicle) && vehicle.GearChildren.Contains(gear): return vehicle.Locations;
                case Weapon weapon when character.Weapons.Contains(weapon) && weapon.Parent == null && weapon.ParentVehicle == null && string.IsNullOrEmpty(weapon.ParentID): return character.WeaponLocations;
                case Armor armor when character.Armor.Contains(armor): return character.ArmorLocations;
                case Vehicle vehicle when character.Vehicles.Contains(vehicle): return character.VehicleLocations;
                default: return null;
            }
        }

        public static async Task<RuleResult> AssignAsync(Character character, object item, Location target, CancellationToken token = default)
        {
            var locations = LocationsFor(character, item);
            if (locations == null || !(item is IHasLocation located) || target != null && !locations.Contains(target)) return RuleResult.NotAllowed;
            Location previous = located.Location;
            if (ReferenceEquals(previous, target)) return RuleResult.Cancelled;
            token.ThrowIfCancellationRequested();
            if (previous != null) await previous.Children.RemoveAsync(located, token).ConfigureAwait(false);
            try
            {
                if (target != null) await target.Children.AddAsync(located, token).ConfigureAwait(false);
                else located.Location = null;
            }
            catch
            {
                if (previous != null && !previous.Children.Contains(located)) await previous.Children.AddAsync(located, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return RuleResult.Changed;
        }

        public static async Task<Location> CreateAsync(Character character, object item, string name, CancellationToken token = default)
        {
            var locations = LocationsFor(character, item);
            if (locations == null || string.IsNullOrWhiteSpace(name)) return null;
            token.ThrowIfCancellationRequested();
            var location = new Location(character, locations, name.Trim()) { SortOrder = locations.Count == 0 ? 0 : locations.Max(x => x.SortOrder) + 1 };
            if (locations is IAsyncCollection<Location> asyncLocations) await asyncLocations.AddAsync(location, token).ConfigureAwait(false);
            else locations.Add(location);
            return location;
        }

        public static RuleResult Rename(Character character, object item, Location location, string name)
        {
            var locations = LocationsFor(character, item);
            if (locations == null || location == null || !locations.Contains(location) || string.IsNullOrWhiteSpace(name)) return RuleResult.NotAllowed;
            if (location.Name == name.Trim()) return RuleResult.Cancelled;
            location.Name = name.Trim(); return RuleResult.Changed;
        }

        public static async Task<RuleResult> RemoveAsync(Character character, object item, Location location, CancellationToken token = default)
        {
            var locations = LocationsFor(character, item);
            if (locations == null || location == null || !locations.Contains(location)) return RuleResult.NotAllowed;
            return await location.RemoveAsync(token: token).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.Cancelled;
        }
    }
}
