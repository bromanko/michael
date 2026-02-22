-- Michael database schema (declarative desired state)
-- This file is the source of truth for the database schema.
-- Run `atlas migrate diff <name> --env local` to generate migrations.

CREATE TABLE bookings (
    id                TEXT PRIMARY KEY,
    participant_name  TEXT NOT NULL,
    participant_email TEXT NOT NULL,
    participant_phone TEXT,
    title             TEXT NOT NULL,
    description       TEXT,
    start_time        TEXT NOT NULL,
    end_time          TEXT NOT NULL,
    start_epoch       INTEGER NOT NULL,
    end_epoch         INTEGER NOT NULL,
    duration_minutes  INTEGER NOT NULL,
    timezone          TEXT NOT NULL,
    status            TEXT NOT NULL DEFAULT 'confirmed',
    created_at        TEXT NOT NULL DEFAULT (datetime('now')),
    cancellation_token TEXT,
    caldav_event_href TEXT
);

CREATE TABLE host_availability (
    id          TEXT PRIMARY KEY,
    day_of_week INTEGER NOT NULL,
    start_time  TEXT NOT NULL,
    end_time    TEXT NOT NULL
);

CREATE TABLE calendar_sources (
    id                TEXT PRIMARY KEY,
    provider          TEXT NOT NULL,
    base_url          TEXT NOT NULL,
    calendar_home_url TEXT,
    last_synced_at    TEXT,
    last_sync_result  TEXT
);

CREATE TABLE cached_events (
    id            TEXT PRIMARY KEY,
    source_id     TEXT NOT NULL REFERENCES calendar_sources(id),
    calendar_url  TEXT NOT NULL,
    uid           TEXT NOT NULL,
    summary       TEXT NOT NULL,
    start_instant TEXT NOT NULL,
    end_instant   TEXT NOT NULL,
    is_all_day    INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_cached_events_source ON cached_events(source_id);
CREATE INDEX idx_cached_events_range ON cached_events(start_instant, end_instant);

CREATE TABLE scheduling_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE sync_status (
    source_id    TEXT PRIMARY KEY,
    last_sync_at TEXT NOT NULL,
    status       TEXT NOT NULL
);

CREATE TABLE admin_sessions (
    token      TEXT PRIMARY KEY,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);
