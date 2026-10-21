using System.Security.Cryptography;
using System.Text;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Http;

namespace DotaAnalyst.Server.Controllers.Internal;

// The machine key of the internal routes (CoachMl:ApiKey): the Python worker sends it as a Bearer token.
internal static class MachineKey
{
    // Compared in constant time, so the response time does not reveal how much of the key matched.
    public static bool IsValid(HttpRequest request, CoachMlOptions options)
    {
        var received = Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString());
        var expected = Encoding.UTF8.GetBytes("Bearer " + options.ApiKey);
        return CryptographicOperations.FixedTimeEquals(received, expected);
    }
}
