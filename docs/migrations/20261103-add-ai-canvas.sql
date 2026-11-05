-- AI infinite canvas (P0) migration.
-- Apply after 20260909-add-gpt-image-25-models.sql.
-- New tables store UTC timestamps. Keep jokester.admin.sql aligned.

ALTER TABLE `media_asset`
  ADD COLUMN `source_task_id` BIGINT DEFAULT NULL AFTER `metadata_stripped`,
  ADD COLUMN `source_result_ordinal` INT DEFAULT NULL AFTER `source_task_id`,
  ADD UNIQUE KEY `uk_media_asset_source_result` (`source_task_id`, `source_result_ordinal`);

CREATE TABLE `ai_canvas_project` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `canvas_id` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `owner_user_id` BIGINT NOT NULL,
  `title` VARCHAR(100) NOT NULL,
  `schema_version` INT NOT NULL DEFAULT 1,
  `revision` BIGINT NOT NULL DEFAULT 1,
  `document` MEDIUMTEXT NOT NULL,
  `document_bytes` INT NOT NULL DEFAULT 0,
  `node_count` INT NOT NULL DEFAULT 0,
  `connection_count` INT NOT NULL DEFAULT 0,
  `cover_asset_id` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `storage_bytes` BIGINT NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `deleted_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_canvas_project_canvas_id` (`canvas_id`),
  KEY `idx_ai_canvas_project_owner_updated` (`owner_user_id`, `is_deleted`, `updated_at`),
  CONSTRAINT `fk_ai_canvas_project_owner`
    FOREIGN KEY (`owner_user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='AI infinite canvas projects';

CREATE TABLE `ai_canvas_project_asset` (
  `canvas_project_id` BIGINT NOT NULL,
  `asset_id` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `owner_user_id` BIGINT NOT NULL,
  PRIMARY KEY (`canvas_project_id`, `asset_id`),
  KEY `idx_ai_canvas_project_asset_asset` (`asset_id`),
  KEY `idx_ai_canvas_project_asset_owner` (`owner_user_id`),
  CONSTRAINT `fk_ai_canvas_project_asset_project`
    FOREIGN KEY (`canvas_project_id`) REFERENCES `ai_canvas_project` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Assets referenced by AI canvas documents';
