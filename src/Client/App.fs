module StudentApp.Client

open System
open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open StudentApp.Ui

type HttpReply = { Status: int; Body: string }
[<Emit("fetch($0, {method:$1, headers:{'Content-Type':'application/json'}, body:$2}).then(async r => ({Status:r.status, Body:await r.text()}))")>]
let fetch (url: string) (method: string) (body: obj) : JS.Promise<HttpReply> = jsNative
[<Emit("$0.showModal()")>]
let showDialog (element: HTMLElement) : unit = jsNative
[<Emit("$0.close()")>]
let closeDialog (element: HTMLElement) : unit = jsNative

let root = document.getElementById "app"
let mutable generation = 0
let mutable notice = ""

let element tag className text =
    let node = document.createElement tag
    node.className <- className
    node.textContent <- text
    node
let append (parent: Node) (child: Node) = parent.appendChild child |> ignore
let attr key value (node: Element) = node.setAttribute(key, value)
let button label handler =
    let node = element "button" "button" label :?> HTMLButtonElement
    node.``type`` <- "button"
    node.onclick <- fun _ -> handler ()
    node
let link label href =
    let node = element "a" "" label
    attr "href" href node
    node
let clear (node: HTMLElement) = node.textContent <- ""
let inputValue (node: HTMLElement) : string = node?value
let route () =
    let parts = window.location.hash.TrimStart('#').Split('/')
    let route = if parts[0] = "" then "students" else parts[0]
    route, (if parts.Length > 1 then parts[1] else "")

// Only these messages replace the page. Table input updates its own rows so focus is retained.
type Message = Navigate | Loaded of int * Page | Failed of int * string
let mutable dispatch: Message -> unit = ignore

let formatCell format value =
    if format = "date" && value <> "" then
        let parts = value.Split('-')
        if parts.Length = 3 then parts[2] + " / " + parts[1] + " / " + parts[0] else value
    else value

let renderForm (page: Page) (spec: Form) target (targets: string array) onSaved =
    let form = element "form" "form" "" :?> HTMLFormElement
    append form (element "h2" "" spec.Title)
    append form (element "p" "muted" spec.Description)
    if targets.Length > 0 then append form (element "p" "selection-note" (sprintf "%d applications selected" targets.Length))
    let controls = spec.Fields |> Array.map (fun field ->
        let wrapper = element "label" "field" ""
        append wrapper (element "span" "field-label" (field.Label + if field.Required then " *" else " (optional)"))
        let control =
            if field.Kind = "choice" then
                let select = element "select" "input" "" :?> HTMLSelectElement
                let placeholder = element "option" "" (if field.Required then "Choose…" else "No application yet")
                attr "value" "" placeholder
                append select placeholder
                for choice in field.Choices do
                    let option = element "option" "" choice.Label
                    attr "value" choice.Value option
                    append select option
                select.required <- field.Required
                select.value <- field.Value
                select :> HTMLElement
            else
                let input = element "input" "input" "" :?> HTMLInputElement
                input.``type`` <- field.Kind
                input.required <- field.Required
                input.value <- field.Value
                input :> HTMLElement
        attr "name" field.Key control
        append wrapper control
        append form wrapper
        field.Key, control)
    let error = element "div" "form-error" ""
    attr "role" "alert" error
    append form error
    let submit = element "button" "button primary" spec.SubmitLabel :?> HTMLButtonElement
    submit.``type`` <- "submit"
    append form submit
    let mutable saving = false
    form.onsubmit <- fun event ->
        event.preventDefault()
        if not saving then
            saving <- true
            submit.disabled <- true
            submit.textContent <- "Saving…"
            error.textContent <- ""
            let request: Request = { Intent = spec.Intent; Target = target; Targets = targets; Revision = page.Revision; Values = controls |> Array.map (fun (key, node) -> { Key = key; Value = inputValue node }) }
            async {
                try
                    let! reply = fetch "/api/command" "POST" (JS.JSON.stringify request) |> Async.AwaitPromise
                    let response = JS.JSON.parse reply.Body |> unbox<Response>
                    if reply.Status >= 200 && reply.Status < 300 then
                        notice <- response.Message
                        onSaved ()
                        if response.NavigateTo <> "" && window.location.hash <> response.NavigateTo then window.location.hash <- response.NavigateTo
                        else dispatch Navigate
                    else
                        error.textContent <- response.Message
                        if reply.Status = 409 then
                            append error (button "Reload latest page" (fun () -> onSaved (); dispatch Navigate))
                with _ -> error.textContent <- "Could not save. Check the connection and refresh before retrying; the command may already have been saved."
                saving <- false
                submit.disabled <- false
                submit.textContent <- spec.SubmitLabel
            } |> Async.StartImmediate
    form

let openForm (page: Page) (action: Action) targets =
    match page.Forms |> Array.tryFind (fun f -> f.Id = action.FormId) with
    | None -> ()
    | Some spec ->
        let dialog = element "dialog" "modal" ""
        attr "aria-label" spec.Title dialog
        let dismiss () = closeDialog dialog; dialog.remove()
        append dialog (button "Cancel" dismiss)
        append dialog (renderForm page spec action.Target targets dismiss)
        dialog.addEventListener("close", fun _ -> dialog.remove())
        append document.body dialog
        showDialog dialog

