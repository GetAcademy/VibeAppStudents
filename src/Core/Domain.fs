namespace StudentApp

open System

// An identity stays the same even when a person's details change.
type StudentId = StudentId of Guid
type ApplicationId = ApplicationId of Guid
type CourseId = CourseId of string

type PersonalDetails = { FullName: string; Email: string; BirthDate: DateOnly }
type Student = { Id: StudentId; Details: PersonalDetails }
type Course = { Id: CourseId; Title: string; StartsOn: DateOnly; EndsOn: DateOnly }

type Attendance = Present | Absent | Excused
type CompletionResult = Passed | NotPassed

// Approval starts participation. Terminal outcomes retain the participation dates.
type ApplicationStatus =
    | Submitted
    | DocumentationRequested of reason: string
    | Rejected of reason: string
    | Participating of startedOn: DateOnly
    | Completed of startedOn: DateOnly * endedOn: DateOnly * result: CompletionResult
    | Discontinued of startedOn: DateOnly * endedOn: DateOnly * reason: string

type Application = {
    Id: ApplicationId
    StudentId: StudentId
    CourseId: CourseId
    AppliedOn: DateOnly
    Status: ApplicationStatus
    Attendance: Map<DateOnly, Attendance>
}

type State = { Students: Map<StudentId, Student>; Applications: Map<ApplicationId, Application> }

module Domain =
    let empty = { Students = Map.empty; Applications = Map.empty }
    let courses = [
        { Id = CourseId "foundation"; Title = "Foundation studies"; StartsOn = DateOnly(2025, 8, 18); EndsOn = DateOnly(2026, 6, 19) }
        { Id = CourseId "digital"; Title = "Digital skills"; StartsOn = DateOnly(2026, 8, 17); EndsOn = DateOnly(2027, 6, 18) }
        { Id = CourseId "design"; Title = "Design & communication"; StartsOn = DateOnly(2026, 8, 17); EndsOn = DateOnly(2027, 6, 18) }
    ]
    let course id = courses |> List.tryFind (fun c -> c.Id = id)
    let courseTitle id = course id |> Option.map _.Title |> Option.defaultValue "Unknown course"
    let statusLabel = function
        | Submitted -> "Submitted"
        | DocumentationRequested _ -> "Documentation requested"
        | Rejected _ -> "Rejected"
        | Participating _ -> "Participating"
        | Completed (_, _, Passed) -> "Completed · passed"
        | Completed (_, _, NotPassed) -> "Completed · not passed"
        | Discontinued _ -> "Discontinued"
    let awaitingDecision = function Submitted | DocumentationRequested _ -> true | _ -> false
    let participationStart = function
        | Participating start | Completed (start, _, _) | Discontinued (start, _, _) -> Some start
        | _ -> None
