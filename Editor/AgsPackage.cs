using System.Collections.Generic;

namespace Aeternum.Packages
{
    /// <summary>One of the studio's packages, as this project and the catalog see it.</summary>
    public sealed class AgsPackage
    {
        /// <summary>The package name, such as com.example.tools.</summary>
        public string Name { get; }

        /// <summary>The installed version, or null when the project doesn't have it.</summary>
        public string InstalledVersion { get; }

        /// <summary>True when the manifest depends on the package's tarball but the file is missing.</summary>
        public bool TarballMissing { get; }

        /// <summary>The versions that can be installed, newest first. Empty without access to the package.</summary>
        public IReadOnlyList<string> AvailableVersions { get; }

        /// <summary>True when the catalog lists the package. Only known with access to the catalog.</summary>
        public bool InCatalog { get; }

        /// <summary>Why the package can't be changed, or null.</summary>
        public string Problem { get; }

        public bool IsInstalled => InstalledVersion != null;

        internal AgsPackage(string name, string installedVersion, bool tarballMissing, IReadOnlyList<string> availableVersions, bool inCatalog, string problem)
        {
            Name = name;
            InstalledVersion = installedVersion;
            TarballMissing = tarballMissing;
            AvailableVersions = availableVersions ?? new string[0];
            InCatalog = inCatalog;
            Problem = problem;
        }

        public override string ToString() =>
            $"{Name} {InstalledVersion ?? "(not installed)"}" +
            (TarballMissing ? " (tarball missing)" : "") +
            (AvailableVersions.Count > 0 ? $"; available: {string.Join(", ", AvailableVersions)}" : "") +
            (Problem != null ? $"; {Problem}" : "");
    }

    /// <summary>What <see cref="AgsPackages.ListAsync"/> found.</summary>
    public sealed class AgsPackageList
    {
        /// <summary>The installed packages and, with access to the catalog, every package it lists.</summary>
        public IReadOnlyList<AgsPackage> Packages { get; }

        /// <summary>True when the catalog was read: packages can be installed, updated and uninstalled.</summary>
        public bool CanChange { get; }

        /// <summary>The GitHub account in use, or null.</summary>
        public string GitHubLogin { get; }

        /// <summary>Why nothing can be changed, or null.</summary>
        public string Problem { get; }

        internal AgsPackageList(IReadOnlyList<AgsPackage> packages, bool canChange, string gitHubLogin, string problem)
        {
            Packages = packages;
            CanChange = canChange;
            GitHubLogin = gitHubLogin;
            Problem = problem;
        }
    }
}
