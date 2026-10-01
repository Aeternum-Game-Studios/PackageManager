using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aeternum.Packages
{
    /// <summary>
    /// The studio's package catalog, a JSON file in a private repository:
    /// <code>
    /// { "packages": [ { "name": "com.example.tools", "repo": "owner/name", "path": "unity/com.example.tools", "tagPrefix": "tools-v" } ] }
    /// </code>
    /// A package's versions are the tags named tagPrefix + version; "path" is the package's
    /// folder in the repository (empty for the root).
    /// </summary>
    internal sealed class Catalog
    {
        public IReadOnlyList<CatalogEntry> Packages { get; }

        Catalog(IReadOnlyList<CatalogEntry> packages) => Packages = packages;

        public CatalogEntry Find(string name) => Packages.FirstOrDefault(p => p.Name == name);

        public static Catalog Parse(string json, string source)
        {
            Dictionary<string, object> root;
            try
            {
                root = Json.ParseObject(json.TrimStart('﻿'));
            }
            catch (FormatException e)
            {
                throw new AgsPackagesException($"The catalog ({source}) is not valid JSON. {e.Message}");
            }

            var packages = new List<CatalogEntry>();
            var items = root.GetArray("packages") ?? throw new AgsPackagesException($"The catalog ({source}) has no \"packages\" list.");
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                var entry = new CatalogEntry(
                    item.GetString("name"),
                    item.GetString("repo"),
                    (item.GetString("path") ?? "").Replace('\\', '/').Trim('/'),
                    item.GetString("tagPrefix") ?? "");
                if (string.IsNullOrEmpty(entry.Name) || !IsRepoName(entry.Repo))
                    throw new AgsPackagesException($"The catalog ({source}): packages[{i}] needs a \"name\" and a \"repo\" (owner/name).");
                if (packages.Any(p => p.Name == entry.Name))
                    throw new AgsPackagesException($"The catalog ({source}) lists {entry.Name} twice.");
                packages.Add(entry);
            }
            return new Catalog(packages);
        }

        public static bool IsRepoName(string repo) =>
            !string.IsNullOrEmpty(repo) && Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
    }

    internal sealed class CatalogEntry
    {
        public string Name { get; }
        public string Repo { get; }
        public string Path { get; }
        public string TagPrefix { get; }

        public CatalogEntry(string name, string repo, string path, string tagPrefix)
        {
            Name = name;
            Repo = repo;
            Path = path;
            TagPrefix = tagPrefix;
        }

        /// <summary>The tag version for "1.2.0" or for the full tag name ("tools-v1.2.0").</summary>
        public string NormalizeVersion(string version)
        {
            var trimmed = (version ?? "").Trim();
            return TagPrefix.Length > 0 && trimmed.StartsWith(TagPrefix, StringComparison.Ordinal)
                ? trimmed.Substring(TagPrefix.Length)
                : trimmed;
        }
    }
}
