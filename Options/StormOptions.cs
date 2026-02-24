namespace TeamStorm.Metrics.Options;

public sealed class StormOptions
{
    public const string SectionName = "Storm";

    public string BaseUrl { get; set; } = "https://storm.alabuga.space";
    public string? ApiToken { get; set; }
    public string? SessionToken { get; set; }
}
