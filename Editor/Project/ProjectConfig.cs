using System;
using System.Collections.Generic;
using System.IO;

namespace Aeternum.Packages
{
    /// <summary>
    /// The project's ProjectSettings/AgsPackages.json: where the package catalog lives. It names a
    /// private repository, so it's part of the project (in version control), never of this package.
    /// <code>
    /// { "catalog": { "repo": "owner/name", "path": "catalog.json", "ref": "main" } }
    /// </code>
    /// "path" defaults to catalog.json and "ref" to the repository's default branch.
    /// </summary>
    internal sealed class ProjectConfig
    {
        public const string RelativePath = "ProjectSettings/AgsPackages.json";

        public string CatalogRepo { get; private set; }
        public string CatalogPath { get; private set; }
        public string CatalogRef { get; private set; }

        /// <summary>The config, or null when the project has none.</summary>
        public static ProjectConfig Load(string projectRoot)
        {
            var path = Path.Combine(projectRoot, RelativePath);
            if (!File.Exists(path))
                return null;

            Dictionary<string, object> root;
            try
            {
                root = Json.ParseObject(File.ReadAllText(path));
            }
            catch (FormatException e)
            {
                throw new AgsPackagesException($"{RelativePath} is not valid JSON. {e.Message}");
            }

            var catalog = root.GetObject("catalog");
            var repo = catalog.GetString("repo");
            if (!Catalog.IsRepoName(repo))
                throw new AgsPackagesException($"{RelativePath} needs \"catalog\": {{ \"repo\": \"owner/name\" }}.");

            var catalogPath = catalog.GetString("path");
            return new ProjectConfig
            {
                CatalogRepo = repo,
                CatalogPath = string.IsNullOrEmpty(catalogPath) ? "catalog.json" : catalogPath.Trim('/'),
                CatalogRef = catalog.GetString("ref"),
            };
        }
    }
}
