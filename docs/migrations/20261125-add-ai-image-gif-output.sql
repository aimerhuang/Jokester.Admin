-- 先迁移，再升级所有 Worker，最后配置原生 GIF 路由。迁移可以重复运行。
SET NAMES utf8mb4;

SET @gif_ddl = IF(
  EXISTS (SELECT 1 FROM information_schema.columns
          WHERE table_schema = DATABASE() AND table_name = 'ai_image_task' AND column_name = 'output_format'),
  'DO 0',
  'ALTER TABLE `ai_image_task`
  ADD COLUMN `output_format` VARCHAR(10) NOT NULL DEFAULT ''png'' COMMENT ''输出格式 png/gif'',
  ADD COLUMN `frame_count` SMALLINT DEFAULT NULL COMMENT ''GIF请求帧数'',
  ADD COLUMN `frame_duration_ms` INT DEFAULT NULL COMMENT ''GIF每帧时长毫秒'',
  ADD COLUMN `gif_rate_snapshot` TEXT DEFAULT NULL COMMENT ''预扣时锁定的GIF单价快照(JSON)'',
  ADD COLUMN `usage_input_tokens` INT DEFAULT NULL COMMENT ''输入Token总数'',
  ADD COLUMN `usage_text_input_tokens` INT DEFAULT NULL COMMENT ''文本输入Token，未拆分时为空'',
  ADD COLUMN `usage_image_input_tokens` INT DEFAULT NULL COMMENT ''参考图输入Token，未拆分时为空'',
  ADD COLUMN `usage_output_tokens` INT DEFAULT NULL COMMENT ''图片输出Token'',
  ADD COLUMN `usage_input_breakdown` VARCHAR(10) DEFAULT NULL COMMENT ''输入明细 split/unsplit，无用量时为空'',
  ADD COLUMN `charged_points` INT DEFAULT NULL COMMENT ''实际扣除积分'',
  ADD COLUMN `platform_cost_cny` DECIMAL(24,12) DEFAULT NULL COMMENT ''按上游用量计算的未封顶平台费用（人民币）'''
);
PREPARE gif_stmt FROM @gif_ddl;
EXECUTE gif_stmt;
DEALLOCATE PREPARE gif_stmt;

SET @gif_ddl = IF(
  EXISTS (SELECT 1 FROM information_schema.columns
          WHERE table_schema = DATABASE() AND table_name = 'ai_image_model_config' AND column_name = 'gif_provider_model'),
  'DO 0',
  'ALTER TABLE `ai_image_model_config`
  ADD COLUMN `gif_provider_model` VARCHAR(100) DEFAULT NULL COMMENT ''原生GIF供应商模型别名'''
);
PREPARE gif_stmt FROM @gif_ddl;
EXECUTE gif_stmt;
DEALLOCATE PREPARE gif_stmt;
