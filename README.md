# AGS Packages

A Unity Editor package that installs, updates and uninstalls private packages as **tarballs
committed with the project**. A project that uses it opens and builds without GitHub access or
credentials: its private packages are plain `.tgz` files next to `Assets/`, referenced from
`Packages/manifest.json` with `file:` paths. GitHub is only needed to change a version.

- Package: `com.aeternum.packages`, namespace `Aeternum.Packages`.
- Unity 6000.0 or later, Windows. Git for Windows to install or update packages.

## How it works

1. The project's `ProjectSettings/AgsPackages.json` says where the **catalog** is: a JSON file in a
   private repository listing the packages, the repository and folder of each, and the prefix of
   its version tags.
2. With the GitHub credential Git already has on the machine (Git Credential Manager), AGS
   Packages reads the catalog and each package's tags. A package's versions are its tags named
   `<tagPrefix><version>`, such as `tools-v1.2.0`.
3. Installing a version downloads the source archive of the tagged commit from the GitHub API,
   takes the package's folder and writes it as `UPM/<name>-<version>.tgz` (or installs the release's
   own tarball, if it has one: see [Signed tarballs](#signed-tarballs)). The manifest then
   depends on `file:../UPM/<name>-<version>.tgz` (through the Package Manager, which resolves it),
   and the previous tarball is deleted. If the Package Manager fails, the manifest is left as it
   was and the new tarball is deleted.
4. You check in `UPM/`, `Packages/manifest.json` and `Packages/packages-lock.json`. AGS Packages only
   changes files.

The tarball holds exactly the files of the package folder at that commit, byte for byte, and is
reproducible: the same files always give the same `.tgz`, whoever generates it, so reinstalling a
version changes nothing in version control.

## Signed tarballs

Unity 6.3 and later warn that a tarball "has been installed without a signature". A signed tarball
can only come out of Unity, with an account of the organization that signs it, so AGS Packages
doesn't sign: it installs a tarball that was signed once, when the version was released.

### Automatically, when a tag is pushed

This repository has a reusable workflow, `.github/workflows/sign-package.yml`, that exports the
tag, checks that `package.json` has the tag's version, signs the package with Unity's UPM CLI
(`upm pack`) and attaches the `.tgz` to the tag's release. Set up once for the organization:

- a Unity Cloud **service account** with the **Package Manager Package Signer** role, and these
  GitHub organization secrets, available to the package repositories:
  `UPM_SERVICE_ACCOUNT_KEY_ID`, `UPM_SERVICE_ACCOUNT_KEY_SECRET` and `UNITY_ORGANIZATION_ID`.

Then each package repository has a workflow that calls it, with its own tag pattern, folder and
prefix (the ones in the catalog):

```yaml
name: Release
on:
  push:
    tags: ['v*']
  workflow_dispatch:
    inputs:
      tag:
        description: Version tag to sign, such as v1.0.0
        required: true
jobs:
  sign:
    permissions:
      contents: write
    uses: Aeternum-Game-Studios/PackageManager/.github/workflows/sign-package.yml@main
    with:
      tag: ${{ inputs.tag || github.ref_name }}
      package-path: ''
      tag-prefix: 'v'
    secrets: inherit
```

Pushing a version tag signs it; a tag pushed before the workflow existed is signed by running it by
hand with the tag.

### By hand

1. Export the tag with the repository's line endings and without `.git` (Git's `core.autocrlf`
   would change the files, and the check below would refuse them):

   ```
   git -C <repository> -c core.autocrlf=false archive -o C:\tmp\tools-1.2.0.zip v1.2.0
   Expand-Archive C:\tmp\tools-1.2.0.zip C:\tmp\tools-1.2.0
   ```

2. Sign its package folder with Unity (or with the Export button of the Package Manager window):

   ```
   Unity.exe -batchmode -username <email> -password <password> -upmPack C:\tmp\tools-1.2.0\unity\com.example.tools C:\tmp\signed\com.example.tools-1.2.0.tgz -cloudOrganization <organization id>
   ```

3. Attach the `.tgz` to the GitHub release of the tag, named exactly `<name>-<version>.tgz`.

When the release of the version's tag has that asset, installing the version downloads it and
installs it as it is, after checking that it holds exactly the tagged package folder: the same
paths with the same bytes. Hidden paths (names starting with a dot: Unity's `.attestation.p7m`,
`.gitignore`, `.github`) aren't compared, because Unity never imports them and packers differ on
them, and the package's `package.json` is compared as JSON, because Unity's packer writes it again
(a CRLF one comes out LF). Anything else and the install fails. A version without the asset is built from the tag as before. Reinstalling a version
whose release gained a signed tarball replaces the unsigned one.

