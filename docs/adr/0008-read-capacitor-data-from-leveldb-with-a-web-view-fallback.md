# ADR-0008: Read the Capacitor data from the LevelDB files first, with a hidden web view as the fallback

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi
- Supersedes: ADR-0004

## Context

ADR-0004 decided to migrate the saved data of the Capacitor version once, to keep the card in `SecureStorage`, and to
read the old `localStorage` of `https://localhost` through a hidden Android `WebView`. The rest of that decision still
holds. Only the way of reading the old values changes here, and it changes after the first implementation showed what
each way costs.

The old values live in Chromium's LevelDB directory `app_webview/Default/Local Storage/leveldb`, which Android keeps
across an update of the same package. There are two ways to read them:

- **Hidden web view.** It needs the origin `https://localhost` to be served by the app (a `WebViewClient` that answers
  the request with an empty page), it needs the main thread and a running web view engine, and it takes noticeably
  longer than reading files. Used alone it would put a web view start on the first launch of every update and make the
  start-up depend on the engine being ready.
- **Parsing the files.** It needs no web view, no origin and no network answer, and it is plain C# that unit tests run on
  any computer. But it reimplements a format that Chromium owns: Chromium may change the key layout or compress table
  blocks with Zstandard instead of Snappy, and a parser that does not know the new format can read nothing, or part
  of the data. Parsing alone is not enough for a promise as strong as "the update keeps your card".

## Decision

We keep LevelDB parsing as the primary reader and add the hidden web view of ADR-0004 as the fallback.

- `LegacyLocalStorageReader` (Infrastructure) reads the `*.log` and `*.ldb` files and returns the `pt.*` values. No
  directory, or a directory without database files, means a new install: a successful read without values.
- The hidden web view (`WebViewLegacyStorageReader`, host) is asked only when the directory exists but the parse fails:
  an unsupported compression, corrupt files, or database files from which no `pt.*` key could be decoded. The web view
  is not attached to a window, is created on the main thread, runs JavaScript, answers the request for
  `https://localhost/` with a tiny empty page, reads every `pt.*` key of `localStorage` with `JSON.stringify`, is given
  10 seconds, and is destroyed afterwards. It never writes.
- `CompositeLegacyStorageReader` (Infrastructure) joins the two behind the port `ILegacyStorageReader`
  (Application). A successful answer of either reader is final, an empty one included. Only when the fallback fails too
  does an attempt count as failed.
- Everything else of ADR-0004 stays: the six keys keep their names and JSON shapes; `pt.card` goes to `SecureStorage`,
  which the backup rules exclude from Android backup; every other key goes to `Preferences`; a value that already
  exists in the new app is never overwritten; the old storage is never changed or deleted; and when the card cannot be
  brought over, the welcome dialog asks for it again.

## Consequences

### Positive

- The normal path is fast, does not depend on a web view, and is covered by unit tests on byte layouts.
- A change of the Chromium format does not lose the card silently: the fallback lets Chromium decode its own files.
- The two readers are independent, so each can be replaced without touching the migration.

### Negative

- Two readers to maintain, and the web view reader is Android-only code that unit tests cannot run; the upgrade test on
  an emulator covers the files path, and the fallback is proven on a device only.
- A failure that no reader can settle is retried at the next launch, up to three times, before the user is asked for
  the card again.

### Operational rules

- Failed attempts are counted in `pt.migrationAttempts`. A failed read, a value that cannot be saved, an unexpected
  exception and a run that takes too long each count as one attempt. The third failure writes the marker with the
  outcome `gaveUp`.
- The whole run (read and import) is limited to 15 seconds, so that the start-up gate in `MainLayout` always opens; a
  time-out counts as an attempt and the migration is tried again at the next launch.
- The marker `pt.migration` is JSON `{ "version": 1, "outcome": "migrated" | "nothing" | "gaveUp", "reader":
  "leveldb" | "webview" | "none", "found": [...], "at": <milliseconds> }`. The reader is recorded so that the upgrade
  test and the log show which path ran.
- "Nothing found" (no LevelDB directory, or no `pt.*` key after both readers) is final and writes the marker.
  "Files present but unreadable" (both readers failed) is a failed attempt.

## Alternatives considered

- **Hidden web view only (ADR-0004 as written)** - rejected as the primary path: slower, needs the origin served and
  the main thread, and every update would start a web view for data that is almost always readable from the files.
- **LevelDB parsing only** - rejected: a format change would make the migration read nothing, and the user would lose
  the card without an error.
- **Ask for the card again** - stays as the last fallback, as in ADR-0004.
