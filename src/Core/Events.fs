namespace StudentApp

open System

type Event =
    | StudentCreated of StudentId * PersonalDetails
    | StudentDetailsChanged of StudentId * PersonalDetails
    | ApplicationSubmitted of StudentApp.ApplicationId * StudentId * CourseId * DateOnly
    | ApplicationApproved of StudentApp.ApplicationId * DateOnly
    | ApplicationRejected of StudentApp.ApplicationId * string
    | MoreDocumentationRequested of StudentApp.ApplicationId * string
    | AttendanceRegistered of StudentApp.ApplicationId * DateOnly * Attendance
    | CourseCompleted of StudentApp.ApplicationId * DateOnly * CompletionResult
    | CourseParticipationDiscontinued of StudentApp.ApplicationId * DateOnly * string

module Event =
    let name = function
        | StudentCreated _ -> "StudentCreated"
        | StudentDetailsChanged _ -> "StudentDetailsChanged"
        | ApplicationSubmitted _ -> "ApplicationSubmitted"
        | ApplicationApproved _ -> "ApplicationApproved"
        | ApplicationRejected _ -> "ApplicationRejected"
        | MoreDocumentationRequested _ -> "MoreDocumentationRequested"
        | AttendanceRegistered _ -> "AttendanceRegistered"
        | CourseCompleted _ -> "CourseCompleted"
        | CourseParticipationDiscontinued _ -> "CourseParticipationDiscontinued"

    let belongsTo studentId state = function
        | StudentCreated (id, _) | StudentDetailsChanged (id, _) -> id = studentId
        | ApplicationSubmitted (_, id, _, _) -> id = studentId
        | ApplicationApproved (id, _) | ApplicationRejected (id, _) | MoreDocumentationRequested (id, _)
        | AttendanceRegistered (id, _, _) | CourseCompleted (id, _, _) | CourseParticipationDiscontinued (id, _, _) ->
            state.Applications |> Map.tryFind id |> Option.exists (fun a -> a.StudentId = studentId)

