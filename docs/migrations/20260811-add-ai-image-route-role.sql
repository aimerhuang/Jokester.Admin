-- AI image provider primary/fallback routing.
-- Stop every API/worker instance and back up the table before applying this file.

ALTER TABLE `ai_image_model_config`
  ADD COLUMN `route_role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci
    NOT NULL DEFAULT 'primary'
    COMMENT 'Route role: primary or fallback'
    AFTER `resolution_code`;

-- Existing GPT rows are the legacy database fallback. Nano Banana keeps one primary route.
UPDATE `ai_image_model_config`
SET `route_role` = CASE
    WHEN `model_code` = 'gpt-image-2' THEN 'fallback'
    ELSE 'primary'
  END;

-- NULL must be normalized before adding a unique route slot. MySQL UNIQUE permits many NULLs.
UPDATE `ai_image_model_config`
SET `resolution_code` = ''
WHERE `resolution_code` IS NULL OR TRIM(`resolution_code`) = '';

ALTER TABLE `ai_image_model_config`
  MODIFY COLUMN `resolution_code` VARCHAR(50) NOT NULL DEFAULT ''
    COMMENT 'Resolution code; empty means all resolutions',
  DROP KEY `uk_ai_image_model_config_code_resolution`,
  DROP KEY `idx_ai_image_model_config_code_status_sort`,
  ADD CONSTRAINT `chk_ai_image_model_config_route_role`
    CHECK (`route_role` IN ('primary', 'fallback')),
  ADD UNIQUE KEY `uk_ai_image_model_config_code_resolution_role`
    (`model_code`, `resolution_code`, `route_role`),
  ADD KEY `idx_ai_image_model_config_resolve`
    (`model_code`, `resolution_code`, `status`, `is_deleted`, `route_role`, `sort`);

-- Create disabled GPT primary slots without copying environment-specific URLs or secrets.
INSERT INTO `ai_image_model_config`
  (`model_code`, `model_name`, `provider`, `provider_model`, `resolution_code`, `route_role`,
   `base_url`, `api_key`, `text_to_image_path`, `image_to_image_path`, `sort`, `status`,
   `created_at`, `updated_at`, `is_deleted`)
SELECT
  `model_code`, `model_name`, `provider`, 'gpt-image-2', `resolution_code`, 'primary',
  '', '', `text_to_image_path`, `image_to_image_path`, `sort`, 0,
  CURRENT_TIMESTAMP, NULL, 0
FROM `ai_image_model_config`
WHERE `model_code` = 'gpt-image-2'
  AND `route_role` = 'fallback'
  AND `is_deleted` = 0;

-- Configure the disabled primary rows in the target database, then enable them:
-- UPDATE `ai_image_model_config`
-- SET `base_url` = '<primary-base-url>',
--     `api_key` = '<primary-api-key>',
--     `status` = 1,
--     `updated_at` = CURRENT_TIMESTAMP
-- WHERE `model_code` = 'gpt-image-2'
--   AND `route_role` = 'primary'
--   AND `is_deleted` = 0;

-- Force all GPT requests to the fallback route without changing route identities:
-- UPDATE `ai_image_model_config`
-- SET `status` = CASE `route_role`
--     WHEN 'primary' THEN 0
--     WHEN 'fallback' THEN 1
--   END,
--   `updated_at` = CURRENT_TIMESTAMP
-- WHERE `model_code` = 'gpt-image-2'
--   AND `route_role` IN ('primary', 'fallback')
--   AND `is_deleted` = 0;
