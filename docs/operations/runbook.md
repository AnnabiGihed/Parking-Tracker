# Back-end runbook

How the Parking Tracker back end is set up, deployed, rolled back, backed up and checked on the shared OVH server
([ADR-0028](../adr/0028-host-on-the-shared-ovh-server-with-ovh-backups-only.md)), with the deployment path of
[ADR-0023](../adr/0023-deploy-from-github-actions-through-an-ssh-forced-command.md). Commands marked "on your PC" run
in Git Bash or a Linux shell with the GitHub CLI signed in; commands marked "on the server" run over SSH as the
server's administrator account (`ubuntu`).

## What runs where

| Piece | Where | Notes |
| --- | --- | --- |
| API, migrator, PostgreSQL with PostGIS | Docker Compose project `parking-tracker` in `/opt/apps/parking-tracker` | Memory limits 224, 128 (deployments only) and 256 MB |
| Public names | `api.parking.pivotsoftwares.com`, `admin.parking.pivotsoftwares.com`, `parking.pivotsoftwares.com` | The shared Caddy; admin answers 503 until stage S8, the apex serves the privacy notice |
| Images | `ghcr.io/annabigihed/parking-tracker-api` and `-migrator`, private | Built and pushed by `.github/workflows/backend.yml` |
| Deploy account | `parking-deploy`, key restricted to `/usr/local/bin/pt-deploy` | Not in the `docker` group; one `sudoers` line for `pt-deploy-root` |
| Secrets | `/opt/apps/parking-tracker/secrets` | Database passwords are generated on the server and never leave it |
| Backups | `/opt/apps/parking-tracker/backups` and OVH's daily server backup | No off-site copy |

## One-time setup

### 1. Protect `main`

A merge to `main` deploys, so `main` needs a pull request for every change. On GitHub: Settings, Branches, add a
branch protection rule (or a ruleset) for `main` that requires a pull request.

### 2. Create the deploy key and the production environment

On your PC, create a key pair for the deploy job only, and read the server's host key:

```bash
ssh-keygen -t ed25519 -N "" -C "parking-deploy" -f parking-deploy
ssh-keyscan -t ed25519 api.parking.pivotsoftwares.com > parking-known-hosts
```

Compare the fingerprint that `ssh-keygen -lf parking-known-hosts` prints with the one in the OVH console before you
trust it. Then create the `production` environment and store the values:

```bash
gh api -X PUT repos/AnnabiGihed/Parking-Tracker/environments/production \
  -F 'deployment_branch_policy[protected_branches]=true' -F 'deployment_branch_policy[custom_branch_policies]=false'
gh secret set DEPLOY_SSH_KEY --env production < parking-deploy
gh variable set DEPLOY_HOST --env production --body api.parking.pivotsoftwares.com
gh variable set DEPLOY_KNOWN_HOSTS --env production < parking-known-hosts
```

Delete the private key file `parking-deploy` from your PC afterwards; keep `parking-deploy.pub` for step 4.

### 3. Create the image read token

On GitHub, create a classic personal access token with the `read:packages` scope only and no expiry longer than you
are willing to rotate. The server uses it to pull the private images. You type it at the provisioning prompt; it is
never stored in the repository or in GitHub.

### 4. Provision the server

On your PC, copy the `deploy/` folder of `main` and the public key to the server:

```bash
scp -r deploy parking-deploy.pub ubuntu@api.parking.pivotsoftwares.com:~/
```

On the server:

```bash
sudo bash ~/deploy/server/provision.sh --deploy-key ~/parking-deploy.pub
```

The script only adds: the `parking-deploy` account, the programs and the `sudoers` line, the folders and secrets, the
Parking Tracker block in the shared Caddyfile (between its marker lines, validated before the reload) and the backup
timers. It prints whether `sshd` limits sign-in with `AllowUsers` or `AllowGroups`; if it does, add `parking-deploy`
there yourself. Run it again after any change to `deploy/server/`, `deploy/parking.Caddyfile` or `deploy/site/`: a
deployment never replaces the programs, the timers or the Caddy block. A change that adds a secret or a Caddy header
needs the script from its branch on the server before the pull request is merged, because the deployment that follows
the merge needs the secret file and the new block.

