# ADR-0002: Use an own Android foreground service for location, parking alerts and timer notifications

- Status: Accepted
- Date: 2026-11-16
- Deciders: Gihed Annabi

## Context

The Capacitor app gets positions from `@capacitor-community/background-geolocation` (a location foreground service
with a persistent notification), schedules notifications with `@capacitor/local-notifications`, and asks for
"while using" and "all the time" location through the custom Java plugin `LocationAccessPlugin`
(`status`, `requestForeground`, `requestBackground`, `openSettings`). The parking detector (`parkDetector` in
`app.js`: driving above 5.5 m/s, then no movement over 15 m for 2 minutes) runs in JavaScript, so it only works while
the WebView process stays alive.

The MAUI app (ADR-0001) must keep: background location with a persistent notification, parking detection and the
"Free for you here" / "Paid parking" notification while the app is not on screen, the 10-minutes-before and end-of-time
timer notifications with the app closed, the location "allow all the time" flow, and the notification permission
(Android 13+). Android 14 (API 34) requires `foregroundServiceType="location"` and the `FOREGROUND_SERVICE_LOCATION`
permission. Android code can only be built and run in CI on this project (no Android workload locally).

Options considered (NuGet versions checked on 2026-11-16):

1. Own code on platform APIs: an Android `Service` in the MAUI project (`Platforms/Android`) with
   `FusedLocationProviderClient` from `Xamarin.GooglePlayServices.Location` 121.4.0.1 (`net10.0-android36.0`),
   `NotificationCompat` channels, `AlarmManager` plus a `BroadcastReceiver` for the timer, and the permission flow
   ported from `LocationAccessPlugin` using MAUI `Permissions` and `AppInfo.ShowSettingsUI()`.
2. Shiny 6.6.0 (`Shiny.Locations`, `Shiny.Notifications`, `net10.0-android36.0`): GPS manager with a background
   delegate and foreground service, notification manager, permission requests.
3. Own location service (option 1) plus `Plugin.LocalNotification` 14.1.2 (`net10.0-android36.0`) for the timer.
4. MAUI Essentials `Geolocation.StartListeningForegroundAsync` only.

## Decision

We write our own Android foreground service and notification code on platform APIs (option 1):

- `ParkingTrackerLocationService` (`[Service(ForegroundServiceType = ForegroundService.TypeLocation)]`, not exported)
  requests high-accuracy fused locations (about 1 s interval, 5 m minimum distance), shows the low-importance
  persistent notification, and feeds every fix to `ILocationFeed` in the app process.
- The parking detector moves to `ParkingTracker.Domain` as a pure class with explicit timestamps. The service owns its
  instance, so detection works whatever the WebView does. On "parked" it sends the Application query that decides the
  spot and finds the nearest free spot, saves `pt.car` and posts the alert notification.
- Timer notifications use `AlarmManager.SetExactAndAllowWhileIdle` when `CanScheduleExactAlarms()` is true and
  `SetAndAllowWhileIdle` otherwise, with a receiver that posts the notification and a boot receiver that re-schedules
  from `pt.timer`. The app declares `SCHEDULE_EXACT_ALARM` and, when the user starts a timer without it, opens the
  system screen to allow exact alarms (owner decision, 2026-11-16).
- Location and notification permissions sit behind an `ILocationAccess` port in Application with the same four
  operations as `LocationAccessPlugin`, so the UI flow (banner, welcome dialog, "Later" snooze) is unchanged.
- All Android types live in `ParkingTracker.App/Platforms/Android`; Application only sees ports (`ILocationFeed`,
  `ILocationAccess`, `INotifier`, `IParkingTimerScheduler`).

## Consequences

### Positive

- One dependency (`Xamarin.GooglePlayServices.Location`, the Microsoft-maintained binding) instead of a framework with
  its own hosting and DI model next to Pivot's installers.
- Full control of the Android 14 foreground-service type, channels, notification ids and texts, so the behaviour can
  match the current app exactly.
- Parking detection becomes unit-testable C# and stops depending on the WebView being alive.

### Negative

- About 300 to 400 lines of Android-specific C# that only CI can build, and that unit tests cannot cover; the emulator
  test must cover it (service running, parking notification, timer notification).
- The team owns Android API changes (foreground-service rules, exact-alarm policy) instead of a library maintainer.
- Fused location requires Google Play services on the phone; a de-Googled phone gets no position. The owner's phone
  has Google Play services (confirmed 2026-11-16).
- If the user refuses exact alarms, timer notifications can arrive a few minutes late, as today.

## Alternatives considered

- **Shiny 6** - rejected. It covers location, background delegates and notifications well and supports .NET 10, but it
  brings its own hosting (`UseShiny`) and conventions, has had several breaking major versions, and would own the
  most delicate part of the app (when the service starts and stops). The amount of code it saves is small.
- **Own location service plus `Plugin.LocalNotification`** - rejected for now, kept as the fallback. It removes the
  alarm and boot-receiver code, but adds a second permission and channel model next to ours for two notifications.
- **MAUI Essentials foreground listening only** - rejected. It has no foreground service, so parking alerts with the
  app off screen would stop working.
