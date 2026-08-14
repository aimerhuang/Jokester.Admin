-- =====================================================
-- 数据库名称：jokester.admin
-- 说明：.NET 10 多站点统一后台管理系统（MySQL 8.0）
-- 场景：统一后台 + 多站点 + RBAC权限 + 博客 + GPT生图
-- 字符集：utf8mb4
-- 排序规则：utf8mb4_unicode_ci
-- =====================================================

CREATE DATABASE IF NOT EXISTS `jokester.admin`
DEFAULT CHARACTER SET utf8mb4
DEFAULT COLLATE utf8mb4_unicode_ci;

USE `jokester.admin`;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- =====================================================
-- 1. 用户表
-- =====================================================
DROP TABLE IF EXISTS `sys_operation_log`;
DROP TABLE IF EXISTS `sys_login_log`;
DROP TABLE IF EXISTS `apple_iap_debt`;
DROP TABLE IF EXISTS `apple_server_notification`;
DROP TABLE IF EXISTS `apple_transaction`;
DROP TABLE IF EXISTS `apple_iap_product`;
DROP TABLE IF EXISTS `account_deletion_request`;
DROP TABLE IF EXISTS `user_consent`;
DROP TABLE IF EXISTS `legal_document`;
DROP TABLE IF EXISTS `media_asset`;
DROP TABLE IF EXISTS `ai_image_favorite`;
DROP TABLE IF EXISTS `ai_image_task`;
DROP TABLE IF EXISTS `ai_prompt_sensitive_word_revision`;
DROP TABLE IF EXISTS `ai_prompt_sensitive_word`;
DROP TABLE IF EXISTS `prompt_library_metric_daily`;
DROP TABLE IF EXISTS `prompt_library_item_version`;
DROP TABLE IF EXISTS `prompt_library_item`;
DROP TABLE IF EXISTS `prompt_library_sync_run`;
DROP TABLE IF EXISTS `ai_image_model_config`;
DROP TABLE IF EXISTS `ai_image_point_price`;
DROP TABLE IF EXISTS `ai_image_parameter`;
DROP TABLE IF EXISTS `blog_comment`;
DROP TABLE IF EXISTS `blog_article_media`;
DROP TABLE IF EXISTS `blog_media`;
DROP TABLE IF EXISTS `blog_article`;
DROP TABLE IF EXISTS `blog_category`;
DROP TABLE IF EXISTS `blog_site_config`;
DROP TABLE IF EXISTS `point_redeem_code`;
DROP TABLE IF EXISTS `point_recharge_order`;
DROP TABLE IF EXISTS `point_recharge_package`;
DROP TABLE IF EXISTS `sys_user_point_detail`;
DROP TABLE IF EXISTS `sys_user_site`;
DROP TABLE IF EXISTS `sys_role_menu`;
DROP TABLE IF EXISTS `sys_user_role`;
DROP TABLE IF EXISTS `sys_menu`;
DROP TABLE IF EXISTS `sys_user`;
DROP TABLE IF EXISTS `sys_role`;
DROP TABLE IF EXISTS `sys_site`;

CREATE TABLE `sys_user` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_name` VARCHAR(50) NOT NULL COMMENT '登录用户名',
  `nick_name` VARCHAR(50) DEFAULT NULL COMMENT '昵称',
  `password_hash` VARCHAR(255) NOT NULL COMMENT '密码哈希',
  `salt` VARCHAR(100) DEFAULT NULL COMMENT '盐值',
  `email` VARCHAR(100) DEFAULT NULL COMMENT '邮箱',
  `phone` VARCHAR(30) DEFAULT NULL COMMENT '手机号',
  `avatar_url` VARCHAR(255) DEFAULT NULL COMMENT '头像地址',
  `signature` VARCHAR(255) DEFAULT NULL COMMENT '个性签名',
  `point_balance` INT NOT NULL DEFAULT 0 COMMENT '当前积分余额',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `is_super_admin` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否超级管理员：1是 0否',
  `last_login_time` DATETIME DEFAULT NULL COMMENT '最后登录时间',
  `last_login_ip` VARCHAR(50) DEFAULT NULL COMMENT '最后登录IP',
  `remark` VARCHAR(500) DEFAULT NULL COMMENT '备注',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_user_name` (`user_name`),
  UNIQUE KEY `uk_sys_user_email` (`email`),
  KEY `idx_sys_user_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='系统用户表';

-- =====================================================
-- 2. 角色表
-- =====================================================
CREATE TABLE `sys_role` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `role_name` VARCHAR(50) NOT NULL COMMENT '角色名称',
  `role_code` VARCHAR(50) NOT NULL COMMENT '角色编码',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `remark` VARCHAR(500) DEFAULT NULL COMMENT '备注',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_role_role_code` (`role_code`),
  KEY `idx_sys_role_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='系统角色表';

-- =====================================================
-- 3. 站点表
-- =====================================================
CREATE TABLE `sys_site` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_name` VARCHAR(100) NOT NULL COMMENT '站点名称',
  `site_code` VARCHAR(50) NOT NULL COMMENT '站点编码',
  `domain` VARCHAR(200) DEFAULT NULL COMMENT '站点域名',
  `description` VARCHAR(500) DEFAULT NULL COMMENT '站点描述',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_site_site_code` (`site_code`),
  KEY `idx_sys_site_status_sort` (`status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='系统站点表';

-- =====================================================
-- 4. 菜单/页面/按钮/接口权限表
-- =====================================================
CREATE TABLE `sys_menu` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '所属站点ID',
  `parent_id` BIGINT NOT NULL DEFAULT 0 COMMENT '父级菜单ID，0表示根节点',
  `menu_name` VARCHAR(100) NOT NULL COMMENT '菜单名称',
  `menu_code` VARCHAR(100) NOT NULL COMMENT '菜单编码',
  `menu_type` TINYINT NOT NULL COMMENT '菜单类型：1目录 2页面 3按钮 4接口',
  `route_path` VARCHAR(200) DEFAULT NULL COMMENT '前端路由地址',
  `component` VARCHAR(200) DEFAULT NULL COMMENT '前端组件路径',
  `permission_code` VARCHAR(100) DEFAULT NULL COMMENT '权限编码',
  `icon` VARCHAR(100) DEFAULT NULL COMMENT '菜单图标',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `visible` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否可见：1是 0否',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `keep_alive` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '前端页面是否缓存',
  `is_external` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否外链',
  `remark` VARCHAR(500) DEFAULT NULL COMMENT '备注',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_menu_menu_code` (`menu_code`),
  UNIQUE KEY `uk_sys_menu_permission_code` (`permission_code`),
  KEY `idx_sys_menu_site_parent_sort` (`site_id`, `parent_id`, `sort`),
  KEY `idx_sys_menu_site_type` (`site_id`, `menu_type`),
  CONSTRAINT `fk_sys_menu_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='系统菜单及权限表';

