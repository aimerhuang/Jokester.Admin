using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Any;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace jokester.admin;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Jokester Admin API",
                Version = "v1",
                Description = "博客接口固定归属 siteCode=blog。评论公开提交需先获取验证码；后台评论审核、删除和仪表盘统计需要 JWT 与权限码。"
            });

            var xmlPath = Path.Combine(AppContext.BaseDirectory, "jokester.admin.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "输入 JWT，例如：Bearer {token}"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            options.MapType<Application.DTOs.Users.SaveUserRequest>(() => new OpenApiSchema
            {
                Type = "object",
                Required = new HashSet<string> { "userName", "nickName", "password", "status", "isSuperAdmin", "roleIds", "siteIds" },
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["userName"] = new() { Type = "string", Description = "登录用户名" },
                    ["nickName"] = new() { Type = "string", Description = "显示昵称" },
                    ["password"] = new() { Type = "string", Description = "明文密码。新增必填。" },
                    ["email"] = new() { Type = "string", Nullable = true, Description = "邮箱" },
                    ["phone"] = new() { Type = "string", Nullable = true, Description = "手机号" },
                    ["status"] = new() { Type = "integer", Format = "int32", Description = "1=启用，0=禁用" },
                    ["avatarUrl"] = new() { Type = "string", Nullable = true, Description = "头像地址" },
                    ["remark"] = new() { Type = "string", Nullable = true, Description = "备注" },
                    ["isSuperAdmin"] = new() { Type = "boolean", Description = "是否超级管理员" },
                    ["roleIds"] = new() { Type = "array", Items = new OpenApiSchema { Type = "integer", Format = "int64" }, Description = "角色 ID 列表" },
                    ["siteIds"] = new() { Type = "array", Items = new OpenApiSchema { Type = "integer", Format = "int64" }, Description = "站点 ID 列表" }
                },
                Example = new OpenApiObject
                {
                    ["userName"] = new OpenApiString("editor01"),
                    ["nickName"] = new OpenApiString("内容编辑"),
                    ["password"] = new OpenApiString("ChangeMe123!"),
                    ["email"] = new OpenApiString("editor01@example.com"),
                    ["phone"] = new OpenApiString("13800000000"),
                    ["status"] = new OpenApiInteger(1),
                    ["avatarUrl"] = new OpenApiString("https://cdn.example.com/avatar/editor01.png"),
                    ["remark"] = new OpenApiString("博客内容编辑用户"),
                    ["isSuperAdmin"] = new OpenApiBoolean(false),
                    ["roleIds"] = new OpenApiArray { new OpenApiLong(2) },
                    ["siteIds"] = new OpenApiArray { new OpenApiLong(1) }
                }
            });

            options.MapType<Application.DTOs.Blog.CreateBlogCommentRequest>(() => new OpenApiSchema
            {
                Type = "object",
                Required = new HashSet<string> { "articleId", "content", "captchaId", "captchaAnswer" },
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["articleId"] = new() { Type = "integer", Format = "int64", Description = "文章 ID" },
                    ["parentId"] = new() { Type = "integer", Format = "int64", Nullable = true, Description = "父评论 ID，空表示一级评论" },
                    ["authorName"] = new() { Type = "string", Description = "评论者昵称" },
                    ["authorEmail"] = new() { Type = "string", Nullable = true, Description = "评论者邮箱" },
                    ["authorWebsite"] = new() { Type = "string", Nullable = true, Description = "评论者网站" },
                    ["content"] = new() { Type = "string", Description = "评论内容" },
                    ["captchaId"] = new() { Type = "string", Description = "GET /api/blog/comments/captcha 返回的验证码 ID" },
                    ["captchaAnswer"] = new() { Type = "string", Description = "验证码答案" }
                },
                Example = new OpenApiObject
                {
                    ["articleId"] = new OpenApiLong(1),
                    ["parentId"] = new OpenApiNull(),
                    ["authorName"] = new OpenApiString("读者"),
                    ["authorEmail"] = new OpenApiString("reader@example.com"),
                    ["authorWebsite"] = new OpenApiString("https://example.com"),
                    ["content"] = new OpenApiString("文章写得很好。"),
                    ["captchaId"] = new OpenApiString("captcha-id-from-api"),
                    ["captchaAnswer"] = new OpenApiString("8")
                }
            });

            options.MapType<Application.DTOs.Blog.ReviewBlogCommentRequest>(() => new OpenApiSchema
            {
                Type = "object",
                Required = new HashSet<string> { "status" },
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["status"] = new() { Type = "integer", Format = "int32", Description = "审核状态：1=已通过，2=已拒绝，3=垃圾评论" }
                },
                Example = new OpenApiObject
                {
                    ["status"] = new OpenApiInteger(1)
                }
            });
        });

        return services;
    }
}
