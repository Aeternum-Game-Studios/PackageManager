using System.Linq;
using NUnit.Framework;

namespace Aeternum.Packages.Tests
{
    public class SemVersionTests
    {
        [Test]
        public void OrdersAsSemVer()
        {
            var ordered = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0", "1.2.0", "1.10.0", "2.0.0" };
            var shuffled = ordered.Reverse().ToArray();

            var sorted = shuffled.Select(Parse).OrderBy(v => v).Select(v => v.ToString()).ToArray();

            CollectionAssert.AreEqual(ordered, sorted);
        }

        [TestCase("1.0")]
        [TestCase("1.0.0.0")]
        [TestCase("v1.0.0")]
        [TestCase("1.0.x")]
        [TestCase("1.0.0-")]
        [TestCase("")]
        public void RejectsNonVersions(string text)
        {
            Assert.IsFalse(SemVersion.TryParse(text, out _));
        }

        [Test]
        public void KeepsTheOriginalText()
        {
            Assert.AreEqual("1.2.3-rc.1+build.5", Parse("1.2.3-rc.1+build.5").ToString());
        }

        static SemVersion Parse(string text)
        {
            Assert.IsTrue(SemVersion.TryParse(text, out var version), text);
            return version;
        }
    }
}
