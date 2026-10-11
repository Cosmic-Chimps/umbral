/// Tests about the repository rather than the product (T102).
///
/// Each one guards a rule that is easy to break silently and expensive to
/// notice late: an endpoint that compiles and serves nothing, a secret that was
/// committed, a SQL statement built from a string. Every rule is written as a
/// function over plain data, with tests that feed it a bad sample, so a check
/// that has quietly stopped checking anything fails here instead of passing.
module Umbral.Tests.MetaTests

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Oxpecker
open Xunit
open YamlDotNet.RepresentationModel

/// The directory holding Umbral.slnx, found by walking up from the test binary.
let private repoRoot =
    lazy
        (let rec up (dir: DirectoryInfo | null) =
            match dir with
            | null -> failwith "Umbral.slnx was not found above the test binary, so the repository root is unknown."
            | d when File.Exists(Path.Combine(d.FullName, "Umbral.slnx")) -> d.FullName
            | d -> up d.Parent

         up (DirectoryInfo AppContext.BaseDirectory))

// ---------------------------------------------------------------------------
// 1. Every handler's endpoint is registered
//
// A handler missing from Program.endpoints compiles, serves a 404 and reports
// nothing anywhere. The fsproj and Program.fs both describe this trap.
// ---------------------------------------------------------------------------

/// The `endpoint` value of every module under `Umbral.Handlers`, with the
/// module's name. A module is a static class and `let endpoint` a static property.
let private handlerEndpoints (assembly: Assembly) : (string * Endpoint list) list =
    assembly.GetTypes()
    |> Array.filter (fun t ->
        t.IsAbstract
        && t.IsSealed
        && (string t.FullName).StartsWith "Umbral.Handlers.")
    |> Array.choose (fun t ->
        let name = string t.FullName

        match t.GetProperty("endpoint", BindingFlags.Public ||| BindingFlags.Static) with
        | null -> None
        | property ->
            match property.GetValue null with
            | :? Endpoint as one -> Some(name, [ one ])
            | :? (Endpoint list) as many -> Some(name, many)
            | _ ->
                // A new shape is a reason to teach this test about it, not to
                // let the module through unchecked.
                failwith $"{name}.endpoint is a {property.PropertyType}, which this check does not understand."
        )
    |> List.ofArray

/// The modules with at least one endpoint that `registered` does not contain.
/// By reference: Program.endpoints is built from these very values.
let private unregistered (registered: Endpoint list) (modules: (string * Endpoint list) list) =
    modules
    |> List.filter (fun (_, endpoints) ->
        endpoints
        |> List.exists (fun e -> not (registered |> List.exists (fun r -> obj.ReferenceEquals(r, e)))))
    |> List.map fst

let private umbral = typeof<Umbral.Infrastructure.Database.DbConfig>.Assembly

[<Fact>]
let ``every handler module's endpoint is in Program.endpoints`` () =
    let missing = unregistered Umbral.Program.endpoints (handlerEndpoints umbral)
    let names = String.concat ", " missing

    Assert.True(
        List.isEmpty missing,
        $"These handlers define an endpoint that Program.endpoints does not list, so they serve a 404: {names}. Add each to the list in Program.fs."
    )

[<Fact>]
let ``the endpoint check does find the handler modules`` () =
    // Reflection that matched nothing would pass the check above for ever.
    let names = handlerEndpoints umbral |> List.map fst
    Assert.True(names.Length >= 15, $"Only {names.Length} handler modules were found.")
    Assert.Contains("Umbral.Handlers.Admin.SignIn", names)
    Assert.Contains("Umbral.Handlers.Connect.Start", names)

[<Fact>]
let ``the endpoint check fails when a handler is left out`` () =
    let modules = handlerEndpoints umbral
    let signIn = modules |> List.find (fun (name, _) -> name = "Umbral.Handlers.Admin.SignIn") |> snd

    // Program.endpoints as it would be if the SignIn line were forgotten.
    let withoutSignIn =
        Umbral.Program.endpoints
        |> List.filter (fun r -> not (signIn |> List.exists (fun e -> obj.ReferenceEquals(r, e))))

    Assert.Equal<string list>([ "Umbral.Handlers.Admin.SignIn" ], unregistered withoutSignIn modules)

