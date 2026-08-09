# Pirouette — Project Plan

A dance studio management app in C#/.NET. Portfolio-first, built so that real deployment
stays possible later.

**Status:** planning
**Last updated:** 2026-08-08

---

## 1. Why this project

The origin is a real complaint: a working studio uses Dance Studio Pro and finds it slow.
That gives the project two things most portfolio apps lack — a real user with real
requirements, and a concrete quality thesis ("this should feel fast") that can be measured
rather than asserted.

The domain is also genuinely non-trivial. Recurring schedules, conflict detection, and
tuition proration are real problems with real edge cases. That is the difference between a
project that reads as "another CRUD app" and one that gives you something to talk about.

### Decisions already made

| Decision | Choice | Consequence |
|---|---|---|
| Goal | Portfolio now, deploy later | Multi-tenancy and auth designed in from day one; compliance, payments, and support deferred |
| First slice | Scheduling + classes | Everything else depends on it — attendance needs sessions, billing needs enrollments |
| Stack | Blazor Web App + EF Core | Pure C# top to bottom; matches the existing scaffold |
| Time budget | A few hours per week | ~16 weeks to MVP; every milestone must be independently shippable |

### Non-goals for v1

Explicitly out of scope. Write them down so they stop being tempting:

- Payment processing (see §6)
- Email/SMS notifications
- Costume, recital, and competition management
- Parent-facing portal
- Payroll or instructor compensation
- Reporting dashboards and analytics
- Mobile app

---

## 2. Current state

A .NET 10 Blazor Web App scaffold with interactive server rendering. One commit. No domain
code, no persistence, no auth.

```
Pirouette.slnx
PirouetteApp/
  Program.cs                 # AddRazorComponents + AddInteractiveServerComponents
  Components/                # App, Routes, MainLayout, Home, Error, NotFound
  Dockerfile
  appsettings.json
```

Everything below is additive to this.

---

## 3. Architecture

Keep it a single deployable web app. Do **not** split into microservices — for an app this
size that is complexity you'd have to defend in an interview without being able to.

Use projects rather than folders to enforce the dependency direction. The compiler becomes
the thing that stops you from calling the database out of a domain rule.

```
Pirouette.Domain          # Entities, value objects, pure business rules. No EF, no ASP.NET.
Pirouette.Infrastructure  # DbContext, EF configurations, migrations, repositories.
PirouetteApp              # Blazor components, DI wiring, endpoints.
Pirouette.Domain.Tests    # Fast, in-memory, no database.
Pirouette.Integration.Tests # Real Postgres via Testcontainers.
```

Dependency rule: `App → Infrastructure → Domain`. Domain depends on nothing.

**Why this matters here specifically:** session generation and conflict detection are pure
functions over dates. If they live in `Domain` with no EF reference, you can test hundreds
of edge cases in milliseconds without a database. That is the single highest-leverage
structural decision in this plan.

### Data access

EF Core with Npgsql against PostgreSQL, run locally via Docker Compose. Postgres over SQL
Server because the container is lighter, it's free to host anywhere, and Npgsql maps
`DateOnly`/`TimeOnly` cleanly.

Start with `DbContext` injected directly into Blazor components' backing services. Do not
build a generic repository layer — EF's `DbSet` already is one, and wrapping it adds
indirection you'll have to justify. Introduce focused services (`ScheduleService`,
`EnrollmentService`) only when logic accumulates.

> **Blazor Server caveat:** the default `AddDbContext` scoped lifetime is wrong for Blazor
> interactive server, because a component's scope lives as long as the user's circuit —
> potentially hours — and a single `DbContext` is not thread-safe across concurrent
> component renders. Use `AddDbContextFactory<PirouetteDbContext>()` and create a
> short-lived context per operation. This is a well-known Blazor trap and worth a line in
> your README.

---

## 4. Domain model — the scheduling slice

### The central design decision: definition vs. occurrence

A dance class is two different things at once. It is *"Intermediate Ballet, Tuesdays 4:00–5:00
PM, Studio A, Fall term"* — a recurring definition. And it is *"the session on
2026-10-13 that Ms. Chen taught, where 11 of 14 students showed up, moved to Studio B
because the floor was being refinished."*

