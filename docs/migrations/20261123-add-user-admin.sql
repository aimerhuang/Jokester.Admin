CREATE TABLE IF NOT EXISTS `sys_user_status_audit` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT NOT NULL,
  `action` VARCHAR(16) NOT NULL,
  `reason` VARCHAR(200) DEFAULT NULL,
  `operator_user_id` BIGINT NOT NULL,
  `operator_ip` VARCHAR(50) DEFAULT NULL,
  `created_at` DATETIME NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_sys_user_status_audit_user` (`user_id`, `id`),
  CONSTRAINT `fk_sys_user_status_audit_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @add_index = IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema = DATABASE()
  AND table_name = 'sys_user' AND index_name = 'idx_sys_user_created_at'), 'SELECT 1',
  'ALTER TABLE sys_user ADD INDEX idx_sys_user_created_at (created_at)');
PREPARE stmt FROM @add_index;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
