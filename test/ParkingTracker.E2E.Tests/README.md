# Parking Tracker E2E tests

The Android emulator test of the .NET MAUI app. It replaces the API end-to-end test of a service
([ADR-0006](../../docs/adr/0006-mobile-client-layout.md)) and ports `tools/emulator-test.mjs`, which stays in place for the
Capacitor workflow until cut-over.

## Run it

You need Node.js 22 or later (no packages to install), `adb` on the path and a booted emulator with Google APIs. The APK
must be the Release build made with test hooks:

```bash
dotnet publish src/Containers/UI/Hosting/ParkingTracker.App/ParkingTracker.App.csproj \
  -f net10.0-android -c Release -p:EnableTestHooks=true -o artifacts/hooks
node test/ParkingTracker.E2E.Tests/emulator-test.mjs path/to/ParkingTracker-test.apk
```

The host builds only where the `maui-android` workload is installed; CI does it in `.github/workflows/maui-android.yml`.

## What it checks

- Install with `-g`, launch `be.parkingtracker.app/be.parkingtracker.app.MainActivity`, find the
  `webview_devtools_remote_<pid>` socket and talk to the web view over the Chrome DevTools Protocol.
- `window.ptTest` (setCard, getCard, decide, freeSpots, officialList, platformStatus, screenState, startTimer) reaches the app's own C# code.
  The answers at Avenue Louise 216 and Place du Châtelain, the official-list check, the permissions and the nearest
  free spot are compared with the expected values.
- The screen (`.hero-title`, `.hero-sub`) shows the answer for the emulator GPS position.
- Parking alerts (ADR-0002): the foreground service of type location is running with its notice; after a short drive above
  20 km/h and a stop at Avenue Louise 216 with the app in the background, the notification "Free for you here" appears
  (`dumpsys notification --noredact`, up to 4 minutes) and the car chip shows the saved car; a 1-minute timer gives the
  notification "Your parking time is over.". The setup grants `POST_NOTIFICATIONS` and allows exact alarms with `appops`.
- No JavaScript error, no unhandled .NET exception in logcat, the Blazor error UI (`#blazor-error-ui`) is hidden, the
  app did not crash.

The report goes to `emulator-report/` (`summary.md`, `report.json`, screenshots, `logcat.txt`, `crash.txt`); the exit
code is 1 when a check fails.
