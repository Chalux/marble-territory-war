using System;
using System.Diagnostics;
using System.Linq;
using Godot;
namespace TerritoryWar;
public static class CompletionTest
{
    public static void Run(SceneTree tree,SimulationConfig config)
    {
        try
        {
            foreach(string arg in OS.GetCmdlineUserArgs())if(arg.StartsWith("--seed="))config.Seed=int.Parse(arg.Split('=')[1]);
            var match=new MatchSimulation(config);var timer=Stopwatch.StartNew();
            double maxBig=0;bool neutronSeen=false;
            for(int i=0;i<120*3600&&match.Phase!=MatchPhase.Finished;i++)
            {
                match.Tick();
                foreach(var b in match.Battle.BigBalls)if(b.Active){maxBig=Math.Max(maxBig,b.Value);neutronSeen|=b.Neutron;}
                if(i%(120*120)==0)GD.Print($"SOAK t={match.Battle.Time:0}s alive={match.Battle.Factions.Count(f=>f.Alive)} big={match.Battle.ActiveBigBalls} maxBig={maxBig:G6} neutron={neutronSeen} shots={match.Battle.ActiveProjectiles} death={match.Pinball.HasDeathBalls} phase={match.Phase}");
                if(match.Battle.BigBalls.Any(b=>b.Active&&(!double.IsFinite(b.Value)||b.Value<=0)))throw new Exception("Invalid big-ball state");
            }
            if(match.Phase!=MatchPhase.Finished)
            {
                foreach(var b in match.Battle.BigBalls)if(b.Active)GD.Print($"SURVIVOR id={b.Id} faction={b.Faction} value={b.Value:G8} pos={b.Position} vel={b.Velocity} radius={b.Radius}");
                throw new Exception("Natural match did not finish within 3600 simulated seconds");
            }
            using var file=Godot.FileAccess.Open($"res://.godot/completed-match-{config.Seed}.json",Godot.FileAccess.ModeFlags.Write);
            file.StoreString(ResultsView.BuildJson(match));
            GD.Print($"COMPLETE MATCH PASS seed={config.Seed} duration={match.Battle.Time:0.00}s wall={timer.Elapsed.TotalSeconds:0.00}s winners={string.Join(',',match.Winners)} splits={match.Battle.SniperSplits} interceptions={match.Battle.Interceptions} maxBig={maxBig:G6} neutron={neutronSeen} revivals={match.Battle.Factions.Sum(f=>f.Revivals)}");tree.Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());tree.Quit(1);}
    }
}
