# Authentication and scrape resilience

## Request authentication

The backend does not store LEB2 credentials or session cookies. Authenticated data
routes require the client-held LEB2 cookie on every request:

```http
Authorization: Bearer <session-cookie-value>
```

The value after `Bearer` is opaque. It is not parsed or validated as a JWT. A custom
ASP.NET Core authentication handler keeps it in a scoped request object, repositories
use it as the outbound LEB2 `Cookie` header, and the scoped value is cleared when the
response completes.

For compatibility with clients of earlier releases, the same opaque value is also
accepted directly in `Authorization` without the `Bearer` prefix. New clients should
use the Bearer form; the legacy form can be removed in a future version after clients
have migrated.

The following routes require this header:

- `GET /Semester`
- `GET /Class/{id}`
- `POST /Activity`

`POST /User/login` and `POST /User/cookie` remain credential-acquisition routes. Their
request credentials are used only for the outbound call in that request and are not
persisted.

## Error contract

Errors use a JSON `responseCode` so clients can distinguish authentication from LEB2
or scraper failures.

| HTTP status | `responseCode` | Meaning |
| --- | --- | --- |
| `400` | `INVALID_REQUEST` | Request input failed validation. |
| `401` | `AUTHENTICATION_REQUIRED` | Bearer header is absent or malformed. |
| `401` | `SESSION_EXPIRED` | LEB2 rejected or redirected the supplied session. Discard the client-held cookie and reauthenticate. |
| `502` | `SCRAPE_RESPONSE_CHANGED` | LEB2 responded, but its HTML or JSON shape no longer matches the scraper. |
| `503` | `LEB2_UNAVAILABLE` | A transient LEB2 network, timeout, rate-limit, or server failure occurred. |
| `503` | `REQUEST_BACKOFF_ACTIVE` | A recent failure has temporarily paused this endpoint. The response includes `Retry-After`. |

As verified on 2026-07-24, an absent or invalid LEB2 session redirects both the class
page and activity API to `https://www.leb2.org/` with HTTP 302. The direct HTTP adapter
detects that redirect before following it; Selenium detects the resulting non-app host.

## Outbound request gate and backoff

All direct HTTP calls and top-level Selenium navigations pass through the singleton
outbound request gate. It:

- caps concurrent LEB2 operations;
- applies exponential backoff per endpoint;
- never caches session cookies or scraped user data;
- clears backoff after a successful request;
- does not treat session expiry as a scrape failure;
- correlates structural parse failures for alerting.

There is no scheduler, background service, or backoff timer.

Defaults are configured under `OutboundRequestGate` in `appsettings.json`. A structural
alert is raised after three consecutive same-shape failures for an endpoint within
15 minutes. Alert delivery runs outside the failed request, is time-bounded, and a
failed delivery remains eligible for retry after the next matching failure.

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
