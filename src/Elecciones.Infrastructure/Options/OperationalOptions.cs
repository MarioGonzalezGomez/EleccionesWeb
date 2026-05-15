namespace Elecciones.Infrastructure.Options;

public sealed class OperationalOptions
{
    public const string SectionName = "Operational";

    public OperationalHoursOptions Hours { get; set; } = new();
    public List<VideoSourceOptions> VideoSources { get; set; } =
    [
        new() { Slot = 1 },
        new() { Slot = 2 },
        new() { Slot = 3 },
        new() { Slot = 4 },
        new() { Slot = 5 },
        new() { Slot = 6 }
    ];
}

public sealed class OperationalHoursOptions
{
    public string HoraAvance1 { get; set; } = string.Empty;
    public string HoraAvance2 { get; set; } = string.Empty;
    public string HoraAvance3 { get; set; } = string.Empty;
    public string HoraParticipacion { get; set; } = string.Empty;
    public string HoraAvance1Historico { get; set; } = string.Empty;
    public string HoraAvance2Historico { get; set; } = string.Empty;
    public string HoraAvance3Historico { get; set; } = string.Empty;
    public string HoraParticipacionHistorico { get; set; } = string.Empty;
}

public sealed class VideoSourceOptions
{
    public int Slot { get; set; }
    public bool IsLive { get; set; }
    public string Path { get; set; } = string.Empty;
}
