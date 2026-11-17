# ADR-0001: Migrate the Android app to .NET 10 MAUI Blazor Hybrid on Pivot.Framework

- Status: Superseded by ADR-0009
- Date: 2026-11-16
- Deciders: Gihed Annabi

## Context

Parking Tracker is today a web app (`index.html`, `app.js`, `live.js` and the data scripts) packaged for Android
with Capacitor 7 (`android/`, `capacitor.config.json`). All decisions run in JavaScript inside the WebView. Native
features come from Capacitor plugins (`@capacitor-community/background-geolocation`, `@capacitor/local-notifications`,
`CapacitorHttp`) and one custom Java plugin (`LocationAccessPlugin`). CI (`.github/workflows/android.yml`) builds a
signed APK, tests a debug build on an Android 14 emulator through the Chrome DevTools Protocol (CDP), and publishes a
GitHub Release only when the test passes. The latest release is `v1.0.11` (versionCode 11).

Forces:

- The owner wants the app on .NET 10 and .NET MAUI, built with Pivot.Framework (selected explicitly for this work).
- The current HTML/CSS design and the Leaflet map must be kept.
- Android only. The new APK must install as an update over the installed app: same application id
  `be.parkingtracker.app`, same release key (alias `parkingtracker`), versionCode greater than 11.
- The decision logic is the product. It is live-only ("CAN'T VERIFY" instead of guessing) and has no unit tests:
  `tools/emulator-test.mjs` is the only automated check.
- Capacitor needed workarounds for HTTP (CORS, binary bodies). Native `HttpClient` calls from C# need none.
- This PC has the .NET 10 SDK (10.0.403) without the `maui-android` workload, Android SDK or JDK: Android builds can
  only run in CI. Class libraries and their tests must build and run locally.
- Pivot.Framework 2.1.0 (GitHub Packages, `net10.0`) ships `Pivot.Framework.Domain` (`Result`, `Result<T>`, `Error`),
  `Pivot.Framework.Application` (`ICommand`, `IQuery<T>`, its own dependency-free mediator registered with
  `AddPivotMediator`, `ValidationPipelineBehavior`) and `Pivot.Framework.Tools.DependencyInjection`
  (`IServiceInstaller`, `BaseServiceInstaller`). MediatR and AutoMapper were removed in 2.0.0. Its other packages are
  server-side (EF Core, RabbitMQ, Redis, MongoDB, Hangfire, ASP.NET Core, Keycloak).

Options considered:

1. MAUI Blazor Hybrid: Razor components in a `BlazorWebView`, decision logic in C#, Leaflet through JS interop.
2. MAUI with native XAML pages and a native map control.
3. Keep the JavaScript engine and host it in a MAUI `BlazorWebView` or `WebView` without porting it.
4. Stay on Capacitor.

## Decision

We migrate the Android app to a .NET 10 MAUI Blazor Hybrid app built on Pivot.Framework, with these rules:

- **Projects.** `src/Core/ParkingTracker.Domain` (pure rules, `net10.0`), `src/Core/ParkingTracker.Application`
  (queries, commands, ports, presentation texts, `net10.0`), `src/Infrastructure/ParkingTracker.Infrastructure`
  (HTTP clients for the live sources, embedded reference data, `net10.0`), `src/Containers/Mobile/ParkingTracker.App`
  (MAUI, `net10.0-android` only: Razor UI, JS interop, Android services), and one test project per class library
  under `test/`. Central package management pins the three Pivot packages to one version.
- **Pivot usage.** `Pivot.Framework.Domain` for `Result`/`Error` in Domain and Application;
  `Pivot.Framework.Application` for `IQuery<T>`/`ICommand` handlers, `AddPivotMediator` and
  `ValidationPipelineBehavior` with FluentValidation; `Pivot.Framework.Tools.DependencyInjection` for one
  `IServiceInstaller` per layer. No other Pivot package: the app has no database, broker, cache, scheduler, API host
  or sign-in, so the persistence, outbox, messaging, caching, read-store, scheduling, container and authentication
  packages are excluded.
