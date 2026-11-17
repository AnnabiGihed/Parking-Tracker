# Contributing to Parking Tracker

Thank you for helping. This page tells you how to branch, commit, open a pull request and build the .NET solution.
Everyone taking part follows the [Code of Conduct](./CODE_OF_CONDUCT.md).

## Branching

- `main` is protected. Never commit or push to it directly.
- Create one new branch per work item from an up-to-date `main`: `feature/<WORKITEM-ID>-<short-kebab-description>`,
  `fix/<WORKITEM-ID>-<short-kebab-description>`, or `chore/<short-kebab-description>` when there is no work item.
- Keep branches short-lived and rebased on `main`; do not merge `main` into them.
- Do not commit to a branch someone else created. Create your own branch from it and open a pull request into it.

## Commit messages

We follow [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): imperative summary`, under 72
characters, with the work item in the summary when there is one. Types: `feat`, `fix`, `docs`, `refactor`, `perf`,
`test`, `build`, `ci`, `chore`, `revert`.

```text
feat(spot-decisions): decide a spot from the street list
fix(paid-hours): read the hours of a rule across midnight
docs(adr): record the mobile client layout
```

Commits are small and compile on their own. A commit message states the checks that ran and their results.

## Pull requests

Open the pull request into `main`. The description has five sections, each filled or marked `none` with a reason:

1. **What changed**, in functional terms.
2. **Why it changed**: the work item and the reason.
3. **How it was tested**: tests added, scenarios checked, including failure paths.
4. **What to review carefully**: uncertain points, constrained decisions, unusual patterns.
5. **Migration or deployment notes**, or `none`.

Review your own pull request against the author self-review checklist before you ask for a review. A pull request
needs:

1. Green checks: the **.NET quality gate** workflow (`.github/workflows/dotnet.yml`: restore, format check, Release
   build, unit tests) and the **Android APK** workflow (`.github/workflows/android.yml`: build, version and certificate guards,
   emulator test, upgrade test). The repository owner should
   mark the ".NET quality gate" and Android checks as required in the branch protection of `main`; until then they
   are expected, not enforced.
2. One approval from the repository owner.
3. A `CHANGELOG.md` entry under `[Unreleased]` for every user-visible change, or a line saying why there is none.
4. Documentation (`README.md`, `docs/`, ADRs) updated in the same pull request as the behavior it describes.

Pull requests are merged with squash, so the title follows Conventional Commits.

## Local setup

You need the .NET SDK named in `global.json` (10.0.4xx), Python 3 and, for the data
tools, `pdftotext`. Node 22 is needed only to run the E2E scripts.
The Android build needs Android Studio or the Android SDK with JDK 21 and runs in CI; see
[Build and test in the README](./README.md#build-and-test).

The `Pivot.Framework.*` packages come from GitHub Packages, so restore needs a token. Set two environment variables in
the shell that builds (details in [docs/architecture.md](./docs/architecture.md#41-build-the-net-solution)):

```bash
export GITHUB_PACKAGES_USER=<your GitHub user name>
export GITHUB_PACKAGES_TOKEN="$(gh auth token)"   # a token with the read:packages scope
```

Then build and test everything, and check the formatting, with:

```bash
python .agent-kit/verify.py
dotnet format ParkingTracker.slnx --verify-no-changes
```

The analyzers treat warnings as errors and every member is documented, including private ones; fix a finding instead of
suppressing it. Known waivers are listed under [Tooling deviations](./README.md#tooling-deviations).

## Reporting a security problem

Do not open a public issue for a vulnerability. Contact the repository owner privately through GitHub (a private
security advisory on this repository) and give the steps to reproduce.

## Releases

Releases are built from `main` by the Android APK workflow, which signs the APK and publishes it on the
[GitHub Releases page](https://github.com/AnnabiGihed/Parking-Tracker/releases) only when the emulator test passes and
the version and certificate guards hold.
Every release gets its own `## [X.Y.Z] - YYYY-MM-DD` section in `CHANGELOG.md`.
