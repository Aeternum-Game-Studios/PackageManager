using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Aeternum.Packages.Tests
{
    public class ProjectTests
    {
        const string Manifest = @"{
  ""dependencies"": {
    ""com.example.tools"": ""file:../UPM/com.example.tools-1.2.0.tgz"",
    ""com.example.elsewhere"": ""file:../Tarballs/com.example.elsewhere-1.0.0.tgz"",
    ""com.example.folder"": ""file:../../SomeFolder"",
    ""com.unity.modules.ui"": ""1.0.0""
  }
}";

        [Test]
        public void ManagesOnlyTarballsInTheUpmFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "SomeProject");

            var installed = ProjectPackages.ReadManaged(Manifest, root, new Dictionary<string, string>());

            Assert.AreEqual(1, installed.Count);
            Assert.AreEqual("com.example.tools", installed[0].Name);
            Assert.AreEqual("1.2.0", installed[0].Version, "from the file name when Unity hasn't resolved it");
            Assert.AreEqual(Path.Combine(root, "UPM", "com.example.tools-1.2.0.tgz"), installed[0].TarballPath);
        }

        [Test]
        public void PrefersTheResolvedVersion()
        {
            var installed = ProjectPackages.ReadManaged(Manifest, Path.GetTempPath(),
                new Dictionary<string, string> { ["com.example.tools"] = "1.2.0-preview" });

            Assert.AreEqual("1.2.0-preview", installed[0].Version);
        }

        [Test]
        public void DependencyIsRelativeToPackages()
        {
            Assert.AreEqual("file:../UPM/com.example.tools-1.2.0.tgz",
                ProjectPackages.Dependency(ProjectPackages.TarballFileName("com.example.tools", "1.2.0")));
        }

        [Test]
        public void ReadsTheCatalog()
        {
            var catalog = Catalog.Parse(@"{ ""packages"": [
                { ""name"": ""com.example.tools"", ""repo"": ""Owner/Tools"", ""path"": ""/unity/com.example.tools/"", ""tagPrefix"": ""tools-v"" },
                { ""name"": ""com.example.root"", ""repo"": ""Owner/Root"" } ] }", "test");

            var tools = catalog.Find("com.example.tools");
            Assert.AreEqual("unity/com.example.tools", tools.Path);
            Assert.AreEqual("1.2.0", tools.NormalizeVersion("tools-v1.2.0"));
            Assert.AreEqual("1.2.0", tools.NormalizeVersion("1.2.0"));
            Assert.AreEqual("", catalog.Find("com.example.root").Path);
            Assert.AreEqual("", catalog.Find("com.example.root").TagPrefix);
        }

        [TestCase(@"{ ""packages"": [ { ""name"": ""a"" } ] }")]
        [TestCase(@"{ ""packages"": [ { ""name"": ""a"", ""repo"": ""not a repo"" } ] }")]
        [TestCase(@"{ ""packages"": [ { ""name"": ""a"", ""repo"": ""o/r"" }, { ""name"": ""a"", ""repo"": ""o/s"" } ] }")]
        [TestCase(@"{ }")]
        [TestCase(@"not json")]
        public void RejectsBadCatalogs(string json)
        {
            Assert.Throws<AgsPackagesException>(() => Catalog.Parse(json, "test"));
        }
    }
}
