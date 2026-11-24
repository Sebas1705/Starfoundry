namespace Starfoundry.Sim;

/// <summary>
/// Sets where the commanding player's guns point while they fire (ADR 0036): a direction in the ship's own frame (the ship's attitude turns it into the world),
/// each component in [-1024, 1024] (<see cref="PlayerRules.ThrustScale"/>) with a length of at least half that and at most 1024. The game sends the camera ray.
/// With an aim set, the guns fire whenever they are armed and ready, with or without an enemy: a projectile leaves the gun nearest to where the aim points and
/// hits the first thing on its path. (0, 0, 0) clears the aim and brings back the old behaviour: fire only at an enemy in range, which the sim picks.
/// Rejected with <see cref="Reject.InvalidArgument"/> for any other vector and with <see cref="Reject.NotAllowedHere"/> in a world with <see cref="WorldRules.LegacyFeel"/>.
/// </summary>
public sealed record SetAim(int Dx, int Dy, int Dz) : Command;