-- =====================================================
-- 5. 用户角色关联表
-- =====================================================
CREATE TABLE `sys_user_role` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `role_id` BIGINT NOT NULL COMMENT '角色ID',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_role_user_role` (`user_id`, `role_id`),
  KEY `idx_sys_user_role_role_id` (`role_id`),
  CONSTRAINT `fk_sys_user_role_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_sys_user_role_role` FOREIGN KEY (`role_id`) REFERENCES `sys_role` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='用户角色关联表';

-- =====================================================
-- 6. 角色菜单权限关联表
-- =====================================================
CREATE TABLE `sys_role_menu` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `role_id` BIGINT NOT NULL COMMENT '角色ID',
  `menu_id` BIGINT NOT NULL COMMENT '菜单ID',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_role_menu_role_menu` (`role_id`, `menu_id`),
  KEY `idx_sys_role_menu_menu_id` (`menu_id`),
  CONSTRAINT `fk_sys_role_menu_role` FOREIGN KEY (`role_id`) REFERENCES `sys_role` (`id`),
  CONSTRAINT `fk_sys_role_menu_menu` FOREIGN KEY (`menu_id`) REFERENCES `sys_menu` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色菜单权限关联表';

-- =====================================================
-- 7. 用户站点关联表
-- =====================================================
CREATE TABLE `sys_user_site` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_site_user_site` (`user_id`, `site_id`),
  KEY `idx_sys_user_site_site_id` (`site_id`),
  CONSTRAINT `fk_sys_user_site_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_sys_user_site_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='用户站点关联表';

-- =====================================================
-- 8. 用户积分明细表
-- =====================================================
CREATE TABLE `sys_user_point_detail` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `change_points` INT NOT NULL COMMENT '积分变动值，正数增加，负数扣减',
  `balance_after` INT NOT NULL COMMENT '变动后积分余额',
  `change_type` VARCHAR(30) NOT NULL COMMENT '变动类型：gift/consume/adjust',
  `source` VARCHAR(50) NOT NULL COMMENT '来源：register 等',
  `business_key` VARCHAR(100) DEFAULT NULL COMMENT '幂等业务键，例如 image:{taskId}:reserve/refund',
  `remark` VARCHAR(500) DEFAULT NULL COMMENT '备注',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_sys_user_point_detail_business_key` (`business_key`),
  KEY `idx_sys_user_point_detail_user_created` (`user_id`, `created_at`),
  CONSTRAINT `fk_sys_user_point_detail_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='用户积分明细表';

