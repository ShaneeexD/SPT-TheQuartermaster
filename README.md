# The Quartermaster

A global, player-driven marketplace trader for SPTarkov. Sell gear from your stash to The Quartermaster and browse live listings posted by other players. The marketplace is backed by a shared Firebase Firestore database, so the same assortment is available across every server and profile using the mod.

## Features

- **Global listings** – Items you sell are uploaded to a shared Firestore database and appear for everyone running the mod.
- **Dynamic pricing** – Listings are priced at handbook value plus a configurable markup, with durability and quality taken into account.
- **Server-side only** – No client mod or prepatcher required.

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
| `modEnabled` | `true` | Enable or disable the mod. |
| `uploadConsent` | `true` | Allow your server to upload listings to the shared database. |
| `baseMarkupPercent` | `15.0` | Markup applied to the base handbook price when listing items. |
| `minPrice` | `100` | Minimum listing price. |
| `maxPrice` | `50000000` | Maximum listing price. |
| `maxListingsPerPlayer` | `100` | Maximum active listings a single player can have at once. |
| `listingDurationHours` | `168` | How long listings remain active before expiring. |
| `maxItemTreeSize` | `100` | Largest item tree (parent + children) that can be listed. |
| `vanillaItemsOnly` | `false` | When `true`, only vanilla items can be listed or shown. |
| `sellerAnonymizationSalt` | `""` | Optional salt used to hash seller identities. |
| `debugLogging` | `false` | Extra logging for troubleshooting. |

## Notes & Limitations

- The mod checks your stash space before completing a purchase. If an item does not fit, the trade is cancelled and the listing remains available.
- Incompatible items (e.g., modded templates missing from your local database) are filtered out of the buyer's view.
- Currency, secure containers, and some special items are excluded from the marketplace.
- A live internet connection is required; the mod cannot function offline.

## Credits

- **Author:** ShaneeexD
- **License:** MIT
