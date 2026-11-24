using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--inventory-shot=path</c>: the personal inventory (ADR 0035) in the structures campaign. At 1280x720 and at 1920x1080, in Spanish and in English, with the reactor
/// panel open (fuel in its slot, the auto-feed selector on "hold, then inventory"): <c>path.1280x720.es.reactor.png</c> and so on. At 1280x720 in English it also shows the
/// hold beside the bag with the ore filter on (<c>path.1280x720.en.hold.png</c>), a machine's input and output (<c>.machine.png</c>) and the bin's question (<c>.bin.png</c>).
/// Seed fixed, nothing is saved. Items are handed out by setup code before the first shot; the moves into the reactor are real commands.
/// </summary>
public partial class Play
{
    private string? _inventoryShotPath;
    private int _invShotFrame;
    private readonly List<(int Frame, Action Do)> _invShotPlan = new();

    private void InventoryShotSetup(string[] args)
    {
        _wantCapture = false;
        if (args.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
        if (args.Contains("--ui-lang=en")) Loc.Lang = Language.En;
        if (!_structuresOn || ShipStruct < 0) { GD.Print("INVSHOT no structure world"); return; }
        string path = _inventoryShotPath!;
        int at = 3;
        void Plan(int gap, Action a) { at += gap; _invShotPlan.Add((at, a)); }
        void Shot(string size, string lang, string name) => Plan(14, () =>
        {
            string file = $"{path}.{size}.{lang}.{name}.png";
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print("INVSHOT saved " + file + " " + GetViewport().GetVisibleRect().Size);
        });
        void Size(int w, int h) => Plan(2, () => SetWindowSize(GetWindow(), w, h));
        void Lang(Language l) => Plan(2, () => Loc.Lang = l);

        Plan(3, InventoryShotItems);
        foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
        {
            string size = $"{w}x{h}";
            Size(w, h);
            Plan(3, () => { InventoryShotOpenReactor(); });
            Plan(4, InventoryShotFeed);
            foreach (var (lang, tag) in new[] { (Language.Es, "es"), (Language.En, "en") })
            {
                Lang(lang);
                Shot(size, tag, "reactor");
            }
            if (w != 1280) continue;
            Plan(2, () => { _inventory!.Open(ContainerRef.Hold); _inventory.SetFilter(ItemCategory.Ore); });
            Plan(4, () => _inventory!.SelectSlot(_inventory.FindSlot(ContainerRef.Person, _content.ItemIndex("core:iron_ore"))));
            Shot(size, "en", "hold");
            Plan(2, () => { _inventory!.SetFilter(ItemCategory.All); if (InventoryShotMachine() is { } m) _inventory.Open(ContainerRef.MachineIn(m), m); });
            Shot(size, "en", "machine");
            Plan(2, () =>
            {
                _inventory!.Open(ContainerRef.Hold);
                if (_inventory.FindSlot(ContainerRef.Person, _content.ItemIndex("core:iron_ore")) is { } s) _inventory.AskBinFor(s);
            });
            Shot(size, "en", "bin");
        }
        Plan(2, () => GetTree().Quit());
    }

    private bool _invAuditFed;

    /// <summary>Fills the bag once for the UI audit, so the panel is audited with items in it.</summary>
    private void InventoryAuditFeed()
    {
        if (_invAuditFed) return;
        _invAuditFed = true;
        InventoryShotItems();
    }

    private int? InventoryShotMachine()
    {
        foreach (int m in _machineIds)
            if (Net.IsMachineAlive(m)) return m;
        return null;
    }

    private void InventoryShotItems()
    {
        int Item(string id) => _content.ItemIndex(id);
        foreach (var (id, n) in new[] { ("core:iron_ore", 100), ("core:iron_ore", 62), ("core:iron_plate", 45), ("core:carbon", 30), ("core:ammo", 40), ("core:hydrogen", 12),
                     ("core:fuel_cell", 20), ("core:fuel_cell", 7), ("core:titanium_ingot", 18), ("core:circuit", 9), ("core:ice", 25), ("core:quartz", 5) })
            Net.GiveInventory(Player, Item(id), n);
        _world.Step(30);
    }

    private void InventoryShotOpenReactor()
    {
        var s = Net.GetStructure(ShipStruct);
        var key = s.Modules.First(kv => ModuleKindIds.IsReactor(kv.Value)).Key;
        _inventory!.Open(ContainerRef.Reactor(ShipStruct, key.Mx, key.My, key.Mz));
    }

    private void InventoryShotFeed()
    {
        var o = _inventory!.Other;
        if (o.Kind != ContainerKind.Reactor) return;
        Send(new MoveItems(ContainerRef.Person, FirstSlotOf("core:fuel_cell"), o, InventoryRules.ReactorSlot, 12));
        Send(new SetFeedSource(o.Id, o.Mx, o.My, o.Mz, FeedSource.HoldThenInventory));
    }

    private int FirstSlotOf(string id)
    {
        int item = _content.ItemIndex(id);
        for (int i = 0; i < InventoryRules.Slots; i++)
            if (Net.InventoryStack(Player, i) is { } st && st.Item == item) return i;
        return 0;
    }

    private void InventoryShotFrame()
    {
        if (_inventoryShotPath is null) return;
        _invShotFrame++;
        _wantCapture = false;
        foreach (var (frame, action) in _invShotPlan)
            if (frame == _invShotFrame) action();
    }
}
