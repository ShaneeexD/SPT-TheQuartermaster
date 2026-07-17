using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

public record QuartermasterConfig
{
    [JsonPropertyName("modEnabled")]
    public bool ModEnabled { get; set; } = true;

    [JsonIgnore]
    public bool UploadConsent { get; set; } = true;

    // Community contracts (client-side display toggles only)
    [JsonIgnore]
    public bool AllowCommunityContracts { get; set; } = true;

    [JsonIgnore]
    public bool AllowAdminContracts { get; set; } = true;

    // Distributed worker
    [JsonIgnore]
    public bool EnableDistributedWorker { get; set; } = true;

    [JsonIgnore]
    public int WorkerIntervalMinutes { get; set; } = 5;

    [JsonIgnore]
    public int CommunityContractIntervalMinutes { get; set; } = 5;

    [JsonIgnore]
    public int WorkshopSyncIntervalMinutes { get; set; } = 5;

    [JsonIgnore]
    public int ContractSchedulerIntervalMinutes { get; set; } = 5;

    // Firebase public client configuration (no service account in public builds)
    [JsonIgnore]
    public string FirebaseProjectId { get; set; } = "spt-the-quartermaster";

    [JsonIgnore]
    public string FirebaseApiKey { get; set; } = "AIzaSyCv5bx6N4ew-nm0BNFyF3cE-TjGTH6PMSw";

    [JsonIgnore]
    public string FirebaseAuthDomain { get; set; } = "spt-the-quartermaster.firebaseapp.com";

    [JsonIgnore]
    public string FirebaseDatabaseUrl { get; set; } = "https://spt-the-quartermaster-default-rtdb.europe-west1.firebasedatabase.app/";

    // Contract file endpoint (optional). When set, the mod fetches contract data
    // from this URL instead of reading Firestore directly, reducing Firestore reads.
    // Falls back to Firestore if the URL is unreachable.
    [JsonPropertyName("contractFileUrl")]
    public string ContractFileUrl { get; set; } = "http://144.21.60.21/contracts/data.json";

    // Marketplace file endpoint (optional). When set, the mod fetches marketplace listings
    // from this URL instead of reading RTDB directly, reducing RTDB reads.
    // Falls back to direct RTDB if the URL is unreachable.
    [JsonPropertyName("marketplaceFileUrl")]
    public string MarketplaceFileUrl { get; set; } = "http://144.21.60.21/contracts/marketplace.json";

    [JsonPropertyName("scavengingEnabled")]
    public bool ScavengingEnabled { get; set; } = true;
}