- **Logic in C#.** The decision engine, house-number rules, sector model, geometry, zone types, card scope, paid
  hours and Belgian holidays, parking detection, card-text parsing and every live-data client move to C#.
  Leaflet rendering stays in JavaScript behind one interop module (`wwwroot/js/map.js`).
- **Reference data.** `city-sectors.js`, `sectors.js` and `municipalities.js` become JSON files embedded in
  `ParkingTracker.Infrastructure`. `tools/build_city_sectors.py` writes the City JSON and `tools/verify_sectors.py`
  reads the tolerance JSON; the Python tools stay the source of truth.
- **Behaviour parity is tested.** Before the old engine is deleted, recorded live responses and the answers of the
  current `live.js` are stored as fixtures; the C# engine must give the same answers on them.
- **Tests.** xUnit, FluentAssertions and NSubstitute, as chosen by the owner, for the class libraries.
  `python .agent-kit/verify.py` runs them locally through a `verify` list in `.agent-kit/config.json` that builds
  the test projects only (the MAUI project needs the Android workload).
- **Delivery.** The workflow file name `.github/workflows/android.yml` is kept so `github.run_number` keeps rising.
  CI restores Pivot packages with the workflow token (`packages: read`), publishes a signed universal APK with the
  existing secrets and keeps the build, emulator test and release-on-success flow. The emulator test drives a Release
  build that has test hooks switched on.
- **Cut-over.** The web/PWA version is retired. Capacitor, `www/`, the service worker, the manifest and the dev
  server are removed in the last stage of this branch, after the MAUI app passes the emulator and upgrade tests.

Android platform services, card OCR and the saved-data migration are decided in ADR-0002, ADR-0003 and ADR-0004.

## Consequences

### Positive

- The decision engine gets fast unit tests on recorded data; today only an emulator run checks it.
- HTTP runs in `HttpClient`: no CORS proxy, no Capacitor HTTP base64 workaround, real timeouts and cancellation.
- Parking detection runs in the Android service in C#, so it no longer depends on a WebView kept alive in the
  background.
- The existing look, CSS and map stay; users see the same app.
- Pivot's `Result`, mediator, validation and installers give the code the same structure as other Pivot projects.

### Negative

- Large rewrite: about 1,600 lines of JavaScript move to C#, with parity risk. Mitigated by recorded fixtures and the
  emulator test, but subtle differences (string normalisation, floating point, time zones) remain possible.
- The APK grows from a few MB to tens of MB (.NET runtime per ABI), and cold start is slower.
- Android builds cannot be done on the owner's PC without installing the Android workload, SDK and JDK; every
  Android change needs a CI run to be checked.
- The web version disappears. Nothing public is lost (GitHub Pages is not enabled for the repository), but the
  browser workflow for quick checks goes away.
- Pivot packages need an authenticated feed for every restore, locally and in CI.
- Release builds use trimming; reflection-based registration (Scrutor scanning, mediator wrappers) must be kept out
  of full trimming. The test build is a Release build so such defects are caught before publishing.
- The Leaflet map now needs a JS interop layer, and large area layers cross it as JSON.

## Alternatives considered

- **MAUI with native XAML pages and a native map** - rejected. It throws away the current HTML/CSS design and the
  Leaflet layers (WMS, polygons, popups), which the owner wants to keep, and needs a new map component.
- **Keep the JavaScript engine inside a MAUI WebView** - rejected. It moves the packaging without moving the logic,
  keeps HTTP inside the WebView (CORS again, now without Capacitor's native HTTP), and leaves the rules untested in
  .NET. It would not be a Pivot-based app in any meaningful sense.
- **Stay on Capacitor** - rejected. It does not meet the owner's goal of a .NET 10 MAUI app on Pivot.Framework.
- **Use more Pivot packages (for example `Infrastructure.Persistence.Sqlite` for local data)** - rejected. Five small
  settings values do not need EF Core, a unit of work or migrations; MAUI `Preferences` covers them (ADR-0004).
