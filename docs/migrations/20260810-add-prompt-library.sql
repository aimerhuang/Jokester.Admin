-- Prompt library storage, snapshots, metrics, and AI task attribution.
-- Run once on MySQL 8 before enabling PromptLibrary synchronization.

USE `jokester.admin`;

CREATE TABLE `prompt_library_sync_run` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `source` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `source_commit_sha` CHAR(40) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `source_etag` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `source_readme_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `parsed_count` INT NOT NULL DEFAULT 0,
  `selected_count` INT NOT NULL DEFAULT 0,
  `downloaded_count` INT NOT NULL DEFAULT 0,
  `reused_image_count` INT NOT NULL DEFAULT 0,
  `failed_image_count` INT NOT NULL DEFAULT 0,
  `started_at` DATETIME NOT NULL,
  `finished_at` DATETIME DEFAULT NULL,
  `error_message` VARCHAR(2000) DEFAULT NULL,
  `warning_message` VARCHAR(4000) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_prompt_sync_run_source_status_started` (`source`, `status`, `started_at`),
  KEY `idx_prompt_sync_run_finished` (`finished_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Prompt library synchronization runs';

CREATE TABLE `prompt_library_item` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `source` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `source_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `external_no` INT NOT NULL,
  `external_occurrence` INT NOT NULL DEFAULT 1,
  `title` VARCHAR(300) NOT NULL,
  `description` LONGTEXT NOT NULL,
  `prompt_text` LONGTEXT NOT NULL,
  `prompt_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `cover_source_url` VARCHAR(1000) NOT NULL,
  `cover_local_path` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `author_name` VARCHAR(200) DEFAULT NULL,
  `author_url` VARCHAR(1000) DEFAULT NULL,
  `source_url` VARCHAR(1000) DEFAULT NULL,
  `source_published_at` DATETIME DEFAULT NULL,
  `language` VARCHAR(50) DEFAULT NULL,
  `source_position` INT NOT NULL,
  `snapshot_id` BIGINT NOT NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_prompt_item_source_key` (`source`, `source_key`),
  KEY `idx_prompt_item_source_external` (`source`, `external_no`, `external_occurrence`),
  KEY `idx_prompt_item_source_active_position` (`source`, `is_active`, `source_position`),
  KEY `idx_prompt_item_snapshot` (`snapshot_id`),
  CONSTRAINT `fk_prompt_item_snapshot` FOREIGN KEY (`snapshot_id`) REFERENCES `prompt_library_sync_run` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Stable prompt library items and current version';

CREATE TABLE `prompt_library_item_version` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `snapshot_id` BIGINT NOT NULL,
  `prompt_id` BIGINT NOT NULL,
  `external_no` INT NOT NULL,
  `external_occurrence` INT NOT NULL DEFAULT 1,
  `title` VARCHAR(300) NOT NULL,
  `description` LONGTEXT NOT NULL,
  `prompt_text` LONGTEXT NOT NULL,
  `prompt_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `cover_source_url` VARCHAR(1000) NOT NULL,
  `cover_local_path` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `author_name` VARCHAR(200) DEFAULT NULL,
  `author_url` VARCHAR(1000) DEFAULT NULL,
  `source_url` VARCHAR(1000) DEFAULT NULL,
  `source_published_at` DATETIME DEFAULT NULL,
  `language` VARCHAR(50) DEFAULT NULL,
  `source_position` INT NOT NULL,
  `created_at` DATETIME NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_prompt_item_version_snapshot_prompt` (`snapshot_id`, `prompt_id`),
  KEY `idx_prompt_item_version_prompt` (`prompt_id`),
  CONSTRAINT `fk_prompt_item_version_snapshot` FOREIGN KEY (`snapshot_id`) REFERENCES `prompt_library_sync_run` (`id`),
  CONSTRAINT `fk_prompt_item_version_prompt` FOREIGN KEY (`prompt_id`) REFERENCES `prompt_library_item` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Rollback-capable prompt item versions';

CREATE TABLE `prompt_library_metric_daily` (
  `prompt_id` BIGINT NOT NULL,
  `metric_date` DATE NOT NULL,
  `detail_view_count` BIGINT NOT NULL DEFAULT 0,
  `copy_count` BIGINT NOT NULL DEFAULT 0,
  `use_count` BIGINT NOT NULL DEFAULT 0,
  `successful_generation_count` BIGINT NOT NULL DEFAULT 0,
  `updated_at` DATETIME NOT NULL,
  PRIMARY KEY (`prompt_id`, `metric_date`),
  KEY `idx_prompt_metric_date` (`metric_date`),
  CONSTRAINT `fk_prompt_metric_prompt` FOREIGN KEY (`prompt_id`) REFERENCES `prompt_library_item` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Daily prompt library behavior metrics';

ALTER TABLE `ai_image_task`
  ADD COLUMN `source_prompt_id` BIGINT DEFAULT NULL COMMENT 'Stable prompt library item ID' AFTER `user_id`,
  MODIFY COLUMN `prompt` VARCHAR(4000) NOT NULL COMMENT 'Image generation prompt',
  ADD KEY `idx_ai_image_task_source_prompt` (`source_prompt_id`),
  ADD CONSTRAINT `fk_ai_image_task_source_prompt` FOREIGN KEY (`source_prompt_id`) REFERENCES `prompt_library_item` (`id`);

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT s.`id`, p.`id`, '查看提示词同步状态', 'prompt_library_sync_view', 3, NULL, NULL,
       'PromptLibrary.Sync.View', NULL, 20, 0, 1, 0, 0, '查看提示词库同步状态'
FROM `sys_site` s
JOIN `sys_menu` p ON p.`site_id` = s.`id` AND p.`menu_code` = 'ai_image_generate_page' AND p.`is_deleted` = 0
WHERE s.`site_code` = 'ai_image' AND s.`is_deleted` = 0
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'prompt_library_sync_view');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT s.`id`, p.`id`, '执行提示词同步', 'prompt_library_sync_run', 3, NULL, NULL,
       'PromptLibrary.Sync.Run', NULL, 21, 0, 1, 0, 0, '手动触发提示词库同步'
FROM `sys_site` s
JOIN `sys_menu` p ON p.`site_id` = s.`id` AND p.`menu_code` = 'ai_image_generate_page' AND p.`is_deleted` = 0
WHERE s.`site_code` = 'ai_image' AND s.`is_deleted` = 0
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'prompt_library_sync_run');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT s.`id`, p.`id`, '切换提示词快照', 'prompt_library_snapshot_switch', 3, NULL, NULL,
       'PromptLibrary.Sync.Switch', NULL, 22, 0, 1, 0, 0, '激活已成功发布的提示词库历史快照'
FROM `sys_site` s
JOIN `sys_menu` p ON p.`site_id` = s.`id` AND p.`menu_code` = 'ai_image_generate_page' AND p.`is_deleted` = 0
WHERE s.`site_code` = 'ai_image' AND s.`is_deleted` = 0
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'prompt_library_snapshot_switch');

INSERT IGNORE INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m ON m.`permission_code` IN (
  'PromptLibrary.Sync.View',
  'PromptLibrary.Sync.Run',
  'PromptLibrary.Sync.Switch'
)
WHERE r.`role_code` IN ('super_admin', 'ai_operator')
  AND r.`is_deleted` = 0
  AND m.`is_deleted` = 0;