Model both. Generate concrete `ClassSession` rows from the recurring `DanceClass`.

**Why materialize sessions instead of computing them on the fly:** you need a row to hang
things on. Attendance records. A cancellation for a snow day. A substitute instructor for
one date. A room change for one date. A holiday skip. Every one of those is an exception to
the pattern, and exceptions need somewhere to live. Computing occurrences dynamically works
right up until the first time reality deviates from the rule — which for a dance studio is
roughly week three.

### Entities

```
Studio                 Tenant root. Name, IANA timezone, changeover buffer.
  Term                 "Fall 2026". Start/end date. Container for classes.
  Room                 Name, capacity.
  Household            Billing unit. Address, contact info. Drives sibling discounts.
  Member               One row per human. Name, DOB, contact, optional Household.
                       NOT "Student" and "Instructor" — see §4.3.
    MemberRole         Member + role + effective dates. A member may hold several.

  DanceClass           The recurring definition.
                       Term, Room, style, level, age range, capacity,
                       and zero or more meeting patterns.
    MeetingPattern     DayOfWeek + TimeOnly start + TimeSpan duration.
                       A class may meet more than once a week. Optional — see §4.2.
    ClassSession       A concrete dated occurrence. The unit reality attaches to.
                       Date, start, duration, effective room, status,
                       origin pattern, override flags. See §4.2.
    ClassAssignment    Member + DanceClass + assignment role
                       (Lead / Assistant / Substitute) + effective dates.
                       Replaces a single InstructorId FK. See §4.3.
    Enrollment         Member + DanceClass + status + StartDate + optional EndDate.
                       An entity, not a list — see §4.4.
      AttendanceRecord Enrollment + ClassSession + status. Added in a later slice.
```

Note `DanceClass`, not `Class` — `class` is a C# keyword and you will regret it.

### 4.1 Date and time handling

This is where scheduling projects usually go wrong. Be deliberate:

- **Recurring definitions use `DayOfWeek` + `TimeOnly` + `TimeSpan` duration.** Not
  `DateTime`. A class is "Tuesdays at 4pm" in wall-clock terms and stays 4pm across a
  daylight-saving transition. Storing a UTC instant on the definition would silently shift
  it to 3pm in November.
- **Sessions store `DateOnly` + `TimeOnly`**, resolved against the studio's IANA timezone
  only at the point of display or export. `DateOnly` and `TimeOnly` map natively to
  Postgres `date` and `time` via Npgsql.
- **Store the studio's timezone as an IANA ID** (`America/New_York`), not a UTC offset.
  Offsets change twice a year; zone IDs don't.
- If timezone logic grows beyond this, reach for **NodaTime** rather than fighting
  `DateTime`. It has first-class Npgsql support. Don't add it on day one.

### 4.2 Irregular schedules, cancellations, and reschedules

This is what the definition/occurrence split buys you, so it's worth being explicit about the
mechanism: **a meeting pattern is a generator, not a constraint.** It produces sessions. Once
a `ClassSession` row exists it is an independent fact that can be edited freely, and nothing
forces it to keep matching the pattern that created it.

That single property covers the whole range of irregularity:

| Situation | How it's represented |
|---|---|
| Meets twice a week | Two `MeetingPattern` rows on one class |
| Snow day / holiday | Session `Status = Cancelled`, with a reason |
| Moved to another day, time, or room | Edit that session's date/time/room directly. `Status = Rescheduled` |
| Substitute teacher for one date | Session-level assignment override |
| Extra rehearsal before recital | A session with no origin pattern, created by hand |
| Makeup class for a cancellation | New session, linked back via `MakeupForSessionId` |
| Fully irregular class (workshop series, competition team) | A class with **zero** patterns; every session entered manually |

That last row is the important one. Because patterns are optional, a completely ad-hoc class
needs no special case — it's just a class nobody generated sessions for.

