using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Application.DTOs.Community;

namespace jokester.admin.Application.Services;

public sealed class SiteNoticeCache(TimeProvider clock)
{
    private volatile Entry<AnnouncementSnapshot> announcements = new();
    private volatile Entry<QqGroupConfig> community = new();

    public Task<AnnouncementSnapshot> GetAnnouncementsAsync(Func<Task<AnnouncementSnapshot>> loader, CancellationToken ct) =>
        announcements.GetAsync(loader, clock, ct);

    public Task<QqGroupConfig> GetCommunityAsync(Func<Task<QqGroupConfig>> loader, CancellationToken ct) =>
        community.GetAsync(loader, clock, ct);

    public void Invalidate()
    {
        // A load started before commit can only populate its old entry.
        announcements = new();
        community = new();
    }

    private sealed class Entry<T> where T : class
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private T? value;
        private DateTimeOffset expiresAt;

        public async Task<T> GetAsync(Func<Task<T>> loader, TimeProvider clock, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                var now = clock.GetUtcNow();
                if (value is not null && now < expiresAt) return value;
                value = await loader();
                expiresAt = now.AddSeconds(60);
                return value;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
