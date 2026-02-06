module Michael.Migrations

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open Donald
open Microsoft.Data.Sqlite
open NodaTime
open Serilog

// ---------------------------------------------------------------------------
// Migration runner
// ---------------------------------------------------------------------------
// Atlas generates versioned SQL migration files at dev time. This module
// reads those files from disk and applies any that haven't been run yet,
// tracking applied versions in an `atlas_schema_revisions` table (same
// schema Atlas itself uses, so `atlas migrate status` still works).
// ---------------------------------------------------------------------------

type private MigrationFile =
    { Version: string
      Name: string
      Path: string }

let private migrationPattern = Regex(@"^(\d+)_(.+)\.sql$", RegexOptions.Compiled)

let private discoverMigrations (migrationsDir: string) : Result<MigrationFile list, string> =
    if not (Directory.Exists(migrationsDir)) then
        Error $"Migrations directory not found: {migrationsDir}"
    else
        Directory.GetFiles(migrationsDir, "*.sql")
        |> Array.choose (fun path ->
            let fileName = Path.GetFileName(path)
            let m = migrationPattern.Match(fileName)

            if m.Success then
                Some
                    { Version = m.Groups[1].Value
                      Name = m.Groups[2].Value
                      Path = path }
            else
                None)
        |> Array.sortBy (fun f -> f.Version)
        |> Array.toList
        |> Ok

// ---------------------------------------------------------------------------
// Checksum verification (matches Atlas's atlas.sum format)
// ---------------------------------------------------------------------------
// Atlas computes per-file hashes using a running SHA-256 state:
//   for each file (sorted by name): hash.Update(nameBytes); hash.Update(contentBytes)
//   file_h1 = base64(hash.Sum())   -- Sum() reads state without resetting
// The integrity hash uses the same pattern over (name + base64_hash) entries.
// ---------------------------------------------------------------------------

/// Compute per-file hashes matching Atlas's running SHA-256 algorithm.
/// Atlas uses a single SHA-256 instance, calling h.Write(name); h.Write(content)
/// for each file, then h.Sum(nil) to snapshot without resetting. Since .NET
/// doesn't support snapshotting, we replay the full prefix for each file.
let private computeFileHashes (migrationsDir: string) (sortedFileNames: string array) : (string * string) array =
    sortedFileNames
    |> Array.mapi (fun i fileName ->
        use h = SHA256.Create()

        for j in 0..i do
            let nameBytes = Encoding.UTF8.GetBytes(sortedFileNames.[j])
            let content = File.ReadAllBytes(Path.Combine(migrationsDir, sortedFileNames.[j]))
            h.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0) |> ignore
            h.TransformBlock(content, 0, content.Length, null, 0) |> ignore

        h.TransformFinalBlock(Array.empty, 0, 0) |> ignore
        (fileName, Convert.ToBase64String(h.Hash)))

