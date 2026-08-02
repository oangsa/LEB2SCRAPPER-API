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

## Endpoint summary

| Method | Path | Access key | LEB2 session required | Request body |
| --- | --- | --- | --- | --- |
| `POST` | `/User/login` | Provisioned | No | Credentials |
| `POST` | `/User/cookie` | Activated | No | Credentials |
| `GET` | `/Semester` | Activated | Yes | None |
| `GET` | `/Class/{id}` | Activated | Yes | None |
| `GET` | `/Activity/{semesterId}/{classId}` | Activated | Yes | None |
| `GET` | `/Activity/{semesterId}` | Activated | Yes | None |
| `GET` | `/Activity/{semesterId}/snapshot` | Activated | Yes | None |
| `GET` | `/health/leb2` | Anonymous | No | None |

## Authentication

Every route except `GET /health/leb2` requires the application access key:

```http
access-key: <uuid-from-keys.id>
```

The UUID is the `keys.id` value manually provisioned in Supabase. It is a secret
per-user API credential, not a JWT. An activated key is bound to one local student
through `keys.id`, `user_keys`, `users.student_id`, and `users.leb2_user_id`.
It cannot be used to log in as another student, obtain that student's LEB2 session,
or request activities with that student's numeric LEB2 ID. Do not put it in a URL,
query string, request body, or log.
`POST /User/login` accepts a provisioned but unassigned key. `/User/cookie` and
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

The application does not run migrations. Before merging or deploying this version,
manually apply and verify this exact SQL:

```sql
ALTER TABLE users
ADD COLUMN leb2_user_id INTEGER;

CREATE UNIQUE INDEX uq_users_leb2_user_id
ON users (leb2_user_id)
WHERE leb2_user_id IS NOT NULL;
```

In production, `user_keys` ownership uniqueness is enforced by constraint
`uq_user_keys_key`. Apply and verify the schema first, then merge; the main deployment
deploys Cloud Run. This prerequisite adds no HTTP/API behavior, and the constraint name
is not exposed to API clients.

Example manual provisioning with a fake UUID:

```sql
INSERT INTO keys (id, created_by, updated_by)
VALUES (
    '00000000-0000-4000-8000-000000000001',
    'admin',
    'admin'
);
```

Use the key once with `/User/login`. After successful LEB2 authentication, the API:

1. Normalizes the login username into `users.student_id`.
2. Persists authoritative LEB2 `User.Id` as `users.leb2_user_id`.
3. Builds `users.name` from the English LEB2 name, falling back to Thai fields.
4. Upserts the user by `student_id` and claims the key in the same PostgreSQL transaction.

An invalid LEB2 login creates neither the user nor the assignment. A key already
assigned to another student is never transferred automatically. Existing users
with null `leb2_user_id` are initialized by their next successful `/User/login`;
activity requests fail closed until that happens.

| Key state | `/User/login` | `/User/cookie` and data routes |
| --- | --- | --- |
| Missing from `keys` | Rejected | Rejected |
| Provisioned, unassigned | Allowed | Rejected with `ACCESS_KEY_NOT_ACTIVATED` |
| Assigned in `user_keys` | Allowed, idempotent for the owner | Allowed when `leb2_user_id` is initialized |

To revoke access, delete the `keys` row. To make a key claimable again, delete its
`user_keys` row. The supplied foreign-key cascades clean up related assignments.

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

The successful `/User/login` response remains the existing LEB2 user profile shape;
it does not return the access key or local database identifiers.

## Shared response models

### Activity

Activity endpoints use the following object. Nullable fields can contain `null`.
Dates emitted by ASP.NET Core use ISO 8601 JSON strings.

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

When present, `activitySubmissionSubmittedAt` has this shape:

```json
{
  "date": "2026-07-20 14:30:00",
  "timezoneType": 3,
  "timezone": "Asia/Bangkok"
}
```

