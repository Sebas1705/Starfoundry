# 10 Data model

Status: proposal. All content lives in versioned data files so balance and mods do not require code changes.

## Conventions
- Format: JSON (or YAML converted to JSON at build time). IDs are `snake_case` strings, namespaced by pack (`core:iron_ingot`).
- Every file has `schema` (version) and `id`.
- Localisation keys instead of display text: `name_key`, `desc_key`.
- Validated by a schema and a referential-integrity check (see [09-technical-architecture](09-technical-architecture.md)).

## Schemas (examples)

### Item
```json
{
  "schema": 1,
  "id": "core:iron_ingot",
  "name_key": "item.iron_ingot",
  "stack_size": 50,
  "mass_kg": 2.0,
  "tags": ["ingot", "metal"]
}
```

### Recipe
```json
{
  "schema": 1,
  "id": "core:plate_from_iron",
  "machine": "core:assembler",
  "inputs": [{ "item": "core:iron_ingot", "count": 2 }],
  "outputs": [{ "item": "core:iron_plate", "count": 1 }],
  "time_s": 2.0,
  "power_kw": 60,
  "heat_kw": 5
}
```

### Machine
```json
{
  "schema": 1,
  "id": "core:smelter",
  "footprint": [3, 3, 2],
  "ports": [
    { "kind": "item_in", "offset": [0, 1, 0], "dir": "west" },
    { "kind": "item_out", "offset": [2, 1, 0], "dir": "east" }
  ],
  "power_kw": 90,
  "heat_kw": 20,
  "recipe_category": "smelting",
  "upgrade_slots": 2
}
```

### Ship / station module
```json
{
  "schema": 1,
  "id": "core:railgun_small",
  "category": "weapon",
  "socket": "hardpoint",
  "footprint": [2, 1, 3],
  "mass_t": 2.5,
  "power_kw": 150,
  "stats": { "damage": 42, "damage_type": "kinetic", "rate_hz": 1.2, "range_m": 1800 }
}
```

### Research node
```json
{
  "schema": 1,
  "id": "core:radiators",
  "tier": 3,
  "requires": ["core:heat_basics"],
  "cost": [{ "datum": "core:expansion", "count": 100 }],
  "unlocks": ["core:radiator_panel", "core:recipe_coolant"]
}
```

### System archetype
```json
{
  "schema": 1,
  "id": "core:dense_belt",
  "weight": 3,
  "star_types": ["yellow", "orange"],
  "bodies": [{ "kind": "asteroid_belt", "count": [2, 4], "richness": [0.8, 1.2] }],
  "modifiers": ["short_hauls"]
}
```

### Blueprint and ship design
Both are versioned JSON with a `kind`, a `schema`, an `entities` array (type, position, rotation, settings), `meta` (name, author, game version) and an optional base64 thumbnail.

## Runtime state versus content
- **Content** (immutable): the files above.
- **State** (saved): entity components, inventories, research progress, world deltas.
- A save stores content pack IDs and versions; loading refuses mismatched hashes unless migrated.

## Mod readiness
Content packs are folders with a manifest `pack.json` (id, version, dependencies). Loading order resolves overrides. Executable mod code is out of scope.

## Open questions
JSON vs a binary content format for load time; schema language (JSON Schema vs custom). See [12-open-questions](12-open-questions.md).