let private verifyChecksums (migrationsDir: string) (migrationFiles: MigrationFile list) : Result<unit, string> =
    let sumPath = Path.Combine(migrationsDir, "atlas.sum")

    if not (File.Exists(sumPath)) then
        Error "atlas.sum not found — cannot verify migration integrity"
    else
        let lines = File.ReadAllLines(sumPath) |> Array.filter (fun l -> l.Length > 0)

        if lines.Length < 1 then
            Error "atlas.sum is empty"
        else
            let expectedIntegrity = lines.[0]

            let storedEntries =
                lines.[1..]
                |> Array.choose (fun line ->
                    match line.Split(" h1:", 2, StringSplitOptions.None) with
                    | [| name; hash |] -> Some(name.Trim(), hash)
                    | _ -> None)

            // 1. Verify the integrity hash (self-consistency of atlas.sum)
            use integrityHash = SHA256.Create()

            for (name, hash) in storedEntries do
                let nameBytes = Encoding.UTF8.GetBytes(name)
                let hashBytes = Encoding.UTF8.GetBytes(hash)
                integrityHash.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0) |> ignore
                integrityHash.TransformBlock(hashBytes, 0, hashBytes.Length, null, 0) |> ignore

            integrityHash.TransformFinalBlock(Array.empty, 0, 0) |> ignore

            let computedIntegrity = "h1:" + Convert.ToBase64String(integrityHash.Hash)

            if computedIntegrity <> expectedIntegrity then
                Error $"atlas.sum integrity mismatch: expected {expectedIntegrity}, got {computedIntegrity}"
            else
                // 2. Recompute file hashes from disk and compare to stored entries
                let allSqlFiles =
                    Directory.GetFiles(migrationsDir, "*.sql")
                    |> Array.map Path.GetFileName
                    |> Array.sort

                let computedHashes = computeFileHashes migrationsDir allSqlFiles
                let storedMap = storedEntries |> Map.ofArray

                let mismatch =
                    computedHashes
                    |> Array.tryFind (fun (name, computed) ->
                        match Map.tryFind name storedMap with
                        | Some stored -> stored <> computed
                        | None -> true)

                match mismatch with
                | Some(name, _) -> Error $"Checksum mismatch for migration file: {name}"
                | None ->

                    // 3. Verify each migration file has an entry in atlas.sum
                    let missingFiles =
                        migrationFiles
                        |> List.filter (fun m ->
                            let fileName = Path.GetFileName(m.Path)
                            not (Map.containsKey fileName storedMap))

                    match missingFiles with
                    | [] -> Ok()
                    | missing ->
                        let names =
                            missing |> List.map (fun m -> Path.GetFileName(m.Path)) |> String.concat ", "

                        Error $"Migration files not in atlas.sum: {names}"

let private ensureRevisionsTable (conn: SqliteConnection) =
    Db.newCommand
        """
        CREATE TABLE IF NOT EXISTS atlas_schema_revisions (
            version    TEXT PRIMARY KEY,
            description TEXT NOT NULL,
            applied_at TEXT NOT NULL
        )
        """
        conn
    |> Db.exec

let private getAppliedVersions (conn: SqliteConnection) : Set<string> =
    Db.newCommand "SELECT version FROM atlas_schema_revisions ORDER BY version" conn
    |> Db.query (fun rd -> rd.ReadString "version")
    |> Set.ofList

let private applyMigration (clock: IClock) (conn: SqliteConnection) (migration: MigrationFile) : Result<unit, string> =
    let sql = File.ReadAllText(migration.Path)

    // Split on semicolons to get individual statements.
    // Comment lines (-- ...) are left intact — SQLite handles them natively.
    let statements =
        sql.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.filter (fun s -> s.Length > 0)

    use txn = conn.BeginTransaction()

    try
        for stmt in statements do
            Db.newCommand stmt conn |> Db.exec

        Db.newCommand
            """
            INSERT INTO atlas_schema_revisions (version, description, applied_at)
            VALUES (@version, @description, @appliedAt)
            """
            conn
        |> Db.setParams
            [ "version", SqlType.String migration.Version
              "description", SqlType.String migration.Name
              "appliedAt", SqlType.String(clock.GetCurrentInstant().ToString()) ]
        |> Db.exec

        txn.Commit()
        Ok()
    with ex ->
        txn.Rollback()
        Error $"Migration {migration.Version}_{migration.Name} failed: {ex.Message}"

let runMigrations (conn: SqliteConnection) (migrationsDir: string) (clock: IClock) : Result<int, string> =
    ensureRevisionsTable conn

    match discoverMigrations migrationsDir with
    | Error msg -> Error msg
    | Ok migrations ->
        match verifyChecksums migrationsDir migrations with
        | Error msg -> Error msg
        | Ok() ->

            let applied = getAppliedVersions conn

            let pending =
                migrations |> List.filter (fun m -> not (Set.contains m.Version applied))

            if pending.IsEmpty then
                Log.Information("Database schema is up to date")
                Ok 0
            else
                Log.Information("Applying {Count} pending migration(s)", pending.Length)

                let rec applyAll remaining appliedCount =
                    match remaining with
                    | [] -> Ok appliedCount
                    | m :: rest ->
                        Log.Information("Applying migration {Version}_{Name}", m.Version, m.Name)

                        match applyMigration clock conn m with
                        | Ok() -> applyAll rest (appliedCount + 1)
                        | Error msg -> Error msg

                applyAll pending 0