**The hard part is regeneration.** When someone edits a class's pattern in week 9, you must
not wipe out eight weeks of history and hand-made adjustments. So every session tracks its
provenance:

```csharp
Guid? OriginPatternId;   // null = created manually
bool  HasManualOverride; // set when date/time/room/assignment edited away from pattern
```

Reconciliation rules on regeneration:

1. Sessions in the past → never touched
2. Sessions with recorded attendance → never touched
3. Sessions with `HasManualOverride` → never touched, but flagged if they now conflict
4. Untouched, pattern-generated, future sessions → safe to delete and regenerate
5. New dates the pattern now implies → created

Present the result as a preview ("this will add 4 sessions and remove 3") before committing.
Silent bulk mutation of a schedule is how you lose a user's trust permanently.

**Cancelled, not deleted.** A cancelled session stays as a row. Studios need to answer "how
many classes did we actually hold?" for makeup credit and refund disputes, and a deleted row
can't answer anything.

### 4.3 Members, not Students and Instructors

The original model had `Student` and `Instructor` as separate entities. That was wrong, and
the student-teacher case is what exposes it — but it would have caused problems anyway. An
adult student who also teaches, a former student who becomes staff, a parent who takes an
adult tap class: all of them break a model where the two are separate tables.

Two entities means two rows for one human. Two names to keep in sync, two birthdays, two
emergency contacts, and no way to answer "is this the same person?" without string matching.

**One `Member` row per human. Roles are a separate, many-valued, time-bounded thing.**

```csharp
class Member     { Name, DateOfBirth, Contact, HouseholdId? }
class MemberRole { MemberId, StudioRole Role, DateOnly From, DateOnly? Until }
```

A 16-year-old who assists with the beginner class is one `Member` holding both a `Student`
role and an `AssistantStaff` role. No duplicate account, no clunky linking.

**Why `Member` and not `User`.** Most humans in a dance studio never log in — a six-year-old
ballet student has no account and never will. `User` means "someone who authenticates," which
would be untrue of the majority of rows. It also collides badly in ASP.NET Core, where
`ApplicationUser` (Identity), `HttpContext.User`, and `ClaimsPrincipal` are all ambient. See
the Identity split below.

### 4.3.1 Where role information lives

Not on booleans, and not in one place. There are three distinct questions, and collapsing them
is the usual mistake:

| Question | Answered by | Scope |
|---|---|---|
| What may they **do**? | `MemberRole` | Studio-wide, time-bounded |
| What do they **teach**? | `ClassAssignment` | Per class |
| What do they **attend**? | `Enrollment` | Per class |

**`MemberRole` is authorization. The other two are facts.**

`StudioRole` values: `Owner`, `Admin`, `Staff`, `AssistantStaff`, `Student`, `Guardian`.

Why not `IsInstructor` / `IsStudent` flags:

- Not time-bounded — can't answer "was she teaching in Fall 2025?"
- Adding a role becomes a schema migration
- N booleans give 2^N states, most of them nonsense
- Nowhere to hang role metadata (hire date, certification, guardian consent on file)

Why `MemberRole` isn't redundant with `ClassAssignment`, even though "has assignments" almost
implies "is an instructor" — four cases break the inference:

1. The studio owner may teach nothing but needs full access
2. A newly hired instructor is staff before their first assignment exists
3. A student between terms is enrolled in nothing but is still a member
4. **The field-level privacy tier in §4.6 is a property of the human, not of any class.** No
   amount of `ClassAssignment` data tells you whether the assignee is a 40-year-old staff
   member or a 16-year-old assistant — and that's precisely the distinction that decides who
   sees medical notes

Two implementation notes:

- Expose `IsStaff(DateOnly asOf)` as a **computed method** on `Member`. Boolean ergonomics, no
  stored column, still time-aware.
- Persist the role enum **as a string**, not an int. Reordering the enum later must not
  silently reassign everyone's permissions.

So the student-teacher resolves to: one `Member`, holding `MemberRole(Student, from 2024-09)`
and `MemberRole(AssistantStaff, from 2026-09)`, with a `ClassAssignment(BeginnerBallet,
Assistant)` and an `Enrollment(AdvancedJazz)`. Nothing duplicated, everything dated.