let renderAction page (action: Action) targets =
    if action.Href <> "" then
        let node = link action.Label action.Href
        node.className <- "button"
        node
    else button action.Label (fun () -> openForm page action targets) :> HTMLElement

let renderTable page (table: Table) =
    let section = element "section" "panel" ""
    let heading = element "div" "panel-heading" ""
    append heading (element "h2" "" table.Title)
    append heading (element "p" "muted" table.Description)
    append section heading
    let toolbar = element "div" "toolbar" ""
    let search = element "input" "input search" "" :?> HTMLInputElement
    search.``type`` <- "search"
    search.placeholder <- "Search " + table.Title.ToLowerInvariant() + "…"
    attr "aria-label" ("Search " + table.Title) search
    append toolbar search
    let filters = table.Filters |> Array.map (fun filter ->
        let select = element "select" "input" "" :?> HTMLSelectElement
        attr "aria-label" filter.Label select
        let all = element "option" "" ("All · " + filter.Label.ToLowerInvariant())
        attr "value" "" all
        append select all
        for value in filter.Options do
            let option = element "option" "" value
            attr "value" value option
            append select option
        append toolbar select
        filter.ColumnKey, select)
    let sort = element "select" "input" "" :?> HTMLSelectElement
    attr "aria-label" ("Sort " + table.Title) sort
    for key in table.SortBy do
        match table.Columns |> Array.tryFind (fun c -> c.Key = key) with
        | Some column ->
            for suffix, description in ["asc", " ↑"; "desc", " ↓"] do
                let option = element "option" "" ("Sort: " + column.Label + description)
                attr "value" (key + ":" + suffix) option
                append sort option
        | None -> ()
    sort.value <- table.DefaultSort + ":asc"
    if table.SortBy.Length > 0 then append toolbar sort
    append section toolbar
    let bulk = element "div" "bulkbar" ""
    if table.BulkActions.Length > 0 then append section bulk
    let scroller = element "div" "table-scroll" ""
    let grid = element "table" "" ""
    let head = element "thead" "" ""
    let header = element "tr" "" ""
    if table.BulkActions.Length > 0 then append header (element "th" "" "Select")
    for column in table.Columns do
        let th = element "th" "" column.Label
        attr "scope" "col" th
        append header th
    let hasActions = table.Rows |> Array.exists (fun r -> r.Actions.Length > 0)
    if hasActions then append header (element "th" "" "Actions")
    append head header
    append grid head
    let body = element "tbody" "" ""
    append grid body
    append scroller grid
    append section scroller
    let footer = element "div" "table-footer" ""
    attr "aria-live" "polite" footer
    append section footer
    let mutable selected = Set.empty<string>
    let mutable visible: Row array = [||]
    let updateBulk () =
        clear bulk
        append bulk (element "span" "" (sprintf "%d selected" selected.Count))
        for action in table.BulkActions do
            let button = button action.Label (fun () -> openForm page action (Set.toArray selected))
            button.disabled <- Set.isEmpty selected
            append bulk button
    let rec refresh () =
        let contains (value: string) = value.ToLowerInvariant().Contains(search.value.ToLowerInvariant())
        visible <- table.Rows |> Array.filter (fun row ->
            (row.Cells |> Array.exists (fun cell -> contains cell.Value)) &&
            (filters |> Array.forall (fun (key, select) ->
                let index = table.Columns |> Array.findIndex (fun c -> c.Key = key)
                select.value = "" || row.Cells[index].Value = select.value)))
        let sortParts = sort.value.Split(':')
        if sortParts.Length = 2 then
            let index = table.Columns |> Array.findIndex (fun c -> c.Key = sortParts[0])
            let direction = if sortParts[1] = "desc" then -1 else 1
            visible <- visible |> Array.sortWith (fun a b ->
                let av, bv = a.Cells[index].Value, b.Cells[index].Value
                let comparison = if table.Columns[index].Format = "number" then compare (float av) (float bv) else compare (av.ToLowerInvariant()) (bv.ToLowerInvariant())
                direction * comparison)
        // Filtering clears hidden selections: bulk operations always refer to visible rows.
        selected <- Set.intersect selected (visible |> Array.map _.Id |> Set.ofArray)
        clear body
        for row in visible do
            let tr = element "tr" "" ""
            if table.BulkActions.Length > 0 then
                let cell = element "td" "" ""
                if row.Selectable then
                    let check = element "input" "checkbox" "" :?> HTMLInputElement
                    check.``type`` <- "checkbox"
                    check.``checked`` <- Set.contains row.Id selected
                    attr "aria-label" ("Select " + row.Cells[0].Value) check
                    check.onchange <- fun _ ->
                        selected <- if check.``checked`` then Set.add row.Id selected else Set.remove row.Id selected
                        updateBulk ()
                    append cell check
                append tr cell
            for index in 0..row.Cells.Length - 1 do
                let cell, column = row.Cells[index], table.Columns[index]
                let td = element "td" "" ""
                let text = formatCell column.Format cell.Value
                if cell.Href <> "" then append td (link text cell.Href)
                elif column.Format = "details" then
                    let details = element "details" "event-details" ""
                    append details (element "summary" "" "Inspect payload")
                    append details (element "pre" "" text)
                    append td details
                elif column.Format = "status" then
                    let tone = if text = "Participating" || text = "Present" || text.Contains("· passed") then "positive" elif text = "Rejected" || text = "Absent" || text.Contains("not passed") then "negative" else "neutral"
                    append td (element "span" ("badge " + tone) text)
                else td.textContent <- text
                append tr td
            if hasActions then
                let cell = element "td" "row-actions" ""
                for action in row.Actions do append cell (renderAction page action [||])
                append tr cell
            append body tr
        if visible.Length = 0 then
            let tr = element "tr" "" ""
            let td = element "td" "empty" "No matching records. Try another search or filter."
            attr "colspan" (string (table.Columns.Length + 2)) td
            append tr td
            append body tr
        footer.textContent <- sprintf "%d of %d records" visible.Length table.Rows.Length
        updateBulk ()
    if table.BulkActions.Length > 0 then
        append toolbar (button "Select visible pending" (fun () -> selected <- visible |> Array.filter _.Selectable |> Array.map _.Id |> Set.ofArray; refresh ()))
        append toolbar (button "Clear selection" (fun () -> selected <- Set.empty; refresh ()))
    search.oninput <- fun _ -> refresh ()
    sort.onchange <- fun _ -> refresh ()
    for _, filter in filters do filter.onchange <- fun _ -> refresh ()
    refresh ()
    section

