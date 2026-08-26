-- AI image size-mode-v1 expand and legacy classification migration.
-- Apply after 20260821-add-gpt-image-2-2k-primary-route.sql.
-- Stop every pre-expand API/worker generation before enabling writes to this schema.
-- This file intentionally publishes no auto capability, route, price, URL, or secret.

CREATE TABLE `ai_image_model_release` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `model_code` VARCHAR(100) NOT NULL,
  `model_name` VARCHAR(100) NOT NULL,
  `catalog_version` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `size_contract_version` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `default_size_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'explicit',
  `status` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'draft',
  `revoked_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `published_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_model_release_catalog` (`model_code`, `catalog_version`),
  KEY `idx_ai_image_model_release_status` (`model_code`, `status`, `revoked_at`),
  CONSTRAINT `chk_ai_image_model_release_contract` CHECK (`size_contract_version` IN ('size-mode-v1','legacy-explicit-v1','legacy-aspect-auto')),
  CONSTRAINT `chk_ai_image_model_release_default_mode` CHECK (`default_size_mode` IN ('explicit','auto')),
  CONSTRAINT `chk_ai_image_model_release_status` CHECK (`status` IN ('draft','published','archived','revoked'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_model_current_release` (
  `model_code` VARCHAR(100) NOT NULL,
  `model_release_id` BIGINT NOT NULL,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`model_code`),
  UNIQUE KEY `uk_ai_image_current_release_id` (`model_release_id`),
  CONSTRAINT `fk_ai_image_current_release_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_model_release_route` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `model_release_id` BIGINT NOT NULL,
  `route_config_id` BIGINT NOT NULL,
  `size_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `resolution_code` VARCHAR(50) NOT NULL DEFAULT '',
  `route_role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `provider_protocol` VARCHAR(50) NOT NULL,
  `consent_provider_code` VARCHAR(50) NOT NULL,
  `provider_model` VARCHAR(100) NOT NULL,
  `base_url` VARCHAR(500) NOT NULL,
  `text_to_image_path` VARCHAR(200) NOT NULL,
  `image_to_image_path` VARCHAR(200) NOT NULL,
  `secret_version_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `verified_generations` TINYINT(1) NOT NULL DEFAULT 0,
  `verified_edits` TINYINT(1) NOT NULL DEFAULT 0,
  `verified_mask_edits` TINYINT(1) NOT NULL DEFAULT 0,
  `sort` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_release_route_slot` (`model_release_id`,`size_mode`,`resolution_code`,`route_role`),
  KEY `idx_ai_image_release_route_resolve` (`model_release_id`,`size_mode`,`resolution_code`,`route_role`,`sort`),
  CONSTRAINT `fk_ai_image_release_route_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`),
  CONSTRAINT `fk_ai_image_release_route_config` FOREIGN KEY (`route_config_id`) REFERENCES `ai_image_model_config` (`id`),
  CONSTRAINT `chk_ai_image_release_route_mode` CHECK (`size_mode` IN ('explicit','auto')),
  CONSTRAINT `chk_ai_image_release_route_role` CHECK (`route_role` IN ('primary','fallback'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_model_release_price` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `model_release_id` BIGINT NOT NULL,
  `model_code` VARCHAR(100) NOT NULL,
  `pricing_mode` VARCHAR(24) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `resolution_code` VARCHAR(50) NOT NULL DEFAULT '',
  `quality_code` VARCHAR(50) NOT NULL DEFAULT '',
  `points` INT NOT NULL,
  `price_amount` DECIMAL(10,2) NOT NULL,
  `currency` CHAR(3) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'CNY',
  `sort` INT NOT NULL DEFAULT 0,
  `status` TINYINT NOT NULL DEFAULT 1,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_release_price_key` (`model_release_id`,`model_code`,`pricing_mode`,`resolution_code`,`quality_code`),
  KEY `idx_ai_image_release_price_lookup` (`model_release_id`,`pricing_mode`,`resolution_code`,`quality_code`,`status`),
  CONSTRAINT `fk_ai_image_release_price_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`),
  CONSTRAINT `chk_ai_image_release_price_mode` CHECK (`pricing_mode` IN ('explicit','auto','legacy_resolution')),
  CONSTRAINT `chk_ai_image_release_price_points` CHECK (`points` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `ai_image_task`
  MODIFY COLUMN `resolution_code` VARCHAR(50) DEFAULT NULL,
  MODIFY COLUMN `aspect_ratio_code` VARCHAR(50) DEFAULT NULL,
  ADD COLUMN `model_code` VARCHAR(100) DEFAULT NULL AFTER `model_name`,
  ADD COLUMN `size_contract_version` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL AFTER `model_code`,
  ADD COLUMN `size_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL AFTER `size_contract_version`,
  ADD COLUMN `requested_size` VARCHAR(50) DEFAULT NULL AFTER `size_mode`,
  ADD COLUMN `requested_width` INT DEFAULT NULL AFTER `requested_size`,
  ADD COLUMN `requested_height` INT DEFAULT NULL AFTER `requested_width`,
  ADD COLUMN `output_width` INT DEFAULT NULL AFTER `requested_height`,
  ADD COLUMN `output_height` INT DEFAULT NULL AFTER `output_width`,
  ADD COLUMN `output_size` VARCHAR(50) DEFAULT NULL AFTER `output_height`,
  ADD COLUMN `output_mime_type` VARCHAR(100) DEFAULT NULL AFTER `output_size`,
  ADD COLUMN `model_release_id` BIGINT DEFAULT NULL AFTER `output_mime_type`,
  ADD COLUMN `price_id` BIGINT DEFAULT NULL AFTER `model_release_id`,
  ADD COLUMN `price_release_id` BIGINT DEFAULT NULL AFTER `price_id`,
  ADD COLUMN `unit_point_cost` INT DEFAULT NULL AFTER `price_release_id`,
  ADD COLUMN `refunded_points` INT DEFAULT NULL AFTER `billing_status`,
  ADD COLUMN `failure_code` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL AFTER `error_message`,
  ADD COLUMN `failure_stage` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL AFTER `failure_code`,
  ADD COLUMN `retryable` TINYINT(1) DEFAULT NULL AFTER `failure_stage`,
  ADD COLUMN `claim_epoch` BIGINT NOT NULL DEFAULT 0 AFTER `retryable`,
  ADD COLUMN `claim_token_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL AFTER `claim_epoch`,
  ADD COLUMN `lease_expires_at` DATETIME DEFAULT NULL AFTER `claim_token_hash`,
  ADD COLUMN `heartbeat_at` DATETIME DEFAULT NULL AFTER `lease_expires_at`,
  ADD KEY `idx_ai_image_task_release_status` (`model_release_id`,`status`,`billing_status`),
  ADD KEY `idx_ai_image_task_claim` (`status`,`billing_status`,`lease_expires_at`),
  ADD CONSTRAINT `fk_ai_image_task_model_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`),
  ADD CONSTRAINT `fk_ai_image_task_release_price` FOREIGN KEY (`price_id`) REFERENCES `ai_image_model_release_price` (`id`),
  ADD CONSTRAINT `fk_ai_image_task_price_release` FOREIGN KEY (`price_release_id`) REFERENCES `ai_image_model_release` (`id`);

CREATE TABLE `ai_image_request_idempotency` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `idempotency_key_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `canonical_payload_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `canonicalization_version` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `normalization_profile` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `size_contract_version` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `model_release_id` BIGINT DEFAULT NULL,
  `admission_reservation_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `admission_quota_date` CHAR(8) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `reserved_point_cost` INT NOT NULL DEFAULT 0,
  `requested_image_count` INT NOT NULL,
  `task_count` INT NOT NULL,
  `legacy_batch_shape` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `status` VARCHAR(24) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'active',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_request_user_key` (`user_id`,`idempotency_key_hash`),
  KEY `idx_ai_image_request_release` (`model_release_id`,`created_at`),
  CONSTRAINT `fk_ai_image_request_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_ai_image_request_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_request_task` (
  `request_id` BIGINT NOT NULL,
  `task_ordinal` INT NOT NULL,
  `task_id` BIGINT NOT NULL,
  PRIMARY KEY (`request_id`,`task_ordinal`),
  UNIQUE KEY `uk_ai_image_request_task_id` (`task_id`),
  CONSTRAINT `fk_ai_image_request_task_request` FOREIGN KEY (`request_id`) REFERENCES `ai_image_request_idempotency` (`id`),
  CONSTRAINT `fk_ai_image_request_task_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_task_input` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `task_id` BIGINT NOT NULL,
  `role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `input_ordinal` INT NOT NULL,
  `input_kind` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `asset_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `owner_user_id` BIGINT NOT NULL,
  `storage_key` VARCHAR(500) DEFAULT NULL,
  `content_sha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL,
  `legacy_url` VARCHAR(500) DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_task_input_order` (`task_id`,`role`,`input_ordinal`),
  KEY `idx_ai_image_task_input_asset` (`asset_id`,`owner_user_id`),
  CONSTRAINT `fk_ai_image_task_input_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`),
  CONSTRAINT `chk_ai_image_task_input_role` CHECK (`role` IN ('reference','mask')),
  CONSTRAINT `chk_ai_image_task_input_kind` CHECK (`input_kind` IN ('asset','legacy_url'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_task_result` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `task_id` BIGINT NOT NULL,
  `result_ordinal` INT NOT NULL,
  `url` VARCHAR(500) NOT NULL,
  `width` INT NOT NULL,
  `height` INT NOT NULL,
  `size` VARCHAR(50) NOT NULL,
  `mime_type` VARCHAR(100) NOT NULL,
  `is_quarantined` TINYINT(1) NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_task_result_order` (`task_id`,`result_ordinal`),
  CONSTRAINT `fk_ai_image_task_result_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_task_outbox` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `request_id` BIGINT NOT NULL,
  `task_id` BIGINT NOT NULL,
  `status` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'pending',
  `attempt_count` INT NOT NULL DEFAULT 0,
  `next_attempt_at` DATETIME NOT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_task_outbox_task` (`request_id`,`task_id`),
  KEY `idx_ai_image_task_outbox_dispatch` (`status`,`next_attempt_at`,`id`),
  CONSTRAINT `fk_ai_image_task_outbox_request` FOREIGN KEY (`request_id`) REFERENCES `ai_image_request_idempotency` (`id`),
  CONSTRAINT `fk_ai_image_task_outbox_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `ai_image_provider_attempt` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `attempt_id` CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `task_id` BIGINT NOT NULL,
  `claim_epoch` BIGINT NOT NULL,
  `model_release_id` BIGINT DEFAULT NULL,
  `release_route_id` BIGINT DEFAULT NULL,
  `route_role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL,
  `consent_provider_code` VARCHAR(50) DEFAULT NULL,
  `upstream_idempotency_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `state` VARCHAR(24) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `started_at` DATETIME NOT NULL,
  `deadline` DATETIME NOT NULL,
  `reconcile_by` DATETIME NOT NULL,
  `completed_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_provider_attempt_id` (`attempt_id`),
  UNIQUE KEY `uk_ai_image_provider_attempt_epoch` (`task_id`,`claim_epoch`),
  KEY `idx_ai_image_provider_attempt_reconcile` (`state`,`deadline`,`reconcile_by`),
  CONSTRAINT `fk_ai_image_provider_attempt_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`),
  CONSTRAINT `fk_ai_image_provider_attempt_release` FOREIGN KEY (`model_release_id`) REFERENCES `ai_image_model_release` (`id`),
  CONSTRAINT `fk_ai_image_provider_attempt_route` FOREIGN KEY (`release_route_id`) REFERENCES `ai_image_model_release_route` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Mutually exclusive legacy classification. It deliberately does not infer output dimensions.
UPDATE `ai_image_task`
SET `model_code` = CASE
      WHEN LOWER(COALESCE(`model_name`, '')) LIKE 'gpt-image%' THEN 'gpt-image-2'
      WHEN LOWER(COALESCE(`model_name`, '')) IN ('nano-banana-2','gemini-3.1-flash-image-preview') THEN 'nano-banana-2'
      WHEN LOWER(COALESCE(`model_name`, '')) IN ('nano-banana-pro','gemini-3-pro-image-preview') THEN 'nano-banana-pro'
      ELSE NULL
    END,
    `size_contract_version` = CASE
      WHEN `aspect_ratio_code` = 'auto'
        AND `size` = 'auto'
        AND COALESCE(`width`, 0) = 0
        AND COALESCE(`height`, 0) = 0 THEN 'legacy-aspect-auto'
      WHEN `aspect_ratio_code` IS NOT NULL
        AND `aspect_ratio_code` <> 'auto'
        AND `width` > 0 AND `height` > 0
        AND `size` = CONCAT(`width`, 'x', `height`)
        AND `size` REGEXP '^[1-9][0-9]*x[1-9][0-9]*$' THEN 'legacy-explicit-v1'
      ELSE 'legacy-unknown'
    END,
    `size_mode` = CASE
      WHEN `aspect_ratio_code` = 'auto' AND `size` = 'auto'
        AND COALESCE(`width`, 0) = 0 AND COALESCE(`height`, 0) = 0 THEN 'auto'
      WHEN `aspect_ratio_code` IS NOT NULL AND `aspect_ratio_code` <> 'auto'
        AND `width` > 0 AND `height` > 0
        AND `size` = CONCAT(`width`, 'x', `height`)
        AND `size` REGEXP '^[1-9][0-9]*x[1-9][0-9]*$' THEN 'explicit'
      ELSE NULL
    END,
    `requested_size` = CASE
      WHEN `aspect_ratio_code` = 'auto' AND `size` = 'auto'
        AND COALESCE(`width`, 0) = 0 AND COALESCE(`height`, 0) = 0 THEN 'auto'
      WHEN `aspect_ratio_code` IS NOT NULL AND `aspect_ratio_code` <> 'auto'
        AND `width` > 0 AND `height` > 0
        AND `size` = CONCAT(`width`, 'x', `height`)
        AND `size` REGEXP '^[1-9][0-9]*x[1-9][0-9]*$' THEN `size`
      ELSE NULL
    END,
    `requested_width` = CASE
      WHEN `aspect_ratio_code` IS NOT NULL AND `aspect_ratio_code` <> 'auto'
        AND `width` > 0 AND `height` > 0 AND `size` = CONCAT(`width`, 'x', `height`) THEN `width`
      ELSE NULL
    END,
    `requested_height` = CASE
      WHEN `aspect_ratio_code` IS NOT NULL AND `aspect_ratio_code` <> 'auto'
        AND `width` > 0 AND `height` > 0 AND `size` = CONCAT(`width`, 'x', `height`) THEN `height`
      ELSE NULL
    END,
    `output_width` = NULL,
    `output_height` = NULL,
    `output_size` = NULL,
    `output_mime_type` = NULL
WHERE `size_contract_version` IS NULL;

-- Deployment automation must create and approve immutable explicit releases, then atomically
-- switch ai_image_model_current_release. Auto rows remain absent until route validation,
-- independent pricing, result allowlists, and product approval are complete.
