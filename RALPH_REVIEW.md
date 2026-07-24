# Ralph Review Log

Sell Everything 0.4.0.0 was reviewed in repeated implementation passes rather
than as a single design edit.

## Pass 1: Functional state flow

- Followed every automation state from retainer selection through transaction
  verification and retainer dismissal.
- Added bounded retries when Compare Prices dispatches but ItemSearchResult does
  not appear.
- Added bounded retries for listing confirmation and retainer-sale confirmation.
- Added direct retry recovery for failed queue entries.
- Treated a valid empty market response as no listings instead of waiting for a
  timeout.

## Pass 2: Safety and stale-action prevention

- Kept exact item ID, HQ/NQ, request ID, active-retainer, and live inventory-slot
  validation.
- Limited Compare Prices, listing confirmation, and retainer-sale retries to
  three attempts.
- Added a time-to-live to armed SelectYesno confirmation so a delayed unrelated
  dialog is not accepted.
- Kept emergency stop, dry run, own-retainer filtering, and market-response
  duplicate rejection intact.

## Pass 3: Interface and interaction design

- Replaced the flat single-page layout with focused tabs.
- Added a dashboard with one primary action, secondary actions, progress, status,
  metrics, current-item context, and recent activity.
- Added queue search, state filters, a frozen header, clearer decision and price
  columns, and color-supported text labels.
- Rebuilt protected-item editing and grouped settings by user intent.
- Kept all status meanings in text so color is not the only signal.

## Pass 4: Static release review

- Updated project, manifest, repository, README, and changelog versions together.
- Parsed JSON manifests.
- Checked C# delimiter balance and duplicate type declarations.
- Checked for accidental TODO or FIXME markers.
- Verified the final ZIP archive structure and integrity.

Runtime interaction with FFXIV cannot be simulated outside the client. The
Windows Dalamud build and an in-game dry run remain required validation steps.
