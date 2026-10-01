using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Aeternum.Packages
{
    /// <summary>A package in the project's manifest whose dependency is a tarball in the UPM folder.</summary>
    internal sealed class InstalledPackage
    {
        public string Name;
        /// <summary>The resolved version, or the one in the tarball's name when Unity couldn't resolve it.</summary>
        public string Version;
        /// <summary>The manifest's dependency, as written: file:../UPM/name-version.tgz.</summary>
        public string Dependency;
        public string TarballPath;
        public bool TarballExists;
    }

    /// <summary>
    /// The project side: AGS Packages owns the UPM folder next to Assets, and a manifest dependency
    /// pointing into it is a package it manages. Reading this needs no network.
    /// </summary>
    internal static class ProjectPackages
    {
        public const string TarballFolderName = "UPM";

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string TarballFolder => Path.Combine(ProjectRoot, TarballFolderName);
        public static string ManifestPath => Path.Combine(ProjectRoot, "Packages", "manifest.json");

        public static string TarballFileName(string name, string version) => $"{name}-{version}.tgz";

        /// <summary>The manifest dependency for a tarball in the UPM folder. Relative to Packages/, so it works on every machine.</summary>
        public static string Dependency(string tarballFileName) => $"file:../{TarballFolderName}/{tarballFileName}";

        public static List<InstalledPackage> GetInstalled()
        {
            var resolved = PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, p => p.version);
            return ReadManaged(File.ReadAllText(ManifestPath), ProjectRoot, resolved);
        }

        public static InstalledPackage Find(string name) => GetInstalled().FirstOrDefault(p => p.Name == name);

        /// <summary>The full paths of every tarball the manifest depends on.</summary>
        public static HashSet<string> ReferencedTarballs()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in Dependencies(File.ReadAllText(ManifestPath)).Values)
            {
                var path = TarballPath(value, ProjectRoot);
                if (path != null)
                    paths.Add(path);
            }
            return paths;
        }

        internal static List<InstalledPackage> ReadManaged(string manifestJson, string projectRoot, IReadOnlyDictionary<string, string> resolvedVersions)
        {
            var folder = Path.GetFullPath(Path.Combine(projectRoot, TarballFolderName));
            var installed = new List<InstalledPackage>();
            foreach (var dependency in Dependencies(manifestJson))
            {
                var path = TarballPath(dependency.Value, projectRoot);
                if (path == null || !string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(path);
                string version = null;
                if (resolvedVersions == null || !resolvedVersions.TryGetValue(dependency.Key, out version))
                {
                    if (fileName.StartsWith(dependency.Key + "-", StringComparison.Ordinal))
                        version = fileName.Substring(dependency.Key.Length + 1);
                }
                installed.Add(new InstalledPackage
                {
                    Name = dependency.Key,
                    Version = version,
                    Dependency = dependency.Value,
                    TarballPath = path,
                    TarballExists = File.Exists(path),
                });
            }
            installed.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return installed;
        }

        static Dictionary<string, string> Dependencies(string manifestJson)
        {
            Dictionary<string, object> manifest;
            try
            {
                manifest = Json.ParseObject(manifestJson);
            }
            catch (FormatException e)
            {
                throw new AgsPackagesException($"Packages/manifest.json is not valid JSON. {e.Message}");
            }
            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            var section = manifest.GetObject("dependencies");
            if (section != null)
                foreach (var pair in section)
                    if (pair.Value is string value)
                        dependencies[pair.Key] = value;
            return dependencies;
        }

        /// <summary>The full path of a file: dependency on a .tgz, or null for any other dependency.</summary>
        static string TarballPath(string dependency, string projectRoot)
        {
            const string scheme = "file:";
            if (!dependency.StartsWith(scheme, StringComparison.Ordinal) ||
                !dependency.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                return null;
            var path = dependency.Substring(scheme.Length);
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, "Packages", path));
        }
    }
}
