-- AI model route admin + public model status (P0).
-- Apply after 20261105-add-ai-image-route-request-format.sql, and before deploying the API that reads these columns.
-- See docs/ai-model-config-status-prd.md. Keep jokester.admin.sql aligned.
-- `ai_image_task` may be large: the column/index changes below use online DDL.

USE `jokester.admin`;

ALTER TABLE `ai_image_model_config`
  ADD COLUMN `revision` INT NOT NULL DEFAULT 0 COMMENT '后台乐观并发版本' AFTER `status`,
  ADD COLUMN `api_key_updated_at` DATETIME DEFAULT NULL COMMENT 'API Key最近更换时间(UTC)' AFTER `revision`,
  ADD COLUMN `updated_by` BIGINT DEFAULT NULL COMMENT '最近修改人用户ID' AFTER `updated_at`;

ALTER TABLE `ai_image_task`
  ADD COLUMN `health_model_code` VARCHAR(100) DEFAULT NULL COMMENT '模型状态统计用的归一化模型编码；不计入样本为空' AFTER `retryable`,
  ADD COLUMN `health_sample` TINYINT DEFAULT NULL COMMENT '模型状态样本：1模型侧成功 2模型侧失败；不计入为空' AFTER `health_model_code`,
  ALGORITHM=INPLACE, LOCK=NONE;

ALTER TABLE `ai_image_task`
  ADD KEY `idx_ai_image_task_health` (`health_model_code`, `completed_at`),
  ALGORITHM=INPLACE, LOCK=NONE;

CREATE TABLE `ai_image_model_config_audit` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `config_id` BIGINT NOT NULL,
  `model_code` VARCHAR(100) NOT NULL,
  `resolution_code` VARCHAR(50) NOT NULL DEFAULT '',
  `route_role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `action` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `changed_fields` VARCHAR(500) NOT NULL DEFAULT '',
  `before_json` TEXT DEFAULT NULL,
  `after_json` TEXT DEFAULT NULL,
  `operator_user_id` BIGINT NOT NULL,
  `operator_ip` VARCHAR(50) DEFAULT NULL,
  `created_at` DATETIME NOT NULL COMMENT 'UTC',
  PRIMARY KEY (`id`),
  KEY `idx_ai_image_model_config_audit_config` (`config_id`, `id`),
  KEY `idx_ai_image_model_config_audit_created` (`created_at`),
  CONSTRAINT `fk_ai_image_model_config_audit_config` FOREIGN KEY (`config_id`) REFERENCES `ai_image_model_config` (`id`),
  CONSTRAINT `chk_ai_image_model_config_audit_action` CHECK (`action` IN ('create', 'update', 'enable', 'disable', 'rotate_key'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图模型路由变更审计(不含明文Key)';

-- Backfill the last 24 hours so the status page has data on day one.
-- ai_image_task timestamps are stored as Hong Kong time (UTC+8).
-- Backfilled failures cannot be split into model-side vs user-side; that error leaves the 24h window within a day.
UPDATE `ai_image_task`
SET
  `health_model_code` = CASE LOWER(TRIM(COALESCE(NULLIF(TRIM(`model_code`), ''), NULLIF(TRIM(`model_name`), ''), 'gpt-image-2')))
    WHEN 'nanao-banana-2' THEN 'nano-banana-2'
    ELSE LOWER(TRIM(COALESCE(NULLIF(TRIM(`model_code`), ''), NULLIF(TRIM(`model_name`), ''), 'gpt-image-2')))
  END,
  `health_sample` = CASE `status` WHEN 1 THEN 1 ELSE 2 END
WHERE `status` IN (1, 2)
  AND `completed_at` IS NOT NULL
  AND `completed_at` >= DATE_ADD(UTC_TIMESTAMP(), INTERVAL 8 HOUR) - INTERVAL 24 HOUR
  AND `health_sample` IS NULL;
