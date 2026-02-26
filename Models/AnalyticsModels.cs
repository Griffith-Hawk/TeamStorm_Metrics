namespace TeamStorm.Metrics.Models;

public sealed class PersonMetricsDto
{
    public string Person { get; set; } = "Unassigned";
    public double CapacityHours { get; set; }
    public double TtlHours { get; set; }
    public double DeveloperVelocityHours { get; set; }
    public double QaVelocityHours { get; set; }
    public double AnalystVelocityHours { get; set; }
    public int TasksCount { get; set; }
}

public sealed class SprintAnalyticsDto
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string SprintId { get; set; } = string.Empty;
    public string SprintName { get; set; } = string.Empty;
    public string SprintState { get; set; } = "unknown";
    public double CapacityHours { get; set; }
    public double TeamVelocityHours { get; set; }
    public double TeamQaVelocityHours { get; set; }
    public double TeamAnalystVelocityHours { get; set; }
    public double TeamTtlHours { get; set; }
    public List<PersonMetricsDto> ByPerson { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}

public sealed class ProjectAnalyticsDto
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<SprintAnalyticsDto> Sprints { get; set; } = [];

    public double TotalCapacityHours => Sprints.Sum(x => x.CapacityHours);
    public double TotalDeveloperVelocityHours => Sprints.Sum(x => x.TeamVelocityHours);
    public double TotalQaVelocityHours => Sprints.Sum(x => x.TeamQaVelocityHours);
    public double TotalAnalystVelocityHours => Sprints.Sum(x => x.TeamAnalystVelocityHours);
    public double TotalTtlHours => Sprints.Sum(x => x.TeamTtlHours);
}