`fileActivities` and `submissions` contain upstream-defined JSON objects. Their
internal fields are not fixed by this API.

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
| `401` | `ACCESS_KEY_REQUIRED` | The `access-key` header was absent. |
| `401` | `ACCESS_KEY_INVALID` | The access key was malformed or is not provisioned. |
| `401` | `AUTHENTICATION_REQUIRED` | A required LEB2 session header was absent or empty. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. |
| `403` | `ACCESS_KEY_NOT_ACTIVATED` | The key must first be claimed by a successful `/User/login`. |
| `403` | `ACCESS_KEY_ALREADY_ASSIGNED` | The key belongs to another account. |
| `403` | `ACCESS_KEY_IDENTITY_MISMATCH` | The key cannot be used with the submitted student or LEB2 user ID. |
| `403` | `ACCESS_KEY_REAUTHENTICATION_REQUIRED` | The local user must complete `/User/login` again to initialize identity. |
| `409` | `ACCESS_KEY_IDENTITY_CONFLICT` | Successful LEB2 identity conflicts with an established local identity. |
| `404` | `RESOURCE_NOT_FOUND` | The requested user, resource, or class/semester relationship was not found. |
| `408` | `LEB2_UNAVAILABLE` | The request timed out. |
| `429` | `CLIENT_THROTTLE_ACTIVE` | The client has too many active or queued LEB2 requests. |
| `500` | `UNEXPECTED_ERROR` | An unexpected server error occurred. |
| `502` | `LEB2_UNAVAILABLE` | LEB2 rejected or could not complete the request. |
| `502` | `SCRAPE_RESPONSE_CHANGED` | LEB2 returned an unexpected HTML or JSON structure. |
| `503` | `LEB2_UNAVAILABLE` | A transient LEB2 network, rate-limit, or server failure occurred. |
| `503` | `REQUEST_BACKOFF_ACTIVE` | This LEB2 operation is temporarily paused after a recent failure. |
| `503` | `ACCESS_KEY_STORE_UNAVAILABLE` | Supabase access-key validation is temporarily unavailable. |

Responses with `CLIENT_THROTTLE_ACTIVE` or `REQUEST_BACKOFF_ACTIVE` include a
`Retry-After` response header. LEB2 session failures include
`WWW-Authenticate: Bearer`; access-key failures use the `ACCESS_KEY_*` response
codes and do not describe the access key as a bearer JWT.

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

### POST `/User/login`

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

### POST `/User/cookie`

Signs in through Selenium and returns the complete LEB2 session cookie needed by
protected endpoints.

Authentication: `access-key` header with an already assigned UUID. The key must
first be claimed by a successful `/User/login` and must have a stored
`users.leb2_user_id`. Legacy assigned users with a null value must complete
`/User/login` again before requesting a cookie.

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
- `403 ACCESS_KEY_NOT_ACTIVATED` when `/User/login` has not claimed the key yet.
- `403 ACCESS_KEY_IDENTITY_MISMATCH` when the credentials belong to another student.
- `403 ACCESS_KEY_REAUTHENTICATION_REQUIRED` when local identity data is incomplete.
- `404 RESOURCE_NOT_FOUND` if the login completes without a usable result.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` when LEB2 does not accept the credentials or cannot complete the login.
- `502 SCRAPE_RESPONSE_CHANGED` when the LEB2 login page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `503 ACCESS_KEY_STORE_UNAVAILABLE` when Supabase access-key validation is temporarily unavailable.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/Semester`

Returns the semesters visible to the authenticated LEB2 session. `id` is the
internal LEB2 semester ID used with `/Class/{id}` and activity routes. `name` is
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
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/User/login`.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 SCRAPE_RESPONSE_CHANGED` when the rendered page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `503 ACCESS_KEY_STORE_UNAVAILABLE` when Supabase access-key validation is temporarily unavailable.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/Class/{id}`

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
GET /Class/101
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
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/User/login`.
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

### GET `/Activity/{semesterId}/{classId}`

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
GET /Activity/101/3001
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
- `403 ACCESS_KEY_NOT_ACTIVATED` when the key has not been claimed through `/User/login`.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `404 RESOURCE_NOT_FOUND` when the class does not belong to the supplied semester.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` or `502 SCRAPE_RESPONSE_CHANGED` for upstream failures.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

A non-integer route value does not match the route and normally produces the
framework's `404` response.

### GET `/Activity/{semesterId}`

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
GET /Activity/101
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
`GET /Activity/{semesterId}/{classId}`.

### GET `/Activity/{semesterId}/snapshot`

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
GET /Activity/101/snapshot
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
`GET /Activity/{semesterId}/{classId}`.

### GET `/health/leb2`

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

Swagger exposes `access-key` as an API-key header scheme and the existing opaque
LEB2 bearer scheme. Login and cookie operations advertise only `access-key`; data
operations advertise both requirements.
