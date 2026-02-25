using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TeamStorm.Metrics.Models;
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

        _httpClient.BaseAddress =
            new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<IReadOnlyList<WorkspaceDto>> GetWorkspacesAsync(CancellationToken cancellationToken)
    {
        //var request = BuildRequest(HttpMethod.Post, "cwm/rtc-service/com.ibm.team.process.internal.service.web.IProcessWebUIService/workspaces");
        var request = BuildRequest(
    HttpMethod.Post,
    "admin/api/v1/workspaces/get");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsListAsync<WorkspaceDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<FolderDto>> GetFoldersAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var request = BuildRequest(HttpMethod.Get, $"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/folders");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsListAsync<FolderDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(string workspaceId, string folderId, CancellationToken cancellationToken)
    {
        var url = $"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/folders/{Uri.EscapeDataString(folderId)}/workItems?maxItemsCount=100";
        var request = BuildRequest(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsListAsync<WorkItemDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEventDto>> GetWorkItemHistoryAsync(string workspaceId, string workItemId, CancellationToken cancellationToken)
    {
        var url = $"history/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workItems/{Uri.EscapeDataString(workItemId)}/history";
        var request = BuildRequest(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsListAsync<HistoryEventDto>(response, cancellationToken);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);

        if (!string.IsNullOrWhiteSpace(_options.SessionToken))
        {
            request.Headers.Add("Cookie", $"session={_options.SessionToken}");
        }

        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json"
            );
        }

        return request;
    }

    private static async Task<IReadOnlyList<T>> ReadAsListAsync<T>(
    HttpResponseMessage response,
    CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Storm API error {(int)response.StatusCode}: {text}");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var document = JsonDocument.Parse(text);

        var root = document.RootElement;

        // Если пришёл массив напрямую
        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<T>>(text, options) ?? [];
        }

        // Если пришёл объект
        if (root.ValueKind == JsonValueKind.Object)
        {
            // ищем первый массив внутри объекта
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<T>>(
                        property.Value.GetRawText(),
                        options) ?? [];
                }
            }

            // если массивов нет — значит это одиночный объект
            var single = JsonSerializer.Deserialize<T>(text, options);
            if (single != null)
                return [single];
        }

        return [];
    }
}
