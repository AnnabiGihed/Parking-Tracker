# ADR-0015: Require sign-in in the mobile app and keep the driver's data on the phone

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The owner decided that sign-in is required in the mobile app (not optional). Today the app has no account at all; the
card, the parked car and the timer live on the phone (ADR-0004, ADR-0008), and the app adds home, work and favourite
places in a local database (ADR-0018). A mandatory account raises the question of what the server should then know
about the driver, and whether the driver's data should follow the account to another phone.

The decision runs on the phone (ADR-0009), so the server does not need the card or the places to answer. The card,
the plate and the home and work addresses are the most sensitive data the app holds. A reinstall already asks for the
card again, and the card photo scan (ADR-0003) makes re-entry quick.

Options considered for what the server holds:

1. Nothing of the driver's data: the server knows the account, its sessions and day-precision activity.
1. An end-to-end encrypted backup of the device database on the server, with a key derived on the device.
1. Plain server-side storage of the card and places, synchronised across phones.

## Decision

We require sign-in and keep the driver's data on the phone only (option 1).

- **Flow.** First start: the start-up gate runs the saved-data migrations (ADR-0008, ADR-0018), then shows the sign-in
  screen; no answer is given while signed out. The upgrade from v1.0.x keeps the migrated data and asks for sign-in
  once. Network errors never sign the user out; only a refresh token the API rejects does.
- **Binding.** After the first sign-in, the device database stores a SHA-256 hash of the Google account ID. Migrated
  data belongs to the first account that signs in. A different account signing in later is asked before the previous
  account's local data is wiped. Sign-out keeps the data; erasure wipes it.
- **What syncs: nothing.** The card, plate, places, parked car and timer are never sent to the server. Tokens live in
  `SecureStorage`, not in the database.
- **What the server stores.** A keyed hash (HMAC-SHA256 with a server key) of the Google `sub`, created on and last
  seen on (day precision), and session rows with hashed refresh tokens. No e-mail, no name, no positions, no bounding
  boxes in logs next to an account.
- **Retention.** Erasure on request (`DELETE /api/v1/me`, ADR-0014); accounts inactive for 24 months are deleted;
  backups keep an erased account at most 6 months, and the erasure log is re-applied after a restore.
- **Data-protection note.** The privacy notice (apex page of `<domain>` and the app's about screen) lists what is
  stored where, the purposes (contract for sign-in, legitimate interest for abuse protection), the retention above and
  how to remove the app's access in the Google account. The app now processes identified users' query areas
  transiently on the server; they are not stored.

A multi-device or backup requirement would reopen option 2 in a new ADR.

## Consequences

### Positive

- The server holds almost nothing personal; a database leak exposes no card, plate or address.
- No encryption keys to manage across devices.
- Per-account rate limiting and abuse protection become possible.

### Negative

- The first start needs network, Google Play services and a Google account; drivers who refuse to sign in cannot use
  the app.
- A new phone or a reinstall starts empty: card and places are entered again.
- The upgrade from v1.0.x adds a sign-in step that existing users did not have.
- If the API is down at the very first start, the app cannot be used until it is back.

## Alternatives considered

- **Encrypted backup on the server** - rejected for now. A key derived on the device is lost with the phone, which
  removes the benefit; a key the server can recover defeats the encryption. The benefit is small next to the card
  photo scan.
- **Plain server-side storage** - rejected. It turns the server into a store of identified home addresses and card
  numbers for no functional need.
- **Optional sign-in with a signed-out mode** - rejected by the owner's decision of 2026-11-17.
