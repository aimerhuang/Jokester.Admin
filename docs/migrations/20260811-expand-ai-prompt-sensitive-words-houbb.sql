-- Curated AI prompt lexicon expansion.
-- Requires 20260811-add-ai-prompt-sensitive-words.sql.
-- Back up the database before applying this migration.
--
-- houbb/sensitive-word-data is a candidate source only. Its native tags are:
-- 0=politics, 1=drugs, 2=sexual content, 3=gambling, 4=illegal activity.
-- The seven category codes below are project-owned manual classifications.

DROP TEMPORARY TABLE IF EXISTS `tmp_ai_prompt_houbb_candidate`;
DROP TEMPORARY TABLE IF EXISTS `tmp_ai_prompt_project_rule`;
DROP TEMPORARY TABLE IF EXISTS `tmp_ai_prompt_category_remap`;

CREATE TEMPORARY TABLE `tmp_ai_prompt_houbb_candidate` (
  `term` VARCHAR(255) NOT NULL,
  `normalized_term` VARCHAR(512) NOT NULL,
  `language_code` VARCHAR(10) NOT NULL,
  `category_code` VARCHAR(50) NOT NULL,
  `match_mode` VARCHAR(20) NOT NULL,
  `severity` TINYINT NOT NULL,
  `source_tags` VARCHAR(20) NOT NULL
);

INSERT INTO `tmp_ai_prompt_houbb_candidate`
  (`term`, `normalized_term`, `language_code`, `category_code`, `match_mode`, `severity`, `source_tags`)
