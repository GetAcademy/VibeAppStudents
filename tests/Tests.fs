module StudentApp.Tests

open System
open System.IO
open Xunit
open StudentApp
open StudentApp.Server

open StudentApp.Fixtures

[<Fact>]
let ``Given no student when created then identity and details are recorded`` () =
    Domain.empty |> whenAsked (CreateStudent (student, details)) |> expectEvents [StudentCreated (student, details)]

[<Fact>]
let ``Given a student when details change then identity is preserved`` () =
    let updated = { details with FullName = "Ada Changed" }
    let state = given created
    let events = state |> whenAsked (ChangeStudentDetails (student, updated)) |> mustSucceed
    Assert.Equal<Event list>([StudentDetailsChanged (student, updated)], events)
    let next = List.fold Evolution.evolve state events
    Assert.Equal(student, next.Students[student].Id)
    Assert.Equal(updated, next.Students[student].Details)
    Assert.Equal(1, next.Students.Count)

[<Fact>]
let ``Given invalid personal details creation produces no facts`` () =
    for invalid in [ { details with FullName = " " }; { details with Email = "bad" }; { details with BirthDate = today.AddDays 1 }; { details with BirthDate = DateOnly(1899, 12, 31) } ] do
        match Rules.decide today Domain.empty (CreateStudent (student, invalid)) with
        | Error (InvalidPersonalDetails _) -> ()
        | other -> failwithf "Expected personal details error, got %A" other

[<Fact>]
let ``Given an existing identity it cannot be created again`` () =
    given created |> whenAsked (CreateStudent (student, details)) |> expectError IdentityAlreadyExists

[<Fact>]
let ``Given an unknown student applications and edits are rejected`` () =
    Domain.empty |> whenAsked (ApplyForCourse (application, student, course, applied)) |> expectError StudentNotFound
    Domain.empty |> whenAsked (ChangeStudentDetails (student, details)) |> expectError StudentNotFound

[<Fact>]
let ``Given an unknown course an application is rejected`` () =
    given created |> whenAsked (ApplyForCourse (application, student, CourseId "unknown", applied)) |> expectError CourseNotFound

[<Fact>]
let ``Given a student when applying then the submitted event is precise`` () =
    given created |> whenAsked (ApplyForCourse (application, student, course, applied))
    |> expectEvents [ApplicationSubmitted (application, student, course, applied)]

[<Fact>]
let ``Given an existing application a second application for the offering is rejected`` () =
    given submitted |> whenAsked (ApplyForCourse (ApplicationId (Guid.NewGuid()), student, course, applied)) |> expectError AlreadyAppliedForCourse

[<Fact>]
let ``Given documentation was requested the application can still be approved`` () =
    given (submitted @ [MoreDocumentationRequested (application, "Certificate")])
    |> whenAsked (ApproveApplication (application, started)) |> expectEvents [ApplicationApproved (application, started)]

[<Fact>]
let ``Given a pending application each decision produces its own fact`` () =
    let state = given submitted
    state |> whenAsked (RejectApplication (application, "Missing prerequisite")) |> expectEvents [ApplicationRejected (application, "Missing prerequisite")]
    state |> whenAsked (RequestMoreDocumentation (application, "Certificate")) |> expectEvents [MoreDocumentationRequested (application, "Certificate")]

[<Fact>]
let ``Reasons are required for rejection documentation and discontinuation`` () =
    given submitted |> whenAsked (RejectApplication (application, " ")) |> expectError ReasonRequired
    given submitted |> whenAsked (RequestMoreDocumentation (application, "")) |> expectError ReasonRequired
    given participating |> whenAsked (DiscontinueParticipation (application, today, "")) |> expectError ReasonRequired

[<Fact>]
let ``Given an unknown application all application commands are rejected`` () =
    for command in [ ApproveApplication (application, started); RejectApplication (application, "reason"); RequestMoreDocumentation (application, "reason"); RegisterAttendance (application, today, Present); CompleteCourse (application, today, Passed); DiscontinueParticipation (application, today, "reason") ] do
        given created |> whenAsked command |> expectError ApplicationNotFound

[<Fact>]
let ``Approval dates respect application course and current dates`` () =
    for date in [applied.AddDays(-1); started.AddDays(-1); today.AddDays 1; DateOnly(2027, 6, 19)] do
        match given submitted |> whenAsked (ApproveApplication (application, date)) with
        | Error (InvalidDate _) -> ()
        | other -> failwithf "Expected date error for %A, got %A" date other