-- =====================================================
-- 9. 博客分类表
-- =====================================================
-- Point recharge packages.
CREATE TABLE `point_recharge_package` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `package_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `name` VARCHAR(100) NOT NULL,
  `description` VARCHAR(300) DEFAULT NULL,
  `points` INT NOT NULL,
  `repeat_points` INT DEFAULT NULL COMMENT 'Points granted after the first redemption; NULL means unchanged',
  `price_amount` DECIMAL(10,2) NOT NULL,
  `currency` VARCHAR(10) NOT NULL DEFAULT 'CNY',
  `validity_days` INT DEFAULT NULL,
  `bonus_percent` INT NOT NULL DEFAULT 0,
  `badge_code` VARCHAR(50) DEFAULT NULL,
  `benefits_json` LONGTEXT DEFAULT NULL,
  `purchase_url` VARCHAR(500) DEFAULT NULL COMMENT 'Supports {orderNo}/{packageCode}/{userId} placeholders',
  `is_featured` TINYINT(1) NOT NULL DEFAULT 0,
  `sort` INT NOT NULL DEFAULT 0,
  `status` TINYINT NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_recharge_package_code` (`package_code`),
  KEY `idx_point_recharge_package_status_sort` (`status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Point recharge packages';

-- Recharge order status: 0 pending, 1 paid/code issued, 2 redeemed, 3 canceled, 4 expired.
CREATE TABLE `point_recharge_order` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `order_no` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `user_id` BIGINT NOT NULL,
  `package_id` BIGINT NOT NULL,
  `package_code` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `points` INT NOT NULL,
  `price_amount` DECIMAL(10,2) NOT NULL,
  `currency` VARCHAR(10) NOT NULL DEFAULT 'CNY',
  `purchase_url` VARCHAR(500) DEFAULT NULL,
  `status` TINYINT NOT NULL DEFAULT 0,
  `expires_at` DATETIME NOT NULL,
  `paid_at` DATETIME DEFAULT NULL,
  `fulfilled_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_recharge_order_no` (`order_no`),
  KEY `idx_point_recharge_order_user_created` (`user_id`, `created_at`),
  KEY `idx_point_recharge_order_status_expires` (`status`, `expires_at`),
  CONSTRAINT `fk_point_recharge_order_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_point_recharge_order_package` FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Point recharge orders';

-- Redeem code status: 0 unused, 1 redeemed, 2 disabled, 3 expired.
CREATE TABLE `point_redeem_code` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `code_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `code_mask` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `package_id` BIGINT DEFAULT NULL,
  `order_id` BIGINT DEFAULT NULL,
  `points` INT NOT NULL,
  `status` TINYINT NOT NULL DEFAULT 0,
  `redeemed_by_user_id` BIGINT DEFAULT NULL,
  `expires_at` DATETIME DEFAULT NULL,
  `redeemed_at` DATETIME DEFAULT NULL,
  `created_by` BIGINT DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_point_redeem_code_hash` (`code_hash`),
  UNIQUE KEY `uk_point_redeem_code_order` (`order_id`),
  KEY `idx_point_redeem_code_status_expires` (`status`, `expires_at`),
  KEY `idx_point_redeem_code_user_package` (`redeemed_by_user_id`, `package_id`),
  CONSTRAINT `fk_point_redeem_code_package` FOREIGN KEY (`package_id`) REFERENCES `point_recharge_package` (`id`),
  CONSTRAINT `fk_point_redeem_code_order` FOREIGN KEY (`order_id`) REFERENCES `point_recharge_order` (`id`),
  CONSTRAINT `fk_point_redeem_code_user` FOREIGN KEY (`redeemed_by_user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_point_redeem_code_creator` FOREIGN KEY (`created_by`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Single-use point redeem codes';

CREATE TABLE `blog_category` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `name` VARCHAR(100) NOT NULL COMMENT '分类名称',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_blog_category_site_sort` (`site_id`, `sort`),
  CONSTRAINT `fk_blog_category_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='博客分类表';

-- =====================================================
-- 10. 博客站点配置表
-- =====================================================
CREATE TABLE `blog_site_config` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `build_date` DATETIME NOT NULL COMMENT '建站时间',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_blog_site_config_site` (`site_id`),
  CONSTRAINT `fk_blog_site_config_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='博客站点配置表';

-- =====================================================
-- 11. 博客文章表
-- =====================================================
CREATE TABLE `blog_article` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `title` VARCHAR(200) NOT NULL COMMENT '文章标题',
  `summary` VARCHAR(500) DEFAULT NULL COMMENT '文章摘要',
  `content` LONGTEXT NOT NULL COMMENT '文章内容',
  `cover_url` VARCHAR(255) DEFAULT NULL COMMENT '封面图地址',
  `category_id` BIGINT DEFAULT NULL COMMENT '分类ID',
  `tags` VARCHAR(500) DEFAULT NULL COMMENT '标签，逗号分隔',
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '状态：0草稿 1已发布 2隐藏',
  `view_count` INT NOT NULL DEFAULT 0 COMMENT '浏览量',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '创建人',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `updated_by` BIGINT DEFAULT NULL COMMENT '更新人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_blog_article_site_status` (`site_id`, `status`),
  KEY `idx_blog_article_created_at` (`created_at`),
  CONSTRAINT `fk_blog_article_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='博客文章表';

-- =====================================================
-- 9. GPT生图参数表
-- =====================================================
CREATE TABLE `ai_image_parameter` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `param_type` VARCHAR(30) NOT NULL COMMENT '参数类型：resolution/quality/aspect_ratio',
  `param_code` VARCHAR(50) NOT NULL COMMENT '参数编码',
  `param_name` VARCHAR(100) NOT NULL COMMENT '参数名称',
  `provider_value` VARCHAR(50) DEFAULT NULL COMMENT '供应商参数值',
  `value_int_1` INT DEFAULT NULL COMMENT '参数数值1：长边/比例宽',
  `value_int_2` INT DEFAULT NULL COMMENT '参数数值2：比例高',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_parameter_type_code` (`param_type`, `param_code`),
  KEY `idx_ai_image_parameter_type_status` (`param_type`, `status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='GPT生图参数表';

-- =====================================================
-- 10. AI生图积分价格表
-- =====================================================
CREATE TABLE `ai_image_point_price` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `model_code` VARCHAR(100) NOT NULL COMMENT '模型编码，例如 gpt-image-2',
  `resolution_code` VARCHAR(50) NOT NULL COMMENT '分辨率档位编码，例如 1k/4k',
  `quality_code` VARCHAR(50) NOT NULL COMMENT '质量档位编码，例如 low/med/high',
  `points` INT NOT NULL COMMENT '消耗积分',
  `price_amount` DECIMAL(10,2) NOT NULL COMMENT '折算金额',
  `currency` VARCHAR(10) NOT NULL DEFAULT 'CNY' COMMENT '币种',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_point_price_model_resolution_quality` (`model_code`, `resolution_code`, `quality_code`),
  KEY `idx_ai_image_point_price_model_status_sort` (`model_code`, `status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图积分价格表';

-- =====================================================
-- 11. AI生图模型配置表
-- =====================================================
CREATE TABLE `ai_image_model_config` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `model_code` VARCHAR(100) NOT NULL COMMENT '前端/业务模型编码',
  `model_name` VARCHAR(100) NOT NULL COMMENT '模型展示名',
  `provider` VARCHAR(50) NOT NULL COMMENT '供应商/调用协议',
  `provider_model` VARCHAR(100) NOT NULL COMMENT '供应商真实模型ID',
  `resolution_code` VARCHAR(50) NOT NULL DEFAULT '' COMMENT '分辨率档位编码，空字符串表示不区分分辨率',
  `route_role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT 'primary' COMMENT '路由角色：primary主路由 fallback备用路由',
  `base_url` VARCHAR(500) NOT NULL COMMENT '供应商基础地址',
  `api_key` VARCHAR(500) NOT NULL DEFAULT '' COMMENT '供应商API Key，生产环境手动写入',
  `text_to_image_path` VARCHAR(200) NOT NULL DEFAULT '/images/generations' COMMENT '文生图端点路径',
  `image_to_image_path` VARCHAR(200) NOT NULL DEFAULT '/images/edits' COMMENT '图生图端点路径',
  `sort` INT NOT NULL DEFAULT 0 COMMENT '排序',
  `status` TINYINT NOT NULL DEFAULT 1 COMMENT '状态：1启用 0禁用',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  CONSTRAINT `chk_ai_image_model_config_route_role` CHECK (`route_role` IN ('primary', 'fallback')),
  UNIQUE KEY `uk_ai_image_model_config_code_resolution_role` (`model_code`, `resolution_code`, `route_role`),
  KEY `idx_ai_image_model_config_resolve` (`model_code`, `resolution_code`, `status`, `is_deleted`, `route_role`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图模型配置表';

-- =====================================================
-- AI prompt sensitive word rules and cache revision
-- =====================================================
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

-- =====================================================
-- 12. 提示词库同步与快照表
-- =====================================================
CREATE TABLE `prompt_library_sync_run` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `source` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `source_commit_sha` CHAR(40) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `source_etag` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `source_readme_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL,
  `status` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `parsed_count` INT NOT NULL DEFAULT 0,
  `selected_count` INT NOT NULL DEFAULT 0,
  `downloaded_count` INT NOT NULL DEFAULT 0,
  `reused_image_count` INT NOT NULL DEFAULT 0,
  `failed_image_count` INT NOT NULL DEFAULT 0,
  `started_at` DATETIME NOT NULL,
  `finished_at` DATETIME DEFAULT NULL,
  `error_message` VARCHAR(2000) DEFAULT NULL,
  `warning_message` VARCHAR(4000) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_prompt_sync_run_source_status_started` (`source`, `status`, `started_at`),
  KEY `idx_prompt_sync_run_finished` (`finished_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='提示词库同步记录';

CREATE TABLE `prompt_library_item` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `source` VARCHAR(50) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `source_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `external_no` INT NOT NULL,
  `external_occurrence` INT NOT NULL DEFAULT 1,
  `title` VARCHAR(300) NOT NULL,
  `description` LONGTEXT NOT NULL,
  `prompt_text` LONGTEXT NOT NULL,
  `prompt_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `cover_source_url` VARCHAR(1000) NOT NULL,
  `cover_local_path` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `author_name` VARCHAR(200) DEFAULT NULL,
  `author_url` VARCHAR(1000) DEFAULT NULL,
  `source_url` VARCHAR(1000) DEFAULT NULL,
  `source_published_at` DATETIME DEFAULT NULL,
  `language` VARCHAR(50) DEFAULT NULL,
  `source_position` INT NOT NULL,
  `snapshot_id` BIGINT NOT NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_prompt_item_source_key` (`source`, `source_key`),
  KEY `idx_prompt_item_source_external` (`source`, `external_no`, `external_occurrence`),
  KEY `idx_prompt_item_source_active_position` (`source`, `is_active`, `source_position`),
  KEY `idx_prompt_item_snapshot` (`snapshot_id`),
  CONSTRAINT `fk_prompt_item_snapshot` FOREIGN KEY (`snapshot_id`) REFERENCES `prompt_library_sync_run` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='稳定提示词条目与当前版本';

CREATE TABLE `prompt_library_item_version` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `snapshot_id` BIGINT NOT NULL,
  `prompt_id` BIGINT NOT NULL,
  `external_no` INT NOT NULL,
  `external_occurrence` INT NOT NULL DEFAULT 1,
  `title` VARCHAR(300) NOT NULL,
  `description` LONGTEXT NOT NULL,
  `prompt_text` LONGTEXT NOT NULL,
  `prompt_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `cover_source_url` VARCHAR(1000) NOT NULL,
  `cover_local_path` VARCHAR(500) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `author_name` VARCHAR(200) DEFAULT NULL,
  `author_url` VARCHAR(1000) DEFAULT NULL,
  `source_url` VARCHAR(1000) DEFAULT NULL,
  `source_published_at` DATETIME DEFAULT NULL,
  `language` VARCHAR(50) DEFAULT NULL,
  `source_position` INT NOT NULL,
  `created_at` DATETIME NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_prompt_item_version_snapshot_prompt` (`snapshot_id`, `prompt_id`),
  KEY `idx_prompt_item_version_prompt` (`prompt_id`),
  CONSTRAINT `fk_prompt_item_version_snapshot` FOREIGN KEY (`snapshot_id`) REFERENCES `prompt_library_sync_run` (`id`),
  CONSTRAINT `fk_prompt_item_version_prompt` FOREIGN KEY (`prompt_id`) REFERENCES `prompt_library_item` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='提示词条目快照版本';

CREATE TABLE `prompt_library_metric_daily` (
  `prompt_id` BIGINT NOT NULL,
  `metric_date` DATE NOT NULL,
  `detail_view_count` BIGINT NOT NULL DEFAULT 0,
  `copy_count` BIGINT NOT NULL DEFAULT 0,
  `use_count` BIGINT NOT NULL DEFAULT 0,
  `successful_generation_count` BIGINT NOT NULL DEFAULT 0,
  `updated_at` DATETIME NOT NULL,
  PRIMARY KEY (`prompt_id`, `metric_date`),
  KEY `idx_prompt_metric_date` (`metric_date`),
  CONSTRAINT `fk_prompt_metric_prompt` FOREIGN KEY (`prompt_id`) REFERENCES `prompt_library_item` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='提示词库每日行为指标';

-- =====================================================
-- 13. GPT生图任务表
-- =====================================================
CREATE TABLE `ai_image_task` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `source_prompt_id` BIGINT DEFAULT NULL COMMENT '来源提示词库稳定条目ID',
  `prompt` VARCHAR(4000) NOT NULL COMMENT '生图提示词',
  `negative_prompt` VARCHAR(2000) DEFAULT NULL COMMENT '反向提示词',
  `prompt_policy_version` BIGINT NOT NULL DEFAULT 0 COMMENT '最近一次提示词审核使用的词库版本',
  `prompt_checked_at` DATETIME DEFAULT NULL COMMENT '最近一次提示词审核通过时间',
  `model_name` VARCHAR(100) DEFAULT NULL COMMENT '模型名称',
  `image_count` INT NOT NULL DEFAULT 1 COMMENT '图片数量',
  `completed_image_count` INT NOT NULL DEFAULT 0 COMMENT '已完成图片数量',
  `idempotency_key` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '用户幂等键SHA-256',
  `request_fingerprint` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范化请求SHA-256',
  `point_cost` INT NOT NULL DEFAULT 0 COMMENT '任务预留积分快照',
  `billing_status` TINYINT NOT NULL DEFAULT 0 COMMENT '结算状态：0预留 1确认 2部分退款 3全额退款',
  `resolution_code` VARCHAR(50) NOT NULL DEFAULT '1k' COMMENT '分辨率档位编码',
  `quality_code` VARCHAR(50) NOT NULL DEFAULT 'med' COMMENT '质量档位编码',
  `aspect_ratio_code` VARCHAR(50) NOT NULL DEFAULT '1:1' COMMENT '画幅比例编码',
  `width` INT NOT NULL DEFAULT 1024 COMMENT '图片宽度（像素）',
  `height` INT NOT NULL DEFAULT 1024 COMMENT '图片高度（像素）',
  `size` VARCHAR(50) NOT NULL DEFAULT '1024x1024' COMMENT '图片尺寸',
  `quality` VARCHAR(20) NOT NULL DEFAULT 'medium' COMMENT '图片质量',
  `reference_image_urls` LONGTEXT DEFAULT NULL COMMENT '参考图地址集合(JSON)',
  `mask_image_url` VARCHAR(255) DEFAULT NULL COMMENT '蒙版图地址',
  `result_urls` LONGTEXT DEFAULT NULL COMMENT '结果图片地址集合(JSON或逗号分隔)',
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '状态：0待处理 1成功 2失败 3处理中',
  `error_message` VARCHAR(1000) DEFAULT NULL COMMENT '错误信息',
  `started_at` DATETIME DEFAULT NULL COMMENT '开始处理时间',
  `completed_at` DATETIME DEFAULT NULL COMMENT '完成或失败时间',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_task_user_idempotency` (`user_id`, `idempotency_key`),
  KEY `idx_ai_image_task_site_status` (`site_id`, `status`),
  KEY `idx_ai_image_task_user_status` (`user_id`, `status`),
  KEY `idx_ai_image_task_source_prompt` (`source_prompt_id`),
  KEY `idx_ai_image_task_created_at` (`created_at`),
  CONSTRAINT `fk_ai_image_task_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`),
  CONSTRAINT `fk_ai_image_task_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`),
  CONSTRAINT `fk_ai_image_task_source_prompt` FOREIGN KEY (`source_prompt_id`) REFERENCES `prompt_library_item` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='GPT生图任务表';

-- =====================================================
-- 13. GPT生图收藏表
-- =====================================================
CREATE TABLE `ai_image_favorite` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `task_id` BIGINT NOT NULL COMMENT '生图任务ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `image_url` VARCHAR(500) NOT NULL COMMENT '收藏图片地址',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_ai_image_favorite_task_user_url` (`task_id`, `user_id`, `image_url`),
  KEY `idx_ai_image_favorite_task_user` (`task_id`, `user_id`),
  KEY `idx_ai_image_favorite_user_deleted_created` (`user_id`, `is_deleted`, `created_at`),
  CONSTRAINT `fk_ai_image_favorite_task` FOREIGN KEY (`task_id`) REFERENCES `ai_image_task` (`id`),
  CONSTRAINT `fk_ai_image_favorite_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='GPT生图收藏表';

-- =====================================================
-- 11. 登录日志表
-- =====================================================
CREATE TABLE `sys_login_log` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_id` BIGINT DEFAULT NULL COMMENT '用户ID',
  `user_name` VARCHAR(50) DEFAULT NULL COMMENT '用户名',
  `ip` VARCHAR(50) DEFAULT NULL COMMENT '登录IP',
  `user_agent` VARCHAR(500) DEFAULT NULL COMMENT 'UserAgent',
  `login_status` TINYINT NOT NULL COMMENT '登录状态：1成功 0失败',
  `error_message` VARCHAR(500) DEFAULT NULL COMMENT '失败原因',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  KEY `idx_sys_login_log_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='登录日志表';

-- =====================================================
-- 11. 操作日志表
-- =====================================================
CREATE TABLE `sys_operation_log` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `user_id` BIGINT DEFAULT NULL COMMENT '用户ID',
  `module_name` VARCHAR(100) DEFAULT NULL COMMENT '模块名称',
  `action_name` VARCHAR(100) DEFAULT NULL COMMENT '操作名称',
  `request_method` VARCHAR(20) DEFAULT NULL COMMENT '请求方式',
  `request_url` VARCHAR(500) DEFAULT NULL COMMENT '请求地址',
  `request_data` LONGTEXT DEFAULT NULL COMMENT '请求数据',
  `response_data` LONGTEXT DEFAULT NULL COMMENT '响应数据',
  `ip` VARCHAR(50) DEFAULT NULL COMMENT '请求IP',
  `execution_ms` INT DEFAULT NULL COMMENT '执行耗时(毫秒)',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  KEY `idx_sys_operation_log_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='操作日志表';

-- =====================================================
-- 12. 初始化站点数据
-- =====================================================
INSERT INTO `sys_site` (`site_name`, `site_code`, `domain`, `description`, `status`, `sort`)
VALUES
('系统管理', 'system', NULL, '后台系统管理能力', 1, 0),
('个人博客', 'blog', NULL, '个人博客网站后台管理', 1, 1),
('GPT生图网站', 'ai_image', NULL, 'GPT生图网站后台管理', 1, 2);


-- =====================================================
-- 13. 初始化博客默认配置
-- =====================================================
INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '技术教程', 1, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
LIMIT 1;

INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '日常笔记', 2, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
LIMIT 1;

INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '好物分享', 3, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
LIMIT 1;

INSERT INTO `blog_site_config` (`site_id`, `build_date`, `created_at`)
VALUES (@blog_site_id, '2026-06-01 00:00:00', NOW());


-- =====================================================
-- 14. 初始化角色数据
-- =====================================================
INSERT INTO `sys_role` (`role_name`, `role_code`, `status`, `remark`)
VALUES
('超级管理员', 'super_admin', 1, '拥有系统全部权限'),
('博客管理员', 'blog_admin', 1, '负责博客内容管理'),
('生图管理员', 'ai_operator', 1, '负责GPT生图功能管理');

-- =====================================================
-- 14. 初始化管理员用户
-- 注意：password_hash 和 salt 请在正式使用前替换成真实值
-- =====================================================
INSERT INTO `sys_user`
(`user_name`, `nick_name`, `password_hash`, `salt`, `email`, `phone`, `status`, `is_super_admin`, `remark`)
VALUES
('admin', '系统管理员', 'REPLACE_WITH_REAL_PASSWORD_HASH', 'REPLACE_WITH_REAL_SALT', 'admin@example.com', NULL, 1, 1, '默认超级管理员');

-- =====================================================
-- 15. 绑定管理员角色
-- =====================================================
INSERT INTO `sys_user_role` (`user_id`, `role_id`)
SELECT u.`id`, r.`id`
FROM `sys_user` u
JOIN `sys_role` r ON r.`role_code` = 'super_admin'
WHERE u.`user_name` = 'admin';

-- =====================================================
-- 16. 给管理员开通全部站点
-- =====================================================
INSERT INTO `sys_user_site` (`user_id`, `site_id`)
SELECT u.`id`, s.`id`
FROM `sys_user` u
JOIN `sys_site` s
WHERE u.`user_name` = 'admin';

-- =====================================================
-- 17. 初始化菜单与权限
-- menu_type: 1目录 2页面 3按钮 4接口
-- =====================================================
SET @system_site_id = (SELECT `id` FROM `sys_site` WHERE `site_code` = 'system' LIMIT 1);
SET @blog_site_id = (SELECT `id` FROM `sys_site` WHERE `site_code` = 'blog' LIMIT 1);
SET @ai_site_id = (SELECT `id` FROM `sys_site` WHERE `site_code` = 'ai_image' LIMIT 1);

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, 0, '系统管理', 'system_root', 1, '/system', NULL, NULL, 'setting', 0, 1, 1, 0, 0, '系统管理目录');
SET @system_root_menu_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_root_menu_id, '站点管理', 'system_site_page', 2, '/system/site', 'views/system/site/index', 'System.Site.View', 'globe', 1, 1, 1, 1, 0, '站点管理页面');
SET @system_site_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_site_page_id, '新增站点', 'system_site_create', 3, NULL, NULL, 'System.Site.Create', NULL, 1, 1, 1, 0, 0, '新增站点按钮权限'),
(@system_site_id, @system_site_page_id, '编辑站点', 'system_site_update', 3, NULL, NULL, 'System.Site.Update', NULL, 2, 1, 1, 0, 0, '编辑站点按钮权限'),
(@system_site_id, @system_site_page_id, '更新站点状态', 'system_site_update_status', 3, NULL, NULL, 'System.Site.UpdateStatus', NULL, 3, 1, 1, 0, 0, '更新站点状态按钮权限'),
(@system_site_id, @system_site_page_id, '删除站点', 'system_site_delete', 3, NULL, NULL, 'System.Site.Delete', NULL, 4, 1, 1, 0, 0, '删除站点按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_root_menu_id, '角色管理', 'system_role_page', 2, '/system/role', 'views/system/role/index', 'System.Role.View', 'team', 2, 1, 1, 1, 0, '角色管理页面');
SET @system_role_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_role_page_id, '新增角色', 'system_role_create', 3, NULL, NULL, 'System.Role.Create', NULL, 1, 1, 1, 0, 0, '新增角色按钮权限'),
(@system_site_id, @system_role_page_id, '编辑角色', 'system_role_update', 3, NULL, NULL, 'System.Role.Update', NULL, 2, 1, 1, 0, 0, '编辑角色按钮权限'),
(@system_site_id, @system_role_page_id, '分配菜单', 'system_role_assign_menus', 3, NULL, NULL, 'System.Role.AssignMenus', NULL, 3, 1, 1, 0, 0, '分配角色菜单按钮权限'),
(@system_site_id, @system_role_page_id, '更新角色状态', 'system_role_update_status', 3, NULL, NULL, 'System.Role.UpdateStatus', NULL, 4, 1, 1, 0, 0, '更新角色状态按钮权限'),
(@system_site_id, @system_role_page_id, '删除角色', 'system_role_delete', 3, NULL, NULL, 'System.Role.Delete', NULL, 5, 1, 1, 0, 0, '删除角色按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_root_menu_id, '菜单管理', 'system_menu_page', 2, '/system/menu', 'views/system/menu/index', 'System.Menu.View', 'menu', 3, 1, 1, 1, 0, '菜单管理页面');
SET @system_menu_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_menu_page_id, '新增菜单', 'system_menu_create', 3, NULL, NULL, 'System.Menu.Create', NULL, 1, 1, 1, 0, 0, '新增菜单按钮权限'),
(@system_site_id, @system_menu_page_id, '编辑菜单', 'system_menu_update', 3, NULL, NULL, 'System.Menu.Update', NULL, 2, 1, 1, 0, 0, '编辑菜单按钮权限'),
(@system_site_id, @system_menu_page_id, '更新菜单状态', 'system_menu_update_status', 3, NULL, NULL, 'System.Menu.UpdateStatus', NULL, 3, 1, 1, 0, 0, '更新菜单状态按钮权限'),
(@system_site_id, @system_menu_page_id, '删除菜单', 'system_menu_delete', 3, NULL, NULL, 'System.Menu.Delete', NULL, 4, 1, 1, 0, 0, '删除菜单按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_root_menu_id, '用户管理', 'system_user_page', 2, '/system/user', 'views/system/user/index', 'System.User.View', 'user', 4, 1, 1, 1, 0, '用户管理页面');
SET @system_user_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_user_page_id, '新增用户', 'system_user_create', 3, NULL, NULL, 'System.User.Create', NULL, 1, 1, 1, 0, 0, '新增用户按钮权限'),
(@system_site_id, @system_user_page_id, '编辑用户', 'system_user_update', 3, NULL, NULL, 'System.User.Update', NULL, 2, 1, 1, 0, 0, '编辑用户按钮权限'),
(@system_site_id, @system_user_page_id, '用户授权', 'system_user_authorize', 3, NULL, NULL, 'System.User.Authorize', NULL, 3, 1, 1, 0, 0, '用户授权按钮权限'),
(@system_site_id, @system_user_page_id, '更新用户状态', 'system_user_update_status', 3, NULL, NULL, 'System.User.UpdateStatus', NULL, 4, 1, 1, 0, 0, '更新用户状态按钮权限'),
(@system_site_id, @system_user_page_id, '删除用户', 'system_user_delete', 3, NULL, NULL, 'System.User.Delete', NULL, 5, 1, 1, 0, 0, '删除用户按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_root_menu_id, '日志管理', 'system_log_root', 1, '/system/log', NULL, NULL, 'file-text', 5, 1, 1, 0, 0, '日志管理目录');
SET @system_log_root_menu_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_log_root_menu_id, '登录日志', 'system_log_login_page', 2, '/system/log/login', 'views/system/log/login/index', 'System.Log.Login.View', 'login', 1, 1, 1, 1, 0, '登录日志页面');
SET @system_log_login_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_log_login_page_id, '删除登录日志', 'system_log_login_delete', 3, NULL, NULL, 'System.Log.Login.Delete', NULL, 1, 1, 1, 0, 0, '删除登录日志按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_log_root_menu_id, '操作日志', 'system_log_operation_page', 2, '/system/log/operation', 'views/system/log/operation/index', 'System.Log.Operation.View', 'profile', 2, 1, 1, 1, 0, '操作日志页面');
SET @system_log_operation_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@system_site_id, @system_log_operation_page_id, '删除操作日志', 'system_log_operation_delete', 3, NULL, NULL, 'System.Log.Operation.Delete', NULL, 1, 1, 1, 0, 0, '删除操作日志按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, 0, '内容管理', 'blog_content', 1, '/blog', NULL, NULL, 'document', 1, 1, 1, 0, 0, '博客内容管理目录');
SET @blog_root_menu_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_root_menu_id, '文章管理', 'blog_article_page', 2, '/blog/article', 'views/blog/article/index', 'Blog.Article.View', 'edit', 1, 1, 1, 1, 0, '文章管理页面');
SET @blog_article_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_article_page_id, '新增文章', 'blog_article_create', 3, NULL, NULL, 'Blog.Article.Create', NULL, 1, 1, 1, 0, 0, '新增文章按钮权限'),
(@blog_site_id, @blog_article_page_id, '编辑文章', 'blog_article_update', 3, NULL, NULL, 'Blog.Article.Update', NULL, 2, 1, 1, 0, 0, '编辑文章按钮权限'),
(@blog_site_id, @blog_article_page_id, '删除文章', 'blog_article_delete', 3, NULL, NULL, 'Blog.Article.Delete', NULL, 3, 1, 1, 0, 0, '删除文章按钮权限'),
(@blog_site_id, @blog_article_page_id, '发布文章', 'blog_article_publish', 3, NULL, NULL, 'Blog.Article.Publish', NULL, 4, 1, 1, 0, 0, '发布文章按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_root_menu_id, '媒体管理', 'blog_media_page', 2, '/blog/media', 'views/blog/media/index', 'Blog.Media.View', 'picture', 2, 1, 1, 1, 0, '媒体资源管理页面');
SET @blog_media_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_media_page_id, '上传媒体', 'blog_media_upload', 3, NULL, NULL, 'Blog.Media.Upload', NULL, 1, 1, 1, 0, 0, '上传图片/GIF按钮权限'),
(@blog_site_id, @blog_media_page_id, '删除媒体', 'blog_media_delete', 3, NULL, NULL, 'Blog.Media.Delete', NULL, 2, 1, 1, 0, 0, '删除媒体按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_root_menu_id, '评论管理', 'blog_comment_page', 2, '/blog/comment', 'views/blog/comment/index', 'Blog.Comment.View', 'message', 3, 1, 1, 1, 0, '博客评论管理页面');
SET @blog_comment_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_comment_page_id, '审核评论', 'blog_comment_review', 3, NULL, NULL, 'Blog.Comment.Review', NULL, 1, 1, 1, 0, 0, '审核评论按钮权限'),
(@blog_site_id, @blog_comment_page_id, '删除评论', 'blog_comment_delete', 3, NULL, NULL, 'Blog.Comment.Delete', NULL, 2, 1, 1, 0, 0, '删除评论按钮权限');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@blog_site_id, @blog_root_menu_id, '仪表盘', 'blog_dashboard_page', 2, '/blog/dashboard', 'views/blog/dashboard/index', 'Blog.Dashboard.View', 'dashboard', 4, 1, 1, 1, 0, '博客仪表盘统计页面');

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@ai_site_id, 0, '生图管理', 'ai_image_root', 1, '/ai', NULL, NULL, 'picture', 1, 1, 1, 0, 0, '生图系统目录');
SET @ai_root_menu_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@ai_site_id, @ai_root_menu_id, '图片生成', 'ai_image_generate_page', 2, '/ai/generate', 'views/ai/generate/index', 'AiImage.Page', 'magic', 1, 1, 1, 1, 0, '图片生成页面');
SET @ai_generate_page_id = LAST_INSERT_ID();

