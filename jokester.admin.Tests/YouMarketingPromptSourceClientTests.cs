using System.Net;
using System.Text;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.PromptLibrary;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace jokester.admin.Tests;

public sealed class YouMarketingPromptSourceClientTests
{
    [Fact]
    public async Task FetchSnapshotAsync_ParsesOfficialChineseReadmeStructureAndStableIds()
    {
        var handler = new RecordingHandler((_, _) => TextResponse(TwoItemChineseReadme));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 2, sourceApiToken: "source-token");

        var snapshot = await client.FetchSnapshotAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.CandidateCount);
        Assert.Equal(0, snapshot.SkippedCount);
        Assert.Collection(
            snapshot.Items,
            item =>
            {
                Assert.Equal("13460", item.StableId);
                Assert.Equal(1, item.ExternalNo);
                Assert.Equal(1, item.SourcePosition);
                Assert.Equal("VR 头显爆炸视图海报", item.Title);
                Assert.Equal("zh-CN", item.Language);
                Assert.Equal("https://cms-assets.youmind.com/media/vr.jpg", item.CoverSourceUrl);
                Assert.Equal("示例作者", item.AuthorName);
                Assert.Equal("https://x.com/example/status/1", item.SourceUrl);
            },
            item =>
            {
                Assert.Equal("20888", item.StableId);
                Assert.Equal(1, item.ExternalNo);
                Assert.Equal(2, item.SourcePosition);
                Assert.Equal("手绘城市美食地图", item.Title);
                Assert.Equal("zh-CN", item.Language);
            });
        Assert.Equal(64, snapshot.ContentHash.Length);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://source.example/README_zh.md", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("source-token", request.AuthorizationParameter);
        Assert.Contains("zh-CN", request.AcceptLanguage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchSnapshotAsync_RejectsEnglishFallbackContent()
    {
        var markdown = TwoItemChineseReadme + """

            ### No. 2: 个人资料 - English fallback title

            #### 📖 描述

            This description is still English.

            #### 📝 提示词

            ```
            Create a studio portrait with soft light and a white background.
            ```

            #### 🖼️ 生成图片

            <img src="https://cms-assets.youmind.com/media/english.jpg" width="600">

            **[立即尝试](https://youmind.com/zh-CN/gpt-image-2-prompts?id=30000)**
            """;
        var handler = new RecordingHandler((_, _) => TextResponse(markdown));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 10);

        var snapshot = await client.FetchSnapshotAsync(CancellationToken.None);

        Assert.Equal(3, snapshot.CandidateCount);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(1, snapshot.SkippedCount);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Contains("must_all_be_substantially_chinese", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchSnapshotAsync_AllowsJapaneseDisplayTextInsideChineseInstructions()
    {
        const string markdown = """
            ### No. 8: 日语旅行海报

            #### 📖 描述

            生成一张以东京早晨街道为主题的中文设计说明海报。

            #### 📝 提示词

            ```
            请生成一张清晨东京街道海报，主标题使用日文「おはようございます」，其余说明文字使用简体中文，画面保持清晰、温暖且易读。
            ```

            #### 🖼️ 生成图片

            <img src="https://cms-assets.youmind.com/media/tokyo.jpg" width="600">

            **[立即尝试](https://youmind.com/zh-CN/gpt-image-2-prompts?id=30001)**
            """;
        var handler = new RecordingHandler((_, _) => TextResponse(markdown));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 1);

        var snapshot = await client.FetchSnapshotAsync(CancellationToken.None);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("30001", item.StableId);
        Assert.Contains("おはようございます", item.PromptText, StringComparison.Ordinal);
        Assert.Equal("zh-CN", item.Language);
    }

    [Fact]
    public async Task FetchSnapshotAsync_SkipsDuplicateStableId()
    {
        var duplicate = TwoItemChineseReadme.Replace("id=20888", "id=13460", StringComparison.Ordinal);
        var handler = new RecordingHandler((_, _) => TextResponse(duplicate));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 10);

        var snapshot = await client.FetchSnapshotAsync(CancellationToken.None);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("13460", item.StableId);
        Assert.Equal(1, snapshot.SkippedCount);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Contains("duplicate_id:13460", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchSnapshotAsync_RetriesTransientServerFailure()
    {
        var handler = new RecordingHandler((requestNumber, _) => requestNumber == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : TextResponse(TwoItemChineseReadme));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 2, retryCount: 1);

        var snapshot = await client.FetchSnapshotAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchSnapshotAsync_DoesNotRetryPermanentClientFailure()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 2, retryCount: 3);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.FetchSnapshotAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchSnapshotAsync_RejectsNonTextResponse()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
            {
                Headers = { ContentType = new("application/json") }
            }
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, targetCount: 1);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.FetchSnapshotAsync(CancellationToken.None));

        Assert.Contains("non-text", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static YouMarketingPromptSourceClient CreateClient(
        HttpClient httpClient,
        int targetCount,
        int retryCount = 0,
        string? sourceApiToken = null)
    {
        var options = Options.Create(new PromptLibraryOptions
        {
            Enabled = true,
            SourceApiUrl = "https://source.example/README_zh.md",
            SourceApiToken = sourceApiToken,
            TargetCount = targetCount,
            RetryCount = retryCount,
            ImageAllowedHosts = ["cms-assets.youmind.com", "marketing-assets.youmind.com"]
        });
        return new YouMarketingPromptSourceClient(
            httpClient,
            new MarkdigPromptReadmeParser(),
            options,
            NullLogger<YouMarketingPromptSourceClient>.Instance);
    }

    private static HttpResponseMessage TextResponse(string markdown) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(markdown, Encoding.UTF8, "text/plain")
    };

    private const string TwoItemChineseReadme = """
        ## 精选提示词

        ### No. 1: VR 头显爆炸视图海报

        ![Language-EN](https://img.shields.io/badge/Language-EN-blue)

        #### 📖 描述

        生成一张高科技头显爆炸视图，包含详细的组件标注和中文宣传文案。

        #### 📝 提示词

        ```json
        {
          "type": "产品爆炸视图海报",
          "subject": "VR 头显",
          "style": "简洁的高科技三维渲染，摄影棚灯光，展示外壳、镜片、传感器和电池组件"
        }
        ```

        #### 🖼️ 生成图片

        ##### Image 1

        <div align="center">
        <img src="https://cms-assets.youmind.com/media/vr.jpg" width="700" alt="VR 头显爆炸视图海报">
        </div>

        #### 📌 详情

        - **作者:** [示例作者](https://x.com/example)
        - **来源:** [Twitter Post](https://x.com/example/status/1)
        - **发布时间:** 2026年4月19日
        - **多语言:** en

        **[立即尝试](https://youmind.com/zh-CN/gpt-image-2-prompts?id=13460)**

        ---

        ## 所有提示词

        ### No. 1: 手绘城市美食地图

        #### 📖 描述

        生成一张手绘水彩风格的旅游地图，包含编号的当地特色美食、地标建筑及图例。

        #### 📝 提示词

        ```
        请绘制一张复古羊皮纸质感的中文城市美食地图，使用水彩和墨线，清楚标注景点、道路、河流与十二种地方美食。
        ```

        #### 🖼️ 生成图片

        <img src="https://marketing-assets.youmind.com/media/map.webp" width="600">

        #### 📌 详情

        - **作者：** 城市地图设计师
        - **多语言：** zh

        **[立即尝试](https://youmind.com/zh-CN/gpt-image-2-prompts?id=20888)**
        """;

    private sealed class RecordingHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int requestCount;

        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref requestCount);
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                string.Join(",", request.Headers.AcceptLanguage.Select(value => value.ToString()))));
            return Task.FromResult(responseFactory(current, request));
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string AcceptLanguage);
}
