# Authentication and scrape resilience

## Application access key

The API has two independent credentials:

```text
access-key UUID       -> authorizes use of this hosted API
LEB2 session cookie   -> authenticates the outbound request against LEB2
```

The `access-key` header contains an existing `keys.id` UUID from the owner's
Supabase PostgreSQL database:

```http
access-key: <provisioned-uuid>
```

The backend does not generate keys, hash them, expose key-management routes, or
automatically reassign them. It checks the database on each request, so deleting a
key or its `user_keys` row takes effect without cache delay. Database failures fail
closed and return `ACCESS_KEY_STORE_UNAVAILABLE` for transient failures.

Key states:

- A missing key is rejected everywhere.
- A provisioned but unassigned key is accepted only by `/User/login`.
- An assigned key is required by `/User/cookie` and all data routes.

First-use flow:

1. The owner inserts a UUID into `keys` directly in Supabase.
2. The user receives that UUID out of band.
3. The client calls `/User/login` with `access-key` and LEB2 credentials.
4. After successful LEB2 authentication, the backend upserts `users` by trimmed login identifier and claims the key in one PostgreSQL transaction.
5. The client calls `/User/cookie` with the assigned key.
6. Normal data requests send both `access-key` and the opaque LEB2 session cookie.

`users.student_id` is the trimmed `/User/login` username. `users.name` uses the
successful LEB2 English name when available, with the existing Thai fields as a
fallback. Audit fields use `leb2scrapper-api`.

Deleting a `user_keys` row makes the key unassigned again. Deleting a `keys` row
invalidates it and cascades its assignment under the supplied schema. The backend
never moves a key from one user to another.

## Supabase connection

The backend uses direct Npgsql access through the configuration key
`ConnectionStrings:Supabase`. It never creates tables or runs migrations. Keep the
connection string in user secrets locally and Secret Manager in Cloud Run; never
commit it.

## Request authentication

The backend does not store LEB2 credentials or session cookies. Authenticated data
routes require the client-held LEB2 cookie on every request:

```http
Authorization: Bearer <session-cookie-value>
```

The value after `Bearer` is opaque. It is not parsed or validated as a JWT. The custom
ASP.NET Core authentication handler only checks that one credential was supplied; it
does not cryptographically verify the session, issue access tokens, or prove that
LEB2 will accept it. The handler keeps it in a scoped request object, repositories
use it as the outbound LEB2 `Cookie` header, and the scoped value is cleared when the
response completes. Actual session validity comes from LEB2 responses.

For compatibility with clients of earlier releases, the same opaque value is also
accepted directly in `Authorization` without the `Bearer` prefix. New clients should
use the Bearer form; the legacy form can be removed in a future version after clients
have migrated.

The following routes require this header in addition to an assigned `access-key`:

- `GET /Semester`
- `GET /Class/{id}`
- `GET /Activity/{semesterId}/{classId}`
- `GET /Activity/{semesterId}`
- `GET /Activity/{semesterId}/snapshot`

All activity routes also require a positive integer user ID in:

```http
X-LEB2-USER-ID: <user-id>
```

`POST /User/login` requires only a provisioned access key because it performs the
enrollment transaction. `POST /User/cookie` requires an assigned key but does not
change local registration. LEB2 credentials are used only for the outbound call in
that request and are not persisted.

## Error contract

Errors use a JSON `responseCode` so clients can distinguish authentication from LEB2
or scraper failures.

