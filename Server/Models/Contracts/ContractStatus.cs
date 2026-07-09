namespace TheQuartermaster.Server.Models.Contracts;

public static class ContractStatus
{
    public const string Draft = "draft";
    public const string PendingVote = "pending_vote";
    public const string Approved = "approved";
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Rejected = "rejected";
    public const string AdminFeatured = "admin_featured";

    public static readonly HashSet<string> VoteEligible =
    [
        PendingVote
    ];

    public static readonly HashSet<string> CanBeScheduled =
    [
        Approved,
        AdminFeatured
    ];

    public static readonly HashSet<string> ActiveOrScheduled =
    [
        Active,
        Scheduled
    ];
}