Every manifest dependency on a `.tgz` in `UPM/` is treated as managed by AGS Packages.

## Install

Add the package to `Packages/manifest.json` by its Git URL and release tag:

```json
"com.aeternum.packages": "https://github.com/Aeternum-Game-Studios/PackageManager.git#v1.0.0"
```

To update it, change the tag.

## Set up a project

Create `ProjectSettings/AgsPackages.json`:

```json
{
  "catalog": {
    "repo": "owner/catalog-repository",
    "path": "catalog.json",
    "ref": "main"
  }
}
```

`path` defaults to `catalog.json` and `ref` to the repository's default branch.

## Catalog format

```json
{
  "packages": [
    {
      "name": "com.example.tools",
      "repo": "owner/tools-repository",
      "path": "unity/com.example.tools",
      "tagPrefix": "tools-v"
    }
  ]
}
```

- `path`: the package's folder in the repository (the one with `package.json`); empty for the root.
- `tagPrefix`: what comes before the version in the tag name; empty if tags are bare versions.
- The `version` in the package's `package.json` must be the tag's version, or installing fails.

To read the catalog and install a package, a GitHub account needs read access to the catalog's
repository and to the package's.

## Window

**Window > AGS Packages** lists the installed packages and, with access, every catalog package
with its versions. Pick a version and press **Install**, **Update** or **Downgrade**;
**Uninstall** removes a package.

- **GitHub user**: the account whose Git credential to use, saved for your Windows user. Set it
  when Git knows more than one GitHub account; otherwise Git can't choose one without asking.
- **Connect**: lets Git Credential Manager sign you in if Git has no credential for that user.
  Nothing else ever opens a sign-in window.
- Without access (no Git, no credential, no access to the catalog, or no network) the window shows
  what's installed and changes nothing.

## Scripting

```csharp
using Aeternum.Packages;

AgsPackageList list = await AgsPackages.ListAsync();       // installed + catalog, never throws for access
IReadOnlyList<string> versions = await AgsPackages.GetVersionsAsync("com.example.tools");
await AgsPackages.InstallAsync("com.example.tools", "1.2.0"); // installs, updates or downgrades
await AgsPackages.UninstallAsync("com.example.tools");
IReadOnlyList<AgsPackage> installed = AgsPackages.GetInstalled(); // offline
AgsPackages.GitHubUser = "your-github-user";
```

Call them from the main thread. Each one logs its outcome to the Console, so a call that isn't
awaited (from `unity command eval`, say) still reports. Failures throw `AgsPackagesException`.

## Command line

`AgsPackagesCli` has entry points for `-executeMethod`. Run Unity in batch mode **without
`-quit`**: the Package Manager works on later Editor updates, so each method exits Unity itself.

```
Unity.exe -batchmode -projectPath <project> -logFile - -executeMethod Aeternum.Packages.AgsPackagesCli.Install -agsPackage com.example.tools -agsVersion 1.2.0
```

| Method | Arguments |
| --- | --- |
| `Install` | `-agsPackage <name> -agsVersion <version or full tag>` |
| `Uninstall` | `-agsPackage <name>` |
| `ListVersions` | `-agsPackage <name>` |
| `List` | |

Every method also takes `-agsUser <GitHub user>` for this run (default: the saved GitHub user).
Exit code 0 on success, 1 on failure, 2 for bad arguments.

## Credentials

- The credential comes from `git credential fill` for `https://github.com` and the GitHub user.
  Git is told not to prompt, except when you press **Connect**.
- The token lives in memory for one operation. It's sent only to `api.github.com`, never written to
  a project file, the manifest, the lock file, EditorPrefs or the log, and never part of an error
  message. A credential that works after **Connect** is kept by Git Credential Manager in the
  Windows Credential Manager.
- GitHub's download links for source archives carry their own short-lived token: AGS Packages
  follows them without the Authorization header and never logs them.

## Limitations

- This package is itself a Git dependency: resolving it into an empty `Library/` needs Git and
  access to github.com (it's public, so no credentials). The packages it installs need neither.
- Unity 6.3 and later log "has been installed without a signature" for these tarballs, because
  they aren't signed.
- GitHub's source archives contain Git LFS pointers, not LFS files: a package folder can't rely
  on Git LFS.

## License

MIT. See [LICENSE.md](LICENSE.md).
