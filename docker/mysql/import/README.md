放入 `*.sql`（例如 mysqldump 导出的现有库）后，**首次**创建 MySQL 数据卷时会按文件名顺序导入，
并跳过 `jokester.admin.sql`。本目录中的 `*.sql` 不会提交到仓库。

已有数据卷不会重复导入；需要重新导入时执行 `.\jokester.ps1 reset` 后再 `up`。
