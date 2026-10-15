using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Aeternum.Packages
{
    /// <summary>Reads a package folder out of a GitHub source archive.</summary>
    internal static class PackageArchive
    {
        /// <summary>
        /// Returns the files under <paramref name="folder"/> (repository-relative, "" for the root)
        /// keyed by their path inside the package. GitHub puts every entry of a zipball under one
        /// top-level directory, which is dropped.
        /// </summary>
        public static Dictionary<string, byte[]> ReadFolder(string zipPath, string folder)
        {
            var prefix = NormalizeFolder(folder);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var stream = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    var fullName = entry.FullName.Replace('\\', '/');
                    if (fullName.EndsWith("/", StringComparison.Ordinal))
                        continue;
                    var root = fullName.IndexOf('/');
                    if (root < 0)
                        continue;
                    var path = fullName.Substring(root + 1);
                    if (prefix.Length > 0)
                    {
                        if (!path.StartsWith(prefix, StringComparison.Ordinal))
                            continue;
                        path = path.Substring(prefix.Length);
                    }
                    if (Array.IndexOf(path.Split('/'), "..") >= 0)
                        throw new AgsPackagesException($"The archive has an invalid path: {fullName}");

                    using (var entryStream = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        entryStream.CopyTo(buffer);
                        files[path] = buffer.ToArray();
                    }
                }
            }
            return files;
        }

        /// <summary>Checks that the files are the package the catalog promised, at the tagged version.</summary>
        public static void Validate(IReadOnlyDictionary<string, byte[]> files, string expectedName, string expectedVersion, string source)
        {
            if (!files.TryGetValue("package.json", out var manifestBytes))
                throw new AgsPackagesException($"{source} has no package.json. Check the package's path in the catalog.");

            Dictionary<string, object> manifest;
            try
            {
                manifest = Json.ParseObject(System.Text.Encoding.UTF8.GetString(manifestBytes).TrimStart('﻿'));
            }
            catch (FormatException e)
            {
                throw new AgsPackagesException($"{source}: package.json is not valid JSON. {e.Message}");
            }

            var name = manifest.GetString("name");
            if (name != expectedName)
                throw new AgsPackagesException($"{source}: package.json names the package \"{name}\", not \"{expectedName}\".");
            var version = manifest.GetString("version");
            if (version != expectedVersion)
                throw new AgsPackagesException(
                    $"{source}: package.json says version \"{version}\", but the tag is for {expectedVersion}. " +
                    "The package's version and its tag have to match.");
        }

        /// <summary>The file Unity adds to a package tarball when it signs it.</summary>
        public const string SignatureFile = ".attestation.p7m";

        /// <summary>
        /// Checks that a tarball published with a release holds exactly the tagged package folder: the same paths with
        /// the same bytes. Hidden paths (a file or folder whose name starts with a dot) aren't compared: Unity never
        /// imports them, so they can't change the package, and packers differ on them (Unity's signature,
        /// .gitignore, .github). The package's own package.json is compared as JSON: Unity's packer writes it again
        /// (CRLF becomes LF), so only its data has to match. Anything else means it isn't the tag's package, so it
        /// isn't installed.
        /// </summary>
        public static void CheckSameFiles(IReadOnlyDictionary<string, byte[]> published, IReadOnlyDictionary<string, byte[]> tagged,
            string publishedSource, string taggedSource)
        {
            var differences = new List<string>();
            foreach (var file in tagged.Where(f => !IsHidden(f.Key)))
            {
                if (!published.TryGetValue(file.Key, out var bytes))
                    differences.Add($"{file.Key} is missing");
                else if (file.Key == "package.json" ? !SameJson(bytes, file.Value) : !bytes.SequenceEqual(file.Value))
                    differences.Add($"{file.Key} is different");
            }
            foreach (var path in published.Keys.Where(p => !IsHidden(p)))
            {
                if (!tagged.ContainsKey(path))
                    differences.Add($"{path} is not in the tag");
            }
            if (differences.Count == 0)
                return;

            var shown = string.Join("; ", differences.Take(10));
            throw new AgsPackagesException(
                $"{publishedSource} doesn't hold the files of {taggedSource}: {shown}" +
                (differences.Count > 10 ? $" (and {differences.Count - 10} more)." : ".") +
                " Pack it from a checkout of the tag with the repository's line endings.");
        }

        static bool SameJson(byte[] a, byte[] b)
        {
            try
            {
                return Json.SameValue(Json.Parse(System.Text.Encoding.UTF8.GetString(a).TrimStart('﻿')),
                    Json.Parse(System.Text.Encoding.UTF8.GetString(b).TrimStart('﻿')));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        static bool IsHidden(string path) => path.Split('/').Any(part => part.StartsWith(".", StringComparison.Ordinal));

        static string NormalizeFolder(string folder)
        {
            var trimmed = (folder ?? "").Replace('\\', '/').Trim('/');
            return trimmed.Length == 0 ? "" : trimmed + "/";
        }
    }
}
