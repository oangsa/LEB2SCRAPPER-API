# Cloud Run continuous deployment

The workflow in `.github/workflows/deploy-cloud-run.yml` builds, tests, and deploys
the repository whenever a commit is pushed to `main`.

The first successful run creates this Cloud Run service in the existing Google Cloud
project:

```text
Service: leb2scrapper-api
Region:  asia-southeast3 (Bangkok)
```

It uses GitHub's OpenID Connect token and Google Cloud Workload Identity Federation.
No long-lived Google service-account key is stored in GitHub.

## Managed service configuration

The workflow applies these settings on every deployment:

| Setting | Value |
| --- | --- |
| Port | `8080` |
| CPU | `1` |
| Memory | `1 GiB` |
| Maximum concurrency per instance | `2` |
| Request timeout | `300 seconds` |
| Minimum instances | `0` |
| Maximum instances | `1` |
| Runtime environment | `Production` |
| Supabase connection | Secret Manager secret `leb2scrapper-api-supabase-connection`, latest enabled version |
| Canonical API | `/api/v1` |
| Legacy route aliases | Enabled during migration (`ApiVersioning__LegacyRoutesEnabled=true`) |
| Client compatibility enforcement | Disabled during rollout (`ClientCompatibility__EnforcementEnabled=false`) |
| Device binding persistence/enforcement | Disabled during rollout (`DeviceBinding__Enabled=false`, `DeviceBinding__EnforcementEnabled=false`) |

The conservative memory and concurrency values account for requests that launch
headless Chromium. Cloud Run request concurrency is the number of simultaneous HTTP
requests accepted by one instance; it is separate from the process-local
`OutboundRequestGate`, which allows at most four outbound LEB2 operations globally,
two per client, and two activity operations in aggregate. The workflow keeps both
limits unchanged.

This release intentionally runs at most one active Cloud Run instance because the
structural scrape cache, activity cache, client fingerprints, outbound throttling,
backoff, structural-failure correlation, and health state are process-local. The
instance limit makes those mechanisms one application-wide coordination state while
the service is running. State is still lost on restart or scale-to-zero.

Horizontal scaling is not supported by this coordination model. Supporting more
than one active instance requires distributed replacements for throttling, backoff,
and caches, plus distributed incident correlation and, if health is aggregated,
distributed health state.

Supabase stores the local users, keys, and user-key assignments. It does not store
LEB2 credentials or session cookies; clients retain their opaque LEB2 session
credentials.

The runtime uses a dedicated service account. It needs only Secret Manager access
to read the Supabase connection string at instance startup.

The workflow does not manage public access. A new service is private by default.
After the first deployment, the project owner can make it public once, and later
workflow runs preserve that setting.

The workflow's migration-safe environment defaults are intentional: old APKs can
continue using temporary unversioned aliases while a compatible client is released.
Before enabling device binding, create a Secret Manager secret for the HMAC key and
add it to the Cloud Run deployment as `DeviceBinding__HmacSecret`; never commit the
secret or place it in a normal environment variable in source control. The backend
refuses startup when device binding is enabled without that secret.

For example, create it from a protected local file:

```bash
gcloud secrets create leb2scrapper-api-device-hmac \
  --replication-policy="automatic" \
  --project="$LEB2_GCP_PROJECT_ID"

gcloud secrets versions add leb2scrapper-api-device-hmac \
  --data-file="/secure/path/device-hmac-secret.txt" \
  --project="$LEB2_GCP_PROJECT_ID"
```

Then add `DeviceBinding__HmacSecret=leb2scrapper-api-device-hmac:latest` to the
Cloud Run `--update-secrets` list and set `DeviceBinding__Enabled=true` before
turning on enforcement.

## GitHub repository variables

Configure these variables under:

`GitHub repository -> Settings -> Secrets and variables -> Actions -> Variables`

| Variable | Value |
| --- | --- |
| `GCP_PROJECT_ID` | Existing Google Cloud project ID. |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | Full Workload Identity Provider resource name. |
| `GCP_DEPLOY_SERVICE_ACCOUNT` | Dedicated deployment service-account email. |
| `GCP_RUNTIME_SERVICE_ACCOUNT` | Dedicated Cloud Run runtime service-account email. |

These values identify cloud resources but are not credentials, so repository
variables are appropriate.

## Supabase database connection

The application expects the PostgreSQL connection string in every environment as:

```text
ConnectionStrings__Production
```

The workflow maps this setting to Secret Manager secret
`leb2scrapper-api-supabase-connection`, using its latest enabled version. The secret
value never belongs in GitHub Actions YAML.

