# Changelog

## 0.2.0.4

- Use the RetainerSell addon's native Change event for Compare Prices.
- Use the native RetainerSell confirmation event after setting the asking price.
- Retry Compare Prices and listing confirmation until their controls are ready.
- Force expected-dialog confirmation during full retainer automation.
- Close the RetainerSell window directly before a retainer-vendor sale.

## 0.2.0.3

- Restores the nullable market-price fix that was accidentally omitted from 0.2.0.2.

# Changelog

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
