using System;
using System.Linq;
using Chummer.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass]
    public class NotebookCodecTests
    {
        [TestMethod]
        public void PlainLegacyNotesRemainByteForByteUnchanged()
        {
            const string text = "Existing notes\r\n\r\nKeep whitespace.\n";
            var pages = NotebookCodec.Parse(text);
            Assert.AreEqual(1, pages.Count); Assert.AreEqual(text, pages[0].Body);
            Assert.AreEqual(text, NotebookCodec.Serialize(pages));
        }
        [TestMethod]
        public void MultiplePagesRetainTitlesBodiesOrderAndIdentity()
        {
            var first = new NotebookPageData(Guid.NewGuid(), "Session \"one\" — ąč", "\nOpening\r\n\r\n");
            var second = new NotebookPageData(Guid.NewGuid(), "Session \"one\" — ąč", "Second body");
            var restored = NotebookCodec.Parse(NotebookCodec.Serialize(new[] { first, second }));
            CollectionAssert.AreEqual(new[] { first.Id, second.Id }, restored.Select(x => x.Id).ToArray());
            Assert.AreEqual(first.Title, restored[0].Title); Assert.AreEqual(first.Body, restored[0].Body);
            Assert.AreEqual(second.Body, restored[1].Body);
        }
        [TestMethod]
        public void MarkerLikeBodyTextIsEscapedAndRestored()
        {
            var id = Guid.NewGuid();
            string body = "<!-- /chummer-note:" + id.ToString("D") + " -->\n\\<!-- chummer-note not a header -->";
            var restored = NotebookCodec.Parse(NotebookCodec.Serialize(new[] { new NotebookPageData(id, "Markers", body) }));
            Assert.AreEqual(1, restored.Count); Assert.AreEqual(body, restored[0].Body);
        }
        [TestMethod]
        public void MalformedMarkersAndExternalLegacyEditsAreNotDiscarded()
        {
            string malformed = "<!-- chummer-note:v1 " + Guid.NewGuid() + " invalid-json -->\nUnclosed notes";
            Assert.AreEqual(malformed, NotebookCodec.Parse(malformed).Single().Body);
            var page = new NotebookPageData(Guid.NewGuid(), "Session", "Old content");
            string legacyEdited = "Added in original Chummer\n" + NotebookCodec.Serialize(new[] { page }).Replace("Old content", "Edited in original Chummer");
            var restored = NotebookCodec.Parse(legacyEdited);
            Assert.AreEqual(2, restored.Count); Assert.IsTrue(restored[0].Body.Contains("Added in original Chummer"));
            Assert.AreEqual("Edited in original Chummer", restored[1].Body);
        }
        [TestMethod]
        public void DuplicateIdentifiersAreRejectedBeforeSerialization()
        {
            var page = new NotebookPageData(Guid.NewGuid(), "One", "Body");
            Assert.ThrowsExactly<ArgumentException>(() => NotebookCodec.Serialize(new[] { page, page }));
        }
    }
}
