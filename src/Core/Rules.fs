namespace StudentApp

open System

module Rules =
    let private validDetails today details =
        if String.IsNullOrWhiteSpace details.FullName || details.FullName.Trim().Length > 120 then
            Error (InvalidPersonalDetails "A name of 1–120 characters is required.")
        elif String.IsNullOrWhiteSpace details.Email || not (details.Email.Contains('@')) || details.Email.Length > 200 then
            Error (InvalidPersonalDetails "Enter an email address of at most 200 characters.")
        elif details.BirthDate > today || details.BirthDate < DateOnly(1900, 1, 1) then
            Error (InvalidPersonalDetails "Birth date must be between 1900 and today.")
        else Ok ()

    let private reasonRequired reason event =
        if String.IsNullOrWhiteSpace reason then Error ReasonRequired else Ok [event]

    let private withApplication state id rule =
        match Map.tryFind id state.Applications with
        | None -> Error ApplicationNotFound
        | Some application -> rule application

    let private awaitingDecision application rule =
        if Domain.awaitingDecision application.Status then rule ()
        else Error (InvalidTransition "Only submitted applications or requests for documentation can be decided.")

    let private duringParticipation today application date rule =
        match application.Status with
        | Participating start ->
            let course = Domain.course application.CourseId |> Option.get
            if date < start || date > course.EndsOn || date > today then
                Error (InvalidDate "Date must be within participation, no later than today or the course end.")
            else rule ()
        | _ -> Error (InvalidTransition "This action requires active participation.")

    // Every accepted command produces facts; rejection produces no events and changes no state.
    let decide (today: DateOnly) (state: State) command : Result<Event list, DomainError> =
        match command with
        | CreateStudent (id, details) ->
            if Map.containsKey id state.Students then Error IdentityAlreadyExists
            else validDetails today details |> Result.map (fun () -> [StudentCreated (id, details)])
        | ChangeStudentDetails (id, details) ->
            if not (Map.containsKey id state.Students) then Error StudentNotFound
            else validDetails today details |> Result.map (fun () -> [StudentDetailsChanged (id, details)])
        | ApplyForCourse (id, student, courseId, date) ->
            if Map.containsKey id state.Applications then Error IdentityAlreadyExists
            elif not (Map.containsKey student state.Students) then Error StudentNotFound
            else
                match Domain.course courseId with
                | None -> Error CourseNotFound
                | Some course ->
                    if date > today || date > course.EndsOn then Error (InvalidDate "Application date cannot be in the future or after the course ends.")
                    elif state.Applications |> Map.exists (fun _ a -> a.StudentId = student && a.CourseId = courseId) then Error AlreadyAppliedForCourse
                    else Ok [ApplicationSubmitted (id, student, courseId, date)]
        | ApproveApplication (id, start) ->
            withApplication state id (fun a -> awaitingDecision a (fun () ->
                let course = Domain.course a.CourseId |> Option.get
                if start < a.AppliedOn || start < course.StartsOn || start > course.EndsOn || start > today then
                    Error (InvalidDate "Participation must start on or after application and course start, within the course, and no later than today.")
                else Ok [ApplicationApproved (id, start)]))
        | RejectApplication (id, reason) ->
            withApplication state id (fun a -> awaitingDecision a (fun () -> reasonRequired reason (ApplicationRejected (id, reason))))
        | RequestMoreDocumentation (id, reason) ->
            withApplication state id (fun a -> awaitingDecision a (fun () -> reasonRequired reason (MoreDocumentationRequested (id, reason))))
        | RegisterAttendance (id, date, attendance) ->
            // Re-registering a date is an audited correction; it replaces that day's current mark.
            withApplication state id (fun a -> duringParticipation today a date (fun () -> Ok [AttendanceRegistered (id, date, attendance)]))
        | CompleteCourse (id, finish, result) ->
            withApplication state id (fun a -> duringParticipation today a finish (fun () ->
                if a.Attendance |> Map.exists (fun date _ -> date > finish) then Error (InvalidDate "End date cannot precede recorded attendance.")
                else Ok [CourseCompleted (id, finish, result)]))
        | DiscontinueParticipation (id, finish, reason) ->
            withApplication state id (fun a -> duringParticipation today a finish (fun () ->
                if a.Attendance |> Map.exists (fun date _ -> date > finish) then Error (InvalidDate "End date cannot precede recorded attendance.")
                else reasonRequired reason (CourseParticipationDiscontinued (id, finish, reason))))