| HTTP status | `responseCode` | Meaning |
| --- | --- | --- |
| `400` | `INVALID_REQUEST` | Request input failed validation. |
| `401` | `ACCESS_KEY_REQUIRED` | The `access-key` header is absent. |
| `401` | `ACCESS_KEY_INVALID` | The access key is malformed or not provisioned. |
| `401` | `AUTHENTICATION_REQUIRED` | Bearer header is absent or malformed. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. Discard the client-held cookie and reauthenticate. |
| `403` | `ACCESS_KEY_NOT_ACTIVATED` | The key has not been claimed through `/User/login`. |
| `403` | `ACCESS_KEY_ALREADY_ASSIGNED` | The key is assigned to another account. |
| `404` | `RESOURCE_NOT_FOUND` | The requested resource or class/semester relationship was not found. |
| `408` | `LEB2_UNAVAILABLE` | The request timed out. |
| `429` | `CLIENT_THROTTLE_ACTIVE` | This client already has the maximum number of active and queued LEB2 operations. The response includes `Retry-After: 1`. |
| `500` | `UNEXPECTED_ERROR` | An unexpected server error occurred. |
| `502` | `LEB2_UNAVAILABLE` | LEB2 rejected or could not complete the upstream request. |
| `502` | `SCRAPE_RESPONSE_CHANGED` | LEB2 responded, but its HTML or JSON shape no longer matches the scraper. |
| `503` | `LEB2_UNAVAILABLE` | A transient LEB2 network, timeout, rate-limit, or server failure occurred. |
| `503` | `REQUEST_BACKOFF_ACTIVE` | A recent failure has temporarily paused this endpoint. The response includes `Retry-After`. |
| `503` | `ACCESS_KEY_STORE_UNAVAILABLE` | Supabase access-key validation is temporarily unavailable. |

As verified on 2026-07-24, an absent or invalid LEB2 session redirects both the class
page and activity API to `https://www.leb2.org/` with HTTP 302. The direct HTTP adapter
detects that redirect before following it; Selenium detects the resulting non-app host.

## Outbound request gate and backoff

All direct HTTP calls and top-level Selenium navigations pass through the singleton
outbound request gate. It:

- caps concurrent LEB2 operations at four globally and two per client;
- queues at most eight additional operations per client before returning `429`;
- applies exponential backoff per endpoint;
- never caches session cookies or scraped user data;
- clears backoff after a successful request;
- does not treat session expiry as a scrape failure;
- correlates structural parse failures for alerting.

There is no scheduler, background service, or backoff timer.

Defaults are configured under `OutboundRequestGate` in `appsettings.json`. A structural
alert is raised after at least three same-shape failures for an endpoint within
15 minutes only when those failures involve at least two distinct clients. One
client can accumulate backoff but cannot trigger an alert alone. Alert delivery runs
outside the failed request, is time-bounded, and a failed delivery remains eligible
for retry after the next matching failure.

Client identity inside the process is an opaque HMAC fingerprint. Authenticated
routes fingerprint the session cookie; credential-acquisition routes fingerprint a
normalized username in a separate HMAC domain. The random HMAC key exists only for
the life of the process. Raw cookies, usernames, and plain hashes are not used as
gate, cache, or alert-correlation keys.

## Aggregate activities

`GET /Activity/{semesterId}` discovers the semester's classes once, de-duplicates
their positive class IDs, and loads activities with maximum parallelism two. It
returns a flat activity list ordered by class ID while preserving LEB2's order within
each class. An empty list is returned when the semester contains no published
classes.

`GET /Activity/{semesterId}/{classId}` first discovers classes for the supplied
semester through the existing structural scrape cache, then verifies that the class
ID belongs to that semester before retrieving activities. A missing relationship
returns `404 RESOURCE_NOT_FOUND`; class discovery failures use the existing error
contract. The activity repository is not called when membership validation fails.

`GET /Activity/{semesterId}/snapshot` uses the same class discovery and activity
retrieval path as the flat semester route. It returns the semester ID and an
ID-ordered class list containing each class name and activities. Classes with no
activities remain in the successful response, and LEB2's ordering is preserved
inside each class.

All data routes require an assigned `access-key`, integer route values,
`Authorization: Bearer <session-cookie-value>`, and
`X-LEB2-USER-ID`. The aggregate request is intentionally fail-fast. If class
discovery or any activity request fails, remaining queued work is canceled and the
request returns the existing error contract. It never returns a successful partial
list.

