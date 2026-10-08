namespace StudentApp

module Evolution =
    let private updateApplication id change state =
        { state with Applications = state.Applications |> Map.change id (Option.map change) }

    // Evolution applies accepted facts. Business validation belongs in Rules, not here.
    let evolve state event =
        match event with
        | StudentCreated (id, details) | StudentDetailsChanged (id, details) ->
            { state with Students = state.Students |> Map.add id { Id = id; Details = details } }
        | ApplicationSubmitted (id, student, course, date) ->
            let application = { Id = id; StudentId = student; CourseId = course; AppliedOn = date; Status = Submitted; Attendance = Map.empty }
            { state with Applications = state.Applications |> Map.add id application }
        | ApplicationApproved (id, start) ->
            updateApplication id (fun a -> { a with Status = Participating start }) state
        | ApplicationRejected (id, reason) ->
            updateApplication id (fun a -> { a with Status = Rejected reason }) state
        | MoreDocumentationRequested (id, reason) ->
            updateApplication id (fun a -> { a with Status = DocumentationRequested reason }) state
        | AttendanceRegistered (id, date, mark) ->
            updateApplication id (fun a -> { a with Attendance = a.Attendance |> Map.add date mark }) state
        | CourseCompleted (id, finish, result) ->
            updateApplication id (fun a -> { a with Status = Completed (Domain.participationStart a.Status |> Option.get, finish, result) }) state
        | CourseParticipationDiscontinued (id, finish, reason) ->
            updateApplication id (fun a -> { a with Status = Discontinued (Domain.participationStart a.Status |> Option.get, finish, reason) }) state

    let replay events = List.fold evolve Domain.empty events

    // Used by bulk approval and the new-student + application workflow: all commands must succeed.
    let decideBatch today state commands =
        let folder result command =
            result |> Result.bind (fun (current, accepted) ->
                Rules.decide today current command
                |> Result.map (fun events -> List.fold evolve current events, accepted @ events))
        commands |> List.fold folder (Ok (state, []))
