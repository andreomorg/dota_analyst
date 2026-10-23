using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

public sealed class MaintenanceApiTests
{
    [Theory]
    [InlineData("replays/0/refresh", true)]
    [InlineData("replays/0/prefetch", true)]
    [InlineData("replay-facts/0", false)]
    [InlineData("gold-facts/0", false)]
    public async Task SwitchesAndKeysAreIndependent(string route, bool post)
    {
        const string maintenanceKey = "maintenance-test-key";
        const string coachKey = "coach-test-machine-key";
        foreach (var (maintenance, coach, key, expected) in new[]
        {
            (false, false, maintenanceKey, HttpStatusCode.NotFound),
            (true, false, "", HttpStatusCode.Unauthorized),
            (true, false, coachKey, HttpStatusCode.Unauthorized),
            (false, true, maintenanceKey, HttpStatusCode.Unauthorized),
            (true, false, maintenanceKey, HttpStatusCode.BadRequest),
            (false, true, coachKey, HttpStatusCode.BadRequest),
            (true, true, maintenanceKey, HttpStatusCode.BadRequest),
            (true, true, coachKey, HttpStatusCode.BadRequest),
        })
        {
            using var factory = new ApiFactory(new Dictionary<string, string?>
            {
                ["Maintenance:Enabled"] = maintenance.ToString(),
                ["Maintenance:ApiKey"] = maintenanceKey,
                ["CoachMl:Enabled"] = coach.ToString(),
                ["CoachMl:ApiKey"] = coachKey,
                ["CoachMl:BaseUrl"] = "http://coach.invalid/",
            });
            using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                IsolateKeys(services)));
            using var client = isolated.CreateClient();
            if (key.Length > 0) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = post ? await client.PostAsync("/api/internal/" + route, null)
                : await client.GetAsync("/api/internal/" + route);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("               ")]
    public void EnabledRequiresAKeyAtStartup(string key)
    {
        using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["Maintenance:Enabled"] = "true",
            ["Maintenance:ApiKey"] = key,
        });
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            IsolateKeys(services)));
        Assert.Throws<OptionsValidationException>(() => isolated.CreateClient());
    }
    // Key-ring warmup also needs isolation: it runs even with an ephemeral protection provider.
    private static void IsolateKeys(IServiceCollection services)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new MemoryKeys();
            options.XmlEncryptor = null;
        });
    }

    private sealed class MemoryKeys : IXmlRepository
    {
        private readonly List<XElement> _elements = [];
        public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }
}
