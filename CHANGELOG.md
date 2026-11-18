# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases 1.0.1 to 1.0.11 (the Capacitor app) are listed on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases); this file starts with the .NET app.

## [Unreleased]

### Changed

- The welcome screen is built with the Radzen component library, the first screen of the move to Radzen. Its texts
  and colours stay the same; buttons and text spacing may look slightly different.
- The card form is built with Radzen components too: drop-downs for the municipality and sector, check-box lists for
  the areas and tolerance codes, and date pickers. Texts and checks stay the same; the dates now open a calendar and
  show in the format of the phone's language.
- The car dialog is built with Radzen components: buttons, a switch for the parking alerts, preset buttons and a
  number field for the timer. Texts and behaviour stay the same; the minutes field now has up and down arrows.

### Fixed

- The card form refuses a card whose "valid from" date is after its "valid until" date, and a card whose municipality
  is not known, with a message, instead of saving a card that could never cover a spot.

## [1.0.21] - 2026-11-17

No issue tracker entry exists for this work; it ships in pull request #1.

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
