-- Database-backed Chinese/English prompt filtering for AI image generation.
-- Back up the database before applying this migration.

CREATE TABLE `ai_prompt_sensitive_word` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `term` VARCHAR(255) NOT NULL,
  `normalized_term` VARCHAR(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `term_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `language_code` VARCHAR(10) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `category_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `match_mode` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `action` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'block',
  `severity` TINYINT NOT NULL DEFAULT 1,
  `status` TINYINT NOT NULL DEFAULT 1,
  `source_code` VARCHAR(100) DEFAULT NULL,
  `source_version` VARCHAR(100) DEFAULT NULL,
  `remark` VARCHAR(500) DEFAULT NULL,
  `created_by` BIGINT DEFAULT NULL,
  `updated_by` BIGINT DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_prompt_sensitive_word_term_key` (`term_key`),
  KEY `idx_ai_prompt_sensitive_word_lookup` (`status`, `is_deleted`, `language_code`, `category_code`),
  CONSTRAINT `chk_ai_prompt_sensitive_word_match_mode` CHECK (`match_mode` IN ('contains', 'word', 'compact')),
  CONSTRAINT `chk_ai_prompt_sensitive_word_action` CHECK (`action` IN ('block', 'audit'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI prompt sensitive word rules';

CREATE TABLE `ai_prompt_sensitive_word_revision` (
  `id` TINYINT NOT NULL,
  `revision` BIGINT NOT NULL,
  `updated_by` BIGINT DEFAULT NULL,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI prompt filter revision';

INSERT INTO `ai_prompt_sensitive_word_revision` (`id`, `revision`, `updated_at`)
VALUES (1, 1, CURRENT_TIMESTAMP);

ALTER TABLE `ai_image_task`
  ADD COLUMN `prompt_policy_version` BIGINT NOT NULL DEFAULT 0
    COMMENT 'Prompt filter revision used for the latest successful check'
    AFTER `negative_prompt`,
  ADD COLUMN `prompt_checked_at` DATETIME DEFAULT NULL
    COMMENT 'Time of the latest successful prompt filter check'
    AFTER `prompt_policy_version`;

CREATE TEMPORARY TABLE `tmp_ai_prompt_sensitive_word_seed` (
  `term` VARCHAR(255) NOT NULL,
  `normalized_term` VARCHAR(512) NOT NULL,
  `language_code` VARCHAR(10) NOT NULL,
  `category_code` VARCHAR(50) NOT NULL,
  `match_mode` VARCHAR(20) NOT NULL,
  `severity` TINYINT NOT NULL
);

INSERT INTO `tmp_ai_prompt_sensitive_word_seed`
  (`term`, `normalized_term`, `language_code`, `category_code`, `match_mode`, `severity`)
VALUES
  ('儿童色情', '儿童色情', 'zh', 'sexual_minors', 'compact', 5),
  ('未成年裸照', '未成年裸照', 'zh', 'sexual_minors', 'compact', 5),
  ('诱骗未成年人', '诱骗未成年人', 'zh', 'sexual_minors', 'compact', 5),
  ('强奸', '强奸', 'zh', 'sexual_violence', 'compact', 5),
  ('乱伦', '乱伦', 'zh', 'sexual_content', 'compact', 4),
  ('兽交', '兽交', 'zh', 'sexual_content', 'compact', 5),
  ('色情', '色情', 'zh', 'sexual_content', 'compact', 3),
  ('伪造裸照', '伪造裸照', 'zh', 'non_consensual', 'compact', 5),
  ('血腥肢解', '血腥肢解', 'zh', 'graphic_violence', 'compact', 4),
  ('斩首', '斩首', 'zh', 'graphic_violence', 'compact', 4),
  ('碎尸', '碎尸', 'zh', 'graphic_violence', 'compact', 4),
  ('虐杀', '虐杀', 'zh', 'graphic_violence', 'compact', 4),
  ('制毒教程', '制毒教程', 'zh', 'illegal_instructions', 'compact', 5),
  ('炸弹制作', '炸弹制作', 'zh', 'illegal_instructions', 'compact', 5),
  ('爆炸物教程', '爆炸物教程', 'zh', 'illegal_instructions', 'compact', 5),
  ('伪造证件', '伪造证件', 'zh', 'illegal_instructions', 'compact', 4),
  ('恐怖主义宣传', '恐怖主义宣传', 'zh', 'extremism', 'compact', 5),
  ('极端主义宣传', '极端主义宣传', 'zh', 'extremism', 'compact', 5),
  ('自杀教程', '自杀教程', 'zh', 'self_harm', 'compact', 5),
  ('child pornography', 'childpornography', 'en', 'sexual_minors', 'word', 5),
  ('nude minor', 'nudeminor', 'en', 'sexual_minors', 'word', 5),
  ('sexualized child', 'sexualizedchild', 'en', 'sexual_minors', 'word', 5),
  ('sexual assault', 'sexualassault', 'en', 'sexual_violence', 'word', 5),
  ('rape', 'rape', 'en', 'sexual_violence', 'word', 5),
  ('incest', 'incest', 'en', 'sexual_content', 'word', 4),
  ('bestiality', 'bestiality', 'en', 'sexual_content', 'word', 5),
  ('pornography', 'pornography', 'en', 'sexual_content', 'word', 3),
  ('deepfake nude', 'deepfakenude', 'en', 'non_consensual', 'word', 5),
  ('graphic dismemberment', 'graphicdismemberment', 'en', 'graphic_violence', 'word', 4),
  ('beheading', 'beheading', 'en', 'graphic_violence', 'word', 4),
  ('torture killing', 'torturekilling', 'en', 'graphic_violence', 'word', 4),
  ('drug manufacturing instructions', 'drugmanufacturinginstructions', 'en', 'illegal_instructions', 'word', 5),
  ('bomb making instructions', 'bombmakinginstructions', 'en', 'illegal_instructions', 'compact', 5),
  ('counterfeit documents', 'counterfeitdocuments', 'en', 'illegal_instructions', 'word', 4),
  ('terrorist propaganda', 'terroristpropaganda', 'en', 'extremism', 'word', 5),
  ('extremist propaganda', 'extremistpropaganda', 'en', 'extremism', 'word', 5),
  ('suicide instructions', 'suicideinstructions', 'en', 'self_harm', 'word', 5);

INSERT INTO `ai_prompt_sensitive_word`
  (`term`, `normalized_term`, `term_key`, `language_code`, `category_code`, `match_mode`,
   `action`, `severity`, `status`, `source_code`, `source_version`, `created_at`, `is_deleted`)
SELECT
  `term`, `normalized_term`, SHA2(CONCAT(`match_mode`, ':', `normalized_term`), 256),
  `language_code`, `category_code`, `match_mode`, 'block', `severity`, 1,
  'builtin', '2026-08-11', CURRENT_TIMESTAMP, 0
FROM `tmp_ai_prompt_sensitive_word_seed`;

DROP TEMPORARY TABLE `tmp_ai_prompt_sensitive_word_seed`;

SET @ai_prompt_word_parent_id = (
  SELECT `id` FROM `sys_menu` WHERE `menu_code` = 'ai_image_generate_page' AND `is_deleted` = 0 LIMIT 1
);

INSERT INTO `sys_menu`
  (`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`,
   `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT `site_id`, `id`, '查看敏感词', 'ai_prompt_sensitive_word_view', 3, NULL, NULL,
  'AiImage.SensitiveWord.View', NULL, 30, 0, 1, 0, 0, 'View AI prompt sensitive words'
FROM `sys_menu`
WHERE `id` = @ai_prompt_word_parent_id
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'ai_prompt_sensitive_word_view');

