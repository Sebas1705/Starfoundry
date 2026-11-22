namespace Starfoundry.Sim;

/// <summary>What a goal asks for ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)).</summary>
public enum GoalType
{
    /// <summary>Clear <see cref="GoalDef.Count"/> waves (the rules' goal waves when the count is 0).</summary>
    SurviveWaves,

    /// <summary>Finish the research node <see cref="GoalDef.Node"/>.</summary>
    Research,

    /// <summary>Make <see cref="GoalDef.Count"/> units of <see cref="GoalDef.Item"/> (crafting, drills, hand mining and drones all count).</summary>
    Produce,

    /// <summary>Take <see cref="GoalDef.Count"/> units of <see cref="GoalDef.Item"/> out of deposits (by hand, with drones or drills).</summary>
    Mine,

    /// <summary>Have been in <see cref="GoalDef.Count"/> systems, home included.</summary>
    VisitSystems,

    /// <summary>Found <see cref="GoalDef.Count"/> stations.</summary>
    FoundStations,

    /// <summary>Place <see cref="GoalDef.Count"/> modules of <see cref="GoalDef.Kind"/>.</summary>
    BuildModule,

    /// <summary>Holds while no module of <see cref="GoalDef.Kind"/> (of any kind when null) has been destroyed. Never "done": it only fails.</summary>
    KeepAlive,

    /// <summary>Holds while no reactor has run dry of fuel. Never "done": it only fails.</summary>
    KeepFuel,
}

/// <summary>One goal of a scenario. Item, node and kind are resolved against the content when the scenario loads.</summary>
/// <param name="Id">Unique within the scenario; story beats refer to it as <c>goal:id</c>.</param>
/// <param name="Kind">The module kind of a build or keep-alive goal; null for a keep-alive goal that covers every kind.</param>
/// <param name="Count">Target amount (waves, items, systems, stations or modules); 0 for goals without one, and for a survive-waves goal that takes the rules' goal waves.</param>
/// <param name="TimeLimitSeconds">0 for none. A goal that is not done by then is failed (a failed primary goal loses the scenario).</param>
public sealed record GoalDef(string Id, GoalType Type, int Item, int Node, ModuleKind? Kind, int Count, int TimeLimitSeconds,
    string TextEs, string TextEn)
{
    /// <summary>True for goals that only hold or fail (they never complete).</summary>
    public bool IsCondition => Type is GoalType.KeepAlive or GoalType.KeepFuel;
}

/// <summary>A line of the story, shown when its trigger happens: <c>start</c>, <c>win</c>, <c>lose</c>, <c>goal:id</c> (a goal is done),
/// <c>fail:id</c> (a goal failed) or <c>time:seconds</c> (that much play time has passed).</summary>
public sealed record StoryBeat(string Trigger, string Speaker, string TextEs, string TextEn);

/// <summary>A structure built at world creation from a content template, <see cref="OffsetCells"/> away from the home origin.</summary>
public sealed record StructureStart(string Template, StructureKind Kind, int[] OffsetCells);

/// <summary>Star rules: one star for a win, two when the win is within <see cref="TwoStarSeconds"/> with at least
/// <see cref="TwoStarSecondary"/> secondary goals done, three likewise with the three-star values. 0 seconds means no time bound;
/// a secondary count of -1 means all of them.</summary>
public sealed record StarsRule(int TwoStarSeconds, int ThreeStarSeconds, int TwoStarSecondary, int ThreeStarSecondary);

/// <summary>
/// A scenario: the rules, start, goals and story of one game, loaded from <c>content/scenarios/*.json</c> by
/// <see cref="ScenarioCatalog"/>. Immutable. <see cref="Rules"/> are the effective rules (the preset, the explicit overrides and the
/// starting items of the rules block and of the start block), which is what the command-log header stores.
/// </summary>
public sealed class ScenarioDef
{
    public required string Id { get; init; }
    public required string NameEs { get; init; }
    public required string NameEn { get; init; }
    public required string DescriptionEs { get; init; }
    public required string DescriptionEn { get; init; }
    public required string BriefingEs { get; init; }
    public required string BriefingEn { get; init; }

    /// <summary>Position in the level list (ascending); the campaign and the sandbox use large numbers.</summary>
    public int Order { get; init; }

    /// <summary>The preset the rules started from (<c>relaxed</c>, <c>normal</c>, <c>hard</c>, <c>brutal</c>).</summary>
    public required string Preset { get; init; }

    /// <summary>The effective rules: preset, overrides and every starting item.</summary>
    public required WorldRules Rules { get; init; }

    /// <summary>Sandbox mode (everything unlocked and free) instead of the campaign's costs and research gates.</summary>
    public bool SandboxMode { get; init; }

    /// <summary>True for the sandbox templates: the game lets the player choose the rules and the seed.</summary>
    public bool PlayerRules { get; init; }

    /// <summary>The galaxy seed, or 0 when the game chooses one at random.</summary>
    public ulong Seed { get; init; }

    /// <summary>The home archetype id, when the scenario names one (only the content's home archetype is accepted today).</summary>
    public string? HomeArchetype { get; init; }

    /// <summary>The structures built at the start, the player's ship first.</summary>
    public required IReadOnlyList<StructureStart> Structures { get; init; }

    public required IReadOnlyList<GoalDef> Primary { get; init; }
    public required IReadOnlyList<GoalDef> Secondary { get; init; }
    public required IReadOnlyList<StoryBeat> Story { get; init; }
    public required StarsRule Stars { get; init; }

    /// <summary>All goals, primary first.</summary>
    public IEnumerable<GoalDef> AllGoals => Primary.Concat(Secondary);

    /// <summary>True when the scenario has no goals (the sandbox): it never ends by itself.</summary>
    public bool IsOpenEnded => Primary.Count == 0;

    /// <summary>Hash of everything that changes how the world is built (id, mode, rules, seed, structures). Goals, stars and texts are not
    /// part of it: fixing a translation must not invalidate saves.</summary>
    public ulong SimHash { get; init; }

    /// <summary>Hash of the whole definition, texts included.</summary>
    public ulong FullHash { get; init; }

    public string Name(bool spanish) => spanish ? NameEs : NameEn;
    public string Description(bool spanish) => spanish ? DescriptionEs : DescriptionEn;
    public string Briefing(bool spanish) => spanish ? BriefingEs : BriefingEn;

    /// <summary>The value stored in the command-log header for a world made from this scenario: the content hash mixed with <see cref="SimHash"/>.</summary>
    public ulong HeaderHash(ContentDb content)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v)
        {
            for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; }
        }
        Mix(content.Hash);
        Mix(SimHash);
        return h;
    }
}
