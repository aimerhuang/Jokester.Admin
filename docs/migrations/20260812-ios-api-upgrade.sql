USE `jokester.admin`;

-- API-04: versioned legal documents. Operations must insert the approved
-- privacy, terms, and AI-processing rows before enabling registration.
CREATE TABLE IF NOT EXISTS `legal_document` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `document_type` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `version` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `platform` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `locale` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `url` VARCHAR(500) NOT NULL,
  `provider_codes_json` JSON DEFAULT NULL,
  `effective_at` DATETIME NOT NULL,
  `requires_reconsent` TINYINT(1) NOT NULL DEFAULT 0,
  `status` TINYINT NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_legal_document_scope_version`
    (`document_type`, `platform`, `locale`, `version`),
  KEY `idx_legal_document_current`
    (`document_type`, `platform`, `locale`, `status`, `effective_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Versioned legal documents';

-- API-04/API-05: append-only consent history. A revocation is represented by
-- a later row with accepted=0 and revoked_at set.
CREATE TABLE IF NOT EXISTS `user_consent` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `consent_type` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `document_version` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `provider_codes_json` JSON DEFAULT NULL,
  `accepted` TINYINT(1) NOT NULL,
  `client_platform` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `accepted_at` DATETIME DEFAULT NULL,
  `revoked_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_user_consent_current` (`user_id`, `consent_type`, `created_at`, `id`),
  CONSTRAINT `fk_user_consent_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='User legal and AI processing consent history';

-- API-03: auditable, retryable account-deletion workflow.
CREATE TABLE IF NOT EXISTS `account_deletion_request` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `request_id` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `user_id` BIGINT NOT NULL,
  `client_request_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `request_fingerprint` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `status` VARCHAR(30) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `reason` VARCHAR(500) DEFAULT NULL,
  `notification_email` VARCHAR(254) DEFAULT NULL,
  `requested_at` DATETIME NOT NULL,
  `scheduled_deletion_at` DATETIME NOT NULL,
  `cancelled_at` DATETIME DEFAULT NULL,
  `data_deleted_at` DATETIME DEFAULT NULL,
  `completed_at` DATETIME DEFAULT NULL,
  `next_retry_at` DATETIME DEFAULT NULL,
  `retry_count` INT NOT NULL DEFAULT 0,
  `failure_message` VARCHAR(100) DEFAULT NULL,
  `notification_sent_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_account_deletion_request_id` (`request_id`),
  UNIQUE KEY `uk_account_deletion_user_client` (`user_id`, `client_request_hash`),
  KEY `idx_account_deletion_due` (`status`, `scheduled_deletion_at`, `next_retry_at`),
  KEY `idx_account_deletion_user_created` (`user_id`, `created_at`),
  CONSTRAINT `fk_account_deletion_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Retryable account deletion requests';

-- API-07/API-09: private server-owned image assets.
CREATE TABLE IF NOT EXISTS `media_asset` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `asset_id` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `owner_user_id` BIGINT NOT NULL,
  `asset_type` VARCHAR(30) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `storage_key` VARCHAR(500) NOT NULL,
  `thumbnail_key` VARCHAR(500) DEFAULT NULL,
  `mime_type` VARCHAR(100) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `width` INT NOT NULL,
  `height` INT NOT NULL,
  `size_bytes` BIGINT NOT NULL,
  `sha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `metadata_stripped` TINYINT(1) NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `deleted_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_media_asset_id` (`asset_id`),
  UNIQUE KEY `uk_media_asset_storage_key` (`storage_key`),
  KEY `idx_media_asset_owner_created` (`owner_user_id`, `is_deleted`, `created_at`),
  KEY `idx_media_asset_sha256` (`sha256`),
  CONSTRAINT `fk_media_asset_owner`
    FOREIGN KEY (`owner_user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Private user-owned media assets';

-- API-01: maps an existing point package to a real StoreKit product.
-- No product rows are seeded because Product IDs are deployment-specific.
CREATE TABLE IF NOT EXISTS `apple_iap_product` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `package_id` BIGINT NOT NULL,
  `package_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `apple_product_id` VARCHAR(200) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `product_type` VARCHAR(30) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'consumable',
  `points` INT NOT NULL,
  `environment` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'Production',
  `status` TINYINT NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_apple_iap_product_id` (`apple_product_id`),
  UNIQUE KEY `uk_apple_iap_product_package` (`package_id`),
  KEY `idx_apple_iap_product_enabled` (`status`, `is_deleted`, `package_id`),
  CONSTRAINT `fk_apple_iap_product_package`
    FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`),
  CONSTRAINT `chk_apple_iap_product_points` CHECK (`points` > 0),
  CONSTRAINT `chk_apple_iap_product_type` CHECK (`product_type` = 'consumable'),
  CONSTRAINT `chk_apple_iap_product_environment`
    CHECK (`environment` IN ('Production', 'Sandbox', 'Both'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='StoreKit product to point-package mappings';

-- API-01: verified StoreKit fulfillment ledger. Raw JWS is never stored.
CREATE TABLE IF NOT EXISTS `apple_transaction` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `transaction_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `idempotency_key_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `request_fingerprint` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `original_transaction_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `user_id` BIGINT NOT NULL,
  `product_id` VARCHAR(200) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `package_id` BIGINT NOT NULL,
  `order_no` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `environment` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `app_account_token` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `points` INT NOT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `signed_transaction_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `purchase_date` DATETIME NOT NULL,
  `revocation_date` DATETIME DEFAULT NULL,
  `fulfilled_at` DATETIME DEFAULT NULL,
  `refunded_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_apple_transaction_id` (`transaction_id`),
  UNIQUE KEY `uk_apple_transaction_order_no` (`order_no`),
  UNIQUE KEY `uk_apple_transaction_user_idempotency` (`user_id`, `idempotency_key_hash`),
  KEY `idx_apple_transaction_user_created` (`user_id`, `created_at`),
  KEY `idx_apple_transaction_product` (`product_id`, `status`),
  CONSTRAINT `fk_apple_transaction_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_apple_transaction_package`
    FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Verified Apple transaction fulfillment ledger';

-- API-02: safely accepted App Store Server Notifications V2.
CREATE TABLE IF NOT EXISTS `apple_server_notification` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `notification_uuid` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `notification_type` VARCHAR(80) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `subtype` VARCHAR(80) CHARACTER SET ascii COLLATE ascii_general_ci DEFAULT NULL,
  `environment` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `transaction_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `signed_payload_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `retry_count` INT NOT NULL DEFAULT 0,
  `failure_message` VARCHAR(100) DEFAULT NULL,
  `received_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `processed_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_apple_server_notification_uuid` (`notification_uuid`),
  KEY `idx_apple_server_notification_pending` (`status`, `retry_count`, `received_at`),
  KEY `idx_apple_server_notification_transaction` (`transaction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='App Store Server Notifications V2 processing ledger';

-- API-02: refund shortfalls. Open debt blocks additional image generation.
CREATE TABLE IF NOT EXISTS `apple_iap_debt` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `transaction_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `points_owed` INT NOT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'open',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_apple_iap_debt_transaction` (`transaction_id`),
  KEY `idx_apple_iap_debt_user_status` (`user_id`, `status`),
  CONSTRAINT `fk_apple_iap_debt_user`
    FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_apple_iap_debt_transaction`
    FOREIGN KEY (`transaction_id`) REFERENCES `apple_transaction` (`transaction_id`),
  CONSTRAINT `chk_apple_iap_debt_points` CHECK (`points_owed` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Outstanding point debt caused by Apple refunds';