### 4.3.2 Identity vs. domain roles

Use ASP.NET Core Identity for **authentication only** — credentials, lockout, 2FA, password
reset. Keep authorization in the domain.

Identity's own role system can't express what's needed here: its roles aren't time-bounded,
aren't tenant-scoped, and can't be derived per-class. Trying to force `MemberRole` into
`AspNetUserRoles` means losing effective dates and the studio boundary at the same time.

```
ApplicationUser (Identity)  ──1:1──▶  Member (domain)
   credentials, lockout                roles, assignments, enrollments
```

Not every `Member` has an `ApplicationUser`; that's the normal case, not an edge case. Project
domain roles into claims at sign-in so routine policy checks stay cheap, but treat the database
as the source of truth for anything that matters.

### 4.3.3 Teaching assignments

This also fixes something unrelated: `DanceClass.InstructorId` as a single foreign key models
teaching badly. It can't express co-taught classes, a substitute for one date, an assistant
alongside a lead, or an instructor change mid-term. `ClassAssignment` — a row per
member-per-class with a role and effective dates — handles all four.

### 4.4 Why Enrollment is an entity and not a list of students

Fair challenge. If all you need is "who is in this class," a collection is simpler, and EF
Core would happily generate a join table behind a skip navigation.

The reason it doesn't hold: **you need to store facts about the relationship itself**, and a
plain list has nowhere to put them.

- **When they joined and when they left.** Students join late and drop mid-term constantly.
  This is required for tuition proration, and there's nowhere to hang a date on a list entry.
- **Status.** Waitlisted, trial class, enrolled, dropped, withdrawn-with-refund. A list is
  binary: in or not in.
- **Rate overrides.** Scholarship, staff child, comped. Belongs to *this student in this
  class*, not to the student and not to the class.
- **Waitlist position**, which is inherently an attribute of the pairing.
- **Audit** — who enrolled them, when.

The decisive one is **historical accuracy**. Suppose a student drops in October. With a plain
list you remove them, and now the November roster is correct but every October attendance
record points at someone the data says was never in the class. You can no longer answer "who
was enrolled on October 13?" — and that's exactly the question a billing dispute asks.

A list also can't distinguish "never enrolled" from "enrolled and dropped," which is the
difference between "we never billed you" and "we billed you for six weeks."

This shape has a name — an associative entity, or a join entity with payload. The rule of
thumb: *the moment the relationship has attributes of its own, it's an entity.* Enrollment
crossed that line before we wrote a single line of code.

Same reasoning applies to `ClassAssignment`, for the same reasons.

### 4.5 Multi-tenancy

Every tenant-owned entity carries `StudioId` from the **first migration**. Retrofitting means
touching every query, backfilling every table, and hoping you didn't miss one — where "missing
one" means leaking one customer's student data to another.

Define the contract as an interface so it's enforceable rather than remembered:

```csharp
public interface ITenantOwned { Guid StudioId { get; set; } }
```

**Read side — global query filters, applied by reflection so you can't forget one:**

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    foreach (var entity in modelBuilder.Model.GetEntityTypes()
                 .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)))
    {
        var param = Expression.Parameter(entity.ClrType);
        var body = Expression.Equal(
            Expression.Property(param, nameof(ITenantOwned.StudioId)),
            Expression.Property(Expression.Constant(this), nameof(StudioId)));
        modelBuilder.Entity(entity.ClrType)
                    .HasQueryFilter(Expression.Lambda(body, param));
    }
}
```

Referencing an instance property on the context is the documented pattern — EF Core treats it
as a query parameter, not a baked-in constant, so one compiled model serves every tenant.

**Write side — filters do not apply to inserts.** A global query filter is a read-time
concern only; nothing stops you saving a row with the wrong `StudioId` or none at all. Stamp
it with a `SaveChanges` interceptor:

```csharp
foreach (var e in ChangeTracker.Entries<ITenantOwned>().Where(e => e.State == EntityState.Added))
    e.Entity.StudioId = StudioId;
