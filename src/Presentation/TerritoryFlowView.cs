using Godot;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
namespace TerritoryWar;

// Long-lived decorative particles follow curved paths from owned cells to their base.
// Their clock and private hash are independent of frame rate and gameplay randomness.
public partial class TerritoryFlowView : Node2D
{
    private readonly record struct Mote(Vector2 From,Vector2 To,float Turn,float Phase,int Faction,double Born,float Life);
    private readonly Mote[] _motes=new Mote[4096];
    private readonly List<Vector2>[] _lines=new List<Vector2>[12];
    private readonly List<Vector2>[] _tips=new List<Vector2>[4];
    private readonly double[] _debt=new double[4];
    private BattleSimulation _battle;
    private double _time=-1;
    private int _cursor;
    private uint _seed=17;
    public override void _Ready()
    {
        for(int i=0;i<_lines.Length;i++)_lines[i]=new();
        for(int i=0;i<4;i++)_tips[i]=new();
    }
    private float Random()
    {
        _seed=unchecked(_seed*1664525u+1013904223u);
        return (_seed>>8)/16777216f;
    }
    public void Observe(BattleSimulation battle,double time)
    {
        if(battle!=_battle||time<_time)
        {
            Array.Clear(_motes);Array.Clear(_debt);_cursor=0;_seed=17;_time=time;
        }
        double dt=Math.Max(0,time-_time);_time=time;_battle=battle;
        for(int f=0;f<4;f++)
        {
            if(!battle.Factions[f].Alive||battle.Territory.Counts[f]==0)continue;
            _debt[f]+=dt*Math.Clamp(50+battle.Territory.Counts[f]/120.0,50,325);
            while(_debt[f]>=1){_debt[f]--;Spawn(f,time);}
        }
    }
    private void Spawn(int f,double time)
    {
        int cell=-1;
        for(int attempt=0;attempt<48;attempt++)
        {
            int candidate=(int)(Random()*_battle.Territory.Owners.Length);
            if(_battle.Territory.Owners[candidate]!=f+1)continue;
            int x=candidate%TerritoryGrid.Width,y=candidate/TerritoryGrid.Width;
            bool edge=x==0||y==0||x==TerritoryGrid.Width-1||y==TerritoryGrid.Height-1;
            if(!edge)edge=_battle.Territory.Owners[candidate-1]!=f+1||_battle.Territory.Owners[candidate+1]!=f+1||
                _battle.Territory.Owners[candidate-TerritoryGrid.Width]!=f+1||_battle.Territory.Owners[candidate+TerritoryGrid.Width]!=f+1;
            if(edge||attempt>=32){cell=candidate;break;}
        }
        if(cell<0)return;
        Vector2 from=_battle.Territory.CellCenter(cell%TerritoryGrid.Width,cell/TerritoryGrid.Width);
        from+=new Vector2(Random()-0.5f,Random()-0.5f)*4;
        Vector2 to=BattleSimulation.Centers[f],delta=to-from;
        float distance=delta.Length();
        if(distance<20)return;
        float turn=(f==0||f==3?1:-1)*(1.0f+Random()*2.0f);
        _motes[_cursor]=new(from,to,turn,Random()*Mathf.Tau,f,time,2.8f+distance/260f+Random()*2.0f);
        _cursor=(_cursor+1)%_motes.Length;
    }
    private static Vector2 Point(Mote m,float t)
    {
        float v=1-t;
        float curl=m.Turn*t+MathF.Sin(t*Mathf.Tau*1.4f+m.Phase)*MathF.Sin(t*Mathf.Pi)*0.22f;
        return m.To+(m.From-m.To).Rotated(curl)*MathF.Pow(v,1.15f);
    }
    private bool Owned(Vector2 p,int faction)
    {
        var local=p-TerritoryGrid.Origin;
        int x=(int)MathF.Floor(local.X/TerritoryGrid.CellSize),y=(int)MathF.Floor(local.Y/TerritoryGrid.CellSize);
        return x>=0&&x<TerritoryGrid.Width&&y>=0&&y<TerritoryGrid.Height&&_battle.Territory.Owners[y*TerritoryGrid.Width+x]==faction+1;
    }
    public void Refresh()=>QueueRedraw();
    public override void _Draw()
    {
        if(_battle==null)return;
        foreach(var batch in _lines)batch.Clear();foreach(var batch in _tips)batch.Clear();
        foreach(var m in _motes)
        {
            if(m.Life<=0)continue;
            float age=(float)((_time-m.Born)/m.Life);
            if(age<=0||age>=1)continue;
            // Accelerate into the core, with a short fading wake behind each mote.
            float end=age*age;
            float start=Math.Max(0,end-0.16f);
            Vector2 previous=Point(m,start);
            bool previousOwned=Owned(previous,m.Faction);
            for(int k=1;k<=6;k++)
            {
                Vector2 next=Point(m,Mathf.Lerp(start,end,k/6f));
                var batch=_lines[m.Faction*3+(k-1)/2];
                bool nextOwned=Owned(next,m.Faction);
                if(previousOwned&&nextOwned){batch.Add(previous);batch.Add(next);}
                previousOwned=nextOwned;
                previous=next;
            }
            // Fade the last few pixels into the absorbing core.
            if(age<0.96f&&Owned(previous,m.Faction)){_tips[m.Faction].Add(previous-new Vector2(0.65f,0));_tips[m.Faction].Add(previous+new Vector2(0.65f,0));}
        }
        for(int f=0;f<4;f++)
        {
            Color color=Main.FactionColors[f];
            for(int b=0;b<3;b++)if(_lines[f*3+b].Count>0)
                DrawMultiline(CollectionsMarshal.AsSpan(_lines[f*3+b]),new Color(color,0.008f+b*0.018f),0.7f,false);
            if(_tips[f].Count==0)continue;
            var tips=CollectionsMarshal.AsSpan(_tips[f]);
            DrawMultiline(tips,new Color(color,0.07f),4,false);
            DrawMultiline(tips,new Color(color.Lightened(0.6f),0.7f),1.1f,false);
        }
    }
}
