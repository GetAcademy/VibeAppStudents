namespace StudentApp

open System
open Ui

module Presentation =
    let studentKey (StudentId id) = string id
    let applicationKey (ApplicationId id) = string id
    let courseKey (CourseId id) = id
    let dateText (date: DateOnly) = date.ToString("yyyy-MM-dd")
    let studentLink id = "#student/" + studentKey id
    let courseLink id = "#course/" + courseKey id

    // These columns are reused, not copied, by the course applicant presentation.
    let standardStudentPresentation = [
        column "name" "Student" (fun (s: Student) -> s.Details.FullName) |> linked (fun s -> studentLink s.Id)
        column "email" "Email" (fun s -> s.Details.Email)
        column "birthDate" "Birth date" (fun s -> dateText s.Details.BirthDate) |> formatted "date"
    ]

    let studentTable =
        table "students" "Student directory" "Select a name to view details, applications and the event history." standardStudentPresentation
        |> sortBy ["name"; "email"; "birthDate"] "name"

    let personalDetailsFields (details: PersonalDetails option) = [
        { text "name" "Full name" with Value = details |> Option.map _.FullName |> Option.defaultValue "" }
        { field "email" "Email address" "email" true "" with Value = details |> Option.map _.Email |> Option.defaultValue "" }
        date "birthDate" "Birth date" (details |> Option.map (fun d -> dateText d.BirthDate) |> Option.defaultValue "")
    ]

    let courseField = select "course" "Course offering" (Domain.courses |> List.map (fun c -> choice (courseKey c.Id) c.Title))

    let applicationActions (a: Application) =
        let id = applicationKey a.Id
        match a.Status with
        | Submitted | DocumentationRequested _ -> [action "Approve" "approve" id; action "Request documents" "documents" id; action "Reject" "reject" id]
        | Participating _ -> [action "Attendance" "attendance" id; action "Complete" "complete" id; action "Discontinue" "discontinue" id]
        | Rejected _ | Completed _ | Discontinued _ -> []

    type ApplicantRow = { Student: Student; Application: Application }
    let applicantPresentation =
        standardStudentPresentation
        |> List.map (fun c -> { Description = c.Description; Read = (fun row -> c.Read row.Student); Link = (fun row -> c.Link row.Student) })

    let applicantsTable =
        table "applicants" "Applications & participants" "Approval starts participation. Select pending applications for a single atomic bulk approval." applicantPresentation
        |> withoutColumns ["birthDate"]
        |> addColumns [
            column "status" "Status" (fun r -> Domain.statusLabel r.Application.Status) |> formatted "status"
            column "applied" "Applied" (fun r -> dateText r.Application.AppliedOn) |> formatted "date"
        ]
        |> filterBy "status" "Application state"
        |> sortBy ["name"; "status"; "applied"] "name"
        |> rowActions (fun r -> applicationActions r.Application)
        |> bulkAction (fun r -> Domain.awaitingDecision r.Application.Status) (action "Approve selected" "approve" "")

    let participationForms today = [
        form "approve" "Approve application" "Approval starts participation on the date below. For a bulk action, every selected application must be eligible." Approve "Approve & start participation" [date "date" "Participation starts" (dateText today)]
        form "documents" "Request more documentation" "The application remains open for a later decision." AskForDocuments "Request documentation" [text "reason" "Documents needed / reason"]
        form "reject" "Reject application" "Rejection closes this application for this course offering." Reject "Reject application" [text "reason" "Reason for rejection"]
        form "attendance" "Register attendance" "One mark per date. Re-registering a date records a correction in the event history." MarkAttendance "Save attendance" [date "date" "Attendance date" (dateText today); select "mark" "Attendance" [choice "present" "Present"; choice "absent" "Absent"; choice "excused" "Excused"]]
        form "complete" "Complete participation" "The result is final in this prototype." Complete "Complete course" [date "date" "Completion date" (dateText today); select "result" "Result" [choice "passed" "Passed"; choice "notPassed" "Not passed"]]
        form "discontinue" "Discontinue participation" "Discontinuation ends participation without a completion result." Discontinue "Discontinue participation" [date "date" "End date" (dateText today); text "reason" "Reason"]
    ]

