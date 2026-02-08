module Michael.Tests.MigrationTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open Microsoft.Data.Sqlite
open Michael.Migrations

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore

    try
        f dir
    finally
        Directory.Delete(dir, true)

let private withMemoryConn f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    f conn

/// Compute per-file hash the way Atlas does: SHA256(name_bytes + content_bytes)
/// accumulated across files in order. For N files, each entry's hash is
/// SHA256(name1 + content1 + name2 + content2 + ... + nameN + contentN)
/// since Go's h.Sum(nil) reads state without resetting.
///
/// .NET SHA256 doesn't support snapshotting intermediate state, so we replay
/// the full prefix for each file to get the intermediate digests.
let private writeAtlasSum (dir: string) =
    let sqlFiles =
        Directory.GetFiles(dir, "*.sql") |> Array.map Path.GetFileName |> Array.sort

    let entries = ResizeArray<string * string>()

    // For each file i, compute SHA256(name0+content0 + ... + nameI+contentI)
    for i in 0 .. sqlFiles.Length - 1 do
        use h = SHA256.Create()

        for j in 0..i do
            let nameBytes = Encoding.UTF8.GetBytes(sqlFiles.[j])
            let content = File.ReadAllBytes(Path.Combine(dir, sqlFiles.[j]))
            h.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0) |> ignore
            h.TransformBlock(content, 0, content.Length, null, 0) |> ignore

        h.TransformFinalBlock(Array.empty, 0, 0) |> ignore
        entries.Add(sqlFiles.[i], Convert.ToBase64String(h.Hash))

    // Integrity hash: SHA256 over (name + base64hash) for each entry
    use integrityHash = SHA256.Create()

    for (name, hash) in entries do
        let nameBytes = Encoding.UTF8.GetBytes(name)
        let hashBytes = Encoding.UTF8.GetBytes(hash)
        integrityHash.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0) |> ignore
        integrityHash.TransformBlock(hashBytes, 0, hashBytes.Length, null, 0) |> ignore

    integrityHash.TransformFinalBlock(Array.empty, 0, 0) |> ignore

    let integrity = "h1:" + Convert.ToBase64String(integrityHash.Hash)

    let lines =
        [ yield integrity
          for (name, hash) in entries do
              yield $"{name} h1:{hash}" ]

    File.WriteAllLines(Path.Combine(dir, "atlas.sum"), lines)

let private appliedVersions (conn: SqliteConnection) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT version FROM atlas_schema_revisions ORDER BY version"
    use reader = cmd.ExecuteReader()

    [ while reader.Read() do
          reader.GetString(0) ]

let private tableExists (conn: SqliteConnection) (name: string) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name"
    cmd.Parameters.AddWithValue("@name", name) |> ignore
    Convert.ToInt64(cmd.ExecuteScalar()) > 0L

