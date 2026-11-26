using jokester.admin.Application.DTOs.Community;

namespace jokester.admin.Application.Abstractions;

public interface ICommunitySettingService
{
    Task<QqGroupConfig> GetAsync(CancellationToken ct);
    Task<AdminQqGroupConfig> GetAdminAsync(CancellationToken ct);
    Task<AdminQqGroupConfig> SaveAsync(SaveQqGroupConfig request, CancellationToken ct);
}