INSERT INTO `sys_menu`
(`site_id`, `parent_id`, `menu_name`, `menu_code`, `menu_type`, `route_path`, `component`, `permission_code`, `icon`, `sort`, `visible`, `status`, `keep_alive`, `is_external`, `remark`)
VALUES
(@ai_site_id, @ai_generate_page_id, '执行生图', 'ai_image_generate', 3, NULL, NULL, 'AiImage.Generate', NULL, 1, 1, 1, 0, 0, '执行生图权限'),
(@ai_site_id, @ai_generate_page_id, '查看记录', 'ai_image_record_view', 3, NULL, NULL, 'AiImage.Record.View', NULL, 2, 1, 1, 0, 0, '查看记录权限'),
(@ai_site_id, @ai_generate_page_id, '收藏图片', 'ai_image_favorite', 3, NULL, NULL, 'AiImage.Favorite', NULL, 3, 1, 1, 0, 0, '收藏图片权限'),
(@ai_site_id, @ai_generate_page_id, '删除记录', 'ai_image_record_delete', 3, NULL, NULL, 'AiImage.Record.Delete', NULL, 4, 1, 1, 0, 0, '删除记录权限'),
(@ai_site_id, @ai_generate_page_id, '查看提示词同步状态', 'prompt_library_sync_view', 3, NULL, NULL, 'PromptLibrary.Sync.View', NULL, 20, 0, 1, 0, 0, '查看提示词库同步状态'),
(@ai_site_id, @ai_generate_page_id, '执行提示词同步', 'prompt_library_sync_run', 3, NULL, NULL, 'PromptLibrary.Sync.Run', NULL, 21, 0, 1, 0, 0, '手动触发提示词库同步'),
(@ai_site_id, @ai_generate_page_id, '切换提示词快照', 'prompt_library_snapshot_switch', 3, NULL, NULL, 'PromptLibrary.Sync.Switch', NULL, 22, 0, 1, 0, 0, '激活已成功发布的提示词库历史快照'),
(@ai_site_id, @ai_generate_page_id, 'Sensitive word view', 'ai_prompt_sensitive_word_view', 3, NULL, NULL, 'AiImage.SensitiveWord.View', NULL, 30, 0, 1, 0, 0, 'View AI prompt sensitive words'),
(@ai_site_id, @ai_generate_page_id, 'Sensitive word manage', 'ai_prompt_sensitive_word_manage', 3, NULL, NULL, 'AiImage.SensitiveWord.Manage', NULL, 31, 0, 1, 0, 0, 'Manage AI prompt sensitive words'),
(@ai_site_id, @ai_generate_page_id, 'Sensitive word test', 'ai_prompt_sensitive_word_test', 3, NULL, NULL, 'AiImage.SensitiveWord.Test', NULL, 32, 0, 1, 0, 0, 'Test AI prompt sensitive words');

