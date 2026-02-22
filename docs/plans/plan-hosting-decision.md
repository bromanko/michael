---
title: "Hosting Decision for Michael"
date: 2026-02-22
status: draft
---

# Hosting Decision for Michael

This ExecPlan is a living document. The sections Progress, Surprises & Discoveries,
Decision Log, and Outcomes & Retrospective must be kept up to date as work proceeds.


## Purpose / Big Picture

Michael needs a production hosting setup that reliably runs one F# web process, serves Elm static assets, keeps a persistent SQLite file, runs background CalDAV sync every 10 minutes, and supports secure admin cookies. The outcome of this plan is a clear hosting decision (one provider/path), a small deployment spike proving it works, and a concrete rollout checklist.

At the end, we should be able to answer: "Where will Michael run in production, why that option over alternatives, and how do we deploy/recover safely?"


## Progress

- [x] (2026-02-22 23:10Z) Gathered hard runtime constraints from docs and source (`docs/design.md`, `docs/nl-spec/michael-runtime-and-delivery-spec.md`, `src/backend/Program.fs`, `src/backend/AdminAuth.fs`).
- [x] (2026-02-22 23:12Z) Shortlisted hosting paths to compare: Fly.io single machine, single VPS (Hetzner/Linode/etc), and Cloudflare-native stack.
- [x] (2026-02-22 23:16Z) Expanded shortlist to include Render and Railway; captured stakeholder constraints: target cost under $8/month (or shared/amortized across multiple apps) and low lock-in concern.
- [ ] Define weighted decision criteria (cost, ops burden, reliability, SQLite fit, secret management, backups, observability, and multi-app amortization).
- [ ] Validate current provider capabilities/pricing from official docs and fill evidence notes.
- [ ] Make final call and record rationale.
- [ ] Run one deployment spike on the chosen option.
- [ ] Write production rollout + backup/restore runbook.


## Surprises & Discoveries

- Observation: Current startup is fail-fast for CalDAV write-back URL and requires it to match a configured CalDAV source.
  Evidence: `src/backend/Program.fs` reads `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` as required and throws if no matching source exists.

- Observation: Admin session and CSRF cookies are marked Secure outside Development, so TLS is mandatory in real deployment.
  Evidence: `src/backend/AdminAuth.fs` and `src/backend/Program.fs` set `Secure=true` when environment is not Development.


## Decision Log

- Decision: Treat "SQLite single-instance + background jobs" as hard constraints for hosting selection.
  Rationale: This is explicit in product/runtime specs and materially rules out scale-to-zero stateless-only platforms.
  Date: 2026-02-22

- Decision: Compare five realistic paths first (Fly.io, Railway, Render, single VPS, and Cloudflare-native).
  Rationale: This captures both options already hinted in project docs and new stakeholder-requested platforms without letting research scope sprawl.
  Date: 2026-02-22

- Decision: Treat "$8/month effective cost" as a top-level selection constraint, allowing higher raw host cost only when it is clearly amortized across multiple apps/sites.
  Rationale: Stakeholder priority is low monthly spend; amortization is explicitly acceptable.
  Date: 2026-02-22

- Decision: Vendor lock-in is a secondary concern for this phase.
  Rationale: Stakeholder explicitly prefers low cost and practical operations over portability.
  Date: 2026-02-22

- Decision: Provisional recommendation shifts to **single low-cost VPS** unless Railway/Fly evidence shows lower effective cost with equivalent persistence and operations.
  Rationale: A small VPS is most likely to satisfy the <$8 target and supports amortizing costs across multiple applications while preserving straightforward SQLite semantics.
  Date: 2026-02-22


## Outcomes & Retrospective

(To be filled at milestone completion.)


## Context and Orientation

Michael is a self-hosted, single-instance scheduling app.

The relevant runtime facts in this repository are:

- `docs/design.md`: deployment is still TBD; SQLite single-instance is intentional.
- `docs/nl-spec/michael-runtime-and-delivery-spec.md`: deployment must preserve cookie behavior, use TLS in non-dev, and persist SQLite state across restarts.
- `src/backend/Program.fs`: app requires core env vars at startup, serves static frontend assets itself, runs background calendar sync, and depends on outbound network access (Gemini, SMTP, CalDAV).
- `src/backend/AdminAuth.fs`: admin auth relies on secure HttpOnly cookie sessions.

Any hosting option that cannot provide persistent disk, stable long-running process execution, and TLS-compatible cookie behavior is not viable without architecture changes.


## Plan of Work

First we will lock the decision rubric so we do not choose based on vibes. The rubric will distinguish hard constraints (must-have) from preference criteria (nice-to-have). Hard constraints include persistent storage for SQLite, long-running process support, TLS, secret injection, outbound internet access, and straightforward backup/restore.

