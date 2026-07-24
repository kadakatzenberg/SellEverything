# Sell Everything

Experimental Dalamud API 15 plugin that processes eligible player inventory
through retainers using the in-game market comparison flow.

## Sale rules

- Scans the four player inventory pages.
- Skips protected, untradable and non-marketable items.
- Compares HQ stacks only with HQ listings.
- Compares NQ stacks only with NQ listings.
- Cross-checks the inventory quality against the HQ marker shown by `RetainerSell`.
- Ignores listings owned by your retainers unless explicitly enabled.
- Uses the in-game **Compare Prices** request and Dalamud market-board response.
- If the lowest matching offer is below the configured market floor, invokes
  **Have Retainer Sell Items**.
- Otherwise, lists at the matching lowest price minus the configured undercut.
- Skips a stack when no matching-quality listing is returned.

## Retainer automation

Starting from the retainer list, the plugin can:

1. Select each retainer.
2. Open the retainer market inventory.
3. Open an eligible item for sale.
4. Trigger **Compare Prices** after `RetainerSell` setup.
5. Activate collection only after `ItemSearchResult` setup, then validate the response by item ID, request ID and quality.
6. Close the market results through its native window event.
7. Set the asking price directly and submit the listing.
8. Confirm expected listing or retainer-sale dialogs.
9. Re-scan inventory and continue.
10. Dismiss the retainer and proceed to the next selected retainer.

Version 0.3 replaced the previous polling-first market UI layer with addon
lifecycle tracking and the native interaction sequence established by
Marketbuddy. Version 0.3.1 added Penny Pincher-inspired request gating,
duplicate-request rejection, sell-window HQ detection, active-retainer
validation, and own-retainer filtering. Version 0.4 adds a modern dashboard, searchable queue, activity history,
bounded UI retries, confirmation expiry, and fault recovery. Version 0.4.1
adds global protected-quantity allocation across matching stacks, stale-queue
configuration detection, stricter post-transaction dialog gating, and further
state-machine recovery hardening. Version 0.4.2 adds sortable queue columns,
a refreshed interface theme, and view-only sorting that leaves the automation
order untouched. See `THIRD_PARTY_NOTICES.md`, `DESIGN_REVIEW.md`, and `RALPH_REVIEW.md`.


## Interface

- Overview dashboard with run controls, state progress, metrics, current item, and recent activity.
- Searchable and filterable queue with compact market, decision, price, and state columns.
- Protected-item editor with quality scope and editable keep values.
- Grouped Pricing, Automation, and Safety settings.

## Commands

- `/selleverything`
- `/selleverything scan`
- `/selleverything start`
- `/selleverything pause`
- `/selleverything resume`
- `/selleverything retry`
- `/selleverything stop`
- `/selleverything config`

## Build

```powershell
dotnet build ".\SellEverything\SellEverything.csproj" -c Release
```

The Dalamud SDK generates a plugin ZIP under the project `bin` directory.

## Safety

The plugin performs native UI interaction and server-facing market or retainer
actions. Game patches can invalidate UI layouts or event parameters. Begin with
Dry Run, use disposable test items for the first live pass, and stop immediately
when the visible UI does not match the displayed state.

This project is intended for a private/custom plugin repository rather than the
official Dalamud plugin repository.
