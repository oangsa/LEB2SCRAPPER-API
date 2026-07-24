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

All request and response bodies use JSON unless stated otherwise.

## Endpoint summary

| Method | Path | LEB2 session required | Request body |
| --- | --- | --- | --- |
| `POST` | `/User/login` | No | Credentials |
| `POST` | `/User/cookie` | No | Credentials |
| `GET` | `/Semester` | Yes | None |
| `GET` | `/Class/{id}` | Yes | None |
| `GET` | `/Activity/{semesterId}/{classId}` | Yes | None |
| `GET` | `/Activity/{semesterId}` | Yes | None |
| `GET` | `/Activity/{semesterId}/snapshot` | Yes | None |
| `GET` | `/health/leb2` | No | None |

## Authentication

Protected endpoints require the complete client-held LEB2 session cookie:

```http
Authorization: Bearer <leb2-session-cookie>
```

The value is an opaque LEB2 cookie, not a JWT. Legacy clients may send the cookie
directly without the `Bearer` prefix, but new clients should use the Bearer form.

Every activity endpoint also requires:

```http
X-LEB2-USER-ID: <positive-integer-user-id>
```

If Cloud Run IAM authentication is enabled, use
`X-Serverless-Authorization` for the Google identity token so that
`Authorization` remains available for the LEB2 session:

```http
X-Serverless-Authorization: Bearer <google-id-token>
Authorization: Bearer <leb2-session-cookie>
```

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
| `401` | `AUTHENTICATION_REQUIRED` | A required LEB2 session header was absent or empty. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. |
| `404` | `RESOURCE_NOT_FOUND` | The requested user or resource was not found. |
| `408` | `LEB2_UNAVAILABLE` | The request timed out. |
| `429` | `CLIENT_THROTTLE_ACTIVE` | The client has too many active or queued LEB2 requests. |
| `502` | `LEB2_UNAVAILABLE` | LEB2 rejected or could not complete the request. |
| `502` | `SCRAPE_RESPONSE_CHANGED` | LEB2 returned an unexpected HTML or JSON structure. |
| `503` | `LEB2_UNAVAILABLE` | A transient LEB2 network, rate-limit, or server failure occurred. |
| `503` | `REQUEST_BACKOFF_ACTIVE` | This LEB2 operation is temporarily paused after a recent failure. |
| `500` | `UNEXPECTED_ERROR` | An unexpected server error occurred. |

Responses with `CLIENT_THROTTLE_ACTIVE` or `REQUEST_BACKOFF_ACTIVE` include a
`Retry-After` response header. Authentication failures include
`WWW-Authenticate: Bearer`.

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

Authentication: none.

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
- `404 RESOURCE_NOT_FOUND` when the credentials are rejected or no user is found.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` when LEB2 rejects or cannot complete the upstream request.
- `502 SCRAPE_RESPONSE_CHANGED` for an unexpected successful LEB2 response shape.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### POST `/User/cookie`

Signs in through Selenium and returns the complete LEB2 session cookie needed by
protected endpoints.

Authentication: none.

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
- `404 RESOURCE_NOT_FOUND` if the login completes without a usable result.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 LEB2_UNAVAILABLE` when LEB2 does not accept the credentials or cannot complete the login.
- `502 SCRAPE_RESPONSE_CHANGED` when the LEB2 login page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/Semester`

Returns the semester IDs visible to the authenticated LEB2 session.

Required header:

```http
Authorization: Bearer <leb2-session-cookie>
```

Request body: none.

Successful response — `200 OK`:

```json
[
  101,
  102
]
```

If no semesters are found, the response is:

```json
[]
```

Relevant error responses:

- `401 AUTHENTICATION_REQUIRED` when the session header is absent or empty.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
- `429 CLIENT_THROTTLE_ACTIVE` when this client has too many queued requests.
- `502 SCRAPE_RESPONSE_CHANGED` when the rendered page no longer matches the scraper.
- `503 LEB2_UNAVAILABLE` or `503 REQUEST_BACKOFF_ACTIVE` for transient failures.
- `500 UNEXPECTED_ERROR` for an unexpected server error.

### GET `/Class/{id}`

Returns classes for one semester.

Route parameter:

| Name | Type | Description |
| --- | --- | --- |
| `id` | integer | LEB2 semester ID; clients should send a positive value. |

Required header:

```http
Authorization: Bearer <leb2-session-cookie>
```

Request body: none.

Example request:

```http
GET /Class/101
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
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /Activity/101/3001
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

The current implementation validates `semesterId` as route context but does not
scrape the semester or verify that `classId` belongs to it.

Relevant error responses:

- `400 INVALID_REQUEST` when an integer route value is less than one, or when
  `X-LEB2-USER-ID` is missing, non-integer, or less than one.
- `401 AUTHENTICATION_REQUIRED` when the session header is absent or empty.
- `401 SESSION_EXPIRED` when LEB2 rejects the session.
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
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /Activity/101
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
Authorization: Bearer <leb2-session-cookie>
X-LEB2-USER-ID: 2001
```

Request body: none.

Example request:

```http
GET /Activity/101/snapshot
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

Returns the process-local backoff status for each fixed LEB2 dependency.

Authentication: none.

Request body: none.

Successful response — `200 OK`:

```json
{
  "observedAt": "2026-07-24T12:00:00Z",
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

`status` is `degraded` when any endpoint has active backoff. An unavailable endpoint
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

The health state is local to one application process or Cloud Run instance.

## Swagger

Swagger UI is available only when `ASPNETCORE_ENVIRONMENT=Development`:

```text
/swagger
```

The normal Cloud Run deployment runs with `ASPNETCORE_ENVIRONMENT=Production`, so
Swagger is disabled there.