-- =====================================================
-- 18. 给角色分配初始化权限
-- =====================================================
INSERT INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m
WHERE r.`role_code` = 'super_admin';

INSERT INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m ON m.`site_id` = @blog_site_id
WHERE r.`role_code` = 'blog_admin';

INSERT INTO `sys_role_menu` (`role_id`, `menu_id`)
SELECT r.`id`, m.`id`
FROM `sys_role` r
JOIN `sys_menu` m
WHERE r.`role_code` = 'ai_operator'
  AND m.`menu_code` <> 'ai_prompt_sensitive_word_manage';

-- =====================================================
-- 19. 示例博客数据
-- =====================================================
INSERT INTO `blog_article`
(`site_id`, `title`, `summary`, `content`, `cover_url`, `category_id`, `tags`, `status`, `view_count`, `created_by`)
SELECT
  @blog_site_id,
  '欢迎使用个人博客后台',
  '这是第一篇初始化文章',
  '这里是文章正文内容，你可以在后台继续编辑。',
  NULL,
  NULL,
  '.NET,博客,后台管理',
  1,
  0,
  u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
LIMIT 1;

-- =====================================================
-- 20. 媒体资源表（图片/GIF 统一管理）
-- =====================================================
CREATE TABLE `blog_media` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `file_name` VARCHAR(255) NOT NULL COMMENT '原始文件名',
  `storage_key` VARCHAR(500) NOT NULL COMMENT '存储路径/对象Key（相对路径或OSS Key）',
  `url` VARCHAR(1000) NOT NULL COMMENT '可访问的完整URL',
  `mime_type` VARCHAR(100) NOT NULL COMMENT 'MIME类型，如 image/jpeg image/gif',
  `file_size` BIGINT NOT NULL DEFAULT 0 COMMENT '文件大小（字节）',
  `width` INT DEFAULT NULL COMMENT '图片宽度（像素）',
  `height` INT DEFAULT NULL COMMENT '图片高度（像素）',
  `storage_provider` VARCHAR(50) NOT NULL DEFAULT 'local' COMMENT '存储提供商：local本地 oss阿里云 cos腾讯云 s3 AWS',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '上传时间',
  `created_by` BIGINT DEFAULT NULL COMMENT '上传人',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_blog_media_site_id` (`site_id`),
  KEY `idx_blog_media_created_at` (`created_at`),
  CONSTRAINT `fk_blog_media_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='博客媒体资源表';

