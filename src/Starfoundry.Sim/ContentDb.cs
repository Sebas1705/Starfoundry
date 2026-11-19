using System.Text.Json;

namespace Starfoundry.Sim;

public sealed record ItemDef(int Index, string Id);

public sealed record ItemStack(int Item, int Count);

public sealed record MachineDef(int Index, string Id);

/// <param name="TimeTicks">Crafting time in simulation ticks (converted once at load from seconds).</param>
public sealed record RecipeDef(int Index, string Id, int Machine, ItemStack[] Inputs, ItemStack[] Outputs, int TimeTicks);

/// <summary>
/// Immutable game content loaded from data files: items, machine types and recipes. Simulation code
/// refers to them by index; files refer to them by string id. Loading is strict (unknown properties,
/// duplicates and dangling references are errors) and the <see cref="Hash"/> identifies the exact content,
/// so replays and co-op peers can detect a mismatch.
/// </summary>
public sealed class ContentDb
{
    public const int MaxItems = 256;
    public const int MaxStack = 64;
    public const int MaxRecipeStacks = 4;
    public const int MaxRecipeTicks = Units.TicksPerSecond * 600;

    private readonly Dictionary<string, int> _items = new();
    private readonly Dictionary<string, int> _machines = new();
    private readonly Dictionary<string, int> _recipes = new();

    public IReadOnlyList<ItemDef> Items { get; }
    public IReadOnlyList<MachineDef> Machines { get; }
    public IReadOnlyList<RecipeDef> Recipes { get; }
    public ulong Hash { get; }

    private ContentDb(List<ItemDef> items, List<MachineDef> machines, List<RecipeDef> recipes)
    {
        Items = items;
        Machines = machines;
        Recipes = recipes;
        foreach (var i in items) _items.Add(i.Id, i.Index);
        foreach (var m in machines) _machines.Add(m.Id, m.Index);
        foreach (var r in recipes) _recipes.Add(r.Id, r.Index);
        Hash = ComputeHash();
    }

    public int ItemIndex(string id) => _items.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown item '{id}'.");
    public int MachineIndex(string id) => _machines.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown machine '{id}'.");
    public int RecipeIndex(string id) => _recipes.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown recipe '{id}'.");

    public bool IsMachine(int index) => (uint)index < (uint)Machines.Count;
    public bool IsRecipe(int index) => (uint)index < (uint)Recipes.Count;

    /// <summary>Parses and validates a content file. Throws <see cref="InvalidDataException"/> with a precise message.</summary>
    public static ContentDb FromJson(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException e) { throw new InvalidDataException($"Invalid JSON: {e.Message}", e); }

        using (doc)
        {
            var root = doc.RootElement;
            RequireObject(root, "content", "schema", "items", "machines", "recipes");
            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1)
                throw new InvalidDataException("Unsupported or missing content schema (expected 1).");

            var items = new List<ItemDef>();
            var itemIds = new Dictionary<string, int>();
            foreach (var e in Array(root, "items"))
            {
                RequireObject(e, "item", "id");
                string id = Id(e, "item");
                if (!itemIds.TryAdd(id, items.Count)) throw new InvalidDataException($"Duplicate item '{id}'.");
                items.Add(new ItemDef(items.Count, id));
            }
            if (items.Count == 0 || items.Count > MaxItems) throw new InvalidDataException($"Item count must be 1 to {MaxItems}.");

            var machines = new List<MachineDef>();
            var machineIds = new Dictionary<string, int>();
            foreach (var e in Array(root, "machines"))
            {
                RequireObject(e, "machine", "id");
                string id = Id(e, "machine");
                if (!machineIds.TryAdd(id, machines.Count)) throw new InvalidDataException($"Duplicate machine '{id}'.");
                machines.Add(new MachineDef(machines.Count, id));
            }

