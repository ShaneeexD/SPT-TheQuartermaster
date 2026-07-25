namespace TheQuartermaster.Server.Models.Contracts;

public static class ContractObjectiveType
{
    public const string KillScavs = "KillScavs";
    public const string KillPmcs = "KillPmcs";
    public const string KillBoss = "KillBoss";
    public const string KillEnemy = "KillEnemy";
    public const string HandOverItem = "HandOverItem";
    public const string HandOverFirItem = "HandOverFirItem";
    public const string FindItem = "FindItem";
    public const string SurviveMap = "SurviveMap";
    public const string ExtractMap = "ExtractMap";

    public static readonly HashSet<string> All =
    [
        KillScavs,
        KillPmcs,
        KillBoss,
        KillEnemy,
        HandOverItem,
        HandOverFirItem,
        FindItem,
        SurviveMap,
        ExtractMap
    ];
}
