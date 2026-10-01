using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Aeternum.Packages
{
    /// <summary>
    /// Entry points for -executeMethod. Run Unity with -batchmode and WITHOUT -quit: the Package
    /// Manager works on later Editor updates, so each method exits Unity itself when it's done.
    /// Exit code 0 on success, 1 on failure, 2 for bad arguments.
    /// <code>
    /// Unity.exe -batchmode -projectPath &lt;project&gt; -logFile - -executeMethod Aeternum.Packages.AgsPackagesCli.Install -agsPackage com.example.tools -agsVersion 1.2.0
    /// </code>
    /// Add -agsUser &lt;GitHub user&gt; to use that account's Git credential for this run only.
    /// </summary>
    public static class AgsPackagesCli
    {
        /// <summary>Installs or updates -agsPackage to -agsVersion.</summary>
        public static void Install() => Run(args =>
            PackageService.InstallAsync(Required(args, "-agsPackage"), Required(args, "-agsVersion"), User(args)));

        /// <summary>Uninstalls -agsPackage.</summary>
        public static void Uninstall() => Run(args =>
            PackageService.UninstallAsync(Required(args, "-agsPackage")));

        /// <summary>Logs the versions of -agsPackage, newest first.</summary>
        public static void ListVersions() => Run(async args =>
        {
            var name = Required(args, "-agsPackage");
            var versions = await PackageService.GetVersionsAsync(name, User(args));
            Logs.Info($"{name}: {(versions.Count == 0 ? "no versions" : string.Join(", ", versions))}");
        });

        /// <summary>Logs the installed packages and, with access, the catalog's.</summary>
        public static void List() => Run(async args =>
        {
            var list = await PackageService.ListAsync(User(args), false);
            if (list.Problem != null)
                Logs.Warning(list.Problem);
            foreach (var package in list.Packages)
                Logs.Info(package.ToString());
            if (list.Packages.Count == 0)
                Logs.Info("No packages.");
        });

        sealed class UsageException : Exception
        {
            public UsageException(string message) : base(message) { }
        }

        static async void Run(Func<string[], Task> operation)
        {
            int exitCode;
            try
            {
                await operation(Environment.GetCommandLineArgs());
                exitCode = 0;
            }
            catch (UsageException e)
            {
                Logs.Error(e.Message);
                exitCode = 2;
            }
            catch (AgsPackagesException e)
            {
                Logs.Error(e.Message);
                exitCode = 1;
            }
            catch (Exception e)
            {
                Logs.Error($"Unexpected {e.GetType().Name}: {e.Message}");
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        static string Required(string[] args, string option) =>
            Optional(args, option) ?? throw new UsageException($"Missing {option} <value>.");

        static string User(string[] args) => Optional(args, "-agsUser") ?? AgsPackages.GitHubUser;

        static string Optional(string[] args, string option)
        {
            var index = Array.IndexOf(args, option);
            if (index < 0)
                return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new UsageException($"{option} needs a value.");
            return args[index + 1];
        }
    }
}
