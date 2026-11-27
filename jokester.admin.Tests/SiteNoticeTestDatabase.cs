using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Tests;

internal static class SiteNoticeTestDatabase
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static SqlSugarClient Open()
    {
        SQLitePCL.Batteries_V2.Init();
        var nullability = new NullabilityInfoContext();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:", DbType = DbType.Sqlite, IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute, ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    column.IsNullable = nullability.Create(property).WriteState == NullabilityState.Nullable;
                    if (column.IsIdentity) column.DataType = "INTEGER";
                }
            }
        });
        db.CodeFirst.InitTables(typeof(SiteAnnouncementEntity), typeof(SiteCommunitySettingEntity), typeof(SysUserEntity));
        db.Insertable(new SiteCommunitySettingEntity { Id = 1, PromptVersion = 1, RemindIntervalDays = 3 }).ExecuteCommand();
        return db;
    }
}