```

**Four things that bypass the filter — know them before they bite:**

1. `IgnoreQueryFilters()` — necessary for admin tooling, dangerous everywhere else. Grep for it in review.
2. Raw SQL (`FromSqlRaw`, `ExecuteSqlRaw`, Dapper) — filters are LINQ-level and simply don't exist here.
3. `Find()` on a primary key — hits the change tracker first and can return a cached cross-tenant entity.
4. **Cross-tenant foreign keys.** Nothing prevents a class in studio A referencing a room in studio B. Filters won't catch it because both reads are individually filtered.

For (4), the strong fix is composite keys: make the primary key `(StudioId, Id)` and carry
`StudioId` into every foreign key. The database then makes cross-tenant references
structurally impossible. It complicates EF configuration noticeably, so it's a judgement call —
but it's a good thing to have an opinion about, because it's a real interview question.

**Index for it.** Composite indexes lead with `StudioId`: `(StudioId, Date)` on sessions,
`(StudioId, TermId)` on classes. It's the highest-selectivity column in a multi-tenant table
and belongs first.

**Test it.** Write a deliberate leakage test early: seed two studios, run every query as
studio A, assert zero studio-B rows come back. That test is the whole feature.

### 4.6 Permissions and privacy

Minors teaching minors is the requirement that forces this to be designed properly rather than
bolted on. Two separate axes, and conflating them is the usual mistake:

**Row scope — which classes can you see at all?**

Derived from `ClassAssignment`, not from a global role. If you're assigned to a class, you see
that class. Staff instructors and student-teachers use the *same rule*; a studio owner or admin
sees everything.

**Field scope — within a class you can see, which fields?**

This is where staff and student-teachers diverge, and it's the part row-level security cannot
express. A 16-year-old assistant legitimately needs the roster for their class. They do not
need their classmates' home addresses.

Columns map directly to `StudioRole` values from §4.3.1.

| | `Owner` / `Admin` | `Staff` | `AssistantStaff` |
|---|---|---|---|
| Their assigned classes | ✓ | ✓ | ✓ |
| All studio classes | ✓ | ✗ | ✗ |
| Roster: name, photo | ✓ | ✓ | ✓ |
| Record attendance | ✓ | ✓ | ✓ |
| Student DOB | ✓ | age band only | age band only |
| Guardian contact info | ✓ | ✓ | **✗** |
| Home address | ✓ | ✗ | **✗** |
| Medical / allergy notes | ✓ | ✓ | **✗** |
| Billing status | ✓ | ✗ | ✗ |

**Enforce it by construction, not by discipline.** Never return an entity to a Blazor
component. Service methods return audience-specific DTOs — `RosterEntryDto` (name, photo,
attendance state) versus `RosterEntryDetailDto` (everything) — and project with `Select` so the
restricted fields are never loaded from the database in the first place. Over-exposure then
requires actively writing the wrong DTO, rather than merely forgetting a check in a `.razor`
file.

**Three rules that fall out of the minor-staff case:**

- **Medical information does not go to a minor.** In an emergency the responsible adult needs
  allergy data instantly, so it must be reachable — but by the supervising staff member, not the
  student-teacher. The app should never be the reason a 16-year-old is the person handling a
  medical incident.
- **No self-dealing.** A person must not record attendance for a class where they are also
  enrolled as a student. Cheap check, obvious once stated, easy to miss.
- **Audit PII access.** Log who viewed student detail and when. Small table, and it's the first
  thing anyone asks about when minors' data is involved.

**Real-world caveats worth a README note but not code:** minors as staff raise guardian consent,
labor law, and background-check questions that a portfolio app should acknowledge and not
pretend to solve.

---

## 5. The interesting algorithms

These are the parts worth doing carefully, because they're what you'll actually discuss in
an interview.

### 5.0 Invariants — validity before conflicts

Two different kinds of wrong, and they need different machinery:

- **Invariants** — states that are wrong *in isolation*. A class that ends before it starts.
  Checkable against a single row, with no other data loaded.
- **Conflicts** — states where each row is individually valid but the combination isn't. Two
  classes in one room at 4pm. Requires querying across rows.

Invariants come first: it's pointless to run conflict detection over data that's already
malformed, and a nonsense duration will produce nonsense overlaps rather than an error.

**The strongest invariant is one you can't express.** Rather than storing `StartTime` and
`EndTime` and validating `End > Start`, store `TimeOnly Start` and `TimeSpan Duration` and
compute the end. A backwards class is then not *invalid* — it's *unrepresentable*. Making bad
states impossible to construct beats validating for them, and this is the cleanest example of
it in the whole model. (This is why `MeetingPattern` in §4 carries a duration.)

That doesn't remove the need for checks, it just moves them somewhere narrower:

| Invariant | Note |
|---|---|
| `Duration > 0` | Zero-length class is meaningless |
| `Duration <= 8 hours` | Sanity bound; catches unit mix-ups |
| `Start + Duration` does not cross midnight | Dance studios don't run overnight; reject rather than silently wrap |
| `Term.End >= Term.Start` | |
| Class start date falls within its term | |
| Session date falls within term bounds | Unless deliberately detached |
| Session date matches its origin pattern's `DayOfWeek` | Only when `HasManualOverride` is false |
| `Enrollment.End >= Enrollment.Start` | |
| Enrollment period overlaps the class's active period | Can't enrol in a term that already ended |
| No duplicate *active* enrolment for one person + class | Overlapping enrolment rows are a bug |
| `Room.Capacity > 0`, `DanceClass.Capacity > 0` | |
| Age range `Min <= Max` | |
| Assignment dates fall within the class's dates | |
| Referenced room/person belong to the same studio | The cross-tenant trap from §4.5 |

**Enforce in three layers, deliberately redundant:**

1. **Domain** — guard clauses in constructors and factory methods, throwing on violation. The
   real enforcement point; an invalid object should never exist in memory.
2. **Database** — `CHECK` constraints as a backstop, because migrations, imports, and manual
   SQL all bypass the domain layer.
3. **UI** — friendly messages so users see "End time must be after start time," not a 500.

The domain-layer checks are cheap to test exhaustively since they need no database, which is
exactly why `Pirouette.Domain` has no EF reference (§3).

### 5.1 Session generation

Given a `DanceClass` with meeting patterns and a `Term` with a date range, produce the
`ClassSession` rows.

Deliberately **do not** implement RFC 5545 `RRULE`. Full recurrence rules are a swamp
(`BYSETPOS`, `BYMONTHDAY`, nested exceptions) and a dance studio needs approximately none of
it. "Weekly on one or more days" covers essentially every real class. Document the
limitation as a scoping decision rather than an oversight — knowing what not to build is a
signal in itself.

Edge cases to test:
- Term boundaries — a Tuesday class in a term that starts on a Wednesday
- Studio closure dates (holidays) skipped, not shifted
- A class added mid-term generates sessions only from its start date
- Regenerating after an edit must not destroy attendance already recorded

That last one is the real design problem. Once attendance exists, session rows are no longer
disposable. Decide early: regeneration reconciles (add missing, cancel orphans, leave
recorded sessions alone) rather than deletes and recreates.

### 5.2 Conflict detection

Five kinds, all built on one primitive:

```csharp
// Half-open intervals: [start, end)
static bool Overlaps(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd)
    => aStart < bEnd && bStart < aEnd;
