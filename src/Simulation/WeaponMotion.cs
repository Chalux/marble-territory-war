using System;
using Godot;

namespace TerritoryWar;

public sealed partial class BattleSimulation
{
    private enum ContactKind { Wall, Base, BigBall, Projectile }
    private const float InterceptionCellSize = 32f;
    private int _interceptionWidth, _interceptionHeight;
    private int[] _interceptionHead = Array.Empty<int>();
    private readonly int[] _interceptionNext = new int[ProjectileCapacity];
    private float _interceptionMaxSpeed;
    private void MoveWeapons(float duration)
    {
        ApplyNeutronGravity(duration);
        _moving.Clear();
        foreach(var p in Projectiles)if(p.Active){p.Previous=p.Position;_moving.Add(p);}
        foreach(var p in BigBalls)if(p.Active){p.Previous=p.Position;_moving.Add(p);}
        _moving.Sort((a,b)=>a.Id.CompareTo(b.Id));
        _combatNow=Time-duration;
        float remaining=duration;
        Span<float> baseRadii=stackalloc float[4];
        while(remaining>1e-7f)
        {
            // A contact can deplete a shield. Refresh once per sweep, before testing the projectiles.
            for(int f=0;f<4;f++)
                baseRadii[f]=Factions[f].Shield>0?ShieldRadius(f):BaseRadius;
            if(ActiveProjectiles>1)BuildInterceptionGrid();
            int interceptionRange=0;
            if(ActiveProjectiles>1)
            {
                // Search wide enough for the fastest pair to meet before this sweep ends.
                float reach=2*_interceptionMaxSpeed*remaining+2*Config.ProjectileRadius;
                interceptionRange=Mathf.Clamp(Mathf.CeilToInt(reach/InterceptionCellSize),1,Mathf.Max(_interceptionWidth,_interceptionHeight));
            }
            var bigTargets=ActiveBigBalls==0?ReadOnlySpan<ProjectileState>.Empty:BigBalls.AsSpan();
            float earliest=remaining;ProjectileState hit=null,other=null;int target=-1;
            Vector2 normal=Vector2.Zero;ContactKind kind=ContactKind.Wall;
            foreach(var p in _moving)
            {
                if(!p.Active)continue;
                void Candidate(float time,ContactKind k,int faction,Vector2 n,ProjectileState body=null)
                {
                    if(time< -1e-6f||time>remaining)return;
                    time=Math.Max(0,time);
                    if(hit==null||time<earliest||(time==earliest&&p.Id<hit.Id))
                    {earliest=time;hit=p;other=body;target=faction;normal=n;kind=k;}
                }
                ClampToBoard(p);var bounds=TerritoryGrid.Bounds;float r=p.Radius;
                if(p.Velocity.X<0)Candidate((bounds.Position.X+r-p.Position.X)/p.Velocity.X,ContactKind.Wall,-1,Vector2.Right);
                if(p.Velocity.X>0)Candidate((bounds.End.X-r-p.Position.X)/p.Velocity.X,ContactKind.Wall,-1,Vector2.Left);
                if(p.Velocity.Y<0)Candidate((bounds.Position.Y+r-p.Position.Y)/p.Velocity.Y,ContactKind.Wall,-1,Vector2.Down);
                if(p.Velocity.Y>0)Candidate((bounds.End.Y-r-p.Position.Y)/p.Velocity.Y,ContactKind.Wall,-1,Vector2.Up);
                for(int f=0;f<4;f++)
                {
                    if(f==p.Faction||!Factions[f].Alive)continue;
                    float radius=baseRadii[f]+r;
                    if(p.Kind==WeaponKind.BigBall&&_shieldContacts.Contains((p.Id,f)))
                    {
                        if(p.Position.DistanceSquaredTo(Centers[f])>(radius+0.05f)*(radius+0.05f))_shieldContacts.Remove((p.Id,f));
                        else if(Factions[f].Shield>0&&(p.Position-Centers[f]).Dot(p.Velocity)>=0)continue;
                    }
                    float time=CircleTime(p.Position,p.Velocity,Centers[f],radius);
                    if(time>=0)Candidate(time,ContactKind.Base,f,Vector2.Zero);
                }
                foreach(var ball in bigTargets)
                {
                    if(!ball.Active||ball==p||(p.Kind==WeaponKind.BigBall&&ball.Id<p.Id))continue;
                    float radius=p.Radius+ball.Radius;
                    if(p.Kind==WeaponKind.BigBall)
                    {
                        var key=(Math.Min(p.Id,ball.Id),Math.Max(p.Id,ball.Id));
                        if(_ballContacts.Contains(key))
                        {
                            if(p.Position.DistanceSquaredTo(ball.Position)>(radius+0.05f)*(radius+0.05f))_ballContacts.Remove(key);
                            else if((p.Position-ball.Position).Dot(p.Velocity-ball.Velocity)>=0)continue;
                        }
                    }
                    float time=CircleTime(p.Position,p.Velocity-ball.Velocity,ball.Position,radius);
                    if(time>=0)Candidate(time,ContactKind.BigBall,-1,Vector2.Zero,ball);
                }
                if(p.Kind!=WeaponKind.BigBall&&interceptionRange>0)
                {
                    int cell=InterceptionCell(p.Position);
                    int cx=cell%_interceptionWidth, cy=cell/_interceptionWidth;
                    int minX=Math.Max(0,cx-interceptionRange), maxX=Math.Min(_interceptionWidth-1,cx+interceptionRange);
                    int minY=Math.Max(0,cy-interceptionRange), maxY=Math.Min(_interceptionHeight-1,cy+interceptionRange);
                    for(int ny=minY;ny<=maxY;ny++)
                    for(int nx=minX;nx<=maxX;nx++)
                    for(int j=_interceptionHead[ny*_interceptionWidth+nx];j>=0;j=_interceptionNext[j])
                    {
                        var q=Projectiles[j];
                        // Stable ID ordering evaluates each hostile pair exactly once.
                        if(!q.Active||q.Faction==p.Faction||q.Id<=p.Id)continue;
                        float time=CircleTime(p.Position,p.Velocity-q.Velocity,q.Position,p.Radius+q.Radius);
                        if(time>=0)Candidate(time,ContactKind.Projectile,-1,Vector2.Zero,q);
                    }
                }
            }
            AdvanceWeapons(earliest);remaining-=earliest;_combatNow+=earliest;
            if(hit==null)break;
            if(!hit.Active)continue;
            switch(kind)
            {
                case ContactKind.Wall:
                    hit.Velocity=hit.Velocity.Bounce(normal);hit.Position+=normal*0.002f;break;
                case ContactKind.BigBall:
                    if(other==null||!other.Active)break;
                    if(hit.Position.DistanceTo(other.Position)>hit.Radius+other.Radius+0.02f)break;
                    if(hit.Kind==WeaponKind.BigBall)CollideBigBalls(hit,other);else HitBigBall(hit,other);
                    break;
                case ContactKind.Projectile:
                    if(other==null||!other.Active||hit.Faction==other.Faction)break;
                    if(hit.Position.DistanceTo(other.Position)>hit.Radius+other.Radius+0.02f)break;
                    Interceptions++;
                    double cancelled=Math.Min(hit.Value,other.Value);
                    hit.Value-=cancelled;other.Value-=cancelled;
                    if(hit.Value<=0)Retire(hit);
                    if(other.Value<=0)Retire(other);
                    break;
                case ContactKind.Base:
                    if(!Factions[target].Alive)break;
                    float radius=(Factions[target].Shield>0?ShieldRadius(target):BaseRadius)+hit.Radius;
                    if(hit.Position.DistanceTo(Centers[target])>radius+0.02f)break;
                    if(Factions[target].Shield<=0){HitBase(hit,target);break;}
                    if(hit.Kind==WeaponKind.BigBall){HitBigShield(hit,target);break;}
                    if(hit.Kind==WeaponKind.Sniper){HitSniperShield(hit,target);break;}
                    double damage=Math.Min(Factions[target].Shield,hit.Value);
                    Factions[target].Shield-=damage;Factions[hit.Faction].ShieldDamage+=damage;RecordDamage(hit,target,damage);
                    // A projectile that breaks the shield keeps its overkill and flies on.
                    if(hit.Value-damage>1e-9)hit.Value-=damage;else Retire(hit);break;
            }
        }
        // Contacts are identity-based; retired/reused pool slots cannot inherit a contact.
        _ballContacts.RemoveWhere(pair=>!BigExists(pair.Item1)||!BigExists(pair.Item2));
        _shieldContacts.RemoveWhere(pair=>!BigExists(pair.Item1));
    }
    private bool BigExists(long id){foreach(var b in BigBalls)if(b.Active&&b.Id==id)return true;return false;}
    // Uniform grid for hostile projectile pairs. Membership is rebuilt after every sweep event.
    private void BuildInterceptionGrid()
    {
        var bounds=TerritoryGrid.Bounds;
        _interceptionWidth=Mathf.Max(1,Mathf.CeilToInt(bounds.Size.X/InterceptionCellSize));
        _interceptionHeight=Mathf.Max(1,Mathf.CeilToInt(bounds.Size.Y/InterceptionCellSize));
        int cells=_interceptionWidth*_interceptionHeight;
        if(_interceptionHead.Length!=cells)_interceptionHead=new int[cells];
        Array.Fill(_interceptionHead,-1);
        _interceptionMaxSpeed=0;
        for(int i=0;i<Projectiles.Length;i++)
        {
            var p=Projectiles[i];
            if(!p.Active)continue;
            int cell=InterceptionCell(p.Position);
            _interceptionNext[i]=_interceptionHead[cell];
            _interceptionHead[cell]=i;
            _interceptionMaxSpeed=MathF.Max(_interceptionMaxSpeed,p.Velocity.Length());
        }
    }
    private int InterceptionCell(Vector2 position)
    {
        var bounds=TerritoryGrid.Bounds;
        int x=Mathf.Clamp((int)((position.X-bounds.Position.X)/InterceptionCellSize),0,_interceptionWidth-1);
        int y=Mathf.Clamp((int)((position.Y-bounds.Position.Y)/InterceptionCellSize),0,_interceptionHeight-1);
        return y*_interceptionWidth+x;
    }
    // Every ordinary projectile and every big ball feels the pull of collapsed big balls.
    // A star skips its own gravity, but other stars and all big balls respond.
    private void ApplyNeutronGravity(float dt)
    {
        if(ActiveBigBalls==0||dt<=0)return;
        bool any=false;
        foreach(var b in BigBalls)if(b.Active&&b.Neutron){any=true;break;}
        if(!any)return;
        foreach(var p in Projectiles)
        {
            if(!p.Active)continue;
            PullTowardNeutronStars(p,dt);
        }
        foreach(var p in BigBalls)
        {
            if(!p.Active)continue;
            PullTowardNeutronStars(p,dt);
        }
    }
    private void PullTowardNeutronStars(ProjectileState p,float dt)
    {
        foreach(var star in BigBalls)
        {
            if(!star.Active||!star.Neutron||star==p)continue;
            Vector2 delta=star.Position-p.Position;
            float distanceSquared=delta.LengthSquared();
            if(distanceSquared<1e-6f)continue;
            // Denser stars pull harder, but on a gentle log curve so late-match values stay playable.
            float density=1+0.35f*MathF.Log2(Math.Max(1f,(float)(star.Value/Config.BigBallNeutronThreshold)));
            float acceleration=MathF.Min(Config.NeutronGravityMaxAcceleration,Config.NeutronGravityStrength*density/distanceSquared);
            p.Velocity+=delta/MathF.Sqrt(distanceSquared)*(acceleration*dt);
        }
    }
    private void AdvanceWeapons(float dt)
    {
        if(dt<=0)return;
        double cost=PaintCost;
        foreach(var p in _moving)
        {
            if(!p.Active)continue;
            Vector2 next=p.Position+p.Velocity*dt;
            // All weapons paint continuously along their actual movement segments.
            double reserve=p.Kind==WeaponKind.Sniper?p.InitialValue*0.2:0;
            double budget=Math.Max(0,p.Value-reserve);
            double spent=Territory.PaintSegment(p.Position,next,p.Kind==WeaponKind.BigBall?p.Radius:Config.ProjectilePaintRadius,p.Faction,cost,ref budget,out bool blocked);
            Factions[p.Faction].PaintSpent+=spent;p.Position=next;p.Value=budget+reserve;
            if(spent>0)p.LastPaintAt=_combatNow+dt;
            if(blocked&&budget<cost*TerritoryGrid.CellArea)
            {Factions[p.Faction].PaintRemainder+=budget;p.Value=reserve;}
            if(p.Kind==WeaponKind.Sniper&&p.Value<=reserve){SplitSniper(p,p.Velocity);continue;}
            if(p.Value<=0){Retire(p);continue;}
            if(p.Kind==WeaponKind.BigBall)SizeWeapon(p);
        }
    }
}