## Structural scrape cache

Rendered semester and class results are cached in memory for 60 seconds by default.
Cache entries are partitioned by the opaque session fingerprint, and class entries
also include the semester ID. Successful empty class lists are cached; null results,
failures, cancellations, credentials, and cookies are not.

Concurrent misses for the same key are coalesced before an outbound permit or browser
is acquired. Mutable results are copied when stored and returned. Defaults are
configured under `StructuralScrapeCache` in `appsettings.json`, including the
10,000-entry capacity.

## Activity result cache

Successful activity results are cached in process memory for 30 seconds by default.
Entries are partitioned by the opaque session fingerprint, user ID, and class ID.
Successful empty activity lists are cached; failures and cancellations are not.
Expired data is removed and is never served as a fallback.

Concurrent misses for the same key are coalesced before the outbound request gate is
entered. The cache holds at most 2,000 entries by default. Cache lookups emit
structured `hit`, `miss`, or `coalesced` timing logs without session values, client
fingerprints, user IDs, or credentials.

## Snapshot performance scope

Semester and class discovery still requires rendered-page scraping because LEB2
does not expose a reusable structured semester/class-list API. The warm-instance
snapshot target of p95 at or below three seconds therefore applies only while the
60-second structural class cache is warm. Requests that miss that cache and launch
Chromium are measured separately.

Snapshot logs record secret-free class-discovery time, activity-retrieval time,
class count, activity cache status, and total snapshot duration. A fully warm
structural and activity cache is expected to be substantially faster, but no
scale-from-zero or Selenium-miss latency is included in the warm target.

## LEB2 dependency health

`GET /health/leb2` is unauthenticated and always returns HTTP `200` with
`Cache-Control: no-store`. It reports every fixed LEB2 dependency endpoint as
`available` or `unavailable`, plus its active retry time and retry delay. The response
includes `source: "local-observed-state"`. The overall status is `degraded` when any
endpoint has active backoff observed by this process and `healthy` when no endpoint
has active local backoff.

This endpoint does not contact LEB2. It is an application-local observation of
request-gate state, not a live reachability probe and not proof that LEB2 is currently
reachable.

The response deliberately excludes client fingerprints, credentials, failure
shapes, alert counts, URLs, and SMTP state.

## Process model

The structural cache, activity cache, fingerprints, throttling, backoff, alert
correlation, and health state are process-local. Production Cloud Run deployment
intentionally uses at most one active application instance, with Cloud Run HTTP
request concurrency set to two. The outbound gate remains process-local: its global
limit of four is application-wide only because production currently has one active
instance. Cloud Run request concurrency and outbound LEB2 concurrency are separate
limits.

The application has no persistent LEB2 user-session database. Supabase stores only
the local access-key enrollment data described above. Horizontal scaling still
requires distributed replacements for throttling, backoff, and caches, plus
distributed incident correlation and, if health is aggregated, distributed health
state. Otherwise each instance would have independent local state.

## Email alert configuration

Email is implemented behind `IFailureAlerter`; another channel can be added without
changing failure detection. Delivery is disabled until SMTP settings are supplied.
Configuration keys can be provided through normal ASP.NET Core configuration, including
environment variables:

```text
FailureAlerts__Email__Enabled=true
FailureAlerts__Email__SmtpHost=smtp.example.test
FailureAlerts__Email__SmtpPort=587
FailureAlerts__Email__EnableSsl=true
FailureAlerts__Email__Username=<smtp-username>
FailureAlerts__Email__Password=<smtp-password>
FailureAlerts__Email__FromAddress=alerts@example.test
FailureAlerts__Email__ToAddress=owner@example.test
FailureAlerts__Email__DeliveryTimeoutSeconds=30
```

Enabled email settings are validated when the application starts. Do not commit real
SMTP credentials or addresses to `appsettings.json`.