[<Fact>]
let ``An application cannot be submitted in the future or after course end`` () =
    for id, date in [course, today.AddDays 1; CourseId "foundation", today] do
        match given created |> whenAsked (ApplyForCourse (application, student, id, date)) with
        | Error (InvalidDate _) -> ()
        | other -> failwithf "Expected date error, got %A" other

[<Fact>]
let ``Attendance can be corrected with another event without duplicating the date`` () =
    let state = given (participating @ [AttendanceRegistered (application, today, Absent)])
    let events = state |> whenAsked (RegisterAttendance (application, today, Present)) |> mustSucceed
    Assert.Equal<Event list>([AttendanceRegistered (application, today, Present)], events)
    let next = List.fold Evolution.evolve state events
    Assert.Equal(1, next.Applications[application].Attendance.Count)
    Assert.Equal(Present, next.Applications[application].Attendance[today])

[<Fact>]
let ``Attendance before participation or in the future is rejected`` () =
    for date in [started.AddDays(-1); today.AddDays 1] do
        match given participating |> whenAsked (RegisterAttendance (application, date, Present)) with
        | Error (InvalidDate _) -> ()
        | other -> failwithf "Expected date error, got %A" other

[<Fact>]
let ``Completion and discontinuation cannot precede recorded attendance`` () =
    let state = given (participating @ [AttendanceRegistered (application, today, Present)])
    for command in [CompleteCourse (application, today.AddDays(-1), Passed); DiscontinueParticipation (application, today.AddDays(-1), "reason")] do
        match state |> whenAsked command with
        | Error (InvalidDate _) -> ()
        | other -> failwithf "Expected date error, got %A" other

[<Fact>]
let ``Every terminal status prevents attendance new decisions and another ending`` () =
    let endings = [ApplicationRejected (application, "reason"); CourseCompleted (application, today, Passed); CourseCompleted (application, today, NotPassed); CourseParticipationDiscontinued (application, today, "reason")]
    let commands = [RegisterAttendance (application, today, Present); ApproveApplication (application, started); RejectApplication (application, "reason"); RequestMoreDocumentation (application, "reason"); CompleteCourse (application, today, Passed); DiscontinueParticipation (application, today, "reason")]
    for ending in endings do
        let state = given (participating @ [ending])
        for command in commands do
            match state |> whenAsked command with
            | Error (InvalidTransition _) -> ()
            | other -> failwithf "Terminal state accepted %A: %A" command other

[<Fact>]
let ``Given approval evolution starts participation with an empty attendance record`` () =
    let state = given participating
    Assert.Equal(Participating started, state.Applications[application].Status)
    Assert.Empty(state.Applications[application].Attendance)

[<Fact>]
let ``Evolution retains participation start attendance and each distinct outcome`` () =
    let state = given (participating @ [AttendanceRegistered (application, today, Present)])
    for event, status in [CourseCompleted (application, today, Passed), Completed (started, today, Passed); CourseCompleted (application, today, NotPassed), Completed (started, today, NotPassed); CourseParticipationDiscontinued (application, today, "Moved"), Discontinued (started, today, "Moved")] do
        let next = Evolution.evolve state event
        Assert.Equal(status, next.Applications[application].Status)
        Assert.Equal<Map<DateOnly, Attendance>>(state.Applications[application].Attendance, next.Applications[application].Attendance)

[<Fact>]
let ``Evolution records submitted rejected and documentation states`` () =
    let state = given submitted
    Assert.Equal(Submitted, state.Applications[application].Status)
    Assert.Equal(applied, state.Applications[application].AppliedOn)
    Assert.Equal(student, state.Applications[application].StudentId)
    Assert.Equal(course, state.Applications[application].CourseId)
    Assert.Equal(Rejected "Reason", (Evolution.evolve state (ApplicationRejected (application, "Reason"))).Applications[application].Status)
    Assert.Equal(DocumentationRequested "Proof", (Evolution.evolve state (MoreDocumentationRequested (application, "Proof"))).Applications[application].Status)

[<Fact>]
let ``A failed bulk operation returns no accepted events or partially updated state`` () =
    let state = given submitted
    Evolution.decideBatch today state [ApproveApplication (application, started); ApproveApplication (ApplicationId (Guid.NewGuid()), started)]
    |> fun result -> Assert.True((result = Error ApplicationNotFound))
    Assert.Equal(Submitted, state.Applications[application].Status)

