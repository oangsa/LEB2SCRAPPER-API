# API Reference

This document describes the HTTP contract implemented by the current application.
Examples use fake identifiers and placeholder credentials.

## Base URL

Local development:

```text
http://localhost:5015
```

Cloud Run:

```text
https://<cloud-run-service-url>
```

Production Cloud Run deployment runs at most one active application instance.
Caches, throttling, backoff, incident correlation, and health state are process-local;
horizontal scaling requires distributed coordination.

All request and response bodies use JSON unless stated otherwise.

## API versioning and migration

Current API version: `v1`. Canonical prefix: `/api/v1`.

ASP.NET Core URL-segment API versioning serves every controller as API `v1` by
default. `ActivityController` additionally serves API `v2`, at `/api/v2`. Each
version has its own generated Swagger document (`v1`, `v2`); every other
controller remains `v1`-only, so requesting `/api/v2/<OtherController>` returns an
ordinary `404 Not Found`.

During migration, `ApiVersioning:LegacyRoutesEnabled` (environment form
`ApiVersioning__LegacyRoutesEnabled`) defaults to `true`. While enabled, the old
unversioned paths remain temporary deprecated compatibility aliases. They resolve to
API version `1.0` (the configured default) and execute the same `v1` controller
actions and authorization chain as their `/api/v1` counterparts; they are not a
second API version and do not redirect requests. Set the flag to `false` after
compatible clients are deployed to make the old paths return ordinary `404 Not
Found` responses. Legacy aliases are hidden from both Swagger contracts.

### Why `/api/v2/Activity/...` exists

LEB2 activity timestamps (`due_date`, `start_date`, `created_at`) are documented
upstream as GMT+7 with no explicit offset. The original implementation stored and
compared these as naive `DateTime` values using the host's local clock, so
pending/overdue status depended on the server's system timezone, and the public
`dueDate`/`startDate`/`createdAt` strings carried the original GMT+7 wall-clock
numbers with no offset marker. Correcting that is an incompatible response-shape
change under the breaking-changes rule below, so it shipped as `/api/v2` instead of
silently changing `v1`:

- `/api/v1/Activity/...` (and its unversioned legacy aliases) keep returning the
  original, pre-fix shape: `startDate`, `dueDate`, `createdAt`,
  `lastDueDateNotificationDate`, and `lastStatusChangeNotificationDate` serialize as
  naive `yyyy-MM-ddTHH:mm:ss` strings with no offset, holding the original GMT+7
  wall-clock numbers. Already-deployed clients that assume GMT+7 keep working
  unchanged.
- `/api/v2/Activity/...` returns the same activities with those same fields as
  genuine UTC instants (`yyyy-MM-ddTHH:mm:ss.fffZ`). New clients should use `v2` and
  convert to the user's local timezone for display.

Internally, `Activity.DueDate`/`StartDate`/`CreatedAt` are always normalized to UTC
by `Leb2DateTimeParser` (`LEB2SCRAPPER.Infrastructure/HttpService`), regardless of
which API version served the request; only the `v1` controller actions re-shape the
outbound response back to the legacy format, via
`LEB2SCRAPPER.Presentation.Legacy.ActivityLegacyExtensions`. Pending/overdue status
logic (`ActivityExtension.GetStatusText`) always compares real UTC instants and is
unaffected by API version or host timezone.

A future breaking HTTP-contract change must use a new version rather than silently
changing an existing one. Breaking changes include incompatible response shapes,
removed required fields, changed authentication semantics, changed route meaning, or
incompatible required request data. Non-breaking additions may remain within the
existing version where appropriate.

## Endpoint summary

| Method | Path | Access key | LEB2 session required | Request body |
| --- | --- | --- | --- | --- |
| `POST` | `/api/v1/User/login` | Provisioned | No | Credentials |
| `POST` | `/api/v1/User/cookie` | Activated | No | Credentials |
| `POST` | `/api/v1/User/logout` | Activated | No | None |
| `GET` | `/api/v1/Semester` | Activated | Yes | None |
| `GET` | `/api/v1/Class/{id}` | Activated | Yes | None |
| `GET` | `/api/v1/Activity/{semesterId}/{classId}` | Activated | Yes | None |
| `GET` | `/api/v2/Activity/{semesterId}/{classId}` | Activated | Yes | None |
| `GET` | `/api/v1/Activity/{semesterId}` | Activated | Yes | None |
| `GET` | `/api/v2/Activity/{semesterId}` | Activated | Yes | None |
| `GET` | `/api/v1/Activity/{semesterId}/snapshot` | Activated | Yes | None |
| `GET` | `/api/v2/Activity/{semesterId}/snapshot` | Activated | Yes | None |
| `GET` | `/api/v1/meta` | Anonymous | No | None |
| `GET` | `/api/v1/health/leb2` | Anonymous | No | None |

## Authentication

Every route except `GET /api/v1/meta` and `GET /api/v1/health/leb2` requires the
application access key:

```http
access-key: <uuid-from-keys.id>
```

The UUID is the `keys.id` value manually provisioned in Supabase. It is a secret
per-user API credential, not a JWT. An activated key is bound to one local student
through `keys.id`, `user_keys`, `users.student_id`, and `users.leb2_user_id`.
It cannot be used to log in as another student, obtain that student's LEB2 session,
or request activities with that student's numeric LEB2 ID. Do not put it in a URL,
query string, request body, or log.
`POST /api/v1/User/login` accepts a provisioned but unassigned key. `/api/v1/User/cookie` and
all data routes require an assigned key.

Protected data endpoints also require the complete client-held LEB2 session cookie:

