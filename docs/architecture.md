# Architecture and experiment notes

## The owner boundary

Eight files in `src/Core` contain the intended application semantics: Domain, Commands, Events, Rules, Evolution, Reports, Presentation and Pages. Every one is compiled by the F# compiler. There is no YAML/JSON language for the owner and no duplicate human-readable specification that the implementation can ignore.

`Ui.fs` is infrastructure: a small set of form, table, column, field, action, filtering, sorting and navigation records plus composition helpers. Server code owns HTTP decoding, JSON, SQLite and time/identity generation. Client code owns DOM construction, interactions, network calls and presentation styles. Owners need a brief introduction to F# records, unions, matching, lists and pipelines; they do not need SQL, HTTP or DOM knowledge.

## The decision loop

```text
Browser form → HTTP decoding → Command(s)
                                  ↓
                    decide today State Command
                           ↙              ↘
                    DomainError        Event list
                                           ↓
                              atomic SQLite append
                                           ↓
                              evolve State Event
                                           ↓
                                  current State
                                           ↓
                             queries / page values
```

`decide : DateOnly -> State -> Command -> Result<Event list, DomainError>` and `evolve : State -> Event -> State` are pure. The shell supplies the local date and newly generated GUIDs; those technical facts never get fetched inside the core. Dates are calendar dates, and recorded timestamps are UTC. A state contains maps of students and applications, with attendance inside each application.

Status is a discriminated union, not unrelated booleans. Pending, documentation-requested, rejected, participating, completed and discontinued states carry only their relevant information. Both terminal participation cases retain the start and end dates. Wrapped IDs prevent accidentally passing a course identity where a student identity is expected. Personal details still use simple strings validated by decisions, deliberately avoiding a large family of wrapper types for v0.1. Email validation is intentionally modest, not a deliverability guarantee.

The shell serializes access with one lock. A whole command batch is decided against a temporary projected state, so student creation plus application and bulk approval are atomic. The SQLite transaction checks the expected log revision before insertion. The browser also submits the page revision; a stale edit gets HTTP 409. The in-memory state advances only after commit. Queries run under the same lock, avoiding observations of half a batch. Multiple server processes against one database are unsupported; a revision mismatch detects the stale writer but is not a multi-process synchronization design.

Evolution assumes events were accepted by decisions. It does not reevaluate today's rules during replay, which would make historical outcomes depend on later policy changes. Do not manufacture events or edit the database. Its simple projection helpers are not a corruption-repair framework.

## Describing the UI

`standardStudentPresentation` is a list of typed column readers. Applicant rows adapt those readers once, then derive a table by removing birth date and adding application-specific columns. This proves reuse and modification without copying the original fields. Table values declare sorting keys, filters, row actions, selection eligibility and bulk actions. All tables currently get case-insensitive search across displayed values as a vocabulary default. Choices are generated from the current rows; filters on an empty table therefore have only “All.”

Forms describe labels, field kinds, requiredness, defaults and choices. Shared personal-details fields are used by creation and editing. The `Intent` union identifies the workflow to execute. The technical HTTP adapter converts submitted values to typed commands. `CreateStudentAndApply` intentionally composes creation and optional application in one batch. There is no independent browser business-validation engine: browser required/email/date inputs give immediate assistance, but all authority remains in Rules.

`Pages.fs` composes those values with pure queries. Pages are evaluated **on the server**, producing plain UI records containing current data. The same vocabulary file is compiled into the Fable frontend, which renders those records. This is a deliberate server-driven UI choice: the owner definitions are genuine compiled F#, but they do not execute twice or require shipping the whole domain to the browser. There is no parallel hand-coded student page hidden in the renderer.

The renderer knows forms, tables, facts and navigation, not Student or Application. It does contain a small styling convention for status text and an inline-form placement convention for the `new` route. Those are acknowledged application-specific leaks in v0.1. A larger vocabulary could replace them, but would cost readability and is not justified yet. Filtering/sorting retain input focus by replacing table rows only. Navigation and completed commands replace the page. A generation counter rejects late responses from old navigations. Native dialogs handle focus containment and Escape; the server rejects stale form revisions.

## Fable and the browser

The pinned local Fable tool compiles `src/Client/App.fs` and the linked `Ui.fs` into JavaScript. esbuild bundles the result and the Fable runtime into one ES module in `wwwroot`. `index.html` loads it. No React, Vue, component framework, virtual DOM, Node production server or server-side HTML templates are involved.

