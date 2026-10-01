# Changelog

## [1.0.0] - 2026-10-01

### Added

- Window > AGS Packages: the catalog's packages with their installed and available versions;
  install, update, downgrade and uninstall. Read-only without access to GitHub.
- `Aeternum.Packages.AgsPackages`: the same from code (`ListAsync`, `GetVersionsAsync`,
  `InstallAsync`, `UninstallAsync`, `GetInstalled`).
- `Aeternum.Packages.AgsPackagesCli`: entry points for `-executeMethod` (`Install`, `Uninstall`,
  `ListVersions`, `List`).
- Reproducible tarballs: the same package files always give the same `.tgz` bytes.