```http
Authorization: Bearer <leb2-session-cookie>
```

The value is an opaque LEB2 cookie, not a JWT. The application authentication handler
only checks that a credential was supplied; it does not issue or locally verify an
access token. Actual session validity comes from LEB2 responses. Legacy clients may
send the cookie directly without the `Bearer` prefix, but new clients should use the
Bearer form.

Every activity endpoint also requires:

```http
X-LEB2-USER-ID: <positive-integer-user-id>
```

This header remains for compatibility. It is a client-supplied assertion and must
match the numeric LEB2 identity stored for the access-key owner. The opaque
`Authorization` session value is separate and is never parsed to derive identity.

The `access-key` header name is case-insensitive. Its value must be one UUID from
`keys.id`; send exactly one value. The API never accepts the key in a URL, query
string, or normal request body.

### Access-key enrollment

The owner provisions keys directly in Supabase. The API has no key-generation or
key-management endpoint.

### Supabase schema prerequisite

The application does not run migrations. Apply this manual migration to the supplied
current production schema before enabling device enforcement:

```sql
BEGIN;

-- Preserve permanent user records, but make key-owned assignments disappear
-- automatically when an operator revokes a key.

ALTER TABLE public.user_keys
DROP CONSTRAINT fk_user_keys_key;

ALTER TABLE public.user_keys
ADD CONSTRAINT fk_user_keys_key
FOREIGN KEY (key_id)
REFERENCES public.keys(id)
ON DELETE CASCADE;


ALTER TABLE public.key_device_bindings
DROP CONSTRAINT fk_key_device_bindings_key;

ALTER TABLE public.key_device_bindings
ADD CONSTRAINT fk_key_device_bindings_key
FOREIGN KEY (key_id)
REFERENCES public.keys(id)
ON DELETE CASCADE;


-- Device binding invariant:
-- one access key may have at most one active device.

CREATE UNIQUE INDEX IF NOT EXISTS uq_key_device_bindings_active_key
ON public.key_device_bindings (key_id)
WHERE unbound_at IS NULL;


-- Useful lookup for logout/binding checks.

CREATE INDEX IF NOT EXISTS ix_key_device_bindings_key_device_hash
ON public.key_device_bindings (key_id, device_id_hash);


-- Existing access-key identity invariant.
-- Keep/create this if production does not already have it.

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_leb2_user_id
ON public.users (leb2_user_id)
WHERE leb2_user_id IS NOT NULL;


-- Normalize the existing user_keys(key_id) unique constraint name without
-- creating a redundant second unique constraint.

DO $$
DECLARE
    existing_constraint text;
BEGIN
    SELECT c.conname
    INTO existing_constraint
    FROM pg_constraint c
    WHERE c.conrelid = 'public.user_keys'::regclass
      AND c.contype = 'u'
      AND pg_get_constraintdef(c.oid) = 'UNIQUE (key_id)'
    LIMIT 1;

    IF existing_constraint IS NULL THEN
        ALTER TABLE public.user_keys
        ADD CONSTRAINT uq_user_keys_key UNIQUE (key_id);
    ELSIF existing_constraint <> 'uq_user_keys_key' THEN
        EXECUTE format(
            'ALTER TABLE public.user_keys RENAME CONSTRAINT %I TO uq_user_keys_key',
            existing_constraint
        );
    END IF;
END
$$;

COMMIT;
```

The migration intentionally does not add `ON DELETE CASCADE` from `users` to
`user_keys`. To reset a device without changing account ownership, an operator can
run:

```sql
UPDATE key_device_bindings
SET unbound_at = CURRENT_TIMESTAMP,
    unbound_reason = 'operator-reset',
    updated_by = 'operator',
    updated_at = CURRENT_TIMESTAMP
WHERE key_id = '00000000-0000-4000-8000-000000000001'
  AND unbound_at IS NULL;
```

In production, `user_keys` ownership uniqueness is enforced by constraint
`uq_user_keys_key`. Apply and verify the schema first, then merge; the main deployment
deploys Cloud Run. This prerequisite adds no HTTP/API behavior, and the constraint name
is not exposed to API clients.

After the migration, revoke a key with one statement. PostgreSQL removes its
`user_keys` assignment and all `key_device_bindings` history while preserving the
`users` row:

```sql
DELETE FROM public.keys
WHERE id = '<key-id>';
```

Verify the production objects before enabling enforcement:

```sql
SELECT
    conname,
    pg_get_constraintdef(oid)
FROM pg_constraint
WHERE conrelid IN (
    'public.user_keys'::regclass,
    'public.key_device_bindings'::regclass
)
ORDER BY conrelid::regclass::text, conname;
```

```sql
SELECT
    indexname,
    indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename IN (
      'users',
      'user_keys',
      'key_device_bindings'
  )
ORDER BY tablename, indexname;
```

The result must include `uq_user_keys_key`, `uq_users_leb2_user_id`,
`uq_key_device_bindings_active_key`, and both key foreign keys with `ON DELETE CASCADE`.

Example manual provisioning with a fake UUID:

```sql
INSERT INTO keys (id, created_by, updated_by)
VALUES (
    '00000000-0000-4000-8000-000000000001',
    'admin',
    'admin'
);
```

Use the key once with `/api/v1/User/login`. After successful LEB2 authentication, the API:

1. Normalizes the login username into `users.student_id`.
2. Persists authoritative LEB2 `User.Id` as `users.leb2_user_id`.
3. Builds `users.name` from the English LEB2 name, falling back to Thai fields.
4. Upserts the user by `student_id` and claims the key in the same PostgreSQL transaction.

