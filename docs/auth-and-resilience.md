# Authentication and scrape resilience

## API v1 and compatibility gates

The canonical HTTP contract is URL-versioned at `/api/v1`; every application
controller explicitly serves API v1. `ApiVersioning:LegacyRoutesEnabled` (environment
form `ApiVersioning__LegacyRoutesEnabled`) defaults to `true` during migration so
deployed older APKs can use deprecated unversioned aliases. The aliases run through
the same controller, middleware, filters, and services; they are not v0 and do not
redirect. Set the flag to `false` after clients and monitoring move to `/api/v1` to
make old paths return `404`.

There is no v2 implementation. A future breaking HTTP contract must use `/api/v2`;
frontend build compatibility remains a separate concern from API URL versioning.

Middleware order for a routed request is:

```text
routing
  -> legacy-route gate
  -> client-version gate
  -> access-key authorization
  -> device-binding authorization
  -> LEB2 session authorization
  -> controller/service
```

`GET /api/v1/meta` and `GET /api/v1/health/leb2` are anonymous, cheap, and exempt
from both compatibility gates so a client can bootstrap and monitor during rollout.

## Application access key

The API keeps access authorization, LEB2 authentication, and LEB2 session state
separate:

```text
access-key            -> selects one authorized local student
LEB2 credentials      -> authenticate that student during login or cookie acquisition
LEB2 session cookie   -> authenticates later outbound requests against LEB2
X-LEB2-USER-ID        -> legacy client assertion checked against the stored LEB2 ID
```

The `access-key` header contains an existing `keys.id` UUID from the owner's
Supabase PostgreSQL database:

```http
access-key: <provisioned-uuid>
```

The backend does not generate keys, hash them, expose key-management routes, or
automatically reassign them. An activated key is bound to exactly one local student:

```text
keys.id
  |
user_keys
  |
users.student_id       normalized LEB2 login identifier
users.leb2_user_id     authoritative LEB2 User.Id
```

That binding prevents the key from logging in as another student, obtaining a LEB2
session for another student, or requesting activities with another LEB2 user ID.
The application checks the database at the access-key authorization stage on every
request, so deleting a key or its `user_keys` row takes effect without cache delay.
Database failures fail closed and return `ACCESS_KEY_STORE_UNAVAILABLE` for
transient failures.

Key states:

- A missing key is rejected everywhere.
- A provisioned but unassigned key is accepted only by `/api/v1/User/login`.
- An assigned key is required by `/api/v1/User/cookie` and all data routes.

First-use flow:

1. The owner inserts a UUID into `keys` directly in Supabase.
2. The user receives that UUID out of band.
3. The client calls `/api/v1/User/login` with `access-key` and LEB2 credentials.
4. After successful LEB2 authentication, the backend upserts `users` by normalized login identifier, stores the authoritative `User.Id` in `users.leb2_user_id`, and claims the key in one PostgreSQL transaction.
5. The client calls `/api/v1/User/cookie` with the assigned key.
6. Normal data requests send both `access-key` and the opaque LEB2 session cookie.

`users.student_id` is the normalized `/api/v1/User/login` username. `users.leb2_user_id`
comes only from the successful LEB2 `/api/v1/User/login` response; the client-supplied
`X-LEB2-USER-ID` is never persisted. Existing users with a null numeric identity
populate it on their next successful `/api/v1/User/login`; activity requests fail closed
until then with `ACCESS_KEY_REAUTHENTICATION_REQUIRED`.

`users.name` uses the
successful LEB2 English name when available, with the existing Thai fields as a
fallback. Audit fields use `leb2scrapper-api`.

Deleting a `user_keys` row makes the key unassigned again. After the production
migration, deleting a `keys` row invalidates it and cascades both its assignment and
device-binding history. The `users` row survives because user identity is not owned by
one key. The backend never moves a key from one user to another.

## Temporary device binding

Account ownership and device binding are different lifecycles:

