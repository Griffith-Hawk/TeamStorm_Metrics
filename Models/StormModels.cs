using System.Text.Json.Serialization;

namespace TeamStorm.Metrics.Models;

public sealed class WorkspaceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class FolderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class WorkItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public sealed class HistoryEventDto
{
    public string? Type { get; set; }
    public DateTimeOffset? Date { get; set; }
    public HistoryDataDto? Data { get; set; }
}

public sealed class HistoryDataDto
{
    public HistoryStatusDto? NewValue { get; set; }
}

public sealed class HistoryStatusDto
{
    [JsonPropertyName("statusName")]
    public string? StatusName { get; set; }

    public string? Name { get; set; }
}

public sealed class FactRequest
{
    public string? PeriodStart { get; set; }
    public string? PeriodEnd { get; set; }
    public List<string> WorkItemIds { get; set; } = [];
}
