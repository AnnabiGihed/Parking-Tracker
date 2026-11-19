# ADR-0022: Host on one OVH VPS with Docker Compose, Caddy and PostgreSQL, with off-site backups to Backblaze B2

- Status: Superseded by ADR-0028
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The owner decided on one OVH VPS-2 (4 vCores, 8 GB RAM, 75 GB NVMe) for the API, the Jobs host, the admin app and
PostgreSQL; the domain name is chosen later (`<domain>`). The project is personal: FAVV and Azure hosting rules do not
apply. The budget is about EUR 15 a month, there is one maintainer and no on-call, and the target is best effort,
about 99 % a month. Recovery: RPO 24 h for accounts and run history; parking data is rebuilt by the jobs; RTO about
4 h. The house layout asks for a Dockerfile per deployable host; Aspire is for development only. Estimated peak memory
is 2 to 3 GB.

Options considered for the runtime:

1. Docker Compose with Caddy, the three hosts, a one-shot migrator and PostgreSQL in containers.
1. systemd services with framework-dependent publishes and PostgreSQL from apt.
1. A single-node k3s cluster.

Options considered for the reverse proxy: Caddy, Traefik, nginx with certbot. For off-site backups: restic to
Backblaze B2, OVH's backup add-ons only, a second VPS.

## Decision

We host on one OVH VPS-2 in an EU datacentre with Docker Compose (option 1), Caddy as the reverse proxy, and nightly
restic backups to Backblaze B2.

- **Host.** Current Debian stable, UTC, bootstrap script in `deploy/`; key-only SSH, root login off, separate `deploy`
  user; OVH edge firewall and host `nftables` allow TCP 22, 80, 443 and UDP 443 only; `unattended-upgrades` with a
  reboot at 05:30; fail2ban on SSH; Docker with the `local` log driver, `no-new-privileges` and `live-restore`.
- **Containers.** Only Caddy publishes ports. Networks: `edge` (Caddy, API, admin), `data` (internal, no route out:
  API, Jobs, admin, migrator, PostgreSQL) and `egress` (Jobs only, to reach the upstream sources). Containers run
  non-root with `cap_drop: [ALL]`, memory limits and read-only file systems where possible. Base images are pinned by
  digest; a chiseled ASP.NET Core 10 image with time-zone data and ICU runs the hosts.
- **Caddy.** Automatic Let's Encrypt certificates for `api.<domain>` and `admin.<domain>`, HTTP/3, compression, HSTS,
  a 256 KB request body limit, no route to `/api/v1/admin/*` from the internet, and an access log that drops query
  strings. Rate limiting is done in ASP.NET Core, because Caddy has none built in.
- **PostgreSQL.** The `postgis/postgis` image, major 17, pinned by digest, never published; one role per host; a
  yearly major upgrade by dump and restore.
- **Secrets.** Files under `/srv/parking-tracker/secrets` (root, mode 600) mounted as Compose secrets; copies in the
  owner's password manager.
- **Backups.** `pg_dump` at 02:00, encrypted and de-duplicated by restic, to a B2 bucket in the EU region (7 daily, 4
  weekly, 6 monthly); a pre-deploy dump kept locally (last 5); OVH's included VM backup as a third layer. The VPS's B2
  key reaches one bucket and, if restic allows it, cannot delete; pruning runs from the owner's PC. A monthly automated
  restore test and a twice-yearly rebuild drill measure the real RTO.
- **Monitoring.** An external uptime monitor on `/health/ready`, `/health/data` and TLS expiry; dead-man pings for the
  backup, the restore test and the daily refresh.

## Consequences

### Positive

- Immutable image digests per release; rollback is a previous tag that is still on disk.
- Host OS updates cannot change the .NET runtime under the application.
- The same images run in the CI smoke job.
- About EUR 10 a month in total.
- Backups leave OVH, so a provider-wide failure does not take them.

### Negative

- One VPS is a single point of failure; an outage means the upstream fallback or CAN'T VERIFY (ADR-0024).
- Docker-published ports bypass the host firewall; only Caddy may publish.
- A PostgreSQL major upgrade is a manual dump and restore.
- Port 22 stays open to the internet for the deploy job.
- Hand-written Compose and scripts to maintain; the owner spends 1 to 2 hours a month on maintenance.

## Alternatives considered

- **systemd services** - rejected. Rollback means managing release folders by hand, the host's .NET runtime is not under
  CI control, and the house layout asks for Dockerfiles.
- **k3s** - rejected. A cluster's moving parts and memory for one node and one maintainer.
- **Traefik** - rejected. Its configuration spreads over Docker labels and it needs the Docker socket.
- **nginx with certbot** - rejected. Two components and renewal hooks to maintain.
- **Aspire's Compose publisher** - rejected. A hand-written Compose file is smaller and reviewable.
- **OVH backups only** - rejected. They stay in the same datacentre and provider.
- **Cloudflare in front** - rejected. It would terminate TLS outside the owner's control and see users' coordinates.