VALUES
  ('幼交色情电影', '幼交色情电影', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('幼女性交', '幼女性交', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('幼女性爱视频', '幼女性爱视频', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('幼女口交', '幼女口交', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('幼女群交', '幼女群交', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('幼女淫图', '幼女淫图', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('亚洲幼女援交', '亚洲幼女援交', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('奸淫未成年的小女护士', '奸淫未成年的小女护士', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('被老师强奸的小初中生', '被老师强奸的小初中生', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('强奸17岁少女全过程', '强奸17岁少女全过程', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('日本无码o幼齿女学生', '日本无码o幼齿女学生', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('6岁幼女阴穴近拍', '6岁幼女阴穴近拍', 'zh', 'sexual_minors', 'compact', 5, '2'),
  ('强奸', '强奸', 'zh', 'non_consensual_nudity', 'compact', 5, '2'),
  ('奸淫', '奸淫', 'zh', 'non_consensual_nudity', 'compact', 5, '2'),
  ('迷奸', '迷奸', 'zh', 'non_consensual_nudity', 'compact', 5, '2'),
  ('轮奸', '轮奸', 'zh', 'non_consensual_nudity', 'compact', 5, '2'),
  ('迷奸药', '迷奸药', 'zh', 'non_consensual_nudity', 'compact', 5, '1,2'),
  ('裸照', '裸照', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('艳照', '艳照', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('真人裸聊', '真人裸聊', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('裸聊', '裸聊', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('视频裸聊', '视频裸聊', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('1对1真人视频裸聊', '1对1真人视频裸聊', 'zh', 'non_consensual_nudity', 'compact', 4, '2'),
  ('偷拍视频下载', '偷拍视频下载', 'zh', 'non_consensual_nudity', 'compact', 5, '2'),
  ('买凶杀人', '买凶杀人', 'zh', 'graphic_violence', 'compact', 5, '4'),
  ('杀人卖尸', '杀人卖尸', 'zh', 'graphic_violence', 'compact', 5, '4'),
  ('枪决现场', '枪决现场', 'zh', 'graphic_violence', 'compact', 4, '4'),
  ('枪毙', '枪毙', 'zh', 'graphic_violence', 'compact', 4, '4'),
  ('死刑枪毙', '死刑枪毙', 'zh', 'graphic_violence', 'compact', 4, '4'),
  ('枪毙后尸体的处理', '枪毙后尸体的处理', 'zh', 'graphic_violence', 'compact', 5, '4'),
  ('用刀横向切腹', '用刀横向切腹', 'zh', 'self_harm', 'compact', 5, '4'),
  ('恐怖主义', '恐怖主义', 'zh', 'hate_extremism', 'compact', 4, '0'),
  ('冰毒配方', '冰毒配方', 'zh', 'weapons_drugs', 'compact', 5, '1'),
  ('制作冰毒', '制作冰毒', 'zh', 'weapons_drugs', 'compact', 5, '1'),
  ('出售冰毒', '出售冰毒', 'zh', 'weapons_drugs', 'compact', 5, '1,4'),
  ('出售海洛因', '出售海洛因', 'zh', 'weapons_drugs', 'compact', 5, '1,4'),
  ('购买海洛因', '购买海洛因', 'zh', 'weapons_drugs', 'compact', 5, '1,4'),
  ('出售可卡因', '出售可卡因', 'zh', 'weapons_drugs', 'compact', 5, '1,4'),
  ('购买可卡因', '购买可卡因', 'zh', 'weapons_drugs', 'compact', 5, '1,4'),
  ('枪支买卖', '枪支买卖', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('买卖枪支', '买卖枪支', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('枪支出售', '枪支出售', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('购买枪支', '购买枪支', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('出售枪支', '出售枪支', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('3d打印枪支', '3d打印枪支', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('枪械图纸', '枪械图纸', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('自制手枪', '自制手枪', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('炸弹制作', '炸弹制作', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('自制炸弹', '自制炸弹', 'zh', 'weapons_drugs', 'compact', 5, '4'),
  ('炸药制作', '炸药制作', 'zh', 'weapons_drugs', 'compact', 5, '1'),
  ('假身份证', '假身份证', 'zh', 'deepfake_privacy', 'compact', 4, '4'),
  ('高仿身份证', '高仿身份证', 'zh', 'deepfake_privacy', 'compact', 4, '4'),
  ('精仿身份证', '精仿身份证', 'zh', 'deepfake_privacy', 'compact', 4, '4'),
  ('身份证生成器', '身份证生成器', 'zh', 'deepfake_privacy', 'compact', 4, '4'),
  ('出售身份证', '出售身份证', 'zh', 'deepfake_privacy', 'compact', 4, '4'),
  ('出售个人信息', '出售个人信息', 'zh', 'deepfake_privacy', 'compact', 5, '4'),
  ('出售个人信息数据', '出售个人信息数据', 'zh', 'deepfake_privacy', 'compact', 5, '4'),
  ('出售个人信息资料', '出售个人信息资料', 'zh', 'deepfake_privacy', 'compact', 5, '4'),
  ('出售股民个人信息', '出售股民个人信息', 'zh', 'deepfake_privacy', 'compact', 5, '4'),
  ('出售车主个人信息', '出售车主个人信息', 'zh', 'deepfake_privacy', 'compact', 5, '4'),
  ('人肉搜索', '人肉搜索', 'zh', 'deepfake_privacy', 'compact', 4, '2'),
  ('手机卧底定位软件下载', '手机卧底定位软件下载', 'zh', 'deepfake_privacy', 'compact', 5, '2');

CREATE TEMPORARY TABLE `tmp_ai_prompt_project_rule` (
  `term` VARCHAR(255) NOT NULL,
  `normalized_term` VARCHAR(512) NOT NULL,
  `language_code` VARCHAR(10) NOT NULL,
  `category_code` VARCHAR(50) NOT NULL,
  `match_mode` VARCHAR(20) NOT NULL,
  `severity` TINYINT NOT NULL
);

INSERT INTO `tmp_ai_prompt_project_rule`
  (`term`, `normalized_term`, `language_code`, `category_code`, `match_mode`, `severity`)
VALUES
  ('未成年人色情', '未成年人色情', 'zh', 'sexual_minors', 'compact', 5),
  ('未成年人性行为', '未成年人性行为', 'zh', 'sexual_minors', 'compact', 5),
  ('儿童性虐待', '儿童性虐待', 'zh', 'sexual_minors', 'compact', 5),
  ('儿童裸照', '儿童裸照', 'zh', 'sexual_minors', 'compact', 5),
  ('强奸未成年人', '强奸未成年人', 'zh', 'sexual_minors', 'compact', 5),
  ('child sexual abuse material', 'childsexualabusematerial', 'en', 'sexual_minors', 'word', 5),
  ('强暴', '强暴', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('性侵', '性侵', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('性侵犯', '性侵犯', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('非自愿裸照', '非自愿裸照', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('真人裸照', '真人裸照', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('私密照泄露', '私密照泄露', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('报复性色情', '报复性色情', 'zh', 'non_consensual_nudity', 'compact', 5),
  ('revenge porn', 'revengeporn', 'en', 'non_consensual_nudity', 'word', 5),
  ('non-consensual nude', 'nonconsensualnude', 'en', 'non_consensual_nudity', 'word', 5),
  ('分尸', '分尸', 'zh', 'graphic_violence', 'compact', 5),
  ('割喉特写', '割喉特写', 'zh', 'graphic_violence', 'compact', 5),
  ('开膛破肚', '开膛破肚', 'zh', 'graphic_violence', 'compact', 5),
  ('爆头特写', '爆头特写', 'zh', 'graphic_violence', 'compact', 5),
  ('断肢残骸', '断肢残骸', 'zh', 'graphic_violence', 'compact', 5),
  ('内脏外露', '内脏外露', 'zh', 'graphic_violence', 'compact', 5),
  ('活体解剖', '活体解剖', 'zh', 'graphic_violence', 'compact', 5),
  ('剥皮处刑', '剥皮处刑', 'zh', 'graphic_violence', 'compact', 5),
  ('exposed organs', 'exposedorgans', 'en', 'graphic_violence', 'word', 5),
  ('severed limbs', 'severedlimbs', 'en', 'graphic_violence', 'word', 5),
  ('自残教程', '自残教程', 'zh', 'self_harm', 'compact', 5),
  ('自杀方法', '自杀方法', 'zh', 'self_harm', 'compact', 5),
  ('自杀步骤', '自杀步骤', 'zh', 'self_harm', 'compact', 5),
  ('割腕自杀', '割腕自杀', 'zh', 'self_harm', 'compact', 5),
  ('上吊自杀', '上吊自杀', 'zh', 'self_harm', 'compact', 5),
  ('跳楼自杀', '跳楼自杀', 'zh', 'self_harm', 'compact', 5),
  ('服毒自杀', '服毒自杀', 'zh', 'self_harm', 'compact', 5),
  ('烧炭自杀', '烧炭自杀', 'zh', 'self_harm', 'compact', 5),
  ('自焚自杀', '自焚自杀', 'zh', 'self_harm', 'compact', 5),
  ('自残割腕', '自残割腕', 'zh', 'self_harm', 'compact', 5),
  ('self-harm instructions', 'selfharminstructions', 'en', 'self_harm', 'word', 5),
  ('suicide method', 'suicidemethod', 'en', 'self_harm', 'word', 5),
  ('恐怖组织招募', '恐怖组织招募', 'zh', 'hate_extremism', 'compact', 5),
  ('极端组织招募', '极端组织招募', 'zh', 'hate_extremism', 'compact', 5),
  ('恐怖主义赞颂', '恐怖主义赞颂', 'zh', 'hate_extremism', 'compact', 5),
  ('纳粹主义宣传', '纳粹主义宣传', 'zh', 'hate_extremism', 'compact', 5),
  ('白人至上主义宣传', '白人至上主义宣传', 'zh', 'hate_extremism', 'compact', 5),
  ('种族清洗宣传', '种族清洗宣传', 'zh', 'hate_extremism', 'compact', 5),
  ('terrorist recruitment', 'terroristrecruitment', 'en', 'hate_extremism', 'word', 5),
  ('white supremacist propaganda', 'whitesupremacistpropaganda', 'en', 'hate_extremism', 'word', 5),
  ('nazi propaganda', 'nazipropaganda', 'en', 'hate_extremism', 'word', 5),
  ('毒品交易', '毒品交易', 'zh', 'weapons_drugs', 'compact', 5),
  ('武器交易', '武器交易', 'zh', 'weapons_drugs', 'compact', 5),
  ('枪械制作教程', '枪械制作教程', 'zh', 'weapons_drugs', 'compact', 5),
  ('爆炸物制作教程', '爆炸物制作教程', 'zh', 'weapons_drugs', 'compact', 5),
  ('drug trafficking', 'drugtrafficking', 'en', 'weapons_drugs', 'word', 5),
  ('weapons trafficking', 'weaponstrafficking', 'en', 'weapons_drugs', 'word', 5),
  ('深度伪造裸照', '深度伪造裸照', 'zh', 'deepfake_privacy', 'compact', 5),
  ('真人AI换脸', '真人ai换脸', 'zh', 'deepfake_privacy', 'compact', 5),
  ('非自愿换脸', '非自愿换脸', 'zh', 'deepfake_privacy', 'compact', 5),
  ('AI换脸裸照', 'ai换脸裸照', 'zh', 'deepfake_privacy', 'compact', 5),
  ('AI脱衣', 'ai脱衣', 'zh', 'deepfake_privacy', 'compact', 5),
  ('一键脱衣', '一键脱衣', 'zh', 'deepfake_privacy', 'compact', 5),
  ('人脸替换裸照', '人脸替换裸照', 'zh', 'deepfake_privacy', 'compact', 5),
  ('伪造私密照', '伪造私密照', 'zh', 'deepfake_privacy', 'compact', 5),
  ('偷拍视频', '偷拍视频', 'zh', 'deepfake_privacy', 'compact', 5),
  ('社工库', '社工库', 'zh', 'deepfake_privacy', 'compact', 5),
  ('开房记录', '开房记录', 'zh', 'deepfake_privacy', 'compact', 5),
  ('deepfake pornography', 'deepfakepornography', 'en', 'deepfake_privacy', 'word', 5),
  ('non-consensual face swap', 'nonconsensualfaceswap', 'en', 'deepfake_privacy', 'word', 5),
  ('ai undress', 'aiundress', 'en', 'deepfake_privacy', 'word', 5),
  ('forged intimate image', 'forgedintimateimage', 'en', 'deepfake_privacy', 'word', 5),
  ('doxxing', 'doxxing', 'en', 'deepfake_privacy', 'word', 5);

CREATE TEMPORARY TABLE `tmp_ai_prompt_category_remap` (
  `normalized_term` VARCHAR(512) NOT NULL,
  `match_mode` VARCHAR(20) NOT NULL,
  `category_code` VARCHAR(50) NOT NULL
);

INSERT INTO `tmp_ai_prompt_category_remap` (`normalized_term`, `match_mode`, `category_code`)
VALUES
  ('强奸', 'compact', 'non_consensual_nudity'),
  ('伪造裸照', 'compact', 'deepfake_privacy'),
  ('制毒教程', 'compact', 'weapons_drugs'),
  ('炸弹制作', 'compact', 'weapons_drugs'),
  ('爆炸物教程', 'compact', 'weapons_drugs'),
  ('伪造证件', 'compact', 'deepfake_privacy'),
  ('恐怖主义宣传', 'compact', 'hate_extremism'),
  ('极端主义宣传', 'compact', 'hate_extremism'),
  ('sexualassault', 'word', 'non_consensual_nudity'),
  ('rape', 'word', 'non_consensual_nudity'),
  ('deepfakenude', 'word', 'deepfake_privacy'),
  ('drugmanufacturinginstructions', 'word', 'weapons_drugs'),
  ('bombmakinginstructions', 'compact', 'weapons_drugs'),
  ('counterfeitdocuments', 'word', 'deepfake_privacy'),
  ('terroristpropaganda', 'word', 'hate_extremism'),
  ('extremistpropaganda', 'word', 'hate_extremism');

START TRANSACTION;

SET @ai_prompt_houbb_inserted = 0;
SET @ai_prompt_project_inserted = 0;
SET @ai_prompt_reclassified = 0;

INSERT INTO `ai_prompt_sensitive_word`
  (`term`, `normalized_term`, `term_key`, `language_code`, `category_code`, `match_mode`,
   `action`, `severity`, `status`, `source_code`, `source_version`, `remark`, `created_at`, `is_deleted`)
SELECT
  candidate.`term`,
  candidate.`normalized_term`,
  SHA2(CONCAT(candidate.`match_mode`, ':', candidate.`normalized_term`), 256),
  candidate.`language_code`,
  candidate.`category_code`,
  candidate.`match_mode`,
  'audit',
  candidate.`severity`,
  0,
  'houbb-sensitive-word-data',
  'fe6fc2921836217b8c90619db81b24af8b22d80f',
  CONCAT(
    'native_tags=', candidate.`source_tags`,
    '; file=src/main/resources/sensitive_word_tags.txt',
    '; license=Apache-2.0',
    '; blob_sha256=37cea2687a1525a436aaa080e918f6c263310bd21b4bce8b05ba5185ee3e5ae8',
    '; reviewed_crlf_sha256=d2ca6f91477238577743e8cfebee71e448b32d2477959c2aa7ba49482b3bd142',
    '; curation=2026-08-11-v1'),
  CURRENT_TIMESTAMP,
  0
FROM `tmp_ai_prompt_houbb_candidate` candidate
WHERE NOT EXISTS (
  SELECT 1
  FROM `ai_prompt_sensitive_word` existing
  WHERE existing.`term_key` = SHA2(CONCAT(candidate.`match_mode`, ':', candidate.`normalized_term`), 256)
);

SET @ai_prompt_houbb_inserted = ROW_COUNT();

INSERT INTO `ai_prompt_sensitive_word`
  (`term`, `normalized_term`, `term_key`, `language_code`, `category_code`, `match_mode`,
   `action`, `severity`, `status`, `source_code`, `source_version`, `remark`, `created_at`, `is_deleted`)
SELECT
  project_rule.`term`,
  project_rule.`normalized_term`,
  SHA2(CONCAT(project_rule.`match_mode`, ':', project_rule.`normalized_term`), 256),
  project_rule.`language_code`,
  project_rule.`category_code`,
  project_rule.`match_mode`,
  'block',
  project_rule.`severity`,
  1,
  'project-curated',
  '2026-08-11-v1',
  'Project-maintained supplement for houbb coverage gaps; curation=2026-08-11-v1',
  CURRENT_TIMESTAMP,
  0
FROM `tmp_ai_prompt_project_rule` project_rule
WHERE NOT EXISTS (
  SELECT 1
  FROM `ai_prompt_sensitive_word` existing
  WHERE existing.`term_key` = SHA2(CONCAT(project_rule.`match_mode`, ':', project_rule.`normalized_term`), 256)
);

SET @ai_prompt_project_inserted = ROW_COUNT();

UPDATE `ai_prompt_sensitive_word` existing
JOIN `tmp_ai_prompt_category_remap` remap
  ON existing.`term_key` = SHA2(CONCAT(remap.`match_mode`, ':', remap.`normalized_term`), 256)
SET existing.`category_code` = remap.`category_code`,
    existing.`updated_at` = CURRENT_TIMESTAMP
WHERE existing.`category_code` <> remap.`category_code`;

SET @ai_prompt_reclassified = ROW_COUNT();

UPDATE `ai_prompt_sensitive_word_revision`
SET `revision` = `revision` + 1,
    `updated_at` = CURRENT_TIMESTAMP
WHERE `id` = 1
  AND (@ai_prompt_houbb_inserted + @ai_prompt_project_inserted + @ai_prompt_reclassified) > 0;

COMMIT;

DROP TEMPORARY TABLE `tmp_ai_prompt_category_remap`;
DROP TEMPORARY TABLE `tmp_ai_prompt_project_rule`;
DROP TEMPORARY TABLE `tmp_ai_prompt_houbb_candidate`;
