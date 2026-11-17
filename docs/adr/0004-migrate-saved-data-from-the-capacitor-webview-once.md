# ADR-0004: Migrate saved data from the Capacitor WebView storage once; keep the card in SecureStorage

- Status: Superseded by ADR-0008
- Date: 2026-11-16
- Deciders: Gihed Annabi

## Context

The Capacitor app keeps all user data in the WebView's `localStorage` of origin `https://localhost`:

| Key | Content |
| --- | --- |
| `pt.card` | Card JSON: number, plate, municipality, sector, mailles, lims, tolerances, from, to |
| `pt.car` | Parked car: lat, lon, time, status text, place |
| `pt.timer` | Parking timer: end time, minutes |
| `pt.layers` | Map layer switches |
| `pt.bgAlerts` | Parking alerts on or off |
| `pt.permSnooze` | "Later" on the location banner, until a time |

After the update (ADR-0001) the UI runs in a MAUI `BlazorWebView`, which serves the app from a different origin, so
`localStorage` of `https://localhost` is not visible to it. Android keeps the WebView data directory
(`app_webview`) across an update of the same package, so the old values are still on the phone. The README promises
that an update "keeps your card". The card holds personal data (card number, plate).

Options considered:

1. One-time migration: on the first start of the MAUI version, a hidden Android `WebView` loads `https://localhost/`
   with a `WebViewClient` that answers that request itself (no network), so the page runs in the old origin, reads the
   six keys and returns them to C#; C# stores them and records that the migration ran.
2. Ask the user to enter the card again after the update.
3. Make the `BlazorWebView` use the old origin `https://localhost`.

## Decision

We migrate once (option 1), with re-entry as the fallback:

- Before the first Blazor page renders, `LegacyStorageMigrator` (Android only) runs if the preference
  `pt.migration` is absent. It copies each key it finds, unchanged, under the same key: `pt.card` into MAUI
  `SecureStorage` (encrypted with the Android keystore, owner decision 2026-11-16), every other key into MAUI
  `Preferences`. Then it writes `pt.migration` = `{ "version": 1, "found": [...], "at": ... }`. It never deletes the
  old WebView data.
- Because `SecureStorage` values cannot be decrypted after an Android backup is restored on another phone, the app
  excludes the `SecureStorage` file from Android backup with backup rules; a restored phone asks for the card again.
- C# owns these values from then on, through an `ISettingsStore` port; JavaScript no longer uses `localStorage` for
  them. The JSON shapes stay exactly as they are today, so the copy needs no conversion and old values stay readable.
- If no card was found, the welcome dialog says that the update could not bring the card over and asks for it again.
- A failure (timeout of 10 s, exception) is logged, records `pt.migration` with the error, and falls back to re-entry;
  it never blocks the app.
- CI proves the path with an upgrade test (see `docs/architecture.md`): install the Capacitor build, store a card,
  install the MAUI build over it, check the card is there.

## Consequences

### Positive

- The user keeps the card, the parked car, the timer and the settings across the update, as the README promises.
- The migration is isolated, runs once, and leaves the source data untouched, so it can be retried by a later build.

### Negative

- It relies on the WebView storing `localStorage` per origin in the shared `app_webview` directory and on Capacitor's
  origin being exactly `https://localhost`; both are checked by the upgrade test, not by documentation.
- The upgrade test needs both builds signed with the same throwaway key and the old Capacitor project built once more
  in CI, which lengthens the pipeline until the old code is gone.
- The card is encrypted at rest; after a restore to a new phone the user enters it again (backup rules exclude it).
- The old WebView data stays on the phone, unused.

## Alternatives considered

- **Ask for the card again** - rejected as the primary path. It breaks the README promise and annoys every user; it
  stays as the fallback.
- **Keep the old origin** - rejected. MAUI's `BlazorWebView` on Android serves from its own fixed origin and does not
  offer a supported setting to change it.
- **Store the card in `Preferences` as plain JSON** - rejected by the owner. The card holds personal data (card
  number, plate); `SecureStorage` encrypts it with the Android keystore at the cost of backup rules.
