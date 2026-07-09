namespace TheQuartermaster.Server.Models.Contracts;

public static class ContractObjectiveType
{
    public const string KillScavs = "KillScavs";
    public const string KillPmcs = "KillPmcs";
    public const string KillBoss = "KillBoss";
    public const string HandOverItem = "HandOverItem";
    public const string HandOverFirItem = "HandOverFirItem";
    public const string SurviveMap = "SurviveMap";
    public const string ExtractMap = "ExtractMap";

    public static readonly HashSet<string> All =
    [
        KillScavs,
        KillPmcs,
        KillBoss,
        HandOverItem,
        HandOverFirItem,
        SurviveMap,
        ExtractMap
    ];
}
