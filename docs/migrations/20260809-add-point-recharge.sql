USE `jokester.admin`;

CREATE TABLE IF NOT EXISTS `point_recharge_package` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `package_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `name` VARCHAR(100) NOT NULL,
  `description` VARCHAR(300) DEFAULT NULL,
  `points` INT NOT NULL,
  `repeat_points` INT DEFAULT NULL,
  `price_amount` DECIMAL(10,2) NOT NULL,
  `currency` VARCHAR(10) NOT NULL DEFAULT 'CNY',
  `validity_days` INT DEFAULT NULL,
  `bonus_percent` INT NOT NULL DEFAULT 0,
  `badge_code` VARCHAR(50) DEFAULT NULL,
  `benefits_json` LONGTEXT DEFAULT NULL,
  `purchase_url` VARCHAR(500) DEFAULT NULL,
  `is_featured` TINYINT(1) NOT NULL DEFAULT 0,
  `sort` INT NOT NULL DEFAULT 0,
  `status` TINYINT NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_recharge_package_code` (`package_code`),
  KEY `idx_point_recharge_package_status_sort` (`status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Point recharge packages';

CREATE TABLE IF NOT EXISTS `point_recharge_order` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `order_no` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `user_id` BIGINT NOT NULL,
  `package_id` BIGINT NOT NULL,
  `package_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `points` INT NOT NULL,
  `price_amount` DECIMAL(10,2) NOT NULL,
  `currency` VARCHAR(10) NOT NULL DEFAULT 'CNY',
  `purchase_url` VARCHAR(500) DEFAULT NULL,
  `status` TINYINT NOT NULL DEFAULT 0,
  `expires_at` DATETIME NOT NULL,
  `paid_at` DATETIME DEFAULT NULL,
  `fulfilled_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_recharge_order_no` (`order_no`),
  KEY `idx_point_recharge_order_user_created` (`user_id`, `created_at`),
  KEY `idx_point_recharge_order_status_expires` (`status`, `expires_at`),
  CONSTRAINT `fk_point_recharge_order_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_point_recharge_order_package` FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Point recharge orders';

CREATE TABLE IF NOT EXISTS `point_redeem_code` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `code_mask` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `package_id` BIGINT DEFAULT NULL,
  `order_id` BIGINT DEFAULT NULL,
  `points` INT NOT NULL,
  `status` TINYINT NOT NULL DEFAULT 0,
  `redeemed_by_user_id` BIGINT DEFAULT NULL,
  `expires_at` DATETIME DEFAULT NULL,
  `redeemed_at` DATETIME DEFAULT NULL,
  `created_by` BIGINT DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_redeem_code_hash` (`code_hash`),
  UNIQUE KEY `uk_point_redeem_code_order` (`order_id`),
  KEY `idx_point_redeem_code_status_expires` (`status`, `expires_at`),
  KEY `idx_point_redeem_code_user_package` (`redeemed_by_user_id`, `package_id`),
  CONSTRAINT `fk_point_redeem_code_package` FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`),
  CONSTRAINT `fk_point_redeem_code_order` FOREIGN KEY (`order_id`) REFERENCES `point_recharge_order` (`id`),
  CONSTRAINT `fk_point_redeem_code_user` FOREIGN KEY (`redeemed_by_user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_point_redeem_code_creator` FOREIGN KEY (`created_by`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Single-use point redeem codes';

INSERT INTO `point_recharge_package`
(`package_code`, `name`, `description`, `points`, `repeat_points`, `price_amount`, `currency`, `validity_days`, `bonus_percent`, `badge_code`, `benefits_json`, `purchase_url`, `is_featured`, `sort`, `status`)
VALUES
('monthly', '特惠月卡', '立得 5000 积分', 5000, NULL, 29.90, 'CNY', 30, 0, 'recommended', '["立即到账 5000 点可用积分","赠 30 天专属尊贵会员标识","生图折合单价低至 ¥0.012","享受生成作品无水印导出"]', NULL, 1, 1, 1),
('trial', '首充体验包', '首充尝鲜，超低价体验生图', 200, 100, 1.00, 'CNY', NULL, 0, 'first_offer', '["首充到账 200 点永久可用积分","续充为 100 点永久可用积分","生图折合单价低至 ¥0.02","享受生成作品无水印导出"]', NULL, 0, 2, 1),
('basic', '基础套餐', '适合日常轻度创作用户', 1000, NULL, 10.00, 'CNY', NULL, 0, 'regular_choice', '["到账 1000 点永久可用积分","卡密永久有效无过期限制","生图折合单价低至 ¥0.02","享受生成作品无水印导出"]', NULL, 0, 3, 1),
('value', '超值套餐', '额外赠送 20%，高性价比', 3600, NULL, 30.00, 'CNY', NULL, 20, 'popular', '["到账 3600 点永久可用积分","包含额外加赠 20% 积分","生图折合单价低至 ¥0.016","享受生成作品无水印导出"]', NULL, 0, 4, 1)
ON DUPLICATE KEY UPDATE
  `name` = VALUES(`name`),
  `description` = VALUES(`description`),
  `points` = VALUES(`points`),
  `repeat_points` = VALUES(`repeat_points`),
  `price_amount` = VALUES(`price_amount`),
  `currency` = VALUES(`currency`),
  `validity_days` = VALUES(`validity_days`),
  `bonus_percent` = VALUES(`bonus_percent`),
  `badge_code` = VALUES(`badge_code`),
  `benefits_json` = VALUES(`benefits_json`),
  `is_featured` = VALUES(`is_featured`),
  `sort` = VALUES(`sort`),
  `status` = VALUES(`status`),
  `updated_at` = CURRENT_TIMESTAMP;