The script also creates the proxy key, `secrets/app/proxy_key` (owner uid 1654, mode 0400), once: a rerun keeps it. It
writes the same key into the Parking Tracker block of the shared Caddyfile (`header_up X-PT-Proxy-Key`), and the API
reads it as `ReverseProxy__Key`. The API trusts `X-Forwarded-For`, `-Proto` and `-Host` only on a request that carries
the key (ADR-0029), and in Production it refuses to start without it. Check that the shared Caddy sets no
`trusted_proxies`.

The script also creates the three secrets of the sign-in, each once (a rerun keeps it; a file that exists but is empty
stops the script, because a new value would orphan the accounts or sign everyone out; restore it from its copy), owner uid 1654, mode 0400, and
the API reads them as the settings named in brackets: `secrets/app/token_signing_key_current.pem` (a P-256 private key,
`Tokens__SigningKeys__Current`), `secrets/app/token_signing_key_previous.pem` (empty until the first rotation,
`Tokens__SigningKeys__Previous`) and `secrets/app/subject_hash_key` (32 random bytes in base64,
`Accounts__SubjectHashKey`). In Production the API refuses to start without the current key and the subject hash key
(ADR-0014). **Copy `subject_hash_key` to the password manager now:** the account identity is the keyed hash of the
Google subject, so a lost or changed key orphans every account and the erasure log. The pull request that adds these
secrets needs `provision.sh` from its branch run on the server before it is merged, or the deployment that follows
fails at start. The Google Web client identifiers are configuration, not secrets: `appsettings.Production.json` holds
the development client; add the production client there when it exists.

The apex, `https://parking.pivotsoftwares.com`, serves the privacy notice: `/privacy` answers with the page,
`/` redirects to it and any other path answers 404. The page is `deploy/site/privacy-notice.html`; the script copies it
into the Caddy block (braces escaped; a page with the heredoc marker or a backslash is refused) and fails when the file
is missing from the copied `deploy/` folder. To change the notice, edit that file, merge it, copy `deploy/` again
and rerun `provision.sh`. The Google consent screens use `https://parking.pivotsoftwares.com` as the home page,
`https://parking.pivotsoftwares.com/privacy` as the privacy policy and `pivotsoftwares.com` as the authorized domain.

Check from your PC that the three names answer over HTTPS with a valid certificate (the API and admin answer 503 until
their deployment):

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://api.parking.pivotsoftwares.com/health/ready
```

### 5. Monitoring and outside checks

- Create an uptime monitor (any free service) on `https://api.parking.pivotsoftwares.com/health/ready` with TLS expiry
  alerts.
- Optional dead-man pings: put a check URL in `/opt/apps/parking-tracker/secrets/deadman_dump_url` and
  `deadman_restore_url` (root, mode 0400); `pt-backup` calls it after each successful dump or restore test.