let render (page: Page) =
    clear root
    document.title <- page.Title + " · Student Office"
    let sidebar = element "aside" "sidebar" ""
    append sidebar (link "so.  Student Office" "#students")
    append sidebar (element "div" "workspace-label" "LEARNING OPERATIONS")
    let nav = element "nav" "navigation" ""
    attr "aria-label" "Main navigation" nav
    let activeRoute, _ = route ()
    for item in page.Navigation do
        let anchor = link item.Label item.Href
        let key = item.Href.TrimStart('#')
        if key = activeRoute || (key = "students" && (activeRoute = "student" || activeRoute = "new")) || (key = "courses" && activeRoute = "course") then
            anchor.className <- "active"
            attr "aria-current" "page" anchor
        append nav anchor
    append sidebar nav
    append sidebar (element "p" "prototype-note" "Architecture lab\nv0.1 · Local prototype\nNo authentication")
    append root sidebar
    let main = element "main" "main" ""
    let top = element "div" "topbar" ""
    append top (element "span" "" "A place for every learning journey")
    append top (element "span" "local-badge" "●  LOCAL WORKSPACE")
    append main top
    let content = element "div" "content" ""
    let heading = element "header" "page-heading" ""
    append heading (element "p" "eyebrow" page.Eyebrow)
    append heading (element "h1" "" page.Title)
    append heading (element "p" "intro" page.Description)
    let actions = element "div" "page-actions" ""
    for action in page.Actions do append actions (renderAction page action [||])
    append heading actions
    append content heading
    if notice <> "" then
        let message = element "div" "notice" notice
        attr "role" "status" message
        append content message
    if page.Facts.Length > 0 then
        let facts = element "div" "facts" ""
        for fact in page.Facts do
            let tile = element "div" "fact" ""
            append tile (element "div" "fact-label" fact.Label)
            append tile (element "div" "fact-value" fact.Value)
            append facts tile
        append content facts
    if activeRoute = "new" then
        for form in page.Forms do
            let panel = element "section" "panel form-panel" ""
            append panel (renderForm page form "" [||] ignore)
            append content panel
    for table in page.Tables do append content (renderTable page table)
    append content (element "footer" "page-footer" (sprintf "Student Office · Architecture experiment  /  History revision %s" (string page.Revision)))
    append main content
    append root main

let update = function
    | Navigate ->
        generation <- generation + 1
        let requestGeneration = generation
        let name, key = route ()
        async {
            try
                let! reply = fetch ("/api/page?route=" + JS.encodeURIComponent name + "&key=" + JS.encodeURIComponent key) "GET" null |> Async.AwaitPromise
                if reply.Status = 200 then dispatch (Loaded (requestGeneration, JS.JSON.parse reply.Body |> unbox<Page>))
                else dispatch (Failed (requestGeneration, "This page could not be found."))
            with _ -> dispatch (Failed (requestGeneration, "Cannot reach the server. Check that it is running, then retry."))
        } |> Async.StartImmediate
    | Loaded (requestGeneration, page) when requestGeneration = generation ->
        render page
    | Failed (requestGeneration, message) when requestGeneration = generation ->
        clear root
        let panel = element "main" "error-page" ""
        append panel (element "h1" "" "Unable to load this page")
        append panel (element "p" "" message)
        append panel (button "Retry" (fun () -> dispatch Navigate))
        append panel (link "Back to students" "#students")
        append root panel
    | _ -> ()

dispatch <- update
window.addEventListener("hashchange", fun _ -> notice <- ""; dispatch Navigate)
dispatch Navigate

