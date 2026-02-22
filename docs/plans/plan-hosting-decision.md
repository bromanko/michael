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
- [x] (2026-02-22 23:26Z) Defined weighted decision criteria (cost, ops burden, reliability, SQLite fit, secret management, backups, observability, and multi-app amortization).
- [x] (2026-02-22 23:33Z) Validated provider capabilities/pricing from official docs and captured pricing snapshot evidence for Fly.io, Railway, Render, and Hetzner Cloud.
- [x] (2026-02-22 23:35Z) Made hosting call based on the weighted criteria and latest pricing snapshot.
- [ ] Run one deployment spike on the chosen option.
- [ ] Write production rollout + backup/restore runbook.


## Surprises & Discoveries

- Observation: Current startup is fail-fast for CalDAV write-back URL and requires it to match a configured CalDAV source.
  Evidence: `src/backend/Program.fs` reads `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` as required and throws if no matching source exists.

- Observation: Admin session and CSRF cookies are marked Secure outside Development, so TLS is mandatory in real deployment.
  Evidence: `src/backend/AdminAuth.fs` and `src/backend/Program.fs` set `Secure=true` when environment is not Development.

- Observation: Fly.io entry pricing is lower than expected for a small always-on process (shared-cpu-1x 256MB is listed at $2.02/month in `ams`; 512MB is $3.32/month), with volume storage charged separately.
  Evidence: `https://fly.io/docs/about/pricing/` plus extracted table rows from 2026-02-22 research.

- Observation: Railway volume-backed services cannot use replicas, which reinforces Michael's single-instance design but limits built-in redundancy if we stay on Railway.
  Evidence: `https://docs.railway.com/volumes/reference` caveats list "Replicas cannot be used with volumes".

- Observation: Render can fit under $8 only at the margin for Michael (`Starter $7/month` + persistent disk at `$0.25/GB/month`), leaving limited headroom.
  Evidence: `https://render.com/pricing` and `https://render.com/docs/disks.md`.


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

- Decision: Use a weighted rubric for the call: effective monthly cost (40%), multi-app amortization potential (20%), operational burden (15%), SQLite/persistence fit (10%), backup/recovery path (10%), and lock-in concerns (5%).
  Rationale: This captures stakeholder priorities (cost first, lock-in lower priority) while still respecting reliability and operability.
  Date: 2026-02-22

- Decision: **Final hosting call: start on a single low-cost VPS (Hetzner Cloud CX23 baseline), with Caddy + systemd + SQLite file backups.**
  Rationale: It best satisfies the <$8 target and amortization requirement across multiple apps. Fly.io is cheaper for one tiny app and remains the best low-ops fallback, but VPS wins the weighted score once multi-app cost sharing is included.
  Date: 2026-02-22


## Outcomes & Retrospective

Research milestone complete: we now have a pricing-backed comparison that includes Fly.io, Railway, Render, and a VPS baseline, and we have made a concrete hosting call.

What changed versus the initial assumption is that Fly.io is cheaper than expected for a single tiny app, but the VPS still wins once we optimize for running several small applications on one budget. The next milestone is no longer "which provider"; it is execution quality on the chosen VPS path (deploy automation, backups, restore drill, and smoke tests).


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

This pricing snapshot was collected on 2026-02-22 from official provider docs/pages.

### Weighted decision criteria

- Effective monthly cost for Michael: **40%**
- Ability to amortize cost across multiple apps/sites: **20%**
- Operational burden (patching, deploy complexity): **15%**
- SQLite + persistent storage fit: **10%**
- Backup and restore story: **10%**
- Vendor lock-in concern: **5%**

### Comparison matrix (pricing + fit)

