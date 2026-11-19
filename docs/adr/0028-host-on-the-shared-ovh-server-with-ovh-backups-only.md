# ADR-0028: Host on the shared OVH server, behind its Caddy, with OVH backups only

- Status: Proposed
- Date: 2026-11-19
- Deciders: Gihed Annabi
- Supersedes: ADR-0022

## Context

ADR-0022 planned a dedicated OVH VPS-2 (8 GB) for the API, the Jobs host, the admin app and PostgreSQL, with its own
Caddy, a domain chosen later (`<domain>`), and nightly restic backups to Backblaze B2. Two owner decisions change it:

- **No Backblaze B2.** The server holds little that cannot be rebuilt. The driver's data stays on the phone
  (ADR-0015, ADR-0026). The jobs download the parking data again from the upstream sources (ADR-0011, ADR-0013).
  What a total loss would cost is the accounts and sessions (drivers sign in again), the admin allow-list
  (re-entered from the runbook) and the run history. A second provider is not worth its keys, its pruning and its drills for that.
- **The OVH server the owner already runs.** It is the server of the RaidManager project, as that project's
  ADR-0027 describes it on 2026-10-03:

  | Property | Value |
  | --- | --- |
  | Offer | OVH VPS-1, datacentre in Gravelines, France |
  | Processor and memory | 2 vCores, 4 GB (3.7 GiB seen by the system), 2 GB swap file |
  | Storage | 40 GB NVMe, local |
  | System | Ubuntu 24.04, Docker Engine and Compose, `ufw` allowing 22, 80, 443 and 8080, unattended security upgrades |
  | Applications | the `pivotsoftwares.com` website, Delivery Atlas, and RaidManager's dev, test and production environments, each under `/opt/apps/<name>` |
  | Proxy | one shared `caddy:2` container, the only one publishing ports, with its Caddyfile at `/opt/apps/proxy/Caddyfile`; applications reach it through the external Docker network `web` |
  | Backup | OVH Automated Backup Standard, daily, of the whole server |

  RaidManager plans about 2.8 GB of the 3.9 GB for its three environments, which leaves about 1 GB. ADR-0022
  estimated Parking Tracker's peak at 2 to 3 GB. The owner chose to join the server as it is, without an upgrade, and
  to use subdomains of `pivotsoftwares.com`.

Options considered:

1. Join the shared server as it is, with a memory budget of about 1 GB, and back up with OVH's daily backup and local
   dumps only.
1. Join the shared server after upgrading it to a VPS-2 (8 GB).
1. Keep the dedicated VPS-2 of ADR-0022, without Backblaze B2.
1. Keep ADR-0022 unchanged.

## Decision

We run Parking Tracker on the shared OVH server as it is, behind its Caddy, and keep backups on that server and in
OVH's daily backup only (option 1). Every rule of ADR-0022 not changed below stays, and is restated here.

### Domain

- `<domain>` in ADR-0014, ADR-0015, ADR-0017 and the architecture is `parking.pivotsoftwares.com`.
- Three hostnames, each with an A record (and AAAA when the server has IPv6) that the owner adds at the DNS provider of
  `pivotsoftwares.com`:

  | Hostname | Serves |
  | --- | --- |
  | `api.parking.pivotsoftwares.com` | the API |
  | `admin.parking.pivotsoftwares.com` | the admin web app |
  | `parking.pivotsoftwares.com` | the privacy notice (ADR-0015), a static page served by Caddy |

### The server is shared: Parking Tracker only adds

- **Nothing is reinstalled or reset.** Parking Tracker does not change the operating system, the Docker daemon
  settings, `ufw`, the update policy or the other applications. ADR-0022's host rules (Debian, `nftables`, fail2ban,
  a reboot at 05:30, the daemon's log driver and `live-restore`) give way to the server as it is. A provisioning
  script in `deploy/` only adds what is missing and can run again.
- **Its own folder and Compose project:** `/opt/apps/parking-tracker`, project name `parking-tracker`.
- **Its own deploy account.** The forced command of ADR-0023 runs as `parking-deploy`, not `deploy`, because
  RaidManager's `deploy` account already exists, with Docker access. `parking-deploy` is not in the `docker` group; its
  key is `restrict`ed to `/usr/local/bin/pt-deploy`, which calls one root helper through a single `sudoers` line, as
  ADR-0023 says. Every other rule of ADR-0023 stays.
- **Secrets** are files under `/opt/apps/parking-tracker/secrets` (root, mode 600), mounted as Compose secrets, with
  copies in the owner's password manager.

### Containers