An invalid LEB2 login creates neither the user nor the assignment. A key already
assigned to another student is never transferred automatically. Existing users
with null `leb2_user_id` are initialized by their next successful `/api/v1/User/login`;
activity requests fail closed until that happens.

| Key state | `/api/v1/User/login` | `/api/v1/User/cookie` and data routes |
| --- | --- | --- |
| Missing from `keys` | Rejected | Rejected |
| Provisioned, unassigned | Allowed | Rejected with `ACCESS_KEY_NOT_ACTIVATED` |
| Assigned in `user_keys` | Allowed, idempotent for the owner | Allowed when `leb2_user_id` is initialized |

To revoke access, delete the `keys` row; the production foreign keys cascade its
assignment and device-binding history. To make a key claimable again without
revoking the key, delete its `user_keys` row.
Neither operation is exposed as an API route.

## Shared request model

### Credentials

`username` and `password` are required strings. `remember` is optional and defaults
to `false`.

```json
{
  "username": "fake.student",
  "password": "fake-password",
  "remember": false
}
```

The backend uses credentials only for the current outbound LEB2 request and does not
persist them.

The successful `/api/v1/User/login` response remains the existing LEB2 user profile shape;
it does not return the access key or local database identifiers.

## Device binding and client compatibility

When `DeviceBinding:Enabled=true`, the backend HMACs the trimmed `X-Device-ID`
header with the configured `DeviceBinding:HmacSecret` using HMAC-SHA256 before it is
stored. Raw device identifiers never enter PostgreSQL logs or response bodies. The
identifier is a stable app-generated value: it supports reinstall/update continuity
when the frontend preserves it, but it is not hardware attestation.

Protected access-key routes accept these optional device metadata headers:

```http
X-Device-ID: <stable-app-device-id>
X-Device-Name: <display-name>
X-Device-Platform: android
X-Device-OS-Version: <os-version>
```

`X-Client-Version` is the authoritative frontend application version. When device
binding is persisted, it also populates the binding's `app_version`; clients do not
send a second app-version header.

With `DeviceBinding:EnforcementEnabled=true`, `X-Device-ID` is required and must
match the one active binding for the key. First successful login binds the account
and device in one PostgreSQL transaction. Repeating login on the same device is
idempotent and refreshes metadata. A different active device is rejected. A key
cannot move to a different LEB2 account. `POST /api/v1/User/logout` unbinds only the
matching temporary device relationship; it never deletes `user_keys` ownership.
After logout, a new device may bind the still-owned key. Reinstall and APK update
reuse the binding when they reuse the stable device ID.

Device enforcement is separate from client-version enforcement. The latter uses:

```http
X-Client-Version: 0.5.2
```

`ClientCompatibility:EnforcementEnabled=true` rejects a semantically older supported
v1 client with `426 CLIENT_UPDATE_REQUIRED`. A newer version than
`LatestClientVersion` is not rejected merely for being newer. Missing or blank client
versions receive `400 CLIENT_VERSION_REQUIRED`; multiple or malformed values receive
`400 CLIENT_VERSION_INVALID`. `/api/v1/meta` and `/api/v1/health/leb2` remain
anonymous and exempt so clients can bootstrap and monitor during rollout.

The `426` body uses the normal error model:

```json
{
  "message": "This client version is no longer supported. Update to 0.5.0.",
  "responseCode": "CLIENT_UPDATE_REQUIRED",
  "details": null,
  "timestamp": "2026-07-24T12:00:00Z",
  "traceId": "example-trace-id"
}
```

The route/header contract is:

