using Godot;
using System;
using System.Linq;

namespace TerritoryWar;

public static class PinballBoardTests
{
    public static void RunStandalone(SceneTree tree, PinballBoard board)
    {
        int checks = 0;
        try
        {
            Run((ok, message) => { if (!ok) throw new Exception(message); checks++; }, board);
            GD.Print($"BOARD-TEST PASS: {checks} checks; scene geometry and dynamic ball lifecycle.");
            tree.Quit();
        }
        catch (Exception error) { GD.PushError("BOARD-TEST FAIL: " + error); tree.Quit(1); }
    }

    public static void Run(Action<bool, string> check, PinballBoard board)
    {
        var balls = board.GetNode<Node2D>("Balls");
        check(balls.GetChildCount() == 0, "board scene has no preallocated ball nodes");
        var pegs = board.GetNode<Node2D>("Surface/Pegs").GetChildren().Cast<Node2D>().ToArray();
        check(pegs.Length == PinballSimulation.Pegs.Length, "scene peg count matches simulation");
        for (int i = 0; i < pegs.Length; i++)
            check(pegs[i].Position.IsEqualApprox(PinballSimulation.Pegs[i]), $"peg {i} matches collision center");
        var rails = board.GetNode<Node2D>("Surface/Rails").GetChildren().Cast<Node2D>().ToArray();
        check(rails.Length == PinballGeometry.Walls.Length, "scene rail count matches collision geometry");
        for (int i = 0; i < rails.Length; i++)
        {
            var points = rails[i].GetNode<Line2D>("Edge").Points;
            check(points[0].IsEqualApprox(PinballGeometry.Walls[i].Start) &&
                points[1].IsEqualApprox(PinballGeometry.Walls[i].End), $"rail {i} matches swept collision segment");
        }
        for (int i = 0; i < 6; i++)
        {
            var pocket = board.GetNode<Node2D>($"Pockets/Pocket{i}");
            check(pocket.Position.IsEqualApprox(PinballGeometry.Slots[i].Center), $"pocket {i} matches trigger");
            check(pocket.GetNode<Label>("Factor").Text == $"×{PinballGeometry.Slots[i].Factor}", $"pocket {i} shows correct multiplier");
        }
        var sim = new PinballSimulation(new SimulationConfig());
        board.Simulation = sim;
        board.Refresh();
        check(balls.GetChildCount() == 0, "waiting balls have no display nodes");
        sim.Tick();
        board.ObserveStep();
        board.Refresh();
        check(balls.GetChildCount() == sim.Balls.Count(b => b.Phase == BallPhase.Flying), "launch instantiates only flying balls");
        var first = balls.GetNode<BallView>("Ball000");
        check(first.Visible && first.Position.IsEqualApprox(sim.Balls[0].Position), "new ball is initialized and displayed");
        board.Refresh();
        check(balls.GetNode<BallView>("Ball000") == first, "refresh reuses the flying ball node");
        sim.Multiply(sim.Balls[0], 0);
        board.ObserveStep();
        board.Refresh();
        check(balls.GetChildCount() == 0 && first.IsQueuedForDeletion(), "return removes and frees ball immediately");
        sim.Balls[0].Phase = BallPhase.Flying;
        board.ObserveStep();
        board.Refresh();
        check(balls.GetNode<BallView>("Ball000") != first, "relaunch creates a fresh node and trail");
        sim.SacrificeLargest(0);
        board.ObserveStep();
        check(balls.GetChildCount() == 0, "inactive ball is released");
        foreach (var state in sim.Balls) { state.Phase = BallPhase.Flying; state.Radius = 7; }
        board.Refresh();
        check(balls.GetChildCount() == PinballSimulation.Capacity, "all simulation slots can be loaded on demand");
        sim.Reset();
        board.Refresh();
        check(balls.GetChildCount() == 0, "same-seed reset clears every ball node");
        sim.Tick();
        board.ObserveStep();
        board.Refresh();
        check(balls.GetChildCount() == 1, "launch after reset creates exactly one ball");
        board.Simulation = new PinballSimulation(new SimulationConfig { Seed = 777 });
        board.Refresh();
        check(balls.GetChildCount() == 0, "replacing the simulation releases old nodes");
        var baffleSim=board.Simulation;
        for(int k=0;k<120;k++)baffleSim.Tick();
        board.Refresh();
        for(int i=0;i<6;i++)
        {
            var line=baffleSim.BaffleLine(i);
            var edge=board.GetNode<Line2D>($"Baffles/Baffle{i}/Edge");
            var body=board.GetNode<Line2D>($"Baffles/Baffle{i}/Body");
            var shadow=board.GetNode<Line2D>($"Baffles/Baffle{i}/Shadow");
            check(edge.ToGlobal(edge.GetPointPosition(0)).IsEqualApprox(line.A)&&
                edge.ToGlobal(edge.GetPointPosition(1)).IsEqualApprox(line.B),
                $"baffle {i} visible top edge is the collision line");
            check(body.GetPointPosition(0).Y>line.A.Y&&body.GetPointPosition(1).Y>line.B.Y&&
                shadow.GetPointPosition(0).Y>body.GetPointPosition(0).Y&&shadow.GetPointPosition(1).Y>body.GetPointPosition(1).Y,
                $"baffle {i} body and shadow hang below the collision surface");
        }
        board.Simulation = null;
    }
}
