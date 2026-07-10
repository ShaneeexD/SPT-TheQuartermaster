# The Quartermaster

A global, player-driven second-hand marketplace for SPTarkov.

Sell gear from your stash. Buy gear that other players have sold. Every item has a history.

## A Living Marketplace

One of the most interesting parts of Fence in live Escape from Tarkov is that his inventory is influenced by what players sell.

SPT can't naturally recreate that shared economy.

Until now.

**The Quartermaster brings a true player-driven second-hand marketplace to SPT.**

Rather than generating random stock, The Quartermaster buys equipment that real players have sold and makes it available to everyone else running the mod.

Every item you see has a history.

Someone found it.

Someone used it.

Someone decided to sell it.

Now it's available for you.

When it's gone, it's gone.

## Features

- **Global listings** – Items you sell are uploaded to a shared Firestore database and appear for everyone running the mod.
- **Dynamic pricing** – Assortment items are priced at handbook value plus a configurable markup, with durability and quality taken into account.
- **Server-side only** – No client mod or prepatcher required.

## Community Contracts

The Quartermaster's work changes over time.

Daily tasks, weekly contracts and limited-time operations can be delivered directly through the mod without requiring a new download.

Everyone running The Quartermaster receives the same active contracts, creating a shared SPT experience while keeping the game fully single-player.

Complete the job before it expires.

Miss the window, and the opportunity is gone.

## Requirements

- SPT-AKI 4.0.13
- A working internet connection (the mod talks to a Firebase Firestore backend)

## Installation

1. Download the latest release from the SPT Hub.
2. Extract the `SPT` folder into your SPT install directory.
3. Launch the SPT server.
4. The Quartermaster will appear in the trader list. Click **Refresh** on his page if his assortment is empty.


## Configuration

Edit `user/mods/TheQuartermaster/config/config.json` to change the mod's behavior.

| Setting | Default | Description |
|---|---|---|
| `modEnabled` | `true` | Enable or disable the mod locally. |
| `uploadConsent` | `true` | Allow your server to upload listings to the shared database. |
| `allowCommunityContracts` | `true` | Show community-submitted contracts in your client. |
| `allowAdminContracts` | `true` | Show admin-created contracts in your client. |
| `enableDistributedWorker` | `true` | Enable the timer-driven community contract scheduler. |
| `workerIntervalMinutes` | `5` | How often the contract scheduler runs. |
| `firebaseProjectId` | `""` | The Mod's Firebase project ID (public client). |
| `firebaseApiKey` | `""` | The Mod's Firebase public Web API key for anonymous auth. |
| `firebaseAuthDomain` | `""` | The Mod's Firebase auth domain. |

### Backend Configuration

Additional runtime settings are stored in the `quartermaster_config/contract_config` Firestore document and can be edited from the **Settings** tab of the Quartermaster Admin Panel.

| Setting | Default | Description |
|---|---|---|
| `workshop_sync_enabled` | `true` | Pull community contracts from the website API into the mod database. |
| `workshop_api_url` | `https://serenity-workshop.netlify.app/api/contract-list` | The website API endpoint to sync contracts from. |


## Credits

- **Author:** ShaneeexD
- **License:** MIT