| Route | API version | Access key | Device ID | Client version | LEB2 session | User ID |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /api/v1/meta` | v1 | No | No | No | No | No |
| `GET /api/v1/health/leb2` | v1 | No | No | No | No | No |
| `POST /api/v1/User/login` | v1 | Provisioned | Yes* | Yes* | No | No |
| `POST /api/v1/User/cookie` | v1 | Activated | Yes* | Yes* | No | No |
| `POST /api/v1/User/logout` | v1 | Activated, allow already-unbound | Yes* | Yes* | No | No |
| `GET /api/v1/Semester` | v1 | Activated | Yes* | Yes* | Yes | No |
| `GET /api/v1/Class/{id}` | v1 | Activated | Yes* | Yes* | Yes | No |
| `GET /api/v1/Activity/...` | v1 | Activated | Yes* | Yes* | Yes | Yes |

`*` means the header is optional while the corresponding rollout enforcement flag is
off and mandatory after enforcement is enabled.

## Shared response models

### Activity

Activity endpoints use the following object. Nullable fields can contain `null`.
`v1` and `v2` return identical fields; they differ only in how `startDate`,
`dueDate`, `createdAt`, `lastDueDateNotificationDate`, and
`lastStatusChangeNotificationDate` are serialized — see
[Why `/api/v2/Activity/...` exists](#why-apiv2activity-exists). The examples
below use the same underlying activity in both shapes.

`v1` (legacy, and all unversioned legacy-alias routes) — naive GMT+7 wall-clock
strings, no offset:

```json
{
  "id": 1001,
  "userId": 2001,
  "classId": 3001,
  "advStarred": 0,
  "groupType": "individual",
  "type": "ASM",
  "peerAssessment": 0,
  "isAllowRepeat": 0,
  "title": "Example assignment",
  "description": "<p>Example description</p>",
  "startDate": "2026-07-01T09:00:00",
  "dueDate": "2026-07-31T23:59:00",
  "editGroupMode": "",
  "createdAt": "2026-06-30T12:00:00",
  "user": 2001,
  "activitySubmissionId": null,
  "classUserId": 4001,
  "activityGroupId": null,
  "activityGroupName": null,
  "activitySubmissionSubmittedAt": null,
  "dueDateExceed": false,
  "quizSubmissionIsSubmitted": false,
  "countGroupMember": 1,
  "activitySubmissionIsLate": false,
  "fileActivities": [],
  "questions": [],
  "submissions": [],
  "lastDueDateNotificationDate": null,
  "lastStatusChangeNotificationDate": null,
  "previousSubmissionStatus": null
}
```

`v2` — genuine UTC instants:

```json
{
  "id": 1001,
  "userId": 2001,
  "classId": 3001,
  "advStarred": 0,
  "groupType": "individual",
  "type": "ASM",
  "peerAssessment": 0,
  "isAllowRepeat": 0,
  "title": "Example assignment",
  "description": "<p>Example description</p>",
  "startDate": "2026-07-01T02:00:00.000Z",
  "dueDate": "2026-07-31T16:59:00.000Z",
  "editGroupMode": "",
  "createdAt": "2026-06-30T05:00:00.000Z",
  "user": 2001,
  "activitySubmissionId": null,
  "classUserId": 4001,
  "activityGroupId": null,
  "activityGroupName": null,
  "activitySubmissionSubmittedAt": null,
  "dueDateExceed": false,
  "quizSubmissionIsSubmitted": false,
  "countGroupMember": 1,
  "activitySubmissionIsLate": false,
  "fileActivities": [],
  "questions": [],
  "submissions": [],
  "lastDueDateNotificationDate": null,
  "lastStatusChangeNotificationDate": null,
  "previousSubmissionStatus": null
}
```

When present, `activitySubmissionSubmittedAt` has this shape:

```json
{
  "date": "2026-07-20 14:30:00",
  "timezoneType": 3,
  "timezone": "Asia/Bangkok"
}
```

`fileActivities` and `submissions` contain upstream-defined JSON objects. Their
internal fields are not fixed by this API. `questions` contains integer question
IDs.

### Standard error

Operational, authentication, and upstream errors use:

```json
{
  "message": "LEB2 is temporarily unavailable.",
  "responseCode": "LEB2_UNAVAILABLE",
  "details": null,
  "timestamp": "2026-07-24T12:00:00Z",
  "traceId": "example-trace-id"
}
```

Possible error codes:

| HTTP status | `responseCode` | Meaning |
| --- | --- | --- |
| `400` | `INVALID_REQUEST` | An argument or operation was invalid. |
| `400` | `DEVICE_ID_REQUIRED` | Device binding enforcement requires `X-Device-ID`. |
| `400` | `DEVICE_ID_INVALID` | A device identifier or device metadata header is invalid. |
| `400` | `CLIENT_VERSION_REQUIRED` | Client compatibility enforcement requires `X-Client-Version`. |
| `400` | `CLIENT_VERSION_INVALID` | `X-Client-Version` has multiple values or is not a semantic version. |
| `401` | `ACCESS_KEY_REQUIRED` | The `access-key` header was absent. |
| `401` | `ACCESS_KEY_INVALID` | The access key was malformed or is not provisioned. |
| `401` | `AUTHENTICATION_REQUIRED` | A required LEB2 session header was absent or empty. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. |
| `403` | `ACCESS_KEY_NOT_ACTIVATED` | The key must first be claimed by a successful `/api/v1/User/login`. |
| `403` | `ACCESS_KEY_ALREADY_ASSIGNED` | The key belongs to another account. |
| `403` | `ACCESS_KEY_IDENTITY_MISMATCH` | The key cannot be used with the submitted student or LEB2 user ID. |
| `403` | `ACCESS_KEY_REAUTHENTICATION_REQUIRED` | The local user must complete `/api/v1/User/login` again to initialize identity. |
| `403` | `DEVICE_BINDING_REQUIRED` | The key has no active device binding and the protected route requires one. |
| `403` | `DEVICE_BINDING_MISMATCH` | The supplied device is not the key's active device. |
| `409` | `ACCESS_KEY_IDENTITY_CONFLICT` | Successful LEB2 identity conflicts with an established local identity. |
| `404` | `RESOURCE_NOT_FOUND` | The requested user, resource, or class/semester relationship was not found. |
| `408` | `LEB2_UNAVAILABLE` | The request timed out. |
| `426` | `CLIENT_UPDATE_REQUIRED` | The supported v1 client is older than `ClientCompatibility:MinimumClientVersion`. |
| `429` | `CLIENT_THROTTLE_ACTIVE` | The client has too many active or queued LEB2 requests. |
| `500` | `UNEXPECTED_ERROR` | An unexpected server error occurred. |
| `502` | `LEB2_UNAVAILABLE` | LEB2 rejected or could not complete the request. |
| `502` | `SCRAPE_RESPONSE_CHANGED` | LEB2 returned an unexpected HTML or JSON structure. |
| `503` | `LEB2_UNAVAILABLE` | A transient LEB2 failure or local browser-automation failure prevented completion. |
| `503` | `REQUEST_BACKOFF_ACTIVE` | This operation's backoff scope is temporarily paused after a recent upstream failure. |
| `503` | `ACCESS_KEY_STORE_UNAVAILABLE` | Supabase access-key validation is temporarily unavailable. |

Responses with `CLIENT_THROTTLE_ACTIVE` or `REQUEST_BACKOFF_ACTIVE` include a
`Retry-After` response header. LEB2 session failures include
`WWW-Authenticate: Bearer`; access-key failures use the `ACCESS_KEY_*` response
codes and do not describe the access key as a bearer JWT.

### LEB2 scraping and backoff behavior

Backoff protects genuine upstream LEB2 failures without allowing one user's
session to pause another user's session:

- For a request using a session credential, backoff is keyed by the fixed LEB2
  endpoint and an opaque server-side fingerprint of that session. A failure for
  one session does not block another session on the same application instance.
- For a request that does not use a session credential, backoff is keyed by the
  endpoint only.
- A successful request clears only its matching backoff state. Session expiration
  returns `401 SESSION_EXPIRED` and does not create upstream backoff.
- Client fingerprints, cookies, access keys, and credentials are never included
  in API responses or health output.

For `/api/v1/Semester`, the scraper waits up to the bounded configured semester
render timeout for at least one usable semester link after navigation and session
validation. The setting is `Scraping:SemesterRenderTimeoutSeconds`
(`Scraping__SemesterRenderTimeoutSeconds` as an environment variable), defaulting
to 30 seconds and bounded to 1–60 seconds. This setting applies only to the
semantic semester-link render wait; it does not change unrelated WebDriver
command, page-load, or class-render timeouts.

If that semantic DOM condition expires, the API returns
`502 SCRAPE_RESPONSE_CHANGED` and retains structural-failure tracking and alert
threshold behavior. A navigation, page-load, or network failure while reaching
LEB2 returns `503 LEB2_UNAVAILABLE` and activates the matching backoff scope.
ChromeDriver/Chromium startup, crash, CDP/configuration, and invalid-driver-state
failures return `503 LEB2_UNAVAILABLE` without activating upstream backoff.
Session expiration remains `401 SESSION_EXPIRED` and does not create backoff.

### Validation error

Request-model, route-value, and header validation errors use:

```json
{
  "statusCode": 400,
  "message": "Validation failed.",
  "responseCode": "INVALID_REQUEST",
  "timestamp": "2026-07-24T12:00:00Z",
  "traceId": "example-trace-id",
  "validationErrors": {
    "semesterId": [
      "The field semesterId must be between 1 and 2147483647."
    ]
  }
}
```

The keys and messages inside `validationErrors` depend on which input failed.

## Endpoints

### POST `/api/v1/User/login`

Authenticates directly against the LEB2 login API and maps the successful result to
a user profile.

Authentication: `access-key` header with a provisioned UUID. An unassigned key is
allowed here so this successful LEB2 login can claim it.

Required header:

```http
access-key: 00000000-0000-4000-8000-000000000001
```

Request body:

```json
{
  "username": "fake.student",
  "password": "fake-password",
  "remember": false
}
```

Successful response — `200 OK`:

```json
{
  "id": 2001,
  "kmuttId": "60000000",
  "nameThai": "ชื่อ",
  "nameEnglish": "Example",
  "surnameThai": "นามสกุล",
  "surnameEnglish": "Student"
}
```

Relevant error responses:

- `400 INVALID_REQUEST` for an invalid request body or argument.
- `401 ACCESS_KEY_REQUIRED` or `401 ACCESS_KEY_INVALID` when the access key is absent, malformed, or unknown.
- `403 ACCESS_KEY_ALREADY_ASSIGNED` when another account already owns the key.
- `403 ACCESS_KEY_IDENTITY_MISMATCH` when an assigned key is used with another username.
- `409 ACCESS_KEY_IDENTITY_CONFLICT` when successful LEB2 `User.Id` conflicts with the established local identity.
- `404 RESOURCE_NOT_FOUND` when the credentials are rejected or no user is found.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` when LEB2 rejects or cannot complete the upstream request.
- `502 SCRAPE_RESPONSE_CHANGED` for an unexpected successful LEB2 response shape.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `503 ACCESS_KEY_STORE_UNAVAILABLE` when Supabase access-key persistence is temporarily unavailable.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### POST `/api/v1/User/logout`

