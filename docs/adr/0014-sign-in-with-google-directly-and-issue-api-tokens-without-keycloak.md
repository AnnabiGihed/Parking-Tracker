# ADR-0014: Sign in with Google directly and issue the API's own tokens, without Keycloak

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

Drivers and administrators sign in with Google. The owner decided on 2026-11-17: Google sign-in directly (ASP.NET Core
OpenID Connect and Google ID-token validation, JWT for the API), no Keycloak, and account erasure as the app's own
endpoint.

Pivot.Framework's authentication packages are Keycloak-only. In the source at commit `c6c6bf0`, `KeycloakOptions`
builds every URL as `{BaseUrl}/realms/{Realm}/...`, roles are read from Keycloak's `realm_access` claim, the MAUI
package registers a Keycloak PKCE flow with a `{ClientId}://callback` redirect, the Blazor package expects a Keycloak
confidential client, the Hangfire package expects a Keycloak token, and `IIdentityProviderAdminService` targets the
Keycloak admin API. None of them can point at Google. Using Google directly is therefore a deviation from the Pivot
frame, which names Keycloak through the Pivot authentication packages.

Further forces:

- The background parking alert runs in an Android foreground service and must call the API while the app is off
  screen. A Google ID token lives one hour, and Android Credential Manager offers no supported silent refresh from a
  service without UI.
- Google access tokens are opaque and are not meant for our API; Google ID tokens are meant for the client that asked.
- Personal data on the server must stay minimal (ADR-0015).
- Google forbids OAuth in embedded web views, so the login cannot run inside the `BlazorWebView`.

Options considered:

1. Exchange a Google ID token once for the API's own short-lived JWT and a rotating refresh token.
1. Use the Google ID token itself as the bearer token on every call.
1. Keycloak brokering Google, with Pivot's authentication packages.
1. A full OAuth server of our own (OpenIddict) or a managed identity service (Auth0, Firebase Authentication).

## Decision

We sign in with Google directly and the API issues its own tokens (option 1).

- **Mobile app.** Android Credential Manager ("Sign in with Google") with the API's Web client ID as the server client
  ID and a nonce from `POST /api/v1/sessions/challenges`. The app sends the Google ID token once to
  `POST /api/v1/sessions`. The login runs in Google's own UI.
- **Exchange.** The API validates the ID token with ASP.NET Core's token handlers against Google's OpenID
  configuration and cached signing keys: signature, issuer (`accounts.google.com` or `https://accounts.google.com`),
  audience (the configured client IDs), expiry and the single-use nonce. It finds or creates the `UserAccount` by a
  keyed hash of the Google `sub` and issues:
  - an access token: JWT signed with ES256 by the API's own key (`kid` in the header, two keys accepted during a
    rotation), issuer `https://api.<domain>`, audience `parkingtracker-api`, lifetime 15 minutes, claims `sub` (the
    account ID) and, for admins only, `role=admin`;
  - a refresh token: 256 random bits, stored only as a SHA-256 hash in a `DeviceSession`, single use, rotated on every
    refresh, 30 days sliding and 90 days absolute; presenting a rotated token revokes the whole family.
- **API.** Every other endpoint uses `Microsoft.AspNetCore.Authentication.JwtBearer` against the API's own public
  key, with a fallback policy that requires an authenticated user. The admin role is issued only for the admin
  client's audience and a `sub` on the allow-list (ADR-0017).
- **Admin web app.** `Microsoft.AspNetCore.Authentication.OpenIdConnect` against `https://accounts.google.com`, code
  flow with PKCE, a confidential Web client, cookie sign-in; the admin's ID token is exchanged at the API right after
  the callback, and the API tokens stay in the admin app's server-side session store.
- **Sign-out** is `DELETE /api/v1/sessions/current`; **account erasure** is `DELETE /api/v1/me`, our own endpoint:
  it hard-deletes the account and its sessions and records the subject hash in an erasure log for 6 months, so a
  restored backup can be cleaned again.
- **Pivot's current-user contract** is implemented from our own claims, so audit stamps carry the account ID, never
  an e-mail.

Not used from Pivot.Framework: `Pivot.Framework.Authentication.AspNetCore` (`AddKeycloakAuthentication`, the
Containers.API overload with Redis token caching and revocation, role policies on `realm_access`),
`Pivot.Framework.Authentication.Blazor` (`AddKeycloakBlazor`), `Pivot.Framework.Authentication.Maui`
(`AddKeycloakMaui`, `AddKeycloakHandler`), `Pivot.Framework.Authentication.Hangfire`
(`AddHangfireKeycloakBrowserAuth`), `Pivot.Framework.Authentication.API` (BFF) and `IIdentityProviderAdminService`.
An upstream issue proposes a generic OpenID Connect option for Pivot's authentication packages; if it ships, a new ADR
can revisit this one.

## Consequences

### Positive

- No identity server to run, patch and back up; about 0.5 to 1 GB of RAM saved on the VPS.
- The background service refreshes tokens without UI, so the parking alert keeps working.
- Server-side sign-out and revocation; token audience and lifetime under our control.
- A Google outage stops new sign-ins only; signed-in users keep working with the API's tokens.
- The server stores no e-mail address or name for drivers.

### Negative

- We own a signing key, its storage, rotation and loss procedure, and a refresh-token store.
- The exchange endpoint is security-critical code we write; every refusal path needs a test and an independent review.
- A permanent deviation from the Pivot authentication frame, with hand-written wiring in three hosts.
- Credential Manager from .NET MAUI depends on Android bindings that are still to be verified (spike in stage S5).
- One identity provider only; adding a second one would reopen this decision.

## Alternatives considered

- **Google ID token as the bearer token** - rejected. One-hour tokens without a supported silent refresh in the
  foreground service would break the background alert; no server-side sign-out; Google's token would travel on every
  call. It stays the fallback if the exchange endpoint proves too costly.
- **Keycloak brokering Google** - rejected by the owner (D2): one more component in the login path, about 0.7 GB of
  RAM, its own patching and backups, and every driver's e-mail and name stored in Keycloak.
- **OpenIddict or a managed identity service** - rejected. OpenIddict makes us a general OAuth server, far more surface
  than one exchange endpoint; a managed service adds a third-party processor of personal data and cost, and still no
  Pivot package fits.
