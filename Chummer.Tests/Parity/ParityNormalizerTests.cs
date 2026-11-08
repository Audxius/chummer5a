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

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Parity
{
    [TestClass]
    public class ParityNormalizerTests
    {
        private const string GuidA = "11111111-2222-3333-4444-555555555555";
        private const string GuidB = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

        [TestMethod]
        public void GuidsAreRenumberedByFirstAppearanceAndReferencesStayLinked()
        {
            ParityNormalizer objNormalizer = new ParityNormalizer();
            string strFirst = objNormalizer.NormalizeXml("<c><a>" + GuidA + "</a><b>" + GuidB + "</b><ref>" + GuidA + "</ref></c>");
            string strSecond = objNormalizer.NormalizeXml("<c><a>" + GuidB + "</a><b>" + GuidA + "</b><ref>" + GuidB + "</ref></c>");
            Assert.AreEqual(strFirst, strSecond);
            StringAssert.Contains(strFirst, "<ref>GUID-0001</ref>");
        }

        [TestMethod]
        public void BuildIdentifyingElementsAreDropped()
        {
            string strResult = new ParityNormalizer().NormalizeXml(
                "<character><appversion>5.226.0</appversion><minimumappversion>5.200</minimumappversion><settingshashcode>123</settingshashcode><name>X</name></character>");
            Assert.IsFalse(strResult.Contains("appversion"));
            Assert.IsFalse(strResult.Contains("settingshashcode"));
            StringAssert.Contains(strResult, "<name>X</name>");
        }

        [TestMethod]
        public void Base64BlobsAreBlankedButShortTextIsKept()
        {
            string strBlob = new string('A', 300) + "==";
            string strResult = new ParityNormalizer().NormalizeXml("<c><mugshot>" + strBlob + "</mugshot><notes>Hello</notes></c>");
            StringAssert.Contains(strResult, "<mugshot>[base64]</mugshot>");
            StringAssert.Contains(strResult, "<notes>Hello</notes>");
        }

        [TestMethod]
        public void UnorderedContainersCompareEqualRegardlessOfOrderAndGuids()
        {
            ParityNormalizer objNormalizer = new ParityNormalizer();
            string strFirst = objNormalizer.NormalizeXml(
                "<c><improvements><improvement><guid>" + GuidA + "</guid><v>1</v></improvement><improvement><guid>" + GuidB + "</guid><v>2</v></improvement></improvements></c>");
            string strSecond = objNormalizer.NormalizeXml(
                "<c><improvements><improvement><guid>" + GuidB + "</guid><v>2</v></improvement><improvement><guid>" + GuidA + "</guid><v>1</v></improvement></improvements></c>");
            Assert.AreEqual(strFirst, strSecond);
        }

        [TestMethod]
        public void OrderedContainersStillDetectReordering()
        {
            ParityNormalizer objNormalizer = new ParityNormalizer();
            Assert.AreNotEqual(objNormalizer.NormalizeXml("<c><gears><g>1</g><g>2</g></gears></c>"),
                               objNormalizer.NormalizeXml("<c><gears><g>2</g><g>1</g></gears></c>"));
        }

        [TestMethod]
        public void MachinePathsAndDataUrisAreReplacedInText()
        {
            ParityNormalizer objNormalizer = new ParityNormalizer();
            objNormalizer.PathReplacements.Add(new KeyValuePair<string, string>(@"C:\run\TestFiles", "<TESTFILES>"));
            string strResult = objNormalizer.NormalizeText("<img src=\"data:image/jpeg;base64,QUJD\"/>   \n\n<p>C:/run/TestFiles/a.chum5</p>\n");
            Assert.AreEqual("<img src=\"data:[base64]\"/>\n<p><TESTFILES>/a.chum5</p>\n", strResult);
        }
    }
}