Then we will research each candidate option (Fly.io, Railway, Render, and a low-cost VPS baseline) from official provider docs and record concrete evidence: how persistent volumes/disks work, how backups are done, what failure modes look like, and rough monthly cost for one small production instance. We will also score whether each option can host additional small apps on the same spend envelope. For Cloudflare-native, we will explicitly document required architecture changes (because current F# process + local SQLite does not map directly) and likely rule it out for phase 1.

After evidence is captured, we will produce the final decision section in this plan with a clear recommendation and rejection reasons for non-selected options.

Finally, we will run one deployment spike on the chosen option and verify end-to-end behavior (booking API, admin login/session cookie, SQLite persistence across restart, background sync still running). The spike is required before declaring the decision complete.


## Concrete Steps

From repository root (`/home/bromanko.linux/Code/michael`):

1. Confirm project health before infra work:

    selfci check

2. Build a production backend artifact for deploy testing:

    dotnet publish src/backend -c Release -o build/backend

3. Validate that required runtime env vars are documented in deploy notes by cross-checking:

    - `README.md` environment table
    - `src/backend/Program.fs` fail-fast checks

4. Collect provider evidence (manual research in provider docs) for Fly.io, Railway, Render, and one VPS provider baseline:

    - Persistent disk semantics
    - Region selection
    - TLS termination model
    - Secret management
    - Backup/restore workflow
    - Estimated monthly cost for one Michael instance
    - Estimated effective cost when sharing one host across 2-4 small apps

5. Fill the final recommendation section in this plan and run a deployment spike.

6. After spike succeeds, write `docs/runbooks/production-hosting.md` (new file) with:

    - Provisioning steps
    - Secrets setup
    - Deploy command sequence
    - Smoke test checklist
    - Backup and restore drill
    - Rollback steps


## Validation and Acceptance

This decision effort is complete when all of the following are true:

- This plan names a single hosting choice and records why alternatives were rejected.
- A deployment spike is completed on the chosen host.
- Verified behavior from the spike:
  - HTTP app responds and serves frontend assets.
  - Admin login succeeds and session cookie works over HTTPS.
  - Booking creation writes to SQLite.
  - Data remains after app restart/redeploy.
  - Background CalDAV sync process runs without crashing.
- `selfci check` passes on the repo after any deployment-related file changes.
- A production runbook exists with backup/restore and rollback instructions.


## Idempotence and Recovery

This plan is safe to execute incrementally. Research/documentation updates are additive. The deployment spike should use a separate test database path and disposable environment so retries do not risk production data.

If a spike deploy fails, destroy/recreate the test instance and repeat using the same artifact and env var checklist.


## Artifacts and Notes

Current working recommendation (to validate):

    Leader: single low-cost VPS (shared across apps) with systemd + reverse proxy (Caddy or Nginx) + SQLite file backups.

Why this is currently favored:

    - Most likely path to stay under an effective $8/month budget.
    - Amortizes naturally across multiple sites/apps on one machine.
    - Fits current architecture directly (one long-running F# process + local SQLite).
    - Avoids per-service platform minimum charges.

Close alternatives to verify with current pricing:

    - Fly.io single machine + volume (may be simpler operationally; budget fit must be confirmed).
    - Railway single service + volume (could fit budget at low usage; persistence and always-on cost must be confirmed).
    - Render (included for completeness; may miss budget target once persistent disk is included).

Confirmed stakeholder constraints:

    - Target cost: under $8/month if possible.
    - Higher nominal cost is acceptable when amortized across multiple applications.
    - Vendor lock-in is acceptable if day-to-day operation is simple.

Key risks to address in spike/runbook:

    - Backup/restore cadence and restore drill time.
    - Operational burden of patching/upgrades if VPS is selected.
    - Proxy header and secure-cookie behavior validation.


## Interfaces and Dependencies

Required platform capabilities for the final host:

- Long-running .NET process (`dotnet` runtime or container support).
- Persistent filesystem mount for `MICHAEL_DB_PATH` SQLite file.
- HTTPS endpoint with TLS termination.
- Environment-variable secret injection for:
  - `MICHAEL_HOST_TIMEZONE`
  - `GEMINI_API_KEY`
  - `MICHAEL_ADMIN_PASSWORD`
  - `MICHAEL_CSRF_SIGNING_KEY`
  - CalDAV and SMTP variables used in production
- Outbound TCP/HTTPS access to Gemini API, CalDAV servers, and SMTP provider.
- Basic logs and restart controls.
- Backup/restore mechanism for SQLite file and migration safety.
