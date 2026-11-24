# The personal inventory

Decision: [ADR 0035](../adr/0035-personal-inventory-container-moves-and-reactor-fuel-slots.md). Spec: section I of `docs/superpowers/specs/2026-11-24-pilot-feedback-round-design.md` (part 2). Everything here is a *proposal* of ours (nothing taken from another game); the numbers are first guesses measured only by tests and the scripted player.

## What the player gets
| Thing | Behaviour |
| --- | --- |
| The bag | 24 slots, one stack of one item in each. A stack holds 100 of most items and 20 fuel cells (`stack_size` in the content). Carried by the person, so it is yours wherever the ship is. |
| `F10` | Opens the inventory beside the ship hold. Aimed at a reactor module (or clicking it) it opens beside that reactor's fuel slot; aimed at a machine it adds a tab with the machine's input and output. `F10`, `Esc` or the close button closes it. Every letter was taken, so it is a function key; the key panel row and `F1` name it. |
| The right-hand panel | Tabs for the containers within reach: the ship hold, a station's stock (docked at it or within 12 cells), each reactor (the ship's and a station's in reach) and the machine you aimed at. A hold or a stock shows one slot per item with a fill gauge. |
| Moving things | Left drag: the whole stack. Right drag: the larger half. Shift click or a double click: to the other panel (the reactor slot, the hold, a stock, a machine's input), or from any other panel into the bag. Keyboard: arrows move the focus ring, Enter picks up and drops, Ctrl+Enter picks up half, Shift+Enter quick-moves, Delete throws away. A drop on a slot of the bag with another item swaps a whole stack. |
| Sort | One command: merges stacks of the same item and packs them in item order. |
| Filters | All, ore, parts, fuel, ammo. They dim the slots that are not of the kind; nothing moves. |
| Bin | Drop a stack on it (or press Delete): it asks "Throw away N x item?" with Throw away and Cancel. Items thrown away are gone. |
| Tooltips | Name, count, what one stack holds, the item's kind, how long a fuel burns, what it is made into and which machines use it. |
| The reactor | A fuel slot (hydrogen or fuel cells, one kind at a time, up to a stack), the time the reactor keeps burning with what it holds, and an auto-feed selector. It burns the slot first. |

## Auto-feed (what a reactor does when its slot is empty)
| Setting | Takes fuel from |
| --- | --- |
| None | Nothing: only what you put in the slot. |
| From the hold (default) | The station stock of the structure first (a docked ship's reactor draws on its station), then the owner's hold: how every reactor behaved before. |
| From my inventory | The bag only (a fuel cell first, else hydrogen). |
| Hold, then inventory | The hold side first, the bag when that is dry. |

## Rules in numbers
| Rule | Value |
| --- | --- |
| Bag | `InventoryRules.Slots` = 24 |
| Default stack | `InventoryRules.DefaultStackSize` = 100; fuel cell 20 (content) |
| Reach | The bag and the hold always (while the ship is not lost); a stock, a reactor or a machine of yours when it is in the ship you fly, in the station you are docked at, or within `InventoryRules.ReachCells` = 12 of the ship |
| Reactor slot | One slot (number 0); hydrogen burns one interval, a fuel cell six (`PowerRules.FuelCellBurnIntervals`) |
| Machine input | What the recipe uses (twice a craft's need), a turret's ammo cap or a repairer's item cap |
| Machine output | Take only |
| Commands | `MoveItems` (tag 56), `SetFeedSource` (57), `SortInventory` (58); log version 18 |
| Rejects | `NotEnoughItems` (the source slot has fewer), `NoRoom` (the target takes fewer), `WrongItem` (new: not for that slot), `OutOfRange`, `InvalidArgument`, `NotAllowedHere` (legacy world, or no structure rules for a stock or reactor) |

## What did not change
- `TransferItems` is the same command with the same rejects; its work is a clamped move between the hold and the stock.
- Mined ore, drone ore and depot deliveries still land in the hold. On foot you can take what is in the hold, a stock or a machine output into the bag and put it from the bag into the hold, a stock, a machine input or a reactor.
- A world that never touches the inventory hashes exactly as before: the bag is mixed in only while it holds something, a reactor's slot and a non-default feed only while they exist.
- Death or a respawn keeps the bag (the first version).

## Measured
- Scripted player (StructureBot, 6 s per action, `--structures --difficulty=<preset> --minutes 150`, seed 20261121): see the numbers in [difficulty](difficulty.md#personal-inventory-round-2026-11-24). The bot never moves items by hand and the default feed is the old rule, so its times do not move.
- Tests: `InventoryTests` (sim), `InventoryViewTests` (engine-free panel logic and texts).
- `--inventory-shot=path` renders the reactor panel at 1280x720 and 1920x1080 in Spanish and English (`path.<size>.<lang>.reactor.png`) and, at 1280x720 in English, the hold with the ore filter (`.hold.png`), a machine's input and output (`.machine.png`) and the bin's question (`.bin.png`). `--ui-audit` opens the panel in several states at every window size.

## Not done here
Crates and dropping items into the world, picking items out of the world on foot (the bag fills from containers only), an inventory for a second player, item weight, turret ammo and drone slots as containers, and gamepad-specific glyphs (the focus ring and `ui_accept` work with a pad).