[<Fact>]
let ``A new student and first application are decided as one consistent batch`` () =
    let next, events = Evolution.decideBatch today Domain.empty [CreateStudent (student, details); ApplyForCourse (application, student, course, applied)] |> mustSucceed
    Assert.Equal<Event list>(submitted, events)
    Assert.Equal(given submitted, next)

[<Fact>]
let ``Prior participation counts only earlier started courses and handles no history`` () =
    let prior = ApplicationId (Guid.NewGuid())
    let another = ApplicationId (Guid.NewGuid())
    let history = participating @ [ApplicationSubmitted (prior, student, CourseId "foundation", DateOnly(2025, 8, 1)); ApplicationApproved (prior, DateOnly(2025, 8, 18)); CourseCompleted (prior, DateOnly(2026, 6, 19), Passed); ApplicationSubmitted (another, student, CourseId "design", applied)]
    let report = Reports.priorParticipation course (given history)
    Assert.Equal(0, report |> List.find (fun r -> r.Category = "No previous participation") |> _.Students)
    Assert.Equal(1, report |> List.find (fun r -> r.Category = "Foundation studies") |> _.Students)
    Assert.Equal(0, report |> List.find (fun r -> r.Category = "Design & communication") |> _.Students)
    let noHistory = Reports.priorParticipation course (given participating)
    Assert.Equal(1, noHistory.Head.Students)

[<Fact>]
let ``Same day or later participation is not prior participation`` () =
    for start in [started; today] do
        let other = ApplicationId (Guid.NewGuid())
        let state = given (participating @ [ApplicationSubmitted (other, student, CourseId "design", applied); ApplicationApproved (other, start)])
        Assert.Equal(1, (Reports.priorParticipation course state).Head.Students)

[<Fact>]
let ``Attendance aggregation distinguishes unknown from zero and excludes excused`` () =
    let state = given participating
    Assert.Equal(None, (Reports.attendance course state).Head.PresencePercent)
    let state = given (participating @ [AttendanceRegistered (application, started, Present); AttendanceRegistered (application, started.AddDays 1, Absent); AttendanceRegistered (application, started.AddDays 2, Excused)])
    let report = (Reports.attendance course state).Head
    Assert.Equal(Some 50, report.PresencePercent)
    Assert.Equal(3, report.Recorded)
    Assert.Equal(1, report.Excused)

[<Fact>]
let ``Composed applicant presentation inherits name links without the birth date column`` () =
    let state = given submitted
    let page = Pages.course 2L today state course
    let table = page.Tables[0]
    Assert.Equal<string array>([|"name"; "email"; "status"; "applied"|], table.Columns |> Array.map _.Key)
    Assert.Equal(Presentation.studentLink student, table.Rows[0].Cells[0].Href)
    Assert.True(table.Rows[0].Selectable)
    Assert.Equal("approve", table.BulkActions[0].FormId)
    Assert.Equal("status", table.Filters[0].ColumnKey)

[<Fact>]
let ``Terminal applications expose no mutating row actions or bulk selection`` () =
    let state = given (participating @ [CourseCompleted (application, today, Passed)])
    let row = (Pages.course 4L today state course).Tables[0].Rows[0]
    Assert.Empty(row.Actions)
    Assert.False(row.Selectable)

[<Fact>]
let ``Every page action resolves to a form or navigation destination`` () =
    let state, _ = Evolution.decideBatch Demo.today Domain.empty (Demo.commands ()) |> mustSucceed
    let pages = [Pages.students 0L state; Pages.newStudent 0L; Pages.courses 0L state; Pages.reports 0L state course; Pages.system 0L state []] @
                (Domain.courses |> List.map (fun c -> Pages.course 0L today state c.Id)) @
                (state.Students |> Map.toList |> List.map (fun (id, _) -> Pages.student 0L today state [] id))
    for page in pages do
        let actions = Array.concat [page.Actions; page.Tables |> Array.collect _.BulkActions; page.Tables |> Array.collect (fun t -> t.Rows |> Array.collect _.Actions)]
        for action in actions do
            Assert.True(action.Href <> "" || page.Forms |> Array.exists (fun f -> f.Id = action.FormId), sprintf "Unresolved action on %s" page.Title)

