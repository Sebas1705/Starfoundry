using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class LoopTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int PlateItem = C.ItemIndex("core:iron_plate");
    private static readonly int CarbonItem = C.ItemIndex("core:carbon");
    private static readonly int Extractor = C.MachineIndex("core:extractor");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Assembler = C.MachineIndex("core:assembler");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Refinery = C.MachineIndex("core:micro_refinery");
    private static readonly int DrillIron = C.RecipeIndex("core:drill_iron");
    private static readonly int DrillCarbon = C.RecipeIndex("core:drill_carbon");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int PlateRecipe = C.RecipeIndex("core:iron_plate");
    private static readonly int ShipSmelt = C.RecipeIndex("core:ship_smelt");
    private static readonly int Logistics = C.ResearchIndex("core:logistics");
    private static readonly int CarbonDrilling = C.ResearchIndex("core:carbon_drilling");
    private static readonly int AdvancedLogistics = C.ResearchIndex("core:advanced_logistics");

    private const int Hand = 0;

    // Costs come from the content so these tests follow balance changes.
    private static int Cost(string machine, string item) =>
        C.Machines[C.MachineIndex(machine)].Cost.Where(s => s.Item == C.ItemIndex(item)).Sum(s => s.Count);
    private static readonly int SmelterCost = Cost("core:smelter", "core:iron_ingot");
    private static readonly int DepotCost = Cost("core:depot", "core:iron_ingot");
    private static readonly int ChainCost = Cost("core:drill", "core:iron_ingot") + SmelterCost
        + Cost("core:assembler", "core:iron_ingot") + Cost("core:research_lab", "core:iron_ingot");
    private static readonly int LabPlates = Cost("core:research_lab", "core:iron_plate");
    private static readonly int LogisticsPlates = C.Research[Logistics].Cost.Sum(s => s.Count);

    /// <summary>A campaign world with an iron deposit (id 0) and a carbon deposit (id 1).</summary>
    private static SimWorld Campaign(int ironAmount = 500)
    {
        var w = new SimWorld(new BeltNetwork(C, GameMode.Campaign, rules: new WorldRules().WithLegacyFeel(true))); // the hold limit of before ADR 0036 (a full hold overflows now)
        w.Network.AddDeposit(Ore, ironAmount);
        w.Network.AddDeposit(CarbonItem, 200);
        return w;
    }

    private static CommandResult Run(SimWorld w, Command c, int player = Hand)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Ok(SimWorld w, Command c, int player = Hand)
    {
        var r = Run(w, c, player);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    private static PlaceSegment Belt(int x, int y, int z, Dir d = Dir.PosX, int len = 4, BeltTier t = BeltTier.Mk1) =>
        new(x, y, z, d, len, t);

    // ---- hand mining -------------------------------------------------------------------------

    [Fact]
    public void HandMiningFillsTheHoldAtTwoOresPerSecond()
    {
        var w = Campaign();
        Ok(w, new StartMining(0));
        w.Step(299); // the command tick plus 299 more is 300 ticks of mining
        Assert.Equal(20, w.Network.HoldCount(Hand, Ore));
        Assert.Equal(480, w.Network.DepositRemaining(0));
        Assert.Equal(20, w.Network.MinedByHand);
        w.ValidateInvariants();
    }

    [Fact]
    public void MiningStopsWhenTheHoldIsFullAndResumesWhenThereIsRoom()
    {
        var w = Campaign();
        Ok(w, new StartMining(0));
        w.Step(PlayerRules.HoldCapacity * PlayerRules.MineTicksPerItem + 200);

        Assert.Equal(PlayerRules.HoldCapacity, w.Network.HoldTotal(Hand));
        Assert.Equal(PlayerRules.HoldCapacity, w.Network.MinedByHand);
        Assert.Equal(0, w.Network.MiningDeposit(Hand)); // still mining the same deposit: it only pauses

        // Refining swaps ore for ingots one for one, so it frees no room. Spending ingots does.
        Ok(w, new SetShipRecipe(ShipSmelt));
        w.Step(SmelterCost * 2 * 60 + 10); // enough ingots for a smelter
        Assert.Equal(PlayerRules.HoldCapacity, w.Network.MinedByHand);
        Ok(w, new PlaceMachine(0, 0, 0, C.MachineIndex("core:smelter"), SmeltIron)); // costs 8 ingots
        w.Step(300);
        Assert.True(w.Network.MinedByHand > PlayerRules.HoldCapacity);
        Assert.True(w.Network.HoldTotal(Hand) <= PlayerRules.HoldCapacity);
        w.ValidateInvariants();
    }

    [Fact]
    public void MiningEndsWhenTheDepositRunsOut()
    {
        var w = Campaign(ironAmount: 3);
        Ok(w, new StartMining(0));
        w.Step(100);

        Assert.Equal(3, w.Network.HoldCount(Hand, Ore));
        Assert.Equal(0, w.Network.DepositRemaining(0));
        Assert.Equal(-1, w.Network.MiningDeposit(Hand));
        Assert.Equal(Reject.Depleted, Run(w, new StartMining(0)).Reject);
        Assert.Equal(Reject.UnknownDeposit, Run(w, new StartMining(9)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void PlayersMineIndependentlyFromTheSameDeposit()
    {
        var w = Campaign();
        w.Submit(0, new StartMining(0));
        w.Submit(1, new StartMining(0));
        w.Step(300);

        Assert.Equal(20, w.Network.HoldCount(0, Ore));
        Assert.Equal(20, w.Network.HoldCount(1, Ore));
        Assert.Equal(460, w.Network.DepositRemaining(0));
        Ok(w, new StopMining(), player: 1);
        w.Step(150);
        Assert.Equal(30, w.Network.HoldCount(0, Ore));
        Assert.Equal(20, w.Network.HoldCount(1, Ore));
        w.ValidateInvariants();
    }

    // ---- ship refinery ------------------------------------------------------------------------

    [Fact]
    public void TheShipRefineryTurnsOreIntoIngotsSlowly()
    {
        var w = Campaign();
        w.Network.GiveItems(Hand, Ore, 10);
        Ok(w, new SetShipRecipe(ShipSmelt));

        // 2 seconds of recipe time, doubled on the ship: 120 ticks per ingot.
        w.Step(125);
        Assert.Equal(1, w.Network.HoldCount(Hand, Ingot));
        w.Step(120);
        Assert.Equal(2, w.Network.HoldCount(Hand, Ingot));
        Assert.Equal(7, w.Network.HoldCount(Hand, Ore)); // one more craft is in progress
        w.ValidateInvariants();
    }

    [Fact]
    public void ChangingTheShipRecipeMidCraftReturnsTheInputs()
    {
        var w = Campaign();
        w.Network.GiveItems(Hand, Ore, 5);
        Ok(w, new SetShipRecipe(ShipSmelt));
        w.Step(50);
        Assert.Equal(4, w.Network.HoldCount(Hand, Ore));
        Assert.True(w.Network.GetPlayerInfo(Hand).ShipCrafting);

        Ok(w, new SetShipRecipe(-1));
        Assert.Equal(5, w.Network.HoldCount(Hand, Ore));
        Assert.False(w.Network.GetPlayerInfo(Hand).ShipCrafting);
        w.Step(300);
        Assert.Equal(0, w.Network.HoldCount(Hand, Ingot));
        w.ValidateInvariants();
    }

    [Fact]
    public void MiningAndRefiningRunTogether()
    {
        var w = Campaign();
        Ok(w, new StartMining(0));
        Ok(w, new SetShipRecipe(ShipSmelt));
        for (int t = 0; t < 30 * 90; t++)
        {
            w.Step();
            if (t % 13 == 0) w.ValidateInvariants();
        }
        Assert.True(w.Network.HoldCount(Hand, Ingot) >= 20, "the refinery should keep up with a slice of the mining");
        Assert.True(w.Network.HoldTotal(Hand) <= PlayerRules.HoldCapacity);
        w.ValidateInvariants();
    }

    [Fact]
    public void OnlyShipRecipesRunOnTheShip()
    {
        var w = Campaign();
        Assert.Equal(Reject.NotAShipRecipe, Run(w, new SetShipRecipe(SmeltIron)).Reject);
        Assert.Equal(Reject.UnknownRecipe, Run(w, new SetShipRecipe(99)).Reject);
        Assert.Equal(Reject.NotPlaceable, Run(w, new PlaceMachine(0, 0, 0, Refinery, ShipSmelt)).Reject);
        Assert.Equal(Reject.NotPlaceable, Run(new SimWorld(C), new PlaceMachine(0, 0, 0, Refinery, -1)).Reject);
    }

    // ---- building costs and locks -----------------------------------------------------------

    [Fact]
    public void BuildingCostsItemsAndRemovingRefundsThem()
    {
        var w = Campaign();
        Assert.Equal(Reject.NotEnoughItems, Run(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron)).Reject);
        Assert.Equal(0, w.Network.MachineCount);

        int start = SmelterCost + 2;
        w.Network.GiveItems(Hand, Ingot, start);
        int m = Ok(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron));
        Assert.Equal(2, w.Network.HoldCount(Hand, Ingot)); // the smelter costs SmelterCost ingots

        Ok(w, new RemoveMachine(m));
        Assert.Equal(start, w.Network.HoldCount(Hand, Ingot));

        // A belt costs one ingot per cell.
        Ok(w, Belt(0, 1, 0, Dir.PosX, 4));
        Assert.Equal(start - 4, w.Network.HoldCount(Hand, Ingot));
        Assert.Equal(Reject.NotEnoughItems, Run(w, Belt(0, 2, 0, Dir.PosX, start - 4 + 1)).Reject);
        Ok(w, new RemoveSegment(0));
        Assert.Equal(start, w.Network.HoldCount(Hand, Ingot));
        w.ValidateInvariants();
    }

    [Fact]
    public void CostsAreChargedToTheCommandingPlayer()
    {
        var w = Campaign();
        w.Network.GiveItems(0, Ingot, SmelterCost + 12);
        w.Network.GiveItems(1, Ingot, SmelterCost + 12);
        Ok(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron), player: 1);
        Assert.Equal(SmelterCost + 12, w.Network.HoldCount(0, Ingot));
        Assert.Equal(12, w.Network.HoldCount(1, Ingot));

        // Whoever removes it gets the refund.
        Ok(w, new RemoveMachine(0), player: 0);
        Assert.Equal(2 * SmelterCost + 12, w.Network.HoldCount(0, Ingot));
        w.ValidateInvariants();
    }

    [Fact]
    public void AFailedBatchRefundsEverythingItPaid()
    {
        var w = Campaign();
        w.Network.GiveItems(Hand, Ingot, 40);
        ulong before = w.Checksum();
        _ = before;

        var r = Run(w, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, 0, Smelter, SmeltIron), // 8
            Belt(1, 0, 0, Dir.PosX, 4),                    // 4
            new PlaceMachine(0, 0, 0, Assembler, PlateRecipe), // overlaps the smelter
        }));

        Assert.Equal(Reject.Overlap, r.Reject);
        Assert.Equal(40, w.Network.HoldCount(Hand, Ingot));
        Assert.Equal(0, w.Network.MachineCount);
        w.ValidateInvariants();
    }

    [Fact]
    public void SandboxOnlyMachinesAndSourcesAreOffInCampaign()
    {
        var w = Campaign();
        Assert.Equal(Reject.Locked, Run(w, new PlaceMachine(0, 0, 0, Extractor, C.RecipeIndex("core:mine_iron"))).Reject);

        w.Network.GiveItems(Hand, Ingot, 10);
        int belt = Ok(w, Belt(0, 1, 0, Dir.PosX, 3));
        Assert.Equal(Reject.Locked, Run(w, new SetSource(belt, new ushort[] { 0 })).Reject);

        // The same commands work in sandbox mode.
        var s = new SimWorld(C);
        Assert.True(Run(s, new PlaceMachine(0, 0, 0, Extractor, C.RecipeIndex("core:mine_iron"))).Ok);
    }

    [Fact]
    public void ResearchGatedContentIsLockedUntilResearched()
    {
        var w = Campaign();
        w.Network.GiveItems(Hand, PlateItem, 50);
        w.Network.GiveItems(Hand, Ingot, 50);

        Assert.Equal(Reject.Locked, Run(w, Belt(0, 0, 0, Dir.PosX, 4, BeltTier.Mk2)).Reject);
        Assert.Equal(Reject.Locked, Run(w, Belt(0, 0, 0, Dir.PosX, 4, BeltTier.Mk3)).Reject);
        Assert.Equal(Reject.Locked, Run(w, new PlaceMachine(0, 0, 0, Drill, DrillCarbon)).Reject);
        int d = Ok(w, new PlaceMachine(0, 0, 0, Drill, DrillIron));
        Assert.Equal(Reject.Locked, Run(w, new SetRecipe(d, DrillCarbon)).Reject);
        Assert.True(w.Network.IsBeltLocked(BeltTier.Mk2));
        Assert.False(w.Network.IsBeltLocked(BeltTier.Mk1));
        Assert.False(w.Network.IsMachineLocked(Smelter));
    }

    // ---- research ------------------------------------------------------------------------------

    [Fact]
    public void ResearchChoiceIsValidated()
    {
        var w = Campaign();
        Assert.Equal(Reject.UnknownResearch, Run(w, new SetResearch(99)).Reject);
        Assert.Equal(Reject.UnknownResearch, Run(w, new SetResearch(-2)).Reject);
        Assert.Equal(Reject.ResearchUnavailable, Run(w, new SetResearch(AdvancedLogistics)).Reject); // needs two others first
        Assert.True(Run(w, new SetResearch(Logistics)).Ok);
        Assert.Equal(Logistics, w.Network.CurrentResearch);
        Assert.True(Run(w, new SetResearch(-1)).Ok);
        Assert.Equal(-1, w.Network.CurrentResearch);
        Assert.Equal(Reject.NoContent, Run(new SimWorld(), new SetResearch(0)).Reject);
    }

    /// <summary>Drill, belt, smelter, belt, assembler, belt, lab: the whole automated chain, built with commands.</summary>
    private static SimWorld WorldWithPlateChainIntoALab()
    {
        var w = Campaign(ironAmount: 5000);
        w.Network.GiveItems(Hand, Ingot, ChainCost + 20);
        w.Network.GiveItems(Hand, PlateItem, LabPlates + 4);
        var r = Run(w, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, 0, Drill, DrillIron),    // machine -1
            new PlaceMachine(5, 0, 0, Smelter, SmeltIron),  // machine -2
            new PlaceMachine(10, 0, 0, Assembler, PlateRecipe), // machine -3
            new PlaceMachine(15, 0, 0, Lab, -1),            // machine -4
            Belt(1, 0, 0), Belt(6, 0, 0), Belt(11, 0, 0),
            new LinkDeposit(-1, 0),
            new ConnectFromMachine(-1, -1), new ConnectToMachine(-1, -2),
            new ConnectFromMachine(-2, -2), new ConnectToMachine(-2, -3),
            new ConnectFromMachine(-3, -3), new ConnectToMachine(-3, -4),
        }));
        Assert.True(r.Ok, r.Reject.ToString());
        return w;
    }

    [Fact]
    public void AnAutomatedChainFeedsALabAndFinishesResearch()
    {
        var w = WorldWithPlateChainIntoALab();
        Assert.Equal(20 - 12, w.Network.HoldCount(Hand, Ingot)); // drill, smelter, assembler and lab, then 3 belts of 4 cells
        Assert.Equal(4, w.Network.HoldCount(Hand, PlateItem));
        Ok(w, new SetResearch(Logistics));

        for (int t = 0; t < 30 * 900 && !w.Network.IsResearched(Logistics); t++)
        {
            w.Step();
            if (t % 11 == 0) w.ValidateInvariants();
        }

        Assert.True(w.Network.IsResearched(Logistics));
        Assert.Equal(-1, w.Network.CurrentResearch);
        Assert.Equal(LogisticsPlates, w.Network.ResearchItemsUsed);
        Assert.True(w.Network.DrilledTotal >= 20, "plates come from ore: 2 ingots each");
        Assert.Equal(5000 - w.Network.DrilledTotal, w.Network.DepositRemaining(0));
        w.ValidateInvariants();

        // The unlock is real.
        Assert.False(w.Network.IsBeltLocked(BeltTier.Mk2));
        Ok(w, Belt(0, 3, 0, Dir.PosX, 4, BeltTier.Mk2)); // costs 4 plates, which we have
        Assert.Equal(0, w.Network.HoldCount(Hand, PlateItem));
    }

    [Fact]
    public void ALabOnlyTakesWhatTheResearchNeedsAndJamsOtherwise()
    {
        var w = WorldWithPlateChainIntoALab();
        // No research selected: plates reach the lab and wait on the belt.
        w.Step(30 * 150);
        Assert.Equal(0, w.Network.ResearchItemsUsed);
        Assert.True(w.Network.CountOn(2) > 0);
        Assert.False(w.Network.IsResearched(Logistics));

        // Choosing research releases them.
        Ok(w, new SetResearch(Logistics));
        w.Step(30 * 20);
        Assert.True(w.Network.ResearchItemsUsed > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void ResearchProgressIsKeptPerNodeWhenSwitching()
    {
        var w = WorldWithPlateChainIntoALab();
        Ok(w, new SetResearch(Logistics));
        for (int t = 0; t < 30 * 120 && w.Network.ResearchProgress(Logistics, PlateItem) < 2; t++) w.Step();
        int progress = w.Network.ResearchProgress(Logistics, PlateItem);
        Assert.InRange(progress, 2, 9);

        Ok(w, new SetResearch(CarbonDrilling));
        w.Step(30 * 5);
        Ok(w, new SetResearch(Logistics));
        Assert.True(w.Network.ResearchProgress(Logistics, PlateItem) >= progress);
        w.ValidateInvariants();
    }

    [Fact]
    public void FinishedResearchCannotBeChosenAgainAndRequirementsUnlockNodes()
    {
        var w = WorldWithPlateChainIntoALab();
        Ok(w, new SetResearch(Logistics));
        for (int t = 0; t < 30 * 900 && !w.Network.IsResearched(Logistics); t++) w.Step();
        Assert.True(w.Network.IsResearched(Logistics));

        Assert.Equal(Reject.ResearchUnavailable, Run(w, new SetResearch(Logistics)).Reject);
        Assert.Equal(Reject.ResearchUnavailable, Run(w, new SetResearch(AdvancedLogistics)).Reject); // carbon drilling still missing
        Assert.True(w.Network.CanResearch(CarbonDrilling));
    }

    // ---- drills -----------------------------------------------------------------------------------

    [Fact]
    public void DrillsNeedALinkedDepositOfTheRightItem()
    {
        var w = new SimWorld(C); // sandbox: no costs or locks
        w.Network.AddDeposit(Ore, 10);
        w.Network.AddDeposit(CarbonItem, 10);
        int d = Ok(w, new PlaceMachine(0, 0, 0, Drill, DrillIron));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 4));
        Ok(w, new ConnectFromMachine(d, belt));
        Ok(w, new SetSink(belt));

        w.Step(600); // not linked: nothing happens
        Assert.Equal(0, w.Network.MachineOutputsCreated);

        Ok(w, new LinkDeposit(d, 1)); // a carbon deposit under an iron recipe still does nothing
        w.Step(600);
        Assert.Equal(0, w.Network.MachineOutputsCreated);

        Ok(w, new LinkDeposit(d, 0));
        w.Step(1500);
        Assert.Equal(10, w.Network.DrilledTotal); // the deposit runs dry
        Assert.Equal(0, w.Network.DepositRemaining(0));
        Assert.Equal(10, w.Network.Consumed);

        Ok(w, new UnlinkDeposit(d));
        Assert.Equal(-1, w.Network.GetMachineInfo(d).Deposit);
        w.ValidateInvariants();
    }

    [Fact]
    public void DepositLinksAreValidated()
    {
        var w = new SimWorld(C);
        w.Network.AddDeposit(Ore, 10);
        int s = Ok(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron));
        int d = Ok(w, new PlaceMachine(2, 0, 0, Drill, DrillIron));

        Assert.Equal(Reject.WrongRole, Run(w, new LinkDeposit(s, 0)).Reject);
        Assert.Equal(Reject.WrongRole, Run(w, new UnlinkDeposit(s)).Reject);
        Assert.Equal(Reject.UnknownMachine, Run(w, new LinkDeposit(9, 0)).Reject);
        Assert.Equal(Reject.UnknownDeposit, Run(w, new LinkDeposit(d, 5)).Reject);
        Assert.Equal(Reject.UnknownDeposit, Run(w, new LinkDeposit(d, -1)).Reject);
        Assert.True(Run(w, new LinkDeposit(d, 0)).Ok);
        w.ValidateInvariants();
    }

    // ---- depots: automation feeds the player --------------------------------------------------

    private static SimWorld WorldWithDrillAndDepot(int ironAmount = 500)
    {
        var w = Campaign(ironAmount);
        w.Network.GiveItems(Hand, Ingot, 40); // a drill, 3 belt cells and a depot
        var r = Run(w, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, 0, Drill, DrillIron),
            Belt(1, 0, 0, Dir.PosX, 3),
            new PlaceMachine(4, 0, 0, C.MachineIndex("core:depot"), -1),
            new LinkDeposit(-1, 0),
            new ConnectFromMachine(-1, -1), new ConnectToMachine(-1, -2),
        }));
        Assert.True(r.Ok, r.Reject.ToString());
        return w;
    }

    [Fact]
    public void ADepotDeliversIntoTheHoldOfWhoeverBuiltIt()
    {
        var w = WorldWithDrillAndDepot();
        int left = 40 - Cost("core:drill", "core:iron_ingot") - 3 - DepotCost;
        Assert.Equal(left, w.Network.HoldCount(Hand, Ingot));

        w.Step(30 * 30); // one ore every 2 seconds, plus belt travel
        int ore = w.Network.HoldCount(Hand, Ore);
        Assert.InRange(ore, 10, 14);
        Assert.Equal(ore, w.Network.DepotDelivered);
        Assert.Equal(0, w.Network.HoldCount(1, Ore));
        var drill = w.Network.GetMachineInfo(0);
        int inProgress = drill.Crafting ? 1 : 0; // ore leaves the deposit when a craft starts
        Assert.Equal(500 - ore - w.Network.CountOn(0) - drill.OutputItems - inProgress, w.Network.DepositRemaining(0));
        w.ValidateInvariants();

        // Delivered ore can be refined on the ship right away: the loop closes.
        Ok(w, new SetShipRecipe(ShipSmelt));
        w.Step(600);
        Assert.True(w.Network.HoldCount(Hand, Ingot) > left);
    }

    [Fact]
    public void ADepotStopsAtThePerItemCapAndTheBeltBacksUpInsteadOfLosingItems()
    {
        var w = WorldWithDrillAndDepot();
        w.Step(30 * 200); // far more ore than the cap
        Assert.Equal(PlayerRules.DepotItemCap, w.Network.HoldCount(Hand, Ore));
        Assert.True(w.Network.HoldTotal(Hand) < PlayerRules.HoldCapacity, "one item type never takes the whole hold");
        long delivered = w.Network.DepotDelivered;

        w.Step(600);
        Assert.Equal(delivered, w.Network.DepotDelivered);
        Assert.True(w.Network.CountOn(0) > 0, "the belt holds the ore back");
        w.ValidateInvariants();

        // Using the ore frees the cap and the flow resumes: the ship refinery eats it.
        Ok(w, new SetShipRecipe(ShipSmelt));
        w.Step(600);
        Assert.True(w.Network.DepotDelivered > delivered);
        w.ValidateInvariants();
    }

    [Fact]
    public void AFullHoldStillStopsDepotsWhateverTheItem()
    {
        var w = WorldWithDrillAndDepot();
        w.Network.GiveItems(Hand, PlateItem, PlayerRules.HoldCapacity); // fill the hold with something else
        long delivered = w.Network.DepotDelivered;
        w.Step(600);
        Assert.Equal(delivered, w.Network.DepotDelivered);
        w.ValidateInvariants();
    }

    [Fact]
    public void ADepotOnlyAcceptsInstructionsFromItsOwnerHold()
    {
        var w = WorldWithDrillAndDepot();
        // Another player removing the depot gets the refund; deliveries stop.
        w.Network.GiveItems(1, Ingot, 1);
        Ok(w, new RemoveMachine(1), player: 1);
        Assert.Equal(1 + DepotCost, w.Network.HoldCount(1, Ingot));
        long delivered = w.Network.DepotDelivered;
        w.Step(300);
        Assert.Equal(delivered, w.Network.DepotDelivered);
        Assert.False(w.Network.FeedsAnyMachine(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void EntityAtFindsBeltsAndMachinesByCell()
    {
        var w = WorldWithDrillAndDepot();
        Assert.Equal((SimWorld.EntityKind.Machine, 0), w.EntityAt(0, 0, 0));
        Assert.Equal((SimWorld.EntityKind.Segment, 0), w.EntityAt(2, 0, 0));
        Assert.Equal((SimWorld.EntityKind.Machine, 1), w.EntityAt(4, 0, 0));
        Assert.Equal((SimWorld.EntityKind.None, -1), w.EntityAt(9, 9, 9));
    }

    // ---- codec and logs --------------------------------------------------------------------------

    [Fact]
    public void NewCommandsSurviveARoundTripAndLogsCarryTheMode()
    {
        Command[] commands =
        {
            new StartMining(3), new StopMining(), new SetShipRecipe(6), new SetResearch(-1),
            new LinkDeposit(2, 4), new UnlinkDeposit(2),
        };
        foreach (var c in commands)
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            Assert.Equal(c.GetType(), CommandCodec.FromBytes(bytes).GetType());
            Assert.Equal(bytes, CommandCodec.ToBytes(CommandCodec.FromBytes(bytes)));
        }
        Assert.Equal(new LinkDeposit(2, 4), CommandCodec.FromBytes(CommandCodec.ToBytes(new LinkDeposit(2, 4))));

        var log = commands.Select((c, i) => new LoggedCommand(i, 0, c)).ToList();
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Campaign);
        ms.Position = 0;
        CommandCodec.ReadLog(ms, out ulong hash, out var mode);
        Assert.Equal(C.Hash, hash);
        Assert.Equal(GameMode.Campaign, mode);

        // An unknown mode byte is an error.
        byte[] raw = ms.ToArray();
        raw[13] = 9; // magic (4) + version (1) + hash (8) puts the mode at offset 13
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(raw), out _, out _));
    }

    [Fact]
    public void ModeChangesTheStateAndNegativePlayersAreRefused()
    {
        Assert.NotEqual(new SimWorld(C).Checksum(), new SimWorld(C, GameMode.Campaign).Checksum());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimWorld(C).Submit(-1, new StopMining()));
    }

    // ---- fuzz --------------------------------------------------------------------------------------

    private static SimWorld StormWorld()
    {
        var w = new SimWorld(C, GameMode.Campaign);
        w.Network.AddDeposit(Ore, 400);
        w.Network.AddDeposit(CarbonItem, 120);
        w.Network.AddDeposit(Ore, 25);
        for (int p = 0; p < 3; p++)
        {
            w.Network.GiveItems(p, Ingot, 220);
            w.Network.GiveItems(p, PlateItem, 60);
            w.Network.GiveItems(p, CarbonItem, 20);
        }
        return w;
    }

    private static Command RandomEconomyCommand(Random rng, SimWorld w)
    {
        var net = w.Network;
        switch (rng.Next(0, 9))
        {
            case 8: return new SetMovement(rng.Next(0, 2) == 0 ? 2 : 0, rng.Next(0, 2) == 0 ? -3 : 0, 0); // invalid or a no-op
            case 0: return new StartMining(rng.Next(-1, net.DepositCount + 2));
            case 1: return new StopMining();
            case 2: return new SetShipRecipe(rng.Next(0, 5) == 0 ? rng.Next(-2, C.Recipes.Count + 2) : rng.Next(0, 2) == 0 ? ShipSmelt : -1);
            case 3: return new SetResearch(rng.Next(-2, C.Research.Count + 2));
            case 4:
            {
                // A drill on a free cell, linked to a deposit, in one batch.
                int x = rng.Next(0, 6), z = rng.Next(0, 6);
                return new Batch(new Command[]
                {
                    new PlaceMachine(x, 0, z, Drill, rng.Next(0, 3) == 0 ? DrillCarbon : DrillIron),
                    new LinkDeposit(-1, rng.Next(0, net.DepositCount)),
                });
            }
            case 5: return new LinkDeposit(rng.Next(-1, net.MachineIdLimit + 1), rng.Next(-1, net.DepositCount + 1));
            case 6: return new UnlinkDeposit(rng.Next(-1, net.MachineIdLimit + 1));
            default:
            {
                // A lab on a free cell next to the end of a belt, fed by it.
                var ids = Enumerable.Range(0, net.SegmentIdLimit).Where(id => net.IsAlive(id) && net.OutputCount(id) == 0
                    && !net.IsSink(id) && !net.FeedsAnyMachine(id)).ToList();
                if (ids.Count == 0) return new SetResearch(rng.Next(-1, 3));
                int seg = ids[rng.Next(0, ids.Count)];
                var f = net.GetSegmentInfo(seg);
                var (dx, dy, dz) = Dirs.Vector((Dir)rng.Next(0, 6));
                return new Batch(new Command[]
                {
                    new PlaceMachine(f.OriginX + f.DirX * (f.LengthCells - 1) + dx, f.OriginY + f.DirY * (f.LengthCells - 1) + dy,
                        f.OriginZ + f.DirZ * (f.LengthCells - 1) + dz, Lab, -1),
                    new ConnectToMachine(seg, -1),
                });
            }
        }
    }

    /// <summary>
    /// Mostly keeps the ship near the work (the build and mining ranges are what we want to exercise, not a ship lost in space):
    /// head home when more than 4 cells out, otherwise stop or drift off now and then.
    /// </summary>
    private static Command SteerShip(Random rng, SimWorld w, int player)
    {
        if (!w.Network.HasPlayer(player)) return new SetMovement(rng.Next(0, 2), 0, 0);
        var p = w.Network.GetPlayerInfo(player);
        static int Home(int units) => Math.Abs(units) > 4 * Units.CellUnits ? -Math.Sign(units) : 0;
        int hx = Home(p.X), hy = Home(p.Y), hz = Home(p.Z);
        if (hx != 0 || hy != 0 || hz != 0) return new SetMovement(hx, hy, hz);
        return rng.Next(0, 4) == 0
            ? new SetMovement(rng.Next(-1, 2), rng.Next(-1, 2), rng.Next(-1, 2))
            : new SetMovement(0, 0, 0);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void RandomCampaignStormKeepsInvariantsAndReplaysIdentically(int seed)
    {
        const int ticks = 3000;
        var rng = new Random(seed);
        var w = StormWorld();
        int ok = 0, locked = 0, broke = 0, rolledBack = 0, minedStarts = 0, shipSets = 0, tooFar = 0, moves = 0;

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.Next(0, 4);
            for (int i = 0; i < n; i++)
            {
                int who = rng.Next(0, 3);
                int pick = rng.Next(0, 12);
                var cmd = pick < 2 ? CommandTests.RandomCommand(rng, w)
                    : pick < 5 ? MachineTests_RandomMachine(rng, w)
                    : pick < 7 ? SteerShip(rng, w, who)
                    : RandomEconomyCommand(rng, w);
                w.Submit(who, cmd);
            }
            w.Step();

            foreach (var r in w.LastResults)
            {
                if (r.Ok) ok++;
                if (r.Reject == Reject.Locked) locked++;
                if (r.Reject == Reject.NotEnoughItems) broke++;
                if (r.Reject == Reject.OutOfRange) tooFar++;
                if (r.Ok && r.Command is SetMovement { Dx: not 0 } or SetMovement { Dy: not 0 } or SetMovement { Dz: not 0 }) moves++;
                if (r.Command is Batch && !r.Ok && r.FailedIndex >= 0) rolledBack++;
                if (r.Ok && r.Command is StartMining) minedStarts++;
                if (r.Ok && r.Command is SetShipRecipe) shipSets++;
            }
            w.ValidateInvariants();
        }

        Assert.True(ok > 150, $"only {ok} commands succeeded");
        Assert.True(locked > 0, "nothing was ever locked");
        Assert.True(rolledBack > 0, "no batch was rolled back");
        Assert.True(minedStarts > 0 && shipSets > 0, "the player economy was not exercised");
        Assert.True(w.Network.MinedByHand > 0, "nothing was mined by hand");
        Assert.True(w.Network.HoldIn > 0 && w.Network.HoldOut > 0, "hold accounting was not exercised");
        Assert.True(moves > 0, "the ships never moved");
        Assert.True(tooFar > 0, "nothing was ever out of range");

        // Replay from the same starting world, directly and through the binary format.
        Assert.Equal(w.Checksum(), SimWorld.Replay(w.Log, ticks, initial: StormWorld()).Checksum());

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms, out ulong hash, out var mode);
        Assert.Equal(C.Hash, hash);
        Assert.Equal(GameMode.Campaign, mode);
        var replay = SimWorld.Replay(restored, ticks, initial: StormWorld());
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }

    // The machine generator lives in MachineTests; reuse it through the same entry point.
    private static Command MachineTests_RandomMachine(Random rng, SimWorld w) => MachineTests.RandomMachineCommand(rng, w);
}