Open the Supabase project dashboard's **Connect** or database connection panel and
copy a PostgreSQL connection string. The application accepts Supabase's
`postgresql://...` URI directly, or Npgsql's key/value format. Use SSL, for example:

```text
postgresql://<supabase-user>:<password>@<supabase-host>:5432/postgres?sslmode=require

# or

Host=<supabase-host>;Port=5432;Database=postgres;Username=<supabase-user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

For this long-lived Npgsql backend on Cloud Run, prefer Supabase's **Session Pooler**
connection when the dashboard offers it. It normally uses an IPv4 pooler hostname
and port `5432`, and supports persistent backend connections. Use the direct database
host and port `5432` if the deployment has suitable IPv6 or the project's IPv4
add-on. The **Transaction Pooler** normally uses port `6543` and is intended for
short-lived/serverless transactions; it may require prepared statements to be
disabled, so it is not the first choice here. Supabase's labels and hostnames can
vary by project; use the values shown by the current dashboard. See the [Supabase
Postgres connection guide](https://supabase.com/docs/guides/database/connecting-to-postgres).

Store this value in Secret Manager. GitHub Actions variables are visible configuration;
GitHub Actions secrets are suitable for deployment-only secrets, but Cloud Run still
needs the value at runtime. The workflow therefore references Secret Manager instead.

## One-time Supabase schema

Apply the existing user identity prerequisite and device-binding table manually
before deploying enforcement. The application intentionally has no migrations or
automatic table creation:

```sql
ALTER TABLE users
ADD COLUMN leb2_user_id INTEGER;

CREATE UNIQUE INDEX uq_users_leb2_user_id
ON users (leb2_user_id)
WHERE leb2_user_id IS NOT NULL;

CREATE TABLE key_device_bindings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key_id UUID NOT NULL REFERENCES keys(id),
    device_id_hash VARCHAR NOT NULL,
    device_name VARCHAR,
    platform VARCHAR,
    os_version VARCHAR,
    app_version VARCHAR,
    bound_at TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    unbound_at TIMESTAMP WITHOUT TIME ZONE,
    unbound_reason VARCHAR,
    created_by VARCHAR NOT NULL,
    updated_by VARCHAR NOT NULL,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX uq_key_device_bindings_active_key
ON key_device_bindings (key_id)
WHERE unbound_at IS NULL;
```

If the schema already contains one of these objects, verify it instead of running a
duplicate statement. An operator device reset marks only the active binding unbound:

```sql
UPDATE key_device_bindings
SET unbound_at = CURRENT_TIMESTAMP,
    unbound_reason = 'operator-reset',
    updated_by = 'operator',
    updated_at = CURRENT_TIMESTAMP
WHERE key_id = '<key-uuid>'
  AND unbound_at IS NULL;
```

Account ownership in `user_keys` is never removed by logout or reset.

## Versioned health and rollout

Use `GET /api/v1/meta` for anonymous client bootstrap and
`GET /api/v1/health/leb2` for monitoring. The temporary `/health/leb2` alias remains
available only while `ApiVersioning__LegacyRoutesEnabled=true`; migrate monitors
before disabling aliases. Roll out in this order:

1. Apply the Supabase schema and create the HMAC secret.
2. Deploy with legacy aliases on and both compatibility enforcement flags off.
3. Release and verify the frontend using `/api/v1`, `X-Device-ID`,
   `X-Client-Version`, `/api/v1/meta`, and `/api/v1/User/logout`.
4. Enable client compatibility enforcement, then device-binding enforcement.
5. After the migration period, set `ApiVersioning__LegacyRoutesEnabled=false`.

## One-time Google Cloud setup

Run these commands in Google Cloud Shell while signed in as a project owner or an
administrator with equivalent IAM permissions.

### 1. Select the existing project

```bash
export LEB2_GCP_PROJECT_ID="your-existing-project-id"
export LEB2_GCP_REGION="asia-southeast3"
export LEB2_CLOUD_RUN_SERVICE="leb2scrapper-api"
export LEB2_GITHUB_REPOSITORY="oangsa/LEB2SCRAPPER-API"

gcloud config set project "$LEB2_GCP_PROJECT_ID"
```

Confirm that the service name is not already used in Bangkok:

```bash
gcloud run services describe "$LEB2_CLOUD_RUN_SERVICE" \
  --project="$LEB2_GCP_PROJECT_ID" \
  --region="$LEB2_GCP_REGION"
