using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Aeternum.Packages.Tests
{
    public class JsonTests
    {
        [Test]
        public void ReadsObjectsArraysAndScalars()
        {
            var root = Json.ParseObject("{ \"a\": \"x\\\"y\\u00e9\", \"b\": [1, 2.5, true, false, null], \"c\": { } }");

            Assert.AreEqual("x\"yé", root.GetString("a"));
            CollectionAssert.AreEqual(new object[] { 1.0, 2.5, true, false, null }, root.GetArray("b"));
            Assert.AreEqual(0, root.GetObject("c").Count);
            Assert.IsNull(root.GetString("missing"));
        }

        [Test]
        public void ReadsTopLevelArrays()
        {
            var items = Json.ParseArray("[{\"ref\":\"refs/tags/v1.0.0\"}]");

            Assert.AreEqual("refs/tags/v1.0.0", ((Dictionary<string, object>)items[0]).GetString("ref"));
        }

        [TestCase("{\"a\": }")]
        [TestCase("{\"a\": 1,}")]
        [TestCase("[1 2]")]
        [TestCase("{\"a\": \"unterminated}")]
        [TestCase("{} extra")]
        public void RejectsInvalidJson(string text)
        {
            Assert.Throws<FormatException>(() => Json.Parse(text));
        }
    }
}
