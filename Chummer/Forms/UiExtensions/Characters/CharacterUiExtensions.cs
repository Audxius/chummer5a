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
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;
using Chummer.Annotations;
using Chummer.Backend.Attributes;
using Chummer.Backend.Enums;
using Chummer.Backend.Equipment;
using Chummer.Backend.Skills;
using Chummer.Backend.Uniques;
using Chummer.Plugins;
using Microsoft.ApplicationInsights;
using Microsoft.IO;
using Newtonsoft.Json;
using NLog;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Character"/>, moved out of the engine for the native port.
    /// </summary>
    public static class CharacterUiExtensions
    {
        /// <summary>
        /// Move a Gear TreeNode after Drag and Drop, changing its parent.
        /// </summary>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="objGearNode">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveGearParent(this Chummer.Character objThis, TreeNode objDestination, TreeNode objGearNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objGearNode == null || objDestination == null)
                return;
            // The item cannot be dropped onto itself or onto one of its children.
            for (TreeNode objCheckNode = objDestination;
                objCheckNode != null && objCheckNode.Level >= objDestination.Level;
                objCheckNode = objCheckNode.Parent)
                if (objCheckNode == objGearNode)
                    return;
            if (!(objGearNode.Tag is Gear objGear))
                return;

            // Gear cannot be moved to one if its children.
            bool blnAllowMove = true;
            if (objDestination.Level > 0)
            {
                TreeNode objFindNode = objDestination;
                do
                {
                    objFindNode = objFindNode.Parent;
                    if (objFindNode.Tag == objGear)
                    {
                        blnAllowMove = false;
                        break;
                    }
                } while (objFindNode.Level > 0);
            }

            if (!blnAllowMove)
                return;

            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                // Remove the Gear from the character.
                if (objGear.Parent is IHasChildren<Gear> parent)
                    parent.Children.Remove(objGear);
                else
                    objThis.Gear.Remove(objGear);

                switch (objDestination.Tag)
                {
                    case Location objLocation:
                        // The Gear was moved to a location, so add it to the character instead.
                        objGear.Location = objLocation;
                        objLocation.Children.Add(objGear);
                        objThis.Gear.Add(objGear);
                        break;

                    case Gear objParent:
                        // Add the Gear as a child of the destination Node and clear its location.
                        objGear.Location = null;
                        objParent.Children.Add(objGear);
                        break;
                }
            }
        }

        /// <summary>
        /// Move a Gear TreeNode after Drag and Drop, changing its parent.
        /// </summary>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="objGearNode">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveGearParentAsync(this Chummer.Character objThis, TreeNode objDestination, TreeNode objGearNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objGearNode == null || objDestination == null)
                return;
            // The item cannot be dropped onto itself or onto one of its children.
            for (TreeNode objCheckNode = objDestination;
                objCheckNode != null && objCheckNode.Level >= objDestination.Level;
                objCheckNode = objCheckNode.Parent)
                if (objCheckNode == objGearNode)
                    return;
            if (!(objGearNode.Tag is Gear objGear))
                return;

            // Gear cannot be moved to one if its children.
            bool blnAllowMove = true;
            if (objDestination.Level > 0)
            {
                TreeNode objFindNode = objDestination;
                do
                {
                    objFindNode = objFindNode.Parent;
                    if (objFindNode.Tag == objGear)
                    {
                        blnAllowMove = false;
                        break;
                    }
                } while (objFindNode.Level > 0);
            }

            if (!blnAllowMove)
                return;

            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // Remove the Gear from the character.
                if (objGear.Parent is IHasChildren<Gear> parent)
                    await parent.Children.RemoveAsync(objGear, token).ConfigureAwait(false);
                else
                    await objThis.Gear.RemoveAsync(objGear, token).ConfigureAwait(false);

                switch (objDestination.Tag)
                {
                    case Location objLocation:
                        // The Gear was moved to a location, so add it to the character instead.
                        objGear.Location = objLocation;
                        await objLocation.Children.AddAsync(objGear, token).ConfigureAwait(false);
                        await objThis.Gear.AddAsync(objGear, token).ConfigureAwait(false);
                        break;

                    case Gear objParent:
                        // Add the Gear as a child of the destination Node and clear its location.
                        objGear.Location = null;
                        await objParent.Children.AddAsync(objGear, token).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Gear TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveGearNode(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null || nodeToMove == null)
                return;
            if (!(nodeToMove.Tag is Gear objGear))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            nodeToMove.TreeView.DoThreadSafe(nodeToMove.Remove, token);
                        else
                            nodeToMove.Remove();
                        objGear.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            objNewParent.TreeView.DoThreadSafe(() => objNewParent.Nodes.Insert(0, nodeToMove), token);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objGear.Location = null;
                        intNewIndex = Math.Min(intNewIndex, objThis.Gear.Count - 1);
                        objThis.Gear.Move(objThis.Gear.IndexOf(objGear), intNewIndex);
                        break;
                }
            }
        }

        /// <summary>
        /// Move a Gear TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveGearNodeAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null || nodeToMove == null)
                return;
            if (!(nodeToMove.Tag is Gear objGear))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            await nodeToMove.TreeView.DoThreadSafeAsync(nodeToMove.Remove, token).ConfigureAwait(false);
                        else
                            nodeToMove.Remove();
                        objGear.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            await objNewParent.TreeView.DoThreadSafeAsync(() =>
                                objNewParent.Nodes.Insert(0, nodeToMove), token).ConfigureAwait(false);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objGear.Location = null;
                        intNewIndex = Math.Min(intNewIndex, await objThis.Gear.GetCountAsync(token).ConfigureAwait(false) - 1);
                        await objThis.Gear.MoveAsync(await objThis.Gear.IndexOfAsync(objGear, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Gear Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of gear location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveGearRoot(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                int intOldIndex = objThis.GearLocations.IndexOf(objLocation);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, objThis.GearLocations.Count, ref intNewIndex))
                    objThis.GearLocations.Move(intOldIndex, intNewIndex);
            }
        }

        /// <summary>
        /// Move a Gear Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of gear location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveGearRootAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                int intOldIndex = await objThis.GearLocations.IndexOfAsync(objLocation, token).ConfigureAwait(false);
                int intCount = await objThis.GearLocations.GetCountAsync(token).ConfigureAwait(false);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, intCount, ref intNewIndex))
                    await objThis.GearLocations.MoveAsync(intOldIndex, intNewIndex, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Lifestyle TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodLifestyleNode">Node of lifestyle to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveLifestyleNode(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodLifestyleNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodLifestyleNode == null)
                return;
            if (objDestination != null)
            {
                TreeNode objNewParent = objDestination;
                while (objNewParent.Level > 0)
                    objNewParent = objNewParent.Parent;
                intNewIndex = objNewParent.Index;
            }

            if (!(nodLifestyleNode.Tag is Lifestyle objLifestyle))
                return;
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
                objThis.Lifestyles.Move(objThis.Lifestyles.IndexOf(objLifestyle), intNewIndex);
        }

        /// <summary>
        /// Move a Lifestyle TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodLifestyleNode">Node of lifestyle to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveLifestyleNodeAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodLifestyleNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodLifestyleNode == null)
                return;
            if (objDestination != null)
            {
                TreeNode objNewParent = objDestination;
                while (objNewParent.Level > 0)
                    objNewParent = objNewParent.Parent;
                intNewIndex = objNewParent.Index;
            }

            if (!(nodLifestyleNode.Tag is Lifestyle objLifestyle))
                return;
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                await objThis.Lifestyles.MoveAsync(await objThis.Lifestyles.IndexOfAsync(objLifestyle, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move an Armor TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of armor to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveArmorNode(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Armor objArmor))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            nodeToMove.TreeView.DoThreadSafe(nodeToMove.Remove, token);
                        else
                            nodeToMove.Remove();
                        objArmor.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            objNewParent.TreeView.DoThreadSafe(() => objNewParent.Nodes.Insert(0, nodeToMove), token);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objArmor.Location = null;
                        intNewIndex = Math.Min(intNewIndex, objThis.Armor.Count - 1);
                        objThis.Armor.Move(objThis.Armor.IndexOf(objArmor), intNewIndex);
                        break;
                }
            }
        }

        /// <summary>
        /// Move an Armor TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of armor to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveArmorNodeAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Armor objArmor))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            await nodeToMove.TreeView.DoThreadSafeAsync(nodeToMove.Remove, token).ConfigureAwait(false);
                        else
                            nodeToMove.Remove();
                        objArmor.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            await objNewParent.TreeView.DoThreadSafeAsync(() => objNewParent.Nodes.Insert(0, nodeToMove), token).ConfigureAwait(false);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objArmor.Location = null;
                        intNewIndex = Math.Min(intNewIndex, await objThis.Armor.GetCountAsync(token).ConfigureAwait(false) - 1);
                        await objThis.Armor.MoveAsync(await objThis.Armor.IndexOfAsync(objArmor, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move an Armor Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of armor location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveArmorRoot(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                int intOldIndex = objThis.ArmorLocations.IndexOf(objLocation);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, objThis.ArmorLocations.Count, ref intNewIndex))
                    objThis.ArmorLocations.Move(intOldIndex, intNewIndex);
            }
        }

        /// <summary>
        /// Move an Armor Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of armor location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveArmorRootAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                int intOldIndex = await objThis.ArmorLocations.IndexOfAsync(objLocation, token).ConfigureAwait(false);
                int intCount = await objThis.ArmorLocations.GetCountAsync(token).ConfigureAwait(false);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, intCount, ref intNewIndex))
                    await objThis.ArmorLocations.MoveAsync(intOldIndex, intNewIndex, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Weapon TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of weapon to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveWeaponNode(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Weapon objWeapon))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            nodeToMove.TreeView.DoThreadSafe(nodeToMove.Remove, token);
                        else
                            nodeToMove.Remove();
                        objWeapon.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            objNewParent.TreeView.DoThreadSafe(() => objNewParent.Nodes.Insert(0, nodeToMove), token);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objWeapon.Location = null;
                        intNewIndex = Math.Min(intNewIndex, objThis.Weapons.Count - 1);
                        objThis.Weapons.Move(objThis.Weapons.IndexOf(objWeapon), intNewIndex);
                        break;
                }
            }
        }

        /// <summary>
        /// Move a Weapon TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of weapon to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveWeaponNodeAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Weapon objWeapon))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            await nodeToMove.TreeView.DoThreadSafeAsync(nodeToMove.Remove, token).ConfigureAwait(false);
                        else
                            nodeToMove.Remove();
                        objWeapon.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            await objNewParent.TreeView.DoThreadSafeAsync(() => objNewParent.Nodes.Insert(0, nodeToMove), token).ConfigureAwait(false);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objWeapon.Location = null;
                        intNewIndex = Math.Min(intNewIndex, await objThis.Weapons.GetCountAsync(token).ConfigureAwait(false) - 1);
                        await objThis.Weapons.MoveAsync(await objThis.Weapons.IndexOfAsync(objWeapon, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Weapon Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of weapon location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveWeaponRoot(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                int intOldIndex = objThis.WeaponLocations.IndexOf(objLocation);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, objThis.WeaponLocations.Count, ref intNewIndex))
                    objThis.WeaponLocations.Move(intOldIndex, intNewIndex);
            }
        }

        /// <summary>
        /// Move a Weapon Location TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of weapon location to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveWeaponRootAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!(nodOldNode?.Tag is Location objLocation))
                return;
            intNewIndex = GetLocationCollectionIndex(objDestination, intNewIndex);
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                int intOldIndex = await objThis.WeaponLocations.IndexOfAsync(objLocation, token).ConfigureAwait(false);
                int intCount = await objThis.WeaponLocations.GetCountAsync(token).ConfigureAwait(false);
                if (Character.TryGetBoundedMoveIndex(intOldIndex, intCount, ref intNewIndex))
                    await objThis.WeaponLocations.MoveAsync(intOldIndex, intNewIndex, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Vehicle TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of vehicle to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveVehicleNode(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Vehicle objVehicle))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            nodeToMove.TreeView.DoThreadSafe(nodeToMove.Remove, token);
                        else
                            nodeToMove.Remove();
                        objVehicle.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            objNewParent.TreeView.DoThreadSafe(() => objNewParent.Nodes.Insert(0, nodeToMove), token);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objVehicle.Location = null;
                        intNewIndex = Math.Min(intNewIndex, objThis.Vehicles.Count - 1);
                        objThis.Vehicles.Move(objThis.Vehicles.IndexOf(objVehicle), intNewIndex);
                        break;
                }
            }
        }

        /// <summary>
        /// Move a Vehicle TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodeToMove">Node of vehicle to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveVehicleNodeAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodeToMove, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodeToMove?.Tag is Vehicle objVehicle))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0 && !(objNewParent.Tag is Location))
                objNewParent = objNewParent.Parent;

            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                switch (objNewParent.Tag)
                {
                    case Location objLocation:
                        if (nodeToMove.TreeView != null)
                            await nodeToMove.TreeView.DoThreadSafeAsync(nodeToMove.Remove, token).ConfigureAwait(false);
                        else
                            nodeToMove.Remove();
                        objVehicle.Location = objLocation;
                        if (objNewParent.TreeView != null)
                            await objNewParent.TreeView.DoThreadSafeAsync(() => objNewParent.Nodes.Insert(0, nodeToMove), token).ConfigureAwait(false);
                        else
                            objNewParent.Nodes.Insert(0, nodeToMove);
                        break;

                    case string _:
                        objVehicle.Location = null;
                        intNewIndex = Math.Min(intNewIndex, await objThis.Vehicles.GetCountAsync(token).ConfigureAwait(false) - 1);
                        await objThis.Vehicles.MoveAsync(await objThis.Vehicles.IndexOfAsync(objVehicle, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move a Vehicle Gear TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="nodDestination">Destination Node.</param>
        /// <param name="nodGearNode">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveVehicleGearParent(this Chummer.Character objThis, TreeNode nodDestination, TreeNode nodGearNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodDestination == null || nodGearNode == null)
                return;
            // The item cannot be dropped onto itself or onto one of its children.
            for (TreeNode objCheckNode = nodDestination;
                objCheckNode != null && objCheckNode.Level >= nodDestination.Level;
                objCheckNode = objCheckNode.Parent)
                if (objCheckNode == nodGearNode)
                    return;
            if (!(nodGearNode.Tag is IHasInternalId nodeId))
                return;
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                // Locate the currently selected piece of Gear.
                //TODO: Better interface for determining what the parent of a bit of gear is.
                Gear objGear = objThis.Vehicles.FindVehicleGear(nodeId.InternalId, out Vehicle objOldVehicle,
                                                        out WeaponAccessory objOldWeaponAccessory,
                                                        out Cyberware objOldCyberware, token);

                if (objGear == null)
                    return;

                using (objThis.LockObject.EnterWriteLock(token))
                {
                    if (nodDestination.Tag is Gear objDestinationGear)
                    {
                        // Remove the Gear from the Vehicle.
                        if (objGear.Parent is IHasChildren<Gear> parent)
                            parent.Children.Remove(objGear);
                        else if (objOldCyberware != null)
                            objOldCyberware.GearChildren.Remove(objGear);
                        else if (objOldWeaponAccessory != null)
                            objOldWeaponAccessory.GearChildren.Remove(objGear);
                        else
                            objOldVehicle.GearChildren.Remove(objGear);

                        // Add the Gear to its new parent.
                        objGear.Location = null;
                        objDestinationGear.Children.Add(objGear);
                    }
                    else
                    {
                        // Determine if this is a Location.
                        TreeNode nodVehicleNode = nodDestination;
                        Location objLocation = null;
                        while (nodVehicleNode.Level > 1)
                        {
                            if (objLocation is null && nodVehicleNode.Tag is Location loc)
                            {
                                objLocation = loc;
                            }

                            nodVehicleNode = nodVehicleNode.Parent;
                        }

                        // Determine if this is a Location in the destination Vehicle.
                        if (nodDestination.Tag is Vehicle objNewVehicle)
                        {
                            // Remove the Gear from the Vehicle.
                            if (objGear.Parent is IHasChildren<Gear> parent)
                                parent.Children.Remove(objGear);
                            else if (objOldCyberware != null)
                                objOldCyberware.GearChildren.Remove(objGear);
                            else if (objOldWeaponAccessory != null)
                                objOldWeaponAccessory.GearChildren.Remove(objGear);
                            else
                                objOldVehicle.GearChildren.Remove(objGear);

                            // Add the Gear to the Vehicle and set its Location.
                            objNewVehicle.GearChildren.Add(objGear);
                            objLocation?.Children.Add(objGear);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Move a Vehicle Gear TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="nodDestination">Destination Node.</param>
        /// <param name="nodGearNode">Node of gear to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveVehicleGearParentAsync(this Chummer.Character objThis, TreeNode nodDestination, TreeNode nodGearNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodDestination == null || nodGearNode == null)
                return;
            // The item cannot be dropped onto itself or onto one of its children.
            for (TreeNode objCheckNode = nodDestination;
                objCheckNode != null && objCheckNode.Level >= nodDestination.Level;
                objCheckNode = objCheckNode.Parent)
            {
                if (objCheckNode == nodGearNode)
                    return;
            }

            if (!(nodGearNode.Tag is IHasInternalId nodeId))
                return;
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // Locate the currently selected piece of Gear.
                //TODO: Better interface for determining what the parent of a bit of gear is.
                (Gear objGear, Vehicle objOldVehicle, WeaponAccessory objOldWeaponAccessory, Cyberware objOldCyberware) = await objThis.Vehicles.FindVehicleGearAsync(nodeId.InternalId, token: token).ConfigureAwait(false);

                if (objGear == null)
                    return;

                IAsyncDisposable objLocker2 = await objThis.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (nodDestination.Tag is Gear objDestinationGear)
                    {
                        // Remove the Gear from the Vehicle.
                        if (objGear.Parent is IHasChildren<Gear> parent)
                            await parent.Children.RemoveAsync(objGear, token).ConfigureAwait(false);
                        else if (objOldCyberware != null)
                            await objOldCyberware.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);
                        else if (objOldWeaponAccessory != null)
                            await objOldWeaponAccessory.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);
                        else
                            await objOldVehicle.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);

                        // Add the Gear to its new parent.
                        objGear.Location = null;
                        await objDestinationGear.Children.AddAsync(objGear, token).ConfigureAwait(false);
                    }
                    else
                    {
                        // Determine if this is a Location.
                        TreeNode nodVehicleNode = nodDestination;
                        Location objLocation = null;
                        while (nodVehicleNode.Level > 1)
                        {
                            if (objLocation is null && nodVehicleNode.Tag is Location loc)
                            {
                                objLocation = loc;
                            }

                            nodVehicleNode = nodVehicleNode.Parent;
                        }

                        // Determine if this is a Location in the destination Vehicle.
                        if (nodDestination.Tag is Vehicle objNewVehicle)
                        {
                            // Remove the Gear from the Vehicle.
                            if (objGear.Parent is IHasChildren<Gear> parent)
                                await parent.Children.RemoveAsync(objGear, token).ConfigureAwait(false);
                            else if (objOldCyberware != null)
                                await objOldCyberware.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);
                            else if (objOldWeaponAccessory != null)
                                await objOldWeaponAccessory.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);
                            else
                                await objOldVehicle.GearChildren.RemoveAsync(objGear, token).ConfigureAwait(false);

                            // Add the Gear to the Vehicle and set its Location.
                            await objNewVehicle.GearChildren.AddAsync(objGear, token).ConfigureAwait(false);
                            if (objLocation != null)
                                await objLocation.Children.AddAsync(objGear, token).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    await objLocker2.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move an Improvement TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of improvement to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveImprovementNode(this Chummer.Character objThis, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodOldNode?.Tag is Improvement objImprovement))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0)
                objNewParent = objNewParent.Parent;
            string strGroup = objNewParent.Tag?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(strGroup) && strGroup != "Node_SelectedImprovements")
                strGroup = objNewParent.Text;
            using (objThis.LockObject.EnterWriteLock(token))
            {
                objImprovement.CustomGroup = strGroup;
                objThis.Improvements[objThis.Improvements.IndexOf(objImprovement)] = objImprovement;
            }
        }

        /// <summary>
        /// Move an Improvement TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of improvement to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveImprovementNodeAsync(this Chummer.Character objThis, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objDestination == null)
                return;
            if (!(nodOldNode?.Tag is Improvement objImprovement))
                return;
            TreeNode objNewParent = objDestination;
            while (objNewParent.Level > 0)
                objNewParent = objNewParent.Parent;
            string strGroup = objNewParent.Tag?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(strGroup) && strGroup != "Node_SelectedImprovements")
                strGroup = objNewParent.Text;
            IAsyncDisposable objLocker = await objThis.LockObject.EnterWriteLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                objImprovement.CustomGroup = strGroup;
                await objThis.Improvements.SetValueAtAsync(await objThis.Improvements.IndexOfAsync(objImprovement, token).ConfigureAwait(false), objImprovement, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Move an Improvement Group TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of improvement group to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static void MoveImprovementRoot(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodOldNode == null)
                return;
            string strNewGroup = string.Empty;
            if (objDestination != null)
            {
                TreeNode objNewParent = objDestination;
                while (objNewParent.Level > 0)
                    objNewParent = objNewParent.Parent;
                intNewIndex = objNewParent.Index;
                strNewGroup = objNewParent.Tag?.ToString() ?? string.Empty;
            }

            string strLocation = nodOldNode.Tag.ToString();
            using (objThis.LockObject.EnterUpgradeableReadLock(token))
            {
                ThreadSafeObservableCollection<string> lstImprovementGroups = objThis.ImprovementGroups;
                if (!lstImprovementGroups.Contains(strNewGroup))
                    intNewIndex = 0;
                lstImprovementGroups.Move(lstImprovementGroups.IndexOf(strLocation), intNewIndex);
            }
        }

        /// <summary>
        /// Move an Improvement Group TreeNode after Drag and Drop.
        /// </summary>
        /// <param name="intNewIndex">Node's new index.</param>
        /// <param name="objDestination">Destination Node.</param>
        /// <param name="nodOldNode">Node of improvement group to move.</param>
        /// <param name="token">CancellationToken to listen to.</param>
        public static async Task MoveImprovementRootAsync(this Chummer.Character objThis, int intNewIndex, TreeNode objDestination, TreeNode nodOldNode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (nodOldNode == null)
                return;
            string strNewGroup = string.Empty;
            if (objDestination != null)
            {
                TreeNode objNewParent = objDestination;
                while (objNewParent.Level > 0)
                    objNewParent = objNewParent.Parent;
                intNewIndex = objNewParent.Index;
                strNewGroup = objNewParent.Tag?.ToString() ?? string.Empty;
            }

            string strLocation = nodOldNode.Tag.ToString();
            IAsyncDisposable objLocker = await objThis.LockObject.EnterUpgradeableReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                ThreadSafeObservableCollection<string> lstImprovementGroups = await objThis.GetImprovementGroupsAsync(token).ConfigureAwait(false);
                if (!await lstImprovementGroups.ContainsAsync(strNewGroup, token).ConfigureAwait(false))
                    intNewIndex = 0;
                await lstImprovementGroups.MoveAsync(await lstImprovementGroups.IndexOfAsync(strLocation, token).ConfigureAwait(false), intNewIndex, token).ConfigureAwait(false);
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Maps a drag-and-drop target to an index in the matching locations collection.
        /// Selected Gear, Selected Armor, and Selected Weapons occupy root rows beside locations, so only location siblings are counted.
        /// </summary>
        /// <param name="objDestination">Node dropped on. A nested node is resolved to its top-level parent.</param>
        /// <param name="intFallbackIndex">Index used when <paramref name="objDestination"/> is null.</param>
        /// <returns>Position of the destination among sibling location nodes, or 0 when the drop lands on a non-location root.</returns>
        internal static int GetLocationCollectionIndex(TreeNode objDestination, int intFallbackIndex)
        {
            if (objDestination == null)
                return intFallbackIndex;

            TreeNode objRoot = objDestination;
            while (objRoot.Level > 0)
                objRoot = objRoot.Parent;

            if (!(objRoot.Tag is Location))
                return 0;

            TreeNodeCollection objSiblings = objRoot.Parent?.Nodes ?? objRoot.TreeView?.Nodes;
            if (objSiblings == null)
                return objRoot.Index;

            int intLocationIndex = 0;
            int intLimit = Math.Min(objRoot.Index, objSiblings.Count);
            for (int i = 0; i < intLimit; ++i)
            {
                if (objSiblings[i].Tag is Location)
                    ++intLocationIndex;
            }

            return intLocationIndex;
        }
    }
}
