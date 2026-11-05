-- Configure Web/Android external purchase links for the four recharge packages.
-- Later link changes only need an UPDATE on point_recharge_package.purchase_url; no code release required.
SET NAMES utf8mb4;

UPDATE `point_recharge_package` SET `purchase_url` = 'https://wzyp.cn/item/r1bw3i' WHERE `package_code` = 'monthly'; -- 5000 积分
UPDATE `point_recharge_package` SET `purchase_url` = 'https://wzyp.cn/item/jxmbco' WHERE `package_code` = 'trial';   -- 200 积分
UPDATE `point_recharge_package` SET `purchase_url` = 'https://wzyp.cn/item/w0yeju' WHERE `package_code` = 'basic';   -- 1000 积分
UPDATE `point_recharge_package` SET `purchase_url` = 'https://wzyp.cn/item/9c22yo' WHERE `package_code` = 'value';   -- 3600 积分
