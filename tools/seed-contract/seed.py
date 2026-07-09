from datetime import datetime, timedelta, timezone
from pathlib import Path
from google.cloud import firestore

# Path to your service account key.
# Set GOOGLE_APPLICATION_CREDENTIALS in your environment, or edit this line.
service_account_path = Path(__file__).parent.parent.parent / "Server" / "config" / "firebase-adminsdk.json"

if service_account_path.exists():
    db = firestore.Client.from_service_account_json(str(service_account_path))
else:
    db = firestore.Client()

now = datetime.now(timezone.utc)
one_week_later = now + timedelta(days=7)

definition_id = "test_handover_btc"
schedule_id = "test_handover_btc_schedule"

# Fixed stable quest ID for this test contract (matches the SHA256-derived ID from the schedule ID).
quest_id = "5d3e17d7a0e63e1f955814ff"

# Correct Bitcoin (physical BTC) template ID.
btc_tpl = "59faff1d86f7746c51718c9c"

definition_ref = db.collection("quartermaster_contracts").document(definition_id)
schedule_ref = db.collection("quartermaster_schedule").document(schedule_id)

batch = db.batch()

batch.set(definition_ref, {
    "title": "Test Bitcoin Handover",
    "description": "Hand over 1 Bitcoin found in raid as a test contract.",
    "status": "approved",
    "created_by": "admin",
    "admin_created": True,
    "admin_featured": False,
    "admin_blocked": False,
    "spt_version": "4.0.13",
    "objectives": [
        {
            "type": "HandOverFirItem",
            "description": "Hand over 1 Bitcoin found in raid",
            "target_tpl": "59faff1d86f7746c51718c9c",
            "target_map": None,
            "target_zone": None,
            "count": 1,
            "required_in_raid": True,
            "target_faction": None
        }
    ],
    "rewards": {
        "roubles": 100000,
        "experience": 1000,
        "items": [],
        "trader_standing": 0.0
    },
    "upvotes": 0,
    "downvotes": 0,
    "approval_ratio": 0.0,
    "validation_errors": [],
    "metadata": {},
    "created_at": now,
    "voting_ends_at": now
}, merge=True)

batch.set(
    schedule_ref,
    {
        "contract_definition_id": definition_id,
        "status": "active",
        "recurrence_type": "one_time",
        "start_at": now,
        "end_at": one_week_later,
        "activated_at": now,
        "expires_at": one_week_later,
        "admin_created": True,
        "created_at": now,
        "quest_id": quest_id
    },
    merge=True
)

batch.commit()
print(f"Seeded/updated test contract {definition_id} and schedule {schedule_id} (quest_id={quest_id}).")