// ---------------------------------------------------------------------------
// 2. No committed settings file holds a secret
// ---------------------------------------------------------------------------

/// Names that suggest a credential. `key` only as a word of its own, so a
/// directory called `keys` is not mistaken for one.
let private secretName =
    Regex(
        "secret|password|token|credential|(^|[^a-z])key([^a-z]|$)|apikey",
        RegexOptions.IgnoreCase ||| RegexOptions.Compiled
    )

/// A value that is not a literal secret: empty, a boolean, or a reference to a
/// variable. `${NAME}` is compose's form and a workflow's, so one rule covers
/// both.
let private isNotALiteral (value: string) =
    let v = value.Trim()

    v = ""
    || v = "~"
    || v.Equals("null", StringComparison.OrdinalIgnoreCase)
    || v.Equals("true", StringComparison.OrdinalIgnoreCase)
    || v.Equals("false", StringComparison.OrdinalIgnoreCase)
    || v.Contains "${"
    || Regex.IsMatch(v, @"^\$[A-Za-z_]")

/// `where` is the path to the key. Never the value: a failing run must not
/// print the secret it found into a build log.
let private checkPair (where: string) (name: string) (value: string) =
    if secretName.IsMatch name && not (isNotALiteral value) then [ where ] else []

let private jsonFindings (text: string) : string list =
    let options = JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)
    use document = JsonDocument.Parse(text, options)

    let rec walk (path: string) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            [ for p in element.EnumerateObject() do
                  let here = if path = "" then p.Name else $"{path}.{p.Name}"

                  match p.Value.ValueKind with
                  | JsonValueKind.String -> yield! checkPair here p.Name (p.Value.GetString() |> Option.ofObj |> Option.defaultValue "")
                  | JsonValueKind.Number -> yield! checkPair here p.Name (p.Value.GetRawText())
                  | _ -> yield! walk here p.Value ]
        | JsonValueKind.Array -> [ for item in element.EnumerateArray() do yield! walk path item ]
        | _ -> []

    walk "" document.RootElement

let private yamlFindings (text: string) : string list =
    let stream = YamlStream()
    stream.Load(new StringReader(text))

    let rec walk (path: string) (node: YamlNode) : string list =
        match node with
        | :? YamlMappingNode as map ->
            [ for entry in map.Children do
                  let name = (entry.Key :?> YamlScalarNode).Value |> Option.ofObj |> Option.defaultValue ""
                  let here = if path = "" then name else $"{path}.{name}"

                  match entry.Value with
                  | :? YamlScalarNode as scalar -> yield! checkPair here name (scalar.Value |> Option.ofObj |> Option.defaultValue "")
                  | other -> yield! walk here other ]
        | :? YamlSequenceNode as items ->
            [ for item in items do
                  match item with
                  // Compose's list form of `environment:`, "NAME=value".
                  | :? YamlScalarNode as scalar ->
                      let m = Regex.Match(scalar.Value |> Option.ofObj |> Option.defaultValue "", @"^([A-Za-z_][\w.\-]*)=(.*)$")
                      if m.Success then yield! checkPair $"{path}.{m.Groups[1].Value}" m.Groups[1].Value m.Groups[2].Value
                  | other -> yield! walk path other ]
        | _ -> []

    [ for document in stream.Documents do yield! walk "" document.RootNode ]

/// `.env` files: `NAME=value`, with an optional `export` and quotes.
let private envFindings (text: string) : string list =
    [ for line in text.Split '\n' do
          let m = Regex.Match(line.Trim(), @"^(?:export\s+)?([A-Za-z_][\w.\-]*)\s*=\s*(.*)$")

          if m.Success && not (line.TrimStart().StartsWith "#") then
              yield! checkPair m.Groups[1].Value m.Groups[1].Value (m.Groups[2].Value.Trim().Trim('"', '\''))
    ]

/// The keys in `text` that name a credential and hold a literal value, or an
/// error that the file could not be read at all (which also fails: a file we
/// cannot parse is a file we cannot vouch for).
let private secretsIn (fileName: string) (text: string) : string list =
    let lower = fileName.ToLowerInvariant()

    if lower.EndsWith ".json" then jsonFindings text
    elif lower.EndsWith ".yml" || lower.EndsWith ".yaml" then yamlFindings text
    else envFindings text