[<Tests>]
let migrationTests =
    testList
        "Migrations"
        [ test "returns error for missing directory" {
              withMemoryConn (fun conn ->
                  let result =
                      runMigrations conn "/nonexistent/path/does/not/exist" NodaTime.SystemClock.Instance

                  Expect.isError result "should fail for missing dir")
          }

          test "applies nothing when directory is empty" {
              withTempDir (fun dir ->
                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isOk result "should succeed"
                      Expect.equal (Result.defaultValue -1 result) 0 "0 migrations applied"))
          }

          test "applies a single migration" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_create_test.sql"),
                      "CREATE TABLE test_table (id TEXT PRIMARY KEY);"
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 result) 1 "1 migration applied"
                      Expect.isTrue (tableExists conn "test_table") "table should exist"))
          }

          test "is idempotent — second run applies nothing" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_create_test.sql"),
                      "CREATE TABLE test_table (id TEXT PRIMARY KEY);"
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let r1 = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 r1) 1 "first run applies 1"

                      let r2 = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 r2) 0 "second run applies 0"

                      let versions = appliedVersions conn
                      Expect.hasLength versions 1 "revision recorded exactly once"))
          }

          test "applies migrations in version order" {
              withTempDir (fun dir ->
                  // Write in reverse order to ensure sorting works
                  File.WriteAllText(
                      Path.Combine(dir, "002_add_col.sql"),
                      "ALTER TABLE ordered_test ADD COLUMN name TEXT;"
                  )

                  File.WriteAllText(
                      Path.Combine(dir, "001_create.sql"),
                      "CREATE TABLE ordered_test (id TEXT PRIMARY KEY);"
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 result) 2 "2 migrations applied"

                      let versions = appliedVersions conn
                      Expect.equal versions [ "001"; "002" ] "applied in version order"))
          }

          test "skips already-applied migrations when new ones are added" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_init.sql"),
                      "CREATE TABLE incremental_test (id TEXT PRIMARY KEY);"
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let r1 = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 r1) 1 "first run applies 1"

                      // Add a second migration and regenerate checksums
                      File.WriteAllText(
                          Path.Combine(dir, "002_extend.sql"),
                          "ALTER TABLE incremental_test ADD COLUMN value TEXT;"
                      )

                      writeAtlasSum dir

                      let r2 = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 r2) 1 "second run applies only the new one"

                      let versions = appliedVersions conn
                      Expect.equal versions [ "001"; "002" ] "both versions recorded"))
          }

          test "returns error for malformed SQL" {
              withTempDir (fun dir ->
                  File.WriteAllText(Path.Combine(dir, "001_bad.sql"), "THIS IS NOT VALID SQL;")
                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isError result "should fail on bad SQL"))
          }

          test "stops on first failure — earlier migrations are committed" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_good.sql"),
                      "CREATE TABLE partial_test (id TEXT PRIMARY KEY);"
                  )

                  File.WriteAllText(Path.Combine(dir, "002_bad.sql"), "INVALID SQL HERE;")
                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isError result "should fail on second migration"

                      // First migration should have been committed
                      Expect.isTrue (tableExists conn "partial_test") "first migration committed"

                      let versions = appliedVersions conn
                      Expect.hasLength versions 1 "only first version recorded"))
          }

          test "ignores non-matching filenames" {
              withTempDir (fun dir ->
                  File.WriteAllText(Path.Combine(dir, "README.sql"), "SELECT 1;")
                  File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a migration")

                  File.WriteAllText(
                      Path.Combine(dir, "001_real.sql"),
                      "CREATE TABLE filter_test (id TEXT PRIMARY KEY);"
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 result) 1 "only matching file applied"))
          }

          test "handles migration with multiple statements" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_multi.sql"),
                      """
CREATE TABLE multi_a (id TEXT PRIMARY KEY);
CREATE TABLE multi_b (id TEXT PRIMARY KEY);
CREATE INDEX idx_multi_b ON multi_b(id);
"""
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 result) 1 "1 migration applied"
                      Expect.isTrue (tableExists conn "multi_a") "first table exists"
                      Expect.isTrue (tableExists conn "multi_b") "second table exists"))
          }

          test "handles migration with comment lines" {
              withTempDir (fun dir ->
                  File.WriteAllText(
                      Path.Combine(dir, "001_commented.sql"),
                      """-- This is a comment
-- Another comment
CREATE TABLE comment_test (id TEXT PRIMARY KEY);
-- Trailing comment
"""
                  )

                  writeAtlasSum dir

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.equal (Result.defaultValue -1 result) 1 "1 migration applied"
                      Expect.isTrue (tableExists conn "comment_test") "table created despite comments"))
          }

          // -----------------------------------------------------------------
          // Checksum verification tests
          // -----------------------------------------------------------------

          test "returns error when atlas.sum is missing" {
              withTempDir (fun dir ->
                  File.WriteAllText(Path.Combine(dir, "001_init.sql"), "CREATE TABLE t (id TEXT PRIMARY KEY);")

                  // Don't write atlas.sum
                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isError result "should fail without atlas.sum"))
          }

          test "returns error when migration file is tampered with" {
              withTempDir (fun dir ->
                  File.WriteAllText(Path.Combine(dir, "001_init.sql"), "CREATE TABLE legit (id TEXT PRIMARY KEY);")

                  writeAtlasSum dir

                  // Tamper with the migration file after checksum was written
                  File.WriteAllText(
                      Path.Combine(dir, "001_init.sql"),
                      "CREATE TABLE evil (id TEXT PRIMARY KEY); DROP TABLE IF EXISTS legit;"
                  )

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isError result "should fail on tampered file"))
          }

          test "returns error when atlas.sum integrity line is wrong" {
              withTempDir (fun dir ->
                  File.WriteAllText(Path.Combine(dir, "001_init.sql"), "CREATE TABLE t (id TEXT PRIMARY KEY);")

                  writeAtlasSum dir

                  // Corrupt the integrity line
                  let lines = File.ReadAllLines(Path.Combine(dir, "atlas.sum"))
                  lines.[0] <- "h1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
                  File.WriteAllLines(Path.Combine(dir, "atlas.sum"), lines)

                  withMemoryConn (fun conn ->
                      let result = runMigrations conn dir NodaTime.SystemClock.Instance
                      Expect.isError result "should fail on integrity mismatch"))
          } ]
