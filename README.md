# Pirouette

A multi-tenant dance studio management system in ASP.NET Core, built around scheduling,
enrollment, and attendance.

[![CI](https://github.com/msmithstern/pirouette/actions/workflows/ci.yml/badge.svg)](https://github.com/msmithstern/pirouette/actions/workflows/ci.yml)

> **Status: in development.** The data layer, multi-tenancy, and test infrastructure are
> complete and covered by tests. The user interface is not yet built — see
> [Roadmap](#roadmap) for what exists and what doesn't.

---

## The problem

The project started from a real complaint: a working dance studio uses commercial management
software and finds it slow. That software handles scheduling, attendance, and tuition billing —
a domain with more depth than it first appears.

Dance studio scheduling isn't CRUD. Classes recur weekly but deviate constantly: snow days,
substitute teachers, room changes, one-off recital rehearsals, makeup sessions. Rosters change
mid-term, which makes "who was enrolled?" a question about a point in time rather than a
current fact. Tuition involves proration, sibling discounts, and drop credits. And older
students frequently teach younger ones, which turns out to break the obvious data model
entirely.

## Current status

**Built and tested:**

- Domain model for studios, terms, and rooms, with invariants enforced at construction
- Multi-tenant data access — global query filters and write-side tenant stamping
- Integration tests proving tenant isolation against a real PostgreSQL instance
- EF Core migrations, Docker Compose for local development, CI on every push

**Not built yet:** classes, sessions, scheduling, enrollment, attendance, authentication, and
the user interface. The application currently starts and serves the default Blazor template.

## Architecture

Four projects, with the dependency direction enforced by the compiler rather than convention:

```
Pirouette.Domain            Entities and business rules. Zero package references.
Pirouette.Infrastructure    EF Core, migrations, tenancy. Depends on Domain.
PirouetteApp                Blazor Web App. Depends on both.
Pirouette.Domain.Tests      Fast, no database.
Pirouette.Integration.Tests Real PostgreSQL via Testcontainers.
```

`Pirouette.Domain` having no dependencies is the load-bearing constraint. Scheduling logic —
recurrence expansion, conflict detection — is pure functions over dates, so it can be tested
exhaustively in milliseconds without a database. Adding a package reference there is the signal
that something has been put in the wrong project.

## Design decisions

The reasoning behind the model is written up in
**[docs/PROJECT_PLAN.md](docs/PROJECT_PLAN.md)**. The three that shaped everything else:

### Recurring definitions are separate from concrete occurrences

A class is both *"Intermediate Ballet, Tuesdays 4–5pm"* and *"the session on October 13th that
was moved to Studio B because the floor was being refinished."* A meeting pattern is treated as
a **generator, not a constraint**: it emits dated `ClassSession` rows, and each row is then an
independent fact that can be cancelled, moved, or reassigned.

This makes irregularity fall out for free. A workshop series with no pattern at all is just a
class nobody generated sessions for. The hard part is regeneration — editing a pattern in week 9
must not destroy eight weeks of attendance history — so sessions carry provenance and
regeneration reconciles rather than rebuilds.

### One `Member` per person, with roles as time-bounded facts

Modelling `Student` and `Instructor` as separate entities breaks the moment a teenager teaches
the beginner class: two rows for one human, two birthdays to keep in sync, no way to know
they're the same person.

Instead there is one `Member` per human, and roles are separate and dated. Teaching is a
`ClassAssignment` (per class, with a role and effective dates) rather than a single
`InstructorId` foreign key — which also expresses co-taught classes, one-day substitutes, and
mid-term instructor changes, none of which a single FK can.

This has a security consequence. A 16-year-old assistant needs the roster for the class they
teach and must not see classmates' home addresses or medical notes. Row-level scoping can't
express that, so permissions are two axes: **which classes** (from assignments) and **which
fields** (from role tier), enforced by returning audience-specific projections rather than
entities.

### Multi-tenancy from the first migration

Every tenant-owned entity implements `ITenantOwned`, and query filters are applied by reflection
over that interface — so implementing it is sufficient, with no list to remember to update.

Query filters are read-time only, so a `SaveChanges` interceptor stamps the tenant on insert.
Nothing in the database schema enforces any of this; the migration contains no trace of
tenancy. The guarantee is entirely an application-level promise, which is why it's covered by
tests that seed two studios and assert one cannot see the other — including a deliberate
`IgnoreQueryFilters()` case, since otherwise a passing isolation test is indistinguishable from
the write having silently failed.

## Tech stack

.NET 10 · ASP.NET Core Blazor · Entity Framework Core · PostgreSQL · xUnit · Testcontainers ·
Docker Compose · GitHub Actions

## Running locally

Requires the .NET 10 SDK and Docker.

```bash
git clone git@github.com:msmithstern/pirouette.git
cd pirouette

docker compose up -d

dotnet user-secrets set "ConnectionStrings:Pirouette" \
  "Host=localhost;Port=5432;Database=pirouette;Username=pirouette;Password=localdev" \
  --project PirouetteApp

dotnet ef database update \
  --project Pirouette.Infrastructure \
  --startup-project PirouetteApp

dotnet run --project PirouetteApp
```

## Tests

```bash
dotnet test
```

Domain tests run in-memory with no database. Integration tests start a disposable PostgreSQL
container via Testcontainers and apply the real migrations to it, so the schema under test is
the one the migrations actually produce — a broken migration fails the suite.

## Roadmap

| | Milestone |
|---|---|
| ✅ | Foundation — projects, tenancy, migrations, CI |
| ⬜ | Core entities and CRUD — members, classes, assignments |
| ⬜ | Session generation from recurring patterns |
| ⬜ | Schedule exceptions — cancellations, reschedules, substitutes |
| ⬜ | Conflict detection — room, instructor, student, capacity |
| ⬜ | Calendar view |
| ⬜ | Enrollment with effective dates |
| ⬜ | Authentication, roles, and field-level privacy |
