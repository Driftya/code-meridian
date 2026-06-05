# Observability Stack (Loki + Alloy + Prometheus + Grafana)

This stack runs:
- Loki for logs
- Grafana Alloy for Docker log collection and relabeling
- Prometheus for metrics storage/query
- Node Exporter for host CPU/memory/disk/network metrics
- cAdvisor for Docker container CPU/memory/network/filesystem metrics
- Grafana for visualization

## 1) Create the shared network

The compose file declares the network as external, so it must exist first:

```bash
docker network create observability-net
```

## 2) Configure environment

Copy `.docker/observability/.env.example` to `.docker/observability/.env` and update values as needed.

Minimum recommended values:

```bash
OBSERVABILITY_ENVIRONMENT=production
GF_SECURITY_ADMIN_USER=admin
GF_SECURITY_ADMIN_PASSWORD=change-me-now
GF_USERS_ALLOW_SIGN_UP=false
GF_SERVER_ROOT_URL=http://localhost:3000
GF_SERVER_DOMAIN=localhost
```

### Azure AD / Microsoft SSO (optional)

```bash
GF_AUTH_AZUREAD_ENABLED=false
GF_AUTH_AZUREAD_NAME=Microsoft
GF_AUTH_AZUREAD_CLIENT_ID=
GF_AUTH_AZUREAD_CLIENT_SECRET=
GF_AUTH_AZUREAD_TOKEN_URL=
GF_AUTH_AZUREAD_AUTH_URL=
GF_AUTH_AZUREAD_SCOPES=openid email profile
GF_AUTH_AZUREAD_ALLOW_SIGN_UP=true
GF_AUTH_DISABLE_LOGIN_FORM=false
GF_USERS_AUTO_ASSIGN_ORG=true
GF_USERS_AUTO_ASSIGN_ORG_ROLE=Admin
```

Redirect URL (Azure app registration):

```bash
http://localhost:3000/login/azuread
```

## 3) Start the stack

```bash
cd .docker/observability
docker compose up -d
```

## 4) Verify

```bash
docker compose ps
docker compose logs -f prometheus
docker compose logs -f node-exporter
docker compose logs -f cadvisor
docker compose logs -f grafana
```

Grafana will be available at `http://localhost:3000`.

## 5) Datasources

Grafana datasources are provisioned automatically from:

- `grafana/provisioning/datasources/datasources.yml`

Provisioned datasources:
- `Prometheus` (default) -> `http://prometheus:9090`
- `Loki` -> `http://loki:3100`

## 6) Enable app container log shipping

Alloy is configured to ship logs only for containers with label `logging=enabled`.

Add labels to containers you want in Loki:

```yaml
labels:
  logging: "enabled"
  app: "driftya"
  environment: "production"
```

Note: cAdvisor container metrics are available regardless of this log label.

## 7) Dashboards to import

In Grafana: `Dashboards -> New -> Import`

Suggested dashboard IDs:
- `1860` Node Exporter Full
- `14282` cAdvisor Exporter

## 8) Quick PromQL checks

Host CPU usage (%):

```promql
100 - (avg by(instance) (rate(node_cpu_seconds_total{mode="idle"}[5m])) * 100)
```

Host memory usage (%):

```promql
100 * (1 - (node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes))
```

Container CPU usage:

```promql
sum by (name) (rate(container_cpu_usage_seconds_total{name!=""}[5m]))
```

Container memory usage:

```promql
sum by (name) (container_memory_working_set_bytes{name!=""})
```

## 9) Security note

If this runs on a public host, do not expose Grafana on all interfaces without additional protection.

Safer local-only bind:

```yaml
ports:
  - "127.0.0.1:3000:3000"
```

Use a reverse proxy and SSO/auth in front of Grafana for internet exposure.