Ends the temporary device binding for the supplied access key. It does not remove
the key's permanent `user_keys` account ownership and does not contact LEB2.

Authentication: `access-key` header with an assigned UUID. When device binding is
enabled, send the matching device headers:

```http
access-key: 00000000-0000-4000-8000-000000000001
X-Device-ID: <stable-app-device-id>
X-Device-Name: Example phone
X-Device-Platform: android
X-Device-OS-Version: 14
X-Client-Version: 0.5.2
```

Request body: none.

Successful response: `204 No Content`.

After logout, the same key can bind on another device. Repeating the same logout
returns `204 No Content` when no active binding remains. A different active device
still receives `403 DEVICE_BINDING_MISMATCH`, and the active binding remains. Missing
device ID under enforcement and access-key failures use the standard error codes above.

### GET `/api/v1/meta`

Returns anonymous, cheap compatibility metadata. It does not access Supabase, LEB2,
Selenium, access-key state, device binding, or client-version enforcement.
The advertised versions and download URL are validated before application startup;
this endpoint does not expose an unvalidated compatibility configuration.

Request headers: none. Successful response — `200 OK`:

```json
{
  "apiVersion": 1,
  "minimumClientVersion": "0.5.0",
  "latestClientVersion": "0.5.0",
  "downloadUrl": "https://github.com/oangsa/leb2-watch/releases/latest"
}
```

`apiVersion` describes the URL contract and remains `1` for `/api/v1/meta`; it is
not the frontend build version.

### POST `/api/v1/User/cookie`

Signs in through Selenium and returns the complete LEB2 session cookie needed by
protected endpoints.

Authentication: `access-key` header with an already assigned UUID. The key must
first be claimed by a successful `/api/v1/User/login` and must have a stored
`users.leb2_user_id`. Legacy assigned users with a null value must complete
`/api/v1/User/login` again before requesting a cookie.

