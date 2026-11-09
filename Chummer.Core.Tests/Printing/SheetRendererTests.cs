/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chummer.Printing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Printing
{
    [TestClass]
    public class SheetRendererTests
    {
        [TestMethod]
        public async Task DefaultSheetRendersCharacter()
        {
            string strPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "Barrett.chum5");
            using (Character objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(strPath)))
            {
                var lstSheets = await SheetRenderer.ListSheetsAsync(GlobalSettings.Language, new[] { objCharacter });
                Assert.IsTrue(lstSheets.Count > 5, "sheets listed: " + lstSheets.Count);
                Assert.IsTrue(lstSheets.Any(x => x.Value?.ToString() == GlobalSettings.DefaultCharacterSheet), "default sheet listed");
                string strHtml = await SheetRenderer.RenderHtmlAsync(new[] { objCharacter }, GlobalSettings.DefaultCharacterSheet);
                StringAssert.Contains(strHtml, "<html");
                StringAssert.Contains(strHtml, objCharacter.CharacterName);
            }
        }
    }
}
