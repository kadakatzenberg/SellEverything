# Changelog

## 0.4.2.0

- Adds sortable queue columns: click any header (Item, Quality, Qty, Market, Decision, Price, State) to sort ascending or descending. The queue now defaults to alphabetical order by item name.
- Sorting, searching, and filtering affect only the on-screen view and never change the order the automation processes entries.
- Reworks the queue rows with clearer decision labels, friendlier state names, partial-stack sell/keep tooltips, and muted empty-note markers.
- Adds empty-state messages to the queue and activity views when there is nothing to show yet.
- Refreshes the overall theme with rounded panels, accent-tinted tables and headers, tighter spacing, and a color-coded live/dry-run indicator.

## 0.4.1.0

- Allocates protected keep quantities globally across matching stacks of the same item and quality instead of preserving the full keep amount in every stack.
- Carries source quantity, sell quantity, and protected quantity independently through validation, listing, and queue display.
- Skips low-price retainer vending for partial stacks so protected remainder cannot be sold accidentally.
- Rebuilds stale queues when pricing, safety, retainer, or protected-item settings change after scanning.
- Locks mutable settings and protected-item rules while a run is active or paused.
- Requires an active retainer context before advancing into item automation.
- Waits for RetainerSell, ItemSearchResult, and SelectYesno to close before re-scanning after a transaction.
- Pauses immediately when a manual transaction confirmation is required, including during transaction verification.
- Detects inventory change before retrying a listing or retainer-sale confirmation to reduce duplicate-action risk.
- Restores interrupted nonterminal entries to Pending on Stop or when moving to another retainer.
- Clears stale per-entry market data before retry and closes leftover market windows before resetting failed entries.
- Avoids invoking the active-retainer close path when a retainer failed before an active context was established.
- Suppresses repeated searches after item-wide terminal decisions, while scoping unsafe partial-stack skips to the exact inventory stack so later unprotected stacks remain eligible.

## 0.4.0.0

- Rebuilt the main window as a modern tabbed dashboard with Overview, Queue, Protected Items, and Settings surfaces.
- Added prominent run controls, live-mode messaging, step progress, current-item details, status-colored metrics, and recent activity history.
- Added queue search and state filters with a compact, resizable table.
- Reworked protected-item management with quality scopes, editable keep values, duplicate detection, and a clearer add-rule flow.
- Reorganized settings into Pricing, Automation, and Safety tabs with contextual descriptions.
- Added bounded retries for Compare Prices, market-list confirmation, and Have Retainer Sell Items confirmation.
- Added an eight-second expiry for armed SelectYesno confirmations so a delayed unrelated dialog is not accepted.
- Accepts a valid empty market packet after ItemSearchResult setup as a no-listings result instead of waiting for the full timeout.
- Added activity history, run progress, elapsed-step display, persistent fault details, and a Retry Failed action.

## 0.3.1.0

- Added Penny Pincher's request-gating pattern: market packets are accepted only after `ItemSearchResult` reaches `PostSetup`.
- Rejects a repeated completed market request ID to avoid reusing the previous response after a temporary search error.
- Detects HQ directly from the `RetainerSell` item-name marker and blocks Compare Prices when the UI quality disagrees with the inventory stack.
- Verifies that `ItemOrderModule.ActiveRetainerId` is nonzero before dispatching Compare Prices.
- Ignores listings owned by the player's retainers by default, with an opt-in setting to undercut them.
- Added Penny Pincher attribution and implementation notes.

## 0.3.0.0

- Refactored market UI interaction around Dalamud addon lifecycle events.
- Arms Compare Prices before RetainerSell opens and dispatches it at PostSetup.
- Begins market response collection before the compare request is sent.
- Closes ItemSearchResult through its native window-component event.
- Sets the RetainerSell numeric price directly and submits with event parameter 21.
- Pumps expected SelectYesno confirmation every framework frame instead of waiting for the configured action delay.
- Preserves exact item-ID and HQ/NQ filtering in the market response collector.
- Added Marketbuddy attribution and third-party notice.

## 0.2.0.4

- Use the RetainerSell addon's native Change event for Compare Prices.
- Use the native RetainerSell confirmation event after setting the asking price.
- Retry Compare Prices and listing confirmation until their controls are ready.
- Force expected-dialog confirmation during full retainer automation.
- Close the RetainerSell window directly before a retainer-vendor sale.

## 0.2.0.3

- Restores the nullable market-price fix that was accidentally omitted from 0.2.0.2.

## 0.2.0.2

- Fixed retainer-list selection being treated as failed when the game callback returned false after dispatch.
- Retainer selection now faults only when the addon is unavailable or the native call throws.

## 0.2.0.0

- Added automatic retainer selection starting from the retainer list.
- Added automatic selection of the retainer market-selling menu.
- Added automatic expected confirmation handling.
- Added automatic progression through up to six retainers.
- Added strict market response validation by exact item ID, quality and request ID.
- Added accumulation of multiple market offering packets before choosing a price.
- Added explicit NQ and HQ listing counts to the queue UI.
- Added live inventory validation before compare, listing and retainer-vendor actions.
- Added inventory re-scan after every completed transaction.
- Added automatic next-retainer handling when a selling window cannot open.
