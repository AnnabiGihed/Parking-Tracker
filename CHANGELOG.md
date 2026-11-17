# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases 1.0.1 to 1.0.11 (the Capacitor app) are listed on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases); this file starts with the .NET app.

## [Unreleased]

The first build of `main` after this change is merged publishes it as the next release, above v1.0.11, and installs
over the earlier version.

No issue tracker entry exists for this work; add the pull request number here once it exists (PR #TBD).

### Breaking changes

- Android 6.0 is no longer supported: the app needs Android 7.0 or later.

### Changed

- The app was rebuilt on a new technology (.NET MAUI) and gives the same answers as before: they were compared with
  the earlier version at 17 recorded places. The screen, the map, the card, the car, the parking alerts and the timer
  look and work as before.
- The parking alerts take your position from Google Play services and run as a foreground location service with a
  small notification.
- The first launch after the update brings your saved card, car, timer, alerts switch, map layers and location-banner
  snooze over from the earlier version, so updating keeps them.

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