The frontend uses functions and a small Navigate/Loaded/Failed message loop. DOM nodes are created with textContent, not string-interpolated HTML. Forms use browser controls, tables use semantic table elements, and status/errors use live regions. CSS alone controls colors, spacing and responsiveness. Read-only page requests produce a coherent snapshot; changes require a fresh snapshot after success. No sockets, queues, brokers or eventual consistency.

Fable setup follows the [official new-project guidance](https://fable.io/docs/getting-started/your-first-fable-project.html). The Fable compiler and the few NuGet/npm dependencies are pinned; npm also has a lockfile.

## Persistence and evolution

SQLite is authoritative. The only application table is `events(sequence, timestamp, event_type, schema_version, payload)`. Sequence is assigned by SQLite; each row carries an explicit type and schema version 1. SQLite transactions protect a batch, WAL supports normal reads, and triggers reject ordinary UPDATE/DELETE operations. Those triggers are an accidental-edit guard, not protection against a database administrator.

Startup reads the entire log in sequence and folds `Evolution.evolve` over an empty state. Rebuild follows the same path and publishes the rebuilt projection only after successful replay. There are no persisted projections, snapshots, generic repositories, aggregates-per-stream abstractions or background projection workers. The history endpoint and UI render the existing in-memory loaded history. This is appropriate only for the stated small dataset.

`Store.fs` isolates FSharp.SystemTextJson serialization and the event-type/version check. Unknown versions fail startup. v1 payloads use F# union case names, field positions, record property names and ISO calendar dates; those are a persistence contract. Renaming a case or changing a payload type is **not** a harmless refactor. For a future v2, keep the v1 decoder and add an explicit local upcast to current events (or introduce a new event case), with committed v1 fixture tests. Never rewrite old events simply to make current deserialization work. There is no general upcaster registry yet.

Course reference data is in code, so its IDs, titles and dates are also part of replay interpretation. Treat existing offerings as immutable and create new IDs for new offerings. A production system should persist/version that reference data and capture the policy inputs needed to explain past decisions.

## What changes for production

Implement authentication and the three authorization levels; enforce policy on every server command/query, not just visibility of buttons. Add secure hosting/TLS, deployment configuration, request limits, auditing of actors and correlation IDs, monitoring, privacy/retention requirements for immutable personal data, backup retention and tested recovery procedures. Decide how to correct terminal mistakes, transfer/withdraw applications, attach documents, model timetables, and maintain course offerings. Add stronger domain value validation where actual requirements justify it. Establish an event migration policy before changing stored schemas.

For larger datasets, query/paginate history and table results, move long-running work out of the global lock, and revisit the whole-state revision strategy. A retry/idempotency key is needed before claiming exactly-once commands over unreliable networks. The current client disables a submit while it is pending and tells users to refresh after an uncertain network failure; it does not automatically retry.

## Where the experiment works, and where it is awkward

**Works:** unions make lifecycle alternatives visible; decide/evolve expose a small auditable state machine; event histories explain where state came from; shared student column readers change multiple pages at once; forms, filtering, sorting, bulk eligibility and report definitions live in compiled owner files. Pure tests can show a broken business rule without running a browser or database.

**Awkward:** typed F# record inference needs occasional annotations, especially when many records have `Id`, `Title` or `Student` fields. Several query pipelines and nested matches still require explanation. Eight files are a manageable boundary for an experiment, not proof that a novice will understand them. Form intent names and transport field keys are strings at the HTTP boundary; compiler checks cannot prove every field is decoded correctly, so integration tests are necessary. There is some repetition between command cases, event cases, evolution and the adapter; that repetition makes the lifecycle inspectable but adds maintenance work.

The UI descriptions mix durable semantics with readable query plumbing. They are more verbose than a dedicated configuration file would be. Complete page replacement resets filters after a command and may lose scroll position; no client history cache or sophisticated diffing was added. Native date/choice controls vary by browser. Long histories and wide course tables are horizontally scrollable rather than a specialized mobile card view. Technical event JSON is inspectable but not a polished narrative for end users.

**Assessment:** this prototype makes the intended review surface real and executable. It demonstrates the hypothesis mechanically, but usability of the source must still be evaluated by the intended application owner. The most valuable next experiment is an owner identifying seeded incorrect rules or screen changes without opening infrastructure files, not extraction of a general-purpose library.
