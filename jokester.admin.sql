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
DROP TABLE IF EXISTS `ai_image_favorite`;
DROP TABLE IF EXISTS `ai_image_task`;
DROP TABLE IF EXISTS `ai_image_model_config`;
DROP TABLE IF EXISTS `ai_image_point_price`;
DROP TABLE IF EXISTS `ai_image_parameter`;
DROP TABLE IF EXISTS `blog_comment`;
DROP TABLE IF EXISTS `blog_article_media`;
DROP TABLE IF EXISTS `blog_media`;
DROP TABLE IF EXISTS `blog_article`;
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
  `remark` VARCHAR(500) DEFAULT NULL COMMENT '备注',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  PRIMARY KEY (`id`),
  KEY `idx_sys_user_point_detail_user_created` (`user_id`, `created_at`),
  CONSTRAINT `fk_sys_user_point_detail_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='用户积分明细表';

-- =====================================================
-- 9. 博客文章表
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
  `resolution_code` VARCHAR(50) DEFAULT NULL COMMENT '分辨率档位编码，空表示不区分分辨率',
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
  UNIQUE KEY `uk_ai_image_model_config_code_resolution` (`model_code`, `resolution_code`, `provider_model`),
  KEY `idx_ai_image_model_config_code_status_sort` (`model_code`, `status`, `sort`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI生图模型配置表';

-- =====================================================
-- 12. GPT生图任务表
-- =====================================================
CREATE TABLE `ai_image_task` (
  `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键ID',
  `site_id` BIGINT NOT NULL COMMENT '站点ID',
  `user_id` BIGINT NOT NULL COMMENT '用户ID',
  `prompt` VARCHAR(2000) NOT NULL COMMENT '生图提示词',
  `negative_prompt` VARCHAR(2000) DEFAULT NULL COMMENT '反向提示词',
  `model_name` VARCHAR(100) DEFAULT NULL COMMENT '模型名称',
  `image_count` INT NOT NULL DEFAULT 1 COMMENT '图片数量',
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
  `status` TINYINT NOT NULL DEFAULT 0 COMMENT '状态：0待处理 1成功 2失败',
  `error_message` VARCHAR(1000) DEFAULT NULL COMMENT '错误信息',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME DEFAULT NULL COMMENT '更新时间',
  `is_deleted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '逻辑删除：1已删除 0未删除',
  PRIMARY KEY (`id`),
  KEY `idx_ai_image_task_site_status` (`site_id`, `status`),
  KEY `idx_ai_image_task_user_id` (`user_id`),
  KEY `idx_ai_image_task_created_at` (`created_at`),
  CONSTRAINT `fk_ai_image_task_site` FOREIGN KEY (`site_id`) REFERENCES `sys_site` (`id`),
  CONSTRAINT `fk_ai_image_task_user` FOREIGN KEY (`user_id`) REFERENCES `sys_user` (`id`)
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
-- 13. 初始化角色数据
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
(@ai_site_id, @ai_generate_page_id, '删除记录', 'ai_image_record_delete', 3, NULL, NULL, 'AiImage.Record.Delete', NULL, 4, 1, 1, 0, 0, '删除记录权限');

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
JOIN `sys_menu` m ON m.`site_id` = @ai_site_id
WHERE r.`role_code` = 'ai_operator';

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

INSERT INTO `ai_image_model_config` (`id`, `model_code`, `model_name`, `provider`, `provider_model`, `resolution_code`, `base_url`, `api_key`, `text_to_image_path`, `image_to_image_path`, `sort`, `status`, `created_at`, `updated_at`, `is_deleted`) VALUES
(1, 'gpt-image-2', 'GPT Image 2 1K', 'openai-image', 'gpt-image-2-1k', '1k', 'https://api.dawclaudecode.com/v1', '', '/images/generations', '/images/edits', 1, 1, '2026-06-10 00:00:00', NULL, 0),
(2, 'gpt-image-2', 'GPT Image 2 4K', 'openai-image', 'gpt-image-2-4k', '4k', 'https://api.dawclaudecode.com/v1', '', '/images/generations', '/images/edits', 2, 1, '2026-06-10 00:00:00', NULL, 0),
(3, 'nano-banana-pro', 'Nano Banana Pro', 'gemini-image', 'gemini-3-pro-image-preview', NULL, 'https://api.dawclaudecode.com', '', '/v1/models/{model}:generateContent', '/v1/models/{model}:generateContent', 3, 1, '2026-06-10 00:00:00', NULL, 0),
(4, 'nano-banana-2', 'Nano Banana 2', 'gemini-image', 'gemini-3.1-flash-image-preview', NULL, 'https://api.dawclaudecode.com', '', '/v1/models/{model}:generateContent', '/v1/models/{model}:generateContent', 4, 1, '2026-06-10 00:00:00', NULL, 0);



SET FOREIGN_KEY_CHECKS = 1;

-- =====================================================
-- 22. 快速检查
-- =====================================================
SELECT * FROM `sys_site`;
SELECT * FROM `sys_role`;
SELECT * FROM `sys_user`;
SELECT * FROM `sys_menu`;
