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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Services
{
    [TestClass]
    public class MemorySettingsStoreTests
    {
        [TestMethod]
        public void RootDoesNotExistUntilCreated()
        {
            MemorySettingsStore objStore = new MemorySettingsStore();
            Assert.IsNull(objStore.OpenRoot());
            using (ISettingsKey objRoot = objStore.CreateRoot())
                Assert.IsNotNull(objRoot);
            Assert.IsNotNull(objStore.OpenRoot());
        }

        [TestMethod]
        public void ValuesAreCaseInsensitiveAndMissingOnesAreNull()
        {
            MemorySettingsStore objStore = new MemorySettingsStore();
            using (ISettingsKey objRoot = objStore.CreateRoot())
            {
                objRoot.SetValue("Language", "de-de");
                Assert.AreEqual("de-de", objRoot.GetValue("language"));
                Assert.IsNull(objRoot.GetValue("missing"));
                objRoot.DeleteValue("LANGUAGE");
                objRoot.DeleteValue("never-existed");
                Assert.IsNull(objRoot.GetValue("language"));
            }
        }

        [TestMethod]
        public void SubKeysNestAndPathsAreSupported()
        {
            MemorySettingsStore objStore = new MemorySettingsStore();
            using (ISettingsKey objRoot = objStore.CreateRoot())
            {
                Assert.IsNull(objRoot.OpenSubKey("CustomDataDirectory"));
                using (ISettingsKey objDeep = objRoot.CreateSubKey("CustomDataDirectory\\My Data"))
                    objDeep.SetValue("Path", "$CHUMMER/customdata/My Data");
                using (ISettingsKey objParent = objRoot.OpenSubKey("customdatadirectory"))
                {
                    CollectionAssert.AreEqual(new[] { "My Data" }, objParent.GetSubKeyNames());
                    using (ISettingsKey objChild = objParent.OpenSubKey("My Data"))
                        Assert.AreEqual("$CHUMMER/customdata/My Data", objChild.GetValue("Path"));
                }
                objRoot.DeleteSubKeyTree("CustomDataDirectory");
                objRoot.DeleteSubKeyTree("CustomDataDirectory");
                Assert.IsNull(objRoot.OpenSubKey("CustomDataDirectory"));
            }
        }

        [TestMethod]
        public void SnapshotRoundTripsAndChangesAreReported()
        {
            MemorySettingsStore objStore = new MemorySettingsStore();
            int intChanges = 0;
            objStore.Changed += (sender, args) => ++intChanges;
            using (ISettingsKey objRoot = objStore.CreateRoot())
            {
                objRoot.SetValue("useloggingApplicationInsightsResetCounter", 3);
                using (ISettingsKey objSourcebooks = objRoot.CreateSubKey("Sourcebook"))
                    objSourcebooks.SetValue("SR5", "/books/core.pdf|0");
            }
            Assert.AreEqual(2, intChanges);

            MemorySettingsStore objCopy = new MemorySettingsStore();
            objCopy.Import(objStore.Export());
            using (ISettingsKey objRoot = objCopy.OpenRoot())
            {
                Assert.AreEqual(3, objRoot.GetValue("useloggingApplicationInsightsResetCounter"));
                using (ISettingsKey objSourcebooks = objRoot.OpenSubKey("Sourcebook"))
                    Assert.AreEqual("/books/core.pdf|0", objSourcebooks.GetValue("SR5"));
            }
        }
    }
}
