using Chummer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass]
    public class ListItemTests
    {
        [TestMethod]
        public void BoxedSelectionValuesCompareLikeTypedItems()
        {
            var first = new ListItem(new string('x', 3), "Displayed name");
            var equivalent = new ListItem(new string('x', 3), "Displayed name");
            Assert.IsTrue(first.Equals(equivalent));
            Assert.IsTrue(((object)first).Equals(equivalent));
            Assert.IsTrue(first.Equals("xxx"));
            Assert.AreEqual(first.GetHashCode(), equivalent.GetHashCode());
            Assert.IsFalse(first.Equals(new ListItem("xxx", "Other label")));
            var empty = new ListItem(null, "Empty");
            Assert.IsTrue(((object)empty).Equals(new ListItem(null, "Empty")));
            Assert.IsFalse(empty.Equals((object)null));
            Assert.AreEqual(0, empty.GetHashCode());
        }
    }
}
