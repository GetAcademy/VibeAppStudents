namespace StudentApp.Server

open System
open StudentApp

module Demo =
    let today = DateOnly(2026, 10, 8)
    let commands () =
        let names = ["Amara Okafor"; "Jonas Berg"; "Sofia Lind"; "Elias Hansen"; "Maya Patel"; "Oliver Dahl"; "Nora Solberg"; "Lucas Nguyen"; "Ingrid Moen"; "Adam Ali"; "Freya Nilsen"; "Leo Johansen"; "Sara Ahmed"; "Emil Strand"; "Thea Bakke"; "Noah Eriksen"]
        names |> List.mapi (fun index name ->
            let student = StudentId (Guid.NewGuid())
            let current = ApplicationId (Guid.NewGuid())
            let prior = ApplicationId (Guid.NewGuid())
            let details = { FullName = name; Email = name.ToLowerInvariant().Replace(' ', '.') + "@example.test"; BirthDate = DateOnly(1998 + index % 7, 1 + index % 12, 5 + index) }
            [ yield CreateStudent (student, details)
              if index < 5 then
                  yield ApplyForCourse (prior, student, CourseId "foundation", DateOnly(2025, 8, 1))
                  yield ApproveApplication (prior, DateOnly(2025, 8, 18))
                  yield RegisterAttendance (prior, DateOnly(2025, 9, 1), Present)
                  yield CompleteCourse (prior, DateOnly(2026, 6, 19), if index = 4 then NotPassed else Passed)
              yield ApplyForCourse (current, student, CourseId (if index > 11 then "design" else "digital"), DateOnly(2026, 8, 10))
              if index < 7 || index > 13 then
                  yield ApproveApplication (current, DateOnly(2026, 8, 17))
                  for day in 1..5 do
                      yield RegisterAttendance (current, DateOnly(2026, 9, day), if (index + day) % 5 = 0 then Absent elif (index + day) % 7 = 0 then Excused else Present)
                  if index = 5 then yield DiscontinueParticipation (current, DateOnly(2026, 9, 15), "Changed study plans")
                  if index = 6 then yield CompleteCourse (current, DateOnly(2026, 9, 30), NotPassed)
              elif index = 8 then yield RequestMoreDocumentation (current, "Please provide prior education certificate.")
              elif index = 9 then yield RejectApplication (current, "Prerequisite qualification not met.") ]) |> List.concat
