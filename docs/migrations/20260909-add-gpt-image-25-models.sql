USE `jokester.admin`;

START TRANSACTION;

INSERT INTO `ai_image_model_config`
  (`model_code`, `model_name`, `provider`, `provider_model`, `resolution_code`,
   `route_role`, `base_url`, `api_key`, `text_to_image_path`, `image_to_image_path`,
   `sort`, `status`, `created_at`, `updated_at`, `is_deleted`)
SELECT
  target.`model_code`,
  target.`model_name`,
  src.`provider`,
  target.`model_code`,
  src.`resolution_code`,
  src.`route_role`,
  src.`base_url`,
  src.`api_key`,
  src.`text_to_image_path`,
  src.`image_to_image_path`,
  src.`sort` + target.`sort_offset`,
  src.`status`,
  CURRENT_TIMESTAMP,
  NULL,
  0
FROM `ai_image_model_config` AS src
CROSS JOIN (
  SELECT 'gpt-image-2.5-flare' AS `model_code`, 'GPT Image 2.5 Flare' AS `model_name`, 10 AS `sort_offset`
  UNION ALL
  SELECT 'gpt-image-2.5-sunburst', 'GPT Image 2.5 Sunburst', 20
) AS target
WHERE src.`model_code` = 'gpt-image-2'
  AND src.`is_deleted` = 0
  AND src.`route_role` IN ('primary', 'fallback')
ON DUPLICATE KEY UPDATE `id` = `ai_image_model_config`.`id`;

INSERT INTO `ai_image_point_price`
  (`model_code`, `resolution_code`, `quality_code`, `points`, `price_amount`,
   `currency`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`)
SELECT
  target.`model_code`,
  src.`resolution_code`,
  src.`quality_code`,
  src.`points`,
  src.`price_amount`,
  src.`currency`,
  src.`sort`,
  src.`status`,
  CURRENT_TIMESTAMP,
  NULL,
  0
FROM `ai_image_point_price` AS src
CROSS JOIN (
  SELECT 'gpt-image-2.5-flare' AS `model_code`
  UNION ALL
  SELECT 'gpt-image-2.5-sunburst'
) AS target
WHERE src.`model_code` = 'gpt-image-2'
  AND src.`is_deleted` = 0
ON DUPLICATE KEY UPDATE `id` = `ai_image_point_price`.`id`;

COMMIT;
