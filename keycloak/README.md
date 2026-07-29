# Keycloak realm provisioning

`realm-grafirio.json` is the source of truth for the `grafirio` realm: clients, protocol
mappers, business claims and the admin account that `Grafirio.Identity.Api` uses to create users.

Keycloak runs with `start --import-realm`, so the realm is (re)created automatically on every
start when it does not already exist. The container databases in Azure have **no persistent
volume** — Azure Files uses SMB, which does not provide the POSIX file locking that Postgres,
SQL Server and MongoDB require, so a restart wipes them. Auto-import is what makes that
survivable: an empty Keycloak database self-heals into a fully configured realm.

## What the realm provides

| Item | Purpose |
| --- | --- |
| `grafirio-client` | Public SPA client used by the frontends (`keycloak-js`) |
| `gateway.api`, `commerce.api`, `grafirio-api` | Audience targets validated by the .NET services |
| `company_id`, `business_roles`, `accessible_companies` mappers | Claims required by the `CompanyAccess` / `CompanyAdmin` / `CompanyManager` policies in `Grafirio.Shared.Identity` |
| `grifirio_admin` user | Holds `realm-management` roles so `KeycloakUserService` can create users |

Custom attributes are only emitted because the realm sets `unmanagedAttributePolicy: ENABLED`
in its user profile config — Keycloak 24+ silently drops unknown attributes otherwise.

Note that protocol mappers live directly on `grafirio-client` rather than in a separate client
scope: declaring a realm-level `clientScopes` array replaces Keycloak's built-in scopes, which
strips `profile`/`email` and therefore the `email` and `preferred_username` claims.

## Local development

`docker-compose.yml` mounts this directory read-only at `/opt/keycloak/data/import`, so
`docker compose up` gives a fully provisioned realm with no manual Admin Console steps.

## Azure

The same file is stored in the `keycloak-import` Azure file share (storage account
`grifiriodata`) and mounted read-only on the `keycloak` container app.

After editing this file, re-upload it and restart Keycloak:

```bash
key=$(az storage account keys list --account-name grifiriodata --resource-group grifirio-rg --query "[0].value" -o tsv)
az storage file upload --account-name grifiriodata --account-key "$key" --share-name keycloak-import --source keycloak/realm-grafirio.json --path realm-grafirio.json
```

Importing only happens when the realm is absent. To apply changes to an existing realm, delete
it in the Admin Console (or via the Admin REST API) and restart the `keycloak` container app.
