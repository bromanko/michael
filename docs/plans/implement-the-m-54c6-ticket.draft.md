## Revised Plan — m-54c6 (`IHttpClientFactory` migration)

### 1) Summary
Implement ticket **m-54c6** by replacing manual `HttpClient` creation with DI-managed `IHttpClientFactory` clients in backend runtime paths.  
Scope is **F# backend only** (Gemini parse + CalDAV sync paths). Preserve existing runtime behavior: timeout, redirects, Basic auth, and error handling.

### 2) Files to change
- `src/backend/Program.fs`
  - Register named clients via `AddHttpClient`.
  - Remove direct `new HttpClient()` usage.
  - Wire factory-based client creation into parse and sync call paths.
- `src/backend/Handlers.fs`
  - Update `handleParse` dependency to support factory-created clients (preferably client factory function) rather than startup-created manual client.
- `src/backend/CalDav.fs`
  - Replace manual `createHttpClient` constructor internals with factory-backed construction.
  - Keep auth header setup reusable.
- `src/backend/CalendarSync.fs`
  - Update background sync wiring to use factory-based CalDAV client creation.
  - Preserve disposal/lifecycle semantics and current sync behavior.
- `tests/Michael.Tests/*` (targeted)
  - Add or adjust tests for changed signatures/behavior where practical (especially CalDAV client/auth wiring and sync path integration).

### 3) Approach
1. **DI setup in `Program.fs`**
   - Register named client `"gemini"`.
   - Register named client `"caldav"` with:
     - timeout = 30s
     - redirect behavior matching current implementation (`AllowAutoRedirect = true`, max redirects = 10).

2. **Gemini path**
   - Remove manual startup `new HttpClient()`.
   - Inject/compose a Gemini client creator from `IHttpClientFactory`.
   - Use the factory-created client in parse flow (prefer per-request creation inside handler path).

3. **CalDAV path**
   - Update `CalDav.createHttpClient` to depend on `IHttpClientFactory` and named client `"caldav"`.
   - Keep Basic auth assignment based on source credentials.
   - Update manual sync trigger and background sync creation paths to call this factory-backed helper.

4. **Integration updates**
   - Update signatures and call sites (`Program.fs` / `CalendarSync.fs` / `Handlers.fs`) to compile cleanly.
   - Remove any now-dead manual-client code.

### 4) Edge cases
- **Auth isolation** across different CalDAV sources (no credential bleed).
- **Behavior parity** for timeout/redirect settings after migration.
- **Proper disposal semantics** (safe with factory-created clients while reusing handlers under the hood).
- **No startup regressions** in fail-fast config checks.
- **No hidden mutable state** from default headers across unrelated requests.

### 5) Test cases
- Run backend validation:
  - `dotnet build src/backend/Michael.fsproj`
  - `dotnet run --project tests/Michael.Tests --verbosity quiet`
- Run full project CI gate:
  - `selfci check`
- Functional sanity checks:
  - `/api/parse` still succeeds/fails as expected.
  - Admin manual sync endpoint still performs sync and status/history updates.
  - Background sync startup and periodic execution still work.

### 6) Open questions
- Should Gemini use per-request factory-created clients inside handler (preferred) vs a single startup-created client from factory?
- Should we set explicit connection lifetime tuning now (e.g., pooled connection lifetime), or keep defaults?
- Do we want a follow-up ticket for typed clients/wrapper services after this migration?