INSERT INTO `sys_menu`
  (`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`,
   `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT `site_id`, `id`, '管理敏感词', 'ai_prompt_sensitive_word_manage', 3, NULL, NULL,
  'AiImage.SensitiveWord.Manage', NULL, 31, 0, 1, 0, 0, 'Manage AI prompt sensitive words'
FROM `sys_menu`
WHERE `id` = @ai_prompt_word_parent_id
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'ai_prompt_sensitive_word_manage');

INSERT INTO `sys_menu`
  (`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`,
   `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
SELECT `site_id`, `id`, '测试敏感词', 'ai_prompt_sensitive_word_test', 3, NULL, NULL,
  'AiImage.SensitiveWord.Test', NULL, 32, 0, 1, 0, 0, 'Test AI prompt sensitive word matching'
FROM `sys_menu`
WHERE `id` = @ai_prompt_word_parent_id
  AND NOT EXISTS (SELECT 1 FROM `sys_menu` WHERE `menu_code` = 'ai_prompt_sensitive_word_test');

INSERT INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m ON m.`menu_code` IN (
  'ai_prompt_sensitive_word_view',
  'ai_prompt_sensitive_word_manage',
  'ai_prompt_sensitive_word_test')
WHERE r.`role_code` = 'super_admin'
  AND NOT EXISTS (
    SELECT 1 FROM `sys_role_menu` rm WHERE rm.`role_id` = r.`id` AND rm.`menu_id` = m.`id`
  );

INSERT INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m ON m.`menu_code` IN (
  'ai_prompt_sensitive_word_view',
  'ai_prompt_sensitive_word_test')
WHERE r.`role_code` = 'ai_operator'
  AND NOT EXISTS (
    SELECT 1 FROM `sys_role_menu` rm WHERE rm.`role_id` = r.`id` AND rm.`menu_id` = m.`id`
  );
