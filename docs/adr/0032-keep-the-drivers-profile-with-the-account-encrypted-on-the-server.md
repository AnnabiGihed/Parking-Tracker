# ADR-0032: Keep the driver's profile with the account, encrypted on the server

- Status: Accepted
- Date: 2026-11-26
- Deciders: Gihed Annabi
- Supersedes in part: ADR-0015, ADR-0031

## Context

ADR-0015 made sign-in mandatory and kept all of the driver's data on the phone ("what syncs: nothing"), and ADR-0031
kept the vehicle profile on the phone with no sync. ADR-0015 named the condition that would reopen the question: a
multi-device or backup requirement.

That requirement now exists. On 2026-11-26 the owner asked for proper account management, in which the home and work
places, the favourite places, the vehicle profile and the residents' parking card follow the Google account to a new
phone, and the side menu groups these settings under the account. Since S5 the API issues its own tokens per account
(ADR-0014), so an authenticated, per-account resource is available.

Forces:

- Home and work addresses, the card number and the plate identify a person and their home. A database dump, the
  monthly restore test and the nightly backups (ADR-0028) all copy the database.
- An end-to-end key must survive a lost phone, and Google sign-in provides no key the app could recover.
- One owner and few users: a whole-profile document is small and changes rarely, and two phones of the same driver
  seldom edit at the same moment.
- The phone must keep working offline (ADR-0009: the decision runs on the phone).

Options considered:

1. Keep everything on the phone (ADR-0015, unchanged).
1. An end-to-end encrypted backup, keyed on the device or on a passphrase.
1. Plain server-side rows for places, vehicle and card.
1. One profile document per account, encrypted on the server with a server key, synchronised with revisions.

## Decision

We keep the driver's profile with the account as one document per account, encrypted on the server (option 4).

- **What syncs.** Home, work, the favourite places, the vehicle profile and the residents' card, including its number
  and plate. The parked car, the timer and the card photos stay on the phone or are not kept (ADR-0003, ADR-0015).
- **Storage.** Table `account_profiles`: `account_id` (primary key, foreign key to the account, cascade delete),
  `revision` (concurrency token), `schema_version`, `key_id`, `payload` (`bytea`) and `updated_on` (day precision).
  The payload is the JSON document encrypted with AES-256-GCM. The associated data is the account id, the revision
  and the schema version, so a payload cannot be moved to another account, replayed under another revision or
  relabelled with another schema version. The key is a file in the
  server's secrets folder (`profile_key`, with a current and a previous key for rotation), mounted like the token
  signing keys. The owner keeps a copy in the password manager.
- **Document.** `schemaVersion`, `home`, `work`, `favourites` (at most 50), `vehicle` and `card`. Each section carries
  the `updatedAt` time the phone set when it changed. A section the user cleared is absent, and the optional `cleared`
  object (`home`, `work`, `vehicle`, `card`) holds the time of the clear, so that a clear beats an older value on
  another phone; the favourites list needs none, because it always carries its `updatedAt`. The field is additive: the
  schema version stays 1 and a document without it is valid. The API validates the plain document (32 KB at most,
  field lengths, coordinates inside the Brussels region) before it encrypts it.
- **API.** `GET /api/v1/me/profile` (200 with an `ETag`, 304 for a matching `If-None-Match`, 404 when there is no
  profile) and `PUT /api/v1/me/profile` (`If-Match` with the revision, or `If-None-Match: *` to create; 428 without
  either, 412 on a stale revision). A token-bucket rate limit `profile` per account.
- **Phone.** The phone database stays the working copy and works offline. It records the base revision, a changed
  flag and the hash of the bound account. It reads the profile after sign-in and when the app starts or resumes, and
  writes it shortly after each change and when the network returns. On a 412 it reads the server copy, merges section
  by section (the newest `updatedAt` wins; the favourites list is one section) and writes again.
- **First sign-in on a phone.** An empty server receives the phone's profile; an empty phone receives the server's.
  When both hold data, they are merged as above. A phone bound to another account wipes that account's profile after
  a confirmation and never uploads it.
- **Sign-out** writes pending changes, then wipes the synced sections from the phone; if the write fails the app warns
  before wiping. **Account deletion** deletes the profile with the account in one transaction, the erasure re-apply
  after a restore covers it, and the phone wipes all its data.
- **Scope of the encryption.** It protects against a leak of the database alone: a dump, SQL access or the restore-test
  container. It does not protect against the loss of the whole server, whose backups hold the key and the data
  together. The privacy notice states this.

This supersedes in part ADR-0015 (its "What syncs: nothing" and "What the server stores" rules; mandatory sign-in,
binding and retention remain) and ADR-0031's no-sync rule. When this ADR is accepted, both get a note pointing here.

## Consequences

### Positive

- A new phone or a reinstall gets the places, vehicle and card back after sign-in.
- A database dump or the restore test exposes no address, card number or plate.
- One small document per account keeps the server model, the API and the conflict rules simple.
- The phone keeps answering offline; the server is not on the decision path.

### Negative

- The server now holds identified, sensitive data: a compromise of the whole server exposes it, and backups keep a
  deleted profile up to 6 months.
- A new secret to provision, back up and rotate; losing `profile_key` makes every stored profile unreadable.
- Last writer wins per section: two phones editing the same section offline keep only the newest edit.
- Sign-out now empties the phone's settings, which is a change for existing users.
- The privacy notice, the processing record and the erasure path all change, and must be live before the app uploads.

## Alternatives considered

- **Keep everything on the phone (ADR-0015)** - rejected. It does not meet the owner's requirement that the data
  follows the account to a new phone.
- **End-to-end encrypted backup** - rejected. A key derived on the phone is lost with it; a passphrase is one more
  secret for the driver to keep, and a lost passphrase loses the data, which removes the benefit.
- **Plain server-side rows** - rejected. Every dump and backup would carry readable home addresses and card numbers,
  for no query the server needs to make.
- **Per-section resources with their own revisions** - rejected for now. More endpoints and more conflict states, for
  a document that is a few kilobytes and changes rarely.
