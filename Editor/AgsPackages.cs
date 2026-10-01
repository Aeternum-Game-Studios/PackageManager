using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Aeternum.Packages
{
    /// <summary>
    /// Lists, installs, updates and uninstalls the studio's private packages, the same as the
    /// AGS Packages window. Call from the main thread. Each method logs its outcome to the
    /// Console, so a call that isn't awaited (from <c>unity command eval</c>, say) still reports.
    /// For -executeMethod, see <see cref="AgsPackagesCli"/>.
    /// </summary>
    public static class AgsPackages
    {
        const string GitHubUserKey = "Aeternum.Packages.GitHubUser";

        /// <summary>
        /// The GitHub account whose Git credential AGS Packages asks for, saved for this Windows
        /// user (EditorPrefs). Set it when Git knows more than one GitHub account. Empty: whichever
        /// account Git gives.
        /// </summary>
        public static string GitHubUser
        {
            get => EditorPrefs.GetString(GitHubUserKey, "");
            set => EditorPrefs.SetString(GitHubUserKey, (value ?? "").Trim());
        }

        /// <summary>True while an install or uninstall is running.</summary>
        public static bool IsBusy => PackageService.IsBusy;

        /// <summary>The installed packages. Offline: reads the project only.</summary>
        public static IReadOnlyList<AgsPackage> GetInstalled() =>
            ProjectPackages.GetInstalled()
                .Select(p => new AgsPackage(p.Name, p.Version, !p.TarballExists, null, false, null))
                .ToList();

        /// <summary>
        /// The installed packages and, with access to the catalog, every package it lists with its
        /// versions. Without access it still returns what's installed, with the reason in
        /// <see cref="AgsPackageList.Problem"/>; it doesn't throw for that.
        /// </summary>
        public static Task<AgsPackageList> ListAsync() => PackageService.ListAsync(GitHubUser, false);

        /// <summary>The versions of a catalog package, newest first.</summary>
        public static Task<IReadOnlyList<string>> GetVersionsAsync(string packageName) =>
            Report(PackageService.GetVersionsAsync(packageName, GitHubUser), $"list the versions of {packageName}");

        /// <summary>
        /// Installs <paramref name="packageName"/> at <paramref name="version"/> ("1.2.0", or the full
        /// tag name), replacing the installed version if there is one. Writes UPM/name-version.tgz
        /// and Packages/manifest.json; checking them in is up to you.
        /// </summary>
        public static Task InstallAsync(string packageName, string version) =>
            Report(PackageService.InstallAsync(packageName, version, GitHubUser), $"install {packageName} {version}");

        /// <summary>Removes the package from the manifest and deletes its tarball.</summary>
        public static Task UninstallAsync(string packageName) =>
            Report(PackageService.UninstallAsync(packageName), $"uninstall {packageName}");

        static async Task Report(Task task, string what)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                LogFailure(e, what);
                throw;
            }
        }

        static async Task<T> Report<T>(Task<T> task, string what)
        {
            try
            {
                return await task;
            }
            catch (Exception e)
            {
                LogFailure(e, what);
                throw;
            }
        }

        static void LogFailure(Exception e, string what)
        {
            Logs.Error(e is AgsPackagesException
                ? $"Could not {what}: {e.Message}"
                : $"Could not {what}: unexpected {e.GetType().Name}. {e.Message}");
        }
    }
}
