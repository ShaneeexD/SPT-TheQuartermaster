const admin = require('firebase-admin');
const path = require('path');

const serviceAccountPath = process.env.GOOGLE_APPLICATION_CREDENTIALS
  || path.resolve(__dirname, '..', '..', 'Server', 'config', 'firebase-adminsdk.json');

admin.initializeApp({
  credential: admin.credential.cert(serviceAccountPath)
});

const db = admin.firestore();

const now = new Date();
const oneWeekLater = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);

const definitionId = 'test_handover_btc';
const scheduleId = 'test_handover_btc_schedule';

const definitionRef = db.collection('quartermaster_contracts').doc(definitionId);
const scheduleRef = db.collection('quartermaster_schedule').doc(scheduleId);

const definition = {
  title: 'Test Bitcoin Handover',
  description: 'Hand over 1 Bitcoin found in raid as a test contract.',
  status: 'approved',
  created_by: 'admin',
  admin_created: true,
  admin_featured: false,
  admin_blocked: false,
  spt_version: '4.0.13',
  objectives: [
    {
      type: 'HandOverFirItem',
      description: 'Hand over 1 Bitcoin found in raid',
      target_tpl: '5449016a4bdc2d6f028b456f',
      target_map: null,
      target_zone: null,
      count: 1,
      required_in_raid: true,
      target_faction: null
    }
  ],
  rewards: {
    roubles: 100000,
    experience: 1000,
    items: [],
    trader_standing: 0.0
  },
  upvotes: 0,
  downvotes: 0,
  approval_ratio: 0.0,
  validation_errors: [],
  metadata: {},
  created_at: admin.firestore.Timestamp.fromDate(now),
  voting_ends_at: admin.firestore.Timestamp.fromDate(now)
};

const schedule = {
  contract_definition_id: definitionId,
  status: 'active',
  recurrence_type: 'one_time',
  start_at: admin.firestore.Timestamp.fromDate(now),
  end_at: admin.firestore.Timestamp.fromDate(oneWeekLater),
  activated_at: admin.firestore.Timestamp.fromDate(now),
  expires_at: admin.firestore.Timestamp.fromDate(oneWeekLater),
  admin_created: true,
  created_at: admin.firestore.Timestamp.fromDate(now)
};

(async () => {
  try {
    await db.runTransaction(async (transaction) => {
      const defDoc = await transaction.get(definitionRef);
      if (defDoc.exists) {
        throw new Error(`Definition ${definitionId} already exists. Aborting to avoid duplicates.`);
      }
      transaction.set(definitionRef, definition);
      transaction.set(scheduleRef, schedule);
    });
    console.log(`Seeded test contract ${definitionId} and schedule ${scheduleId}.`);
  } catch (err) {
    console.error('Failed to seed test contract:', err.message);
    process.exitCode = 1;
  }
})();
