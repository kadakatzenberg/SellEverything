# Third-Party Notices

## Marketbuddy

Parts of the native retainer-market interaction design in Sell Everything are
based on the public Marketbuddy implementation, including the addon lifecycle
timing and the RetainerSell / ItemSearchResult event parameters and targets.

- Project: Marketbuddy
- Source: https://github.com/PunishXIV/Marketbuddy
- License: Apache License 2.0
- Copyright: Marketbuddy contributors

Sell Everything contains an independently written implementation adapted for
its own automated inventory queue, strict HQ/NQ market-response filtering, and
retainer-vendor workflow.

## Penny Pincher

Parts of the market-response validation design are based on observations from
the public Penny Pincher implementation, including activating a request when
`ItemSearchResult` reaches `PostSetup`, rejecting a repeated completed request
ID, detecting HQ from the `RetainerSell` item-name marker, checking the active
retainer context, and identifying listings owned by the player's retainers.

- Project: Penny Pincher
- Source: https://github.com/tesu/PennyPincher
- License: MIT License
- Copyright: Penny Pincher contributors

Sell Everything contains an independently written implementation integrated
with its automated inventory queue and strict matching-quality collector.
