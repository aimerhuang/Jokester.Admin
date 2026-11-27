-- 站内公告及 QQ 群弹窗。重复执行会保留运营配置。
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `site_announcement` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `title` VARCHAR(80) NOT NULL,
  `summary` VARCHAR(200) NOT NULL,
  `content` TEXT DEFAULT NULL COMMENT '纯文本',
  `cover_url` VARCHAR(255) DEFAULT NULL,
  `category` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'notice',
  `version_label` VARCHAR(20) DEFAULT NULL,
  `is_pinned` TINYINT(1) NOT NULL DEFAULT 0,
  `is_important` TINYINT(1) NOT NULL DEFAULT 0,
  `link_url` VARCHAR(500) DEFAULT NULL,
  `link_text` VARCHAR(20) DEFAULT NULL,
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '0草稿 1已发布 2已下线',
  `published_at` DATETIME(6) DEFAULT NULL COMMENT 'UTC，首次发布时间',
  `revision` INT NOT NULL DEFAULT 0,
  `created_by` BIGINT NOT NULL,
  `updated_by` BIGINT DEFAULT NULL,
  `created_at` DATETIME NOT NULL COMMENT 'UTC',
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_site_announcement_feed` (`is_deleted`, `status`, `is_pinned`, `published_at`, `id`),
  CONSTRAINT `chk_site_announcement_category` CHECK (`category` IN ('feature', 'activity', 'maintenance', 'notice')),
  CONSTRAINT `chk_site_announcement_status` CHECK (`status` IN (0, 1, 2))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='站内公告';

CREATE TABLE IF NOT EXISTS `site_community_setting` (
  `id` TINYINT NOT NULL,
  `qq_popup_enabled` TINYINT(1) NOT NULL DEFAULT 0,
  `qq_group_name` VARCHAR(50) NOT NULL DEFAULT '',
  `qq_group_number` VARCHAR(12) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT '',
  `qq_join_url` VARCHAR(500) DEFAULT NULL,
  `popup_title` VARCHAR(40) NOT NULL,
  `popup_description` VARCHAR(200) NOT NULL,
  `remind_interval_days` TINYINT NOT NULL DEFAULT 3,
  `prompt_version` INT NOT NULL DEFAULT 1,
  `revision` INT NOT NULL DEFAULT 0,
  `updated_by` BIGINT DEFAULT NULL,
  `updated_at` DATETIME DEFAULT NULL COMMENT 'UTC',
  PRIMARY KEY (`id`),
  CONSTRAINT `chk_site_community_setting_id` CHECK (`id` = 1),
  CONSTRAINT `chk_site_community_setting_interval` CHECK (`remind_interval_days` BETWEEN 1 AND 30)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='社群引流配置（单行）';

INSERT INTO `site_community_setting` (`id`, `popup_title`, `popup_description`)
VALUES (1, '加入 QQ 交流群', '加入官方 QQ 交流群，与更多 AI 绘画爱好者交流技巧、分享作品，并获取最新功能更新和活动资讯。')
ON DUPLICATE KEY UPDATE `id` = `id`;
