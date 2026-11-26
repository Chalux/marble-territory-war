using Godot;
using System;
using System.Linq;
namespace TerritoryWar;

public static class SimulationTests
{
    private static int _checks;
    private static void Check(bool ok,string description){if(!ok)throw new Exception(description);_checks++;}
    public static void Run(SceneTree tree, PinballBoard board)
    {
        try
        {
            TerritoryContourTests.Run(Check);
            PaintAgeTests.Run(Check);
            ProjectileTrailTests.Run(Check);
            WeaponTuningTests.Run(Check);
            PinballBoardTests.Run(Check, board);
            PinballCollisionTests.Run(Check);
            var growth=new PinballSimulation(new SimulationConfig());
            foreach(var ball in growth.Balls)ball.Phase=BallPhase.Inactive;
            for(int i=0;i<120*30;i++)growth.Tick();
            Check(Math.Abs(growth.BaseBallValue-1)<1e-9,"base ball value holds at one until the first jump");
            for(int i=0;i<120*30;i++)growth.Tick();
            Check(Math.Abs(growth.BaseBallValue-2)<1e-9,"base ball value jumps to two after one minute");
            growth.AwardBall(0);
            var grown=growth.Balls[0];
            Check(Math.Abs(grown.Value-2)<1e-9,"reward balls start at the current base value");
            grown.Phase=BallPhase.Flying;grown.Value=123;growth.Redeem(grown,Ability.Storm);
            Check(Math.Abs(grown.Value-2)<1e-9&&growth.Events.Last().Value==123,"redemption preserves the action value and resets to the current base");
            foreach(var ball in growth.Balls)ball.Phase=BallPhase.Inactive;
            for(int i=0;i<120*60;i++)growth.Tick();
            Check(Math.Abs(growth.BaseBallValue-4)<1e-9,"base ball value doubles again after two minutes");
            growth.AwardBall(0);grown.Phase=BallPhase.Flying;growth.Multiply(grown,0);
            Check(Math.Abs(grown.Value-32)<1e-8,"multiplier still multiplies accumulated value without resetting it");
            growth.Reset();
            Check(growth.BaseBallValue==1&&growth.Balls.Take(PinballSimulation.StartingBallsPerFaction*4).All(ball=>ball.Value==1),"restart resets time and starting ball values");
            var config=new SimulationConfig();var sim=new PinballSimulation(config);
            Check(sim.Balls.Count(b=>b.Phase!=BallPhase.Inactive)==PinballSimulation.StartingBallsPerFaction*4,"twelve starting balls");
            for(int f=0;f<4;f++)Check(sim.Balls.Count(b=>b.Phase!=BallPhase.Inactive&&b.Faction==f&&b.Value==1)==PinballSimulation.StartingBallsPerFaction,"three value-one balls per faction");
            var b=sim.Balls[0];b.Phase=BallPhase.Flying;sim.Multiply(b,0);
            Check(b.Id==0&&b.Value==8&&b.Phase==BallPhase.Waiting,"multiplier preserves identity and value");
            sim.Multiply(b,0);Check(b.Value==8,"no repeated multiplier while returning");
            b.Phase=BallPhase.Flying;sim.Redeem(b,Ability.Sniper);
            Check(b.Value==1&&sim.Redeemed[0,4]==8&&sim.Events.Last().Value==8,"redemption snapshots pre-reset value");
            sim.Redeem(b,Ability.Sniper);Check(sim.Redeemed[0,4]==8,"no duplicate redemption");
            Check(sim.AbilityAt(30)==Ability.Storm&&sim.AbilityAt(154)==Ability.Gun&&sim.AbilityAt(650)==Ability.Storm,"half-open wrapping ability regions");
            Check(PinballSimulation.BeltAbilities[^1]==Ability.Shield&&PinballSimulation.BeltAbilities[^2]==Ability.Sniper,
                "shield action is copied between sniper and storm");
            Check(sim.AbilityAt(PinballSimulation.AbilityLeft+sim.AbilityRegionWidth*4.1f)==Ability.Shotgun&&
                sim.AbilityAt(PinballSimulation.AbilityLeft+sim.AbilityRegionWidth*5.1f)==Ability.Sniper&&
                sim.AbilityAt(PinballSimulation.AbilityLeft+sim.AbilityRegionWidth*6.1f)==Ability.Shield,
                "belt order includes shotgun before sniper and the shield copy last");
            b.Value=1;sim.UpdateSize(b);float r=b.Radius,m=b.Mass;
            b.Value=256;sim.UpdateSize(b);Check(b.Radius>r&&b.Mass>m,"value increases size and mass");
            // Full pool must retain outstanding rewards.
            for(int i=0;i<130;i++)sim.AwardBall(0);
            Check(sim.Balls.Count(x=>x.Phase!=BallPhase.Inactive)==128&&sim.PendingRewards[0]==14,"pool overflow queues rewards");
            sim.Balls[30].Phase=BallPhase.Inactive;sim.Tick();Check(sim.PendingRewards[0]==13,"free slot drains pending reward");
            sim.Reset(); var twin=new PinballSimulation(config);
            for(int i=0;i<120*600;i++){sim.Tick();twin.Tick();}
            Check(sim.AbilityHits>0&&sim.MultiplierHits>0,"ten-minute simulation exercises both loops");
            Check(sim.Balls.Count(x=>x.Phase!=BallPhase.Inactive)==12,"recycling preserves twelve starting balls");
            Check(sim.Balls.All(x=>float.IsFinite(x.Position.X)&&float.IsFinite(x.Position.Y)&&double.IsFinite(x.Value)),"all states remain finite");
            Check(sim.Balls.All(x=>x.Phase!=BallPhase.Flying||(x.Position.Y>=0&&x.Position.Y<977)),"balls stay inside board");
            Check(sim.Balls.Zip(twin.Balls).All(pair=>pair.First.Position==pair.Second.Position&&pair.First.Value==pair.Second.Value),"seeded simulation deterministic");
            b=sim.Balls[0];b.Value=1;sim.UpdateSize(b);Check(b.Radius<r&&b.Mass<m,"whole-match time decreases size and mass");
            Check(Enum.GetValues<Ability>().All(a=>Enumerable.Range(0,4).Sum(f=>sim.Redeemed[f,(int)a])>0),"all six ability types exercised");
            Check(Battlefield.EaseShieldRadius(100,40,0,0.2f)==40,"zero frame delta snaps shield radius");
            float easedShield=Battlefield.EaseShieldRadius(100,40,1/60f,0.2f);
            Check(easedShield<100&&easedShield>40,"shield radius easing moves part way");
            float settledShield=100;
            for(int i=0;i<120;i++)settledShield=Battlefield.EaseShieldRadius(settledShield,40,1/60f,0.2f);
            Check(Math.Abs(settledShield-40)<1e-6,"shield radius easing settles at its target");
            BattleTests.Run(Check);
            AdvancedTests.Run(Check);
            GD.Print($"SELF-TEST PASS: {_checks} checks; 600 simulated seconds; {sim.MultiplierHits} multipliers; {sim.AbilityHits} redemptions.");
            tree.Quit();
        }
        catch(Exception error){GD.PushError("SELF-TEST FAIL: "+error);tree.Quit(1);}
    }
}
