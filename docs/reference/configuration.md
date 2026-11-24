# Configuration reference

The configuration sections of the API host (`ParkingTracker.ApiService`), with their defaults and which keys are
secrets. Non-secret defaults live in `appsettings.json` and `appsettings.Production.json`; secrets are mounted as one
file per key under `/run/secrets`, named like the key with `__` for `:` (see
[the architecture, 6.6](../architecture-v2.md)). The OpenAPI document is the reference of the endpoints, not this page.

## Sections

| Key | Default | Secret | Meaning |
| --- | --- | --- | --- |
| `ConnectionStrings:ParkingData` | none | yes | The PostgreSQL connection string of the API role; the host refuses to start without it |
| `ReverseProxy:Key` | none | yes | The key the shared Caddy adds to every request; Production refuses to start without it (ADR-0029) |
| `Authentication:Google:MetadataAddress` | Google's OpenID Connect metadata address | no | Where the signing keys of Google are read; outside Development it must be Google's |
| `Authentication:Google:ValidIssuers` | `accounts.google.com`, `https://accounts.google.com` | no | The issuers of a Google ID token; outside Development they must be Google's |
| `Authentication:Google:Audiences` | none in `appsettings.json`; the development Web client in `appsettings.Production.json` | no | The Google client identifiers an ID token may be meant for; outside Development at least one is required |
| `Tokens:Issuer` | `https://localhost`; `https://api.parking.pivotsoftwares.com` in Production | no | The issuer of the API access tokens; outside Development an https address |
| `Tokens:SigningKeys:Current` | none | yes | The P-256 private key (PEM) that signs the access tokens; outside Development it is required |
| `Tokens:SigningKeys:Previous` | empty | yes | The key of the rotation before, accepted for validation only; empty when there is none |
| `Accounts:SubjectHashKey` | none | yes | Base64 of 32 random bytes, the key of the subject hash; outside Development it is required; never rotated |

## Development

In Development the signing key and the subject hash key may be missing: the API makes one for the life of the process, so
the tokens and the accounts of a run do not survive a restart. The audiences stay empty until a Web client identifier is
configured, so no Google ID token is accepted. In every other environment the start-up checks above refuse a missing or
unusable value, and name the key in the message without ever printing the value.

## Behaviours worth knowing

- **Unknown paths and wrong methods answer 404.** The fallback policy requires a signed-in user on every endpoint, so an
  anonymous catch-all endpoint keeps a path that does not exist, or a method that an endpoint does not serve, a plain 404
  (not 401 and not 405). This is intended.
- **A rotated Google key.** Google rotates its signing keys. A token signed with a key that is newer than the metadata the
  API holds is refused (reason `token.signature`) until the metadata refresh interval passes; the refusal asks for a
  refresh, and the app asks for a new nonce and signs in again. The nonce of a refused token is kept, because the
  signature is checked before the nonce is consumed.
- **Body size.** `POST /api/v1/sessions` accepts a body of 16 KB at most and `POST /api/v1/sessions/refresh` one of 1 KB,
  and both answer 413 above it.
- **Sign-out and erasure do not recall access tokens.** `DELETE /api/v1/sessions/current` revokes the device's refresh
  tokens at once, but an access token already issued stays valid until it expires (15 minutes at most). After
  `DELETE /api/v1/me` an old access token names an account that no longer exists, so `GET /api/v1/me` answers 401;
  a second `DELETE /api/v1/me` with such a token answers 204.

## Fixed values

The audience of the access tokens is `parkingtracker-api`, their life is 15 minutes, a nonce lives 5 minutes, and the
rate limits are 10 session requests (challenge and sign-in) and, separately, 30 refresh requests per minute per client
address, and 120 requests per minute per account with a burst of 30. They are constants of the code, not settings.

## Mobile sign-in build settings

Stage S5 adds an optional account sheet to the phone's side menu. These MSBuild properties are compiled into the
Android host; they are separate from the API settings above and contain no secrets.

| Property | Default | Meaning |
| --- | --- | --- |
| `GoogleWebClientId` | Development Web client ID in Debug; empty in Release | The Google Web client ID for Credential Manager; empty hides the account entry |
| `ApiBaseUrl` | `https://api.parking.pivotsoftwares.com/` | The account API base address, ending with a slash |

Override either property with `-p:GoogleWebClientId=...` or `-p:ApiBaseUrl=...` on the Android build command. The API's
`Authentication:Google:Audiences` must accept that Web client ID. Production sign-in remains deferred to stage S9;
the map continues to work without signing in.

The sheet offers Google sign-in, account details, sign-out and account deletion with confirmation. Access and refresh
tokens are saved in encrypted storage, with the refresh token written first. Requests refresh an expiring access token
or retry once after a 401.
A refused refresh clears the session; network and server failures keep it. Sign-out clears the phone's session even
when the API cannot be reached. Account deletion clears it only after the API confirms success; it does not erase
parking cards or other data kept locally on the phone.
