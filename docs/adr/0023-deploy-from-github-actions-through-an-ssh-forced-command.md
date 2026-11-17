# ADR-0023: Deploy from GitHub Actions with private GHCR images and an SSH forced command, with migration bundles

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The source and the Pivot NuGet feed are on GitHub. The existing workflows (`dotnet.yml`, `android.yml`) pin actions by
SHA, use least-privilege permissions and restore Pivot packages with the workflow token. The back end (ADR-0022) needs
images built, tested and deployed to one VPS, with database migrations and a rollback. Three hosts start against one
database, so they must not migrate at start-up. Phones update late, so the API must stay compatible with older app
versions across deployments.

Options considered for the path from GitHub to the VPS:

1. Push over SSH to a forced command on the VPS.
1. Pull: a timer on the VPS polls the registry for a release tag.
1. Push over SSH through a Tailscale network.

Options for image visibility: private or public GitHub Container Registry packages.

## Decision

We deploy from a new workflow `.github/workflows/backend.yml` through an SSH forced command (option 1), with private
GHCR images (decided by default, the owner may change it).

- **Jobs.** `build` (pull requests build only; `main` builds and pushes four images: API, Jobs, admin, migrator; tags
  `sha-<12>` and `1.<run_number>`; SBOM and provenance; `packages: write` only when pushing), `smoke` (starts the images
  with a PostgreSQL container on the runner, runs the migrator, checks health and anonymous `401` on protected
  endpoints), `deploy` (`main` only, after `smoke`, environment `production`, `concurrency` without cancel, 15-minute
  timeout). Actions pinned by SHA; no event text in scripts; forks never get the environment or `packages: write`.
- **Forced command.** The deploy key in `~deploy/.ssh/authorized_keys` is `restrict`ed to `/usr/local/bin/pt-deploy`,
  which accepts only `deploy sha-<12 hex>`, `rollback` and `status`, and calls one root helper through a single
  `sudoers` line. `deploy` is not in the `docker` group. The private key lives only in the `production` environment;
  the VPS host key is pinned in `known_hosts`.
- **Sequence.** Pull by tag and record digests, pre-deploy dump, run the migrator, start the new release, poll
  `/health/ready` through Caddy for up to 90 seconds, and roll the application back to the previous release when it is
  unhealthy. `pt-deploy` refuses to deploy between 02:30 and 05:00 (the job window) unless forced.
- **Migrations.** An EF Core migration bundle (`dotnet ef migrations bundle --self-contained`) in the migrator image,
  run once per deployment with the owner role. Hosts never call `Database.Migrate()`. Migrations follow expand and
  contract, so the previous release always runs on the new schema; a schema rollback is a manual restore.
- **Images.** The VPS pulls with a token limited to `read:packages`; a scheduled clean-up keeps the last 10 images.
  The Pivot feed token is a BuildKit secret, never a build argument.
- **Drift.** `pt-deploy` copies `deploy/` at the release tag, so hand edits on the VPS are overwritten. Each deployment
  is appended to `releases.log` on the VPS.
- Deployment is automatic after the smoke job (decided by default); a required reviewer can be added to the
  `production` environment.

## Consequences

### Positive

- Feedback in the Actions run; the key can do exactly one thing.
- Every release is an immutable digest that passed the smoke job.
- A failed health check rolls the application back without a person.
- Private images do not publish compiled Pivot packages.

### Negative

- Port 22 must be reachable from GitHub-hosted runners.
- Expand and contract makes some schema changes take two releases.
- A token with `read:packages` lives on the VPS, and private images count against the plan's Packages storage.
- `pt-deploy` and its root helper are scripts to write, test and keep safe.

## Alternatives considered

- **Pull by timer** - rejected. No feedback in Actions, a polling delay, and more scripting for migrations and
  rollback; it would avoid a GitHub-held key, which the forced command already limits.
- **SSH through Tailscale** - rejected for now. Another account and secret; a good later step if SSH noise or risk
  grows.
- **Public images** - rejected by default. Simpler and free, but they publish compiled Pivot packages and the
  application.
- **Migrate at host start-up** - rejected. Three hosts would race on the same schema.
