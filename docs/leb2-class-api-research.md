# LEB2 Semester/Class API Research

Research date: 2026-07-24

## Scope and safety

This investigation used unauthenticated, read-only requests to public,
first-party `app.leb2.org` HTML, manifests, JavaScript bundles, and source
maps. No credentials, cookies, authorization values, personal data, browser
automation, or authenticated API requests were used. Response headers were
not printed or retained.

## Verified facts

- Anonymous requests to the application root and `/class` return HTTP 302 to
  `https://www.leb2.org/`. Consequently, the authenticated class-page HTML and
  its runtime request configuration are not available from those anonymous
  page requests. Source:
  [LEB2 application class page](https://app.leb2.org/class).
- The public [Mix manifest](https://app.leb2.org/mix-manifest.json) exposes
  current first-party bundle and source-map paths. Relevant-looking entries
  include:
  - `/js/class_list_check_course.js?id=329fbe17d756da1a30ae`
  - `/js/class_list_check_course.js.map?id=3aa9528ddb4497fd0502`
  - `/js/student_dashboard.js?id=8b763e42ff69e25c85e3`
  - `/js/student_dashboard.js.map?id=18093c47d7a88f052b88`
  - `/js/m.dashboard.js?id=d9deeff6eb1dd1df6328`
  - `/js/m.home.js?id=70fa9b74e8cb34f5ac40`
  - `/js/app-webpack.js?id=a332e8f88b73f049ef72`
- The current
  [class-list bundle](https://app.leb2.org/js/class_list_check_course.js?id=329fbe17d756da1a30ae)
  renders class-related values such as `course_code`, `course_name`, and
  `semester_slug`. It does not identify the HTTP request that supplies the
  semester/class collection.
- The shared
  [application bundle](https://app.leb2.org/js/app-webpack.js?id=a332e8f88b73f049ef72)
  configures generic jQuery AJAX behavior, including
  `X-Requested-With: XMLHttpRequest` and a page-provided CSRF header. This is
  shared transport code, not evidence that a particular semester/class call
  exists or requires those headers.
- The public
  [sign-in application bundle](https://signin.leb2.org/assets/index-g_Wr5RKJ.js)
  identifies a separate API base,
  `https://leb2-mcs-api-production.leb2.org/mono-core/v1`, and implements:
  - `GET /public/class/{classId}`
  - `GET /public/class/is-member?classId={classId}`

  Its fetch wrapper sends `Accept: application/json` and adds
  `Authorization: Bearer {accessToken}` when the sign-in application's access
  token is available. Status-only requests made without authorization on the
  research date returned HTTP 401 for both endpoints; response bodies and
  headers were discarded. These calls retrieve one class or check membership,
  rather than discover a user's semesters or class list, and the bundle does
  not provide a typed success-response mapping.
- The public [web-app manifest](https://app.leb2.org/manifest.json) declares
  `/mobile` as an application start URL. This does not describe a
  semester/class data contract.

## Unknowns

The public evidence inspected does **not** verify:

- an endpoint URL for semester discovery;
- an endpoint URL for class discovery;
- either request's HTTP method, parameters, or necessary headers;
- redirect/session-expiry behavior for either request;
- the response envelope, class/semester field mapping, nullability, ordering,
  or malformed-response behavior.
- whether the newer sign-in access token can or should be used by this
  repository's existing session-cookie flow.

The class-list values found in a rendering bundle are not enough to infer a
JSON response shape. They may be supplied by server-rendered page data, another
bundle, or an authenticated runtime request.

## Implementation conclusion

The evidence is **insufficient to implement direct semester/class repository
calls safely**. Doing so would require guessing at least the endpoint, request
contract, and response mapping. The confirmed scraper-first design therefore
keeps Selenium-based semester/class discovery and treats Chromium-backed cache
misses separately from the warm snapshot latency target.
