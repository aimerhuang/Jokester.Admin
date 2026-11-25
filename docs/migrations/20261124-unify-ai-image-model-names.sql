-- AI 生图模型显示名统一为模型级名称，不再带分辨率后缀（分辨率是路由属性）。
-- 历史种子把 gpt-image-2 的 1K/2K/4K 路由分别命名为 “GPT Image 2 1K/2K/4K”，
-- 导致生图页模型下拉显示 “GPT Image 2 1K”。只改仍是这些种子名称的行，运营改过的名称保持不变。
-- 可重复执行；每条被改的路由递增 revision 并写入 operator_user_id=0 的系统审计。可在 API 部署前后任意时间执行。
SET NAMES utf8mb4;
START TRANSACTION;

-- Lock the affected rows first; CREATE ... SELECT cannot take FOR UPDATE itself.
SELECT COUNT(*) INTO @model_name_fix_count
FROM `ai_image_model_config`
WHERE `model_code` = 'gpt-image-2'
  AND `model_name` IN ('GPT Image 2 1K', 'GPT Image 2 2K', 'GPT Image 2 4K')
FOR UPDATE;

DROP TEMPORARY TABLE IF EXISTS `tmp_model_name_fix`;
CREATE TEMPORARY TABLE `tmp_model_name_fix` AS
SELECT `id`, `model_code`, `resolution_code`, `route_role`, `model_name` AS `old_name`
FROM `ai_image_model_config`
WHERE `model_code` = 'gpt-image-2'
  AND `model_name` IN ('GPT Image 2 1K', 'GPT Image 2 2K', 'GPT Image 2 4K');

INSERT INTO `ai_image_model_config_audit`
  (`config_id`, `model_code`, `resolution_code`, `route_role`, `action`, `changed_fields`, `before_json`, `after_json`, `operator_user_id`, `operator_ip`, `created_at`)
SELECT `id`, `model_code`, `resolution_code`, `route_role`, 'update', 'modelName',
       JSON_OBJECT('modelName', `old_name`), JSON_OBJECT('modelName', 'GPT Image 2'), 0, NULL, UTC_TIMESTAMP()
FROM `tmp_model_name_fix`;

UPDATE `ai_image_model_config` c
JOIN `tmp_model_name_fix` f ON f.`id` = c.`id`
SET c.`model_name` = 'GPT Image 2',
    c.`revision` = c.`revision` + 1,
    c.`updated_by` = 0,
    c.`updated_at` = NOW();

DROP TEMPORARY TABLE `tmp_model_name_fix`;
COMMIT;