Required header:

```http
access-key: 00000000-0000-4000-8000-000000000001
```

Request body:

```json
{
  "username": "fake.student",
  "password": "fake-password",
  "remember": false
}
```

`remember` is accepted as part of the shared credentials model but is not used by the
Selenium login flow.

Successful response — `200 OK`:

```json
{
  "cookie": "session_cookie_name=fake-session-value; another_cookie=fake-value"
}
```

Treat the returned value as a secret. Send the entire string on later requests:

```http
Authorization: Bearer session_cookie_name=fake-session-value; another_cookie=fake-value
```

Relevant error responses:

- `400 INVALID_REQUEST` for an invalid request body.
- `401 ACCESS_KEY_REQUIRED` or `401 ACCESS_KEY_INVALID` when the access key is absent, malformed, or unknown.
- `403 ACCESS_KEY_NOT_ACTIVATED` when `/api/v1/User/login` has not claimed the key yet.
- `403 ACCESS_KEY_IDENTITY_MISMATCH` when the credentials belong to another student.
- `403 ACCESS_KEY_REAUTHENTICATION_REQUIRED` when local identity data is incomplete.
- `404 RESOURCE_NOT_FOUND` if the login completes without a usable result.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` when LEB2 does not accept the credentials or cannot complete the login.
- `502 SCRAPE_RESPONSE_CHANGED` when the LEB2 login page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `503 ACCESS_KEY_STORE_UNAVAILABLE` when Supabase access-key validation is temporarily unavailable.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/api/v1/Semester`

Returns the semesters visible to the authenticated LEB2 session. `id` is the
internal LEB2 semester ID used with `/api/v1/Class/{id}` and activity routes. `name` is
the rendered visible semester label for display.

Required header:

```http
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
```

Request body: none.

Successful response — `200 OK`:

```json
[
  {
    "id": 46,
    "name": "1/2026"
  }
]
```

Missing or unrecognizable semester structure returns `502 SCRAPE_RESPONSE_CHANGED`.

Relevant error responses:

- `401 AUTHENTICATION_REQUIRED` when the session header is absent or empty.
- `401 ACCESS_KEY_REQUIRED` or `401 ACCESS_KEY_INVALID` when the access key is absent, malformed, or unknown.
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/api/v1/User/login`.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 SCRAPE_RESPONSE_CHANGED` when the rendered page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` for a transient LEB2 or local browser-automation failure.
- `503 REQUEST_BACKOFF_ACTIVE` when this session's `semesters` backoff is active.
- `503 ACCESS_KEY_STORE_UNAVAILABLE` when Supabase access-key validation is temporarily unavailable.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/api/v1/Class/{id}`

Returns classes for one semester.

Route parameter:

| Name | Type | Description |
| --- | --- | --- |
| `id` | integer | LEB2 semester ID; clients should send a positive value. |

Required header:

```http
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
```

Request body: none.

Example request:

```http
GET /api/v1/Class/101
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
```

Successful response — `200 OK`:

```json
[
  {
    "id": 3001,
    "name": "Example Course"
  },
  {
    "id": 3002,
    "name": "Another Course"
  }
]
```

If no classes are found, the response is:

```json
[]
```

Relevant error responses:

- `401 AUTHENTICATION_REQUIRED` when the session header is absent or empty.
- `401 ACCESS_KEY_REQUIRED` or `401 ACCESS_KEY_INVALID` when the access key is absent, malformed, or unknown.
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/api/v1/User/login`.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` for an invalid or unusable LEB2 class request.
- `502 SCRAPE_RESPONSE_CHANGED` when the rendered page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

The route requires an integer. A non-integer does not match the route and normally
produces the framework's `404` response. Unlike the activity routes, this route does
not currently apply range validation; a non-positive integer reaches the repository
and can produce `502 LEB2_UNAVAILABLE`.

### GET `/api/v1/Activity/{semesterId}/{classId}`