```text
user --permanent--> access key --temporary, one active device--> device
```

When `DeviceBinding:Enabled=true`, the backend receives a stable app-generated
`X-Device-ID`, computes `HMAC-SHA256(DeviceBinding:HmacSecret, device-id)`, and
stores only the resulting fingerprint. Raw device IDs are never persisted or logged.
This is a stable application identifier, not hardware attestation.

Optional metadata headers are `X-Device-Name`, `X-Device-Platform`, and
`X-Device-OS-Version`. `X-Client-Version` is the authoritative frontend version and
also populates stored device `app_version`; clients do not send a second app-version
header. These values update the active binding when the same device logs in again. A
first successful `/api/v1/User/login` binds the
already successful account claim and device in one PostgreSQL transaction. Repeating
the login on the same device is idempotent. A different active device gets
`DEVICE_BINDING_MISMATCH`; a different LEB2 account still gets the existing permanent
ownership error. Reinstall and APK update can reuse the binding if the app preserves
the stable device ID.

`POST /api/v1/User/logout` requires the assigned access key and, when enforcement is
enabled, a valid `X-Device-ID`. A matching active device marks only that binding
unbound; it never removes `user_keys`, `users`, or the account relationship. If no
active binding remains, the same valid request returns `204` without recreating a
binding. A different active device receives `403 DEVICE_BINDING_MISMATCH` and cannot
release the active device. A later device may then bind the still-owned key. An
operator reset uses the same unbind operation with reason `operator-reset`.

`DeviceBinding:EnforcementEnabled` controls request rejection separately from
`DeviceBinding:Enabled`. During rollout, enable persistence first while enforcement
is off. When enforcement is on, login may bind an unbound provisioned key, while
cookie, logout, semester, class, and activity routes require the active matching
device.

