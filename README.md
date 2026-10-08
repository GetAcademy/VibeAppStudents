# Student Office — an F# architecture experiment

A working v0.1 exploring whether an application owner can audit business behaviour and screen semantics in a small set of **compiled F# files**, while ignoring the technical implementation.

**Local experimental software. No authentication or authorization. Do not expose it on a network, put it behind a public proxy, or enter real student data.** It is not ready for production. The three eventual access levels are ordinary user, application administrator, and system administrator; none are enforced here.

## Run

Prerequisites: .NET 10 SDK and Node.js 22+ with npm. No Docker, external database, or frontend framework.

From the repository root:

```sh
dotnet tool restore
npm ci
npm run build
dotnet test tests/Tests.fsproj -m:1 -nodeReuse:false
dotnet run --project src/Server/Server.fsproj -- --demo
```

Open **http://127.0.0.1:5080**. The backend serves the Fable-generated browser bundle and stylesheet. `npm start` runs the same demo command. Stop with Ctrl+C. On Windows, stop the server before rebuilding its loaded DLLs.

The first `--demo` start creates 16 fictional students, 3 course offerings, 21 applications, and 110 accepted events, including documentation requests, rejection, both completion results, discontinuation, and attendance. Later starts replay the existing history and do **not** seed it again. Without `--demo`, a missing database starts empty. Demo history uses fixed 2025–2026 dates; normal commands use the server's current local date. Course offerings end in June 2027: for later experiments, add a new offering with a new ID in `Domain.fs`, rather than altering an old offering's dates.

Default database: `data/students.db`. Set `STUDENT_DB` to a different absolute file path for a separate experiment. Set `STUDENT_PORT` to change the loopback port. Run only one server per database file. Browser tests during development may add clearly named fictional students to your local demo history; use another `STUDENT_DB` path for a fresh baseline.

