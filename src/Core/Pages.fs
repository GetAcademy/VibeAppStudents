namespace StudentApp

open System
open Ui
open Presentation

module Pages =
    type HistoryItem = { Sequence: int64; Timestamp: string; Event: Event; Payload: string }
    let navigation = [|
        { Label = "Students"; Href = "#students" }
        { Label = "Courses"; Href = "#courses" }
        { Label = "Reports"; Href = "#reports" }
        { Label = "System"; Href = "#system" }
    |]
    let private page revision title description =
        { Title = title; Eyebrow = "STUDENT OFFICE / EXPERIMENT 0.1"; Description = description; Revision = revision; Navigation = navigation
          Facts = [||]; Actions = [||]; Forms = [||]; Tables = [||] }

    let private historyTable title history =
        table "history" title "Append-only history, ordered by event sequence. Recorded time is separate from the effective dates inside events." [
            column "sequence" "Sequence" (fun h -> string h.Sequence) |> formatted "number"
            column "time" "Recorded (UTC)" _.Timestamp
            column "event" "Event" (fun h -> Event.name h.Event)
            column "payload" "Details" _.Payload |> formatted "details"
        ] |> sortBy ["sequence"; "event"] "sequence" |> renderTable (fun h -> string h.Sequence) history

    let students revision state =
        let rows = state.Students |> Map.toList |> List.map snd
        { page revision "Students" "A clear view of every learner, from first application to final outcome." with
            Facts = [| { Label = "Students"; Value = string rows.Length }; { Label = "Applications"; Value = string state.Applications.Count }; { Label = "Active participants"; Value = state.Applications |> Map.filter (fun _ a -> match a.Status with Participating _ -> true | _ -> false) |> Map.count |> string } |]
            Actions = [| navigate "New student" "#new" |]
            Tables = [|
                studentTable
                |> addColumns [column "participation" "Participation" (fun (s: Student) ->
                    if state.Applications |> Map.exists (fun _ a -> a.StudentId = s.Id && match a.Status with Participating _ -> true | _ -> false) then "Active"
                    else "Not currently active") |> formatted "status"]
                |> filterBy "participation" "Participation"
                |> renderTable (fun s -> studentKey s.Id) rows
            |] }

    let newStudent revision =
        { page revision "A new beginning" "Create a student, optionally with their first course application. Both are saved together." with
            Forms = [| form "create" "Student & first application" "Personal details can change later; the generated student identity stays the same." CreateStudentAndApply "Create student" (personalDetailsFields None @ [{ courseField with Required = false }]) |] }

    let student revision today state history id =
        let student = state.Students[id]
        let applications = state.Applications |> Map.toList |> List.map snd |> List.filter (fun a -> a.StudentId = id)
        let applicationTable =
            table "applications" "Course history" "Each row is one application to a course offering." [
                column "course" "Course" (fun a -> Domain.courseTitle a.CourseId) |> linked (fun a -> courseLink a.CourseId)
                column "applied" "Applied" (fun a -> dateText a.AppliedOn) |> formatted "date"
                column "status" "Status" (fun a -> Domain.statusLabel a.Status) |> formatted "status"
                column "reason" "Decision / outcome" (fun a -> match a.Status with DocumentationRequested reason | Rejected reason | Discontinued (_, _, reason) -> reason | Completed (_, finish, _) -> "Ended " + dateText finish | Participating start -> "Started " + dateText start | _ -> "Awaiting a decision")
            ] |> rowActions applicationActions |> sortBy ["course"; "applied"; "status"] "applied" |> renderTable (fun a -> applicationKey a.Id) applications
        let marks = applications |> List.collect (fun a -> a.Attendance |> Map.toList |> List.map (fun (date, mark) -> a, date, mark))
        let attendanceTable =
            table "attendance" "Attendance record" "Only registered dates appear. Unrecorded dates are unknown." [
                column "course" "Course" (fun (a, _, _) -> Domain.courseTitle a.CourseId)
                column "date" "Date" (fun (_, date, _) -> dateText date) |> formatted "date"
                column "mark" "Mark" (fun (_, _, mark) -> string mark) |> formatted "status"
            ] |> filterBy "course" "Course" |> sortBy ["date"; "course"; "mark"] "date"
              |> renderTable (fun (a, date, _) -> applicationKey a.Id + dateText date) marks
        { page revision student.Details.FullName "Personal details, course decisions and the history behind the current state." with
            Facts = [| { Label = "Email"; Value = student.Details.Email }; { Label = "Birth date"; Value = dateText student.Details.BirthDate }; { Label = "Student ID"; Value = studentKey id } |]
            Actions = [| action "Edit details" "edit" (studentKey id); action "Apply for a course" "apply" (studentKey id) |]
            Forms = [ form "edit" "Edit personal details" "Identity and existing course history are preserved." EditStudent "Save details" (personalDetailsFields (Some student.Details))
                      form "apply" "Apply for a course" "One application per student per course offering." Apply "Submit application" [courseField] ] @ participationForms today |> Array.ofList
            Tables = [| applicationTable; attendanceTable; historyTable "Student event history" (history |> List.filter (fun h -> Event.belongsTo id state h.Event)) |] }

    let courses revision state =
        let courseTable =
            table "courses" "Course offerings" "Reference data is defined in Domain.fs for this experiment." [
                column "title" "Course" (fun (c: Course) -> c.Title) |> linked (fun c -> courseLink c.Id)
                column "starts" "Starts" (fun c -> dateText c.StartsOn) |> formatted "date"
                column "ends" "Ends" (fun c -> dateText c.EndsOn) |> formatted "date"
                column "applications" "Applications" (fun (c: Course) -> state.Applications |> Map.filter (fun _ a -> a.CourseId = c.Id) |> Map.count |> string) |> formatted "number"
            ] |> sortBy ["title"; "starts"; "applications"] "title" |> renderTable (fun c -> courseKey c.Id) Domain.courses
        { page revision "Courses" "Explore each cohort and manage applications in one place." with Tables = [| courseTable |] }

    let course revision today state id =
        let course = Domain.course id |> Option.get
        let rows = state.Applications |> Map.toList |> List.map snd |> List.filter (fun a -> a.CourseId = id) |> List.map (fun a -> { Student = state.Students[a.StudentId]; Application = a })
        { page revision course.Title (dateText course.StartsOn + " — " + dateText course.EndsOn) with
            Actions = [| navigate "View course reports" ("#reports/" + courseKey id) |]
            Forms = participationForms today |> Array.ofList
            Tables = [| applicantsTable |> renderTable (fun r -> applicationKey r.Application.Id) rows |] }

    let reports revision state id =
        let previous = Reports.priorParticipation id state
        let attendance = Reports.attendance id state
        let priorTable =
            table "prior" "Before this course" "Includes everyone who started this course. Prior courses started strictly earlier. A learner can appear in multiple prior-course groups; the no-previous-participation group is exclusive." [
                column "category" "Previous participation" (fun (r: Reports.PriorParticipation) -> r.Category)
                column "students" "Learners" (fun (r: Reports.PriorParticipation) -> string r.Students) |> formatted "number"
            ] |> renderTable _.Category previous
        let attendanceTable =
            table "summary" "Attendance overview" "Presence = present ÷ (present + absent). Excused dates are excluded. Missing dates are unknown; no recorded days is not 0% attendance." [
                column "name" "Student" (fun (r: Reports.AttendanceSummary) -> r.Student.Details.FullName) |> linked (fun r -> studentLink r.Student.Id)
                column "present" "Present" (fun (r: Reports.AttendanceSummary) -> string r.Present) |> formatted "number"
                column "absent" "Absent" (fun (r: Reports.AttendanceSummary) -> string r.Absent) |> formatted "number"
                column "excused" "Excused" (fun (r: Reports.AttendanceSummary) -> string r.Excused) |> formatted "number"
                column "rate" "Presence rate" (fun r -> r.PresencePercent |> Option.map (fun p -> string p + "%") |> Option.defaultValue "No data")
            ] |> sortBy ["name"; "present"; "absent"] "name" |> renderTable (fun r -> applicationKey r.Application.Id) attendance
        { page revision "Reports" (Domain.courseTitle id + " · transparent queries over current state") with
            Actions = Domain.courses |> List.map (fun c -> navigate c.Title ("#reports/" + courseKey c.Id)) |> Array.ofList
            Facts = [| { Label = "Cohort participants"; Value = string attendance.Length }; { Label = "Recorded attendance marks"; Value = attendance |> List.sumBy _.Recorded |> string } |]
            Tables = [| priorTable; attendanceTable |] }

    let system revision state history =
        { page revision "System administration" "Local prototype only. No authentication or authorization. Do not expose this application or use real personal data." with
            Facts = [| { Label = "Persisted events"; Value = string revision }; { Label = "Students in projection"; Value = string state.Students.Count }; { Label = "Applications in projection"; Value = string state.Applications.Count } |]
            Actions = [| action "Rebuild current state" "rebuild" ""; navigate "Download SQLite backup" "/api/backup"; navigate "Inspect projection JSON" "/api/state" |]
            Forms = [| form "rebuild" "Rebuild current state" "Replay the persisted event log through Evolution.evolve. The event log is preserved." Rebuild "Rebuild from history" [] |]
            Tables = [| historyTable "Persisted event log" history |] }


