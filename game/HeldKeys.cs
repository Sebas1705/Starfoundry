using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Held keys (movement, roll, run, boost, look) read through the action registry, so a rebound key moves the ship too. A key still on its
/// default slot is read by position (physical key, so WASD stays WASD on any layout); a key the player chose is read by the character it types.
/// </summary>
public static class HeldKeys
{
    public static bool Down(string actionId, int slot)
    {
        string name = InputActions.KeysOf(actionId)[slot];
        if (name == KeyRules.Unbound || !Enum.TryParse<Key>(name, out var key)) return false;
        bool byPosition = name == InputActions.Get(actionId).Keys[slot] && key is not (Key.Shift or Key.Alt);
        return byPosition ? Input.IsPhysicalKeyPressed(key) : Input.IsKeyPressed(key);
    }

    /// <summary>+1 while the positive slot is held, -1 while the negative one is, 0 for both or neither.</summary>
    public static int Axis(string actionId, int positive, int negative) => (Down(actionId, positive) ? 1 : 0) - (Down(actionId, negative) ? 1 : 0);
}
