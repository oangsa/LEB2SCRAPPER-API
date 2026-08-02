**Note**: This API is not officially affiliated with KMUTT or the LEB2 system. It is an independent tool created to help students monitor their academic activities more effectively.

See [authentication and scrape resilience](docs/auth-and-resilience.md) for the
credential contract, Supabase-backed access-key enrollment, error codes, backoff
behavior, and alert configuration.

See [the API reference](docs/api-reference.md) for request examples. Every
user-facing route requires the manually provisioned `access-key` header; data
routes additionally require the opaque LEB2 session cookie in `Authorization`.

See [Cloud Run continuous deployment](docs/cloud-run-continuous-deployment.md) for
the GitHub Actions workflow and one-time Workload Identity Federation setup.

An activated access key is bound to one local student identity. It cannot be used
to log in as another student, obtain a LEB2 session for another student, or request
activities with another LEB2 user ID. The key relationship is `keys.id` to
`user_keys` to `users.student_id` and `users.leb2_user_id`.

Production Cloud Run uses at most one active application instance. Caches,
throttling, backoff, incident correlation, and health state are process-local;
horizontal scaling requires distributed coordination.

## License and security reporting

This project is licensed under [Apache-2.0](LICENSE). Report non-confidential
security problems through [SECURITY.md](SECURITY.md): GitHub Issues are public
and are not a channel for confidential information.
