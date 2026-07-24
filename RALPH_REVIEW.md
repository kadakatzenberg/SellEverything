# Ralph Review Log

Sell Everything 0.4.1.0 was reviewed through repeated inspect, patch, validate,
and re-inspect loops. These are static engineering passes. FFXIV client behavior
was not simulated.

## Loop 1: State reachability and numeric correctness

- Followed every automation state and removed an unreachable market-request state.
- Corrected mixed unsigned and signed numeric handling before writing price input.
- Made inventory quantity conversion explicit and checked.
- Preserved bounded retries and timeout exits for all external UI waits.

## Loop 2: Protected-quantity semantics

- Found that the earlier rule could sell a whole stack whenever its quantity was
  greater than the configured keep value.
- Split source quantity, sell quantity, and protected quantity.
- Changed allocation so a keep value is applied once across all matching stacks
  of the same item and quality, not once per stack.
- Prevented low-price retainer vending of partial stacks because that action does
  not provide a safe quantity field for preserving the remainder.

## Loop 3: Transaction sequencing

- Revalidated the live inventory slot before every irreversible action.
- Checked for an inventory change before retrying listing or retainer-sale
  confirmation.
- Added a post-transaction gate that waits for RetainerSell, ItemSearchResult,
  and SelectYesno to close before re-scanning.
- Returned interrupted nonterminal entries to Pending on Stop or retainer change.

## Loop 4: Confirmation isolation

- Kept confirmation arms bound to a specific transaction kind.
- Kept the eight-second arm expiry and empty-prompt rejection.
- Added an explicit pause whenever manual confirmation is required during
  transaction verification instead of allowing a later timeout.
- Refused to advance from the UI-close gate while any confirmation dialog remains.

## Loop 5: Queue freshness and configuration safety

- Added a configuration fingerprint covering pricing, automation, safety,
  retainer, and protected-item settings.
- Rebuilds a queue when its governing settings changed after scanning.
- Locks settings and protected-item editing while running or paused.
- Requires a fresh review after failed entries are reset when review mode is on.

## Loop 6: Interface behavior

- Corrected pending metrics and filters to include active queue states.
- Shows source quantity and actual sell quantity separately for partial stacks.
- Prevents overlapping protected rules while still allowing distinct NQ-only and
  HQ-only rules for the same item.
- Balanced disabled UI scopes and retained explicit text labels alongside color.

## Loop 7: Retainer and recovery edge cases

- Avoids calling the active-retainer close path when a retainer failed before an
  active context was created.
- Clears stale market and sell windows before resetting failed entries.
- Clears old market counts, request IDs, decisions, and prices whenever an
  interrupted entry returns to Pending.
- Suppresses later stacks of an item and quality after an item-wide terminal
  decision, while keeping unsafe partial-stack skips scoped to the exact stack so
  later unprotected stacks can still be sold.

## Loop 8: Suppression-scope correction

- Found that item-wide suppression was too broad for a partial-stack low-price
  skip because another stack of the same item could be fully unprotected and safe
  to sell to the retainer.
- Split session suppression into item-quality scope and exact inventory-stack
  scope.
- Kept no-listing and no-vendor-value outcomes item-wide, while partial protected
  stacks are suppressed only by inventory type, slot, item ID, and quality.

## Loop 9: Static release validation

- Parsed both JSON manifests.
- Checked C# delimiter balance, enum references, version consistency, stale method
  signatures, required safety invariants, and ImGui scope balance.
- Checked the final archive paths and ZIP integrity.

## Remaining runtime validation

The package cannot be compiled or connected to FFXIV in this environment because
the .NET SDK and game client are unavailable. A Windows `dotnet build`, followed
by a dry run and a one-item live test, remains required before broad use.
