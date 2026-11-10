using Chummer.ParityVerification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests
{
    [TestClass]
    public class SaveComparisonTests
    {
        [TestMethod]
        public void AttributeOrderAndCarriedWeightScaleAreEquivalentButValuesAreNot()
        {
            const string original = "<character><attributes><attribute><name>BOD</name><totalvalue>3</totalvalue></attribute><attribute><name>EDG</name><totalvalue>2</totalvalue></attribute></attributes><totalcarriedweight>0</totalcarriedweight></character>";
            const string reordered = "<character><attributes><attribute><name>EDG</name><totalvalue>2</totalvalue></attribute><attribute><name>BOD</name><totalvalue>3</totalvalue></attribute></attributes><totalcarriedweight>0.0</totalcarriedweight></character>";
            Assert.AreEqual(SaveComparison.Canonicalize(original), SaveComparison.Canonicalize(reordered));
            Assert.AreNotEqual(SaveComparison.Canonicalize(original), SaveComparison.Canonicalize(reordered.Replace("<totalvalue>3</totalvalue>", "<totalvalue>4</totalvalue>")));
        }

        [TestMethod]
        public void ComparisonPreservesOtherNumbersNotesAndGameplayListOrder()
        {
            const string original = "<character><nuyen>001</nuyen><notes>Keep 01 and 1 distinct</notes><skills><skill>A</skill><skill>B</skill></skills></character>";
            string expected = SaveComparison.Canonicalize(original);
            Assert.AreNotEqual(expected, SaveComparison.Canonicalize(original.Replace("<nuyen>001</nuyen>", "<nuyen>1</nuyen>")));
            Assert.AreNotEqual(expected, SaveComparison.Canonicalize(original.Replace("Keep 01", "Keep 1")));
            Assert.AreNotEqual(expected, SaveComparison.Canonicalize(original.Replace("<skill>A</skill><skill>B</skill>", "<skill>B</skill><skill>A</skill>")));
        }
    }
}
