-- AI 生图积分定价与 VIP 权益。先执行本迁移，再发布 API。
-- 在目标库运行；每个 ALTER TABLE 独立原子提交，数据初始化与系统审计共同提交。
SET NAMES utf8mb4;

SET @pricing_ddl = IF(
  EXISTS (SELECT 1 FROM information_schema.columns
          WHERE table_schema = DATABASE() AND table_name = 'ai_image_point_price' AND column_name = 'vip_points'),
  'DO 0',
  'ALTER TABLE `ai_image_point_price`
    ADD COLUMN `vip_points` INT DEFAULT NULL COMMENT ''VIP单图积分；无优惠时为空'',
    ADD COLUMN `vip_price_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT ''follow'' COMMENT ''VIP价模式：follow或manual'',
    ADD COLUMN `revision` INT NOT NULL DEFAULT 0 COMMENT ''价格档乐观并发版本'',
    ADD COLUMN `updated_by` BIGINT DEFAULT NULL COMMENT ''最近修改人；0为系统迁移'',
    ADD CONSTRAINT `chk_ai_image_point_price_vip_points` CHECK (`vip_points` IS NULL OR (`vip_points` > 0 AND `vip_points` < `points`)),
    ADD CONSTRAINT `chk_ai_image_point_price_vip_mode` CHECK (`vip_price_mode` IN (''follow'', ''manual''))'
);
PREPARE pricing_stmt FROM @pricing_ddl;
EXECUTE pricing_stmt;
DEALLOCATE PREPARE pricing_stmt;

SET @pricing_ddl = IF(
  EXISTS (SELECT 1 FROM information_schema.columns
          WHERE table_schema = DATABASE() AND table_name = 'ai_image_task' AND column_name = 'pricing_tier'),
  'DO 0',
  'ALTER TABLE `ai_image_task`
    ADD COLUMN `pricing_tier` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL COMMENT ''计价档：standard或vip'',
    ADD COLUMN `list_unit_point_cost` INT DEFAULT NULL COMMENT ''标准单图积分快照'',
    ADD COLUMN `access_basis` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL COMMENT ''专属模型准入依据：vip或super_admin'''
);
PREPARE pricing_stmt FROM @pricing_ddl;
EXECUTE pricing_stmt;
DEALLOCATE PREPARE pricing_stmt;

CREATE TABLE IF NOT EXISTS `ai_image_pricing_setting` (
  `id` TINYINT NOT NULL,
  `default_vip_discount_percent` TINYINT NOT NULL DEFAULT 80,
  `revision` INT NOT NULL DEFAULT 0 COMMENT '默认折扣乐观并发版本',
  `pricing_revision` BIGINT NOT NULL DEFAULT 1 COMMENT '全局定价配置版本',
  `updated_by` BIGINT DEFAULT NULL,
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  PRIMARY KEY (`id`),
  CONSTRAINT `chk_ai_image_pricing_setting_id` CHECK (`id` = 1),
  CONSTRAINT `chk_ai_image_pricing_setting_discount` CHECK (`default_vip_discount_percent` BETWEEN 1 AND 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图默认VIP折扣与全局版本';

CREATE TABLE IF NOT EXISTS `ai_image_model_policy` (
  `model_code` VARCHAR(100) NOT NULL,
  `access_level` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'all',
  `required_tier_code` VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'monthly_vip',
  `vip_discount_percent` TINYINT DEFAULT NULL,
  `revision` INT NOT NULL DEFAULT 0,
  `updated_by` BIGINT DEFAULT NULL,
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  PRIMARY KEY (`model_code`),
  CONSTRAINT `chk_ai_image_model_policy_access` CHECK (`access_level` IN ('all', 'vip_only')),
  CONSTRAINT `chk_ai_image_model_policy_discount` CHECK (`vip_discount_percent` IS NULL OR `vip_discount_percent` BETWEEN 1 AND 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图模型访问策略与VIP折扣';

CREATE TABLE IF NOT EXISTS `ai_image_pricing_audit` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `batch_id` CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `target_type` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `target_id` VARCHAR(100) NOT NULL,
  `model_code` VARCHAR(100) DEFAULT NULL,
  `resolution_code` VARCHAR(50) DEFAULT NULL,
  `quality_code` VARCHAR(50) DEFAULT NULL,
  `action` VARCHAR(24) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `before_json` TEXT DEFAULT NULL,
  `after_json` TEXT DEFAULT NULL,
  `reason` VARCHAR(200) NOT NULL,
  `operator_user_id` BIGINT NOT NULL,
  `operator_ip` VARCHAR(50) DEFAULT NULL,
  `created_at` DATETIME NOT NULL COMMENT 'UTC',
  PRIMARY KEY (`id`),
  KEY `idx_ai_image_pricing_audit_model` (`model_code`, `id`),
  KEY `idx_ai_image_pricing_audit_operator` (`operator_user_id`, `id`),
  KEY `idx_ai_image_pricing_audit_created` (`created_at`, `id`),
  KEY `idx_ai_image_pricing_audit_batch` (`batch_id`, `id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图定价与访问策略审计';

-- 设置行与首次回填同事务提交。重跑只检查已有设置行，保留后续运营调价及其审计。
START TRANSACTION;

INSERT IGNORE INTO `ai_image_pricing_setting`
  (`id`, `default_vip_discount_percent`, `revision`, `pricing_revision`, `updated_by`, `updated_at`)
VALUES (1, 80, 0, 1, 0, UTC_TIMESTAMP());
SET @initialize_vip_prices = ROW_COUNT();
SET @pricing_init_batch = REPLACE(UUID(), '-', '');

UPDATE `ai_image_point_price`
SET `vip_points` = NULLIF(CEIL(`points` * 0.80), `points`),
    `vip_price_mode` = 'follow',
    `updated_by` = 0
WHERE @initialize_vip_prices = 1 AND `is_deleted` = 0;

INSERT INTO `ai_image_pricing_audit`
  (`batch_id`, `target_type`, `target_id`, `model_code`, `resolution_code`, `quality_code`,
   `action`, `before_json`, `after_json`, `reason`, `operator_user_id`, `created_at`)
SELECT @pricing_init_batch, 'price', CAST(`id` AS CHAR), `model_code`, `resolution_code`, `quality_code`,
       'system_init', NULL, JSON_OBJECT('points', `points`, 'vipPoints', `vip_points`, 'vipPriceMode', `vip_price_mode`),
       '上线初始化 VIP 默认 8 折', 0, UTC_TIMESTAMP()
FROM `ai_image_point_price`
WHERE @initialize_vip_prices = 1 AND `is_deleted` = 0;

COMMIT;
