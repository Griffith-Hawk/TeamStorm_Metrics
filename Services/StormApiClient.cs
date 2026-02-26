using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TeamStorm.Metrics.Options;

namespace TeamStorm.Metrics.Services;

public sealed class StormApiClient : IStormApiClient
{
    private readonly HttpClient _httpClient;
    private readonly StormOptions _options;

    public StormApiClient(HttpClient httpClient, IOptions<StormOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + '/');
    }

    public Task<JsonElement> PostAsync(string relativePath, object? payload, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, relativePath, payload, cancellationToken);

    public Task<JsonElement> GetAsync(string relativePath, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, relativePath, null, cancellationToken);

    public async Task<JsonElement> SendWorkItemUpdateAsync(string workspaceId, string workitemId, int originalEstimateSeconds, string? folderId, CancellationToken cancellationToken)
    {
        var payload = new { originalEstimate = originalEstimateSeconds };
        var basePath = "cwm/public/api/v1";

        var urls = new List<string>
        {
            $"{basePath}/workspaces/{Uri.EscapeDataString(workspaceId)}/workitems/{Uri.EscapeDataString(workitemId)}",
            $"{basePath}/workspaces/{Uri.EscapeDataString(workspaceId)}/nodes/{Uri.EscapeDataString(workitemId)}"
        };

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            urls.Insert(0, $"{basePath}/workspaces/{Uri.EscapeDataString(workspaceId)}/folders/{Uri.EscapeDataString(folderId)}/workitems/{Uri.EscapeDataString(workitemId)}");
        }

        Exception? last = null;
        foreach (var url in urls)
        {
            foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Put })
            {
                try
                {
                    return await SendAsync(method, url, payload, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    last = ex;
                    if (!ex.Message.Contains("404")) break;
                }
            }
        }

        throw last ?? new InvalidOperationException("Storm workitem update failed");
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string relativePath, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        ApplyAuth(request);

        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Storm API error {(int)response.StatusCode}: {text}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("PrivateToken", _options.ApiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.SessionToken))
        {
            request.Headers.Add("Cookie", $"session={_options.SessionToken}");
            return;
        }

        throw new InvalidOperationException("STORM auth not configured. Set Storm:ApiToken or Storm:SessionToken.");
    }
}
