# Parking Tracker E2E tests

The Android emulator test and the upgrade test of the .NET MAUI app. It replaces the API end-to-end test of a service
([ADR-0006](../../docs/adr/0006-mobile-client-layout.md)) and ports the emulator test of the earlier Capacitor app.

## Run it

You need Node.js 22 or later (no packages to install), `adb` on the path and a booted emulator with Google APIs. The APK
must be the Release build made with test hooks:

```bash
dotnet publish src/Containers/UI/Hosting/ParkingTracker.App/ParkingTracker.App.csproj \
  -f net10.0-android -c Release -p:EnableTestHooks=true -o artifacts/hooks
bash test/ParkingTracker.E2E.Tests/collect-apk.sh artifacts/hooks apk/ParkingTracker-test.apk
node test/ParkingTracker.E2E.Tests/emulator-test.mjs   # reads apk/ParkingTracker-test.apk
```

The host builds only where the `maui-android` workload is installed; CI does it in `.github/workflows/android.yml`.

## What it checks

- Install with `-g`, launch `be.parkingtracker.app/be.parkingtracker.app.MainActivity`, find the
  `webview_devtools_remote_<pid>` socket and talk to the web view over the Chrome DevTools Protocol.
- `window.ptTest` (setCard, getCard, decide, freeSpots, officialList, platformStatus, screenState, startTimer,
  parseCardText, getCar, migration, dbMigration) reaches the app's own C# code. The answers at Avenue Louise 216 and Place du
  Châtelain, the official-list check, the permissions and the nearest free spot are compared with the expected values.
- The card scan (ADR-0003): `ptTest.parseCardText` reads a recognised sample card text (number with a misread `8`,
  validity range with an en dash, plate, sector, tolerance code) with the same parser the scan uses. The camera and the
  picker cannot be driven on a CI emulator, so taking a photo and the ML Kit recognition itself are not covered here;
  they need a check on a phone.
- The screen (`.hero-title`, `.hero-sub`) shows the answer for the emulator GPS position.
- Parking alerts (ADR-0002): the foreground service of type location is running with its notice; after a short drive
  above 20 km/h and a stop at Avenue Louise 216 with the app in the background, the notification "Free for you here"
  appears (`dumpsys notification --noredact`, up to 4 minutes) and the car chip shows the saved car; a 1-minute timer
  gives the notification "Your parking time is over.". The setup grants `POST_NOTIFICATIONS` and allows exact alarms
  with `appops`.
- No JavaScript error, no unhandled .NET exception in logcat, the Blazor error UI (`#blazor-error-ui`) is hidden, the
  app did not crash.

The report goes to `emulator-report/` (`summary.md`, `report.json`, screenshots, `logcat.txt`, `crash.txt`); the exit
code is 1 when a check fails.

## Upgrade test

`upgrade-test.mjs` proves that the saved data of the Capacitor version survives the update to the MAUI version
([ADR-0004](../../docs/adr/0004-migrate-saved-data-from-the-capacitor-webview-once.md), reader of
[ADR-0008](../../docs/adr/0008-read-capacitor-data-from-leveldb-with-a-web-view-fallback.md)). It needs two APKs of
the same app id signed with the same key, the MAUI one with the higher versionCode: the Capacitor debug build of the
Git tag `v1.0.11`, the last Capacitor release (web view debugging on) and the MAUI test-hooks build.
`resign-apk.sh` signs both with a throwaway key and `version-code.sh` prints a versionCode; the workflow `android.yml`
(job `upgrade-test`) builds, signs and runs everything. Both scripts need `ANDROID_HOME`, echo each stage and report every failure as an `::error::` line on
stderr.

```bash
export UPGRADE_KEY_PASSWORD=changeit
keytool -genkeypair -keystore upgrade.p12 -storetype PKCS12 \
  -storepass "$UPGRADE_KEY_PASSWORD" -keypass "$UPGRADE_KEY_PASSWORD" \
  -alias upgrade -keyalg RSA -keysize 2048 -validity 2 -dname "CN=upgrade test"
# build the Capacitor app from the tag v1.0.11 (needs Node 22 and JDK 21):
git worktree add ../parking-capacitor v1.0.11
(cd ../parking-capacitor \
  && npm ci && npm run build && npx cap sync android \
  && cd android && VERSION_CODE=1 ./gradlew assembleDebug)
bash test/ParkingTracker.E2E.Tests/resign-apk.sh \
  ../parking-capacitor/android/app/build/outputs/apk/debug/app-debug.apk \
  upgrade/capacitor.apk upgrade.p12 upgrade
bash test/ParkingTracker.E2E.Tests/resign-apk.sh path/to/ParkingTracker-test.apk \
  upgrade/maui-test.apk upgrade.p12 upgrade
node test/ParkingTracker.E2E.Tests/upgrade-test.mjs upgrade/capacitor.apk upgrade/maui-test.apk
```

What it checks:

- The Capacitor build installs, runs in the origin `https://localhost`, and takes `pt.card`, `pt.car`, `pt.layers`,
  `pt.bgAlerts` and `pt.permSnooze` in `localStorage` through the devtools protocol (`webview-cdp.mjs`).
- The test waits until the web view data on disk mentions the card (`run-as` on the debug build; Chromium writes a few
  seconds after the change), then force-stops the app.
- It starts the old app once more and waits 15 seconds, so that LevelDB recovery or compaction writes `.ldb` table files
  as it does for a real user, then force-stops it again and records the files of the web view storage
  (`run-as be.parkingtracker.app ls -la 'app_webview/Default/Local Storage/leveldb'`) in the report.
- The MAUI build installs over it with `adb install -r` and a higher versionCode.
- Through `ptTest.getCard()`, `ptTest.getCar()`, `ptTest.migration()` and `ptTest.dbMigration()`: the card and the car are
  the stored ones (read from the device database, ADR-0026), the marker `pt.migration` says `migrated`, names the reader
  that was used (`leveldb` or `webview`) and lists the keys, the marker `pt.dbMigration` says `migrated` and lists the card
  and the car, and the welcome dialog (`#welcomeDialog`) is not shown.
- No JavaScript error, no unhandled .NET exception in logcat.

The report goes to `upgrade-report/` (`summary.md` with the file list and the reader, `report.json`, `logcat.txt`,
screenshots); the exit code is 1 when a check fails. The job runs on branch pushes, pull requests and on demand, and
is skipped on pushes to `main`.
