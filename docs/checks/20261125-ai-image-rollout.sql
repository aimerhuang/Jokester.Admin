-- 在已完成 2026-11-22 模型配置迁移的业务库运行。各项显示 当前值 / 预期值。
-- 上线后可调整折扣和路由别名，此核验只检查通用迁移的结果。
SELECT DATABASE() AS target_database;

SELECT 'vip_price_columns' AS check_name, COUNT(*) AS actual, 4 AS expected
FROM information_schema.columns
WHERE table_schema = DATABASE() AND table_name = 'ai_image_point_price'
  AND column_name IN ('vip_points', 'vip_price_mode', 'revision', 'updated_by')
UNION ALL
SELECT 'vip_task_columns', COUNT(*), 3
FROM information_schema.columns
WHERE table_schema = DATABASE() AND table_name = 'ai_image_task'
  AND column_name IN ('pricing_tier', 'list_unit_point_cost', 'access_basis')
UNION ALL
SELECT 'vip_tables', COUNT(*), 3
FROM information_schema.tables
WHERE table_schema = DATABASE()
  AND table_name IN ('ai_image_pricing_setting', 'ai_image_model_policy', 'ai_image_pricing_audit')
UNION ALL
SELECT 'vip_price_checks', COUNT(*), 2
FROM information_schema.table_constraints
WHERE constraint_schema = DATABASE() AND table_name = 'ai_image_point_price' AND constraint_type = 'CHECK'
  AND constraint_name IN ('chk_ai_image_point_price_vip_points', 'chk_ai_image_point_price_vip_mode')
UNION ALL
SELECT 'old_gpt_seed_names', COUNT(*), 0
FROM ai_image_model_config
WHERE model_code = 'gpt-image-2' AND model_name IN ('GPT Image 2 1K', 'GPT Image 2 2K', 'GPT Image 2 4K')
UNION ALL
SELECT 'gif_task_columns', COUNT(*), 11
FROM information_schema.columns
WHERE table_schema = DATABASE() AND table_name = 'ai_image_task'
  AND column_name IN ('output_format', 'frame_count', 'frame_duration_ms', 'gif_rate_snapshot',
                      'usage_input_tokens', 'usage_text_input_tokens', 'usage_image_input_tokens',
                      'usage_output_tokens', 'usage_input_breakdown', 'charged_points', 'platform_cost_cny')
UNION ALL
SELECT 'gif_route_column', COUNT(*), 1
FROM information_schema.columns
WHERE table_schema = DATABASE() AND table_name = 'ai_image_model_config' AND column_name = 'gif_provider_model';

-- 定价迁移在同一事务内写入 id=1、首批 VIP 价格与初始化审计。旧库也可运行本检查。
SET @pricing_check = IF(
  EXISTS (SELECT 1 FROM information_schema.tables
          WHERE table_schema = DATABASE() AND table_name = 'ai_image_pricing_setting'),
  'SELECT ''vip_setting_row'' AS check_name, COUNT(*) AS actual, 1 AS expected FROM ai_image_pricing_setting WHERE id = 1',
  'SELECT ''vip_setting_row'' AS check_name, 0 AS actual, 1 AS expected'
);
PREPARE check_stmt FROM @pricing_check;
EXECUTE check_stmt;
DEALLOCATE PREPARE check_stmt;
