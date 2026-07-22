# Sell Everything

Experimental Dalamud API 15 plugin for building and executing a reviewed retainer-sale queue.

## Rules

- Scans the four player inventory pages.
- Skips protected items, untradable items and items without a market category.
- HQ stacks compare only against HQ listings.
- NQ stacks compare only against NQ listings.
- Uses the in-game **Compare Prices** flow and Dalamud market-board response event.
- If the lowest matching offer is below 100 gil, invokes **Have Retainer Sell Items**.
- Otherwise, lists at the matching lowest price minus 1 gil.
- If there are no matching HQ/NQ listings, skips the stack.

## Current alpha scope

This is a first in-game validation build. It processes the currently opened retainer. When that retainer is full or the retainer UI is closed, stop or pause the queue, open the next retainer, and resume. Automatic selection across all six retainers is deliberately deferred until the sale loop is proven stable.

The plugin starts in **Dry run** mode. Disable Dry run only after reviewing the generated queue and testing on disposable items.

## Commands

- `/selleverything`
- `/selleverything scan`
- `/selleverything start`
- `/selleverything pause`
- `/selleverything resume`
- `/selleverything stop`
- `/selleverything config`

## Build

```powershell
dotnet build ".\SellEverything\SellEverything.csproj" -c Release
```

The Dalamud SDK should generate a release-ready ZIP under the project `bin` directory.

## Important

This plugin performs native UI interaction and server-facing market/retainer actions. FFXIV patches may invalidate UI layouts or interaction behavior. Do not leave it unattended. Use Emergency Stop immediately if the UI state differs from the queue state.

This type of automation is intended for a private/custom plugin repository, not the official Dalamud plugin repository.