```

A class ending at 5:00 and one starting at 5:00 do **not** conflict. Using `<=` here is the
classic off-by-one in scheduling code and produces phantom conflicts on every back-to-back
class in the building.

Run these only after §5.0 invariants pass — overlap math on a malformed duration produces
garbage rather than an error.

| Conflict | Rule |
|---|---|
| Room double-booked | Same room, same date, overlapping times |
| Instructor double-booked | Same assigned person, same date, overlapping times |
| Student conflict | One person enrolled in two classes whose sessions overlap |
| **Teaching vs. attending** | One person *assigned to teach* one session and *enrolled in* another that overlaps |
| Over capacity | Active enrollments exceed `min(class capacity, room capacity)` |

The fourth row only exists because of the student-teacher case, and it's a good illustration
of why unifying `Member` (§4.3) was the right call: with separate `Student` and `Instructor`
tables this conflict is invisible, because nothing in the schema knows the two rows are the
same human. With one `Member`, it's the same overlap query as every other conflict.

Add an optional **changeover buffer** per studio (5–10 minutes) so back-to-back classes in
one room can be flagged. Real studios need students to clear the floor. Small feature, shows
domain awareness.

Surface conflicts in two places: as validation when saving a class, and as a standing
"schedule health" view listing everything currently wrong. The second is more useful,
because conflicts appear from edits elsewhere.

### 5.3 Roster as of a date

Because `Enrollment` carries effective dates rather than a boolean (§4.4), "who is in this
class" is always a question *about a point in time*:

```csharp
enrollments.Where(e => e.DanceClassId == id
                    && e.Start <= asOf
                    && (e.End == null || e.End >= asOf))
