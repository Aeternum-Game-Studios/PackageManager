using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aeternum.Packages
{
    /// <summary>Window > AGS Packages: the studio's packages, their installed and available versions.</summary>
    internal sealed class AgsPackagesWindow : EditorWindow
    {
        AgsPackageList list;
        bool loading;
        string operation;
        string userField;
        readonly Dictionary<string, int> selectedVersion = new Dictionary<string, int>();
        Vector2 scroll;

        [MenuItem("Window/AGS Packages")]
        static void Open() => GetWindow<AgsPackagesWindow>("AGS Packages");

        void OnEnable()
        {
            userField = AgsPackages.GitHubUser;
            minSize = new Vector2(480, 200);
            Refresh(false);
        }

        async void Refresh(bool interactive)
        {
            if (loading)
                return;
            loading = true;
            Repaint();
            try
            {
                list = await PackageService.ListAsync(AgsPackages.GitHubUser, interactive);
                selectedVersion.Clear();
            }
            catch (Exception e)
            {
                // Listing never changes anything; whatever went wrong stays in the window.
                list = new AgsPackageList(new AgsPackage[0], false, null, e.Message);
            }
            finally
            {
                loading = false;
                Repaint();
            }
        }

        async void Run(string description, Func<Task> change)
        {
            operation = description;
            Repaint();
            try
            {
                await change();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("AGS Packages", e is AgsPackagesException ? e.Message : $"{e.GetType().Name}: {e.Message}", "OK");
            }
            finally
            {
                operation = null;
                Refresh(false);
            }
        }

        void OnGUI()
        {
            var idle = !loading && operation == null && !AgsPackages.IsBusy;
            DrawToolbar(idle);

            if (operation != null)
                EditorGUILayout.HelpBox(operation + "...", MessageType.None);
            else if (loading)
                EditorGUILayout.HelpBox("Reading the catalog...", MessageType.None);
            else if (list?.Problem != null)
                EditorGUILayout.HelpBox(list.Problem + "\nShowing the installed packages only; they can't be changed.", MessageType.Info);
            else if (list?.GitHubLogin != null)
                EditorGUILayout.LabelField($"Connected to GitHub as {list.GitHubLogin}.", EditorStyles.miniLabel);

            if (list == null)
                return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (list.Packages.Count == 0)
                EditorGUILayout.LabelField("No packages.", EditorStyles.centeredGreyMiniLabel);
            foreach (var package in list.Packages)
                DrawPackage(package, idle && list.CanChange);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(
                $"Installing writes {ProjectPackages.TarballFolderName}/ and Packages/manifest.json. Check them in yourself.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawToolbar(bool idle)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("GitHub user", GUILayout.ExpandWidth(false));
                using (new EditorGUI.DisabledScope(!idle))
                {
                    var user = EditorGUILayout.DelayedTextField(userField, EditorStyles.toolbarTextField, GUILayout.Width(160));
                    if (user != userField)
                    {
                        userField = user.Trim();
                        AgsPackages.GitHubUser = userField;
                        Refresh(false);
                    }
                    if (GUILayout.Button(new GUIContent("Connect", "Sign in through Git Credential Manager if Git has no credential for this user."),
                            EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                        Refresh(true);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                        Refresh(false);
                }
            }
        }

        void DrawPackage(AgsPackage package, bool canChange)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(package.Name, EditorStyles.boldLabel, GUILayout.MinWidth(160));
                    var installed = package.InstalledVersion ?? "not installed";
                    EditorGUILayout.LabelField(package.TarballMissing ? $"{installed} (tarball missing)" : installed, GUILayout.Width(150));

                    var versions = package.AvailableVersions;
                    var packageCanChange = canChange && package.Problem == null;
                    using (new EditorGUI.DisabledScope(!packageCanChange || versions.Count == 0))
                    {
                        if (!selectedVersion.TryGetValue(package.Name, out var index))
                            index = Math.Max(0, versions.ToList().IndexOf(package.InstalledVersion ?? ""));
                        var labels = versions.Count > 0 ? versions.ToArray() : new[] { "—" };
                        index = EditorGUILayout.Popup(Math.Min(index, labels.Length - 1), labels, GUILayout.Width(110));
                        selectedVersion[package.Name] = index;

                        var chosen = versions.Count > 0 ? versions[index] : null;
                        var label = ActionLabel(package, chosen);
                        using (new EditorGUI.DisabledScope(label == "Installed"))
                        {
                            if (GUILayout.Button(label, GUILayout.Width(80)))
                                Run($"Installing {package.Name} {chosen}", () => AgsPackages.InstallAsync(package.Name, chosen));
                        }
                    }

                    using (new EditorGUI.DisabledScope(!canChange || !package.IsInstalled))
                    {
                        if (GUILayout.Button("Uninstall", GUILayout.Width(80)) &&
                            EditorUtility.DisplayDialog("AGS Packages", $"Uninstall {package.Name} from this project?", "Uninstall", "Cancel"))
                            Run($"Uninstalling {package.Name}", () => AgsPackages.UninstallAsync(package.Name));
                    }
                }

                if (package.Problem != null)
                    EditorGUILayout.LabelField(package.Problem, EditorStyles.wordWrappedMiniLabel);
            }
        }

        static string ActionLabel(AgsPackage package, string chosen)
        {
            if (!package.IsInstalled)
                return "Install";
            if (chosen == null)
                return "Installed";
            if (chosen == package.InstalledVersion)
                return package.TarballMissing ? "Reinstall" : "Installed";
            return SemVersion.TryParse(chosen, out var target) && SemVersion.TryParse(package.InstalledVersion, out var current) &&
                   target.CompareTo(current) < 0
                ? "Downgrade"
                : "Update";
        }
    }
}
