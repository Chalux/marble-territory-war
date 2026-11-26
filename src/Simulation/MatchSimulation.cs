using System;
using System.Linq;
using System.Collections.Generic;

namespace TerritoryWar;

public enum MatchPhase { Running, Finale, Finished }
public sealed class MatchSimulation
{
    public readonly PinballSimulation Pinball;
    public readonly BattleSimulation Battle;
    public readonly List<Redemption> RedemptionLog=new();
    public MatchPhase Phase {get;private set;}
    public double FinaleStarted {get;private set;}=-1;
    public double FinaleRemaining=>Phase==MatchPhase.Finale?Math.Max(0,15-(Battle.Time-FinaleStarted)):0;
    public int[] Winners {get;private set;}=Array.Empty<int>();
    public MatchSimulation(SimulationConfig config)
    {
        Pinball=new PinballSimulation(config);Battle=new BattleSimulation(config,Pinball.AwardBall);
        Pinball.AbilityRedeemed+=ev=>{RedemptionLog.Add(ev);Battle.Accept(ev);};Battle.BaseHit+=Defeat;
    }
    public void Tick()
    {
        if(Phase==MatchPhase.Finished)return;
        Pinball.Tick();Battle.Tick();UpdateEndgame();
    }
    public void Reset(){Pinball.Reset();Battle.Reset();RedemptionLog.Clear();Phase=MatchPhase.Running;FinaleStarted=-1;Winners=Array.Empty<int>();}
    public void Defeat(int faction)
    {
        var state=Battle.Factions[faction];if(!state.Alive)return;
        if(!state.ReviveUsed)
        {
            state.ReviveUsed=true;
            double sacrifice=Pinball.SacrificeLargest(faction);
            if(sacrifice>0)
            {
                double proportion=state.Experience/state.Threshold;
                state.Level=Math.Max(0,state.Level-1);state.Experience=proportion*state.Threshold;
                state.Shield=sacrifice;state.Revivals++;
                Battle.Notice($"{BattleSimulation.FactionName(faction)}复活 · 牺牲最大弹珠 {sacrifice:0.##}");return;
            }
        }
        state.Alive=false;state.Shield=state.GunReserve=0;state.EliminatedAt=Battle.Time;
        Pinball.Eliminate(faction);Battle.Notice($"{BattleSimulation.FactionName(faction)}淘汰 · 亡语球聚合");
    }
    public void UpdateEndgame()
    {
        if(Phase==MatchPhase.Finished)return;
        if(Phase==MatchPhase.Finale)
        {
            if(Battle.Time-FinaleStarted<15-1e-8)return;
            int maximum=Battle.Territory.Counts.Max();
            Winners=Enumerable.Range(0,4).Where(f=>Battle.Territory.Counts[f]==maximum).ToArray();
            Phase=MatchPhase.Finished;Battle.Notice("终盘结束 · 按最终领土面积结算");return;
        }
        if(Pinball.HasDeathBalls)return;
        int mask=0;
        for(int f=0;f<4;f++)if(Battle.Factions[f].Alive)mask|=1<<f;
        foreach(var b in Battle.BigBalls)if(b.Active)mask|=1<<b.Faction;
        foreach(var w in Battle.PendingWeapons)if(w.Kind==WeaponKind.BigBall)mask|=1<<w.Faction;
        if(mask!=0&&(mask&(mask-1))!=0)return;
        Phase=MatchPhase.Finale;FinaleStarted=Battle.Time;Battle.BeginFinale();
    }
}
