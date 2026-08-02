**Note**: This API is not officially affiliated with KMUTT or the LEB2 system. It is an independent tool created to help students monitor their academic activities more effectively.

See [authentication and scrape resilience](docs/auth-and-resilience.md) for the
credential contract, Supabase-backed access-key enrollment, error codes, backoff
behavior, and alert configuration.

See [the API reference](docs/api-reference.md) for request examples. Every
user-facing route uses the canonical `/api/v1` prefix. Protected routes require the
manually provisioned `access-key` header; data routes additionally require the opaque
LEB2 session cookie in `Authorization`. During migration,
`ApiVersioning__LegacyRoutesEnabled=true` keeps deprecated unversioned aliases
available; set it to `false` after clients migrate.

See [Cloud Run continuous deployment](docs/cloud-run-continuous-deployment.md) for
the GitHub Actions workflow and one-time Workload Identity Federation setup.

An activated access key is bound to one local student identity. It cannot be used
to log in as another student, obtain a LEB2 session for another student, or request
activities with another LEB2 user ID. The key relationship is `keys.id` to
`user_keys` to `users.student_id` and `users.leb2_user_id`.

Device binding is temporary and separate from account ownership. When enabled,
`X-Device-ID` is HMAC-SHA256 fingerprinted before persistence, one active device is
allowed per key, and `POST /api/v1/User/logout` removes only that device binding.
`X-Client-Version` is the authoritative frontend-build version and populates stored
device `app_version`; clients do not send a duplicate app-version header. Anonymous
bootstrap and monitoring endpoints are `GET /api/v1/meta` and
`GET /api/v1/health/leb2`.

After the documented production migration, `DELETE FROM public.keys WHERE id = ...`
cascades `user_keys` and `key_device_bindings` while preserving the `users` row.

Production Cloud Run uses at most one active application instance. Caches,
throttling, backoff, incident correlation, and health state are process-local;
horizontal scaling requires distributed coordination.

## License and security reporting

This project is licensed under [Apache-2.0](LICENSE). Report non-confidential
security problems through [SECURITY.md](SECURITY.md): GitHub Issues are public
and are not a channel for confidential information.