```

A `NOT_FOUND` response is expected before the first deployment. If the service
already exists, choose whether to update it or change `CLOUD_RUN_SERVICE` in the
workflow before continuing.

### 2. Enable the required APIs

```bash
gcloud services enable \
  run.googleapis.com \
  cloudbuild.googleapis.com \
  artifactregistry.googleapis.com \
  secretmanager.googleapis.com \
  iam.googleapis.com \
  iamcredentials.googleapis.com \
  sts.googleapis.com \
  --project="$LEB2_GCP_PROJECT_ID"
```

Get the numeric project number:

```bash
export LEB2_GCP_PROJECT_NUMBER="$(
  gcloud projects describe "$LEB2_GCP_PROJECT_ID" \
    --format="value(projectNumber)"
)"
```

### 3. Create dedicated deployment and runtime identities

Create the GitHub deployment service account:

```bash
gcloud iam service-accounts create github-cloud-run-deployer \
  --project="$LEB2_GCP_PROJECT_ID" \
  --display-name="GitHub Cloud Run deployer"

export LEB2_DEPLOY_SERVICE_ACCOUNT="github-cloud-run-deployer@${LEB2_GCP_PROJECT_ID}.iam.gserviceaccount.com"
```

Create the service's runtime identity:

```bash
gcloud iam service-accounts create leb2scrapper-api-runtime \
  --project="$LEB2_GCP_PROJECT_ID" \
  --display-name="LEB2SCRAPPER API runtime"

export LEB2_RUNTIME_SERVICE_ACCOUNT="leb2scrapper-api-runtime@${LEB2_GCP_PROJECT_ID}.iam.gserviceaccount.com"
```

If either account already exists, skip its create command and keep the corresponding
export. The runtime account does not need broad project roles, but it does need the
Secret Manager accessor role shown below.

Create the Secret Manager secret and add the connection string from a protected local
file. The file must contain only the value for `ConnectionStrings:Production`; never
commit it:

```bash
export LEB2_SUPABASE_SECRET_NAME="leb2scrapper-api-supabase-connection"

gcloud secrets create "$LEB2_SUPABASE_SECRET_NAME" \
  --replication-policy="automatic" \
  --project="$LEB2_GCP_PROJECT_ID"

gcloud secrets versions add "$LEB2_SUPABASE_SECRET_NAME" \
  --data-file="/secure/path/supabase-connection-string.txt" \
  --project="$LEB2_GCP_PROJECT_ID"

gcloud secrets add-iam-policy-binding "$LEB2_SUPABASE_SECRET_NAME" \
  --project="$LEB2_GCP_PROJECT_ID" \
  --member="serviceAccount:${LEB2_RUNTIME_SERVICE_ACCOUNT}" \
  --role="roles/secretmanager.secretAccessor"
```

The first version is pinned as `1` by the workflow. For rotation, add a new secret
version and update the workflow's `--update-secrets` value to the new version before
deploying. Cloud Run fetches environment-variable secrets before an instance starts;
the runtime service account therefore needs the accessor role. See Google's
[Cloud Run Secret Manager guide](https://docs.cloud.google.com/run/docs/configuring/services/secrets).

Grant the deployer only the project roles required for a Cloud Run source deployment:

```bash
gcloud projects add-iam-policy-binding "$LEB2_GCP_PROJECT_ID" \
  --member="serviceAccount:${LEB2_DEPLOY_SERVICE_ACCOUNT}" \
  --role="roles/run.sourceDeveloper"

gcloud projects add-iam-policy-binding "$LEB2_GCP_PROJECT_ID" \
  --member="serviceAccount:${LEB2_DEPLOY_SERVICE_ACCOUNT}" \
  --role="roles/serviceusage.serviceUsageConsumer"
```

Allow the deployer to attach the dedicated runtime identity to the service:

```bash
gcloud iam service-accounts add-iam-policy-binding \
  "$LEB2_RUNTIME_SERVICE_ACCOUNT" \
  --project="$LEB2_GCP_PROJECT_ID" \
  --member="serviceAccount:${LEB2_DEPLOY_SERVICE_ACCOUNT}" \
  --role="roles/iam.serviceAccountUser"
```

Cloud Run source deployment uses the Compute Engine default service account for its
Cloud Build by default. Grant that account the builder role:

```bash
gcloud projects add-iam-policy-binding "$LEB2_GCP_PROJECT_ID" \
  --member="serviceAccount:${LEB2_GCP_PROJECT_NUMBER}-compute@developer.gserviceaccount.com" \
  --role="roles/run.builder"
