using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerritoryWar;

public static class WeaponTuningTests
{
    public static void Run(Action<bool,string> check)
    {
        var config=new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0,BaseRotationSpeed=0};
        var battle=new BattleSimulation(config,_=>{});
        float[] Fire()
        {
            battle.Reset();battle.Factions[0].GunReserve=10000;
            var angles=new List<float>();long last=0;
            for(int i=0;i<240;i++)
            {
                battle.Tick();
                foreach(var p in battle.Projectiles)
                    if(p.Active&&p.Id>last){angles.Add(Mathf.RadToDeg(p.Velocity.Angle()));last=p.Id;}
            }
            return angles.ToArray();
        }
        var first=Fire();
        check(first.Length>50&&first.All(a=>Math.Abs(a)<=5.001f),"machine gun spread stays within +/- five degrees");
        check(first.Min()<-3&&first.Max()>3&&first.Distinct().Count()>40,"machine gun independently scatters to both sides");
        check(first.SequenceEqual(Fire()),"reset restores deterministic gun scatter");
        config.Seed++;
        check(!first.SequenceEqual(Fire()),"different seeds produce different gun scatter");
        config.GunSpreadDegrees=0;
        check(Fire().All(a=>Math.Abs(a)<0.0001f),"zero spread setting restores exact aim");

        var pinball=new PinballSimulation(new SimulationConfig());var ball=pinball.Balls[0];
        float previous=0;
        foreach(double value in new double[]{1,8,64,256,4096,65536,1048576})
        {
            ball.Value=value;pinball.UpdateSize(ball);
            float old=(float)(7*(1+0.13*Math.Log2(value)));
            check(ball.Radius>previous,"radius increases monotonically at value "+value);previous=ball.Radius;
            if(value>1&&value<256)check(ball.Radius<old,"low value radius growth is reduced at "+value);
            if(value>256)check(ball.Radius>old,"high value radius growth is increased at "+value);
            if(value==256)check(Math.Abs(ball.Radius-old)<0.001f,"radius curve preserves the 256-value pivot");
        }
        foreach(var b in pinball.Balls)b.Phase=BallPhase.Inactive;
        for(int i=0;i<120*240;i++)pinball.Tick();
        check(Math.Abs(pinball.RadiusPivotLog-10)<0.00001,"radius pivot rises from 256 to 1024 at four minutes");
        ball.Value=1024;pinball.UpdateSize(ball);
        double decay=Math.Sqrt(1+pinball.Time/pinball.Config.TimeScale);
        check(Math.Abs(ball.Radius-(7*(1+0.13*10)/decay))<0.001,"moving radius pivot remains the crossover with the original curve");
        var scaledRadius=new PinballSimulation(new SimulationConfig{RadiusTimeMultiplier=2});
        foreach(var b in scaledRadius.Balls)b.Phase=BallPhase.Inactive;
        for(int i=0;i<120*240;i++)scaledRadius.Tick();
        var scaledBall=scaledRadius.Balls[0];scaledBall.Value=1024;scaledRadius.UpdateSize(scaledBall);
        check(Math.Abs(scaledRadius.RadiusPivotLog-12)<0.00001&&scaledBall.Radius<ball.Radius,
            "radius time multiplier accelerates pivot growth and time shrink");
        var normalMass=pinball.Balls[1];normalMass.Value=256;pinball.UpdateSize(normalMass);
        var scaledMass=scaledRadius.Balls[1];scaledMass.Value=256;scaledRadius.UpdateSize(scaledMass);
        check(Math.Abs(normalMass.Mass-scaledMass.Mass)<1e-6,"radius time multiplier leaves mass decay unchanged");
        var frozen=new PinballSimulation(new SimulationConfig{RadiusTimeMultiplier=0});
        check(Math.Abs(frozen.RadiusPivotLog-8)<1e-9,"zero radius time multiplier disables time-driven pivot growth");
        ball.Value=512;pinball.UpdateSize(ball);
        check(ball.Radius<7*(1+0.13*9)/decay,"formerly high value enters the gentler growth region over time");
        pinball.Reset();check(pinball.RadiusPivotLog==8,"new match restores initial radius pivot");
    }
}
