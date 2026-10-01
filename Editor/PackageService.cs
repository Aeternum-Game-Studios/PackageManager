using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aeternum.Packages
{
    /// <summary>What <see cref="AgsPackages"/>, the CLI and the window share.</summary>
    internal static class PackageService
    {
        const string PendingKey = "Aeternum.Packages.PendingOperation";

        static bool busy;

        public static bool IsBusy => busy || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""));

        sealed class Connection
        {
            public GitHubClient Client;
            public string Login;
            public Catalog Catalog;
        }

        /// <summary>
        /// Reads the project's config, gets the credential from Git, checks it and reads the
        /// catalog. Only an interactive call lets Git Credential Manager show its sign-in window,
        /// and only then is a working credential stored back (in the Windows credential store).
        /// </summary>
        static async Task<Connection> ConnectAsync(string user, bool interactive)
        {
            var config = ProjectConfig.Load(ProjectPackages.ProjectRoot)
                ?? throw new AgsPackagesException(
                    $"This project has no {ProjectConfig.RelativePath}, so AGS Packages doesn't know where the catalog is.");

            var credential = await GitCredential.FillAsync(user, interactive);
            if (credential == null)
                throw new GitHubAccessException(GitHubAccess.NoCredential, string.IsNullOrEmpty(user)
                    ? "Git has no GitHub credential it can give without asking. Set your GitHub user (Git can't choose " +
                      "when it knows more than one account) and press Connect."
                    : $"Git has no GitHub credential for {user}. Press Connect to sign in.");

            var client = new GitHubClient(credential);
            var login = await client.GetLoginAsync();
            if (!string.IsNullOrEmpty(user) && !string.Equals(login, user, StringComparison.OrdinalIgnoreCase))
                throw new GitHubAccessException(GitHubAccess.WrongAccount,
                    $"Git gave the credential of {login}, not of {user}.");
            if (interactive)
                await credential.ApproveAsync();

            var source = $"{config.CatalogPath} in {config.CatalogRepo}";
            var catalog = Catalog.Parse(await client.GetFileAsync(config.CatalogRepo, config.CatalogPath, config.CatalogRef), source);
            return new Connection { Client = client, Login = login, Catalog = catalog };
        }

        public static async Task<AgsPackageList> ListAsync(string user, bool interactive)
        {
            var installed = ProjectPackages.GetInstalled();
            Connection connection = null;
            string problem = null;
            try
            {
                connection = await ConnectAsync(user, interactive);
            }
            catch (AgsPackagesException e)
            {
                problem = e.Message;
            }

            var packages = new List<AgsPackage>();
            if (connection != null)
            {
                var lookups = connection.Catalog.Packages.Select(async entry =>
                {
                    try
                    {
                        var tags = await connection.Client.GetVersionTagsAsync(entry.Repo, entry.TagPrefix);
                        return (entry, versions: tags.Select(t => t.Version.ToString()).ToList(), problem: (string)null);
                    }
                    catch (AgsPackagesException e)
                    {
                        return (entry, versions: (List<string>)null, problem: e.Message);
                    }
                }).ToList();

                foreach (var lookup in await Task.WhenAll(lookups))
                {
                    var current = installed.FirstOrDefault(p => p.Name == lookup.entry.Name);
                    packages.Add(new AgsPackage(lookup.entry.Name, current?.Version, current != null && !current.TarballExists,
                        lookup.versions, true, lookup.problem));
                }
            }

            foreach (var current in installed.Where(p => packages.All(row => row.Name != p.Name)))
                packages.Add(new AgsPackage(current.Name, current.Version, !current.TarballExists, null, false,
                    connection != null ? "Not in the catalog." : null));

            packages.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return new AgsPackageList(packages, connection != null, connection?.Login, problem);
        }

        public static async Task<IReadOnlyList<string>> GetVersionsAsync(string name, string user)
        {
            var connection = await ConnectAsync(user, false);
            var entry = FindEntry(connection, name);
            var tags = await connection.Client.GetVersionTagsAsync(entry.Repo, entry.TagPrefix);
            return tags.Select(t => t.Version.ToString()).ToList();
        }

        /// <summary>
        /// Installs or updates <paramref name="name"/> to <paramref name="version"/>: the tagged
        /// folder becomes UPM/name-version.tgz, the manifest depends on it, and the previous
        /// tarball is deleted once the Package Manager has resolved the new one. If the Package
        /// Manager fails, the manifest stays as it was and the new tarball is deleted.
        /// </summary>
        public static async Task InstallAsync(string name, string version, string user)
        {
            BeginOperation();
            var work = Path.Combine(ProjectPackages.ProjectRoot, "Temp", "AgsPackages", Guid.NewGuid().ToString("N"));
            try
            {
                var connection = await ConnectAsync(user, false);
                var entry = FindEntry(connection, name);
                var wanted = entry.NormalizeVersion(version);
                var tags = await connection.Client.GetVersionTagsAsync(entry.Repo, entry.TagPrefix);
                var tag = tags.FirstOrDefault(t => t.Version.ToString() == wanted)
                    ?? throw new AgsPackagesException(tags.Count == 0
                        ? $"{name} has no versions: no tags named {entry.TagPrefix}<version> in {entry.Repo}."
                        : $"{name} has no version {wanted}. Available: {string.Join(", ", tags.Select(t => t.Version))}.");

                Log($"Installing {name} {wanted} from {entry.Repo} ({tag.Name})...");
                var commit = await connection.Client.ResolveCommitAsync(entry.Repo, tag);
                Directory.CreateDirectory(work);
                var zip = Path.Combine(work, "source.zip");
                await connection.Client.DownloadZipballAsync(entry.Repo, commit, zip);

                var fileName = ProjectPackages.TarballFileName(name, wanted);
                var built = Path.Combine(work, fileName);
                var source = $"{entry.Repo} at {tag.Name}";
                await Task.Run(() =>
                {
                    var files = PackageArchive.ReadFolder(zip, entry.Path);
                    PackageArchive.Validate(files, name, wanted, source);
                    TarGz.Write(built, files);
                });

                var target = Path.Combine(ProjectPackages.TarballFolder, fileName);
                var dependency = ProjectPackages.Dependency(fileName);
                var current = ProjectPackages.Find(name);
                var sameDependency = current != null && current.Dependency == dependency;
                if (sameDependency && File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(built)))
                {
                    Log($"{name} {wanted} is already installed.");
                    return;
                }

                Directory.CreateDirectory(ProjectPackages.TarballFolder);
                MakeWritable(target);
                File.Copy(built, target, true);
                await RunPackageManagerAsync(() => Upm.AddAsync(dependency), current?.TarballPath, target);
                if (sameDependency)
                    UnityEditor.PackageManager.Client.Resolve();
                Log($"Installed {name} {wanted} as {ProjectPackages.TarballFolderName}/{fileName}.");
            }
            finally
            {
                EndOperation();
                TryDeleteDirectory(work);
            }
        }

        /// <summary>Removes the package from the manifest and, once the Package Manager is done, deletes its tarball.</summary>
        public static async Task UninstallAsync(string name)
        {
            BeginOperation();
            try
            {
                var current = ProjectPackages.Find(name)
                    ?? throw new AgsPackagesException($"{name} isn't installed from the {ProjectPackages.TarballFolderName} folder.");
                Log($"Uninstalling {name}...");
                await RunPackageManagerAsync(() => Upm.RemoveAsync(name), current.TarballPath, null);
                Log($"Uninstalled {name}.");
            }
            finally
            {
                EndOperation();
            }
        }

        static CatalogEntry FindEntry(Connection connection, string name) =>
            connection.Catalog.Find(name) ?? throw new AgsPackagesException($"{name} is not in the catalog.");

        static void BeginOperation()
        {
            if (IsBusy)
                throw new AgsPackagesException("Another AGS Packages operation is in progress.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new AgsPackagesException("Unity is compiling or importing. Try again when it's done.");
            busy = true;
            // The domain reload the Package Manager triggers waits until the operation has finished.
            EditorApplication.LockReloadAssemblies();
        }

        static void EndOperation()
        {
            busy = false;
            EditorApplication.UnlockReloadAssemblies();
        }

        [Serializable]
        sealed class PendingOperation
        {
            public string previousTarball;
            public string newTarball;
        }

        /// <summary>
        /// Runs a Package Manager change and then deletes whichever of the two tarballs the
        /// manifest no longer depends on. The tarballs are kept in SessionState first, so the
        /// cleanup also happens if a domain reload cuts the operation short.
        /// </summary>
        static async Task RunPackageManagerAsync(Func<Task> change, string previousTarball, string newTarball)
        {
            SessionState.SetString(PendingKey, JsonUtility.ToJson(new PendingOperation
            {
                previousTarball = previousTarball,
                newTarball = newTarball,
            }));
            try
            {
                await change();
            }
            finally
            {
                FinishPending();
            }
        }

        // Only file operations, so it runs right away: a delayCall can wait indefinitely in an
        // Editor that's idle in the background.
        [InitializeOnLoadMethod]
        static void FinishPendingAfterReload() => FinishPending();

        static void FinishPending()
        {
            var json = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(json))
                return;
            SessionState.EraseString(PendingKey);

            var pending = JsonUtility.FromJson<PendingOperation>(json);
            var referenced = ProjectPackages.ReferencedTarballs();
            foreach (var path in new[] { pending.previousTarball, pending.newTarball })
            {
                if (string.IsNullOrEmpty(path) || referenced.Contains(Path.GetFullPath(path)) || !File.Exists(path))
                    continue;
                try
                {
                    MakeWritable(path);
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Logs.Warning($"Could not delete {path}: {e.Message}");
                }
            }
        }

        // Version control can leave checked-in files read-only.
        static void MakeWritable(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        static void Log(string message) => Logs.Info(message);
    }
}