```

### 4. Create the GitHub Workload Identity Provider

Create a pool:

```bash
gcloud iam workload-identity-pools create github-actions \
  --project="$LEB2_GCP_PROJECT_ID" \
  --location="global" \
  --display-name="GitHub Actions"
```

If a suitable pool already exists, reuse it and substitute its ID for
`github-actions` in the remaining commands.

Create a provider restricted to this repository and the `main` branch:

```bash
gcloud iam workload-identity-pools providers create-oidc \
  leb2scrapper-api-main \
  --project="$LEB2_GCP_PROJECT_ID" \
  --location="global" \
  --workload-identity-pool="github-actions" \
  --display-name="LEB2SCRAPPER API main" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref" \
  --attribute-condition="assertion.repository == '${LEB2_GITHUB_REPOSITORY}' && assertion.ref == 'refs/heads/main'"
```

Get the full pool and provider resource names:

```bash
export LEB2_WORKLOAD_IDENTITY_POOL="$(
  gcloud iam workload-identity-pools describe github-actions \
    --project="$LEB2_GCP_PROJECT_ID" \
    --location="global" \
    --format="value(name)"
)"

export LEB2_WORKLOAD_IDENTITY_PROVIDER="$(
  gcloud iam workload-identity-pools providers describe \
    leb2scrapper-api-main \
    --project="$LEB2_GCP_PROJECT_ID" \
    --location="global" \
    --workload-identity-pool="github-actions" \
    --format="value(name)"
)"
```

Allow only this GitHub repository to impersonate the deployment service account:

```bash
gcloud iam service-accounts add-iam-policy-binding \
  "$LEB2_DEPLOY_SERVICE_ACCOUNT" \
  --project="$LEB2_GCP_PROJECT_ID" \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/${LEB2_WORKLOAD_IDENTITY_POOL}/attribute.repository/${LEB2_GITHUB_REPOSITORY}"
```

IAM and Workload Identity changes can take several minutes to propagate.

### 5. Add the GitHub repository variables

Create the four repository variables using these values:

```text
GCP_PROJECT_ID=<value of LEB2_GCP_PROJECT_ID>
GCP_WORKLOAD_IDENTITY_PROVIDER=<value of LEB2_WORKLOAD_IDENTITY_PROVIDER>
GCP_DEPLOY_SERVICE_ACCOUNT=<value of LEB2_DEPLOY_SERVICE_ACCOUNT>
GCP_RUNTIME_SERVICE_ACCOUNT=<value of LEB2_RUNTIME_SERVICE_ACCOUNT>
```

The provider must use its full resource name, for example:

```text
projects/123456789/locations/global/workloadIdentityPools/github-actions/providers/leb2scrapper-api-main
```

## Create the service

Commit the workflow and push or merge it into `main`. Open the repository's
**Actions** tab and select **Deploy to Cloud Run** to monitor the run.

The first successful run:

1. Restores, builds, and tests the .NET 9 solution.
2. Exchanges GitHub's short-lived OIDC token for Google credentials.
3. Builds the repository `Dockerfile` with Cloud Build.
4. Creates the private `leb2scrapper-api` Cloud Run service in Bangkok.
5. Writes the new Cloud Run URL to the workflow summary.

If authentication fails immediately after setup, wait five minutes and rerun the
workflow before changing the IAM configuration.

## Public access

Keep the service private if it is called only by authenticated Google Cloud
workloads. To expose this API to web or mobile clients, disable the Cloud Run Invoker
IAM check after the first deployment:

```bash
gcloud run services update "$LEB2_CLOUD_RUN_SERVICE" \
  --project="$LEB2_GCP_PROJECT_ID" \
  --region="$LEB2_GCP_REGION" \
  --no-invoker-iam-check
```

This is an owner-controlled, one-time access decision. Later workflow deployments
preserve it.

Get the service URL and verify the health endpoint:

```bash
export LEB2_CLOUD_RUN_URL="$(
  gcloud run services describe "$LEB2_CLOUD_RUN_SERVICE" \
    --project="$LEB2_GCP_PROJECT_ID" \
    --region="$LEB2_GCP_REGION" \
    --format="value(status.url)"
)"

curl "${LEB2_CLOUD_RUN_URL}/api/v1/health/leb2"
```

Making the service public exposes `/api/v1/User/login` and `/api/v1/User/cookie` to network
traffic without Cloud Run IAM. They still require a valid application `access-key`
(`/api/v1/User/cookie` requires an activated one), but review application-level abuse
controls before advertising the URL broadly.
