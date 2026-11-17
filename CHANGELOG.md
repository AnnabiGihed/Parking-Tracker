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
- Added the view models and Blazor components of the main screen for the .NET app: the spot check keeps the answer of the
  newest check only, a "can't verify" cannot be overwritten by a late check, a failing background check is told to the user,
  and the map script builds marker labels as text and handles popup buttons with one delegated listener.
- Organised the .NET solution in the Pivot.Framework canonical layout: feature folders in every project, a test tree
  that mirrors `src/`, and the layout adapted to a mobile client in ADR-0006.
- Added the .NET MAUI Android host (position, permissions, links, connection, saved settings in the JSON of the first
  version, vibration on a change of coverage), the opt-in test hooks, the Node emulator test in
  `test/ParkingTracker.E2E.Tests` and the workflow `maui-android.yml`, which builds the APKs on the migration branch.
  The host supports Android 7.0 (API 24) and later. Nothing is released yet.
- Added the Meziantou analyzers, the StyleCop documentation settings (`stylecop.json`) and the target framework in
  `Directory.Build.props`, and fixed the findings they raised.
- Added the `.NET quality gate` GitHub workflow: restore, format check, Release build and unit tests on every push and
  pull request into `main`.
- Made the emulator test explain a missing position: a `ptTest.screenState()` hook, a log of the start, failures and first fix
  of the location tracker, a fix re-sent during the wait, and a "finding your position" chip that no longer overwrites the
  chips of a check that a fix already started.
- Replaced the MAUI geolocation listener by the fused location provider of Google Play services (ADR-0002), which kept
  reporting "position unavailable" on the emulator and stopped delivering fixes.
- Added `CONTRIBUTING.md` and this changelog.
