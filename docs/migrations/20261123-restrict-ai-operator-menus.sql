-- Restrict the default registered-user role `ai_operator` to AI-site menus.
-- See docs/user-admin-prd.md section 9. Keep jokester.admin.sql aligned.
--
-- Why: `RegistrationService` assigns `ai_operator` to every registered user, and the init script
-- granted that role every menu of every site (the grant joined `sys_menu` without a site filter).
-- Registered users therefore held `System.*` and `Blog.*` permission codes: user, role, menu, site
-- and log management, plus blog article/media/comment management.
--
-- Deploy order: deploy the API build that removes `[Permission("System.User.Update")]` from the
-- self-service endpoints (password / avatar / signature) BEFORE or TOGETHER WITH this migration.
-- With the old API, revoking `System.User.Update` stops users from changing their own password.
--
-- After applying, drop the permission cache or wait up to 2 hours for it to expire:
--   redis-cli --scan --pattern '<Redis.InstanceName>perm:*' | xargs -r redis-cli del
--
-- Idempotent: re-running deletes nothing.

USE `jokester.admin`;

-- 1) Review what will be revoked. No rows means the database is not affected.
SELECT s.`site_code`, m.`menu_code`, m.`permission_code`
FROM `sys_role` r
JOIN `sys_role_menu` rm ON rm.`role_id` = r.`id`
JOIN `sys_menu` m ON m.`id` = rm.`menu_id`
JOIN `sys_site` s ON s.`id` = m.`site_id`
WHERE r.`role_code` = 'ai_operator'
  AND s.`site_code` <> 'ai_image'
ORDER BY s.`site_code`, m.`menu_code`;

-- 2) Review accounts that may rely on those grants on purpose: non-super-admin `ai_operator`
--    users bound to a site other than `ai_image`. Give such accounts a dedicated role
--    (for example `blog_admin`) before applying step 3.
SELECT u.`id`, u.`user_name`, s.`site_code`
FROM `sys_user` u
JOIN `sys_user_role` ur ON ur.`user_id` = u.`id`
JOIN `sys_role` r ON r.`id` = ur.`role_id` AND r.`role_code` = 'ai_operator'
JOIN `sys_user_site` us ON us.`user_id` = u.`id`
JOIN `sys_site` s ON s.`id` = us.`site_id` AND s.`site_code` <> 'ai_image'
WHERE u.`is_deleted` = 0
  AND u.`is_super_admin` = 0
ORDER BY u.`id`;

-- 3) Revoke every non-AI-site menu from `ai_operator`.
DELETE rm
FROM `sys_role_menu` rm
JOIN `sys_role` r ON r.`id` = rm.`role_id`
JOIN `sys_menu` m ON m.`id` = rm.`menu_id`
JOIN `sys_site` s ON s.`id` = m.`site_id`
WHERE r.`role_code` = 'ai_operator'
  AND s.`site_code` <> 'ai_image';
