using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamStorm.Metrics.Models;

public sealed class FactRequest
{
    public List<string> WorkitemIds { get; set; } = [];
}

public sealed class WorkItemUpdateRequest
{
    public double? OriginalEstimate { get; set; }
    public double? EstimatedTime { get; set; }
}

public sealed class HistoryEventDto
{
    public string? Type { get; set; }
    public DateTimeOffset? Date { get; set; }
    public HistoryDataDto? Data { get; set; }
    public JsonElement? User { get; set; }
    public JsonElement? Author { get; set; }
    public string? UserId { get; set; }
    public string? AuthorId { get; set; }
}

public sealed class HistoryDataDto
{
    public HistoryStatusDto? NewValue { get; set; }
    public JsonElement? User { get; set; }
    public JsonElement? Author { get; set; }
    public string? UserId { get; set; }
}

public sealed class HistoryStatusDto
{
    [JsonPropertyName("statusName")]
    public string? StatusName { get; set; }

    public string? Name { get; set; }
}

public sealed class ReadyToTestPairDto
{
    public string? ReadyAt { get; set; }
    public string? ReadyBy { get; set; }
    public string? TakenAt { get; set; }
    public string? TakenBy { get; set; }
    public double? LagHours { get; set; }
    public double? TestingHours { get; set; }
}
