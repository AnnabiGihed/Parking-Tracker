# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases 1.0.1 to 1.0.11 (the Capacitor app) are listed on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases); this file starts with the .NET app.

## [Unreleased]

The first build of `main` after this change is merged publishes it as the next release, above v1.0.11, and installs
over the earlier version.

### Changed

- Rebuilt the app as a .NET 10 MAUI app (Blazor Hybrid, Pivot.Framework) with the same answers: the decision logic is
  ported to C# and tested against the answers of the earlier version at 17 recorded places. The screen, the map, the
  card, the car, the parking alerts and the timer look and work as before; Android 7.0 or later is needed (the earlier
  version needed 6.0).
- The parking alerts use the fused location provider of Google Play services and an own foreground location service.
- The release workflow builds with .NET, signs with the same key and refuses to publish when the versionCode is not
  above the latest release's or the signing certificate differs, so that the update installs over the old app.
- The app brings the saved card, car, timer, alerts switch, map layers and location-banner snooze over from the
  earlier version on the first launch, so updating keeps them.

### Added

- The parking timer survives a restart of the phone and uses exact alarms when allowed.
- Card scan on the phone: "Scan my card" takes a photo (or picks one), reads it with ML Kit on the device and fills only
  the fields it found; the photo never leaves the phone and is deleted after reading.
- Android emulator test and upgrade test of the saved data in CI, and a quality gate workflow for the .NET solution.

### Removed

- The web version (browser, GitHub Pages, service worker) and its development server and CORS proxy. The app calls the
  data services directly.

### Security

- The saved card is stored encrypted on the phone.
