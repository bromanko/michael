-- Create "bookings" table
CREATE TABLE `bookings` (`id` text NULL, `participant_name` text NOT NULL, `participant_email` text NOT NULL, `participant_phone` text NULL, `title` text NOT NULL, `description` text NULL, `start_time` text NOT NULL, `end_time` text NOT NULL, `start_epoch` integer NOT NULL, `end_epoch` integer NOT NULL, `duration_minutes` integer NOT NULL, `timezone` text NOT NULL, `status` text NOT NULL DEFAULT 'confirmed', `created_at` text NOT NULL DEFAULT (datetime('now')), PRIMARY KEY (`id`));
-- Create "host_availability" table
CREATE TABLE `host_availability` (`id` text NULL, `day_of_week` integer NOT NULL, `start_time` text NOT NULL, `end_time` text NOT NULL, PRIMARY KEY (`id`));
-- Create "calendar_sources" table
CREATE TABLE `calendar_sources` (`id` text NULL, `provider` text NOT NULL, `base_url` text NOT NULL, `calendar_home_url` text NULL, `last_synced_at` text NULL, `last_sync_result` text NULL, PRIMARY KEY (`id`));
-- Create "cached_events" table
CREATE TABLE `cached_events` (`id` text NULL, `source_id` text NOT NULL, `calendar_url` text NOT NULL, `uid` text NOT NULL, `summary` text NOT NULL, `start_instant` text NOT NULL, `end_instant` text NOT NULL, `is_all_day` integer NOT NULL DEFAULT 0, PRIMARY KEY (`id`), CONSTRAINT `0` FOREIGN KEY (`source_id`) REFERENCES `calendar_sources` (`id`) ON UPDATE NO ACTION ON DELETE NO ACTION);
-- Create index "idx_cached_events_source" to table: "cached_events"
CREATE INDEX `idx_cached_events_source` ON `cached_events` (`source_id`);
-- Create index "idx_cached_events_range" to table: "cached_events"
CREATE INDEX `idx_cached_events_range` ON `cached_events` (`start_instant`, `end_instant`);
-- Create "scheduling_settings" table
CREATE TABLE `scheduling_settings` (`key` text NULL, `value` text NOT NULL, PRIMARY KEY (`key`));
-- Create "sync_status" table
CREATE TABLE `sync_status` (`source_id` text NULL, `last_sync_at` text NOT NULL, `status` text NOT NULL, PRIMARY KEY (`source_id`));
-- Create "admin_sessions" table
CREATE TABLE `admin_sessions` (`token` text NULL, `created_at` text NOT NULL, `expires_at` text NOT NULL, PRIMARY KEY (`token`));
