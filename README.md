# Event Ticketing API

[![CI](https://github.com/ryanlakner/event-ticketing-api/actions/workflows/ci.yml/badge.svg)](https://github.com/ryanlakner/event-ticketing-api/actions/workflows/ci.yml)

A .NET 10 Web API for selling event tickets **without overselling**, even when many buyers go for the last seats at the same moment. It's built with **CQRS** and **Clean Architecture** on ASP.NET Core controllers, and deployed to **Azure App Service + Azure SQL** with **Terraform**.

Organizers create and publish events. Customers reserve seats, which are held for 10 minutes, and then confirm them. Holds that aren't confirmed expire, and their seats go back on sale.

| Concern        | Choice                                                                     |
| -------------- | -------------------------------------------------------------------------- |
| API            | ASP.NET Core controllers, RFC 9457 ProblemDetails, health checks           |
| CQRS           | Lightweight in-house dispatcher (no MediatR licensing) + FluentValidation  |
| Consistency    | Optimistic concurrency tokens, automatic retry, database check constraints |
| Persistence    | EF Core 10 on Azure SQL with Entra ID (Managed Identity) auth, no passwords |
| API docs       | Swagger / Swashbuckle with XML doc comments                                |
| Observability  | OpenTelemetry → Application Insights (Log Analytics-backed)                |
| Quality gates  | CSharpier + Husky.Net git hooks, GitHub Actions CI                         |
| Tests          | xUnit v3 on Microsoft.Testing.Platform, Shouldly, WebApplicationFactory, SQLite |
| IaC            | Terraform (`azurerm` 5.x)                                                  |

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

- Commands change state through domain methods. Queries never load entities; they project straight to DTOs. `GetReservationById`, for example, joins in event details as its own read model.
- The dispatcher runs every `IValidator<T>` before the handler. Handlers and validators are found by assembly scanning, so a new feature needs no DI wiring.
- Errors are mapped to problem details: validation → **400**, not found → **404**, and business-rule or concurrency conflicts → **409**.

Each use case is a single file with its request, validator, and handler (for example [`ReserveTickets.cs`](Ticketing.Application/Reservations/Commands/ReserveTickets.cs)).

## Endpoints

| Method | Route                              | Description                                     |
| ------ | ---------------------------------- | ----------------------------------------------- |
| GET    | `/api/events`                      | Paged list (`page`, `pageSize`, `search`, `status`) |
| GET    | `/api/events/{id}`                 | Event with live seat availability               |
| POST   | `/api/events`                      | Create a draft event → 201                      |
| PUT    | `/api/events/{id}`                 | Update details → 204                            |
| POST   | `/api/events/{id}/publish`         | Open for reservations → 204                     |
| POST   | `/api/events/{id}/cancel`          | Cancel the event and its reservations → 204     |
| POST   | `/api/events/{id}/reservations`    | Hold seats → 201 + `Location`                   |
| GET    | `/api/reservations/{id}`           | Reservation with event details                  |
| POST   | `/api/reservations/{id}/confirm`   | Confirm before the hold expires → 204           |
| POST   | `/api/reservations/{id}/cancel`    | Cancel and release seats → 204                  |
| GET    | `/health`                          | Liveness + database check                       |

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
infra/                           Terraform for all Azure resources.
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

CI fails if the model has changes with no matching migration. Every CI run also publishes a **`migrations-sql` artifact**. It's an idempotent T-SQL script covering all migrations, and it's safe to run against a database at any version. You can review schema changes in it as plain SQL, or apply it without EF tooling. To generate it locally:

```bash
dotnet ef migrations script --idempotent \
  --project Ticketing.Infrastructure --startup-project Ticketing.Api \
  --output artifacts/migrations.sql
```

## Code style and git hooks

CSharpier (`.csharpierrc.json`) is the only formatter. Husky.Net hooks install automatically on `dotnet restore`:

| Hook         | Runs                                                            |
| ------------ | --------------------------------------------------------------- |
| `pre-commit` | `csharpier format` on staged files, then re-stages them         |
| `pre-push`   | `csharpier check .`, `dotnet build -warnaserror`, `dotnet test` |

Set `HUSKY=0` to skip installing the hooks (CI does this).

## Azure infrastructure

`infra/` provisions:

- Resource group
- Log Analytics workspace + workspace-based Application Insights
- Azure SQL logical server (**Entra ID-only auth**) + serverless General Purpose database (auto-pauses in dev)
- Linux App Service plan + Web App (.NET 10) with a **system-assigned managed identity**, HTTPS only, TLS 1.2, FTPS disabled, and `/health` wired to the platform health check
- Diagnostic settings that send App Service logs to Log Analytics

### Deploy

```bash
cd infra
cp backend.hcl.example backend.hcl        # point at your state storage account
az login
terraform init -backend-config=backend.hcl
terraform apply -var-file=environments/dev.tfvars -var="subscription_id=<SUBSCRIPTION_ID>"
```

**1. Grant the API's managed identity access to the database** (one time per environment). Connect as the Entra SQL admin, for example with `sqlcmd -G` or Azure Data Studio. Then run [`infra/sql/grant-app-identity.sql`](infra/sql/grant-app-identity.sql).

**2. Apply migrations.** Add your IP to `sql_allowed_ip_addresses`, then run:

```bash
HUSKY=0 dotnet ef migrations bundle --project Ticketing.Infrastructure \
  --startup-project Ticketing.Api -o efbundle --force
./efbundle --connection "Server=tcp:$(terraform -chdir=infra output -raw sql_server_fqdn),1433;Database=$(terraform -chdir=infra output -raw sql_database_name);Authentication=Active Directory Default;Encrypt=True;"
```

Alternatively, download the `migrations-sql` artifact from a CI run and apply it: `sqlcmd -S <sql_server_fqdn> -d <sql_database_name> -G -i migrations.sql`.

**3. Deploy the app:**

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

## Roadmap

- Entra ID authentication with organizer and customer roles
- Idempotency keys on `POST /reservations` so client retries never double-book
- Azure Service Bus for confirmation emails (outbox pattern)
- GitHub Actions deployment with OIDC federated credentials
