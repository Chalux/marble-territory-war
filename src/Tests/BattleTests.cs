using System;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace TerritoryWar;

public static class BattleTests
{
    public static void Run(Action<bool,string> check)
    {
        var opening=new MatchSimulation(new SimulationConfig());
        check(opening.Battle.Factions.All(f=>f.Shield==20_000_000),"all four factions start with 20M shield");
        opening.Battle.Factions[0].Shield=1;opening.Reset();
        check(opening.Battle.Factions.All(f=>f.Shield==20_000_000),"reset restores starting shields");
        check(opening.Battle.Invested.Cast<double>().All(v=>v==0),"opening shields do not count as redeemed firepower");
        var config=new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0};
        var grid=new TerritoryGrid();
        var center=grid.CellCenter(1,1);
        double budget=50;
        grid.PaintDisk(center,1,0,2,ref budget);
        check(budget==0&&grid.Counts[0]==1,"one logical cell charges its fixed area");
        budget=50;grid.PaintDisk(center,1,0,2,ref budget);
        check(budget==50,"friendly territory is free");
        grid.PaintDisk(center,1,1,2,ref budget);
        check(grid.Counts[0]==0&&grid.Counts[1]==1&&budget==0,"ownership transfer updates both area counts");
        budget=49;grid.PaintDisk(center,1,0,2,ref budget);
        check(grid.Counts[0]==0&&budget==49,"unaffordable cells stay unchanged");
        budget=100;grid.PaintSegment(grid.CellCenter(10,10),grid.CellCenter(30,10),4,2,0.0032,ref budget);
        check(grid.Counts[2]>=21&&budget<100,"swept paint has no gaps at high speed");
        // A near-tangent cell lies between the old 2.5-unit disk samples.
        var sweep=new TerritoryGrid();double sweepBudget=100000;
        Vector2 target=sweep.CellCenter(20,20);
        Vector2 begin=target+new Vector2(-11.25f,-3.99f),end=begin+new Vector2(100,0);
        sweep.PaintSegment(begin,end,4,0,1,ref sweepBudget);
        check(sweep.Owners[20*TerritoryGrid.Width+20]==1,"continuous paint includes cells between disk samples");
        var split=new TerritoryGrid();double splitBudget=100000;
        for(int i=0;i<40;i++)split.PaintSegment(begin.Lerp(end,i/40f),begin.Lerp(end,(i+1)/40f),4,0,1,ref splitBudget);
        check(sweep.Owners.SequenceEqual(split.Owners)&&sweepBudget==splitBudget,"swept coverage and cost are independent of segment subdivision");
        var limited=new TerritoryGrid();double limitedBudget=25;
        limited.PaintSegment(end,begin,4,0,1,ref limitedBudget,out bool exhausted);
        check(exhausted&&limited.Counts[0]==1&&limitedBudget==0,"continuous sweep still respects one-cell budget");
        var tiny=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0},_=>{});
        tiny.Spawn(0,new Vector2(1202.5f,502.5f),new Vector2(300,0),0.001);tiny.Tick();
        check(tiny.ActiveProjectiles==0&&tiny.Territory.Counts[0]==0&&Math.Abs(tiny.Factions[0].PaintRemainder-0.001)<1e-12,"sub-cell projectile budget retires without free paint or untracked loss");
        var splat=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,ExperiencePerAreaSecond=0},_=>{});
        check(Math.Abs(splat.PaintCost*TerritoryGrid.CellArea-0.5)<1e-9,"all paint starts at 0.5 points per cell");
        splat.Spawn(0,new Vector2(1200,500),new Vector2(300,0),80);splat.Tick();
        check(splat.Projectiles[0].Active&&splat.Territory.Counts[0]>0&&splat.Projectiles[0].Value<80,"normal projectile paints and continues with its remaining value");
        check(Math.Abs(80-splat.Projectiles[0].Value-splat.Factions[0].PaintSpent)<1e-9,"continuous projectile painting conserves value");
        for(int i=0;i<120*10&&splat.Projectiles[0].Active;i++)splat.Tick();
        check(!splat.Projectiles[0].Active&&splat.Territory.Counts[0]<=Math.Ceiling(80/(splat.PaintCost*TerritoryGrid.CellArea))&&Math.Abs(splat.Factions[0].PaintSpent+splat.Factions[0].PaintRemainder-80)<1e-8,
            "projectile keeps painting forward until its paint budget is exhausted");
        splat.Reset();splat.Territory.SeedDisk(new Vector2(1200,500),120,0);
        splat.Spawn(0,new Vector2(1200,500),new Vector2(300,0),80);splat.Tick();
        check(splat.Projectiles[0].Active&&splat.Projectiles[0].Value==80,"normal projectile flies on over friendly territory");
        splat.Reset();
        splat.SpawnWeapon(0,new Vector2(1200,500),Vector2.Right*300,80,WeaponKind.Fragment,Ability.Sniper);splat.Tick();
        check(splat.Projectiles[0].Active&&splat.Projectiles[0].Value<80&&splat.Territory.Counts[0]>0,
            "sniper fragments also paint continuously and retain unspent value");
        var clockCost=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0},_=>{});
        for(int i=0;i<120*60;i++)clockCost.Tick();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-0.5)<1e-9,"paint cost holds at 0.5 through the first minute");
        for(int i=0;i<120*180;i++)clockCost.Tick();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-0.5)<1e-9,"paint cost holds until the first jump");
        for(int i=0;i<120*60;i++)clockCost.Tick();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-1)<1e-9,"paint cost jumps to 1 after the first extended interval");
        for(int i=0;i<120*240;i++)clockCost.Tick();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-2)<1e-9,"paint cost jumps to 2 after the second extended interval");
        for(int i=0;i<120*240;i++)clockCost.Tick();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-4)<1e-9,"paint cost doubles at each extended step");
        clockCost.Reset();
        check(Math.Abs(clockCost.PaintCost*TerritoryGrid.CellArea-0.5)<1e-9,"reset restores initial paint cost");

        var curve=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0},_=>{});
        check(Math.Abs(curve.RadiusForShield(1)-1)<1e-4&&Math.Abs(curve.RadiusForShield(4_000_000)-128)<1e-2,
            "shield radius passes through 1 at one shield and meets the previous curve at four million");
        check(Math.Abs(curve.RadiusForShield(4_000_000_000)-240)<1e-2&&Math.Abs(curve.ShieldForRadius(128)-4_000_000)<2000,
            "shield radius caps at 240 at four billion and the inverse matches");

        // Tiny fixed radii isolate individual logical cells and exact paint accounting.
        var paintConfig=new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,
            ShieldBaseRadius=4,ShieldLogRadius=0,ShieldMaxRadius=4,
            BigBallRadiusBase=1,BigBallRadiusLog=0,ExperiencePerAreaSecond=0};
        var shieldPaint=new BattleSimulation(paintConfig,_=>{});
        shieldPaint.Factions[0].Shield=80;
        shieldPaint.Tick();
        double shieldSpent=shieldPaint.Territory.Counts[0]*shieldPaint.PaintCost*TerritoryGrid.CellArea;
        check(shieldPaint.Territory.Counts[0]==4&&Math.Abs(shieldPaint.Factions[0].Shield-(80-shieldSpent))<1e-9,
            "shield paints its disk using its own value at half a point per cell with exponential time scaling");
        check(Math.Abs(shieldPaint.Factions[0].PaintSpent-shieldSpent)<1e-9,"shield paint is included in spending statistics");
        double shieldLeft=shieldPaint.Factions[0].Shield;shieldPaint.Tick();
        check(shieldPaint.Factions[0].Shield==shieldLeft,"shield never pays again for friendly cells");
        shieldPaint.Territory.SetOwner(23*TerritoryGrid.Width+27,1);shieldPaint.Tick();
        check(shieldPaint.Territory.Owners[23*TerritoryGrid.Width+27]==1&&shieldPaint.Factions[0].Shield<shieldLeft,
            "shield repaints enemy cells inside its coverage and pays again");
        shieldPaint.Reset();shieldPaint.Factions[0].Shield=0.25;shieldPaint.Tick();
        check(shieldPaint.Territory.Counts[0]==0&&shieldPaint.Factions[0].Shield==0.25,
            "shield cannot paint a cell it cannot afford and preserves the remaining defense");
        shieldPaint.Reset();shieldPaint.Factions[0].Shield=80;shieldPaint.Factions[0].Alive=false;shieldPaint.Tick();
        check(shieldPaint.Territory.Counts[0]==0&&shieldPaint.Factions[0].Shield==80,"eliminated shields do not paint");
        var ballPaint=new BattleSimulation(paintConfig,_=>{});
        Vector2 paintCenter=ballPaint.Territory.CellCenter(90,90);
        ballPaint.SpawnWeapon(0,paintCenter,Vector2.Zero,80,WeaponKind.BigBall,Ability.BigBall);ballPaint.Tick();
        double cellCost=ballPaint.PaintCost*TerritoryGrid.CellArea;
        check(ballPaint.Territory.Counts[0]==1&&Math.Abs(ballPaint.BigBalls[0].Value-(80-cellCost))<1e-9,
            "big ball pays half a point per painted cell with exponential time scaling instead of the legacy continuous rate");
        check(Math.Abs(ballPaint.Factions[0].PaintSpent-cellCost)<1e-9,"big-ball paint spending matches its value deduction");
        double ballLeft=ballPaint.BigBalls[0].Value;ballPaint.Tick();
        check(ballPaint.BigBalls[0].Value==ballLeft,"big ball does not repay friendly paint");
        ballPaint.Reset();ballPaint.SpawnWeapon(0,paintCenter,Vector2.Zero,0.25,WeaponKind.BigBall,Ability.BigBall);ballPaint.Tick();
        check(!ballPaint.BigBalls[0].Active&&ballPaint.Territory.Counts[0]==0&&ballPaint.Factions[0].PaintRemainder==0.25,
            "unaffordable big-ball paint retires with an accounted remainder and no free cell");

        var battle=new BattleSimulation(config,_=>{});
        // Friendly corridor isolates shield collision spending from territory paint spending.
        battle.Territory.SeedDisk(BattleSimulation.Centers[1],120,0);
        battle.Factions[1].Shield=10;
        float radius=battle.ShieldRadius(1);
        battle.Spawn(0,BattleSimulation.Centers[1]-new Vector2(radius+3,0),new Vector2(600,0),7);
        battle.Tick();
        check(Math.Abs(battle.Factions[1].Shield-3)<1e-9&&!battle.Projectiles[0].Active,"enemy shield consumes projectile and exact damage");
        check(battle.ShieldRadius(1)<radius,"shield collision radius follows remaining value");
        battle.Reset();battle.Territory.SeedDisk(BattleSimulation.Centers[1],120,0);battle.Factions[1].Shield=10;radius=battle.ShieldRadius(1);
        battle.Spawn(0,BattleSimulation.Centers[1]-new Vector2(radius+3,0),new Vector2(600,0),100);
        battle.Tick();
        check(battle.Factions[1].Shield==0&&battle.Factions[1].UnshieldedHits==1&&!battle.Factions[1].Alive,
            "overkill projectile breaks the shield and continues into the unshielded base");
        battle.Reset();battle.Territory.SeedDisk(BattleSimulation.Centers[1],120,0);battle.Factions[1].Shield=0;
        battle.Spawn(0,BattleSimulation.Centers[1]-new Vector2(15,0),new Vector2(600,0),1);
        battle.Tick();
        check(battle.Factions[1].UnshieldedHits==1,"separate later projectile reaches unshielded base");
        battle.Reset();battle.Territory.SeedDisk(BattleSimulation.Centers[0],120,0);battle.Factions[0].Shield=100;
        battle.Spawn(0,BattleSimulation.Centers[0],new Vector2(600,0),7);battle.Tick();
        check(battle.Projectiles[0].Active&&battle.Projectiles[0].Value==7&&battle.Factions[0].Shield==100,"friendly shield is transparent");
        battle.Reset();battle.Territory.SeedDisk(BattleSimulation.Centers[1],120,0);battle.Factions[1].Shield=10;radius=battle.ShieldRadius(1);
        battle.Spawn(0,BattleSimulation.Centers[1]-new Vector2(radius+8,0),new Vector2(6000,0),5);
        battle.Spawn(0,BattleSimulation.Centers[1]-new Vector2(radius+3,0),new Vector2(6000,0),10);
        battle.Tick();
        check(battle.Factions[1].Shield==0&&battle.Factions[1].UnshieldedHits==1,"time of impact takes precedence over pool slot order");
        battle.Reset();battle.Territory.SeedDisk(new Vector2(1837,500),120,0);
        battle.Spawn(0,new Vector2(1837,500),new Vector2(600,0),1);battle.Tick();
        check(battle.Projectiles[0].Velocity.X<0&&battle.Projectiles[0].Value==1,"edge reflects without consuming budget");

        battle.Reset();battle.Accept(new Redemption(0,0,0,Ability.Gun,100));battle.Tick();
        check(Math.Abs(battle.Factions[0].GunReserve+ battle.Factions[0].GunSpent-100)<1e-9,"gun spending conserves reserve");
        var dense=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0,GunInterval=0.02f},_=>{});
        dense.Factions[0].GunReserve=1000;
        for(int i=0;i<6;i++){dense.Factions[0].NextShot=0;dense.Tick();}
        check(Math.Abs(dense.Factions[0].GunSpent-26)<0.0001,"six finer shots consume one original shot budget");
        double low=battle.Projectiles[0].Value;
        battle.Reset();battle.Territory.SeedDisk(BattleSimulation.Centers[0],200,0);battle.Accept(new Redemption(0,0,0,Ability.Gun,1000));battle.Tick();
        check(battle.Projectiles[0].Value>low,"gun per-shot cost grows with current reserve");
        battle.Reset();battle.Accept(new Redemption(0,0,0,Ability.Storm,1e9));
        check(battle.PendingWaves.Count==5&&battle.PendingWaves.All(w=>w.Count<=config.StormProjectilesPerWave),"storm wave and bullet caps");
        check(Math.Abs(battle.PendingWaves.Sum(w=>w.Budget)-1e9)<0.00001,"storm queue conserves total budget");
        check(battle.PendingWaves.Select((wave,i)=>Math.Abs(wave.Due-battle.Time-i*1.5)<1e-9).All(ok=>ok),"storm rounds are scheduled 1.5 simulation seconds apart");
        for(int i=0;i<120;i++)battle.Tick();
        check(battle.TotalShots==config.StormProjectilesPerWave&&battle.PendingWaves.Count==4,"only the first storm round fires within one second");
        for(int i=0;i<120*6;i++)battle.Tick();
        check(battle.PendingWaves.Count==0&&battle.TotalShots==5*config.StormProjectilesPerWave,"bounded storm waves actually fire");
        battle.Reset();battle.Territory.SeedDisk(new Vector2(1200,500),120,0);
        for(int i=0;i<BattleSimulation.ProjectileCapacity;i++)battle.Spawn(0,new Vector2(1200,500),Vector2.Zero,1);
        battle.Accept(new Redemption(0,0,0,Ability.Storm,100));battle.Accept(new Redemption(0,0,0,Ability.Gun,30));battle.Tick();
        check(battle.PendingWaves.Sum(w=>w.Budget)==100&&battle.Factions[0].GunReserve==30,"full projectile pool preserves queued firepower");
        battle.Reset();battle.Accept(new Redemption(0,0,0,Ability.Shotgun,240));
        check(battle.PendingWaves.Count==1&&battle.PendingWaves[0].Shotgun&&battle.PendingWaves[0].Count==config.ShotgunPellets&&Math.Abs(battle.PendingWaves[0].Budget-240)<1e-9,
            "shotgun queues one full-value volley");
        battle.Tick();
        var pellets=battle.Projectiles.Where(p=>p.Active).ToArray();
        check(pellets.Length==config.ShotgunPellets&&pellets.All(p=>p.Source==Ability.Shotgun&&p.Kind==WeaponKind.Normal),
            "shotgun fires every pellet in a single volley");
        check(Math.Abs(pellets.Sum(p=>p.Value)-240)<1e-6,"shotgun pellets conserve the whole redemption value");
        var shotgunAngles=pellets.Select(p=>p.Velocity.Angle()).OrderBy(a=>a).ToArray();
        float shotgunStep=Mathf.DegToRad(config.ShotgunSpreadDegrees)/(config.ShotgunPellets-1);
        bool irregular=false;
        for(int i=1;i<shotgunAngles.Length;i++)
            if(Math.Abs(shotgunAngles[i]-shotgunAngles[i-1]-shotgunStep)>Mathf.DegToRad(0.1f)){irregular=true;break;}
        check(irregular,"shotgun pellet angles carry per-pellet random jitter");
        var shotgunDistances=pellets.Select(p=>p.Position.DistanceTo(BattleSimulation.Centers[0])).ToArray();
        check(shotgunDistances.Max()-shotgunDistances.Min()>2&&pellets.Max(p=>p.Velocity.Length())-pellets.Min(p=>p.Velocity.Length())>1,
            "shotgun pellets stagger in launch distance and speed instead of forming a rigid row");
        for(int i=0;i<BattleSimulation.ProjectileCapacity;i++)battle.Spawn(0,new Vector2(1200,500),Vector2.Zero,1);
        battle.Accept(new Redemption(0,0,0,Ability.Shotgun,100));battle.Tick();
        check(battle.PendingWaves.Count==1&&battle.PendingWaves[0].Shotgun&&Math.Abs(battle.PendingWaves[0].Budget-100)<1e-9,
            "full pool preserves the queued shotgun volley");
        battle.Reset();battle.Accept(new Redemption(0,12,2,Ability.BigBall,123));battle.Accept(new Redemption(0,14,3,Ability.Sniper,456));
        check(battle.ActiveBigBalls==1&&battle.BigBalls[0].Value==123&&battle.Projectiles[0].Kind==WeaponKind.Sniper&&battle.Projectiles[0].Value==456,"big-ball and sniper redemptions create value-bearing entities immediately");

        int[] awards=new int[4];battle=new BattleSimulation(config,f=>awards[f]++);
        battle.IntegrateExperience(10);check(battle.Factions.All(f=>f.Experience==0),"zero area earns no experience");
        battle.Territory.SetOwner(0,0);battle.IntegrateExperience(10);
        double first=battle.Factions[0].Experience;battle.IntegrateExperience(10);
        check(Math.Abs(first-0.5)<1e-9&&Math.Abs(battle.Factions[0].Experience-1)<1e-9,"constant area earns experience continuously at the reduced coefficient");
        battle.Factions[0].Experience=112000+168000+17;battle.IntegrateExperience(0);
        check(awards[0]==2&&battle.Factions[0].Level==2&&battle.Factions[0].Experience==17&&battle.Factions[0].Threshold==224000,"multiple levels preserve overflow using increasing thresholds");

        var match=new MatchSimulation(new SimulationConfig {InitialShield=0});
        for(int i=0;i<110;i++)
        {var b=match.Pinball.Balls[0];b.Phase=BallPhase.Flying;b.Value=2;match.Pinball.Redeem(b,Ability.Shield);}
        check(match.Battle.Factions[0].Shield==220&&match.Pinball.Events.Count==100,"UI history truncation cannot lose gameplay events");
        match.Battle.Factions[0].Experience=112000;match.Battle.IntegrateExperience(0);
        check(match.Pinball.Balls.Count(b=>b.Phase!=BallPhase.Inactive)==PinballSimulation.StartingBallsPerFaction*4+1&&match.Pinball.Balls[PinballSimulation.StartingBallsPerFaction*4].Value==1,"area level-up creates an actual value-one ball");
        match.Reset();check(match.Battle.Factions.All(f=>f.Experience==0&&f.Shield==0)&&match.Pinball.Balls.Count(b=>b.Phase!=BallPhase.Inactive)==PinballSimulation.StartingBallsPerFaction*4,"reset clears both boards and reward state");

        var stopwatch=Stopwatch.StartNew();
        for(int i=0;i<120*600;i++)match.Tick();
        check(match.Battle.TotalShots>0&&match.Battle.Territory.Counts.Sum()>2000,"ten-minute integrated simulation expands territory with real shots");
        check(match.Battle.Factions.Sum(f=>f.ExtraBalls)>0,"integrated area economy awards upgrades");
        check(match.Pinball.Balls.All(b=>b.Phase==BallPhase.Inactive||match.Battle.Factions[b.Faction].Alive||b.IsDeath),"eliminated factions retain only their death ball");
        check(match.Battle.Territory.Counts.Sum()==match.Battle.Territory.Owners.Count(o=>o>0),"incremental territory counters match ownership texture data");
        check(match.Battle.Projectiles.Count(p=>p.Active)==match.Battle.ActiveProjectiles&&match.Battle.Projectiles.All(p=>double.IsFinite(p.Value)&&p.Value>=0),"projectile capacity counters and values remain valid");
        check(Math.Abs(match.Pinball.Time-match.Battle.Time)<1e-9,"both simulation clocks stay in lockstep");
        var owners=(byte[])match.Battle.Territory.Owners.Clone();
        var xp=match.Battle.Factions.Select(f=>f.Experience).ToArray();long shots=match.Battle.TotalShots;
        match.Reset();for(int i=0;i<120*600;i++)match.Tick();
        check(owners.SequenceEqual(match.Battle.Territory.Owners)&&xp.SequenceEqual(match.Battle.Factions.Select(f=>f.Experience))&&shots==match.Battle.TotalShots,"entire match reset/replay is deterministic");
        GD.Print($"BATTLE TEST: 600s replay x2 in {stopwatch.Elapsed.TotalSeconds:0.00}s; shots={shots}, territory={match.Battle.Territory.Counts.Sum()}, upgrades={match.Battle.Factions.Sum(f=>f.ExtraBalls)}.");
    }
}