```

Default `asOf` to today for the live roster, to the session date for attendance, and to the
billing period start for invoicing. One query, three uses.

The subtle part is that attendance must resolve the roster **as of the session date**, not
today. Open a September session in November and you should see who was actually there in
September. Getting this wrong is invisible until someone drops, and then quietly corrupts
every historical record.

---

## 6. Payments — read before writing any billing code

When billing eventually comes into scope:

**Never let card data touch your server.** Use Stripe Checkout or Stripe Elements, where the
card is captured by Stripe's iframe and your backend only ever sees a token. The moment card
numbers pass through your application you are in PCI DSS scope, and that is not a thing to
take on for a portfolio project.

For portfolio purposes, the *calculation* is the interesting half anyway — rate plans,
proration for mid-term joins, sibling discounts, multi-class discounts, drop credits. That
is real domain logic you can build and test with no payment provider at all. Stripe test
mode can be a thin layer on top later.

---

## 7. Milestones

Sized for a few hours a week. Each one ends with something you could demo or commit and walk
away from. Week numbers are guidance, not commitments.

### M0 — Foundation and tenancy (weeks 1–2)
Docker Compose with Postgres. `Pirouette.Domain` and `Pirouette.Infrastructure` projects.
EF Core via `AddDbContextFactory`, first migration, connection string in user secrets.
`ITenantOwned`, the reflection-applied query filter, and the `SaveChanges` stamping
interceptor — all of §4.5 — plus the two-studio leakage test. GitHub Actions running
`dotnet build` and `dotnet test`.

*Done when:* CI is green, the app starts against a real database, and the leakage test passes.

### M1 — Core entities, invariants, CRUD (weeks 3–4)
`Studio`, `Term`, `Room`, `Household`, `Member` + `MemberRole`, `DanceClass` with meeting
patterns, `ClassAssignment`. Guard clauses for every invariant in §5.0, with `CHECK`
constraints behind them. Blazor list and edit pages. Seed data for one realistic studio —
include a student-teacher in the seed so the case stays visible.

*Done when:* you can build a term, rooms, people, and classes through the UI, and invalid
input is rejected with a sensible message.

### M2 — Session generation (weeks 5–6)
Pure domain service. Heavy unit tests covering the edge cases in §5.1. Studio closure dates.
Reconciling regeneration with the provenance rules from §4.2.

*Done when:* creating a class produces correct sessions across a full term, and regenerating
after an edit provably preserves manual changes.

### M3 — Schedule exceptions (week 7)
Cancel, reschedule, and add one-off sessions. Session-level room and assignment overrides.
Makeup links. The regeneration preview ("this will add 4 and remove 3").

*Done when:* you can cancel a snow day, move a class, assign a substitute for one date, and
run a workshop-style class with no pattern at all.

### M4 — Conflict detection (weeks 8–9)
All five conflict types from §5.2, including teaching-vs-attending. Changeover buffer.
Validation on save plus a standing schedule health view.

*Done when:* you cannot save a double-booked room, and existing conflicts are listed somewhere.

### M5 — Calendar view (week 10)
Weekly grid, rooms as columns, time as rows. Filter by member, term, room. Cancelled and
rescheduled sessions visibly distinct.

*Done when:* it looks good enough to screenshot for the README. This milestone exists partly
to make the project legible to someone skimming it in 30 seconds.

### M6 — Enrollment (weeks 11–12)
Enroll and drop with effective dates. Waitlist. Roster-as-of-date (§5.3). Capacity
enforcement.

*Done when:* a student can be enrolled, appear on a roster, be dropped mid-term, and the
September roster still reads correctly in November.

### M7 — Auth, roles, and field-level privacy (weeks 13–14)
ASP.NET Core Identity for auth only, `ApplicationUser` 1:1 onto `Member` (§4.3.2).
`MemberRole` for authorization, projected into claims. Assignment-derived row scope. Audience-specific
roster DTOs implementing the §4.6 table. Self-dealing check. PII access audit log.

*Done when:* a student-teacher login can mark attendance for their own class and can reach
no other class, no guardian contact details, and no medical notes — proven by tests, not by
clicking around.

### M8 — Polish, deploy, document (weeks 15–16)
Deploy somewhere with a public URL. README with screenshots, architecture notes, and the
design decisions from §4 and §5 written up. Integration tests via Testcontainers.

*Done when:* someone can find the repo, understand what it does in 60 seconds, and click a
live link.

> **Note on the schedule.** The original plan was 12 weeks. Unifying `Member`, adding schedule
> exceptions as first-class behaviour, and doing field-level privacy properly pushes it to
> ~16. That's a real cost and worth naming — but exceptions and the student-teacher case are
> both things that would have forced a painful schema migration if deferred, and M5 still gives
> you something demoable at the two-thirds mark.

---

## 8. Testing

- **xUnit** throughout.
- **Domain tests are the priority.** Session generation and conflict detection are pure
  functions; test them exhaustively. This is cheap because there's no database.
- **Integration tests with Testcontainers** for EF queries, migrations, and query filters.
  Real Postgres in a container, no mocking of the database.
- **Skip UI tests.** Playwright against Blazor is a time sink at this scope. Better spent on
  domain coverage.

Aim for meaningful coverage of §5, not a coverage percentage across the whole solution.

---

## 9. Performance

The origin story is "the existing tool is slow." That's only worth claiming if it's measured.

**Do now (cheap, habit-forming):**
- Log EF-generated SQL in development and actually read it
- `AsNoTracking()` on every read-only query
- Project to DTOs with `Select` rather than loading full entity graphs
- Index the columns you filter sessions by: `(StudioId, Date)`, `(RoomId, Date)`

**Do at M7:**
- Pick 3–5 key pages, set an explicit budget (e.g. p95 under 300 ms server-side), measure,
  and put the numbers in the README

**Do only if the algorithms get complex:**
- BenchmarkDotNet on session generation and conflict detection

**Don't do:** caching layers, read replicas, or CQRS. At this data size — a studio has
hundreds of students, not millions — a single indexed Postgres instance is far more than
enough. Premature optimization is worse than no optimization here, because it's complexity
you'd have to justify.

One honest note: Blazor Server sends UI diffs over a WebSocket, so perceived speed depends on
latency to the server. It feels excellent on a LAN and less so on a bad mobile connection.
Worth knowing before you build a perf narrative on it.

---

## 10. Immediate next steps

1. Add `.idea/` to `.gitignore` — not tracked, but not ignored either, so it clutters `git status`
2. Create `Pirouette.Domain` and `Pirouette.Infrastructure` projects, reference them from `PirouetteApp`
3. Docker Compose file with Postgres
4. Add Npgsql EF Core provider; `PirouetteDbContext` via `AddDbContextFactory`
5. Define `ITenantOwned`; implement the reflection-applied query filter and the `SaveChanges` stamping interceptor
6. First entities — `Studio`, `Term`, `Room` — and the first migration; verify it applies
7. Write the two-studio leakage test *before* there's much to leak
8. GitHub Actions workflow: build + test

Then start M1.
