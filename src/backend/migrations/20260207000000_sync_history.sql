-- Create "sync_history" table
CREATE TABLE `sync_history` (`id` text NULL, `source_id` text NOT NULL, `synced_at` text NOT NULL, `status` text NOT NULL, `error_message` text NULL, PRIMARY KEY (`id`), CONSTRAINT `0` FOREIGN KEY (`source_id`) REFERENCES `calendar_sources` (`id`) ON UPDATE NO ACTION ON DELETE NO ACTION);
-- Create index "idx_sync_history_source_time" to table: "sync_history"
CREATE INDEX `idx_sync_history_source_time` ON `sync_history` (`source_id`, `synced_at` DESC);
