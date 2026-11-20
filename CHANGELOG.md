# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Releases 1.0.1 to 1.0.11 (the Capacitor app) are listed on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases); this file starts with the .NET app.

## [Unreleased]

### Added

- The privacy notice is published at <https://parking.pivotsoftwares.com/privacy>.
- Home and Work shortcut chips under the search bar: tap one to see that place on the map and check it, or set it
  from an address or your position. The places stay on the phone, encrypted.
- A side menu, opened from the button at the start of the search bar, with Home, Work and your parking card and what
  is saved under each.
- Favourite places in the side menu, saved with the same pin-and-confirm picker (with a name you choose, up to 40
  characters, and at most 20 places) and shown as shortcuts under the search bar.

### Changed

- Setting Home or Work shows the place on the map with a pin you can drag, and saves only when you confirm. The
  Home and Work chips and the side menu open this map view; a saved place can be moved or removed there.
- Searching an address shows all the matching places to choose from; one match still goes straight to the map.
- The welcome screen is built with the Radzen component library, the first screen of the move to Radzen. Its texts
  and colours stay the same; buttons and text spacing may look slightly different.
- The card form is built with Radzen components too: drop-downs for the municipality and sector, check-box lists for
  the areas and tolerance codes, and date pickers. Texts and checks stay the same; the dates now open a calendar and
  show in the format of the phone's language.
- The car dialog is built with Radzen components: buttons, a switch for the parking alerts, preset buttons and a
  number field for the timer. Texts and behaviour stay the same; the minutes field now has up and down arrows.
- The layers popover is built with Radzen components: a switch for each map layer, a close button and the line that
  says what the map shows. Texts and behaviour stay the same; the switches look like Radzen switches.
- The status sheet is built with Radzen components: the answer card, the chips, the nearest free spot with its Waze and
  Google Maps buttons, the location banner and the search box. Texts and behaviour stay the same; buttons and spacing
  may look slightly different. This completes the move of the screens to Radzen.
- "Valid until" on the card form defaults to one year after "Valid from" unless you or a scan chose another date.
- The plate is written in capitals as you type.
- Home and Work are edited from the menu; the pencil buttons on the chips are gone. A chip for a place that is not set
  still opens its dialog.
- The back button closes the menu or the open dialog instead of leaving the app.

### Security

- The API trusts a client's address only when it comes from the shared proxy, and limits health checks to 60 a
  minute per address, so nobody can fake an address to get around the limits. The API also refuses new requests
  while 64 are already being handled.
- The residents' card (number and plate), the parked car's position and the saved places are kept in a local database on
  the phone, and their personal fields are encrypted one by one with a key that stays on the phone. The database, the
  key and the app's preferences are excluded from Android backup, so a restore on a new phone asks for the card
  again. Your saved card, car and timer are moved into the database once when you update; the old copies are kept for
  this release. This protects the stored values against a copied or backed-up file; it does not protect against someone
  who unlocks the phone.
- When the phone's protected storage loses the key of the saved data, the app no longer replaces the key silently and
  no longer treats the card as missing: the spot check answers "can't verify" with "Your saved card could not be read.
  Please enter it again.", the card line asks for the card, and entering it again makes a new key and replaces the
  unreadable data. A card or car that you delete or replace before the one-time move has finished is not brought back
  by the move at the next launch.

### Fixed

- The parking alert no longer fires in slow traffic or at a red light. The phone now counts as parked only when it
  stays within 15 m of the place where it stopped for 2 minutes, or when it stood still and then went no faster than
  walking for 3 minutes; in that case the alert names the place where the car stopped, not where you walked to.
- The card form refuses a card whose "valid from" date is after its "valid until" date, and a card whose municipality
  is not known, with a message, instead of saving a card that could never cover a spot.
- The "valid from" and "valid until" dates of the card form can be picked again: the calendar opened behind the form,
  so no date could be chosen. A picked date now shows as a short date in the phone's language, without a time, and
  picking it no longer brings up the keyboard; tap the calendar button to change it.
- The Home and Work chips under the search bar use the colours of the app, light or dark, like the car chip, instead of
  a light grey fill whose text could not be read in the dark theme. The other light buttons (car dialog, place dialog,
  "pay" button) follow the dark theme too.
- The Home and Work buttons are readable in light and dark mode; a place that is not set reads "Set home" or "Set work".
- The search bar no longer slides under the status bar on phones with an older system web view.

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