Legacy shape — see [`v2`](#get-apiv2activitysemesteridclassid) for the
timezone-corrected version and
[Why `/api/v2/Activity/...` exists](#why-apiv2activity-exists).

Returns activities for one class.

Route parameters:

| Name | Type | Validation | Description |
| --- | --- | --- | --- |
| `semesterId` | integer | `>= 1` | Semester route context. |
| `classId` | integer | `>= 1` | LEB2 class ID used for the activity lookup. |

Required headers:

```http
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /api/v1/Activity/101/3001
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Successful response — `200 OK`:

```json
[
  {
    "id": 1001,
    "userId": 2001,
    "classId": 3001,
    "advStarred": 0,
    "groupType": "individual",
    "type": "ASM",
    "peerAssessment": 0,
    "isAllowRepeat": 0,
    "title": "Example assignment",
    "description": "<p>Example description</p>",
    "startDate": "2026-07-01T09:00:00",
    "dueDate": "2026-07-31T23:59:00",
    "editGroupMode": "",
    "createdAt": "2026-06-30T12:00:00",
    "user": 2001,
    "activitySubmissionId": null,
    "classUserId": 4001,
    "activityGroupId": null,
    "activityGroupName": null,
    "activitySubmissionSubmittedAt": null,
    "dueDateExceed": false,
    "quizSubmissionIsSubmitted": false,
    "countGroupMember": 1,
    "activitySubmissionIsLate": false,
    "fileActivities": [],
    "questions": [],
    "submissions": [],
    "lastDueDateNotificationDate": null,
    "lastStatusChangeNotificationDate": null,
    "previousSubmissionStatus": null
  }
]
```

If no activities are found, the response is:

```json
[]
```

The implementation discovers classes for `semesterId` through the existing
60-second structural cache and verifies that `classId` belongs to that semester
before retrieving activities. If the relationship is missing, the response is
`404 RESOURCE_NOT_FOUND` and the activity lookup is not performed.

Relevant error responses:

- `400 INVALID_REQUEST` when an integer route value is less than one, or when
  `X-LEB2-USER-ID` is missing, non-integer, or less than one.
- `401 AUTHENTICATION_REQUIRED` when the session header is absent or empty.
- `401 ACCESS_KEY_REQUIRED` or `401 ACCESS_KEY_INVALID` when the access key is absent, malformed, or unknown.
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/api/v1/User/login`.
- `403 ACCESS_KEY_IDENTITY_MISMATCH` when `X-LEB2-USER-ID` does not match the
  LEB2 identity bound to the access key.
- `403 ACCESS_KEY_REAUTHENTICATION_REQUIRED` when the access-key owner's stored
  LEB2 identity has not been initialized.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `404 RESOURCE_NOT_FOUND` when the class does not belong to the supplied semester.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` or `502 SCRAPE_RESPONSE_CHANGED` for upstream failures.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

A non-integer route value does not match the route and normally produces the
framework's `404` response.

### GET `/api/v2/Activity/{semesterId}/{classId}`

Identical to [`GET /api/v1/Activity/{semesterId}/{classId}`](#get-apiv1activitysemesteridclassid)
in route parameters, headers, discovery behavior, and error responses. The only
difference is that `startDate`, `dueDate`, `createdAt`,
`lastDueDateNotificationDate`, and `lastStatusChangeNotificationDate` serialize as
genuine UTC instants instead of naive GMT+7 wall-clock strings — see the `v2`
[Activity](#activity) sample above.

Example request:

```http
GET /api/v2/Activity/101/3001
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

### GET `/api/v1/Activity/{semesterId}`

Legacy shape — see [`v2`](#get-apiv2activitysemesterid) for the
timezone-corrected version and
[Why `/api/v2/Activity/...` exists](#why-apiv2activity-exists).

Discovers every class in a semester, retrieves their activities with maximum
parallelism two, and returns one flat activity array. Classes are processed in class
ID order, while LEB2's activity order is preserved within each class.

Route parameter:

| Name | Type | Validation | Description |
| --- | --- | --- | --- |
| `semesterId` | integer | `>= 1` | LEB2 semester ID. |

Required headers:

```http
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /api/v1/Activity/101
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Successful response — `200 OK`:

```json
[
  {
    "id": 1001,
    "userId": 2001,
    "classId": 3001,
    "advStarred": 0,
    "groupType": "individual",
    "type": "ASM",
    "peerAssessment": 0,
    "isAllowRepeat": 0,
    "title": "Example assignment",
    "description": "<p>Example description</p>",
    "startDate": "2026-07-01T09:00:00",
    "dueDate": "2026-07-31T23:59:00",
    "editGroupMode": "",
    "createdAt": "2026-06-30T12:00:00",
    "user": 2001,
    "activitySubmissionId": null,
    "classUserId": 4001,
    "activityGroupId": null,
    "activityGroupName": null,
    "activitySubmissionSubmittedAt": null,
    "dueDateExceed": false,
    "quizSubmissionIsSubmitted": false,
    "countGroupMember": 1,
    "activitySubmissionIsLate": false,
    "fileActivities": [],
    "questions": [],
    "submissions": [],
    "lastDueDateNotificationDate": null,
    "lastStatusChangeNotificationDate": null,
    "previousSubmissionStatus": null
  }
]
```

If the semester has no published classes or activities, the response is:

```json
[]
```

The operation is fail-fast. It returns an error instead of a partial activity list
if class discovery or any class activity request fails.

Relevant error responses are the same as
`GET /api/v1/Activity/{semesterId}/{classId}`, except `404 RESOURCE_NOT_FOUND`: that
response applies only to the class-membership check performed when `classId` is
supplied.

### GET `/api/v2/Activity/{semesterId}`

Identical to [`GET /api/v1/Activity/{semesterId}`](#get-apiv1activitysemesterid) in
route parameters, headers, discovery behavior, and error responses. The only
difference is that `startDate`, `dueDate`, `createdAt`,
`lastDueDateNotificationDate`, and `lastStatusChangeNotificationDate` serialize as
genuine UTC instants instead of naive GMT+7 wall-clock strings — see the `v2`
[Activity](#activity) sample above.

Example request:

```http
GET /api/v2/Activity/101
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

### GET `/api/v1/Activity/{semesterId}/snapshot`

Legacy shape — see [`v2`](#get-apiv2activitysemesteridsnapshot) for the
timezone-corrected version and
[Why `/api/v2/Activity/...` exists](#why-apiv2activity-exists).

Returns the semester's classes and activities as a nested snapshot. Classes with no
activities remain in the response.

Route parameter:

| Name | Type | Validation | Description |
| --- | --- | --- | --- |
| `semesterId` | integer | `>= 1` | LEB2 semester ID. |

Required headers:

```http
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /api/v1/Activity/101/snapshot
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Successful response — `200 OK`:

```json
{
  "semesterId": 101,
  "classes": [
    {
      "id": 3001,
      "name": "Example Course",
      "activities": [
        {
          "id": 1001,
          "userId": 2001,
          "classId": 3001,
          "advStarred": 0,
          "groupType": "individual",
          "type": "ASM",
          "peerAssessment": 0,
          "isAllowRepeat": 0,
          "title": "Example assignment",
          "description": "<p>Example description</p>",
          "startDate": "2026-07-01T09:00:00",
          "dueDate": "2026-07-31T23:59:00",
          "editGroupMode": "",
          "createdAt": "2026-06-30T12:00:00",
          "user": 2001,
          "activitySubmissionId": null,
          "classUserId": 4001,
          "activityGroupId": null,
          "activityGroupName": null,
          "activitySubmissionSubmittedAt": null,
          "dueDateExceed": false,
          "quizSubmissionIsSubmitted": false,
          "countGroupMember": 1,
          "activitySubmissionIsLate": false,
          "fileActivities": [],
          "questions": [],
          "submissions": [],
          "lastDueDateNotificationDate": null,
          "lastStatusChangeNotificationDate": null,
          "previousSubmissionStatus": null
        }
      ]
    },
    {
      "id": 3002,
      "name": "Course Without Activities",
      "activities": []
    }
  ]
}
```

If the semester has no published classes:

```json
{
  "semesterId": 101,
  "classes": []
}
```

The operation is fail-fast and never returns a successful partial snapshot.

Relevant error responses are the same as
`GET /api/v1/Activity/{semesterId}/{classId}`, except `404 RESOURCE_NOT_FOUND`: that
response applies only to the class-membership check performed when `classId` is
supplied.

### GET `/api/v2/Activity/{semesterId}/snapshot`

Identical to [`GET /api/v1/Activity/{semesterId}/snapshot`](#get-apiv1activitysemesteridsnapshot)
in route parameters, headers, nesting behavior, and error responses. The only
difference is that each nested activity's `startDate`, `dueDate`, `createdAt`,
`lastDueDateNotificationDate`, and `lastStatusChangeNotificationDate` serialize as
genuine UTC instants instead of naive GMT+7 wall-clock strings — see the `v2`
[Activity](#activity) sample above.

Example request:

```http
GET /api/v2/Activity/101/snapshot
access-key: 00000000-0000-4000-8000-000000000001
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

### GET `/api/v1/health/leb2`

Returns locally observed request-gate/backoff state for each fixed LEB2 dependency.
It does not contact LEB2 and is not a live upstream reachability probe.

Authentication: none. This is the only route that does not require either
`access-key` or `Authorization`.

Request body: none.

Successful response — `200 OK`:

```json
{
  "observedAt": "2026-07-24T12:00:00Z",
  "source": "local-observed-state",
  "status": "healthy",
  "endpoints": [
    {
      "name": "activities",
      "status": "available",
      "retryAt": null,
      "retryAfterSeconds": 0
    },
    {
      "name": "classes",
      "status": "available",
      "retryAt": null,
      "retryAfterSeconds": 0
    },
    {
      "name": "cookie-login",
      "status": "available",
      "retryAt": null,
      "retryAfterSeconds": 0
    },
    {
      "name": "semesters",
      "status": "available",
      "retryAt": null,
      "retryAfterSeconds": 0
    },
    {
      "name": "user-login",
      "status": "available",
      "retryAt": null,
      "retryAfterSeconds": 0
    }
  ]
}
```

`status` is `degraded` when any endpoint has active backoff observed by this process,
and `healthy` when no endpoint has active local backoff. An unavailable endpoint
looks like:

```json
{
  "name": "activities",
  "status": "unavailable",
  "retryAt": "2026-07-24T12:00:30Z",
  "retryAfterSeconds": 30
}
```

For session-scoped backoff, an endpoint is shown as unavailable when any session
backoff for that endpoint is active. `retryAt` and `retryAfterSeconds` report the
latest active retry time observed for that endpoint; they do not identify which
session is affected. A health response therefore cannot be used to determine
whether a particular session is currently backoff-blocked.

This endpoint always returns `200 OK`, including when the reported status is
`degraded`, and includes:

```http
Cache-Control: no-store
```

`source` is always `local-observed-state`. The health state is local to one
application process or Cloud Run instance. The endpoint reports what this process
has observed; it does not prove that LEB2 is currently reachable.

## Swagger

Swagger UI is available only when `ASPNETCORE_ENVIRONMENT=Development`:

```text
/swagger
```

The normal Cloud Run deployment runs with `ASPNETCORE_ENVIRONMENT=Production`, so
Swagger is disabled there.

The Swagger UI version selector lists both `v1` and `v2` documents. `v2` advertises
only the `ActivityController` routes; every other route appears in `v1` only.

Swagger exposes `access-key` as an API-key header scheme and the existing opaque
LEB2 bearer scheme. Login and cookie operations advertise only `access-key`; data
operations advertise both requirements.

## Deployment rollout

Use this order when enabling the three compatibility controls:

1. Apply and verify the production key-revocation migration above.
2. Configure the device HMAC secret without enabling enforcement.
3. Deploy with `DeviceBinding:EnforcementEnabled=false`,
   `ClientCompatibility:EnforcementEnabled=false`, and
   `ApiVersioning:LegacyRoutesEnabled=true`.
4. Release a frontend that uses `/api/v1`, sends `X-Device-ID` and
   `X-Client-Version`, reads `/api/v1/meta`, and supports `/api/v1/User/logout`.
5. Verify that frontend in production.
6. Enable client-version enforcement, then device-binding enforcement.
7. After the migration period, set `ApiVersioning:LegacyRoutesEnabled=false`.

The canonical monitoring URL is `/api/v1/health/leb2`. If existing Cloud Run or
external monitoring still calls `/health/leb2`, keep legacy routes enabled until
those callers are migrated; the temporary alias uses the same anonymous health
action and is not the canonical contract.