In restricted environments, .NET may need a writable CLI home. For PowerShell:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.local"
```

Keep that setting for every .NET/npm build or run command in the session. It is not required for a normal installation.

## Start here if you are the application owner

Read these files in order. They are the application, not a second specification maintained alongside it.

| File | Questions it answers |
| --- | --- |
| [Domain.fs](src/Core/Domain.fs) | What is a student? What can an application's status be? What course offerings exist? |
| [Commands.fs](src/Core/Commands.fs) | What can somebody ask the system to do? What kinds of refusal exist? |
| [Events.fs](src/Core/Events.fs) | What facts can be recorded? Which events belong in a student's history? |
| [Rules.fs](src/Core/Rules.fs) | When is a request accepted or rejected? What dates and transitions are allowed? |
| [Evolution.fs](src/Core/Evolution.fs) | How does each fact change the current view of the world? |
| [Reports.fs](src/Core/Reports.fs) | What exactly do the reports count, and what do they exclude? |
| [Presentation.fs](src/Core/Presentation.fs) | Which student columns and fields are reused? What actions and forms exist? |
| [Pages.fs](src/Core/Pages.fs) | What does each screen show? Which filters, sorts, navigation and bulk actions exist? |

You can ignore `src/Client`, `src/Server`, and `src/Core/Ui.fs` during the owner experiment. `Ui.fs` implements the small vocabulary used by the readable presentation files. CSS and DOM code live outside the owner files.

### Five minutes of F#

- `type Student = { Id: StudentId; Details: PersonalDetails }` says a student contains those two named things. Follow `PersonalDetails` to see its fields.
- `| Passed | NotPassed` means choose exactly one alternative. A completed participation cannot simultaneously be discontinued.
- `of ...` names the information an alternative carries. `DocumentationRequested of reason: string` always carries a reason.
- `let` gives a name to a value or rule. `[ ... ]` is a list. `Some value` means a value exists; `None` means it does not.
- `match ... with` is a decision table. Read each `| pattern -> answer` as “in this case, do this.”
- `Ok [event]` accepts a request and returns facts. `Error ...` refuses it. It cannot do both.
- `|>` means “then pass the result to.” In a table definition, read successive lines as instructions to add columns, filters, sorting and actions.
- `{ existing with ... }` creates a modified copy. It leaves the original unchanged.
- `fun row -> row.Student.Details.FullName` reads a value from a row; it does not change that row.
- `Map` stores things by identity. `List.filter` keeps matching things; `List.map` transforms each thing.

For example, `applicantsTable` starts from the standard student presentation, removes birth date, adds status and applied date, filters by status, then adds row actions and bulk approval. Changing the common name column changes both presentations. `applicationActions` explicitly lists actions for each status.

### Try the owner experiment

1. Read `Rules.fs`: can a rejected application later be approved? Can attendance be entered before participation starts?
2. Read `Reports.fs`: are excused days treated as absences? Does a previous rejected application count as previous participation?
3. Read `Presentation.fs`: which pending applications can be selected for bulk approval? Which actions disappear after completion?
4. Change a column label in `Presentation.fs`, run the application again, and find it in both Students and Courses. For backend-only owner changes, restarting `dotnet run` is enough; rebuild the browser bundle when changing `Ui.fs` or `src/Client`.
5. Propose a deliberately wrong rule, such as allowing attendance after completion. The readable terminal-state test in `tests/Tests.fs` should fail if that rule is implemented.

See [the architecture and candid tradeoffs](docs/architecture.md). The hypothesis still needs a real non-programmer's review; compilation and tests do not prove that the files are understandable.

## Workflows and boundaries

- Create a student with an optional first application; this is one atomic transaction. IDs are generated by the server and stay stable during edits.
- One application per student per course **offering**. A repeat offering needs a new course ID. Rejection is terminal in v0.1.
- Approval immediately starts participation on the chosen date; there is no separate enrollment queue.
- Requesting documents keeps an application open. This prototype records the request, not document uploads.
- Register present, absent or excused attendance. Re-register the same date to make an audited correction. Corrections are only allowed during active participation.
- Complete with passed/not-passed, or discontinue with a reason. Those outcomes are terminal. An end date cannot precede existing attendance.
- Bulk approval is all-or-nothing. Filtering clears hidden selections. Any stale page must reload before submitting a change.
- Reports include prior participation by course and attendance aggregation. Definitions and overlap rules appear both on screen and in `Reports.fs`.
- System shows the event count, inspectable payloads and current projection JSON, and can rebuild state or download a consistent SQLite backup.

## Tests

```sh
npm test
dotnet build src/Server/Server.fsproj -m:1 -nodeReuse:false
node --test tests/http.integration.mjs
```

The F# suite covers readable given/when/then decisions, all event cases, terminal states, dates, atomic batches, report denominators, presentation composition, versioned serialization, SQLite replay, stale writers and backups. The HTTP suite starts an isolated server on **5081**, writes only under ignored `test-results`, verifies concurrent commands and malformed requests, downloads a backup, and restarts from both the original and backup databases. It leaves logs and test databases for inspection. Port 5081 must be free. Build the frontend first so the HTTP asset check can succeed.

[UI verification steps and results](docs/verification.md) cover the actual frameworkless Fable browser application. Tests use fictional data only.

## Backup and offline restore

Use **System → Download SQLite backup** while the server is running. This uses SQLite's backup API under the application's command lock; it does not simply copy a live WAL database file.

There is deliberately **no live restore button**. The safe v0.1 procedure is:

1. Stop all server processes using the database. Retain the current database and any `-wal` / `-shm` files together as a rollback copy; never mix sidecars from different databases.
2. Keep the downloaded backup unchanged. Copy it to a **new path**, and set `STUDENT_DB` to that path.
3. Start the application, inspect the event count and several students, and run Rebuild current state. Unknown event versions or payload/type mismatches cause startup to fail without rewriting history.
4. Keep the previous database until satisfied with the restored copy. If validation fails, stop the server and switch `STUDENT_DB` back.

Backups contain the full personal-data history, including previous names and email addresses. Authentication, retention/deletion policy and encryption are production work, not solved here.
