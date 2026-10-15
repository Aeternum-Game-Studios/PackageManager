# Changelog

## [1.1.0] - 2026-10-15

### Added

- Signed tarballs: when the GitHub release of a version's tag has an asset named
  `<name>-<version>.tgz`, it's installed as it is (so a tarball signed by Unity keeps its
  signature), after checking that it holds exactly the tagged package folder plus Unity's
  `.attestation.p7m` (a packer may leave out `.gitignore` and `.npmignore`). Versions without one
  are built from the tag as before.

## [1.0.0] - 2026-10-01

### Added

- Window > AGS Packages: the catalog's packages with their installed and available versions;
  install, update, downgrade and uninstall. Read-only without access to GitHub.
- `Aeternum.Packages.AgsPackages`: the same from code (`ListAsync`, `GetVersionsAsync`,
  `InstallAsync`, `UninstallAsync`, `GetInstalled`).
- `Aeternum.Packages.AgsPackagesCli`: entry points for `-executeMethod` (`Install`, `Uninstall`,
  `ListVersions`, `List`).
- Reproducible tarballs: the same package files always give the same `.tgz` bytes.
