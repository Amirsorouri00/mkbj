using System.Net.Http.Headers;

namespace EwaysVNext.IntegrationTests.Support;

internal static class BearerClientExtensions
{
    public static Task<HttpResponseMessage> GetWithBearerAsync(
        this HttpClient client, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client.SendAsync(request);
    }
}
