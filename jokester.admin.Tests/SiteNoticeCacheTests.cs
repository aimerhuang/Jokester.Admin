using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Application.DTOs.Community;
using jokester.admin.Application.Services;

namespace jokester.admin.Tests;

public sealed class SiteNoticeCacheTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ConcurrentReadersShareLoadUntilExpiryOrInvalidation()
    {
        var clock = new Clock();
        var cache = new SiteNoticeCache(clock);
        var count = 0;
        async Task<AnnouncementSnapshot> Load() { count++; await Task.Yield(); return new([], null); }
        async Task Read() => await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetAnnouncementsAsync(Load, default)));
        await Read();
        clock.Now = clock.Now.AddSeconds(59);
        await Read();
        Assert.Equal(1, count);
        clock.Now = clock.Now.AddSeconds(1);
        await Read();
        Assert.Equal(2, count);
        cache.Invalidate();
        await Read();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task FailedLoadRetriesAndInvalidationWinsAgainstInFlightLoader()
    {
        var cache = new SiteNoticeCache(new Clock());
        await Assert.ThrowsAsync<IOException>(() => cache.GetCommunityAsync(() => throw new IOException(), default));
        var old = new TaskCompletionSource<QqGroupConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prior = cache.GetCommunityAsync(() => old.Task, default);
        cache.Invalidate();
        var fresh = new QqGroupConfig(true);
        Assert.Same(fresh, await cache.GetCommunityAsync(() => Task.FromResult(fresh), default));
        old.SetResult(new(false));
        Assert.False((await prior).Enabled);
        Assert.Same(fresh, await cache.GetCommunityAsync(() => throw new InvalidOperationException(), default));
    }
}
