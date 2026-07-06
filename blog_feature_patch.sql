-- 博客功能增量脚本
-- 适用于已有数据库的增量部署：创建博客分类表、博客站点配置表，并初始化默认数据

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

SET @blog_site_id = (
  SELECT `id`
  FROM `sys_site`
  WHERE `site_code` = 'blog' AND `is_deleted` = 0
  LIMIT 1
);

-- =====================================================
-- 博客分类表
-- =====================================================
CREATE TABLE IF NOT EXISTS `blog_category` (
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
-- 博客站点配置表
-- =====================================================
CREATE TABLE IF NOT EXISTS `blog_site_config` (
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
-- 初始化默认分类
-- =====================================================
INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '技术教程', 1, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
  AND NOT EXISTS (
    SELECT 1
    FROM `blog_category` c
    WHERE c.`site_id` = @blog_site_id
      AND c.`name` = '技术教程'
      AND c.`is_deleted` = 0
  )
LIMIT 1;

INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '日常笔记', 2, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
  AND NOT EXISTS (
    SELECT 1
    FROM `blog_category` c
    WHERE c.`site_id` = @blog_site_id
      AND c.`name` = '日常笔记'
      AND c.`is_deleted` = 0
  )
LIMIT 1;

INSERT INTO `blog_category` (`site_id`, `name`, `sort`, `created_by`)
SELECT @blog_site_id, '好物分享', 3, u.`id`
FROM `sys_user` u
WHERE u.`user_name` = 'admin'
  AND NOT EXISTS (
    SELECT 1
    FROM `blog_category` c
    WHERE c.`site_id` = @blog_site_id
      AND c.`name` = '好物分享'
      AND c.`is_deleted` = 0
  )
LIMIT 1;

-- =====================================================
-- 初始化博客建站时间
-- =====================================================
INSERT INTO `blog_site_config` (`site_id`, `build_date`, `created_at`)
SELECT @blog_site_id, '2026-06-01 00:00:00', NOW()
WHERE @blog_site_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM `blog_site_config` c
    WHERE c.`site_id` = @blog_site_id
      AND c.`is_deleted` = 0
  );

SET FOREIGN_KEY_CHECKS = 1;
