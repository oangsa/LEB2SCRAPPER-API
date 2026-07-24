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
| Maximum instances | `3` |
| Runtime environment | `Production` |

The conservative memory and concurrency values account for requests that launch
headless Chromium. The runtime uses a dedicated service account with no project
roles.

The workflow does not manage public access. A new service is private by default.
After the first deployment, the project owner can make it public once, and later
workflow runs preserve that setting.

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
export. The runtime account intentionally receives no project role because this
application does not call Google Cloud APIs.

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

curl "${LEB2_CLOUD_RUN_URL}/health/leb2"
```

Making the service public exposes the unauthenticated `/User/login` and
`/User/cookie` routes. Review application-level abuse controls before advertising
the URL broadly.
