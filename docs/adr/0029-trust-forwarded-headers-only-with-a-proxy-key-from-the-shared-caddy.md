# ADR-0029: Trust forwarded headers only with a proxy key from the shared Caddy

- Status: Accepted
- Date: 2026-11-19
- Deciders: Gihed Annabi

## Context

The API sits on the shared Docker network `web` with the containers of other projects (ADR-0028). The shared Caddy
reaches it there. The rate limits of 6.2 are per IP, so the API must know the real client address from
`X-Forwarded-For`.

- The IP of the shared Caddy is assigned by Docker when that container is created. The proxy stack belongs to another
  project, so we neither fix nor control it.
- Trusting the subnet of `web` trusts every neighbour on it.
- Today `ApiHostExtensions.ConfigureForwardedHeaders` clears `KnownProxies` and `KnownIPNetworks`, so any sender on
  `web` can set `X-Forwarded-For`. The per-IP limits would be spoofable. With no trust at all, every caller would
  share the address of Caddy, and so one bucket.

Options considered:

1. Put the subnet of `web` in `KnownIPNetworks`.
1. Put the container IP of Caddy in `KnownProxies`.
1. Share a proxy key: Caddy adds it as a header, and the API trusts the forwarded headers only with it.

## Decision

We trust forwarded headers only on a request that carries a proxy key the shared Caddy adds (option 3).

- `provision.sh` generates a random `proxy_key` secret: a file under `secrets/app`, owner uid 1654, mode 0400,
  mounted into the API.
- `provision.sh` writes `header_up X-PT-Proxy-Key <key>` into the parking block of the shared Caddyfile.
- The API applies `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` only when `X-PT-Proxy-Key` matches the
  key, compared in constant time. Otherwise it removes the `X-Forwarded-*` headers.
- The API always removes `X-PT-Proxy-Key` before the request goes further.
- Caddy 2.5 and later already discards forwarded headers sent by clients unless `trusted_proxies` is set. The runbook
  checks that setting. The Caddy version of the server is to be verified.

## Consequences

### Positive

- It works whatever IP Docker gives to Caddy, also after Caddy is recreated.
- Neighbours on `web` cannot spoof the client address.
- The rate limits of 6.2 see the real client IP.

### Negative

- One more secret to provision and keep.
- Rotating it means running `provision.sh` again, reloading Caddy, then deploying the API.
- A leak of the key lets a neighbour spoof its own IP, and nothing more.

## Alternatives considered

- **Subnet of `web` in `KnownIPNetworks` (option 1)** - rejected: it trusts every container of the other projects.
- **Container IP of Caddy in `KnownProxies` (option 2)** - rejected: Docker assigns the IP when the container is
  created, so a recreated Caddy would break the trust or need a change in another project's stack.
