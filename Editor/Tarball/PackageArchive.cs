using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

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

        static string NormalizeFolder(string folder)
        {
            var trimmed = (folder ?? "").Replace('\\', '/').Trim('/');
            return trimmed.Length == 0 ? "" : trimmed + "/";
        }
    }
}