| Option | Current price evidence | Michael fit | Cost vs <$8 | Multi-app amortization |
|---|---|---|---|---|
| **Hetzner VPS (CX23 baseline)** | `CX23` listed at **$4.09/month** (DE, IPv4) with 2 vCPU / 4 GB RAM / 40 GB SSD; optional block volume at **$0.0484/GB/month**. Source: `https://www.hetzner.com/cloud` (captured 2026-02-22). | Excellent (native process + local SQLite). | ✅ Strong | ✅ Strong (many apps can share one host) |
| **Fly.io single Machine + volume** | `shared-cpu-1x` 256MB shown at **$2.02/month** in `ams`; 512MB **$3.32/month**. Volume **$0.15/GB/month**; snapshots **$0.08/GB/month** with first 10GB free. Sources: `https://fly.io/docs/about/pricing/`, `https://raw.githubusercontent.com/superfly/docs/main/about/pricing.html.markerb`. | Excellent. Local volume maps well to SQLite. | ✅ Very strong for one app | ⚠️ Medium (per-app machine cost compounds) |
| **Railway (Hobby + usage)** | Hobby subscription **$5/month** with $5 included usage; RAM **$10/GB/month**, CPU **$20/vCPU/month**, volume **$0.15/GB/month**. Sources: `https://docs.railway.com/pricing/plans`, `https://docs.railway.com/volumes/reference`. | Good. Supports volumes/backups, but replicas cannot be used with volumes. | ✅ Often (usage-dependent) | ✅ Good (shared account credits/usage) |
| **Render (Starter + disk)** | Web service `Starter` **$7/month**; persistent disks available on paid services; pricing page shows disks at **$0.25/GB/month**. Sources: `https://render.com/pricing`, `https://render.com/docs/disks.md`. | Good. Straightforward deploy, managed TLS, daily disk snapshots. | ⚠️ Borderline (e.g. 1GB disk ≈ $7.25) | ⚠️ Weak (per-service pricing) |
| **Cloudflare-native (Workers + D1 path)** | Not directly comparable with current architecture. | Poor for phase 1 (requires backend/runtime/storage redesign). | N/A | N/A |

### Practical monthly estimates for Michael

These are rough planning estimates, not invoices.

- **VPS baseline (chosen)**: about **$4.09/month** for host, plus backup tooling/storage depending on strategy.
- **Fly single app baseline**: 512MB machine + 1GB volume ≈ **$3.47/month** before egress.
- **Railway hobby baseline**: minimum **$5/month**; with light sustained usage can stay under $8, but CPU-heavy workloads can exceed.
- **Render starter baseline**: **$7/month + disk**, so usually around **$7.25+**.

### Final call rationale

We are choosing **single low-cost VPS first** because it best satisfies your stated priority order:

- Stay below $8/month if possible.
- If not, amortize one bill across multiple small apps.
- Keep architecture simple for the current SQLite single-instance model.
- Accept some lock-in/ops tradeoff.

Fly.io remains the best fallback if we later optimize for minimum operations over multi-app amortization.

### Source links used in this research pass

- Fly pricing: `https://fly.io/docs/about/pricing/`
- Fly volumes overview: `https://raw.githubusercontent.com/superfly/docs/main/volumes/overview.html.markerb`
- Fly custom domains/TLS: `https://raw.githubusercontent.com/superfly/docs/main/networking/custom-domain.html.markerb`
- Railway pricing plans: `https://docs.railway.com/pricing/plans`
- Railway volumes: `https://docs.railway.com/volumes/reference`
- Railway public networking/TLS: `https://docs.railway.com/networking/public-networking`
- Render pricing: `https://render.com/pricing`
- Render persistent disks: `https://render.com/docs/disks.md`
- Render custom domains/TLS: `https://render.com/docs/custom-domains.md`
- Hetzner cloud pricing page: `https://www.hetzner.com/cloud`

### Key risks to address in spike/runbook

- Backup/restore cadence and restore drill time on the VPS path.
- Operational burden of host patching/upgrades and TLS renewal ownership.
- Reverse proxy header behavior to preserve secure-cookie/session semantics.


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
