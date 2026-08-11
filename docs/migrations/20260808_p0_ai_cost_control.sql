-- P0-08 AI 成本控制与积分状态机迁移。
-- 执行前停止所有 API/Worker 实例，并先人工处理 status IN (0, 3) 的历史任务。

ALTER TABLE `sys_user_point_detail`
  ADD COLUMN `business_key` VARCHAR(100) NULL COMMENT '幂等业务键，例如 image:{taskId}:reserve/refund' AFTER `source`,
  ADD UNIQUE KEY `uk_sys_user_point_detail_business_key` (`business_key`);

ALTER TABLE `ai_image_task`
  ADD COLUMN `completed_image_count` INT NOT NULL DEFAULT 0 COMMENT '已完成图片数量' AFTER `image_count`,
  ADD COLUMN `idempotency_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '用户幂等键SHA-256' AFTER `completed_image_count`,
  ADD COLUMN `request_fingerprint` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '规范化请求SHA-256' AFTER `idempotency_key`,
  ADD COLUMN `point_cost` INT NOT NULL DEFAULT 0 COMMENT '任务预留积分快照' AFTER `request_fingerprint`,
  ADD COLUMN `billing_status` TINYINT NOT NULL DEFAULT 0 COMMENT '结算状态：0预留 1确认 2部分退款 3全额退款' AFTER `point_cost`,
  ADD COLUMN `started_at` DATETIME NULL COMMENT '开始处理时间' AFTER `error_message`,
  ADD COLUMN `completed_at` DATETIME NULL COMMENT '完成或失败时间' AFTER `started_at`;

UPDATE `ai_image_task`
SET `idempotency_key` = LOWER(SHA2(CONCAT('legacy:', `id`), 256)),
    `request_fingerprint` = LOWER(SHA2(CONCAT('legacy:', `id`), 256)),
    `completed_image_count` = CASE WHEN `status` = 1 THEN `image_count` ELSE 0 END,
    `billing_status` = CASE WHEN `status` IN (1, 2) THEN 1 ELSE 0 END,
    `completed_at` = CASE WHEN `status` IN (1, 2) THEN COALESCE(`updated_at`, `created_at`) ELSE NULL END
WHERE `idempotency_key` IS NULL OR `request_fingerprint` IS NULL;

ALTER TABLE `ai_image_task`
  MODIFY COLUMN `idempotency_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '用户幂等键SHA-256',
  MODIFY COLUMN `request_fingerprint` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范化请求SHA-256',
  DROP KEY `idx_ai_image_task_user_id`,
  ADD UNIQUE KEY `uk_ai_image_task_user_idempotency` (`user_id`, `idempotency_key`),
  ADD KEY `idx_ai_image_task_user_status` (`user_id`, `status`);
