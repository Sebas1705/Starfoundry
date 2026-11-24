using System.Text.Json;

namespace Starfoundry.Sim;

/// <summary>
/// The scenarios of a game: loads and validates <c>content/scenarios/*.json</c> against a <see cref="ContentDb"/>. Loading is strict like
/// the content loader (unknown properties, duplicate ids, dangling item, node, module and template references and out-of-range numbers are
/// errors with a message that names the file's scenario and the field). See [ADR 0025](docs/adr/0025-scenarios-and-goals.md).
/// </summary>
public sealed class ScenarioCatalog
{
    public const int MaxStructures = 8, MaxGoals = 12, MaxStory = 40, MaxGoalCount = 100_000, MaxTimeLimitSeconds = 86_400, MaxOffsetCells = 400;

    private readonly Dictionary<string, ScenarioDef> _byId = new(StringComparer.Ordinal);

    /// <summary>Every scenario, by <see cref="ScenarioDef.Order"/> then id.</summary>
    public IReadOnlyList<ScenarioDef> All { get; }

    /// <summary>Hash of the whole set (every scenario's full hash, in id order): a cheap "did any scenario file change" check.</summary>
    public ulong Hash { get; }

    private ScenarioCatalog(List<ScenarioDef> all)
    {
        All = all.OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        foreach (var s in all) _byId.Add(s.Id, s);
        ulong h = 14695981039346656037UL;
        foreach (var s in all.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            for (int b = 0; b < 8; b++) { h ^= (byte)(s.FullHash >> (b * 8)); h *= 1099511628211UL; }
        }
        Hash = h;
    }

    public static ScenarioCatalog Empty { get; } = new(new List<ScenarioDef>());

    public ScenarioDef? Find(string id) => _byId.GetValueOrDefault(id);

    public ScenarioDef Get(string id) => Find(id) ?? throw new KeyNotFoundException($"Unknown scenario '{id}'.");

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Loads every <c>*.json</c> file of a folder (sorted by name, so errors are reproducible).</summary>
    /// <exception cref="InvalidDataException">A file is invalid; the message names the file.</exception>
    public static ScenarioCatalog FromDirectory(ContentDb content, string directory)
    {
        var files = Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var texts = new List<(string Name, string Json)>();
        foreach (var f in files) texts.Add((Path.GetFileName(f), File.ReadAllText(f)));
        return FromJson(content, texts);
    }

