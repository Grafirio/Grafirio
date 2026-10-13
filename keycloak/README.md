# Keycloak realm provisioning

`realm-grafirio.json` is the source of truth for the `grafirio` realm: clients, protocol
mappers, business claims and the admin account that `Grafirio.Identity.Api` uses to create users.

Keycloak runs with `start --import-realm`, so an absent realm is initialized from the import.
The live Keycloak database is Azure PostgreSQL Flexible Server `grifirio-keycloak-db`,
not container-local storage (verified 2026-10-14). It persists account and group assignments
across restarts and has 35-day backup retention. The import defines configuration, not a
backup of production users: restore the database to recover existing identities.

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

Importing only happens when the realm is absent. Apply existing-realm changes with targeted
Admin REST API updates. Do not delete the production realm to apply configuration changes:
that destroys users, credentials, sessions and assignments.

## Persistent application ownership

`PLATFORM_ADMIN` is the application's highest role. It grants all application permissions,
cross-company access, subscription administration and ProjectAdmin/observability access.
There is no additional application `SUPER_ADMIN` role.

The non-default `grafirio-platform-owners` group supplies `ADMIN` and `PLATFORM_ADMIN`.
The `business_roles` mapper uses `aggregate.attrs=true`, combining account and group values.
This keeps application ownership independent of company membership synchronization, which
can remove the account's `business_roles` when removing its current company.
See [Keycloak mapper configuration](https://www.keycloak.org/admin-api/protocol-mappers).

Production owner: `fatih@grafirio.com`, user ID `3ebc7f32-9975-4b38-a3c6-2f8533adc9d3`.
The import creates only the empty group; it never grants ownership by email or creates a
password. `scripts/Ensure-PlatformOwner.ps1` checks both the approved UUID and email, preserves
existing account attributes and roles, and rejects unexpected group members/configuration.
Run without switches for read-only verification; `-Apply` applies the targeted configuration.
It reads the existing administrator credential from Azure without printing it.

Verified live on 2026-10-14: owner membership and generated example token include both roles;
a disabled disposable test account gets neither role without group membership, inherits both
without account role attributes, and retains them alongside a company `MEMBER` role. The test
account was deleted. Example token evaluation is not an interactive password login test.

These application permissions do not grant Keycloak `realm-admin`, master-realm administration,
Azure RBAC Owner, database login, or GitHub ownership. Application permissions do not bypass
data integrity checks or service-account-only authentication. A Keycloak administrator can
still disable/delete the account or remove owner-group membership; database loss requires
recovery. Ordinary application/company role changes cannot remove the inherited owner roles.
