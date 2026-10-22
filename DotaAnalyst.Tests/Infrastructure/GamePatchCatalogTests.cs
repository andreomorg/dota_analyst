using System.Net;
using System.Text;
using DotaAnalyst.Infrastructure.Caching;
using DotaAnalyst.Infrastructure.GameData;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class GamePatchCatalogTests
{
    [Fact]
    public void Embedded_HasThe741SubPatchesInOrder()
    {
        var patches = PatchNotesList.Embedded;

        Assert.Equal("7.41", patches.Single(patch => patch.Name == "7.41").Major);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1774335600), patches.Single(patch => patch.Name == "7.41").ReleasedAt);
        Assert.Equal(new[] { "7.41", "7.41a", "7.41b", "7.41c", "7.41d", "7.41e", "7.41f" },
            patches.Where(patch => patch.Major == "7.41").Select(patch => patch.Name).ToArray());
        Assert.Equal(patches.OrderBy(patch => patch.ReleasedAt), patches);
    }

    [Fact]
    public async Task Valve_LiveListIsKept()
    {
        var handler = new Answer(HttpStatusCode.OK, """{"patches":[{"patch_number":"7.42","patch_name":"7.42","patch_timestamp":1800000000}],"success":true}""");
        var catalog = Catalog(handler);

        var first = await catalog.GetPatchesAsync(CancellationToken.None);
        var second = await catalog.GetPatchesAsync(CancellationToken.None);

        Assert.Equal("7.42", Assert.Single(first).Name);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "<html>maintenance</html>")]
    [InlineData(HttpStatusCode.OK, """{"patches":[]}""")]
    public async Task Valve_FailingGivesTheKeptCopy(HttpStatusCode status, string body)
    {
        var patches = await Catalog(new Answer(status, body)).GetPatchesAsync(CancellationToken.None);

        Assert.Same(PatchNotesList.Embedded, patches);
    }

    private static ValvePatchCatalog Catalog(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheEntries.SizeLimit }),
            NullLogger<ValvePatchCatalog>.Instance);

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
