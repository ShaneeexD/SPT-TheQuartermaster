using System.Diagnostics;
using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class WorkerLockService(
    ISptLogger<WorkerLockService> logger,
    FirestoreService firestoreService
)
{
    private readonly string _instanceId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}";

    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    public string InstanceId => _instanceId;

    public async Task<bool> AcquireOrRenewAsync(TimeSpan? leaseDuration = null)
    {
        if (!firestoreService.IsEnabled || firestoreService.Db is null)
        {
            return true;
        }

        var db = firestoreService.Db;
        var docRef = db.Collection(QuartermasterConstants.FirestoreCollections.Meta).Document("worker_lock");
        var duration = leaseDuration ?? DefaultLeaseDuration;
        var now = DateTime.UtcNow;
        var nowTs = Timestamp.FromDateTime(now);
        var expiresTs = Timestamp.FromDateTime(now.Add(duration));

        try
        {
            var acquired = await db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (snapshot.Exists)
                {
                    var holder = snapshot.ContainsField("lock_holder") ? snapshot.GetValue<string>("lock_holder") : null;
                    var expiresAt = snapshot.ContainsField("expires_at") ? snapshot.GetValue<Timestamp>("expires_at") : (Timestamp?)null;

                    if (!string.IsNullOrWhiteSpace(holder) && !string.Equals(holder, _instanceId, StringComparison.OrdinalIgnoreCase) && expiresAt.HasValue && expiresAt.Value.ToDateTime() >= now)
                    {
                        return false;
                    }
                }

                transaction.Set(docRef, new Dictionary<string, object>
                {
                    ["lock_holder"] = _instanceId,
                    ["acquired_at"] = nowTs,
                    ["expires_at"] = expiresTs
                });

                return true;
            });

            return acquired;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to acquire/renew worker lock: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> IsLeaderAsync()
    {
        return await AcquireOrRenewAsync(DefaultLeaseDuration);
    }
}
