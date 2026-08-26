USE `jokester.admin`;

-- GPT Image 2 2K billing only. This migration deliberately does not create or
-- enable an `ai_image_model_config` route; configure a verified 2K provider
-- route separately before exposing 2K generation.
START TRANSACTION;

INSERT INTO `ai_image_parameter`
  (`param_type`, `param_code`, `param_name`, `provider_value`, `value_int_1`,
   `value_int_2`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`)
VALUES
  ('resolution', '2k', '2K(推荐尺寸)', NULL, 2048, NULL, 2, 1, CURRENT_TIMESTAMP, NULL, 0)
ON DUPLICATE KEY UPDATE
  `param_name` = VALUES(`param_name`),
  `provider_value` = VALUES(`provider_value`),
  `value_int_1` = VALUES(`value_int_1`),
  `value_int_2` = VALUES(`value_int_2`),
  `sort` = VALUES(`sort`),
  `status` = VALUES(`status`),
  `updated_at` = CURRENT_TIMESTAMP,
  `is_deleted` = VALUES(`is_deleted`);

UPDATE `ai_image_parameter`
SET `sort` = CASE `param_code` WHEN '1k' THEN 1 WHEN '4k' THEN 3 END,
    `updated_at` = CURRENT_TIMESTAMP
WHERE `param_type` = 'resolution'
  AND `param_code` IN ('1k', '4k')
  AND `sort` <> CASE `param_code` WHEN '1k' THEN 1 WHEN '4k' THEN 3 END;

UPDATE `ai_image_point_price`
SET `sort` = FIELD(`quality_code`, 'low', 'med', 'high'),
    `updated_at` = CURRENT_TIMESTAMP
WHERE `model_code` = 'gpt-image-2'
  AND `resolution_code` = '1k'
  AND `quality_code` IN ('low', 'med', 'high')
  AND `sort` <> FIELD(`quality_code`, 'low', 'med', 'high');

INSERT INTO `ai_image_point_price`
  (`model_code`, `resolution_code`, `quality_code`, `points`, `price_amount`,
   `currency`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`)
VALUES
  ('gpt-image-2', '2k', 'low', 15, 0.15, 'CNY', 4, 1, CURRENT_TIMESTAMP, NULL, 0),
  ('gpt-image-2', '2k', 'med', 30, 0.30, 'CNY', 5, 1, CURRENT_TIMESTAMP, NULL, 0),
  ('gpt-image-2', '2k', 'high', 60, 0.60, 'CNY', 6, 1, CURRENT_TIMESTAMP, NULL, 0)
ON DUPLICATE KEY UPDATE
  `updated_at` = IF(
    `points` <> VALUES(`points`)
      OR `price_amount` <> VALUES(`price_amount`)
      OR `currency` <> VALUES(`currency`)
      OR `sort` <> VALUES(`sort`)
      OR `status` <> VALUES(`status`)
      OR `is_deleted` <> VALUES(`is_deleted`),
    CURRENT_TIMESTAMP,
    `updated_at`
  ),
  `points` = VALUES(`points`),
  `price_amount` = VALUES(`price_amount`),
  `currency` = VALUES(`currency`),
  `sort` = VALUES(`sort`),
  `status` = VALUES(`status`),
  `is_deleted` = VALUES(`is_deleted`);

UPDATE `ai_image_point_price`
SET `sort` = 6 + FIELD(`quality_code`, 'low', 'med', 'high'),
    `updated_at` = CURRENT_TIMESTAMP
WHERE `model_code` = 'gpt-image-2'
  AND `resolution_code` = '4k'
  AND `quality_code` IN ('low', 'med', 'high')
  AND `sort` <> 6 + FIELD(`quality_code`, 'low', 'med', 'high');

COMMIT;
