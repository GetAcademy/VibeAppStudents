namespace StudentApp

module Reports =
    type PriorParticipation = { Category: string; Students: int }
    type AttendanceSummary = { Student: Student; Application: Application; Present: int; Absent: int; Excused: int; Recorded: int; PresencePercent: int option }

    // Cohort = anyone who started the selected course, including ended participation.
    // Prior means started strictly before this student's selected-course participation.
    // A student may appear in several prior-course categories. "Never" is exclusive.
    let priorParticipation courseId state =
        let cohort =
            state.Applications |> Map.toList |> List.map snd
            |> List.filter (fun a -> a.CourseId = courseId)
            |> List.choose (fun a -> Domain.participationStart a.Status |> Option.map (fun start -> a.StudentId, start))
        let priorCourses (student, start) =
            state.Applications |> Map.toList |> List.map snd
            |> List.filter (fun a -> a.StudentId = student && a.CourseId <> courseId && Domain.participationStart a.Status |> Option.exists (fun prior -> prior < start))
            |> List.map _.CourseId |> Set.ofList
        let histories = cohort |> List.map priorCourses
        { Category = "No previous participation"; Students = histories |> List.filter Set.isEmpty |> List.length }
        :: (Domain.courses |> List.filter (fun c -> c.Id <> courseId)
            |> List.map (fun c -> { Category = c.Title; Students = histories |> List.filter (Set.contains c.Id) |> List.length }))

    // No timetable exists in v0.1: missing marks are unknown, never inferred as absences.
    // Presence rate = present / (present + absent); excused days are excluded.
    let attendance courseId state =
        state.Applications |> Map.toList |> List.map snd
        |> List.filter (fun a -> a.CourseId = courseId && Option.isSome (Domain.participationStart a.Status))
        |> List.map (fun a ->
            let count mark = a.Attendance |> Map.filter (fun _ value -> value = mark) |> Map.count
            let present, absent, excused = count Present, count Absent, count Excused
            { Student = state.Students[a.StudentId]; Application = a; Present = present; Absent = absent; Excused = excused
              Recorded = a.Attendance.Count
              PresencePercent = if present + absent = 0 then None else Some (100 * present / (present + absent)) })
