namespace StudentApp.Server

open System
open System.IO
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open StudentApp

module Program =
    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        let port = Environment.GetEnvironmentVariable "STUDENT_PORT" |> Option.ofObj |> Option.map int |> Option.defaultValue 5080
        builder.WebHost.UseSetting("urls", sprintf "http://127.0.0.1:%d" port) |> ignore
        let app = builder.Build()
        let path = Environment.GetEnvironmentVariable "STUDENT_DB" |> Option.ofObj |> Option.defaultValue (Path.Combine(builder.Environment.ContentRootPath, "../../data/students.db")) |> Path.GetFullPath
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        use store = new Store(path)
        let gate = obj()
        let mutable history = store.Read()
        if List.isEmpty history && Array.contains "--demo" args then
            match Evolution.decideBatch Demo.today Domain.empty (Demo.commands ()) with
            | Error error -> failwith (DomainError.describe error)
            | Ok (_, events) -> store.Append(0L, events); history <- store.Read()
        let mutable state = history |> List.map _.Event |> Evolution.replay
        let revision () = history |> List.tryLast |> Option.map _.Sequence |> Option.defaultValue 0L
        let today () = DateOnly.FromDateTime(DateTime.Today)
        let json value = Results.Json(value, Serialization.options)
        let response (status: int) message = Results.Json({ Ui.Response.Message = message; Ui.Response.NavigateTo = "" }, Serialization.options, statusCode = Nullable status)

        app.UseDefaultFiles() |> ignore
        app.UseStaticFiles() |> ignore
        app.MapGet("/api/page", Func<HttpContext, IResult>(fun context ->
            lock gate (fun () ->
                try
                    let route = string context.Request.Query["route"]
                    let key = string context.Request.Query["key"]
                    let page =
                        match route with
                        | "" | "students" -> Pages.students (revision ()) state
                        | "new" -> Pages.newStudent (revision ())
                        | "student" -> Pages.student (revision ()) (today ()) state history (StudentId (Guid.Parse key))
                        | "courses" -> Pages.courses (revision ()) state
                        | "course" -> Pages.course (revision ()) (today ()) state (CourseId key)
                        | "reports" ->
                            let id = CourseId (if key = "" then "digital" else key)
                            if Domain.course id |> Option.isNone then invalidArg "key" "Unknown course."
                            Pages.reports (revision ()) state id
                        | "system" -> Pages.system (revision ()) state history
                        | _ -> invalidArg "route" "Page not found."
                    json page
                with :? ArgumentException | :? FormatException | :? Collections.Generic.KeyNotFoundException -> response 404 "Page not found."))) |> ignore

        app.MapPost("/api/command", Func<HttpContext, Threading.Tasks.Task<IResult>>(fun context -> task {
            // JSON-only POSTs plus same-origin checks avoid drive-by writes from another website.
            let origin = string context.Request.Headers.Origin
            let expectedOrigin = context.Request.Scheme + "://" + string context.Request.Host
            if (origin <> "" && origin <> expectedOrigin) || not (context.Request.HasJsonContentType()) then
                return response 400 "Use same-origin JSON requests."
            else
                try
                    let! request = JsonSerializer.DeserializeAsync<Ui.Request>(context.Request.Body, Serialization.options)
                    return lock gate (fun () ->
                        if isNull (box request) || isNull request.Values || isNull request.Targets || isNull request.Intent || isNull request.Target ||
                           request.Values |> Array.exists (fun value -> isNull (box value) || isNull value.Key || isNull value.Value) ||
                           request.Targets |> Array.exists isNull then response 400 "Invalid command body."
                        elif request.Revision <> revision () then response 409 "The application changed since this page loaded. Refresh the page and try again."
                        elif request.Intent = "rebuild" then
                            let reloaded = store.Read()
                            let rebuilt = reloaded |> List.map _.Event |> Evolution.replay
                            history <- reloaded
                            state <- rebuilt
                            response 200 "Current state rebuilt from the persisted event log."
                        else
                            let commands, navigate = HttpCommands.decode (today ()) request
                            match Evolution.decideBatch (today ()) state commands with
                            | Error error -> response 422 (DomainError.describe error)
                            | Ok (nextState, events) ->
                                store.Append(revision (), events)
                                state <- nextState
                                history <- store.Read()
                                json { Ui.Response.Message = sprintf "Saved %d event(s)." events.Length; Ui.Response.NavigateTo = navigate })
                with
                | :? JsonException | :? FormatException | :? ArgumentException -> return response 400 "Some command fields are missing or invalid. Check the form and try again."
        })) |> ignore

        app.MapGet("/api/state", Func<IResult>(fun () -> lock gate (fun () ->
            json {| Revision = revision (); Students = state.Students |> Map.toArray |> Array.map snd; Applications = state.Applications |> Map.toArray |> Array.map snd |}))) |> ignore
        app.MapGet("/api/backup", Func<IResult>(fun () -> lock gate (fun () ->
            let temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db")
            try
                store.Backup temporary
                Results.File(File.ReadAllBytes temporary, "application/vnd.sqlite3", "student-office-backup.db")
            finally
                if File.Exists temporary then File.Delete temporary))) |> ignore
        app.MapGet("/api/health", Func<IResult>(fun () -> lock gate (fun () -> json {| Status = "ok"; Events = revision () |}))) |> ignore
        app.Run()
        0

