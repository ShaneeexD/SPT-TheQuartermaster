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

All global settings (markup percentage, vanilla-items-only, the community contracts toggle, contract tuning, and scheduling caps) are controlled via the backend (`quartermaster_config/contract_config`) and are not exposed in this local config file.

## Notes & Limitations

- The mod checks your stash space before completing a purchase. If an item does not fit, the trade is cancelled and the listing remains available.
- Incompatible items (e.g., modded templates missing from your local database) are filtered out of the buyer's view.
- Currency, secure containers, and some special items are excluded from the marketplace.
- A live internet connection is required; the mod cannot function offline.

## Credits

- **Author:** ShaneeexD
- **License:** MIT