    /// <summary>Loads scenarios from (file name, JSON text) pairs. A scenario's id must equal its file name without the extension.</summary>
    public static ScenarioCatalog FromJson(ContentDb content, IEnumerable<(string Name, string Json)> files)
    {
        var all = new List<ScenarioDef>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, json) in files)
        {
            ScenarioDef def;
            try { def = Parse(content, json, name); }
            catch (InvalidDataException e) { throw new InvalidDataException($"Scenario file '{name}': {e.Message}", e); }
            string expected = Path.GetFileNameWithoutExtension(name);
            if (name.Length > 0 && def.Id != expected) throw new InvalidDataException($"Scenario file '{name}': the id '{def.Id}' must equal the file name '{expected}'.");
            if (!ids.Add(def.Id)) throw new InvalidDataException($"Scenario file '{name}': duplicate scenario id '{def.Id}'.");
            all.Add(def);
        }
        return new ScenarioCatalog(all);
    }

    /// <summary>Parses one scenario. Throws <see cref="InvalidDataException"/>.</summary>
    public static ScenarioDef Parse(ContentDb content, string json, string fileName = "")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }); }
        catch (JsonException e) { throw new InvalidDataException($"Invalid JSON: {e.Message}", e); }
        using (doc) return Build(content, doc.RootElement);
    }

    // ---- parsing ---------------------------------------------------------------------------------

    private static ScenarioDef Build(ContentDb content, JsonElement root)
    {
        Only(root, "scenario", "schema", "id", "order", "name_es", "name_en", "description_es", "description_en", "briefing_es", "briefing_en",
            "mode", "player_rules", "rules", "start", "goals", "story", "stars");
        if (!root.TryGetProperty("schema", out var sc) || sc.ValueKind != JsonValueKind.Number || sc.GetInt32() != 1)
            throw new InvalidDataException("unsupported or missing scenario schema (expected 1).");
        string id = Str(root, "id", "the scenario");
        if (id.Length > 40 || id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new InvalidDataException($"invalid scenario id '{id}' (lower case letters, digits and underscores, at most 40).");
        int order = Int(root, "order", id, 0, 100_000);
        string nameEs = Text(root, "name_es", id, 60), nameEn = Text(root, "name_en", id, 60);
        string descEs = Text(root, "description_es", id, 400), descEn = Text(root, "description_en", id, 400);
        string briefEs = Text(root, "briefing_es", id, 1500), briefEn = Text(root, "briefing_en", id, 1500);

        bool sandbox = false;
        if (root.TryGetProperty("mode", out _))
        {
            sandbox = Str(root, "mode", id) switch
            {
                "campaign" => false,
                "sandbox" => true,
                var m => throw new InvalidDataException($"scenario '{id}' has an unknown mode '{m}' (use campaign or sandbox)."),
            };
        }
        bool playerRules = root.TryGetProperty("player_rules", out var pr) && Bool(pr, "player_rules", id);

        // ---- rules and start ----
        string preset = "normal";
        var rules = WorldRules.Default;
        if (root.TryGetProperty("rules", out var rj)) rules = ParseRules(content, rj, id, out preset);

        ulong seed = 0;
        string? homeArchetype = null;
        var structures = new List<StructureStart>();
        var items = rules.StartingItems.ToList();
        if (root.TryGetProperty("start", out var sj))
        {
            Only(sj, "start", "structures", "starting_items", "seed", "home_archetype");
            if (sj.TryGetProperty("seed", out var sd))
            {
                if (sd.ValueKind != JsonValueKind.Number || !sd.TryGetInt64(out long s) || s < 0) throw new InvalidDataException($"scenario '{id}': 'seed' must be a non-negative integer.");
                seed = (ulong)s;
            }
            if (sj.TryGetProperty("home_archetype", out _))
            {
                homeArchetype = Str(sj, "home_archetype", id);
                var arch = content.Archetypes.FirstOrDefault(a => a.Id == homeArchetype);
                if (arch is null || !arch.IsHome) throw new InvalidDataException($"scenario '{id}': home_archetype '{homeArchetype}' is not a home archetype of the content.");
            }
            if (sj.TryGetProperty("starting_items", out var si))
                foreach (var st in Items(content, si, id, "start.starting_items")) items.Add(st);
            if (sj.TryGetProperty("structures", out var sts))
            {
                if (sts.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"scenario '{id}': 'structures' must be an array.");
                foreach (var e in sts.EnumerateArray())
                {
                    Only(e, "structure", "template", "kind", "offset");
                    string tn = Str(e, "template", id);
                    if (!content.Templates.ContainsKey(tn)) throw new InvalidDataException($"scenario '{id}' uses unknown structure template '{tn}'.");
                    var kind = structures.Count == 0 ? StructureKind.Ship : StructureKind.Station;
                    if (e.TryGetProperty("kind", out _))
                        kind = Str(e, "kind", id) switch
                        {
                            "ship" => StructureKind.Ship,
                            "station" => StructureKind.Station,
                            var k => throw new InvalidDataException($"scenario '{id}' has an unknown structure kind '{k}' (use ship or station)."),
                        };
                    var off = new[] { 0, 0, 0 };
                    if (e.TryGetProperty("offset", out var oj))
                    {
                        if (oj.ValueKind != JsonValueKind.Array || oj.GetArrayLength() != 3 || oj.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out _)))
                            throw new InvalidDataException($"scenario '{id}': a structure 'offset' must be three integers (cells).");
                        off = oj.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                        if (off.Any(v => Math.Abs(v) > MaxOffsetCells)) throw new InvalidDataException($"scenario '{id}': a structure offset is beyond {MaxOffsetCells} cells.");
                    }
                    structures.Add(new StructureStart(tn, kind, off));
                }
            }
        }
        if (structures.Count == 0) structures.Add(new StructureStart("starter_ship", StructureKind.Ship, new[] { 0, 0, 0 }));
        if (structures.Count > MaxStructures) throw new InvalidDataException($"scenario '{id}' has more than {MaxStructures} start structures.");
        if (structures[0].Kind != StructureKind.Ship) throw new InvalidDataException($"scenario '{id}': the first start structure must be the player's ship.");
        if (structures[0].OffsetCells.Any(v => v != 0)) throw new InvalidDataException($"scenario '{id}': the ship starts at the home origin (offset 0, 0, 0).");

        // Starting items of the rules block and of the start block together, merged per item.
        var merged = new List<ItemStack>();
        foreach (var s in items)
        {
            int at = merged.FindIndex(m => m.Item == s.Item);
            if (at >= 0) merged[at] = new ItemStack(s.Item, merged[at].Count + s.Count); else merged.Add(s);
        }
        rules = new WorldRulesCopy(rules).With(merged);
        if (rules.Validate(content) is { } problem) throw new InvalidDataException($"scenario '{id}' has invalid rules: {problem}.");

        // ---- goals ----
        var primary = new List<GoalDef>();
        var secondary = new List<GoalDef>();
        var goalIds = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("goals", out var gj))
        {
            Only(gj, "goals", "primary", "secondary");
            if (gj.TryGetProperty("primary", out var p)) foreach (var g in Goals(content, p, id, goalIds)) primary.Add(g);
            if (gj.TryGetProperty("secondary", out var s2)) foreach (var g in Goals(content, s2, id, goalIds)) secondary.Add(g);
        }
        if (primary.Count + secondary.Count > MaxGoals) throw new InvalidDataException($"scenario '{id}' has more than {MaxGoals} goals.");
        if (primary.Any(g => g.IsCondition) && primary.All(g => g.IsCondition))
            throw new InvalidDataException($"scenario '{id}' needs at least one primary goal that can be completed.");
        foreach (var g in primary.Concat(secondary))
        {
            if (g.Type == GoalType.VisitSystems && g.Count > content.Galaxy?.SystemCount) throw new InvalidDataException($"scenario '{id}': goal '{g.Id}' asks for more systems than the galaxy has.");
        }

        // ---- story ----
        var story = new List<StoryBeat>();
        if (root.TryGetProperty("story", out var stj))
        {
            if (stj.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"scenario '{id}': 'story' must be an array.");
            foreach (var e in stj.EnumerateArray())
            {
                Only(e, "story beat", "trigger", "speaker", "text_es", "text_en");
                string trigger = Str(e, "trigger", id);
                CheckTrigger(trigger, id, goalIds);
                string speaker = e.TryGetProperty("speaker", out _) ? Str(e, "speaker", id) : "ORCA";
                if (speaker.Length > 30) throw new InvalidDataException($"scenario '{id}': a speaker name is longer than 30 characters.");
                story.Add(new StoryBeat(trigger, speaker, Text(e, "text_es", id, 400), Text(e, "text_en", id, 400)));
            }
        }
        if (story.Count > MaxStory) throw new InvalidDataException($"scenario '{id}' has more than {MaxStory} story beats.");

        // ---- stars ----
        var stars = new StarsRule(0, 0, 0, -1);
        if (root.TryGetProperty("stars", out var starsJ))
        {
            Only(starsJ, "stars", "two_star_seconds", "three_star_seconds", "two_star_secondary", "three_star_secondary");
            int two = starsJ.TryGetProperty("two_star_seconds", out _) ? Int(starsJ, "two_star_seconds", id, 0, MaxTimeLimitSeconds) : 0;
            int three = starsJ.TryGetProperty("three_star_seconds", out _) ? Int(starsJ, "three_star_seconds", id, 0, MaxTimeLimitSeconds) : 0;
            int twoS = starsJ.TryGetProperty("two_star_secondary", out _) ? Int(starsJ, "two_star_secondary", id, -1, MaxGoals) : 0;
            int threeS = starsJ.TryGetProperty("three_star_secondary", out _) ? Int(starsJ, "three_star_secondary", id, -1, MaxGoals) : -1;
            if (two > 0 && three > 0 && three > two) throw new InvalidDataException($"scenario '{id}': three_star_seconds must not exceed two_star_seconds.");
            if (twoS > secondary.Count || threeS > secondary.Count) throw new InvalidDataException($"scenario '{id}': star secondary counts exceed the number of secondary goals.");
            stars = new StarsRule(two, three, twoS, threeS);
        }

        var def = new ScenarioDef
        {
            Id = id, NameEs = nameEs, NameEn = nameEn, DescriptionEs = descEs, DescriptionEn = descEn, BriefingEs = briefEs, BriefingEn = briefEn,
            Order = order, Preset = preset, Rules = rules, SandboxMode = sandbox, PlayerRules = playerRules, Seed = seed, HomeArchetype = homeArchetype,
            Structures = structures, Primary = primary, Secondary = secondary, Story = story, Stars = stars,
        };
        return WithHashes(def);
    }

    // The hashes need the finished definition, so they are computed on a copy.
    private static ScenarioDef WithHashes(ScenarioDef d) => new()
    {
        Id = d.Id, NameEs = d.NameEs, NameEn = d.NameEn, DescriptionEs = d.DescriptionEs, DescriptionEn = d.DescriptionEn, BriefingEs = d.BriefingEs,
        BriefingEn = d.BriefingEn, Order = d.Order, Preset = d.Preset, Rules = d.Rules, SandboxMode = d.SandboxMode, PlayerRules = d.PlayerRules,
        Seed = d.Seed, HomeArchetype = d.HomeArchetype, Structures = d.Structures, Primary = d.Primary, Secondary = d.Secondary, Story = d.Story,
        Stars = d.Stars, SimHash = ComputeHash(d, simOnly: true), FullHash = ComputeHash(d, simOnly: false),
    };

    private static void CheckTrigger(string trigger, string id, HashSet<string> goalIds)
    {
        if (trigger is "start" or "win" or "lose") return;
        int colon = trigger.IndexOf(':');
        if (colon > 0)
        {
            string kind = trigger[..colon], arg = trigger[(colon + 1)..];
            if (kind is "goal" or "fail" && goalIds.Contains(arg)) return;
            if (kind == "time" && int.TryParse(arg, out int secs) && secs >= 0 && secs <= MaxTimeLimitSeconds) return;
        }
        throw new InvalidDataException($"scenario '{id}' has a story trigger '{trigger}' that is not start, win, lose, goal:<id>, fail:<id> or time:<seconds> of a known goal.");
    }

    private static IEnumerable<GoalDef> Goals(ContentDb content, JsonElement list, string id, HashSet<string> goalIds)
    {
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"scenario '{id}': goal lists must be arrays.");
        foreach (var e in list.EnumerateArray())
        {
            Only(e, "goal", "id", "type", "item", "node", "kind", "count", "time_limit_seconds", "text_es", "text_en");
            string gid = Str(e, "id", id);
            if (gid.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))) throw new InvalidDataException($"scenario '{id}': invalid goal id '{gid}'.");
            if (!goalIds.Add(gid)) throw new InvalidDataException($"scenario '{id}' uses the goal id '{gid}' twice.");
            string tn = Str(e, "type", id);
            var type = tn switch
            {
                "survive_waves" => GoalType.SurviveWaves,
                "research" => GoalType.Research,
                "produce" => GoalType.Produce,
                "mine" => GoalType.Mine,
                "visit_systems" => GoalType.VisitSystems,
                "found_stations" => GoalType.FoundStations,
                "build_module" => GoalType.BuildModule,
                "keep_alive" => GoalType.KeepAlive,
                "keep_fuel" => GoalType.KeepFuel,
                "have_module" => GoalType.HaveModule,
                "salvage" => GoalType.Salvage,
                "dock" => GoalType.Dock,
                "stock" => GoalType.Stock,
                "ship_speed" => GoalType.ShipSpeed,
                "kill_enemies" => GoalType.KillEnemies,
                _ => throw new InvalidDataException($"scenario '{id}': goal '{gid}' has an unknown type '{tn}'."),
            };
            string what = $"goal '{gid}' of scenario '{id}'";
            int item = -1, node = -1, count = 0;
            ModuleKind? kind = null;
            bool needsItem = type is GoalType.Produce or GoalType.Mine or GoalType.Stock, needsNode = type == GoalType.Research,
                needsKind = type is GoalType.BuildModule or GoalType.KeepAlive or GoalType.HaveModule,
                needsCount = type is not (GoalType.Research or GoalType.KeepAlive or GoalType.KeepFuel or GoalType.Dock);
            void Reject(string field, bool needed)
            {
                if (!needed && e.TryGetProperty(field, out _)) throw new InvalidDataException($"{what} (type {tn}) does not take '{field}'.");
            }
            Reject("item", needsItem); Reject("node", needsNode); Reject("kind", needsKind); Reject("count", needsCount);
            if (needsItem)
            {
                string iid = Str(e, "item", what);
                try { item = content.ItemIndex(iid); } catch (KeyNotFoundException) { throw new InvalidDataException($"{what} uses unknown item '{iid}'."); }
            }
            if (needsNode)
            {
                string nid = Str(e, "node", what);
                try { node = content.ResearchIndex(nid); } catch (KeyNotFoundException) { throw new InvalidDataException($"{what} uses unknown research '{nid}'."); }
            }
            if (needsKind && (type is GoalType.BuildModule or GoalType.HaveModule || e.TryGetProperty("kind", out _)))
            {
                string kn = Str(e, "kind", what);
                var found = Enum.GetValues<ModuleKind>().Where(k => ModuleKindIds.Id(k) == kn).Select(k => (ModuleKind?)k).FirstOrDefault();
                kind = found ?? throw new InvalidDataException($"{what} uses unknown module kind '{kn}'.");
            }
            if (needsCount && (type != GoalType.SurviveWaves || e.TryGetProperty("count", out _)))
            {
                int max = type switch { GoalType.SurviveWaves => WorldRules.MaxGoalWaves, GoalType.VisitSystems => 64, GoalType.FoundStations => 20, GoalType.BuildModule or GoalType.HaveModule => 50, GoalType.Salvage => 20, GoalType.ShipSpeed => StructureFlight.MaxSpeedPercent, _ => MaxGoalCount };
                count = Int(e, "count", what, 1, max);
            }
            int limit = e.TryGetProperty("time_limit_seconds", out _) ? Int(e, "time_limit_seconds", what, 0, MaxTimeLimitSeconds) : 0;
            if (limit is > 0 and < 30) throw new InvalidDataException($"{what} has a time limit under 30 seconds.");
            yield return new GoalDef(gid, type, item, node, kind, count, limit, Text(e, "text_es", what, 160), Text(e, "text_en", what, 160));
        }
    }

    private static WorldRules ParseRules(ContentDb content, JsonElement rj, string id, out string preset)
    {
        Only(rj, "rules", "preset", "enemy_damage_percent", "enemy_hp_percent", "wave_size_percent", "wave_interval_percent", "first_wave_grace_seconds",
            "deposit_richness_percent", "power_output_percent", "threats_enabled", "free_build", "goal_waves", "starting_items");
        preset = "normal";
        var baseRules = WorldRules.Default;
        if (rj.TryGetProperty("preset", out _))
        {
            preset = Str(rj, "preset", id);
            baseRules = WorldRules.Preset(preset) ?? throw new InvalidDataException($"scenario '{id}' has an unknown rules preset '{preset}' (relaxed, normal, hard or brutal).");
            preset = preset.ToLowerInvariant();
        }
        int G(string field, int current, int min, int max) => rj.TryGetProperty(field, out _) ? Int(rj, field, id, min, max) : current;
        var items = baseRules.StartingItems.ToList();
        if (rj.TryGetProperty("starting_items", out var si)) items.AddRange(Items(content, si, id, "rules.starting_items"));
        return new WorldRules
        {
            EnemyDamagePercent = G("enemy_damage_percent", baseRules.EnemyDamagePercent, WorldRules.MinPercent, WorldRules.MaxPercent),
            EnemyHpPercent = G("enemy_hp_percent", baseRules.EnemyHpPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
            WaveSizePercent = G("wave_size_percent", baseRules.WaveSizePercent, WorldRules.MinPercent, WorldRules.MaxPercent),
            WaveIntervalPercent = G("wave_interval_percent", baseRules.WaveIntervalPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
            FirstWaveGraceSeconds = G("first_wave_grace_seconds", baseRules.FirstWaveGraceSeconds, WorldRules.ContentGrace, WorldRules.MaxGraceSeconds),
            DepositRichnessPercent = G("deposit_richness_percent", baseRules.DepositRichnessPercent, WorldRules.MinRichness, WorldRules.MaxRichness),
            PowerOutputPercent = G("power_output_percent", baseRules.PowerOutputPercent, WorldRules.MinPercent, WorldRules.MaxPercent),
            GoalWaves = G("goal_waves", baseRules.GoalWaves, WorldRules.MinGoalWaves, WorldRules.MaxGoalWaves),
            ResourceDensityPercent = baseRules.ResourceDensityPercent,
            ThreatsEnabled = rj.TryGetProperty("threats_enabled", out var te) ? Bool(te, "threats_enabled", id) : baseRules.ThreatsEnabled,
            FreeBuild = rj.TryGetProperty("free_build", out var fb) ? Bool(fb, "free_build", id) : baseRules.FreeBuild,
            StartingItems = items,
        };
    }

    private static List<ItemStack> Items(ContentDb content, JsonElement list, string id, string field)
    {
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"scenario '{id}': '{field}' must be an array.");
        var result = new List<ItemStack>();
        foreach (var e in list.EnumerateArray())
        {
            Only(e, "stack", "item", "count");
            string iid = Str(e, "item", id);
            int item;
            try { item = content.ItemIndex(iid); } catch (KeyNotFoundException) { throw new InvalidDataException($"scenario '{id}' uses unknown item '{iid}' in {field}."); }
            if (result.Any(s => s.Item == item)) throw new InvalidDataException($"scenario '{id}' lists '{iid}' twice in {field}.");
            result.Add(new ItemStack(item, Int(e, "count", id, 1, WorldRules.MaxStackCount)));
        }
        return result;
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static void Only(JsonElement e, string what, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"each {what} must be an object.");
        foreach (var p in e.EnumerateObject())
            if (System.Array.IndexOf(allowed, p.Name) < 0) throw new InvalidDataException($"unknown property '{p.Name}' in {what}.");
    }

    private static string Str(JsonElement e, string name, string context)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(v.GetString()))
            throw new InvalidDataException($"'{name}' must be a non-empty string in {context}.");
        return v.GetString()!;
    }

    private static string Text(JsonElement e, string name, string context, int max)
    {
        string s = Str(e, name, context);
        if (s.Length > max) throw new InvalidDataException($"'{name}' in {context} is longer than {max} characters.");
        if (s.Trim() != s || s.Any(char.IsControl)) throw new InvalidDataException($"'{name}' in {context} has leading or trailing blanks or control characters.");
        return s;
    }

    private static int Int(JsonElement e, string name, string context, int min, int max)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n) || n < min || n > max)
            throw new InvalidDataException($"'{name}' in {context} must be an integer from {min} to {max}.");
        return n;
    }

    private static bool Bool(JsonElement v, string name, string context)
    {
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"'{name}' in {context} must be true or false.");
        return v.GetBoolean();
    }

    // ---- hashing ---------------------------------------------------------------------------------

    private static ulong ComputeHash(ScenarioDef d, bool simOnly)
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; }
        }
        void MixStr(string? s)
        {
            if (s is null) { Mix(-1); return; }
            Mix(s.Length);
            foreach (char c in s) Mix(c);
        }
        MixStr(d.Id);
        Mix(d.SandboxMode ? 1 : 0);
        Mix((long)d.Seed);
        foreach (byte b in d.Rules.Serialize()) Mix(b);
        Mix(d.Structures.Count);
        foreach (var s in d.Structures)
        {
            MixStr(s.Template); Mix((int)s.Kind);
            foreach (int o in s.OffsetCells) Mix(o);
        }
        if (simOnly) return h;

        MixStr(d.NameEs); MixStr(d.NameEn); MixStr(d.DescriptionEs); MixStr(d.DescriptionEn); MixStr(d.BriefingEs); MixStr(d.BriefingEn);
        Mix(d.Order); MixStr(d.Preset); Mix(d.PlayerRules ? 1 : 0); MixStr(d.HomeArchetype);
        foreach (var list in new[] { d.Primary, d.Secondary })
        {
            Mix(list.Count);
            foreach (var g in list)
            {
                MixStr(g.Id); Mix((int)g.Type); Mix(g.Item); Mix(g.Node); Mix(g.Kind is { } gk ? (int)gk : -1); Mix(g.Count); Mix(g.TimeLimitSeconds); MixStr(g.TextEs); MixStr(g.TextEn);
            }
        }
        Mix(d.Story.Count);
        foreach (var b in d.Story) { MixStr(b.Trigger); MixStr(b.Speaker); MixStr(b.TextEs); MixStr(b.TextEn); }
        Mix(d.Stars.TwoStarSeconds); Mix(d.Stars.ThreeStarSeconds); Mix(d.Stars.TwoStarSecondary); Mix(d.Stars.ThreeStarSecondary);
        return h;
    }

    /// <summary>Tiny helper to copy a <see cref="WorldRules"/> with other starting items (its other values are init-only).</summary>
    private readonly struct WorldRulesCopy
    {
        private readonly WorldRules _r;
        public WorldRulesCopy(WorldRules r) => _r = r;

        public WorldRules With(IReadOnlyList<ItemStack> items) => new()
        {
            EnemyDamagePercent = _r.EnemyDamagePercent, EnemyHpPercent = _r.EnemyHpPercent, WaveSizePercent = _r.WaveSizePercent,
            WaveIntervalPercent = _r.WaveIntervalPercent, FirstWaveGraceSeconds = _r.FirstWaveGraceSeconds, DepositRichnessPercent = _r.DepositRichnessPercent,
            PowerOutputPercent = _r.PowerOutputPercent, GoalWaves = _r.GoalWaves, ThreatsEnabled = _r.ThreatsEnabled, FreeBuild = _r.FreeBuild,
            ResourceDensityPercent = _r.ResourceDensityPercent, StartingItems = items,
        };
    }
}
