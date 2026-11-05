-- AI image route request format (per relay size parameter support).
-- Apply after 20261105-add-gpt-image-26-models.sql, and before deploying the API that reads `request_format`.
-- openai-size: send size=WxH (default; existing dawcode relay behavior).
-- prompt-aspect-ratio: the relay ignores size/aspect_ratio, so the explicit aspect ratio is appended to the provider prompt.

USE `jokester.admin`;

ALTER TABLE `ai_image_model_config`
  ADD COLUMN `request_format` VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci
    NOT NULL DEFAULT 'openai-size'
    COMMENT 'Request format: openai-size or prompt-aspect-ratio'
    AFTER `image_to_image_path`,
  ADD CONSTRAINT `chk_ai_image_model_config_request_format`
    CHECK (`request_format` IN ('openai-size', 'prompt-aspect-ratio'));

-- Relay base URLs are deployment-specific and are not stored in this repository.
-- Switch the routes of a relay that only honors prompt aspect ratios, for example:
-- UPDATE `ai_image_model_config`
-- SET `request_format` = 'prompt-aspect-ratio', `updated_at` = CURRENT_TIMESTAMP
-- WHERE `provider` = 'openai-image' AND `base_url` = '<relay base url>' AND `is_deleted` = 0;
