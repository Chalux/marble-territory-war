using System;
using System.Collections.Generic;
using Godot;

namespace TerritoryWar;

public enum WeaponKind { Normal, Sniper, Fragment, BigBall }
public readonly record struct WeaponRequest(int Faction,Vector2 Position,Vector2 Velocity,double Value,WeaponKind Kind,Ability Source,bool Death=false,bool Finale=false);

public sealed partial class BattleSimulation
{
    public const int BigBallCapacity=64;
    public readonly ProjectileState[] BigBalls=new ProjectileState[BigBallCapacity];
    public readonly List<WeaponRequest> PendingWeapons=new();
    private readonly List<ProjectileState> _moving=new(ProjectileCapacity+BigBallCapacity);
    private readonly HashSet<(long,long)> _ballContacts=new();
    private readonly HashSet<(long,int)> _shieldContacts=new();
    public int ActiveBigBalls {get;private set;}
    public long TotalBigBalls {get;private set;}
    public long SniperSplits {get;private set;}
    private double _combatNow;
    private void ResetAdvanced()
    {
        PendingWeapons.Clear();_moving.Clear();_ballContacts.Clear();_shieldContacts.Clear();
        ActiveBigBalls=0;TotalBigBalls=SniperSplits=0;
        foreach(var p in BigBalls){p.Active=false;p.Neutron=false;p.Value=0;p.Id=0;}
    }
    public void Notice(string message)
    {
        Notices.Enqueue($"{(int)Time/60:00}:{(int)Time%60:00}  {message}");
        while(Notices.Count>30)Notices.Dequeue();
    }
    public static string FactionName(int f)=>new[]{"红方","绿方","蓝方","黄方"}[f];
    public float BigRadius(double value)=>Math.Min(Config.BigBallMaxRadius,Config.BigBallRadiusBase+Config.BigBallRadiusLog*(float)Math.Log2(1+Math.Max(0,value)));
    private void SizeWeapon(ProjectileState p)
    {
        if(p.Kind==WeaponKind.BigBall&&!p.Neutron&&p.Value>=Config.BigBallNeutronThreshold)
        {
            p.Neutron=true;
            Notice($"{FactionName(p.Faction)}大球坍缩为中子星");
        }
        p.Radius=p.Kind==WeaponKind.BigBall?(p.Neutron?Config.BigBallNeutronRadius:BigRadius(p.Value)):Config.ProjectileRadius;
        p.Mass=p.Kind==WeaponKind.BigBall?1+0.3f*(float)Math.Log2(1+Math.Max(0,p.Value)):1;
    }
    private void QueueSpecial(Redemption ev,WeaponKind kind)
    {
        Vector2 dir=Vector2.FromAngle(Angle(ev.Faction));
        float radius=kind==WeaponKind.BigBall?BigRadius(ev.Value):Config.ProjectileRadius;
        PendingWeapons.Add(new(ev.Faction,Centers[ev.Faction]+dir*(BaseRadius+radius+4),dir*(kind==WeaponKind.BigBall?Config.BigBallSpeed:Config.SniperSpeed),ev.Value,kind,ev.Ability,ev.IsDeath));
    }
    public bool SpawnWeapon(int faction,Vector2 position,Vector2 velocity,double value,WeaponKind kind,Ability source,bool death=false,bool finale=false)
    {
        if(value<=0||!double.IsFinite(value))return false;
        var pool=kind==WeaponKind.BigBall?BigBalls:Projectiles;
        foreach(var p in pool)
        {
            if(p.Active)continue;
            p.Active=true;p.Id=++_nextId;p.Faction=faction;p.Value=p.InitialValue=value;
            p.LastPaintAt=double.NegativeInfinity;
            p.Kind=kind;p.Source=source;p.Death=death;p.Finale=finale;p.Neutron=false;
            p.Position=p.Previous=position;p.Velocity=velocity;SizeWeapon(p);
            ClampToBoard(p);
            if(kind==WeaponKind.BigBall){ActiveBigBalls++;TotalBigBalls++;}else{ActiveProjectiles++;TotalShots++;}
            return true;
        }
        return false;
    }
    private void FlushSpecial()
    {
        for(int i=0;i<PendingWeapons.Count;)
        {
            var w=PendingWeapons[i];
            if(!SpawnWeapon(w.Faction,w.Position,w.Velocity,w.Value,w.Kind,w.Source,w.Death,w.Finale)){i++;continue;}
            PendingWeapons.RemoveAt(i);
        }
    }
    private void ClampToBoard(ProjectileState p)
    {
        var bounds=TerritoryGrid.Bounds;
        p.Position=new(Mathf.Clamp(p.Position.X,bounds.Position.X+p.Radius,bounds.End.X-p.Radius),Mathf.Clamp(p.Position.Y,bounds.Position.Y+p.Radius,bounds.End.Y-p.Radius));
    }
    public void BeginFinale()
    {
        Notice("终盘开始 · 15 秒后按领土结算");
        if(!Config.FinaleReserveStorm)return;
        for(int f=0;f<4;f++)
        {
            double reserve=Factions[f].GunReserve;
            if(!Factions[f].Alive||reserve<=0)continue;
            Factions[f].GunReserve=0;Invested[f,FinaleSlot]+=reserve;
            QueueStorm(f,reserve,false,true);
        }
    }
    private void RecordDamage(ProjectileState p,int target,double damage)
    {
        if(damage<=0)return;
        Damage[p.Faction,(int)p.Source]+=damage;
        if(p.Death)Damage[p.Faction,DeathSlot]+=damage;
        if(p.Finale)Damage[p.Faction,FinaleSlot]+=damage;
        Factions[target].DamageTaken+=damage;
    }
    public void HitBigBall(ProjectileState shot,ProjectileState ball)
    {
        if(!shot.Active||!ball.Active)return;
        float mass=ball.Mass;
        ball.Velocity+=shot.Velocity.Normalized()*(Config.ProjectileImpulse*(float)Math.Log2(1+shot.Value)/mass);
        ball.Velocity=ball.Velocity.LimitLength(500);
        if(shot.Faction==ball.Faction){ball.Value+=shot.Value;Retire(shot);}
        else
        {
            double damage=Math.Min(ball.Value,shot.Value);
            ball.Value-=damage;RecordDamage(shot,ball.Faction,damage);
            // Overkill passes through: only the value spent on the big ball is consumed.
            if(shot.Value-damage>1e-9)shot.Value-=damage;else Retire(shot);
        }
        if(ball.Value<=0)Retire(ball);else{SizeWeapon(ball);ClampToBoard(ball);}
    }
    public void CollideBigBalls(ProjectileState a,ProjectileState b)
    {
        if(!a.Active||!b.Active)return;
        var key=(Math.Min(a.Id,b.Id),Math.Max(a.Id,b.Id));
        Vector2 n=(a.Position-b.Position).Normalized();if(n==Vector2.Zero)n=Vector2.Right;
        if(_ballContacts.Add(key)&&a.Faction!=b.Faction)
        {
            double damage=0.8*Math.Min(a.Value,b.Value);
            a.Value-=damage;b.Value-=damage;RecordDamage(a,b.Faction,damage);RecordDamage(b,a.Faction,damage);
        }
        float approach=(a.Velocity-b.Velocity).Dot(n);
        if(approach<0)
        {
            float impulse=-(1+Config.BigBallRestitution)*approach/(1/a.Mass+1/b.Mass);
            a.Velocity+=n*impulse/a.Mass;b.Velocity-=n*impulse/b.Mass;
        }
        SizeWeapon(a);SizeWeapon(b);
        float overlap=a.Radius+b.Radius-a.Position.DistanceTo(b.Position);
        if(overlap>=0){a.Position+=n*(overlap+0.002f)*0.5f;b.Position-=n*(overlap+0.002f)*0.5f;}
        ClampToBoard(a);ClampToBoard(b);
    }
    public void HitBigShield(ProjectileState ball,int faction)
    {
        if(!ball.Active||!Factions[faction].Alive||ball.Faction==faction)return;
        Vector2 n=(ball.Position-Centers[faction]).Normalized();if(n==Vector2.Zero)n=Vector2.Right;
        if(_shieldContacts.Add((ball.Id,faction)))
        {
            double damage=Math.Min(ball.Value*Config.BigBallShieldTransferRatio,Factions[faction].Shield);
            ball.Value-=damage;Factions[faction].Shield-=damage;
            Factions[ball.Faction].ShieldDamage+=damage;RecordDamage(ball,faction,damage);
        }
        if(ball.Velocity.Dot(n)<0)ball.Velocity=ball.Velocity.Bounce(n)*Config.BigBallRestitution;
        SizeWeapon(ball);
        float minimum=ShieldRadius(faction)+ball.Radius+0.002f;
        if(ball.Position.DistanceTo(Centers[faction])<minimum)ball.Position=Centers[faction]+n*minimum;
        ClampToBoard(ball);
    }
    public void SplitSniper(ProjectileState p,Vector2 direction)
    {
        if(!p.Active||p.Kind!=WeaponKind.Sniper)return;
        int count=Math.Clamp(Config.SniperFragments,1,64);double remaining=p.Value;
        float angle=direction.Angle();
        for(int i=0;i<count;i++)
        {
            double share=i==count-1?remaining:p.Value/count;remaining-=share;
            float spread=count==1?0:((float)i/(count-1)-0.5f)*Config.FragmentSpread;
            Vector2 dir=Vector2.FromAngle(angle+spread);
            if(share>0)PendingWeapons.Add(new(p.Faction,p.Position+direction.Normalized()*0.01f,dir*Config.ProjectileSpeed,share,WeaponKind.Fragment,Ability.Sniper,p.Death,p.Finale));
        }
        SniperSplits++;Retire(p);
    }
    public void HitSniperShield(ProjectileState p,int faction)
    {
        Vector2 center=Centers[faction],dir=p.Velocity.Normalized();
        Vector2 normal=(p.Position-center).Normalized();if(normal==Vector2.Zero)normal=-dir;
        // Follow the actual forward segment to the next board reflection, not just the contact point.
        var bounds=TerritoryGrid.Bounds;float length=100000;
        if(dir.X>0)length=Math.Min(length,(bounds.End.X-p.Radius-p.Position.X)/dir.X);
        if(dir.X<0)length=Math.Min(length,(bounds.Position.X+p.Radius-p.Position.X)/dir.X);
        if(dir.Y>0)length=Math.Min(length,(bounds.End.Y-p.Radius-p.Position.Y)/dir.Y);
        if(dir.Y<0)length=Math.Min(length,(bounds.Position.Y+p.Radius-p.Position.Y)/dir.Y);
        float along=Mathf.Clamp((center-p.Position).Dot(dir),0,Math.Max(0,length));
        float distance=center.DistanceTo(p.Position+dir*along);
        float allowed=Math.Max(0,distance-p.Radius-0.02f);
        double target=allowed<=0?0:ShieldForRadius(allowed);
        double need=Math.Max(0,Factions[faction].Shield-target),budget=Math.Max(0,p.Value-p.InitialValue*0.2);
        double damage=Math.Min(need,budget);Factions[faction].Shield-=damage;p.Value-=damage;
        Factions[p.Faction].ShieldDamage+=damage;RecordDamage(p,faction,damage);
        if(p.Value<=p.InitialValue*0.2+1e-8||damage+1e-8<need)SplitSniper(p,p.Velocity.Bounce(normal));
        else p.Position+=dir*0.001f;
    }
    private void HitBase(ProjectileState p,int faction)
    {
        Factions[faction].UnshieldedHits++;
        if(BaseHit!=null)BaseHit(faction);else{Factions[faction].Alive=false;Factions[faction].GunReserve=0;Factions[faction].EliminatedAt=Time;}
        if(p.Kind==WeaponKind.BigBall)
        {
            Vector2 normal=(p.Position-Centers[faction]).Normalized();if(normal==Vector2.Zero)normal=Vector2.Right;
            if(p.Velocity.Dot(normal)<0)p.Velocity=p.Velocity.Bounce(normal);
            p.Position=Centers[faction]+normal*(Math.Max(BaseRadius,ShieldRadius(faction))+p.Radius+0.01f);ClampToBoard(p);
        }
        else Retire(p);
    }
}
