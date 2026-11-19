# ADR-0027: Run the app on a developer PC, not in CI

- Status: Accepted
- Date: 2026-11-19
- Deciders: Gihed Annabi

## Context

Until now `android.yml` built the APKs and then ran the app twice on a GitHub-hosted Android 14 emulator: the emulator
test (`test` job) on every build and the upgrade test (`upgrade-test` job) on branches and pull requests. The release
job waited for the emulator test, so no APK was published unless it passed on the same Release code.

Forces:

- An emulator job takes up to 40 minutes per run, and two of them run in parallel on every pull request.
- The hosted emulator fails for reasons outside the app: the Android framework restarts when Google Play services
  updates itself after boot, which needed a retry wrapper, and slow software rendering stretches timings.
- Stage S3 needs a cold-start measurement against the 300 ms budget (ADR-0026). A shared runner adds noise to that
  measurement that a pull request check cannot tell apart from a regression.
- The owner wants every run of the app on a device to happen on a developer PC.
- Only CI has the `maui-android` workload, so only CI can build the APKs.

Options considered:

1. Keep the emulator and upgrade jobs in CI, and add a cold-start check there.
1. Keep building the APKs in CI, and run the app only on a developer PC: the emulator test, the upgrade test and the
   cold-start budget.
1. Build and run everything on a developer PC.

## Decision

We use option 2.

- `android.yml` builds the release-key APK and the test-hooks APK, runs the version and certificate guards and uploads
  both APKs as the `apks` artifact. No workflow starts the app.
- The release job of a push to `main` depends on the build job only.
- On a developer PC with an API 34 emulator and the downloaded `apks` artifact, you run:
  - the emulator test (`run-emulator-test.mjs`) before you merge a change that the app can show;
  - the upgrade test (`upgrade-test.mjs`) before you merge a change to stored data or its migration;
  - the budget check (`budget-check.mjs`) before you close a stage that adds weight to the app.
- You put the results in the pull request description. `test/ParkingTracker.E2E.Tests/README.md` describes the runs.

## Consequences

### Positive

- Pull request checks take minutes instead of up to 40, and emulator flakes no longer block a merge or a release.
- Cold-start numbers come from one machine run by one person, measured base against head in the same session.

### Negative

- A release can ship an APK that never ran on a device. The emulator test is now a discipline of the author, not a
  gate; a forgotten run is only visible as a missing result in the pull request.
- The upgrade path from the Capacitor app (ADR-0004, ADR-0008) and the device database migration (ADR-0026) are no
  longer proven on every pull request.
- A developer PC needs the Android emulator, an API 34 system image, Node.js 22 and, for the upgrade test, the
  Capacitor build tools.
- Local timings depend on the PC: a fresh emulator or a busy machine moves a cold start by several hundred
  milliseconds, so a budget run needs more than one round.

## Alternatives considered

- **Keep the emulator jobs in CI (option 1)** - rejected by the owner: slow, flaky, and noisy for the start-up budget.
- **Everything on a developer PC (option 3)** - rejected: the PC has no `maui-android` workload, and the release must
  stay built and signed in CI with the release key.
