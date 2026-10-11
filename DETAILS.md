# DuplicateItemFix: details

Repairs corrupt world-item spawn data in an existing *The Long Dark* save, so items stop piling up invisibly at a single spot.

## The problem

On a long-running save you may find a stick, stone or piece of coal that you can pick up **more than once from the same spot**, and that comes back to a place you already cleared.

That happens because the game's stored spawn data has been corrupted. Spawn points end up holding wrong or shared claims on the same position, so several objects are placed on top of each other. They share identical coordinates, so they look like a single item; you only notice when the pickup prompt comes back.

The damage accumulates quietly, a few entries at a time, over hundreds of hours of play. A save with a lot of travel behind it can carry hundreds of bad entries across its regions, spread over sticks, stones, coal and bark tinder.

## What this mod does

At each region load it inspects the region's stored spawn data and removes entries that cannot be legitimate:

- a spawn point that stores the same position more than once
- a position claimed by several spawn points, where all but the one that can actually reach it are removed
- loose items that the world's gear list records more than once at the exact same position and rotation, where one entry is kept and the extra copies are removed

**It removes corrupt entries; it does not place anything.** A wrong claim is removed rather than moved: the item stays with the spawn point that really owns it, and the other spawn point refills its own spot at its normal position and on its normal schedule, so a repaired region is not left short of items.

Everything else is left alone. An item sitting somewhere odd but claimed by only one spawn point is not touched, because removing it would be deleting a real item rather than undoing a duplicate.

## What you will see

Nothing, visually. The corrupt objects share coordinates with a legitimate one, so the world does not look different before or after. What changes is that an item stops being pickable several times over.

The mod writes **one line per damaged region** to `MelonLoader/Latest.log`:

```
[DuplicateItemFix] Repaired <RegionName>: removed N corrupt spawner entries (N duplicated, N misclaimed) from N spawners. The game refills them itself.
```

A loose item recorded more than once in the world's gear list is reported separately, one line per scene:

```
[DuplicateItemFix] Repaired <SceneName>: removed N duplicate world-item record from N item recorded more than once.
```

A region or scene with nothing wrong produces no line at all.

## How the repair reaches your save

It works **region by region, as you load each one.** A region you never revisit keeps its damage.

The repair happens in memory when the region loads, and reaches the save file at the **next normal save**, which the game performs when you enter an indoor scene. If you quit before that, nothing is lost: the repair simply runs again next time. Running it twice does nothing, so there is no harm in revisiting a region that is already repaired.

## Requirements

- The Long Dark
- [MelonLoader](https://melonwiki.xyz/) 0.7.2

## Installation

Place `DuplicateItemFix.dll` in your `Mods` folder.

There is nothing to configure. The mod is active whenever the DLL is present.

## Back up your saves

Worth doing before you run any mod that edits save data, including this one.

Saves live in `%USERPROFILE%\AppData\Local\Hinterland\TheLongDark\Survival`. Copy that folder somewhere safe.

## Scope and limits

**It repairs the items a spawn point is authored to place** - sticks, stones, coal, bark tinder, crow feathers and anything else placed the same way. It reads what each spawn point is supposed to produce from the game's own data rather than from a list, so items in regions and DLC this was never tested against are covered too.

**It never touches loot.** Spawn points that draw from a loot table, which is what scatters assorted items along a shoreline, are skipped. Nothing you dropped or stored is affected, and the mod does not read or modify container contents, your inventory, or anything outside stored world-spawn data.

**Beachcombing is handled carefully rather than skipped.** On the shoreline spawn points the beachcombing system owns, the mod will collapse a genuine stack of identical items at one spot, because that cannot lose you anything. It will not do the second kind of repair there, reassigning a contested spot between spawn points, because those draw from a large mixed table where a wrongly removed entry could be a real one-off item.

**Items placed along a path are covered too.** A small number of spawn points place items along a line rather than around a point, a stone trail for instance. Those are measured along the path rather than from a single point, so an item 500 m down the trail is correctly recognized as belonging there.

**Existing damage only.** This repairs what is already in your save. It does not patch whatever created the damage, because that was produced on older versions of the game and current versions are rarely or never affected. Because the repair runs every time a region loads, anything that does form later is cleaned up the next time you load that region.

**Items added by other mods are treated the same way.** The gear-list repair only acts when two entries match to the last digit of position and rotation, which two separately placed items never do. A mod whose spawn file lists the same item at the same coordinates twice, usually by accident, therefore ends up with one item there instead of two. You will not see the difference, because the copies overlap exactly. Items you have ever carried, and items with an authored identity, are never removed.

**It changes nothing else.** The mod adds two patches, one on the game's spawn-data restore and one on its gear-list restore, and makes no change to items, wildlife, weather, difficulty or any other system. It does not alter how or where the game places anything; it only changes how many pending entries a spawn point holds.
