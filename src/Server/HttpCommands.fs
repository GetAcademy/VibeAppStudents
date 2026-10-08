namespace StudentApp.Server

open System
open StudentApp

module HttpCommands =
    // This is only HTTP decoding. Business permissions and validation remain in Rules.fs.
    let decode today (request: Ui.Request) =
        let value key = request.Values |> Array.tryFind (fun v -> v.Key = key) |> Option.map _.Value |> Option.defaultValue ""
        let date key = DateOnly.ParseExact(value key, "yyyy-MM-dd")
        let studentId () = StudentId (Guid.Parse request.Target)
        let applicationId () = ApplicationId (Guid.Parse request.Target)
        let details () = { FullName = (value "name").Trim(); Email = (value "email").Trim(); BirthDate = date "birthDate" }
        let one command = [command], ""
        match request.Intent with
        | "create" ->
            let id = StudentId (Guid.NewGuid())
            [ yield CreateStudent (id, details ())
              if value "course" <> "" then yield ApplyForCourse (ApplicationId (Guid.NewGuid()), id, CourseId (value "course"), today) ], Presentation.studentLink id
        | "edit" -> one (ChangeStudentDetails (studentId (), details ()))
        | "apply" -> one (ApplyForCourse (ApplicationId (Guid.NewGuid()), studentId (), CourseId (value "course"), today))
        | "approve" ->
            let targets = if request.Targets.Length > 0 then request.Targets else [| request.Target |]
            targets |> Array.distinct |> Array.map (fun id -> ApproveApplication (ApplicationId (Guid.Parse id), date "date")) |> Array.toList, ""
        | "reject" -> one (RejectApplication (applicationId (), value "reason"))
        | "documents" -> one (RequestMoreDocumentation (applicationId (), value "reason"))
        | "attendance" ->
            let mark = match value "mark" with "present" -> Present | "absent" -> Absent | "excused" -> Excused | _ -> invalidArg "mark" "Choose a valid attendance mark."
            one (RegisterAttendance (applicationId (), date "date", mark))
        | "complete" ->
            let result = match value "result" with "passed" -> Passed | "notPassed" -> NotPassed | _ -> invalidArg "result" "Choose a valid completion result."
            one (CompleteCourse (applicationId (), date "date", result))
        | "discontinue" -> one (DiscontinueParticipation (applicationId (), date "date", value "reason"))
        | _ -> invalidArg "intent" "Unknown command."
