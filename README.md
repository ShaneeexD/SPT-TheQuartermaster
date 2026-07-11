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
- **Dynamic pricing** – Assortment items are priced at handbook value minus 10% plus a 5% markup, with durability and quality taken into account, markup decreases with higher loyalty level down to 2%.
- **Server-side only** – No client mod or prepatcher required.

## Community Contracts

The Quartermaster's work changes over time.

Daily tasks, weekly contracts and limited-time operations can be delivered directly through the mod without requiring a new download.

Everyone running The Quartermaster receives the same active contracts, creating a shared SPT experience while keeping the game fully single-player.

Complete the job before it expires.

Miss the window, and the opportunity is gone.

You can submit and view community made contracts on my [website](https://serenity-workshop.netlify.app/contracts) to be added to the automatic rotating quest pool.

## Requirements

- SPT-AKI 4.0.13
- A working internet connection (the mod talks to a Firebase Firestore backend)

## Installation

1. Download the latest release from the SPT Hub.
2. Extract the `SPT` folder into your SPT install directory.
3. Launch the SPT server.
4. The Quartermaster will appear in the trader list. Click **Refresh** on his page if his assortment is empty.

## Notes
- Trader assort is updated by your local server reading the mods database every time you hit refresh, or buy/sell and item.
- Quest submissions go through a voting system to ensure only quality quests are auto approved (I can still override submitted quests).
- Voting lasts for 12 hours, and requires a minimum of 10 votes with a 70% upvote ratio, if the minimum votes are not met, the time will be extended for another 6 hours up to 48 where it will be auto rejected.
- If someone is seen to be submitting blatently troll quests, you will be banned from submitting again as submitting requires login via discord.
- New quests are added to the trader every Day/Week/Weekend, and if high quality, will be kept in the rotating pool.
- One time quests can also be submitted, these will then be deleted after they expire (24 hours).
- When new quests are added, it requires a game restart for it to be injected into your local database.

## Credits

- **Author:** ShaneeexD
- **License:** MIT
