module StudentApp.Fixtures

open System
open Xunit
open StudentApp

let today = DateOnly(2026, 10, 8)
let student = StudentId (Guid.Parse "10000000-0000-0000-0000-000000000001")
let application = ApplicationId (Guid.Parse "20000000-0000-0000-0000-000000000001")
let course = CourseId "digital"
let details = { FullName = "Ada Example"; Email = "ada@example.test"; BirthDate = DateOnly(2000, 1, 2) }
let applied = DateOnly(2026, 8, 10)
let started = DateOnly(2026, 8, 17)
let created = [StudentCreated (student, details)]
let submitted = created @ [ApplicationSubmitted (application, student, course, applied)]
let participating = submitted @ [ApplicationApproved (application, started)]
let given history = Evolution.replay history
let whenAsked command state = Rules.decide today state command
let expectEvents events actual = Assert.True((actual = Ok events), sprintf "Expected %A, got %A" events actual)
let expectError error actual = Assert.True((actual = Error error), sprintf "Expected %A, got %A" error actual)
let mustSucceed = function Ok result -> result | Error error -> failwithf "Unexpected error: %A" error