[<Fact>]
let ``Every event shape round trips through the version one codec`` () =
    let _, events = Evolution.decideBatch Demo.today Domain.empty (Demo.commands ()) |> mustSucceed
    let events = StudentDetailsChanged (student, details) :: events
    for event in events do
        Assert.Equal(event, Serialization.decode 1 (Event.name event) (Serialization.encode event))

[<Fact>]
let ``Unknown schema versions and mismatched event names fail closed`` () =
    let payload = Serialization.encode (StudentCreated (student, details))
    Assert.ThrowsAny<Exception>(fun () -> Serialization.decode 2 "StudentCreated" payload |> ignore) |> ignore
    Assert.ThrowsAny<Exception>(fun () -> Serialization.decode 1 "ApplicationApproved" payload |> ignore) |> ignore

[<Fact>]
let ``Committed version one payloads retain their historical meaning`` () =
    use fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "event-v1.json")))
    let decoded = fixture.RootElement.EnumerateArray() |> Seq.map (fun item -> Serialization.decode 1 (item.GetProperty("Case").GetString()) (item.GetRawText())) |> Seq.toList
    let expected = [
        StudentCreated (student, details)
        StudentDetailsChanged (student, { details with FullName = "Ada Changed" })
        ApplicationSubmitted (application, student, course, applied)
        MoreDocumentationRequested (application, "Certificate")
        ApplicationApproved (application, started)
        AttendanceRegistered (application, DateOnly(2026, 9, 1), Present)
        CourseCompleted (application, today, Passed)
        ApplicationRejected (application, "Prerequisite missing")
        CourseParticipationDiscontinued (application, today, "Moved") ]
    Assert.Equal<Event list>(expected, decoded)
    // The first seven events form a real lifecycle; the last two independently cover codec cases.
    let restored = decoded |> List.take 7 |> Evolution.replay
    Assert.Equal("Ada Changed", restored.Students[student].Details.FullName)
    Assert.Equal(Completed (started, today, Passed), restored.Applications[application].Status)

let withDatabase test =
    let directory = Path.Combine(Path.GetTempPath(), "student-office-tests-" + Guid.NewGuid().ToString())
    Directory.CreateDirectory directory |> ignore
    try test directory
    finally
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        Directory.Delete(directory, true)

[<Fact>]
let ``SQLite history reopens replays and backs up with the same state`` () =
    withDatabase (fun directory ->
        let path = Path.Combine(directory, "events.db")
        let backup = Path.Combine(directory, "backup.db")
        let expected, events = Evolution.decideBatch Demo.today Domain.empty (Demo.commands ()) |> mustSucceed
        do
            use store = new Store(path)
            store.Append(0L, events)
            store.Backup backup
        for filename in [path; backup] do
            use reopened = new Store(filename)
            let history = reopened.Read()
            Assert.Equal(events.Length, history.Length)
            Assert.Equal<int64 list>([1L..int64 events.Length], history |> List.map _.Sequence)
            Assert.Equal(expected, history |> List.map _.Event |> Evolution.replay))

[<Fact>]
let ``A stale writer appends nothing`` () =
    withDatabase (fun directory ->
        use store = new Store(Path.Combine(directory, "events.db"))
        store.Append(0L, created)
        Assert.ThrowsAny<Exception>(fun () -> store.Append(0L, [StudentDetailsChanged (student, details)])) |> ignore
        Assert.Single(store.Read()) |> ignore)

[<Fact>]
let ``Database guards prevent updating and deleting history`` () =
    withDatabase (fun directory ->
        let path = Path.Combine(directory, "events.db")
        use store = new Store(path)
        store.Append(0L, created)
        use connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path)
        connection.Open()
        for sql in ["UPDATE events SET event_type='Broken'"; "DELETE FROM events"] do
            use command = connection.CreateCommand()
            command.CommandText <- sql
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(fun () -> command.ExecuteNonQuery() |> ignore) |> ignore
        Assert.Single(store.Read()) |> ignore)

[<Fact>]
let ``A database failure halfway through a batch rolls back its earlier events`` () =
    withDatabase (fun directory ->
        let path = Path.Combine(directory, "events.db")
        use store = new Store(path)
        use connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path)
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "CREATE TRIGGER simulate_failure BEFORE INSERT ON events WHEN NEW.event_type='ApplicationSubmitted' BEGIN SELECT RAISE(ABORT, 'Simulated storage failure'); END;"
        command.ExecuteNonQuery() |> ignore
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(fun () -> store.Append(0L, submitted)) |> ignore
        Assert.Empty(store.Read()))


