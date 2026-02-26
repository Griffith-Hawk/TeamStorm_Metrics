using System.Text.Json;
using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public sealed class SprintAnalyticsService : ISprintAnalyticsService
{
    private readonly IStormApiClient _api;
    private readonly IWorkItemMetricsService _metrics;
    private readonly IEncryptedCacheService _cache;

    public SprintAnalyticsService(IStormApiClient api, IWorkItemMetricsService metrics, IEncryptedCacheService cache)
    {
        _api = api;
        _metrics = metrics;
        _cache = cache;
    }

    public Task<ProjectAnalyticsDto> GetProjectAnalyticsAsync(string workspaceId, string? workspaceName, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync($"project::{workspaceId}", TimeSpan.FromMinutes(20), async () =>
        {
            var foldersJson = await _api.GetAsync($"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/folders", cancellationToken);
            var folders = AsArray(foldersJson);
            var sprints = new List<SprintAnalyticsDto>();

            foreach (var folder in folders)
            {
                var folderId = ReadString(folder, "id") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(folderId)) continue;

                var folderName = ReadString(folder, "name") ?? "Folder";
                var sprintJson = await _api.GetAsync($"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/sprints?folderId={Uri.EscapeDataString(folderId)}&maxItemsCount=100", cancellationToken);
                foreach (var sprint in AsArray(sprintJson))
                {
                    var sprintId = ReadString(sprint, "id") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sprintId)) continue;

                    var analytics = await BuildSprintAnalyticsAsync(workspaceId, folderId, folderName, sprintId, ReadString(sprint, "name") ?? "Sprint", ReadString(sprint, "state") ?? ReadString(sprint, "status") ?? "unknown", cancellationToken);
                    sprints.Add(analytics);
                }
            }

            return new ProjectAnalyticsDto
            {
                WorkspaceId = workspaceId,
                WorkspaceName = workspaceName ?? workspaceId,
                GeneratedAt = DateTimeOffset.UtcNow,
                Sprints = sprints.OrderByDescending(x => x.SprintName).ToList()
            };
        }, cancellationToken);

    public Task<SprintAnalyticsDto> GetSprintAnalyticsAsync(string workspaceId, string folderId, string sprintId, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync($"sprint::{workspaceId}::{folderId}::{sprintId}", TimeSpan.FromMinutes(15), async () =>
        {
            var sprintJson = await _api.GetAsync($"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/sprints?folderId={Uri.EscapeDataString(folderId)}", cancellationToken);
            var sprint = AsArray(sprintJson).FirstOrDefault(x => (ReadString(x, "id") ?? string.Empty) == sprintId);

            var folderJson = await _api.GetAsync($"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/folders", cancellationToken);
            var folder = AsArray(folderJson).FirstOrDefault(x => (ReadString(x, "id") ?? string.Empty) == folderId);

            return await BuildSprintAnalyticsAsync(
                workspaceId,
                folderId,
                ReadString(folder, "name") ?? "Folder",
                sprintId,
                ReadString(sprint, "name") ?? "Sprint",
                ReadString(sprint, "state") ?? ReadString(sprint, "status") ?? "unknown",
                cancellationToken);
        }, cancellationToken);

    private async Task<SprintAnalyticsDto> BuildSprintAnalyticsAsync(string workspaceId, string folderId, string folderName, string sprintId, string sprintName, string sprintState, CancellationToken cancellationToken)
    {
        var workitemsJson = await _api.GetAsync($"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workitems?folderId={Uri.EscapeDataString(folderId)}&sprintId={Uri.EscapeDataString(sprintId)}&maxItemsCount=100", cancellationToken);
        var workitems = AsArray(workitemsJson);

        var byPerson = new Dictionary<string, PersonMetricsDto>(StringComparer.OrdinalIgnoreCase);
        var sem = new SemaphoreSlim(8);
        var tasks = new List<Task>();

        foreach (var item in workitems)
        {
            var workitemId = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(workitemId)) continue;

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(cancellationToken);
                try
                {
                    var person = ReadAssignee(item) ?? "Unassigned";
                    var estimateSeconds = ReadNumber(item, "originalEstimate") ?? ReadNumber(item, "estimatedTime") ?? 0;

                    var history = await LoadHistoryAsync(workspaceId, workitemId, cancellationToken);
                    var ttl = CalculateTtlHours(history);
                    var dev = CalculateTransitionHours(history, "selected", "ready to test");
                    var qa = CalculateTransitionHours(history, "ready to test", "acceptance");
                    var analyst = CalculateTransitionHours(history, "open", "handoff");

                    lock (byPerson)
                    {
                        if (!byPerson.TryGetValue(person, out var p))
                        {
                            p = new PersonMetricsDto { Person = person };
                            byPerson[person] = p;
                        }

                        p.TasksCount += 1;
                        p.CapacityHours += Math.Round(estimateSeconds / 3600d, 2);
                        p.TtlHours += ttl;
                        p.DeveloperVelocityHours += dev;
                        p.QaVelocityHours += qa;
                        p.AnalystVelocityHours += analyst;
                    }
                }
                finally
                {
                    sem.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        var people = byPerson.Values.OrderByDescending(x => x.CapacityHours).ToList();
        var summary = new SprintAnalyticsDto
        {
            WorkspaceId = workspaceId,
            FolderId = folderId,
            FolderName = folderName,
            SprintId = sprintId,
            SprintName = sprintName,
            SprintState = sprintState,
            ByPerson = people,
            CapacityHours = Math.Round(people.Sum(x => x.CapacityHours), 2),
            TeamVelocityHours = Math.Round(people.Sum(x => x.DeveloperVelocityHours), 2),
            TeamQaVelocityHours = Math.Round(people.Sum(x => x.QaVelocityHours), 2),
            TeamAnalystVelocityHours = Math.Round(people.Sum(x => x.AnalystVelocityHours), 2),
            TeamTtlHours = Math.Round(people.Sum(x => x.TtlHours), 2)
        };

        FillRisksAndRecommendations(summary);
        return summary;
    }

    private static void FillRisksAndRecommendations(SprintAnalyticsDto summary)
    {
        var isActive = summary.SprintState.Contains("run", StringComparison.OrdinalIgnoreCase)
            || summary.SprintState.Contains("active", StringComparison.OrdinalIgnoreCase)
            || summary.SprintState.Contains("start", StringComparison.OrdinalIgnoreCase);
        var isDone = summary.SprintState.Contains("done", StringComparison.OrdinalIgnoreCase)
            || summary.SprintState.Contains("close", StringComparison.OrdinalIgnoreCase)
            || summary.SprintState.Contains("complete", StringComparison.OrdinalIgnoreCase);

        var throughput = summary.TeamVelocityHours + summary.TeamQaVelocityHours + summary.TeamAnalystVelocityHours;
        var loadRatio = summary.CapacityHours <= 0 ? 0 : throughput / summary.CapacityHours;

        if (summary.CapacityHours > 0 && loadRatio < 0.6)
            summary.Risks.Add("Низкая утилизация Capacity (<60%): возможна потеря темпа релиза.");
        if (summary.TeamQaVelocityHours < summary.TeamVelocityHours * 0.45)
            summary.Risks.Add("QA Velocity заметно ниже Dev Velocity: риск очереди на тестировании.");
        if (summary.TeamTtlHours > summary.TeamVelocityHours * 2.2 && summary.TeamVelocityHours > 0)
            summary.Risks.Add("TTL высокий относительно скорости разработки: задачи долго проходят жизненный цикл.");

        if (isActive)
        {
            summary.Recommendations.Add("Для активного спринта: ежедневно сверять WIP-лимиты и очередь Ready to Test.");
            summary.Recommendations.Add("Перераспределите Capacity на роли с узким местом (QA/аналитика) по текущим velocity.");
        }
        else if (isDone)
        {
            summary.Recommendations.Add("Для завершённого спринта: использовать TTL и velocity по ролям как baseline для планирования следующего релиза.");
            summary.Recommendations.Add("Сократить handoff-этапы: повысит Analyst/Dev velocity и снизит итоговый TTL.");
        }
        else
        {
            summary.Recommendations.Add("Если спринт не запущен: выровняйте Capacity между ролями по историческим velocity перед стартом.");
            summary.Recommendations.Add("Добавьте буфер 15–20% в Capacity для задач с высоким прогнозным TTL.");
        }

        if (summary.Risks.Count == 0)
        {
            summary.Risks.Add("Критические риски не обнаружены по текущим метрикам Capacity/Velocity.");
        }
    }

    private async Task<List<HistoryEventDto>> LoadHistoryAsync(string workspaceId, string workitemId, CancellationToken cancellationToken)
    {
        foreach (var path in new[]
                 {
                     $"history/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workItems/{Uri.EscapeDataString(workitemId)}/history",
                     $"history/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workitems/{Uri.EscapeDataString(workitemId)}/history"
                 })
        {
            try
            {
                var json = await _api.GetAsync(path, cancellationToken);
                var list = new List<HistoryEventDto>();
                foreach (var node in AsArray(json))
                {
                    var dto = JsonSerializer.Deserialize<HistoryEventDto>(node.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dto is not null) list.Add(dto);
                }
                if (list.Count > 0) return list;
            }
            catch
            {
                // try next path
            }
        }

        return [];
    }

    private static double CalculateTtlHours(IReadOnlyList<HistoryEventDto> history)
    {
        var points = history.Where(x => x.Date.HasValue).Select(x => x.Date!.Value).OrderBy(x => x).ToList();
        if (points.Count < 2) return 0;
        return Math.Round((points[^1] - points[0]).TotalHours, 2);
    }

    private static double CalculateTransitionHours(IReadOnlyList<HistoryEventDto> history, string start, string end)
    {
        var statusEvents = history
            .Where(x => x.Date.HasValue)
            .Where(x => x.Type?.Equals("StatusUpdated", StringComparison.OrdinalIgnoreCase) == true || x.Type?.Equals("WorkItemStatusUpdated", StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => new { Date = x.Date!.Value, Status = NormalizeStatus(x.Data?.NewValue?.StatusName ?? x.Data?.NewValue?.Name) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Status))
            .OrderBy(x => x.Date)
            .ToList();

        DateTimeOffset? marker = null;
        var hours = 0d;
        foreach (var evt in statusEvents)
        {
            if (evt.Status == start) marker = evt.Date;
            if (evt.Status == end && marker.HasValue)
            {
                hours += Math.Max(0, (evt.Date - marker.Value).TotalHours);
                marker = null;
            }
        }
        return Math.Round(hours, 2);
    }

    private static string NormalizeStatus(string? value)
        => string.Join(' ', (value ?? string.Empty).ToLowerInvariant().Replace('о', 'o').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? ReadAssignee(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in new[] { "assignee", "owner", "executor", "responsible" })
        {
            if (!item.TryGetProperty(prop, out var assignee)) continue;
            var name = ReadString(assignee, "displayName") ?? ReadString(assignee, "name") ?? assignee.GetString();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return null;
    }

    private static string? ReadString(JsonElement node, string prop)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        if (!node.TryGetProperty(prop, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? ReadNumber(JsonElement node, string prop)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        if (!node.TryGetProperty(prop, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static List<JsonElement> AsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(x => x.Clone()).ToList();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "items", "workspaces", "data", "entries" })
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr.EnumerateArray().Select(x => x.Clone()).ToList();
            }
        }
        return [];
    }
}