- Once, after the first deployment: check from outside that the server shows no new open port
  (`nmap -Pn api.parking.pivotsoftwares.com` shows 22, 80, 443 and the other application's 8080 only), and grade
  `api.parking.pivotsoftwares.com` on SSL Labs.

## Deploy

Every push to `main` that changes more than Markdown runs `backend.yml`: build, smoke test, publish, then `deploy` in
the `production` environment. The deploy job calls `ssh parking-deploy@api.parking.pivotsoftwares.com deploy
sha-<12>`, and `pt-deploy-root` then:

1. refuses between 02:30 and 05:00 Brussels time (the refresh jobs' window) unless the command ends with `force`;
1. pulls both images by tag and records their digests;
1. copies `/deploy` out of the migrator image into a staging folder, refuses anything but regular files, and checks
   the Compose file with `pt_compose_check.py`, an allow-list: only the keys this project uses, a non-root `user`, a
   read-only root file system, all capabilities dropped, no-new-privileges, the project's own named volume and the
   `data` and `web` networks only, secrets from the project's secrets folder only; only then the folder becomes
   `releases/sha-<12>`;
1. starts PostgreSQL, applies `sql/roles.sql`, dumps the database to `backups/pre-deploy` (the last 5 are kept) and
   runs the migrator; a failure here leaves the running release as it was;
1. starts the new API and polls `/health/ready` through the shared Caddy for 90 seconds, checking that the running
   container is the pulled image;
1. moves the `current` and `previous` links and appends to `releases.log`; when the API does not start or is not
   healthy, starts the running release's API again and exits with code 3.

Deploying the release that already runs is refused, except as a drill. Exit codes: 0 deployed, 1 failed, 2 refused,
3 rolled back after the health gate.

A failed migration is not undone: migrations follow expand and contract, so the previous release runs on the new
schema; a schema rollback is a restore of the pre-deploy dump (see "Restore").

## Prove the rollback (drill)

The rollback needs an earlier release, so run the drill after the second deployment. On GitHub: Actions, Back end,
Run workflow on `main` with `drill` ticked. The run deploys the current `main` release again, the health gate polls a
path that is never healthy, and the server must fall back to the release before it, a different image; the job passes
only when the server answers with exit code 3, the rollback. Afterwards `status` shows the earlier release as
`current` and the drilled one as `previous`; run `rollback` (below), or merge the next change, to bring the newer
release back.

## Status and manual rollback

On your PC, with the deploy key (or from the server as root, `pt-deploy-root status`):

```bash
ssh -i parking-deploy parking-deploy@api.parking.pivotsoftwares.com status
ssh -i parking-deploy parking-deploy@api.parking.pivotsoftwares.com rollback
```

`rollback` starts the previous release's API and swaps the `current` and `previous` links; run it again to go
forward.

## Backups

| Layer | When | Where | Kept |
| --- | --- | --- | --- |
| Nightly dump | 02:00 Brussels time | `backups/nightly-*.dump` | 7 days |
| Pre-deploy dump | Every deployment | `backups/pre-deploy/*.dump` | Last 5 |
| Whole server | Daily, by OVH | OVH Automated Backup | As OVH keeps it |

The restore test runs on the first day of each month at 05:30 Brussels time: it restores the newest nightly dump into
a throw-away container without network and checks the migrations history. See the timers and the last results on the
server:

```bash
systemctl list-timers 'parking-tracker-*'
journalctl -u parking-tracker-dump -u parking-tracker-restore-test --since -7d
```

### Restore

To put a dump back into the running database, on the server as root:

```bash
cd /opt/apps/parking-tracker
docker compose -p parking-tracker stop api
docker exec -i parking-tracker-postgres-1 pg_restore -U postgres -d parkingdata --clean --if-exists \
  < backups/pre-deploy/<file>.dump
docker compose -p parking-tracker start api
```

Erasures made after the dump must not come back, so the restore keeps the erasure log (architecture 6.1). On the
server as root, `pt-erasure-reapply` does it in three steps around the restore above:

```bash
# 1. Before the restore: export the erasure log of the live database (CSV, mode 600).
pt-erasure-reapply export /root/erasure-log-$(date -u +%Y%m%d).csv

# 2. Restore as above, with the API stopped.

# 3. Before the API starts: a dry run prints what would change and changes nothing, then the real run.
pt-erasure-reapply apply /root/erasure-log-<date>.csv
pt-erasure-reapply apply /root/erasure-log-<date>.csv --commit
docker compose -p parking-tracker start api
```

The dry run and the real run are the same transaction; the first ends with `ROLLBACK`, the second with `COMMIT`.
It prints `accounts_deleted` (the accounts of the list that the restored database still has, their sessions go with
them), `log_rows_merged` and `log_rows_purged` (rows older than 6 months; the cleanup job of S6 purges the live log in
the same way). The tool refuses a file with any line that is not `<64 hex characters>,<yyyy-mm-dd>` before it touches
the database. Only accounts created on or before the day of the erasure are deleted, so a person who signed up again
after erasing keeps the new account; an erasure and a new sign-up on the same day cannot be told apart, and the re-apply
would remove that new account (the person signs in again). Keep the exported file until the restore is checked,
then delete it: it holds keyed hashes of the subjects, not the subjects. After a total loss nothing newer exists to
re-apply.

A total loss of the server and of OVH's backup means: provision a server again with this runbook, deploy, and the
drivers sign in again; the owner re-enters the admin allow-list, and the run history starts again (ADR-0028).

## Sign-out and account deletion

- `DELETE /api/v1/sessions/current` revokes the refresh tokens of one device at once. An access token already issued
  keeps working until it expires (15 minutes at most); there is no list of revoked access tokens. To cut an account off
  immediately, delete it.
- `DELETE /api/v1/me` deletes the account and its sessions and records the keyed hash of the subject in `erasure_log`
  in one transaction. The old access token then gets `401` from `GET /api/v1/me`. The same Google account can sign in
  again and gets a new account; the log only serves to apply the erasure after a restore.
- A refresh refused because its token was used before revokes the whole device family, and the log carries
  `Refresh refused: refresh.reused`; other reasons are `refresh.unknown`, `refresh.revoked`, `refresh.expired`,
  `refresh.malformed` and `save.failed` (a second request won the race for the same token). The answer is always the
  same `401`.

## Memory

The budget is about 1 GB on a 4 GB server shared with other applications (ADR-0028). Check it on the server:

```bash
docker stats --no-stream --format 'table {{.Name}}\t{{.MemUsage}}' | grep parking-tracker
free -h
```

The smoke job of each `backend.yml` run writes the same figures to its summary; stage S6 compares them with a full
refresh. A container that reaches its limit is restarted by Docker; raising a limit is an owner decision, not a fix.

## Rotate a secret

- **Database passwords:** replace the file under `secrets/postgres/` (keep the owner and mode 0400), run
  `provision.sh` again to rewrite the connection strings, then deploy: `sql/roles.sql` sets the new password.
- **Proxy key:** delete `secrets/app/proxy_key`, run `provision.sh` again (it creates a new key and reloads Caddy with
  it), then redeploy the current tag right after, so that the API reads the new key. Until that deployment finishes
  the API holds the old key, forwarded client addresses are ignored and callers share one rate-limit bucket.
- **Token signing key:** run `sudo bash deploy/server/provision.sh --rotate-signing-key`. It rotates the files and
  recreates the API container of the current release (`docker compose ... up -d --force-recreate --no-deps api`, the
  invocation of `pt-deploy-root`), so the API reads the new keys at once; before the first deployment there is nothing to
  recreate. If the recreate fails the script says so after the rotation: deploy the current tag or restart the API. The
  old current key becomes the previous one and is still accepted,
  so nobody is signed out; new tokens carry the new key id. The key before the previous one is dropped, so wait 15
  minutes (the life of an access token) before rotating again. A refresh token does not depend on the key. If the
  current key leaks, rotate twice, 15 minutes apart, to retire it.
- **Subject hash key:** never. A new key gives every user a new account hash, orphans the accounts and the erasure log
  and makes an erased user appear new. Restore the file from the password manager if it is lost; without a copy the
  accounts of the old key cannot be matched again.
- **Image read token:** replace `secrets/ghcr_token` (root, 0400); the next deployment uses it.
- **Deploy key:** create a new pair, run `provision.sh --deploy-key` with the new public key, and replace
  `DEPLOY_SSH_KEY` in the `production` environment.
