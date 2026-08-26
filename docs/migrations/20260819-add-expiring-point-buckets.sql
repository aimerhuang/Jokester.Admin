USE `jokester.admin`;

ALTER TABLE `point_recharge_order`
  ADD COLUMN `point_validity_days` INT DEFAULT NULL AFTER `points`;

ALTER TABLE `point_redeem_code`
  ADD COLUMN `point_validity_days` INT DEFAULT NULL AFTER `points`;

UPDATE `point_recharge_order` AS recharge_order
JOIN `point_recharge_package` AS package
  ON package.`id` = recharge_order.`package_id`
SET recharge_order.`point_validity_days` = COALESCE(package.`validity_days`, 0)
WHERE recharge_order.`point_validity_days` IS NULL;

UPDATE `point_redeem_code` AS redeem_code
LEFT JOIN `point_recharge_package` AS package
  ON package.`id` = redeem_code.`package_id`
SET redeem_code.`point_validity_days` = COALESCE(package.`validity_days`, 0)
WHERE redeem_code.`point_validity_days` IS NULL;

CREATE TABLE `sys_user_point_bucket` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `source` VARCHAR(50) NOT NULL,
  `business_key` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `granted_points` INT NOT NULL,
  `remaining_points` INT NOT NULL,
  `expired_points` INT NOT NULL DEFAULT 0,
  `expires_at` DATETIME DEFAULT NULL,
  `spend_priority` INT NOT NULL DEFAULT 100,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_point_bucket_id_user` (`id`, `user_id`),
  UNIQUE KEY `uk_sys_user_point_bucket_business` (`user_id`, `business_key`),
  KEY `idx_sys_user_point_bucket_spend` (`user_id`, `spend_priority`, `expires_at`, `id`),
  KEY `idx_sys_user_point_bucket_expire` (`user_id`, `expires_at`, `remaining_points`),
  CONSTRAINT `fk_sys_user_point_bucket_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `chk_sys_user_point_bucket_granted`
    CHECK (`granted_points` > 0),
  CONSTRAINT `chk_sys_user_point_bucket_remaining`
    CHECK (`remaining_points` >= 0
      AND `expired_points` >= 0
      AND `remaining_points` + `expired_points` <= `granted_points`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Tracked point grant buckets; null expiry means permanent';

CREATE TABLE `sys_user_point_bucket_usage` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `bucket_id` BIGINT NOT NULL,
  `user_id` BIGINT NOT NULL,
  `business_key` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `used_points` INT NOT NULL,
  `refunded_points` INT NOT NULL DEFAULT 0,
  `deferred_clawback_points` INT NOT NULL DEFAULT 0,
  `deferred_clawback_business_key` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_point_bucket_usage_business` (`bucket_id`, `business_key`),
  KEY `idx_sys_user_point_bucket_usage_bucket_user` (`bucket_id`, `user_id`),
  KEY `idx_sys_user_point_bucket_usage_business` (`user_id`, `business_key`, `id`),
  KEY `idx_sys_user_point_bucket_usage_deferred` (`user_id`, `deferred_clawback_business_key`),
  CONSTRAINT `fk_sys_user_point_bucket_usage_bucket`
    FOREIGN KEY (`bucket_id`, `user_id`) REFERENCES `sys_user_point_bucket` (`id`, `user_id`),
  CONSTRAINT `fk_sys_user_point_bucket_usage_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `chk_sys_user_point_bucket_usage_used`
    CHECK (`used_points` > 0),
  CONSTRAINT `chk_sys_user_point_bucket_usage_refunded`
    CHECK (`refunded_points` >= 0
      AND `deferred_clawback_points` >= 0
      AND `refunded_points` + `deferred_clawback_points` <= `used_points`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Point consumption allocations to tracked grant buckets';

-- Run this migration with API and Worker writes stopped. Preserve a same-day
-- sign-in grant so the new process can continue with the bucket ledger. Legacy
-- interleaved reservations cannot be reconstructed exactly from aggregate details,
-- so only points provably attributable to the sign-in grant are carried forward.
-- Historical Apple fulfillments are also left untracked; legacy refunds may only
-- consume untracked balance and record any remaining amount as Apple debt.
INSERT INTO `sys_user_point_bucket`
(`user_id`, `source`, `business_key`, `granted_points`, `remaining_points`, `expires_at`, `spend_priority`, `created_at`)
SELECT
  sign_in.`user_id`,
  'sign_in',
  sign_in.`business_key`,
  sign_in.`change_points`,
  LEAST(
    sign_in.`change_points`,
    GREATEST(0, user_account.`point_balance`),
    GREATEST(
      0,
      sign_in.`change_points` - COALESCE((
        SELECT SUM(CASE
          WHEN consumption.`change_points` < 0 THEN -consumption.`change_points`
          WHEN consumption.`source` = 'image_refund'
            AND consumption.`change_points` > 0
            AND EXISTS (
              SELECT 1
              FROM `sys_user_point_detail` AS image_reserve
              WHERE image_reserve.`user_id` = sign_in.`user_id`
                AND image_reserve.`source` = 'image_generate'
                AND image_reserve.`change_points` < 0
                AND image_reserve.`business_key` = CONCAT(
                  SUBSTRING_INDEX(consumption.`business_key`, ':', 2),
                  ':reserve'
                )
                AND (
                  image_reserve.`created_at` > sign_in.`created_at`
                  OR (
                    image_reserve.`created_at` = sign_in.`created_at`
                    AND image_reserve.`id` > sign_in.`id`
                  )
                )
            ) THEN -consumption.`change_points`
          ELSE 0
        END)
        FROM `sys_user_point_detail` AS consumption
        WHERE consumption.`user_id` = sign_in.`user_id`
          AND (
            consumption.`created_at` > sign_in.`created_at`
            OR (consumption.`created_at` = sign_in.`created_at` AND consumption.`id` > sign_in.`id`)
          )
      ), 0)
    )
  ),
  DATE_ADD(DATE(sign_in.`created_at`), INTERVAL 1 DAY),
  100,
  sign_in.`created_at`
FROM `sys_user_point_detail` AS sign_in
JOIN `sys_user` AS user_account
  ON user_account.`id` = sign_in.`user_id`
WHERE sign_in.`source` = 'sign_in'
  AND sign_in.`change_points` > 0
  AND sign_in.`business_key` IS NOT NULL
  AND sign_in.`business_key` <> ''
  AND sign_in.`created_at` >= CURRENT_DATE()
  AND sign_in.`created_at` < DATE_ADD(CURRENT_DATE(), INTERVAL 1 DAY)
  AND NOT EXISTS (
    SELECT 1
    FROM `sys_user_point_bucket` AS existing_bucket
    WHERE existing_bucket.`user_id` = sign_in.`user_id`
      AND existing_bucket.`business_key` = sign_in.`business_key`
  );
