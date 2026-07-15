# The Quartermaster

A global, player-driven second-hand marketplace.

Sell gear from your stash. Buy gear that other players have sold. Every item has a history.

**You can submit and view community made contracts on my [website](https://serenity-workshop.netlify.app/contracts?tab=active) to be added to the automatic rotating quest pool**

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


## Community Contracts

The Quartermaster's work changes over time.

Daily tasks, weekly contracts and limited-time operations can be delivered directly through the mod without requiring a new download.

Everyone running The Quartermaster receives the same active contracts, creating a shared SPT experience while keeping the game fully single-player.

Complete the job before it expires.

Miss the window, and the opportunity is gone.

You can submit and view community made contracts on my [website](https://serenity-workshop.netlify.app/contracts) to be added to the automatic rotating quest pool.

## Requirements

- SPT-AKI 4.0.13
- A working internet connection

## Installation

1. Download the latest release from the SPT Hub.
2. Extract the `SPT` folder into your SPT install directory.
3. Launch the SPT server.
4. The Quartermaster will appear in the trader list. Click **Refresh** on his page if his assortment is empty.

## Important Notes
- Trader assort is updated by your local server reading the mods online database every time you hit refresh, or when it auto refreshes by vanilla functions.
- Modded items are fully compatible, you can sell modded items, if another user does not have the mod, it simply will not show for said user, if they do, it will show.
- Items sold to the trader will stay there for a configured time in the backend (can change) before expiring and being deleted.
- Contract data (quests, schedules, config) is fetched from a dedicated caching server every 5 minutes, reducing database load. If the caching server is unreachable, the mod falls back to reading directly from the database automatically.
- Marketplace cleanup (removing expired and sold listings) is handled by a central server, so your local server doesn't need to download the full marketplace data for cleanup.
- Quest submissions go through a voting system to ensure only quality quests are auto approved (I can still override submitted quests).
- Voting lasts for 12 hours, and requires a minimum of (5 - can change in real time in backend) votes with a 70% upvote ratio, if the minimum votes are not met, the time will be extended for another 6 hours up to 48 where it will be auto rejected.
- If someone is seen to be submitting blatantly troll quests, your discord id will be banned from submitting any more quests.
- New quests are added to the trader every Day/Week/Weekend, and if high quality, will be kept in the rotating pool.
- One time quests can also be submitted, these will then be deleted after they expire (24 hours).
- When new quests are added, you'll receive an in-game message from The Quartermaster letting you know what's been added.
- When new quests are added, it requires a game restart for it to be injected into your local database.
- Brand new quests are prioritised over quests that have already been used before, this is to ensure that it stays fresh and only repeats quests when new ones are not available.

## Credits

- **Author:** ShaneeexD
- **License:** MIT

**READ THE [PRIVACY & NETWORK DISCLOSURE](https://github.com/ShaneeexD/SPT-TheQuartermaster/blob/main/PRIVACY_DISCLOSURE.md)**