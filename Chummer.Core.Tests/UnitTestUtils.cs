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
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests
{
    /// <summary>
    /// Headless host for engine tests: no UI is registered, so HostServices uses its headless defaults
    /// (in-memory settings, pass-through image codec, prompts only answered when forced).
    /// Same API as the net48 Chummer.Tests helper so the shared parity harness compiles against both.
    /// </summary>
    [TestClass]
    public static class UnitTestUtils
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            Utils.IsUnitTest = true;
            Utils.IsUnitTestForUI = false;
            Utils.CreateSynchronizationContext();
        }

        /// <summary>
        /// Load a character file, failing the test with details if it cannot be loaded.
        /// </summary>
        public static Character LoadCharacter(FileInfo objFileInfo, Character objExistingCharacter = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Character objCharacter = objExistingCharacter;
            try
            {
                Debug.WriteLine("Loading: " + objFileInfo.Name);
                if (objExistingCharacter != null)
                    objCharacter.ResetCharacter(token);
                else
                    objCharacter = new Character();
                objCharacter.FileName = objFileInfo.FullName;
                Assert.IsTrue(objCharacter.Load(token: token), "Could not load " + objFileInfo.FullName);
            }
            catch (Exception e) when (!(e is AssertFailedException))
            {
                if (objCharacter != null && objExistingCharacter == null)
                    objCharacter.Dispose();
                Assert.Fail("Exception while loading " + objFileInfo.FullName + ":" + Environment.NewLine + e);
                throw;
            }

            return objCharacter;
        }
    }
}