Manual schema prerequisite (no migrations run by the application): apply the
current-schema migration in [the API reference](api-reference.md#supabase-schema-prerequisite)
or [the Cloud Run deployment guide](cloud-run-continuous-deployment.md#one-time-supabase-schema).
The resulting definitions must be:

```sql
CONSTRAINT fk_user_keys_key
FOREIGN KEY (key_id)
REFERENCES public.keys(id)
ON DELETE CASCADE

CONSTRAINT fk_key_device_bindings_key
FOREIGN KEY (key_id)
REFERENCES public.keys(id)
ON DELETE CASCADE

CONSTRAINT uq_user_keys_key UNIQUE (key_id)

CREATE UNIQUE INDEX uq_key_device_bindings_active_key
ON public.key_device_bindings (key_id)
WHERE unbound_at IS NULL;
```

The migration also creates `uq_users_leb2_user_id` and
`ix_key_device_bindings_key_device_hash`. Verify both key foreign keys report
`ON DELETE CASCADE` before enabling enforcement. The repository locks the key row and the active binding in the same transaction;
the partial unique index is the database backstop that permits at most one active
device per key under concurrent requests.

## Frontend client compatibility

`X-Client-Version` identifies the frontend build, not the API contract. The anonymous
`GET /api/v1/meta` response is:

```json
{
  "apiVersion": 1,
  "minimumClientVersion": "0.5.0",
  "latestClientVersion": "0.5.0",
  "downloadUrl": "https://github.com/oangsa/leb2-watch/releases/latest"
}
```

With `ClientCompatibility:EnforcementEnabled=true`, versions are parsed and compared
as semantic versions. Minimum/latest versions and `DownloadUrl` are validated once at
startup; invalid server configuration prevents startup. A supported-v1 client below
`minimumClientVersion` receives `426 CLIENT_UPDATE_REQUIRED`; a version newer than
`latestClientVersion` is allowed. Missing or blank values receive
`400 CLIENT_VERSION_REQUIRED`; multiple or malformed values receive
`400 CLIENT_VERSION_INVALID`. Rejection occurs before access-key,
device, LEB2, Selenium, or service work. `/api/v1/meta`, `/api/v1/health/leb2`, and
temporary anonymous aliases remain exempt.

## Supabase connection

The backend uses direct Npgsql access through `ConnectionStrings:Production` in every
environment. It never creates tables or runs migrations. Keep the local connection
string in user secrets and the production value in Secret Manager; never commit
either value. The existing `users` table must include nullable `leb2_user_id` and
its unique non-null index before deploying this version. If the column is missing,
access-key persistence fails closed through the existing database error contract;
the application does not attempt to repair the schema.

Before merging, manually apply and verify this prerequisite in Supabase. Merge
deploys Cloud Run, so the schema must be ready first:

```sql
ALTER TABLE users
ADD COLUMN leb2_user_id INTEGER;

CREATE UNIQUE INDEX uq_users_leb2_user_id
ON users (leb2_user_id)
WHERE leb2_user_id IS NOT NULL;
```

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

- `GET /api/v1/Semester`
- `GET /api/v1/Class/{id}`
- `GET /api/v1/Activity/{semesterId}/{classId}`
- `GET /api/v1/Activity/{semesterId}`
- `GET /api/v1/Activity/{semesterId}/snapshot`

All activity routes also require a positive integer user ID in:

```http
X-LEB2-USER-ID: <user-id>
```

`X-LEB2-USER-ID` remains for compatibility, but it is only a client-supplied
assertion. It must equal `users.leb2_user_id` for the activated access-key. The
opaque `Authorization` value remains a separate LEB2 session credential and is not
parsed to derive identity.

`POST /api/v1/User/login` requires only a provisioned access key because it performs the
enrollment transaction. `POST /api/v1/User/cookie` requires an assigned key with an
initialized `users.leb2_user_id`; legacy assigned users must complete one successful
`/api/v1/User/login` first. It does not change local registration. LEB2 credentials are used
only for the outbound call in that request and are not persisted.

## Error contract

Errors use a JSON `responseCode` so clients can distinguish authentication from LEB2
or scraper failures.

| HTTP status | `responseCode` | Meaning |
| --- | --- | --- |
| `400` | `INVALID_REQUEST` | Request input failed validation. |
| `400` | `DEVICE_ID_REQUIRED` | Device binding enforcement requires `X-Device-ID`. |
| `400` | `DEVICE_ID_INVALID` | A device identifier or metadata value is invalid. |
| `400` | `CLIENT_VERSION_REQUIRED` | Client compatibility enforcement requires `X-Client-Version`. |
| `400` | `CLIENT_VERSION_INVALID` | `X-Client-Version` has multiple values or is not a semantic version. |
| `401` | `ACCESS_KEY_REQUIRED` | The `access-key` header is absent. |
| `401` | `ACCESS_KEY_INVALID` | The access key is malformed or not provisioned. |
| `401` | `AUTHENTICATION_REQUIRED` | Bearer header is absent or malformed. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. Discard the client-held cookie and reauthenticate. |
| `403` | `ACCESS_KEY_NOT_ACTIVATED` | The key has not been claimed through `/api/v1/User/login`. |
| `403` | `ACCESS_KEY_ALREADY_ASSIGNED` | The key is assigned to another account. |
| `403` | `ACCESS_KEY_IDENTITY_MISMATCH` | The key cannot be used with the submitted student or LEB2 user ID. |
| `403` | `ACCESS_KEY_REAUTHENTICATION_REQUIRED` | The local user needs one successful `/api/v1/User/login` to initialize its LEB2 identity. |
| `403` | `DEVICE_BINDING_REQUIRED` | The protected route has no active device binding. |
| `403` | `DEVICE_BINDING_MISMATCH` | The supplied device is not the key's active device. |
| `409` | `ACCESS_KEY_IDENTITY_CONFLICT` | Successful LEB2 authentication conflicts with an established local identity. |
| `404` | `RESOURCE_NOT_FOUND` | The requested resource or class/semester relationship was not found. |
| `408` | `LEB2_UNAVAILABLE` | The request timed out. |
| `426` | `CLIENT_UPDATE_REQUIRED` | The supported v1 client is below the configured minimum version. |
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
- applies exponential backoff per endpoint for non-session requests;
- keys session-request backoff by endpoint and opaque session fingerprint, so one
  session cannot pause another session;
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

## Selenium rendering and failure classification

Authenticated semester and class discovery uses headless Selenium after successful
navigation to LEB2. The semester scraper polls the actual usable semester-link
condition while the SPA renders and checks for session redirect/expiration during
that wait. It does not use fixed `Thread.Sleep` calls.

`Scraping:SemesterRenderTimeoutSeconds` controls only the semester semantic-render
wait. It defaults to 30 seconds and is validated between 1 and 60 seconds. The
environment-variable form is:

```text
Scraping__SemesterRenderTimeoutSeconds=30
```

Failure classification is stage-aware:

- Chrome/ChromeDriver startup, browser crash, CDP/configuration, and invalid driver
  state become `BrowserAutomationException`; they do not create LEB2 backoff.
- Navigation, page-load, and network failures attributable to reaching LEB2 become
  `TransientLeb2Exception`; they create backoff in the request's existing scope.
- An explicit semantic DOM wait timeout becomes `StructuralParseException`; it
  returns `502 SCRAPE_RESPONSE_CHANGED` and participates in structural-failure
  correlation.
- `SessionExpiredException` remains `401 SESSION_EXPIRED` and clears matching
  failure state without creating backoff.

## Aggregate activities

`GET /api/v1/Activity/{semesterId}` discovers the semester's classes once, de-duplicates
their positive class IDs, and loads activities with maximum parallelism two. It
returns a flat activity list ordered by class ID while preserving LEB2's order within
each class. An empty list is returned when the semester contains no published
classes.

`GET /api/v1/Activity/{semesterId}/{classId}` first discovers classes for the supplied
semester through the existing structural scrape cache, then verifies that the class
ID belongs to that semester before retrieving activities. A missing relationship
returns `404 RESOURCE_NOT_FOUND`; class discovery failures use the existing error
contract. The activity repository is not called when membership validation fails.

`GET /api/v1/Activity/{semesterId}/snapshot` uses the same class discovery and activity
retrieval path as the flat semester route. It returns the semester ID and an
ID-ordered class list containing each class name and activities. Classes with no
activities remain in the successful response, and LEB2's ordering is preserved
inside each class.

All activity routes require an assigned `access-key`, integer route values,
`Authorization: Bearer <session-cookie-value>`, and
`X-LEB2-USER-ID`. The aggregate request is intentionally fail-fast. If class
discovery or any activity request fails, remaining queued work is canceled and the
request returns the existing error contract. It never returns a successful partial
list.

## Structural scrape cache

Rendered semester and class results are cached in memory for 60 seconds by default.
Semester entries retain each internal LEB2 ID and rendered display name. Cache entries
are partitioned by the opaque session fingerprint, and class entries also include the
semester ID. Successful empty class lists are cached; null results, failures,
cancellations, credentials, and cookies are not.

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

`GET /api/v1/health/leb2` is unauthenticated and always returns HTTP `200` with
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

## Rollout order

1. Apply and verify the current-schema key-revocation migration manually in Supabase.
2. Configure `DeviceBinding:HmacSecret` without enabling enforcement.
3. Deploy with device enforcement and client compatibility enforcement off and
   `ApiVersioning:LegacyRoutesEnabled=true`.
4. Release the frontend using `/api/v1`, `X-Device-ID`, `X-Client-Version`,
   `/api/v1/meta`, and `/api/v1/User/logout`.
5. Verify the new client, then enable client-version enforcement.
6. Enable device-binding enforcement.
7. Migrate monitoring to `/api/v1/health/leb2`, then set
   `ApiVersioning:LegacyRoutesEnabled=false` after the intended migration period.

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
