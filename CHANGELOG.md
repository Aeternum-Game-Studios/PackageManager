# Changelog

## [1.1.0] - 2026-10-15

### Added

- Signed tarballs: when the GitHub release of a version's tag has an asset named
  `<name>-<version>.tgz`, it's installed as it is (so a tarball signed by Unity keeps its
  signature), after checking that it holds exactly the tagged package folder (hidden paths, such
  as the signature or `.gitignore`, aren't compared). Versions without one are built from the
  tag as before.
- `.github/workflows/sign-package.yml`: a reusable workflow that signs a package's version tag
  with the organization's Unity service account and attaches the `.tgz` to its release.

## [1.0.0] - 2026-10-01

### Added

- Window > AGS Packages: the catalog's packages with their installed and available versions;
  install, update, downgrade and uninstall. Read-only without access to GitHub.
- `Aeternum.Packages.AgsPackages`: the same from code (`ListAsync`, `GetVersionsAsync`,
  `InstallAsync`, `UninstallAsync`, `GetInstalled`).
- `Aeternum.Packages.AgsPackagesCli`: entry points for `-executeMethod` (`Install`, `Uninstall`,
  `ListVersions`, `List`).
- Reproducible tarballs: the same package files always give the same `.tgz` bytes.
