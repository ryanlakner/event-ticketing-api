# Event Ticketing API

[![CI](https://github.com/ryanlakner/event-ticketing-api/actions/workflows/ci.yml/badge.svg)](https://github.com/ryanlakner/event-ticketing-api/actions/workflows/ci.yml)

A .NET 10 Web API for selling event tickets **without overselling**, even when many buyers go for the last seats at the same moment. It's built with **CQRS** and **Clean Architecture** on ASP.NET Core controllers, and deployed to **Azure App Service + Azure SQL** with **Terraform**.

Organizers create and publish events. Customers reserve seats, which are held for 10 minutes, and then confirm them. Holds that aren't confirmed expire, and their seats go back on sale.

| Concern        | Choice                                                                     |
| -------------- | -------------------------------------------------------------------------- |
| API            | ASP.NET Core controllers, RFC 9457 ProblemDetails, health checks           |
| Security       | Entra ID access tokens (JWT bearer), Organizer/Customer app roles, ownership checks |
| CQRS           | Lightweight in-house dispatcher (no MediatR licensing) + FluentValidation  |
| Consistency    | Optimistic concurrency tokens, automatic retry, database check constraints |
| Persistence    | EF Core 10 on Azure SQL with Entra ID (Managed Identity) auth, no passwords |
| API docs       | Swagger / Swashbuckle with XML doc comments                                |
| Observability  | OpenTelemetry → Application Insights (Log Analytics-backed)                |
| Quality gates  | CSharpier, Conventional Commits, Husky.Net git hooks, GitHub Actions CI    |
| Tests          | xUnit v3 on Microsoft.Testing.Platform, Shouldly, WebApplicationFactory, SQLite |
| IaC / CD       | Terraform (`azurerm` 5.x), GitHub Actions deploy with OIDC (no secrets)     |

## How overselling is prevented

A seat check is a classic read-modify-write race. Two requests both read "1 seat left", and both succeed. The API closes that gap in layers:

1. **The domain owns the rule.** `Event.Reserve()` is the only way to take seats. It checks that the event is published, hasn't started, and has enough seats, then increments `SeatsReserved`.
2. **Optimistic concurrency.** Every aggregate has a `Version` token that changes on every write. EF Core adds `WHERE Version = @original` to each `UPDATE`. So if two requests read the same version, only the first save succeeds and the second gets a `DbUpdateConcurrencyException`.
3. **Automatic retry.** `ConcurrencyRetry` catches the conflict, throws away the stale entities, and re-runs the whole command with jittered backoff. On the retry the rules are checked against fresh data, so the losing request either books a seat that's still free or gets a clean `409 This event is sold out`.
4. **Database backstop.** A `CHECK (SeatsReserved >= 0 AND SeatsReserved <= Capacity)` constraint rejects any write that would oversell, even one caused by a bug.

`Concurrent_reservations_never_oversell_an_event` sends 25 simultaneous requests for 5 seats through the real HTTP pipeline. It then checks that the seat counter matches the reservations table exactly. With the concurrency token removed, the test fails.

### Expiring holds

Pending reservations hold seats until `ExpiresAt`. Seats are released in two ways:

- **On demand.** When `ReserveTickets` finds too few seats, it first expires that event's lapsed holds. So correctness never depends on a background job.
- **Background sweep.** `ReservationExpiryService` runs `ExpireReservationsCommand` every minute. It's safe to run on every App Service instance at once, because overlapping sweeps just conflict and retry. Dev turns it off so the serverless database can auto-pause.

## CQRS flow

```
Controller ──► IDispatcher.SendAsync(command) ──► validators ──► ICommandHandler ──► aggregate ──► SaveChanges
           └─► IDispatcher.QueryAsync(query)  ──► validators ──► IQueryHandler   ──► AsNoTracking + SQL projection
```

- Commands change state through domain methods. Queries never load entities; they project straight to DTOs. Reservations are read through one joined read model (`ReservationWithEvent`), which both `GetReservationById` and `ListMyReservations` filter and sort before projecting in SQL.
- The `/api/me/...` endpoints take the user from the access token, never the URL, so there's no user ID to tamper with. All list queries share one set of paging rules (`IPagedQuery`) and one helper that requires an ordered query before paging.
- The dispatcher runs every `IValidator<T>` before the handler. Handlers and validators are found by assembly scanning, so a new feature needs no DI wiring.
- Errors are mapped to problem details: validation → **400**, not signed in → **401**, not allowed → **403**, not found → **404**, and business-rule or concurrency conflicts → **409**.

Each use case is a single file with its request, validator, and handler (for example [`ReserveTickets.cs`](Ticketing.Application/Reservations/Commands/ReserveTickets.cs)).

## Authentication and roles

Callers sign in with **Microsoft Entra ID**. The API validates each request's access token with ASP.NET Core's JWT bearer handler and reads two app roles from its `roles` claim:

| Role | Can |
| ---- | --- |
| *(anyone, signed in or not)* | Browse published and cancelled events |
| `Organizer` | Create events, and update, publish, or cancel **their own** events; see and cancel reservations for their events |
| `Customer` | Reserve seats, and see, confirm, or cancel **their own** reservations |

Authorization happens in two layers:

- **Roles at the edge.** Every endpoint requires a signed-in user by default (a fallback policy). Only public reads are marked `[AllowAnonymous]`, and each write carries `[Authorize(Roles = ...)]`. A missing or invalid token gets **401**, and the wrong role gets **403**.
- **Ownership in the application layer.** Events record their `OrganizerId`, and reservations their `CustomerId`. Both come from the token's `oid` claim (Entra ID's stable user ID). Handlers enforce ownership through an `ICurrentUser` abstraction, with the rules in one place ([`EventAccess`](Ticketing.Application/Events/EventAccess.cs)):
  - Changing another organizer's published event → **403**.
  - **Drafts are invisible** to everyone except their organizer → **404**.
  - Someone else's reservation → **404**, so its existence isn't revealed.

Token validation **fails closed**. Issuer and audience checks are always on, so an environment with missing settings rejects every token rather than accepting any. Each environment has its own app registration, so a dev token is never valid in prod.

### Trying it locally, no Entra tenant needed

`dotnet user-jwts` issues development tokens that the same JWT bearer setup accepts:

```bash
dotnet user-jwts create --project Ticketing.Api --name organizer@example.com --role Organizer --output token
dotnet user-jwts create --project Ticketing.Api --name customer@example.com --role Customer --output token
```

Paste a token into Swagger UI's **Authorize** button, or into [`Ticketing.Api.http`](Ticketing.Api/Ticketing.Api.http). The signing key lives in your user secrets, and `appsettings.Development.json` holds only the local issuer and audiences.

### Real Entra ID tokens

`infra/bootstrap` creates an app registration for each environment, with the `Organizer` and `Customer` app roles and an `access_as_user` scope. Whoever runs the bootstrap is granted both roles in `dev` and `qa`. Assign roles to other people under **Entra ID → Enterprise applications → ticketing-api-&lt;env&gt; → Users and groups**. The Azure CLI is pre-authorized, so getting a token is one command:

```bash
az login --allow-no-subscriptions
az account get-access-token --scope "api://<API_CLIENT_ID>/access_as_user" --query accessToken -o tsv
```

### Signing in from Swagger UI

In environments that expose Swagger (`dev` and `qa`), the **Authorize** button offers **Sign in with Entra ID** alongside pasting a token. Swagger UI runs the **authorization code flow with PKCE** as a public single-page-app client, so no client secret exists anywhere.

The bootstrap creates a separate Swagger UI app registration for each environment. It's pre-authorized for the API's `access_as_user` scope, so users see no consent prompt. Its redirect URI is the environment's `https://app-ticketing-<env>-api.azurewebsites.net/swagger/oauth2-redirect.html`, and in `dev` it also allows `localhost`. The API advertises the sign-in option only when an environment sets `Swagger:SignIn:*`. Otherwise, as with local `dotnet user-jwts` tokens, only the paste-a-token option appears.

## Endpoints

| Method | Route                              | Who                     | Description                                     |
| ------ | ---------------------------------- | ----------------------- | ----------------------------------------------- |
| GET    | `/api/events`                      | Anyone                  | Paged list (`page`, `pageSize`, `search`, `status`) |
| GET    | `/api/events/{id}`                 | Anyone                  | Event with live seat availability               |
| POST   | `/api/events`                      | Organizer               | Create a draft event → 201                      |
| PUT    | `/api/events/{id}`                 | Organizer (owner)       | Update details → 204                            |
| POST   | `/api/events/{id}/publish`         | Organizer (owner)       | Open for reservations → 204                     |
| POST   | `/api/events/{id}/cancel`          | Organizer (owner)       | Cancel the event and its reservations → 204     |
| POST   | `/api/events/{id}/reservations`    | Customer                | Hold seats → 201 + `Location`                   |
| GET    | `/api/reservations/{id}`           | Its customer or organizer | Reservation with event details                |
| GET    | `/api/me/events`                   | Organizer               | The caller's events, drafts included (`page`, `pageSize`, `status`) |
| GET    | `/api/me/reservations`             | Customer                | The caller's reservations, soonest event first (`page`, `pageSize`, `status`) |
| POST   | `/api/reservations/{id}/confirm`   | Its customer            | Confirm before the hold expires → 204           |
| POST   | `/api/reservations/{id}/cancel`    | Its customer or organizer | Cancel and release seats → 204                |
| GET    | `/health`                          | Anyone                  | Liveness + database check                       |

## Project layout

```
Ticketing.Domain/                Aggregates (Event, Reservation) and business rules. No dependencies.
Ticketing.Application/           Commands, queries, validators, dispatcher, concurrency retry.
Ticketing.Infrastructure/        EF Core DbContext, mappings, check constraints, migrations.
Ticketing.Api/                   Controllers, error mapping, Swagger, expiry background job.
Ticketing.Domain.Tests/          Pure business-rule tests.
Ticketing.Application.Tests/     Handlers through the real dispatcher against SQLite.
Ticketing.Api.IntegrationTests/  HTTP tests against the real host, including concurrency.
Ticketing.Testing/               Shared SQLite test database helper.
infra/                           Terraform for the app's Azure resources; bootstrap/ for one-time setup.
.husky/                          Git hooks and task runner config.
```

## Getting started

**Prerequisites:** .NET SDK 10.0.4xx (pinned in `global.json`) and Docker. For deployment, you also need Terraform ≥ 1.9 and the Azure CLI.

```bash
dotnet restore              # also restores local tools and installs the Husky git hooks
docker compose up -d        # local SQL Server on localhost:1433
dotnet run --project Ticketing.Api
```

Swagger UI opens at <http://localhost:5084/swagger>. In Development, the database is migrated on startup. [`Ticketing.Api.http`](Ticketing.Api/Ticketing.Api.http) walks through the full create → publish → reserve → confirm flow.

```bash
dotnet test                 # 40+ tests; no Docker or SQL Server required
```

Tests use file-backed SQLite with the production EF model. That way concurrency tokens, check constraints, and truly parallel requests are all exercised. EF's InMemory provider supports none of those.

### Migrations

```bash
dotnet ef migrations add <Name> \
  --project Ticketing.Infrastructure --startup-project Ticketing.Api \
  --output-dir Persistence/Migrations
```

CI fails if the model has changes with no matching migration.

### API contract

[`openapi/ticketing-api.v1.json`](openapi/ticketing-api.v1.json) is the committed OpenAPI document. `OpenApiContractTests` fails if it no longer matches what the API serves, so every contract change shows up as a reviewable diff. Clients such as [event-ticketing-web](https://github.com/ryanlakner/event-ticketing-web) generate their types from it. After an intended change, refresh it with:

```bash
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project Ticketing.Api.IntegrationTests
``` Every CI run also publishes a **`migrations-sql` artifact**. It's an idempotent T-SQL script covering all migrations, and it's safe to run against a database at any version. You can review schema changes in it as plain SQL, or apply it without EF tooling. To generate it locally:

```bash
dotnet ef migrations script --idempotent \
  --project Ticketing.Infrastructure --startup-project Ticketing.Api \
  --output artifacts/migrations.sql
```

## Code style, commits, and git hooks

CSharpier (`.csharpierrc.json`) is the only formatter. Husky.Net hooks install automatically on `dotnet restore`:

| Hook         | Runs                                                              |
| ------------ | ----------------------------------------------------------------- |
| `pre-commit` | `csharpier format` on staged files, then re-stages them           |
| `commit-msg` | Rejects messages that don't follow Conventional Commits           |
| `pre-push`   | `csharpier check .`, `dotnet build -warnaserror`, `dotnet test`   |

Set `HUSKY=0` to skip installing the hooks (CI does this).

### Commit messages

Commits follow [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/):

```
<type>(<optional scope>)<optional !>: <description>

<optional body>

<optional footers, e.g. BREAKING CHANGE: ...>
```

| Type | Use for |
| ---- | ------- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs`, `style`, `refactor`, `perf`, `test` | Changes that don't alter behavior, or only speed it up |
| `build`, `ci`, `chore` | Tooling, pipelines, dependencies, housekeeping |
| `revert` | Reverting an earlier commit |

Add `!` after the type or scope, or a `BREAKING CHANGE:` footer, to flag a breaking change. For example: `feat(reservations): let customers cancel pending holds`.

[`.husky/csx/commit-lint.csx`](.husky/csx/commit-lint.csx) enforces the rules. The `commit-msg` hook and CI both run it, so commits made on github.com or with `--no-verify` are still checked, and so are pull request titles, which become the commit when squash merging. Git's own `Merge`/`Revert` messages and `fixup!`/`squash!` commits are allowed through.

## Azure infrastructure

`infra/` provisions, inside a resource group created by `infra/bootstrap`:

- Log Analytics workspace + workspace-based Application Insights
- Azure SQL logical server (**Entra ID-only auth**) + serverless General Purpose database (auto-pauses outside prod)
- Linux App Service plan + Web App (.NET 10) with a **user-assigned managed identity**, HTTPS only, TLS 1.2, FTPS disabled, and `/health` wired to the platform health check
- Diagnostic settings that send App Service logs to Log Analytics

## Deployment (GitHub Actions)

[`deploy.yml`](.github/workflows/deploy.yml) deploys **dev automatically after CI passes on `main`**. Other environments are promoted by hand from the Actions tab (**Run workflow**). It signs in to Azure with **OIDC federated credentials**, so no secrets are stored in GitHub.

| Environment | Deployed | Purpose | Sizing |
| ----------- | -------- | ------- | ------ |
| `dev` | Automatically, on every green `main` | Shared development | B1 App Service, serverless SQL that auto-pauses, Swagger on, expiry sweep off |
| `qa` | On demand | Functional testing | Dev-sized, Swagger on, expiry sweep on so holds expire on schedule |
| `stg` | On demand, with reviewers | Pre-production rehearsal | Same SKUs and settings as prod; the database may pause when idle |
| `prod` | On demand, with reviewers | Production | P0v3 App Service, always-on SQL, Swagger off |

Each environment has its own `infra/environments/<env>.tfvars`, Terraform state file, resource group, deploy identity, and GitHub environment. To add another environment, add it everywhere the list appears: the tfvars file, the validation in `infra/variables.tf`, the bootstrap `environments` default, and the workflow's `options`. CI fails if those lists drift apart.

```
build ──► infrastructure ──► deploy
  │           │                 ├─ open SQL firewall for this runner
  │           │                 ├─ apply migrations (EF bundle)
  │           │                 ├─ grant the API identity database access (idempotent)
  │           │                 ├─ close firewall (always)
  │           │                 ├─ zip deploy to App Service
  │           │                 └─ smoke test /health
  │           └─ terraform plan + apply
  └─ dotnet publish + self-contained migrations bundle
```

- **Least privilege.** Each environment has its own deploy identity, trusted only for jobs bound to that GitHub environment (`repo:…:environment:dev`). It's **Contributor on its own resource group** only, not the subscription.
- **No Graph permissions.** The API's database user is created `WITH SID` from its managed identity's client ID ([`grant-app-identity.sql`](infra/sql/grant-app-identity.sql)). Azure SQL never needs Directory Readers.
- **Migrations before code.** Migrations run before the new version ships, so they must stay backward compatible with the version that's live (expand first, then contract in a later release).
- **Safe by default.** Deploys to the same environment are queued, never cancelled mid-apply. The workflow is skipped until the bootstrap below sets `DEPLOY_ENABLED`.

### One-time setup

Run this as a subscription Owner who can also create Entra ID app registrations (for example, Application Administrator). Do `az login` and `gh auth login` first:

```bash
cd infra/bootstrap
terraform init
terraform apply -var="subscription_id=<SUBSCRIPTION_ID>"   # all four; limit with -var='environments=["dev"]'
./configure-github.sh
```

The bootstrap creates:

- the Terraform state storage account (Entra ID access only, versioned)
- a resource group and deploy identity for each environment
- the OIDC federated credentials and role assignments
- the resource provider registrations the deploy identity can't do itself
- an Entra ID app registration for each environment, with the `Organizer` and `Customer` app roles, plus sign-in clients for Swagger UI and the web app

To let the deployed web app call the API, pass its origin with `-var='web_origins={dev=["https://<web host>"]}'`. That one setting drives both the web app's sign-in redirect and the API's CORS allow-list.

`configure-github.sh` then creates the GitHub environments and sets their variables. It also prints the values the web app needs. None of them are secrets. For **stg** and **prod**, add required reviewers under *Settings → Environments* so those deploys wait for approval.

Until the bootstrap runs, the Deploy workflow is **skipped** on every push rather than failing, so the repo works fine with no Azure subscription.

The deploy identity is the Azure SQL Entra admin, so the pipeline can run migrations. To query the database yourself, set `TF_VAR_sql_entra_admin_*` to an Entra group that contains both you and the deploy identity.

### Deploying by hand

Normally the workflow does all of this. To run the same steps locally:

```bash
cd infra
cp backend.hcl.example backend.hcl          # fill in from `terraform -chdir=bootstrap output`
terraform init -backend-config=backend.hcl
terraform apply -var-file=environments/dev.tfvars -var="subscription_id=<SUBSCRIPTION_ID>" \
  -var="sql_entra_admin_login=<name>" -var="sql_entra_admin_object_id=<object id>" \
  -var="api_client_id=<environments.dev.api_client_id from bootstrap>"
```

Then add your IP to `sql_allowed_ip_addresses` and run the migrations. The bundle reads its connection string from configuration, so pass it as an environment variable:

```bash
HUSKY=0 dotnet ef migrations bundle --project Ticketing.Infrastructure \
  --startup-project Ticketing.Api -o efbundle --force
ConnectionStrings__Database="Server=tcp:$(terraform -chdir=infra output -raw sql_server_fqdn),1433;Database=$(terraform -chdir=infra output -raw sql_database_name);Authentication=Active Directory Default;Encrypt=True;" ./efbundle
```

Alternatively, apply the `migrations-sql` artifact from a CI run: `sqlcmd -S <sql_server_fqdn> -d <sql_database_name> --authentication-method ActiveDirectoryAzCli -i migrations.sql`. Grant the API identity access with [`grant-app-identity.sql`](infra/sql/grant-app-identity.sql) (see its header for the `sqlcmd` command). Then deploy the app:

```bash
HUSKY=0 dotnet publish Ticketing.Api -c Release -o publish
(cd publish && zip -qr ../api.zip .)
az webapp deploy -g "$(terraform -chdir=infra output -raw resource_group_name)" \
  -n "$(terraform -chdir=infra output -raw api_app_name)" --src-path api.zip --type zip
```

### Configuration

| Setting                                  | Default    | Purpose                                         |
| ---------------------------------------- | ---------- | ----------------------------------------------- |
| `ConnectionStrings:Database`             | —          | SQL connection; set by Terraform in Azure       |
| `Reservations:HoldDuration`              | `00:10:00` | How long unconfirmed seats are held             |
| `Reservations:ExpirySweepEnabled`        | `true`     | Background release of lapsed holds              |
| `Reservations:ExpirySweepInterval`       | `00:01:00` | How often the sweep runs                        |
| `Database:ApplyMigrationsOnStartup`      | `false`    | `true` in Development                           |
| `Swagger:Enabled`                        | `false`    | Always on in Development                        |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`  | —          | Turns on Azure Monitor OpenTelemetry export     |
| `Authentication:Schemes:Bearer:*`        | —          | Token authority, issuer, and audiences; set by Terraform in Azure, by `dotnet user-jwts` locally |
| `Swagger:SignIn:*`                       | —          | Swagger UI's Entra ID sign-in: client ID, authorize and token URLs, and scope; set by Terraform where Swagger is on |
| `Cors:AllowedOrigins`                    | `[]`       | Browser origins allowed to call the API (the web app); set from the bootstrap's `web_origins` |

## Roadmap

See [ROADMAP.md](ROADMAP.md) for what's done and what's next, phase by phase. Each item has a "done when" checklist and the Conventional Commit type to use.