- **Only the shared Caddy publishes ports.** Parking Tracker publishes none and runs no proxy of its own.
- **Networks:** the API and the admin app join the external network `web` under the aliases `parking-api` and
  `parking-admin`. The project's own networks are `data` (internal, no route out: API, Jobs, admin, migrator,
  PostgreSQL) and `egress` (Jobs only, to reach the upstream sources).
- **Hardening as in ADR-0022:** non-root, `cap_drop: [ALL]`, `no-new-privileges`, read-only file systems where
  possible, base images pinned by digest, the chiseled ASP.NET Core 10 image with time-zone data and ICU.
- **PostgreSQL:** `postgis/postgis` major 17, pinned by digest, never published, one role per host, a yearly major
  upgrade by dump and restore.

### Memory budget: about 1 GB

| Container | Memory limit | Settings |
| --- | --- | --- |
| PostgreSQL with PostGIS | 256 MB | `shared_buffers` 48 MB, `max_connections` 30 |
| API | 224 MB | workstation garbage collector, `DOTNET_GCConserveMemory` |
| Jobs | 288 MB | as the API; one job at a time |
| Admin web app | 160 MB | as the API |
| Total while running | 928 MB | the migrator (128 MB) runs only during a deployment |

- The limits are hard (`mem_limit`), so Parking Tracker cannot push the other applications into swap beyond its own
  share. A container that reaches its limit is restarted by Docker, and the uptime monitor reports it.
- Stage S6 measures the real use of each container during a full refresh of all areas. If a limit is too tight for
  the work, the way out is a larger server (option 2), which is a new owner decision, not a silent raise.

### Caddy sites

- Parking Tracker adds its three sites to the shared Caddyfile once, at provisioning, from `deploy/parking.Caddyfile`.
  The script validates the whole file and reloads Caddy, and puts the previous file back when Caddy rejects it. A
  deployment never edits the shared file.
- Per site, as in ADR-0022: Let's Encrypt certificates, compression, HSTS, a 256 KB request body limit, no route to
  `/api/v1/admin/*` from the internet, and an access log without query strings. HTTP/3 depends on the shared Caddy
  and on UDP 443, which the server does not open; Parking Tracker does not ask for it. Until a container exists, a site
  answers 503, so certificates are obtained before the first deployment.
- Rate limiting stays in ASP.NET Core.

### Backups and recovery

- **OVH Automated Backup Standard**, daily, of the whole server, already in place. It restores the whole server, not
  one database.
- **A nightly `pg_dump -Fc` at 02:00** of the application database and the roles, kept on the server under
  `/opt/apps/parking-tracker/backups` for 7 days, so one database can be restored without touching the other
  applications. OVH's daily backup carries these files off the disk.
- **A pre-deploy dump**, the last 5 kept, as in ADR-0022 and ADR-0023.
- **A monthly restore test** at 05:30, after the job window: the latest dump is restored into a throwaway PostgreSQL
  container with a 256 MB limit, checked and removed.
- **Recovery targets:** RPO 24 h for accounts and run history while the server or OVH's backup survives; parking data
  is rebuilt by the jobs; RTO about 4 h. After a total loss of the server and its backups, drivers sign in again, the
  owner re-enters the admin allow-list from the runbook, and the run history starts again.
- **Monitoring as in ADR-0022:** an external uptime monitor on `/health/ready`, `/health/data` and TLS expiry, and
  dead-man pings for the nightly dump, the restore test and the daily refresh.

## Consequences

### Positive

- No new server, account or provider: Parking Tracker adds no hosting cost.
- No Backblaze account, key, bucket, pruning or rebuild drill to maintain.
- The server, its proxy, its certificates and its updates are already run for the other applications.
- One database can still be restored on its own from the nightly dump.

### Negative

- About 1 GB for four containers is a third to a half of ADR-0022's estimate. The limits can starve a full refresh,
  and S6 may show that the server must be upgraded after all.
- Parking Tracker shares memory, CPU, disk and the proxy with four other workloads: a peak or a broken shared Caddyfile
  affects all of them.
- The backups stay with OVH. An OVH-wide incident or a lost OVH account loses the accounts, the allow-list and the run
  history.
- The host rules are the server's, not Parking Tracker's: no fail2ban, no `nftables` rules of its own, and port 8080 is
  open for another application.
- HTTP/3 is not available while UDP 443 stays closed.

## Alternatives considered

- **Shared server upgraded to a VPS-2 (option 2)** - rejected by the owner for now; it stays the way out if S6 shows
  the budget is too small.
- **Dedicated VPS-2 without Backblaze B2 (option 3)** - rejected: a second server to run and pay for, while the
  shared one already has a proxy, certificates and updates.
- **ADR-0022 unchanged (option 4)** - rejected: Backblaze B2 protects data that is mostly rebuilt by the jobs, and the
  dedicated server duplicates the one the owner runs.
