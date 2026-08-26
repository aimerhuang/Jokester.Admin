USE `jokester.admin`;

CREATE TABLE IF NOT EXISTS `sys_user_membership_entitlement` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `tier_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `source` VARCHAR(30) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `business_key` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `starts_at` DATETIME NOT NULL,
  `expires_at` DATETIME NOT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'active',
  `revoked_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_membership_entitlement_business` (`business_key`),
  KEY `idx_sys_user_membership_entitlement_active`
    (`user_id`, `tier_code`, `status`, `revoked_at`, `expires_at`),
  CONSTRAINT `fk_sys_user_membership_entitlement_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `chk_sys_user_membership_entitlement_period`
    CHECK (`expires_at` > `starts_at`),
  CONSTRAINT `chk_sys_user_membership_entitlement_status`
    CHECK ((`status` = 'active' AND `revoked_at` IS NULL)
      OR (`status` = 'revoked' AND `revoked_at` IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='User membership entitlement source ledger';

-- Backfill Web monthly cards that are still valid. The point/recharge tables use
-- local server time, so membership timestamps intentionally follow that convention.
INSERT INTO `sys_user_membership_entitlement`
(`user_id`, `tier_code`, `source`, `business_key`, `starts_at`, `expires_at`, `status`, `created_at`)
SELECT
  redeem_code.`redeemed_by_user_id`,
  'monthly_vip',
  'recharge',
  CONCAT('recharge:redeem:', redeem_code.`id`),
  COALESCE(point_bucket.`created_at`, redeem_code.`redeemed_at`),
  COALESCE(point_bucket.`expires_at`, DATE_ADD(redeem_code.`redeemed_at`, INTERVAL 30 DAY)),
  'active',
  COALESCE(point_bucket.`created_at`, redeem_code.`redeemed_at`)
FROM `point_redeem_code` AS redeem_code
JOIN `point_recharge_package` AS package
  ON package.`id` = redeem_code.`package_id`
LEFT JOIN `sys_user_point_bucket` AS point_bucket
  ON point_bucket.`user_id` = redeem_code.`redeemed_by_user_id`
 AND point_bucket.`business_key` = CONCAT('recharge:redeem:', redeem_code.`id`)
WHERE package.`package_code` = 'monthly'
  AND redeem_code.`status` = 1
  AND redeem_code.`redeemed_by_user_id` IS NOT NULL
  AND redeem_code.`redeemed_at` IS NOT NULL
  AND COALESCE(point_bucket.`expires_at`, DATE_ADD(redeem_code.`redeemed_at`, INTERVAL 30 DAY)) > NOW()
  AND NOT EXISTS (
    SELECT 1
    FROM `sys_user_membership_entitlement` AS existing
    WHERE existing.`business_key` = CONCAT('recharge:redeem:', redeem_code.`id`)
  );

-- Apple transaction timestamps are UTC, while point buckets and this entitlement
-- ledger use local server time. Prefer the bucket timestamp and convert only the
-- fallback using the server's current UTC offset.
INSERT INTO `sys_user_membership_entitlement`
(`user_id`, `tier_code`, `source`, `business_key`, `starts_at`, `expires_at`, `status`, `created_at`)
SELECT
  apple_transaction.`user_id`,
  'monthly_vip',
  'apple_iap',
  CONCAT('apple:', apple_transaction.`transaction_id`, ':fulfill'),
  COALESCE(
    point_bucket.`created_at`,
    TIMESTAMPADD(
      MINUTE,
      TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW()),
      COALESCE(apple_transaction.`fulfilled_at`, apple_transaction.`created_at`)
    )
  ),
  COALESCE(
    point_bucket.`expires_at`,
    DATE_ADD(
      TIMESTAMPADD(
        MINUTE,
        TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW()),
        COALESCE(apple_transaction.`fulfilled_at`, apple_transaction.`created_at`)
      ),
      INTERVAL 30 DAY
    )
  ),
  'active',
  COALESCE(
    point_bucket.`created_at`,
    TIMESTAMPADD(
      MINUTE,
      TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW()),
      COALESCE(apple_transaction.`fulfilled_at`, apple_transaction.`created_at`)
    )
  )
FROM `apple_transaction` AS apple_transaction
JOIN `point_recharge_package` AS package
  ON package.`id` = apple_transaction.`package_id`
LEFT JOIN `sys_user_point_bucket` AS point_bucket
  ON point_bucket.`user_id` = apple_transaction.`user_id`
 AND point_bucket.`business_key` = CONCAT('apple:', apple_transaction.`transaction_id`, ':fulfill')
WHERE package.`package_code` = 'monthly'
  AND apple_transaction.`status` = 'fulfilled'
  AND COALESCE(
    point_bucket.`expires_at`,
    DATE_ADD(
      TIMESTAMPADD(
        MINUTE,
        TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW()),
        COALESCE(apple_transaction.`fulfilled_at`, apple_transaction.`created_at`)
      ),
      INTERVAL 30 DAY
    )
  ) > NOW()
  AND NOT EXISTS (
    SELECT 1
    FROM `sys_user_membership_entitlement` AS existing
    WHERE existing.`business_key` = CONCAT('apple:', apple_transaction.`transaction_id`, ':fulfill')
  );
