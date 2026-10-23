using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace DotaAnalyst.Server.Controllers.Internal;

// Internal clients send the configured machine key as a Bearer token.
internal static class MachineKey
{
    // Compared in constant time, so the response time does not reveal how much of the key matched.
    public static bool IsValid(HttpRequest request, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var received = Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString());
        var expected = Encoding.UTF8.GetBytes("Bearer " + key);
        return CryptographicOperations.FixedTimeEquals(received, expected);
    }
}
