# Roadmap

A plan for building out the Event Ticketing API, one reviewable step at a time. Each item says **why** it's worth doing, **what done looks like**, and **where** in the codebase it lands.

## How to use this file

1. Pick the first unchecked item in the earliest unfinished phase. Items within a phase are in suggested order, but most are independent.
2. Branch, build it, and open a pull request. Use the Conventional Commit type shown next to the item, for example `feat(reservations): ...`.
3. When it merges, tick the box here in the same PR, so the roadmap never drifts from the code.

Sizes are rough effort: **S** is an evening, **M** is a few sessions, and **L** is a multi-part feature worth splitting into several PRs.

---

## ✅ Phase 0 — Foundation (done)

- [x] Clean Architecture solution (Domain, Application, Infrastructure, Api) on .NET 10
- [x] CQRS with an in-house dispatcher, FluentValidation, and a single-file-per-use-case layout
- [x] Event and Reservation aggregates; seat holds that expire on demand and via a background sweep
- [x] Overselling prevented with optimistic concurrency, automatic retry, and a database check constraint, proven by a 25-buyer concurrency test
- [x] EF Core on Azure SQL with Managed Identity; migrations, a pending-changes check, and a SQL script artifact in CI
- [x] Entra ID authentication with `Organizer`/`Customer` roles and ownership rules; `/api/me` endpoints
- [x] Swagger UI, with paste-a-token and Entra ID sign-in (PKCE)
- [x] Web client support: CORS, a web sign-in app registration, and a committed OpenAPI contract for [event-ticketing-web](https://github.com/ryanlakner/event-ticketing-web)
- [x] Terraform for App Service, SQL, and monitoring; a bootstrap for state, identities, and app registrations
- [x] GitHub Actions: CI, plus OIDC deploys to `dev`, `qa`, `stg`, and `prod` (skipped until the bootstrap runs)
- [x] CSharpier, Husky.Net hooks, and enforced Conventional Commits

---

## Phase 1 — Correctness at the HTTP edge

Close the remaining ways a well-behaved client can still get a wrong result.

- [ ] **Idempotency keys on `POST /api/events/{id}/reservations`** · `feat(reservations)` · M
  - *Why:* a client that times out and retries can currently book twice. This is the classic companion to the overselling story.
  - *Done when:*
    - [ ] An `Idempotency-Key` header is accepted, and a repeat with the same key and user returns the original `201` response, including the same reservation.
    - [ ] Reusing a key with a different body returns `422`.
    - [ ] Keys are stored in an `IdempotencyKeys` table (key, user, request hash, response, expiry) with a unique index, and lapsed keys are cleaned up by the existing sweep.
    - [ ] A test replays the same request concurrently and gets exactly one reservation.
  - *Where:* a new dispatcher behavior or an MVC filter in `Ticketing.Api`, a new entity and migration in `Ticketing.Infrastructure`.

- [ ] **HTTP concurrency with `ETag` / `If-Match` on `PUT /api/events/{id}`** · `feat(events)` · S
  - *Why:* two organizers' tabs can overwrite each other's edits. The `Version` token already exists, so this just exposes it over HTTP.
  - *Done when:* `GET` returns an `ETag` built from `Version`. `PUT` without `If-Match` returns `428`, and a stale value returns `412`. Tests cover all three.
  - *Where:* `EventsController`, `UpdateEventCommand` (add `ExpectedVersion`), and `EventDto`.

- [ ] **Rate limiting on reservations** · `feat(api)` · S
  - *Why:* one account shouldn't be able to hammer the booking endpoint during an on-sale.
  - *Done when:* ASP.NET Core's rate limiter applies a per-user token bucket to the reservation endpoint and returns `429` with `Retry-After`. The limits come from configuration, and a test covers it.
  - *Where:* `Program.cs`, `EventsController`.

- [ ] **API versioning** · `feat(api)` · S
  - *Why:* the next breaking change shouldn't break existing clients.
  - *Done when:* `Asp.Versioning.Mvc` is in place, routes are `/api/v1/...`, Swagger shows a document per version, and the README documents the versioning policy.

---

## Phase 2 — Testing and code quality

Make the safety net match production more closely, and make the architecture enforce itself.

- [ ] **Integration tests on real SQL Server (Testcontainers)** · `test` · M
  - *Why:* SQLite needs a `DateTimeOffset` workaround and behaves differently from SQL Server under locking. Real SQL Server removes that doubt.
  - *Done when:*
    - [ ] A SQL Server container runs the concurrency and reservation tests in CI.
    - [ ] Migrations, not `EnsureCreated`, build the schema, so the tests also check the migrations.
    - [ ] Local runs fall back to SQLite when Docker isn't available.
  - *Where:* `Ticketing.Testing`, `.github/workflows/ci.yml`.

- [ ] **Architecture tests** · `test` · S
  - *Why:* the dependency rules (Domain depends on nothing, and Application doesn't depend on Api or Infrastructure) are only a convention today.
  - *Done when:* a `Ticketing.ArchitectureTests` project uses NetArchTest to assert the layer dependencies, that handlers are `internal sealed`, and that controllers only depend on `IDispatcher`.

- [ ] **Code coverage in CI** · `ci` · S
  - *Done when:* coverage is collected with Microsoft.Testing.Platform's coverage extension, summarized on each run, and shown as a README badge, with a minimum threshold that fails the build.

- [x] **OpenAPI snapshot test** · `test` · S
  - *Why:* catches accidental API contract changes in review, and gives clients a file to generate types from.
  - *Done:* `OpenApiContractTests` keeps [`openapi/ticketing-api.v1.json`](openapi/ticketing-api.v1.json) identical to the served document. `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test` refreshes it, and changes show up as a diff in the PR.

- [ ] **Mutation testing** · `test` · M
  - *Done when:* Stryker.NET runs against `Ticketing.Domain` and `Ticketing.Application` in a weekly workflow, and the score is recorded in the README.

- [ ] **Load test for an on-sale burst** · `test` · M
  - *Done when:* a k6 script (or Azure Load Testing) simulates thousands of buyers hitting one event, and the results in `docs/` include p95 latency, the conflict rate, and zero oversells.

---

## Phase 3 — Richer ticketing domain

Grow the business rules; this is where CQRS earns its keep.

- [ ] **Ticket tiers and pricing** · `feat(events)` · L
  - *Why:* real events sell General Admission, VIP, and so on, each with its own capacity and price.
  - *Done when:*
    - [ ] An event has one or more `TicketTier`s, each with its own name, price, and capacity. Seat counting and the check constraint move to the tier.
    - [ ] Reservations pick a tier, and money is a `Money` value object (amount and currency).
    - [ ] The migration moves each existing event's capacity into a default tier.
  - *Where:* `Ticketing.Domain/Events`, all event and reservation commands, the DTOs, and a migration.

- [ ] **Waitlist when sold out** · `feat(reservations)` · M
  - *Done when:*
    - [ ] Customers can join a waitlist when a tier is sold out.
    - [ ] Released seats (cancelled or expired) go to the waitlist first come, first served, as a new pending hold, and the customer is notified (see Phase 4).
    - [ ] A test covers fairness under concurrency.

- [ ] **Cancellation policy** · `feat(reservations)` · S
  - *Done when:* customers can't cancel a confirmed reservation within a configurable window before the event starts (`409`), while organizers still can. The rule lives in the domain and is unit-tested.

- [ ] **Better event discovery** · `feat(events)` · S
  - *Done when:* `GET /api/events` supports `from`/`to` date filters and sorting by start time or availability, with indexes to match. Validation rejects inverted date ranges.

---

## Phase 4 — Events, messaging, and notifications on Azure

Tell the outside world what happened, reliably.

- [ ] **Domain events + transactional outbox** · `feat` · M
  - *Why:* notifying other systems must not be lost if the process crashes right after `SaveChanges`.
  - *Done when:*
    - [ ] Aggregates raise events such as `ReservationConfirmed` and `EventCancelled`.
    - [ ] `SaveChanges` writes them to an `OutboxMessages` table in the same transaction.
    - [ ] A background dispatcher publishes pending messages and marks them sent, with retry and poison handling.

- [ ] **Azure Service Bus** · `feat(infra)` · M
  - *Done when:*
    - [ ] Terraform creates a Service Bus namespace and topic per environment.
    - [ ] The API publishes outbox messages using its managed identity (the `Azure Service Bus Data Sender` role; no connection strings).
    - [ ] Local development uses the Service Bus emulator in `docker-compose.yml`.

- [ ] **Confirmation emails** · `feat(notifications)` · M
  - *Done when:*
    - [ ] An Azure Function (isolated worker, .NET 10) subscribes to `ReservationConfirmed` and sends an email through Azure Communication Services.
    - [ ] The Function is deployed by the same pipeline and authenticates with managed identity.
    - [ ] Messages carry an ID so redelivered ones don't send duplicate emails.

---

## Phase 5 — Delivery pipeline and infrastructure

Make shipping safer and cheaper to reason about.

- [ ] **Automated releases from Conventional Commits** · `ci` · S
  - *Why:* the commit history already encodes semver: `feat` is a minor release, `fix` a patch, and `!` a major.
  - *Done when:* release-please opens release PRs that bump the version and update `CHANGELOG.md`, and merging one tags a GitHub Release.

- [ ] **Terraform plan on pull requests** · `ci` · M
  - *Done when:* PRs that touch `infra/` get a plan for `dev`, posted as a PR comment by a read-only identity. `plan` fails the check if Terraform errors.

- [ ] **Build once, promote everywhere** · `ci` · M
  - *Why:* each environment currently rebuilds from source, so `prod` isn't the exact artifact `stg` tested.
  - *Done when:* one run builds versioned artifacts once, then promotes them `dev` → `qa` → `stg` → `prod` as chained jobs, with the GitHub environment approvals in between.

- [ ] **Zero-downtime deploys with slots** · `feat(infra)` · M
  - *Done when:* `stg` and `prod` deploy to a `staging` slot, warm up and pass `/health`, then swap. A failed smoke test leaves production untouched.

- [ ] **Private networking** · `feat(infra)` · L
  - *Done when:*
    - [ ] App Service uses VNet integration.
    - [ ] Azure SQL is reachable only through a private endpoint, with a private DNS zone.
    - [ ] Public network access is disabled, replacing the "Allow Azure services" firewall rule.
    - [ ] Migrations run from inside the network, for example on a self-hosted runner or a deployment job in the VNet.

- [ ] **Dependency updates** · `ci` · S
  - *Done when:* Dependabot (or Renovate) opens grouped weekly PRs for NuGet, GitHub Actions, and Terraform providers, with Conventional Commit titles so they pass the commit check.

- [ ] **Container image** · `build` · S
  - *Done when:* a multi-stage `Dockerfile` for the API is built and scanned in CI. Optionally, a Container Apps deployment exists as an alternative to App Service.

---

## Phase 6 — Observability and operations

Know it's healthy before a customer tells you.

- [ ] **Business metrics** · `feat(observability)` · S
  - *Done when:* an OpenTelemetry `Meter` records reservations created, sold-out conflicts, concurrency retries, and holds expired, and they're visible in Application Insights.

- [ ] **Alerts as code** · `feat(infra)` · S
  - *Done when:* Terraform defines Azure Monitor alerts for a 5xx rate, a failing health check, a spike in concurrency retries, and database CPU, notifying an action group (email).

- [ ] **Liveness vs readiness** · `feat(api)` · S
  - *Done when:* `/health/live` doesn't touch the database but `/health/ready` does, and the App Service health check uses readiness.

- [ ] **Operations dashboard** · `feat(infra)` · M
  - *Done when:* an Azure Workbook, deployed by Terraform, shows traffic, latency, errors, bookings per minute, and seat utilization for each event.

---

## Phase 7 — Security hardening

- [ ] **Require the `access_as_user` scope** · `fix(auth)` · S
  - *Why:* delegated tokens should prove they were issued for this API's scope, not just carry a role.
  - *Done when:* a policy requires the `scp` claim to contain `access_as_user`, and tests cover a token with the role but no scope.

- [ ] **Security scanning in CI** · `ci` · S
  - *Done when:* CodeQL runs on PRs, and `dotnet list package --vulnerable --include-transitive` fails the build when a vulnerable package is found.

- [ ] **Security headers** · `feat(api)` · S
  - *Done when:* responses include HSTS, `X-Content-Type-Options`, and a Content Security Policy that still allows Swagger UI, with a test asserting them.

- [ ] **Least-privilege database access** · `fix(infra)` · S
  - *Done when:* the API identity has only the permissions it needs, instead of `db_datawriter` on everything. Migration rights stay with the deploy identity.

---

## Phase 8 — Clients (optional)

- [ ] **Generated typed client** · `feat(client)` · S
  - *Done when:* a Kiota- or NSwag-generated C#/TypeScript client is produced from `swagger.json` in CI and published as a build artifact.

- [x] **Web front end** · `feat(web)` · L
  - *Done:* lives in its own repo, [event-ticketing-web](https://github.com/ryanlakner/event-ticketing-web): React, TypeScript, and Vite, with MSAL sign-in and types generated from `openapi/ticketing-api.v1.json`. Deploying it is tracked in that repo's roadmap.