/// Tooling that is not part of Umbral and holds no Umbral settings.
let private toolingDirectories = [ ".specify/"; ".claude/"; ".alta/"; ".git/" ]

let private isSettingsFile (relative: string) =
    let name = (string (Path.GetFileName relative)).ToLowerInvariant()

    not (toolingDirectories |> List.exists relative.StartsWith)
    && ((name.StartsWith "appsettings" && name.EndsWith ".json")
        || name = "launchsettings.json"
        || name.EndsWith ".yml"
        || name.EndsWith ".yaml"
        || name.StartsWith ".env"
        || name.EndsWith ".env")

/// Tracked files plus new ones not yet added, minus anything ignored: what a
/// commit made right now could contain, without flagging a developer's own
/// git-ignored `.env`.
let private candidateFiles () : string list =
    let info = ProcessStartInfo("git", "ls-files --cached --others --exclude-standard -z")
    info.WorkingDirectory <- repoRoot.Value
    info.RedirectStandardOutput <- true
    use proc = Process.Start info |> nonNull
    let output = proc.StandardOutput.ReadToEnd()
    proc.WaitForExit()

    if proc.ExitCode <> 0 then
        failwith "git ls-files failed, so the committed files cannot be listed. Run the tests inside the git checkout."

    output.Split('\000', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

[<Fact>]
let ``no committed settings file holds a literal secret`` () =
    let scanned = candidateFiles () |> List.filter isSettingsFile

    let findings =
        [ for relative in scanned do
              let text = File.ReadAllText(Path.Combine(repoRoot.Value, relative))

              let keys =
                  try
                      secretsIn relative text
                  with ex ->
                      [ $"(unreadable: {ex.GetType().Name})" ]

              for key in keys do
                  $"{relative}: {key}" ]

    Assert.True(
        List.isEmpty findings,
        "These settings hold a non-empty literal where a credential is named. Move the value to an environment variable and reference it as ${NAME}:"
        + Environment.NewLine
        + String.concat Environment.NewLine findings
    )

[<Fact>]
let ``the secret scan covers the files it is meant to`` () =
    // A file filter that matched nothing would pass the check above for ever.
    let scanned = candidateFiles () |> List.filter isSettingsFile
    Assert.Contains("src/Umbral/Properties/launchSettings.json", scanned)

[<Theory>]
[<InlineData("appsettings.json", "{ \"Connectors\": { \"Discord\": { \"ClientSecret\": \"abc123\" } } }", "Connectors.Discord.ClientSecret")>]
[<InlineData("appsettings.Production.json", "{ \"Smtp\": { \"Password\": 12345 } }", "Smtp.Password")>]
[<InlineData("launchSettings.json", "{ \"profiles\": { \"http\": { \"environmentVariables\": { \"UMBRAL__TOKEN\": \"x\" } } } }", "profiles.http.environmentVariables.UMBRAL__TOKEN")>]
[<InlineData("docker-compose.yml", "services:\n  umbral:\n    environment:\n      UMBRAL__ADMIN_PASSWORD: hunter2\n", "services.umbral.environment.UMBRAL__ADMIN_PASSWORD")>]
[<InlineData("docker-compose.yml", "services:\n  umbral:\n    environment:\n      - GITHUB_CLIENT_SECRET=hunter2\n", "services.umbral.environment.GITHUB_CLIENT_SECRET")>]
[<InlineData(".github/workflows/ci.yml", "jobs:\n  build:\n    steps:\n      - name: deploy\n        env:\n          API_TOKEN: abc\n", "jobs.build.steps.env.API_TOKEN")>]
[<InlineData(".env", "UMBRAL__API_KEY=abc\n", "UMBRAL__API_KEY")>]
[<InlineData(".env.production", "export UMBRAL__SECRET=\"abc\"\n", "UMBRAL__SECRET")>]
let ``the secret scan flags a literal credential`` (fileName: string, text: string, expected: string) =
    Assert.Equal<string list>([ expected ], secretsIn fileName text)

[<Theory>]
[<InlineData("appsettings.json", "{ \"ClientSecret\": \"\" }")>]
[<InlineData("appsettings.json", "{ \"ClientSecret\": null, \"Logging\": { \"Level\": \"Warning\" } }")>]
[<InlineData("launchSettings.json", "{ \"environmentVariables\": { \"UMBRAL__PUBLIC_ORIGIN\": \"http://localhost:5173\" } }")>]
[<InlineData("docker-compose.yml", "services:\n  umbral:\n    environment:\n      UMBRAL__GITHUB_CLIENT_SECRET: ${GITHUB_CLIENT_SECRET}\n")>]
[<InlineData("docker-compose.yml", "services:\n  umbral:\n    environment:\n      - UMBRAL__SECRET=${UMBRAL_SECRET:?set it}\n")>]
[<InlineData("docker-compose.yml", "services:\n  umbral:\n    volumes:\n      - keys:/data/keys\n    environment:\n      UMBRAL__KEYS_DIRECTORY: /data/keys\n")>]
[<InlineData(".github/workflows/ci.yml", "jobs:\n  build:\n    env:\n      TOKEN: ${{ secrets.TOKEN }}\n")>]
[<InlineData(".env", "# UMBRAL__SECRET=abc\nUMBRAL__SECRET=$FROM_SHELL\nUMBRAL__SECRET_EMPTY=\n")>]
let ``the secret scan lets references and ordinary values through`` (fileName: string, text: string) =
    Assert.Empty(secretsIn fileName text)

// ---------------------------------------------------------------------------
// 3. SQL text is literal
//
// The convention, from Database.fs, UserStore.fs and the rest: a statement is
// a string literal, or a [<Literal>] constant, assigned to CommandText, and
// every value travels as an @parameter. Nothing is formatted into the text.
// ---------------------------------------------------------------------------

type private Token =
    | Str of text: string * interpolated: bool * hasHole: bool
    | Ident of string
    | Plus
    | Assign
    | Other

/// Just enough F# lexing to tell code from comments and strings, and to know
/// where a string starts and ends. Not a parser: it never needs to be.
let private tokenize (source: string) : Token list =
    let tokens = ResizeArray<Token>()
    let n = source.Length
    let mutable i = 0
    let at k = if k < n then source[k] else '\000'

    // Reads a string body starting just after the opening quote(s).
    let readString (triple: bool) (verbatim: bool) (interpolated: bool) =
        let text = StringBuilder()
        let mutable hole = false
        let mutable fin = false

        while not fin && i < n do
            let c = source[i]

            if triple && c = '"' && at (i + 1) = '"' && at (i + 2) = '"' then
                i <- i + 3
                fin <- true
            elif not triple && c = '"' && verbatim && at (i + 1) = '"' then
                text.Append '"' |> ignore
                i <- i + 2
            elif not triple && c = '"' then
                i <- i + 1
                fin <- true
            elif not triple && not verbatim && c = '\\' then
                text.Append(c).Append(at (i + 1)) |> ignore
                i <- i + 2
            elif interpolated && c = '{' && at (i + 1) = '{' then
                text.Append "{{" |> ignore
                i <- i + 2
            elif interpolated && c = '{' then
                // A hole. Skip to its closing brace, stepping over any string inside it.
                hole <- true
                let mutable depth = 1
                i <- i + 1

                while depth > 0 && i < n do
                    match source[i] with
                    | '{' -> depth <- depth + 1; i <- i + 1
                    | '}' -> depth <- depth - 1; i <- i + 1
                    | '"' ->
                        i <- i + 1
                        while i < n && source[i] <> '"' do
                            i <- i + (if source[i] = '\\' then 2 else 1)
                        i <- i + 1
                    | _ -> i <- i + 1
            else
                text.Append c |> ignore
                i <- i + 1

        tokens.Add(Str(text.ToString(), interpolated, hole))

    while i < n do
        let c = source[i]

        if c = '/' && at (i + 1) = '/' then
            while i < n && source[i] <> '\n' do i <- i + 1
        elif c = '(' && at (i + 1) = '*' && at (i + 2) <> ')' then
            i <- i + 2
            while i < n && not (source[i] = '*' && at (i + 1) = ')') do i <- i + 1
            i <- i + 2
        elif c = '\'' && at (i + 1) = '"' && at (i + 2) = '\'' then
            i <- i + 3 // the character literal '"'
            tokens.Add Other
        elif c = '\'' && at (i + 1) = '\\' && at (i + 3) = '\'' then
            i <- i + 4
            tokens.Add Other
        elif c = '$' && at (i + 1) = '"' && at (i + 2) = '"' && at (i + 3) = '"' then
            i <- i + 4
            readString true false true
        elif (c = '$' || c = '@') && (at (i + 1) = '"' || ((at (i + 1) = '$' || at (i + 1) = '@') && at (i + 2) = '"')) then
            let prefix = if at (i + 1) = '"' then string c else source.Substring(i, 2)
            i <- i + prefix.Length + 1
            readString false (prefix.Contains '@') (prefix.Contains '$')
        elif c = '"' && at (i + 1) = '"' && at (i + 2) = '"' then
            i <- i + 3
            readString true false false
        elif c = '"' then
            i <- i + 1
            readString false false false
        elif c = '+' && at (i + 1) <> '+' then
            i <- i + 1
            tokens.Add Plus
        elif c = '<' && at (i + 1) = '-' then
            i <- i + 2
            tokens.Add Assign
        elif Char.IsLetter c || c = '_' then
            let start = i
            while i < n && (Char.IsLetterOrDigit source[i] || source[i] = '_' || source[i] = '.' || source[i] = '\'') do i <- i + 1
            tokens.Add(Ident(source.Substring(start, i - start)))
        elif Char.IsWhiteSpace c then
            i <- i + 1
        else
            i <- i + 1
            tokens.Add Other

    List.ofSeq tokens

/// SQL statements are uppercase in this codebase, and the patterns are strict
/// enough that prose ("Delete the connector, or ...") does not match.
let private sqlText =
    Regex(
        @"SELECT\s[\s\S]*\sFROM\s|INSERT\s+INTO\s|UPDATE\s+\w+\s+SET\s|DELETE\s+FROM\s|CREATE\s+(TABLE|INDEX)\s",
        RegexOptions.Compiled
    )

let private formatPlaceholder = Regex(@"%[sdiOAbfxXeEgGMc]|\{\d+\}", RegexOptions.Compiled)

/// The constants a CommandText may be built from.
let private literalNames (sources: string list) =
    sources
    |> List.collect (fun source ->
        [ for m in Regex.Matches(source, @"\[<Literal>\]\s*(?:private\s+|internal\s+)?let\s+(?:private\s+|internal\s+)?(\w+)" ) ->
              m.Groups[1].Value ])
    |> Set.ofList

/// `ConnectorStore.query` appends a `where` fragment to a literal. It is
/// private and all three callers pass a literal, which is why it is here and
/// not rewritten; a new entry belongs in a review, not in a habit.
let private allowedNames = Set.ofList [ "where" ]

/// Problems in one source file: SQL text with a value formatted into it.
let private sqlProblems (literals: Set<string>) (source: string) : string list =
    let tokens = tokenize source |> Array.ofList

    [ for k in 0 .. tokens.Length - 1 do
          match tokens[k] with
          // $"SELECT ... {x}"
          | Str(text, true, true) when sqlText.IsMatch text -> yield "SQL text built by string interpolation"
          // sprintf "SELECT ... %s" / String.Format("... {0}")
          | Str(text, false, _) when sqlText.IsMatch text && formatPlaceholder.IsMatch text ->
              yield "SQL text with a format placeholder"
          | _ -> ()

          // "SELECT ..." + x, or x + "SELECT ..."
          match tokens[k] with
          | Str(text, _, _) when sqlText.IsMatch text ->
              let nonLiteral t =
                  match t with
                  | Ident name -> not (literals.Contains name || allowedNames.Contains name)
                  | _ -> false

              let before = if k >= 2 && tokens[k - 1] = Plus then Some tokens[k - 2] else None
              let after = if k + 2 < tokens.Length && tokens[k + 1] = Plus then Some tokens[k + 2] else None

              if before |> Option.exists nonLiteral || after |> Option.exists nonLiteral then
                  yield "SQL text concatenated with a value"
          | _ -> ()

          // CommandText <- <terms joined by +>: each term a literal or a named constant.
          match tokens[k] with
          | Ident name when name.EndsWith "CommandText" && k + 1 < tokens.Length && tokens[k + 1] = Assign ->
              let mutable j = k + 2
              let mutable go = true

              while go do
                  match (if j < tokens.Length then tokens[j] else Other) with
                  | Str(_, true, true) ->
                      yield "CommandText is an interpolated string"
                      j <- j + 1
                  | Str _ -> j <- j + 1
                  | Ident term ->
                      if not (literals.Contains term || allowedNames.Contains term) then
                          yield $"CommandText is built from '{term}', which is not a [<Literal>] constant"
                      j <- j + 1
                  | _ -> go <- false

                  if go && not (j < tokens.Length && tokens[j] = Plus) then go <- false
                  elif go then j <- j + 1
          | _ -> () ]

let private sourceFiles () =
    let root = Path.Combine(repoRoot.Value, "src")

    Directory.EnumerateFiles(root, "*.fs", SearchOption.AllDirectories)
    |> Seq.filter (fun p ->
        let relative = Path.GetRelativePath(root, p)
        not (relative.Split(Path.DirectorySeparatorChar) |> Array.exists (fun part -> part = "obj" || part = "bin")))
    |> List.ofSeq

[<Fact>]
let ``SQL text is literal, with no values formatted into it`` () =
    let files = sourceFiles () |> List.map (fun p -> p, File.ReadAllText p)
    let literals = literalNames (files |> List.map snd)

    let findings =
        [ for path, source in files do
              for problem in sqlProblems literals source do
                  $"{Path.GetRelativePath(repoRoot.Value, path)}: {problem}" ]

    Assert.True(
        List.isEmpty findings,
        "SQL must be a string literal or a [<Literal>] constant, with every value passed as an @parameter:"
        + Environment.NewLine
        + String.concat Environment.NewLine findings
    )

[<Fact>]
let ``the SQL check reads the real source`` () =
    // Zero SQL statements seen would mean the lexer or the file walk is broken.
    let files = sourceFiles () |> List.map File.ReadAllText

    let statements =
        files
        |> List.collect (fun source ->
            tokenize source |> List.choose (function | Str(text, _, _) when sqlText.IsMatch text -> Some text | _ -> None))

    Assert.True(statements.Length >= 20, $"Only {statements.Length} SQL statements were found in src.")

[<Theory>]
[<InlineData("let q (id: string) = $\"SELECT * FROM users WHERE id = '{id}'\"")>]
[<InlineData("let q (id: string) = $\"\"\"DELETE FROM users WHERE id = {id}\"\"\"")>]
[<InlineData("let q id = sprintf \"UPDATE users SET name = '%s' WHERE id = 1\" id")>]
[<InlineData("let q id = String.Format(\"SELECT a FROM t WHERE x = {0}\", id)")>]
[<InlineData("let q id = \"SELECT a FROM t WHERE x = \" + id")>]
[<InlineData("let q id = id + \" ORDER BY x DELETE FROM t \"")>]
[<InlineData("command.CommandText <- Columns + clause")>]
[<InlineData("command.CommandText <- $\"{statement}\"")>]
let ``the SQL check flags a statement with a value formatted in`` (source: string) =
    // `clause`, `id` and `statement` are not constants; `Columns` is.
    Assert.NotEmpty(sqlProblems (Set.ofList [ "Columns" ]) source)

[<Theory>]
[<InlineData("command.CommandText <- \"SELECT a FROM t WHERE x = @x\"")>]
[<InlineData("command.CommandText <- Columns + \" WHERE slug = @slug\"")>]
[<InlineData("command.CommandText <-\n    \"INSERT INTO t (a)\n     VALUES (@a)\"")>]
[<InlineData("// UPDATE users SET name = {x} is only a comment\nlet q = 1")>]
[<InlineData("/// SELECT a FROM t WHERE x = %s, in a doc comment\nlet q = 1")>]
[<InlineData("let message = \"Delete the connector, or set both.\"")>]
[<InlineData("let message (n: string) = $\"Delete from the list {n}\"")>]
[<InlineData("let q = \"\"\"CREATE TABLE IF NOT EXISTS t (id TEXT)\"\"\"")>]
[<InlineData("let c = '\"'\nlet q = \"SELECT a FROM t\"")>]
let ``the SQL check lets literal statements and prose through`` (source: string) =
    Assert.Empty(sqlProblems (Set.ofList [ "Columns" ]) source)
