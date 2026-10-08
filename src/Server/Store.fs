namespace StudentApp.Server

open System
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.Data.Sqlite
open StudentApp

module Serialization =
    let options = JsonSerializerOptions(WriteIndented = false)
    do options.Converters.Add(JsonFSharpConverter())
    let encode event = JsonSerializer.Serialize<Event>(event, options)
    let decode version eventType payload =
        if version <> 1 then failwithf "Unsupported event schema version %d. History was not changed." version
        let event = JsonSerializer.Deserialize<Event>((payload: string), options)
        if Event.name event <> eventType then failwith "Event type/payload mismatch. History was not changed."
        event

// Exactly one process owns this file. A transaction commits a whole command batch.
type Store(path: string) =
    let connection = new SqliteConnection(SqliteConnectionStringBuilder(DataSource = path).ToString())
    do
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT NOT NULL, event_type TEXT NOT NULL, schema_version INTEGER NOT NULL, payload TEXT NOT NULL); CREATE TRIGGER IF NOT EXISTS events_no_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT, 'Event log is append-only'); END; CREATE TRIGGER IF NOT EXISTS events_no_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT, 'Event log is append-only'); END;"
        command.ExecuteNonQuery() |> ignore

    member _.Read() =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT sequence, timestamp, event_type, schema_version, payload FROM events ORDER BY sequence"
        use reader = command.ExecuteReader()
        [ while reader.Read() do
            let payload = reader.GetString 4
            yield { Pages.HistoryItem.Sequence = reader.GetInt64 0; Pages.HistoryItem.Timestamp = reader.GetString 1
                    Pages.HistoryItem.Event = Serialization.decode (reader.GetInt32 3) (reader.GetString 2) payload
                    Pages.HistoryItem.Payload = payload } ]

    member _.Append(expectedRevision: int64, events: Event list) =
        use transaction = connection.BeginTransaction()
        use revision = connection.CreateCommand()
        revision.Transaction <- transaction
        revision.CommandText <- "SELECT COALESCE(MAX(sequence),0) FROM events"
        if Convert.ToInt64(revision.ExecuteScalar()) <> expectedRevision then
            failwith "The event log was changed by another process. Restart this application."
        for event in events do
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- "INSERT INTO events(timestamp,event_type,schema_version,payload) VALUES ($time,$type,1,$payload)"
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$type", Event.name event) |> ignore
            command.Parameters.AddWithValue("$payload", Serialization.encode event) |> ignore
            command.ExecuteNonQuery() |> ignore
        transaction.Commit()

    member _.Backup(destination: string) =
        // A temporary download file must be closed physically, not returned to a connection pool.
        use backup = new SqliteConnection(SqliteConnectionStringBuilder(DataSource = destination, Pooling = false).ToString())
        backup.Open()
        connection.BackupDatabase backup

    interface IDisposable with member _.Dispose() = connection.Dispose()

