# ADR-0017: Build a public admin web app on Blazor Server and Radzen, protected by Google sign-in and an allow-list

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

Administrators need to see the refresh runs, their failures, durations and row counts, the freshness of every dataset,
rejected datasets and alerts, and to change schedules or start a run. The owner decided that the admin web app is
public (reachable from anywhere without a tunnel), protected by Google sign-in plus an allow-list of Google account
IDs, with the hardening this requires. Sign-in uses Google directly (ADR-0014). Hangfire's dashboard is useful for job
internals but its Pivot filter expects Keycloak (ADR-0013).

Options considered:

1. Public, Google sign-in plus an allow-list, hardened; domain data through the API.
1. Private: no public DNS record, reached through an SSH tunnel or a WireGuard or Tailscale interface.
1. Public with an IP allow-list in Caddy.
1. The Hangfire dashboard as the only admin UI.

## Decision

We build `ParkingTracker.AdminWeb`, Blazor Server (interactive server render mode) with Radzen.Blazor, public at
`https://admin.<domain>` (option 1).

- **Sign-in.** OpenID Connect against Google (code flow with PKCE, confidential Web client, redirect
  `/signin-oidc`), cookie authentication. Access needs `email_verified` and a Google `sub` on the allow-list.
- **Allow-list.** A secret file of Google `sub` values (e-mail kept only as a label), read by the admin app and the
  API, not in the repository, not editable from the app. It is checked again on every request, so a removal takes
  effect at once; adding an ID needs a restart.
- **Authorization everywhere.** A fallback policy `AdminOnly` covers every page, the Blazor hub and `/jobs`; only the
  OpenID Connect callback paths and the framework's static assets are anonymous.
- **Data path.** Screens read and act only through the API's admin endpoints on the internal Docker network, with an
  admin access token obtained by exchanging the admin's ID token (ADR-0014) and kept in a server-side session store
  (`IDistributedCache` in memory, one instance). Caddy does not route `/api/v1/admin/*` from the internet.
- **Hangfire dashboard.** Mounted at `/jobs` against the same storage with no Hangfire server, behind the `AdminOnly`
  policy (`MapHangfireDashboardWithAuthorizationPolicy`, to verify) and a dashboard filter that evaluates the same
  policy. The admin app's database role reaches the `hangfire` schema only.
- **Hardening.** Per-IP rate limiting (20 requests per minute on the callback and anonymous requests, 300 per minute
  for signed-in admins); HSTS; a Content Security Policy with `frame-ancestors 'none'` and WebSocket allowed for the
  Blazor circuit; `X-Content-Type-Options: nosniff`; `Referrer-Policy: no-referrer`; a restrictive
  `Permissions-Policy`; `__Host-` cookies that are `Secure`, `HttpOnly`, `SameSite=Lax`, 8 hours absolute and 1 hour
  idle; Data Protection keys on a volume; forwarded headers trusted from Caddy only; antiforgery on.
- **Owner's rule.** Every allow-listed Google account uses 2-step verification.
- **Screens.** Freshness board (areas by sources, status shown by label and icon, not colour alone), alerts, run list
  and run detail, duration and row-count trends, rejected datasets with approve and discard, schedules with run now,
  upstream health per source. No driver data is shown, because none is stored.

## Consequences

### Positive

- Admins reach the app from any network with their Google account.
- Authorization of domain actions lives in one place, the API; the admin app holds no database credentials beyond the
  Hangfire schema.
- Domain facts (freshness, row counts, rejected datasets) instead of only job internals.

### Negative

- An internet-facing admin surface: it must be patched, its headers and policies tested, and its logs watched.
- Admin security rests on the admins' Google accounts; 2-step verification cannot be enforced by the app.
- In-memory sessions: a restart signs admins out; a second instance would need a shared cache.
- An allow-list change needs a restart of the admin app and the API.

## Alternatives considered

- **Private admin through a tunnel** - rejected by the owner's decision of 2026-11-17. It is stronger and remains the
  option if the public surface ever causes trouble.
- **IP allow-list in Caddy** - rejected. It breaks on mobile networks and changing addresses.
- **Hangfire dashboard only** - rejected. It shows jobs, not freshness or rejected datasets, and its Pivot filter does
  not fit Google sign-in.
- **Admin app reading the database directly** - rejected. A second place to enforce authorization and a second set of
  database credentials for domain data.
- **Blazor WebAssembly** - rejected. Tokens would live in the browser; Blazor Server keeps them on the server.
