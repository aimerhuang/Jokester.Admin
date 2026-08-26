USE `jokester.admin`;

-- GPT Image 2 supports explicit custom sizes. Reuse the already verified and
-- enabled generic primary channel for an exact 2K route. Specialized third-party
-- 1K/4K fallback model names are deliberately not reused for 2K.
START TRANSACTION;

INSERT INTO `ai_image_model_config`
  (`model_code`, `model_name`, `provider`, `provider_model`, `resolution_code`,
   `route_role`, `base_url`, `api_key`, `text_to_image_path`, `image_to_image_path`,
   `sort`, `status`, `created_at`, `updated_at`, `is_deleted`)
SELECT
  src.`model_code`,
  'GPT Image 2 2K',
  src.`provider`,
  src.`provider_model`,
  '2k',
  'primary',
  src.`base_url`,
  src.`api_key`,
  src.`text_to_image_path`,
  src.`image_to_image_path`,
  2,
  1,
  CURRENT_TIMESTAMP,
  NULL,
  0
FROM `ai_image_model_config` AS src
WHERE src.`model_code` = 'gpt-image-2'
  AND src.`provider_model` = 'gpt-image-2'
  AND src.`resolution_code` = '1k'
  AND src.`route_role` = 'primary'
  AND src.`status` = 1
  AND src.`is_deleted` = 0
  AND TRIM(src.`base_url`) <> ''
  AND TRIM(src.`api_key`) <> ''
ORDER BY src.`id`
LIMIT 1
ON DUPLICATE KEY UPDATE
  `updated_at` = IF(
    `ai_image_model_config`.`model_name` <> VALUES(`model_name`)
      OR `ai_image_model_config`.`provider` <> VALUES(`provider`)
      OR `ai_image_model_config`.`provider_model` <> VALUES(`provider_model`)
      OR `ai_image_model_config`.`base_url` <> VALUES(`base_url`)
      OR `ai_image_model_config`.`api_key` <> VALUES(`api_key`)
      OR `ai_image_model_config`.`text_to_image_path` <> VALUES(`text_to_image_path`)
      OR `ai_image_model_config`.`image_to_image_path` <> VALUES(`image_to_image_path`)
      OR `ai_image_model_config`.`sort` <> VALUES(`sort`)
      OR `ai_image_model_config`.`status` <> VALUES(`status`)
      OR `ai_image_model_config`.`is_deleted` <> VALUES(`is_deleted`),
    CURRENT_TIMESTAMP,
    `ai_image_model_config`.`updated_at`
  ),
  `model_name` = VALUES(`model_name`),
  `provider` = VALUES(`provider`),
  `provider_model` = VALUES(`provider_model`),
  `base_url` = VALUES(`base_url`),
  `api_key` = VALUES(`api_key`),
  `text_to_image_path` = VALUES(`text_to_image_path`),
  `image_to_image_path` = VALUES(`image_to_image_path`),
  `sort` = VALUES(`sort`),
  `status` = VALUES(`status`),
  `is_deleted` = VALUES(`is_deleted`);

COMMIT;

-- This must return one enabled row with provider_model = 'gpt-image-2'.
SELECT `id`, `model_code`, `provider`, `provider_model`, `resolution_code`,
       `route_role`, `status`, `is_deleted`,
       CASE WHEN `api_key` <> '' THEN 1 ELSE 0 END AS `has_api_key`
FROM `ai_image_model_config`
WHERE `model_code` = 'gpt-image-2'
  AND `provider_model` = 'gpt-image-2'
  AND `resolution_code` = '2k'
  AND `route_role` = 'primary'
  AND `status` = 1
  AND `is_deleted` = 0;