-- =====================================================
-- 21. 文章与媒体关联表（用于追踪文章引用了哪些媒体）
-- =====================================================
CREATE TABLE `blog_article_media` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `article_id` BIGINT NOT NULL COMMENT '文章ID',
  `media_id` BIGINT NOT NULL COMMENT '媒体ID',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_blog_article_media` (`article_id`, `media_id`),
  KEY `idx_blog_article_media_media_id` (`media_id`),
  CONSTRAINT `fk_blog_article_media_article` FOREIGN KEY (`article_id`) REFERENCES `blog_article` (`id`),
  CONSTRAINT `fk_blog_article_media_media` FOREIGN KEY (`media_id`) REFERENCES `blog_media` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='文章媒体关联表';

-- =====================================================
-- 22. 文章评论表
-- =====================================================
CREATE TABLE `blog_comment` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `article_id` BIGINT NOT NULL COMMENT '文章ID',
  `parent_id` BIGINT DEFAULT NULL COMMENT '父评论ID，NULL表示一级评论',
  `author_name` VARCHAR(80) NOT NULL COMMENT '评论者昵称',
  `author_email` VARCHAR(120) DEFAULT NULL COMMENT '评论者邮箱',
  `author_website` VARCHAR(255) DEFAULT NULL COMMENT '评论者网站',
  `content` TEXT NOT NULL COMMENT '评论内容',
  `ip_address` VARCHAR(50) DEFAULT NULL COMMENT '评论者IP',
  `user_agent` VARCHAR(500) DEFAULT NULL COMMENT 'UserAgent',
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '状态：0待审核 1已通过 2已拒绝 3垃圾评论',
  `reviewed_at` DATETIME DEFAULT NULL COMMENT '审核时间',
  `reviewed_by` BIGINT DEFAULT NULL COMMENT '审核人',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_blog_comment_site_article_status` (`site_id`, `article_id`, `status`),
  KEY `idx_blog_comment_article_parent` (`article_id`, `parent_id`),
  KEY `idx_blog_comment_status_created_at` (`status`, `created_at`),
  KEY `idx_blog_comment_created_at` (`created_at`),
  KEY `idx_blog_comment_reviewed_by` (`reviewed_by`),
  CONSTRAINT `fk_blog_comment_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`),
  CONSTRAINT `fk_blog_comment_article` FOREIGN KEY (`article_id`) REFERENCES `blog_article` (`id`),
  CONSTRAINT `fk_blog_comment_parent` FOREIGN KEY (`parent_id`) REFERENCES `blog_comment` (`id`),
  CONSTRAINT `fk_blog_comment_reviewed_by` FOREIGN KEY (`reviewed_by`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='博客评论表';

-- =====================================================
-- iOS API upgrade: legal, consent, assets, deletion and Apple IAP
-- =====================================================
CREATE TABLE `legal_document` (
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

CREATE TABLE `user_consent` (
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

CREATE TABLE `account_deletion_request` (
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

CREATE TABLE `media_asset` (
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

CREATE TABLE `apple_iap_product` (
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

CREATE TABLE `apple_transaction` (
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

CREATE TABLE `apple_server_notification` (
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

CREATE TABLE `apple_iap_debt` (
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

-- =====================================================
-- 22. 生图参数表
-- =====================================================
INSERT INTO `ai_image_parameter` VALUES (1, 'resolution', '1k', '1K(快速预览)', NULL, 1024, NULL, 1, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (2, 'resolution', '2k', '2K(高清)', NULL, 2048, NULL, 2, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (3, 'resolution', '4k', '4K(超清画质)', NULL, 4096, NULL, 3, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (4, 'quality', 'low', 'Low(快速/基础)', 'low', NULL, NULL, 1, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (5, 'quality', 'med', 'Medium(标准)', 'medium', NULL, NULL, 2, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (6, 'quality', 'high', 'High(高精细)', 'high', NULL, NULL, 3, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (7, 'aspect_ratio', '1:1', '1:1(方形)', NULL, 1, 1, 1, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (8, 'aspect_ratio', '16:9', '16:9(横屏)', NULL, 16, 9, 2, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (9, 'aspect_ratio', '9:16', '9:16(竖屏)', NULL, 9, 16, 3, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (10, 'aspect_ratio', '4:3', '4:3(标准)', NULL, 4, 3, 4, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (11, 'aspect_ratio', '3:4', '3:4(纵向)', NULL, 3, 4, 5, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (12, 'aspect_ratio', '3:2', '3:2(胶片)', NULL, 3, 2, 6, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (13, 'aspect_ratio', '2:3', '2:3(经典)', NULL, 2, 3, 7, 1, '2026-06-04 15:13:53', NULL, 0);
INSERT INTO `ai_image_parameter` VALUES (14, 'aspect_ratio', '21:9', '21:9(宽屏)', NULL, 21, 9, 8, 1, '2026-06-04 15:13:53', NULL, 0);

INSERT INTO `ai_prompt_sensitive_word_revision` (`id`, `revision`, `updated_at`)
VALUES (1, 1, '2026-08-11 00:00:00');

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
  'builtin', '2026-08-11', '2026-08-11 00:00:00', 0
FROM `tmp_ai_prompt_sensitive_word_seed`;

DROP TEMPORARY TABLE `tmp_ai_prompt_sensitive_word_seed`;

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

INSERT INTO `point_recharge_package`
(`package_code`, `name`, `description`, `points`, `repeat_points`, `price_amount`, `currency`, `validity_days`, `bonus_percent`, `badge_code`, `benefits_json`, `purchase_url`, `is_featured`, `sort`, `status`)
VALUES
('monthly', '特惠月卡', '立得 5000 积分', 5000, NULL, 29.90, 'CNY', 30, 0, 'recommended', '["立即到账 5000 点可用积分","赠 30 天专属尊贵会员标识","生图折合单价低至 ¥0.012","享受生成作品无水印导出"]', NULL, 1, 1, 1),
('trial', '首充体验包', '首充尝鲜，超低价体验生图', 200, 100, 1.00, 'CNY', NULL, 0, 'first_offer', '["首充到账 200 点永久可用积分","续充为 100 点永久可用积分","生图折合单价低至 ¥0.02","享受生成作品无水印导出"]', NULL, 0, 2, 1),
('basic', '基础套餐', '适合日常轻度创作用户', 1000, NULL, 10.00, 'CNY', NULL, 0, 'regular_choice', '["到账 1000 点永久可用积分","卡密永久有效无过期限制","生图折合单价低至 ¥0.02","享受生成作品无水印导出"]', NULL, 0, 3, 1),
('value', '超值套餐', '额外赠送 20%，高性价比', 3600, NULL, 30.00, 'CNY', NULL, 20, 'popular', '["到账 3600 点永久可用积分","包含额外加赠 20% 积分","生图折合单价低至 ¥0.016","享受生成作品无水印导出"]', NULL, 0, 4, 1);

INSERT INTO `ai_image_point_price` (`id`, `model_code`, `resolution_code`, `quality_code`, `points`, `price_amount`, `currency`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`) VALUES
(1, 'gpt-image-2', '1k', 'low', 10, 0.10, 'CNY', 1, 1, '2026-06-11 00:00:00', NULL, 0),
(2, 'gpt-image-2', '1k', 'med', 15, 0.15, 'CNY', 2, 1, '2026-06-11 00:00:00', NULL, 0),
(3, 'gpt-image-2', '1k', 'high', 20, 0.20, 'CNY', 3, 1, '2026-06-11 00:00:00', NULL, 0),
(4, 'gpt-image-2', '4k', 'low', 25, 0.25, 'CNY', 4, 1, '2026-06-11 00:00:00', NULL, 0),
(5, 'gpt-image-2', '4k', 'med', 35, 0.35, 'CNY', 5, 1, '2026-06-11 00:00:00', NULL, 0),
(6, 'gpt-image-2', '4k', 'high', 50, 0.50, 'CNY', 6, 1, '2026-06-11 00:00:00', NULL, 0),
(7, 'nano-banana-2', '1k', '', 60, 0.60, 'CNY', 7, 1, '2026-06-11 00:00:00', NULL, 0),
(8, 'nano-banana-2', '2k', '', 60, 0.60, 'CNY', 8, 1, '2026-06-11 00:00:00', NULL, 0),
(9, 'nano-banana-2', '4k', '', 60, 0.60, 'CNY', 9, 1, '2026-06-11 00:00:00', NULL, 0),
(10, 'nano-banana-pro', '1k', '', 80, 0.80, 'CNY', 10, 1, '2026-06-11 00:00:00', NULL, 0),
(11, 'nano-banana-pro', '2k', '', 80, 0.80, 'CNY', 11, 1, '2026-06-11 00:00:00', NULL, 0),
(12, 'nano-banana-pro', '4k', '', 80, 0.80, 'CNY', 12, 1, '2026-06-11 00:00:00', NULL, 0);

INSERT INTO `ai_image_model_config` (`id`, `model_code`, `model_name`, `provider`, `provider_model`, `resolution_code`, `route_role`, `base_url`, `api_key`, `text_to_image_path`, `image_to_image_path`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`) VALUES
(1, 'gpt-image-2', 'GPT Image 2 1K', 'openai-image', 'gpt-image-2-1k', '1k', 'fallback', 'https://api.dawclaudecode.com/v1', '', '/images/generations', '/images/edits', 1, 1, '2026-06-10 00:00:00', NULL, 0),
(2, 'gpt-image-2', 'GPT Image 2 4K', 'openai-image', 'gpt-image-2-4k', '4k', 'fallback', 'https://api.dawclaudecode.com/v1', '', '/images/generations', '/images/edits', 2, 1, '2026-06-10 00:00:00', NULL, 0),
(3, 'nano-banana-pro', 'Nano Banana Pro', 'gemini-image', 'gemini-3-pro-image-preview', '', 'primary', 'https://api.dawclaudecode.com', '', '/v1/models/{model}:generateContent', '/v1/models/{model}:generateContent', 3, 1, '2026-06-10 00:00:00', NULL, 0),
(4, 'nano-banana-2', 'Nano Banana 2', 'gemini-image', 'gemini-3.1-flash-image-preview', '', 'primary', 'https://api.dawclaudecode.com', '', '/v1/models/{model}:generateContent', '/v1/models/{model}:generateContent', 4, 1, '2026-06-10 00:00:00', NULL, 0),
(5, 'gpt-image-2', 'GPT Image 2 1K', 'openai-image', 'gpt-image-2', '1k', 'primary', '', '', '/images/generations', '/images/edits', 1, 0, '2026-08-11 00:00:00', NULL, 0),
(6, 'gpt-image-2', 'GPT Image 2 4K', 'openai-image', 'gpt-image-2', '4k', 'primary', '', '', '/images/generations', '/images/edits', 2, 0, '2026-08-11 00:00:00', NULL, 0);



SET FOREIGN_KEY_CHECKS = 1;

-- =====================================================
-- 22. 快速检查
-- =====================================================
SELECT * FROM `sys_site`;
SELECT * FROM `sys_role`;
SELECT * FROM `sys_user`;
SELECT * FROM `sys_menu`;
