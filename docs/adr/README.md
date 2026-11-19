# Architecture Decision Records

| # | Title | Status |
| --- | --- | --- |
| 1 | [Migrate the Android app to .NET 10 MAUI Blazor Hybrid on Pivot.Framework](0001-migrate-to-dotnet-maui-blazor-hybrid.md) | Superseded by [ADR-0009](0009-add-a-parking-data-service-and-keep-the-decision-on-the-phone.md) |
| 2 | [Use an own Android foreground service for location, parking alerts and timer notifications](0002-use-own-android-foreground-service-for-location-and-notifications.md) | Accepted |
| 3 | [Read parking-card photos with ML Kit text recognition on the device](0003-read-card-photos-with-ml-kit-on-device.md) | Accepted |
| 4 | [Migrate saved data from the Capacitor WebView storage once; keep the card in SecureStorage](0004-migrate-saved-data-from-the-capacitor-webview-once.md) | Superseded by ADR-0008 |
| 5 | [Waive StyleCop SA1514 where it conflicts with the region convention](0005-waive-stylecop-sa1514-for-the-region-convention.md) | Accepted |
| 6 | [Map the Pivot.Framework canonical layout onto an on-device mobile client](0006-mobile-client-layout.md) | Superseded by [ADR-0021](0021-map-the-canonical-layout-to-two-bounded-contexts.md) |
| 7 | [Exclude namespaces from CA1716 so the mandatory Features/Shared folder builds](0007-narrow-ca1716-to-exclude-namespaces.md) | Accepted |
| 8 | [Read the Capacitor data from the LevelDB files first, with a hidden web view as the fallback](0008-read-capacitor-data-from-leveldb-with-a-web-view-fallback.md) | Superseded by [ADR-0018](0018-keep-the-drivers-data-in-an-encrypted-on-device-sqlite-database.md) |
| 9 | [Add a parking data service on a VPS and keep the spot decision on the phone](0009-add-a-parking-data-service-and-keep-the-decision-on-the-phone.md) | Accepted |
| 10 | [Use PostgreSQL with PostGIS through Pivot's PostgreSQL persistence package](0010-use-postgresql-with-postgis-through-pivot-persistence.md) | Accepted |
| 11 | [Store upstream data as versioned snapshots per area with a publication guard and fail-closed freshness](0011-store-upstream-data-as-versioned-snapshots-with-fail-closed-freshness.md) | Accepted |
| 12 | [Drain the outbox in process in the Jobs host only, without a message broker](0012-drain-the-outbox-in-the-jobs-host-without-a-message-broker.md) | Accepted |
| 13 | [Run refresh jobs with Hangfire on PostgreSQL storage, outside Pivot's scheduling package](0013-run-refresh-jobs-with-hangfire-on-postgresql-outside-pivot-scheduling.md) | Accepted |
| 14 | [Sign in with Google directly and issue the API's own tokens, without Keycloak](0014-sign-in-with-google-directly-and-issue-api-tokens-without-keycloak.md) | Accepted |
| 15 | [Require sign-in in the mobile app and keep the driver's data on the phone](0015-require-sign-in-and-keep-the-drivers-data-on-the-phone.md) | Accepted |
| 16 | [Expose a REST API versioned by URL segment, documented with Swashbuckle, with Scalar in Development only](0016-expose-a-rest-api-versioned-by-url-segment-with-scalar-in-development.md) | Accepted |
| 17 | [Build a public admin web app on Blazor Server and Radzen, protected by Google sign-in and an allow-list](0017-build-a-public-admin-web-app-with-google-sign-in-and-an-allow-list.md) | Accepted |
| 18 | [Keep the driver's data in an encrypted on-device SQLite database through Pivot's SQLite package](0018-keep-the-drivers-data-in-an-encrypted-on-device-sqlite-database.md) | Superseded by [ADR-0026](0026-store-the-drivers-data-with-sqlite-net-pcl-on-the-phone.md) |
| 19 | [Model aggregates per bounded context and keep the decision engine as value objects and pure services](0019-model-aggregates-per-bounded-context-and-keep-the-decision-engine-pure.md) | Accepted |
| 20 | [Use Radzen.Blazor for the phone screens and keep Leaflet for the map](0020-use-radzen-for-the-phone-screens-and-keep-leaflet-for-the-map.md) | Accepted |
| 21 | [Map the canonical layout to two bounded contexts with one Domain and two Application projects](0021-map-the-canonical-layout-to-two-bounded-contexts.md) | Accepted |
| 22 | [Host on one OVH VPS with Docker Compose, Caddy and PostgreSQL, with off-site backups to Backblaze B2](0022-host-on-one-ovh-vps-with-docker-compose-and-off-site-backups.md) | Superseded by ADR-0028 |
| 23 | [Deploy from GitHub Actions with private GHCR images and an SSH forced command, with migration bundles](0023-deploy-from-github-actions-through-an-ssh-forced-command.md) | Accepted |
| 24 | [Fall back to the upstream services when the API cannot answer, switchable remotely](0024-fall-back-to-upstream-services-when-the-api-cannot-answer.md) | Accepted |
| 25 | [Test the API path with API-mode parity and Testcontainers PostgreSQL, without in-memory providers](0025-test-the-api-path-with-api-mode-parity-and-testcontainers.md) | Accepted |
| 26 | [Store the driver's data with sqlite-net-pcl on the phone](0026-store-the-drivers-data-with-sqlite-net-pcl-on-the-phone.md) | Accepted |
| 27 | [Run the app on a developer PC, not in CI](0027-run-the-app-on-a-developer-pc-not-in-ci.md) | Accepted |
| 28 | [Host on the shared OVH server, behind its Caddy, with OVH backups only](0028-host-on-the-shared-ovh-server-with-ovh-backups-only.md) | Proposed |
