namespace Starfoundry.Sim;

/// <summary>A module of a structure template at a local module coordinate.</summary>
public sealed record TemplateModule(int Mx, int My, int Mz, ModuleKind Kind);

/// <summary>A pre-built machine at a local cell. <see cref="Name"/> is only used inside the template to wire belts.</summary>
public sealed record TemplateMachine(string Name, int X, int Y, int Z, int Type, int Recipe);

/// <summary>A pre-built belt segment at local cells. <see cref="From"/> and <see cref="To"/> index the template's machines (-1 for none):
/// the machine at <see cref="From"/> feeds the first cell, the last cell feeds the machine at <see cref="To"/>.</summary>
public sealed record TemplateBelt(int X, int Y, int Z, Dir Dir, int Length, BeltTier Tier, int From, int To);

/// <summary>
/// A structure that is built whole at world creation, at no cost: its modules, machines and belts in local cells.
/// Part of the content (and so of the content hash); see <c>structure_templates</c> in the content file.
/// </summary>
public sealed record StructureTemplate(TemplateModule[] Modules, TemplateMachine[] Machines, TemplateBelt[] Belts);