            var recipes = new List<RecipeDef>();
            var recipeIds = new HashSet<string>();
            foreach (var e in Array(root, "recipes"))
            {
                RequireObject(e, "recipe", "id", "machine", "inputs", "outputs", "time_s");
                string id = Id(e, "recipe");
                if (!recipeIds.Add(id)) throw new InvalidDataException($"Duplicate recipe '{id}'.");

                string machineId = Str(e, "machine", id);
                if (!machineIds.TryGetValue(machineId, out int machine))
                    throw new InvalidDataException($"Recipe '{id}' uses unknown machine '{machineId}'.");

                var inputs = Stacks(e, "inputs", id, itemIds, allowEmpty: true);
                var outputs = Stacks(e, "outputs", id, itemIds, allowEmpty: false);

                if (!e.TryGetProperty("time_s", out var t) || t.ValueKind != JsonValueKind.Number || !t.TryGetDecimal(out decimal seconds))
                    throw new InvalidDataException($"Recipe '{id}' needs a numeric time_s.");
                int ticks = (int)Math.Round(seconds * Units.TicksPerSecond, MidpointRounding.AwayFromZero);
                if (ticks < 1 || ticks > MaxRecipeTicks)
                    throw new InvalidDataException($"Recipe '{id}' time_s must be between one tick and {MaxRecipeTicks / Units.TicksPerSecond} seconds.");

                recipes.Add(new RecipeDef(recipes.Count, id, machine, inputs, outputs, ticks));
            }

            return new ContentDb(items, machines, recipes);
        }
    }

    public static ContentDb FromFile(string path) => FromJson(File.ReadAllText(path));

    private static JsonElement.ArrayEnumerator Array(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"'{name}' must be an array.");
        return a.EnumerateArray();
    }

    private static void RequireObject(JsonElement e, string what, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Each {what} must be an object.");
        foreach (var p in e.EnumerateObject())
            if (System.Array.IndexOf(allowed, p.Name) < 0) throw new InvalidDataException($"Unknown property '{p.Name}' in {what}.");
    }

    private static string Id(JsonElement e, string what)
    {
        string id = Str(e, "id", what);
        int colon = id.IndexOf(':');
        if (colon <= 0 || colon == id.Length - 1 || id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or ':')) || id.Count(c => c == ':') != 1)
            throw new InvalidDataException($"Invalid {what} id '{id}' (expected pack:name in lower case).");
        return id;
    }

    private static string Str(JsonElement e, string name, string context)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(v.GetString()))
            throw new InvalidDataException($"'{name}' must be a non-empty string in {context}.");
        return v.GetString()!;
    }

    private static ItemStack[] Stacks(JsonElement recipe, string name, string recipeId, Dictionary<string, int> itemIds, bool allowEmpty)
    {
        var list = new List<ItemStack>();
        foreach (var s in Array(recipe, name))
        {
            RequireObject(s, "stack", "item", "count");
            string item = Str(s, "item", $"{name} of '{recipeId}'");
            if (!itemIds.TryGetValue(item, out int index))
                throw new InvalidDataException($"Recipe '{recipeId}' uses unknown item '{item}'.");
            if (!s.TryGetProperty("count", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out int count) || count < 1 || count > MaxStack)
                throw new InvalidDataException($"Recipe '{recipeId}' count must be 1 to {MaxStack}.");
            if (list.Any(x => x.Item == index))
                throw new InvalidDataException($"Recipe '{recipeId}' lists '{item}' twice in {name}.");
            list.Add(new ItemStack(index, count));
        }
        if (list.Count > MaxRecipeStacks) throw new InvalidDataException($"Recipe '{recipeId}' has more than {MaxRecipeStacks} {name}.");
        if (list.Count == 0 && !allowEmpty) throw new InvalidDataException($"Recipe '{recipeId}' needs at least one output.");
        return list.ToArray();
    }

    private ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; }
        }
        void MixStr(string s)
        {
            Mix(s.Length);
            foreach (char c in s) Mix(c);
        }

        Mix(Items.Count);
        foreach (var i in Items) MixStr(i.Id);
        Mix(Machines.Count);
        foreach (var m in Machines) MixStr(m.Id);
        Mix(Recipes.Count);
        foreach (var r in Recipes)
        {
            MixStr(r.Id); Mix(r.Machine); Mix(r.TimeTicks);
            Mix(r.Inputs.Length);
            foreach (var s in r.Inputs) { Mix(s.Item); Mix(s.Count); }
            Mix(r.Outputs.Length);
            foreach (var s in r.Outputs) { Mix(s.Item); Mix(s.Count); }
        }
        return h;
    }
}
