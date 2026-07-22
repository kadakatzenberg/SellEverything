# Sell Everything

Experimental Dalamud API 15 plugin for automated retainer inventory liquidation.

## v0.2 rules

- Start with the FFXIV retainer list open.
- Select retainers automatically, up to the configured count.
- Select **Sell items in your inventory on the market** automatically.
- Open eligible inventory stacks and use the in-game **Compare Prices** action.
- NQ inventory items accept only listings where `IsHq == false`.
- HQ inventory items accept only listings where `IsHq == true`.
- Packets for a different item or request ID are ignored.
- Multiple offering packets are accumulated before selecting the lowest matching price.
- Matching market prices below 100 gil use **Have Retainer Sell Items**.
- Other items list at the lowest matching price minus 1 gil.
- Expected listing and retainer-sale confirmations are handled automatically.
- Inventory is re-scanned after every successful transaction.
- When the current retainer cannot accept another listing, the plugin closes it and advances to the next retainer.
- Protected items and keep-quantity rules are applied before any sale action.

## First test

The plugin starts with **Dry run** enabled. Confirm the queue and whitelist first. For the initial live test, use disposable inventory and remain at the computer with the Emergency Stop button visible.

The default retainer menu option index is `2`. This corresponds to **Sell items in your inventory on the market** in the current menu order. It can be changed in plugin settings if the client menu order differs.

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

## Warning

This plugin performs native UI interaction and server-facing market and retainer actions. FFXIV patches can change UI layouts or callback behavior. Keep Dry run enabled after game patches until the workflow is revalidated.
