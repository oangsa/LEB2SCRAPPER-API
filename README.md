**Note**: This API is not officially affiliated with KMUTT or the LEB2 system. It is an independent tool created to help students monitor their academic activities more effectively.

See [authentication and scrape resilience](docs/auth-and-resilience.md) for the
two-credential contract, Supabase-backed access-key enrollment, error codes,
backoff behavior, and alert configuration.

See [the API reference](docs/api-reference.md) for request examples. Every
user-facing route requires the manually provisioned `access-key` header; data
routes additionally require the opaque LEB2 session cookie in `Authorization`.

See [Cloud Run continuous deployment](docs/cloud-run-continuous-deployment.md) for
the GitHub Actions workflow and one-time Workload Identity Federation setup.

Production Cloud Run uses at most one active application instance. Caches,
throttling, backoff, incident correlation, and health state are process-local;
horizontal scaling requires distributed coordination.

## License and security reporting

This project is licensed under [Apache-2.0](LICENSE). Report non-confidential
security problems through [SECURITY.md](SECURITY.md): GitHub Issues are public
and are not a channel for confidential information.
