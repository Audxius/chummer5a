using Chummer.ParityVerification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests
{
    [TestClass]
    public class ExportComparisonTests
    {
        [TestMethod]
        public void ExportAttributeOrderIsEquivalentButRatingsAndOtherListsStaySignificant()
        {
            const string original = "<characters><character><attributes><attributecategory_english>Standard</attributecategory_english><attribute><name_english>BOD</name_english><total>3</total></attribute><attribute><name_english>AGI</name_english><total>2</total></attribute></attributes><skills><skill>A</skill><skill>B</skill></skills></character></characters>";
            const string reordered = "<characters><character><attributes><attributecategory_english>Standard</attributecategory_english><attribute><name_english>AGI</name_english><total>2</total></attribute><attribute><name_english>BOD</name_english><total>3</total></attribute></attributes><skills><skill>A</skill><skill>B</skill></skills></character></characters>";
            string expected = ExportComparison.CanonicalizeXml(original);
            Assert.AreEqual(expected, ExportComparison.CanonicalizeXml(reordered));
            Assert.AreNotEqual(expected, ExportComparison.CanonicalizeXml(reordered.Replace("<total>3</total>", "<total>4</total>")));
            Assert.AreNotEqual(expected, ExportComparison.CanonicalizeXml(reordered.Replace("<skill>A</skill><skill>B</skill>", "<skill>B</skill><skill>A</skill>")));
        }

        [TestMethod]
        public void JsonPortraitEncodingAndAttributeOrderAreEquivalentButOtherDataIsPreserved()
        {
            string first = "{\"character\":{\"mainmugshotbase64\":\"" + new string('A', 220) + "\",\"notes\":\"01\",\"attributes\":{\"attribute\":[{\"name_english\":\"BOD\",\"total\":\"3\"},{\"name_english\":\"AGI\",\"total\":\"2\"}]}}}";
            string second = "{\"character\":{\"mainmugshotbase64\":\"" + new string('B', 220) + "\",\"notes\":\"01\",\"attributes\":{\"attribute\":[{\"name_english\":\"AGI\",\"total\":\"2\"},{\"name_english\":\"BOD\",\"total\":\"3\"}]}}}";
            string expected = ExportComparison.CanonicalizeJson(first);
            Assert.AreEqual(expected, ExportComparison.CanonicalizeJson(second));
            Assert.AreNotEqual(expected, ExportComparison.CanonicalizeJson(second.Replace("\"total\":\"3\"", "\"total\":\"4\"")));
            Assert.AreNotEqual(expected, ExportComparison.CanonicalizeJson(second.Replace("\"notes\":\"01\"", "\"notes\":\"1\"")));
            Assert.AreNotEqual(expected, ExportComparison.CanonicalizeJson(second.Replace(new string('B', 220), "")));
        }
    }
}
