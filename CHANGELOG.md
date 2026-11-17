# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases 1.0.1 to 1.0.11 (the Capacitor app) are listed on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases); this file starts with the .NET
migration.

## [Unreleased]

The .NET migration is in progress and is not part of any release yet: the published APK is still the Capacitor app.

### Changed

- Started the .NET 10 migration on Pivot.Framework: solution skeleton, reference data as embedded JSON, the pure
  decision logic, the spot decision, the area status and the free-spot search, and the HTTP clients of the live
  sources. None of it is shipped yet.
- Organised the .NET solution in the Pivot.Framework canonical layout: feature folders in every project, a test tree
  that mirrors `src/`, and the layout adapted to a mobile client in ADR-0006.
- Added the Meziantou analyzers, the StyleCop documentation settings (`stylecop.json`) and the target framework in
  `Directory.Build.props`, and fixed the findings they raised.
- Added the `.NET quality gate` GitHub workflow: restore, format check, Release build and unit tests on every push and
  pull request into `main`.
- Added `CONTRIBUTING.md` and this changelog.
