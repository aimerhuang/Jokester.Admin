using jokester.admin.Application.DTOs.Menus;
using jokester.admin.Application.DTOs.Roles;
using jokester.admin.Application.DTOs.Sites;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Domain.Entities;
using Mapster;

namespace jokester.admin.Infrastructure.Mapping;

public static class MapsterRegistration
{
    public static void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SysUserEntity, UserListItemDto>();
        config.NewConfig<SaveSiteRequest, SysSiteEntity>();
        config.NewConfig<SysSiteEntity, SiteDto>();
        config.NewConfig<SaveRoleRequest, SysRoleEntity>();
        config.NewConfig<SysRoleEntity, RoleDto>();
        config.NewConfig<SaveMenuRequest, SysMenuEntity>();
        config.NewConfig<SysMenuEntity, MenuTreeNodeDto>();
    }
}
