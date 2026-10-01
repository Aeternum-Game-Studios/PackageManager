using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Aeternum.Packages.Tests
{
    public class TarballTests
    {
        string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AgsPackagesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        [Test]
        public void SameFilesGiveTheSameBytes()
        {
            var files = SampleFiles();
            var first = Path.Combine(directory, "a.tgz");
            var second = Path.Combine(directory, "b.tgz");

            TarGz.Write(first, files);
            TarGz.Write(second, new Dictionary<string, byte[]>(files.Reverse().ToDictionary(p => p.Key, p => p.Value)));

            CollectionAssert.AreEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }

        [Test]
        public void WritesEveryFileUnderPackage()
        {
            var files = SampleFiles();
            var tarball = Path.Combine(directory, "p.tgz");

            TarGz.Write(tarball, files);
            var read = ReadTarball(tarball);

            CollectionAssert.AreEquivalent(files.Keys.Select(k => "package/" + k), read.Keys);
            foreach (var file in files)
                CollectionAssert.AreEqual(file.Value, read["package/" + file.Key], file.Key);
        }

        [Test]
        public void ReadsThePackageFolderOutOfAGitHubZipball()
        {
            var zip = Path.Combine(directory, "source.zip");
            using (var archive = ZipFile(zip))
            {
                Add(archive, "Owner-Repo-abc1234/", null);
                Add(archive, "Owner-Repo-abc1234/README.md", "repo readme");
                Add(archive, "Owner-Repo-abc1234/unity/com.example.tools/", null);
                Add(archive, "Owner-Repo-abc1234/unity/com.example.tools/package.json", "{\"name\":\"com.example.tools\",\"version\":\"1.2.0\"}");
                Add(archive, "Owner-Repo-abc1234/unity/com.example.tools/Editor/Tool.cs", "class Tool {}\r\n");
                Add(archive, "Owner-Repo-abc1234/unity/com.example.toolsextra/package.json", "{}");
            }

            var files = PackageArchive.ReadFolder(zip, "unity/com.example.tools/");

            CollectionAssert.AreEquivalent(new[] { "package.json", "Editor/Tool.cs" }, files.Keys);
            Assert.AreEqual("class Tool {}\r\n", Encoding.UTF8.GetString(files["Editor/Tool.cs"]));
            Assert.DoesNotThrow(() => PackageArchive.Validate(files, "com.example.tools", "1.2.0", "test"));
            Assert.Throws<AgsPackagesException>(() => PackageArchive.Validate(files, "com.example.tools", "1.3.0", "test"));
            Assert.Throws<AgsPackagesException>(() => PackageArchive.Validate(files, "com.example.other", "1.2.0", "test"));
        }

        static Dictionary<string, byte[]> SampleFiles() => new Dictionary<string, byte[]>
        {
            ["package.json"] = Encoding.UTF8.GetBytes("{\"name\":\"com.example.tools\",\"version\":\"1.0.0\"}"),
            ["Editor/Tool.cs"] = Encoding.UTF8.GetBytes("class Tool {}\r\n"),
            ["Editor/Empty.txt"] = new byte[0],
            ["Editor/Exactly512.bin"] = Enumerable.Repeat((byte)7, 512).ToArray(),
            [string.Join("/", Enumerable.Repeat("a-rather-long-folder-name", 8)) + "/File.cs"] = Encoding.UTF8.GetBytes("long"),
            ["Editor/Ñandú.cs"] = Encoding.UTF8.GetBytes("non-ascii name"),
        };

        static ZipArchive ZipFile(string path) => new ZipArchive(File.Create(path), ZipArchiveMode.Create);

        static void Add(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            if (content == null)
                return;
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        /// <summary>A ustar + pax reader, enough to check what TarGz writes.</summary>
        static Dictionary<string, byte[]> ReadTarball(string path)
        {
            var files = new Dictionary<string, byte[]>();
            byte[] tar;
            using (var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
            using (var buffer = new MemoryStream())
            {
                gzip.CopyTo(buffer);
                tar = buffer.ToArray();
            }

            string paxPath = null;
            for (var offset = 0; offset + 512 <= tar.Length;)
            {
                if (tar.Skip(offset).Take(512).All(b => b == 0))
                    break;
                var header = tar.Skip(offset).Take(512).ToArray();
                var name = Text(header, 0, 100);
                var prefix = Text(header, 345, 155);
                var size = Convert.ToInt64(Text(header, 124, 12).Trim(), 8);
                var type = (char)header[156];
                Assert.AreEqual("ustar", Text(header, 257, 6));
                Assert.AreEqual(Checksum(header), Convert.ToInt64(Text(header, 148, 8).Trim(), 8));
                var data = tar.Skip(offset + 512).Take((int)size).ToArray();
                offset += 512 + (int)((size + 511) / 512 * 512);

                if (type == 'x')
                {
                    var record = Encoding.UTF8.GetString(data);
                    paxPath = record.Substring(record.IndexOf("path=", StringComparison.Ordinal) + 5).TrimEnd('\n');
                    continue;
                }
                Assert.AreEqual('0', type);
                files[paxPath ?? (prefix.Length > 0 ? prefix + "/" + name : name)] = data;
                paxPath = null;
            }
            return files;
        }

        static string Text(byte[] header, int offset, int length) =>
            Encoding.ASCII.GetString(header, offset, length).Split('\0')[0];

        static long Checksum(byte[] header)
        {
            long sum = 0;
            for (var i = 0; i < header.Length; i++)
                sum += i >= 148 && i < 156 ? (byte)' ' : header[i];
            return sum;
        }
    }
}
