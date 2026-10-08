namespace StudentApp

open System

// IDs and today's date are supplied by the shell; decisions never read a clock or generate randomness.
type Command =
    | CreateStudent of StudentId * PersonalDetails
    | ChangeStudentDetails of StudentId * PersonalDetails
    | ApplyForCourse of StudentApp.ApplicationId * StudentId * CourseId * appliedOn: DateOnly
    | ApproveApplication of StudentApp.ApplicationId * startsOn: DateOnly
    | RejectApplication of StudentApp.ApplicationId * reason: string
    | RequestMoreDocumentation of StudentApp.ApplicationId * reason: string
    | RegisterAttendance of StudentApp.ApplicationId * DateOnly * Attendance
    | CompleteCourse of StudentApp.ApplicationId * endsOn: DateOnly * CompletionResult
    | DiscontinueParticipation of StudentApp.ApplicationId * endsOn: DateOnly * reason: string

type DomainError =
    | StudentNotFound
    | ApplicationNotFound
    | CourseNotFound
    | IdentityAlreadyExists
    | InvalidPersonalDetails of string
    | AlreadyAppliedForCourse
    | InvalidTransition of string
    | InvalidDate of string
    | ReasonRequired

module DomainError =
    let describe = function
        | StudentNotFound -> "This student does not exist."
        | ApplicationNotFound -> "This application does not exist."
        | CourseNotFound -> "Choose a known course."
        | IdentityAlreadyExists -> "This identity already exists."
        | InvalidPersonalDetails message | InvalidTransition message | InvalidDate message -> message
        | AlreadyAppliedForCourse -> "This student already has an application for this course offering."
        | ReasonRequired -> "Please enter a reason."

