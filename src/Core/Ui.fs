namespace StudentApp

// Small transport-neutral UI vocabulary. No browser elements, layout or CSS here.
module Ui =
    type Choice = { Value: string; Label: string }
    type Field = { Key: string; Label: string; Kind: string; Required: bool; Value: string; Choices: Choice array }
    type Form = { Id: string; Title: string; Description: string; Intent: string; SubmitLabel: string; Fields: Field array }
    type Action = { Label: string; FormId: string; Target: string; Href: string }
    type Column = { Key: string; Label: string; Format: string }
    type Cell = { Value: string; Href: string }
    type Row = { Id: string; Cells: Cell array; Actions: Action array; Selectable: bool }
    type Filter = { ColumnKey: string; Label: string; Options: string array }
    type Table = { Id: string; Title: string; Description: string; Columns: Column array; Rows: Row array; Filters: Filter array; SortBy: string array; DefaultSort: string; BulkActions: Action array }
    type Link = { Label: string; Href: string }
    type Fact = { Label: string; Value: string }
    type Page = { Title: string; Eyebrow: string; Description: string; Revision: int64; Navigation: Link array; Facts: Fact array; Actions: Action array; Forms: Form array; Tables: Table array }

    type ValuePair = { Key: string; Value: string }
    type Request = { Intent: string; Target: string; Targets: string array; Values: ValuePair array; Revision: int64 }
    type Response = { Message: string; NavigateTo: string }

    type Intent = CreateStudentAndApply | EditStudent | Apply | Approve | Reject | AskForDocuments | MarkAttendance | Complete | Discontinue | Rebuild
    let intentName = function
        | CreateStudentAndApply -> "create" | EditStudent -> "edit" | Apply -> "apply"
        | Approve -> "approve" | Reject -> "reject" | AskForDocuments -> "documents"
        | MarkAttendance -> "attendance" | Complete -> "complete" | Discontinue -> "discontinue" | Rebuild -> "rebuild"

    let choice value label : Choice = { Value = value; Label = label }
    let field key label kind required value : Field = { Key = key; Label = label; Kind = kind; Required = required; Value = value; Choices = [||] }
    let text key label = field key label "text" true ""
    let date key label value = field key label "date" true value
    let select key label choices = { field key label "choice" true "" with Choices = Array.ofList choices }
    let form id title description intent submit fields : Form =
        { Id = id; Title = title; Description = description; Intent = intentName intent; SubmitLabel = submit; Fields = Array.ofList fields }
    let action label formId target : Action = { Label = label; FormId = formId; Target = target; Href = "" }
    let navigate label href : Action = { Label = label; Href = href; FormId = ""; Target = "" }

    type PresentationColumn<'row> = { Description: Column; Read: 'row -> string; Link: 'row -> string }
    type TableDefinition<'row> = { Id: string; Title: string; Description: string; Columns: PresentationColumn<'row> list; Filters: (string * string) list; SortBy: string list; DefaultSort: string; Actions: 'row -> Action list; Selectable: 'row -> bool; BulkActions: Action list }
    let column key label read : PresentationColumn<'row> = { Description = { Key = key; Label = label; Format = "text" }; Read = read; Link = fun _ -> "" }
    let formatted format (column: PresentationColumn<'row>) = { column with Description = { column.Description with Format = format } }
    let linked href (column: PresentationColumn<'row>) = { column with Link = href }
    let table id title description columns : TableDefinition<'row> =
        { Id = id; Title = title; Description = description; Columns = columns; Filters = []; SortBy = []; DefaultSort = ""; Actions = (fun _ -> []); Selectable = (fun _ -> false); BulkActions = [] }
    let addColumns columns (table: TableDefinition<'row>) = { table with Columns = table.Columns @ columns }
    let withoutColumns keys (table: TableDefinition<'row>) = { table with Columns = table.Columns |> List.filter (fun c -> not (List.contains c.Description.Key keys)) }
    let filterBy key label (table: TableDefinition<'row>) = { table with Filters = table.Filters @ [key, label] }
    let sortBy keys defaultKey (table: TableDefinition<'row>) = { table with SortBy = keys; DefaultSort = defaultKey }
    let rowActions actions (table: TableDefinition<'row>) = { table with Actions = actions }
    let bulkAction eligible action (table: TableDefinition<'row>) = { table with Selectable = eligible; BulkActions = table.BulkActions @ [action] }
    let renderTable identify rows (definition: TableDefinition<'row>) : Table =
        let rows = rows |> List.map (fun item ->
            { Id = identify item; Cells = definition.Columns |> List.map (fun c -> { Value = c.Read item; Href = c.Link item }) |> Array.ofList
              Actions = definition.Actions item |> Array.ofList; Selectable = definition.Selectable item }) |> Array.ofList
        let columns = definition.Columns |> List.map _.Description |> Array.ofList
        { Id = definition.Id; Title = definition.Title; Description = definition.Description; Columns = columns; Rows = rows
          Filters = definition.Filters |> List.map (fun (key, label) ->
              let index = columns |> Array.findIndex (fun c -> c.Key = key)
              { ColumnKey = key; Label = label; Options = rows |> Array.map (fun r -> r.Cells[index].Value) |> Array.distinct |> Array.sort }) |> Array.ofList
          SortBy = Array.ofList definition.SortBy; DefaultSort = definition.DefaultSort; BulkActions = Array.ofList definition.BulkActions }
