using System;
using System.Linq;
using Godot;

namespace TerritoryWar;
public static class AdvancedTests
{
    public static void RunStandalone(SceneTree tree)
    {
        try{int count=0;Run((ok,message)=>{if(!ok)throw new Exception(message);count++;});GD.Print($"ADVANCED TEST PASS: {count} checks");tree.Quit();}
        catch(Exception e){GD.PushError(e.ToString());tree.Quit(1);}
    }
    public static void Run(Action<bool,string> check)
    {
        var config=new SimulationConfig{InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0,ExperiencePerAreaSecond=0};
        BattleSimulation Fresh()=>new(config,_=>{});
        var battle=Fresh();Vector2 point=new(1200,500);
        battle.SpawnWeapon(0,point,Vector2.Right*20,100,WeaponKind.BigBall,Ability.BigBall);
        var big=battle.BigBalls[0];float radius=big.Radius;
        battle.Spawn(0,point,Vector2.Right*100,50);battle.HitBigBall(battle.Projectiles[0],big);
        check(big.Value==150&&big.Radius>radius&&!battle.Projectiles[0].Active&&big.Velocity.X>20,"friendly shot adds value, grows radius, applies impulse and disappears");
        battle.Spawn(1,point,Vector2.Left*100,150);battle.HitBigBall(battle.Projectiles[0],big);
        check(!big.Active&&big.Value==0&&battle.ActiveBigBalls==0,"exact hostile depletion destroys big ball immediately");
        battle.Spawn(0,point,Vector2.Zero,500);battle.HitBigBall(battle.Projectiles[0],big);
        check(!big.Active&&big.Value==0,"inactive big ball cannot be revived by a later shot");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Right*20,1e7,WeaponKind.BigBall,Ability.BigBall);
        battle.SpawnWeapon(1,point+Vector2.Right*80,Vector2.Left*20,4e6,WeaponKind.BigBall,Ability.BigBall);
        var a=battle.BigBalls[0];var b=battle.BigBalls[1];battle.CollideBigBalls(a,b);
        check(a.Value==6.8e6&&b.Value==0.8e6,"enemy big balls lose eighty percent of the same pre-contact minimum");
        battle.CollideBigBalls(a,b);check(a.Value==6.8e6&&b.Value==0.8e6,"continuous contact does not repeat big-ball damage");
        a.Position=point;b.Position=point+Vector2.Right*(a.Radius+b.Radius+0.002f);a.Velocity=Vector2.Right*20;b.Velocity=Vector2.Left*20;battle.Tick();
        check(a.Value==6.8e6&&b.Value==0.8e6&&a.Velocity.X<20,"recompression within a contact rebounds physically without repeating damage");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Right*20,100,WeaponKind.BigBall,Ability.BigBall);
        battle.SpawnWeapon(0,point+Vector2.Right*20,Vector2.Left*20,50,WeaponKind.BigBall,Ability.BigBall);
        battle.CollideBigBalls(battle.BigBalls[0],battle.BigBalls[1]);
        check(battle.BigBalls[0].Value==100&&battle.BigBalls[1].Value==50,"friendly big balls only rebound");
        battle=Fresh();battle.Factions[1].Shield=30;
        battle.SpawnWeapon(0,BattleSimulation.Centers[1]-Vector2.Right*60,Vector2.Right*20,100,WeaponKind.BigBall,Ability.BigBall);
        big=battle.BigBalls[0];battle.HitBigShield(big,1);
        check(big.Value==70&&battle.Factions[1].Shield==0&&big.Velocity.X<0,"insufficient shield transfers only existing shield value and reflects big ball");
        battle.Factions[1].Shield=100;battle.HitBigShield(big,1);
        check(big.Value==70&&battle.Factions[1].Shield==100,"ongoing shield contact is deduplicated");
        big.Position=point;battle.Tick();big.Position=BattleSimulation.Centers[1]-Vector2.Right*70;big.Velocity=Vector2.Right*20;battle.HitBigShield(big,1);
        check(Math.Abs(big.Value-14)<1e-8&&Math.Abs(battle.Factions[1].Shield-44)<1e-8,"separation rearms shield transfer using current big-ball value");

        battle=Fresh();battle.Factions[1].Shield=1000;
        battle.SpawnWeapon(0,BattleSimulation.Centers[1]-Vector2.Right*100,Vector2.Right*720,100,WeaponKind.Sniper,Ability.Sniper);
        var sniper=battle.Projectiles[0];battle.HitSniperShield(sniper,1);
        check(!sniper.Active&&battle.SniperSplits==1&&battle.Factions[1].Shield==920,"sniper reserves twenty percent and splits at insufficient shield budget");
        check(Math.Abs(battle.PendingWeapons.Sum(w=>w.Value)-20)<1e-9&&battle.PendingWeapons.All(w=>w.Velocity.X<0),"shield-triggered fragments conserve value and follow reflected direction");
        battle.SplitSniper(sniper,Vector2.Right);check(battle.SniperSplits==1,"sniper split is idempotent");
        battle=Fresh();battle.Factions[1].Shield=10000;
        battle.SpawnWeapon(0,BattleSimulation.Centers[1]+new Vector2(-110,60),Vector2.Right*720,1e6,WeaponKind.Sniper,Ability.Sniper);
        sniper=battle.Projectiles[0];battle.HitSniperShield(sniper,1);
        check(sniper.Active&&battle.ShieldRadius(1)+sniper.Radius<60&&battle.Factions[1].Shield>0,"sniper geometry shrinks shield enough for the whole forward path");
        check(Math.Abs((1e6-sniper.Value)-(10000-battle.Factions[1].Shield))<1e-6,"sniper value spent equals shield removed");
        battle=new BattleSimulation(new SimulationConfig{InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0},_=>{});
        foreach(var faction in battle.Factions)faction.Alive=false;
        battle.SpawnWeapon(0,point,Vector2.Right*720,100,WeaponKind.Sniper,Ability.Sniper);
        battle.SpawnWeapon(0,point,Vector2.Right*720,100,WeaponKind.Fragment,Ability.Sniper);
        for(int i=0;i<120*30;i++)battle.Tick();
        check(battle.Projectiles[0].Active&&battle.Projectiles[1].Active&&battle.SniperSplits==0,"sniper and fragments no longer expire with time");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Zero,999_999_999,WeaponKind.BigBall,Ability.BigBall);
        var ordinary=battle.BigBalls[0];
        check(!ordinary.Neutron&&ordinary.Radius>config.BigBallNeutronRadius,"big ball below one billion stays a normal body");
        battle.SpawnWeapon(0,point,Vector2.Zero,2_000_000_000,WeaponKind.BigBall,Ability.BigBall);
        var star=battle.BigBalls[1];
        check(star.Neutron&&Math.Abs(star.Radius-config.BigBallNeutronRadius)<1e-6,"big ball above one billion collapses to a compact neutron star");
        battle.Spawn(1,point,Vector2.Zero,1.5e9);battle.HitBigBall(battle.Projectiles[0],star);
        check(Math.Abs(star.Value-5e8)<1e-6&&star.Neutron&&Math.Abs(star.Radius-config.BigBallNeutronRadius)<1e-6,"neutron collapse is irreversible after value drops");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Zero,5e8,WeaponKind.BigBall,Ability.BigBall);
        battle.Spawn(1,point+Vector2.Up*300,Vector2.Zero,1);battle.Tick();
        check(battle.Projectiles.First(p=>p.Active).Velocity==Vector2.Zero,"ordinary big balls do not pull projectiles");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Zero,2e9,WeaponKind.BigBall,Ability.BigBall);
        battle.Spawn(1,point+Vector2.Up*300,Vector2.Zero,1);battle.Tick();
        var pulled=battle.Projectiles.First(p=>p.Active);
        check(pulled.Velocity.Y>0&&pulled.Velocity.X==0,"neutron star gravity pulls projectiles toward it");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Zero,2e9,WeaponKind.BigBall,Ability.BigBall);
        battle.SpawnWeapon(1,point+Vector2.Up*300,Vector2.Zero,1000,WeaponKind.BigBall,Ability.BigBall);
        var host=battle.BigBalls[0];var moon=battle.BigBalls[1];battle.Tick();
        check(moon.Velocity.Y>0&&moon.Velocity.X==0&&host.Velocity==Vector2.Zero,"neutron gravity also pulls big balls but not the star itself");
        battle=Fresh();
        battle.SpawnWeapon(0,new Vector2(1200,500),Vector2.Right*300,100,WeaponKind.Normal,Ability.Gun);
        battle.SpawnWeapon(1,new Vector2(1210,500),Vector2.Left*300,40,WeaponKind.Normal,Ability.Gun);
        battle.Tick();
        check(battle.Interceptions==1&&battle.Projectiles[0].Active&&Math.Abs(battle.Projectiles[0].Value-60)<1e-9&&!battle.Projectiles[1].Active,
            "hostile projectiles cancel values and the larger one keeps the remainder");
        battle=Fresh();
        battle.SpawnWeapon(0,new Vector2(1200,500),Vector2.Right*300,50,WeaponKind.Normal,Ability.Gun);
        battle.SpawnWeapon(1,new Vector2(1210,500),Vector2.Left*300,50,WeaponKind.Normal,Ability.Gun);
        battle.Tick();
        check(battle.Interceptions==1&&!battle.Projectiles[0].Active&&!battle.Projectiles[1].Active&&battle.ActiveProjectiles==0,
            "equal hostile projectiles annihilate each other");
        battle=Fresh();
        battle.SpawnWeapon(0,new Vector2(1200,500),Vector2.Right*300,50,WeaponKind.Normal,Ability.Gun);
        battle.SpawnWeapon(0,new Vector2(1210,500),Vector2.Left*300,50,WeaponKind.Normal,Ability.Gun);
        battle.Tick();
        check(battle.Interceptions==0&&battle.Projectiles[0].Active&&battle.Projectiles[1].Active,
            "same-faction projectiles still pass through each other");
        battle=Fresh();
        battle.SpawnWeapon(0,new Vector2(1200,500),Vector2.Right*6000,10,WeaponKind.Normal,Ability.Gun);
        battle.SpawnWeapon(1,new Vector2(1250,500),Vector2.Left*6000,10,WeaponKind.Normal,Ability.Gun);
        battle.Tick();
        check(battle.Interceptions==1&&!battle.Projectiles[0].Active&&!battle.Projectiles[1].Active,
            "interception sweep catches very fast hostile pairs");
        battle=Fresh();battle.Factions[1].Shield=1000;
        float passRadius=battle.ShieldRadius(1);
        Vector2 passCenter=BattleSimulation.Centers[1];
        battle.Spawn(0,passCenter+new Vector2(-(passRadius+30),-(passRadius-1)),new Vector2(600,0),5000);
        for(int i=0;i<30&&battle.Factions[1].Shield>0;i++)battle.Tick();
        battle.Tick();
        var passing=battle.Projectiles.First(p=>p.Active);
        check(battle.Factions[1].Shield==0&&battle.Factions[1].UnshieldedHits==0&&Math.Abs(passing.Value-4000)<1e-6,
            "overkill projectile passes through a destroyed shield with its remaining value");
        battle=Fresh();battle.SpawnWeapon(0,point,Vector2.Zero,100,WeaponKind.BigBall,Ability.BigBall);
        var bigTarget=battle.BigBalls[0];
        battle.Spawn(1,point,Vector2.Right*100,250);battle.HitBigBall(battle.Projectiles[0],bigTarget);
        check(!bigTarget.Active&&battle.Projectiles[0].Active&&Math.Abs(battle.Projectiles[0].Value-150)<1e-9,
            "overkill projectile continues after destroying a big ball");
        battle=Fresh();
        battle.SpawnWeapon(0,point,Vector2.Right*100,100,WeaponKind.BigBall,Ability.BigBall);
        battle.SpawnWeapon(0,point+Vector2.Right*60,Vector2.Left*100,100,WeaponKind.BigBall,Ability.BigBall);
        var friendlyA=battle.BigBalls[0];var friendlyB=battle.BigBalls[1];
        for(int i=0;i<20;i++)battle.Tick();
        check(friendlyA.Active&&friendlyB.Active&&friendlyA.Value==100&&friendlyB.Value==100,
            "same-faction big-ball collisions never deduct value");
        battle=Fresh();for(int i=0;i<BattleSimulation.BigBallCapacity;i++)battle.SpawnWeapon(0,point,Vector2.Zero,1,WeaponKind.BigBall,Ability.BigBall);
        battle.Accept(new Redemption(0,0,0,Ability.BigBall,123));
        check(battle.ActiveBigBalls==64&&battle.PendingWeapons.Sum(w=>w.Value)==123,"big-ball pool exhaustion preserves complete queued budget");

        var match=new MatchSimulation(config);var state=match.Battle.Factions[0];
        match.Pinball.Balls[0].Value=100;match.Pinball.Balls[4].Value=100;state.Level=2;state.Experience=112000;
        match.Defeat(0);
        check(state.Alive&&state.ReviveUsed&&state.Shield==100&&match.Pinball.Balls[0].Phase==BallPhase.Inactive&&match.Pinball.Balls[4].Phase!=BallPhase.Inactive,"revive sacrifices exactly one largest ball using stable ID tie-break");
        check(state.Level==1&&state.Experience==84000,"revive demotion preserves experience proportion");
        state.GunReserve=999;match.Defeat(0);
        var death=match.Pinball.Balls.Single(p=>p.Faction==0&&p.Phase!=BallPhase.Inactive);
        check(!state.Alive&&state.GunReserve==0&&death.IsDeath&&death.Value==101,"second defeat aggregates one death ball and clears gun");
        double xp=state.Experience;match.Battle.Territory.SetOwner(0,0);match.Battle.IntegrateExperience(10);
        check(state.Experience==xp,"eliminated faction earns no further experience");
        death.Phase=BallPhase.Flying;death.Velocity=Vector2.Down*200;
        match.Pinball.Redeem(death,Ability.Gun);match.Pinball.Redeem(death,Ability.Shield);
        check(death.Phase==BallPhase.Flying&&death.Value==101&&death.Velocity.Y<0&&match.RedemptionLog.Count==0,"death ball rejects gun and shield without spending value");
        match.Pinball.Redeem(death,Ability.Storm);
        check(death.Phase==BallPhase.Inactive&&match.RedemptionLog.Single().IsDeath&&!state.Alive&&match.Battle.PendingWaves.Sum(w=>w.Budget)==101,"death ability fires after elimination and consumes death ball once");
        match=new MatchSimulation(config);match.Defeat(0);
        check(match.Battle.Factions[0].Level==0&&match.Battle.Factions[0].Alive,"level-zero revival is allowed once");
        match=new MatchSimulation(config);foreach(var p in match.Pinball.Balls)if(p.Faction==0)p.Phase=BallPhase.Inactive;match.Defeat(0);
        check(!match.Battle.Factions[0].Alive&&!match.Pinball.Balls.Any(p=>p.Faction==0&&p.IsDeath),"no-ball revival fails without creating a phantom death ball");
        match=new MatchSimulation(config);match.Pinball.Balls[0].Value=10;match.Pinball.Balls[4].Value=20;
        match.Battle.Factions[0].ReviveUsed=true;match.Defeat(0);
        check(match.Pinball.Balls.Single(p=>p.Faction==0&&p.Phase!=BallPhase.Inactive).Value==31,"death ball sums all current surviving ball values");
        match=new MatchSimulation(config);match.Pinball.Balls[0].Value=1000;match.Pinball.Balls[4].Value=1;
        match.Battle.Territory.SeedDisk(BattleSimulation.Centers[0]-Vector2.Right*20,120,1);
        match.Battle.Spawn(1,BattleSimulation.Centers[0]-Vector2.Right*15,Vector2.Right*6000,5);
        match.Battle.Spawn(1,BattleSimulation.Centers[0]-Vector2.Right*20,Vector2.Right*6000,5);
        match.Battle.Tick();
        check(match.Battle.Factions[0].Alive&&match.Battle.Factions[0].Revivals==1&&match.Battle.Factions[0].Shield==995,"later same-step attack sees revived shield");

        match=EndScenario(config);match.Battle.Factions[0].Alive=true;
        var d=match.Pinball.Balls[1];d.IsDeath=true;d.Faction=1;d.Phase=BallPhase.Flying;d.Value=1;
        match.UpdateEndgame();check(match.Phase==MatchPhase.Running,"unfinished death ball blocks finale");
        match.Pinball.Redeem(d,Ability.BigBall);match.UpdateEndgame();
        check(match.Phase==MatchPhase.Running&&match.Battle.ActiveBigBalls==1,"death-spawned enemy big ball still blocks finale");
        match.Battle.Spawn(0,point,Vector2.Zero,10);match.Battle.HitBigBall(match.Battle.Projectiles[0],match.Battle.BigBalls[0]);match.UpdateEndgame();
        check(match.Phase==MatchPhase.Finale,"last enemy big-ball removal starts finale");
        double started=match.FinaleStarted;match.Battle.Factions[0].Alive=false;match.UpdateEndgame();
        check(match.Phase==MatchPhase.Finale&&match.FinaleStarted==started,"all bases disappearing does not reset started countdown");
        match=EndScenario(config);match.Battle.Territory.SetOwner(0,0);match.Battle.Territory.SetOwner(1,1);match.UpdateEndgame();
        for(int i=0;i<1799;i++)match.Tick();check(match.Phase==MatchPhase.Finale,"finale lasts a full fifteen simulation seconds");
        match.Tick();check(match.Phase==MatchPhase.Finished&&match.Winners.SequenceEqual(new[]{0,1}),"final-area ties include eliminated factions");
        double ended=match.Battle.Time;match.Tick();check(match.Battle.Time==ended,"finished match freezes exactly once");
        string json=ResultsView.BuildJson(match);check(json.Contains("redemptions")&&json.Contains("winners"),"export report includes outcome and traceable redemption log");
        match=EndScenario(config);match.Battle.Factions[0].Alive=true;match.Battle.Factions[0].GunReserve=123;
        match.UpdateEndgame();check(match.Battle.Factions[0].GunReserve==0&&match.Battle.PendingWaves.Sum(w=>w.Budget)==123,"final storm converts remaining gun reserve without creating budget");
        match.Reset();check(match.Phase==MatchPhase.Running&&match.Battle.ActiveBigBalls==0&&match.RedemptionLog.Count==0&&!match.Pinball.HasDeathBalls,"reset clears advanced weapons, lifecycle, logs and finale");
        match=EndScenario(config);match.Battle.Factions[0].Alive=true;
        match.Battle.PendingWeapons.Add(new(1,point,Vector2.Zero,10,WeaponKind.BigBall,Ability.BigBall,true));match.UpdateEndgame();
        check(match.Phase==MatchPhase.Running,"queued enemy big ball blocks finale before it obtains a pool slot");
        match.Battle.PendingWeapons.Clear();match.Battle.Spawn(1,point,Vector2.Zero,10);match.UpdateEndgame();
        check(match.Phase==MatchPhase.Finale,"enemy projectile alone does not block finale");
    }
    private static MatchSimulation EndScenario(SimulationConfig config)
    {
        var match=new MatchSimulation(config);
        foreach(var f in match.Battle.Factions){f.Alive=false;f.EliminatedAt=0;}
        foreach(var p in match.Pinball.Balls)p.Phase=BallPhase.Inactive;
        return match;
    }
}
