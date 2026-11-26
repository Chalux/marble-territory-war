using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TerritoryWar;

public partial class Battlefield
{
    private bool _particlesEnabled=true;
    // Gun glow/core, storm glow/front, sniper glow/core, orbital particles.
    private static readonly float[] EffectWidths={4f,1.1f,8f,1.6f,9f,1.7f,1.2f};
    private static readonly float[] EffectAlpha={0.13f,0.85f,0.10f,0.65f,0.14f,1f,0.9f};
    private readonly List<Vector2>[] _effects=Enumerable.Range(0,28).Select(_=>new List<Vector2>()).ToArray();
    private readonly List<Vector2> _tailPath=new(8);

    public void SetParticlesEnabled(bool enabled)
    {
        _particlesEnabled=enabled;
        _flowView.Visible=enabled;
        _material.SetShaderParameter("particles_enabled",enabled);
        foreach(var material in _shieldMaterials)material.SetShaderParameter("particles_enabled",enabled);
        QueueRedraw();
    }

    private void Stroke(int faction,int layer,Vector2 from,Vector2 to)
    {
        // Clip the entire stroke, including glow and AA, rather than moving its tips.
        var bounds=TerritoryGrid.Bounds.Grow(-EffectWidths[layer]*0.5f-1);
        if(!ProjectileTrailGeometry.Clip(ref from,ref to,bounds))return;
        var batch=_effects[faction*7+layer];batch.Add(from);batch.Add(to);
    }

    private void Tail(ProjectileState p,float length,int layer)
    {
        _tailPath.Clear();
        ProjectileTrailGeometry.Append(_tailPath,p.Position,p.Velocity,length,TerritoryGrid.Bounds.Grow(-p.Radius));
        for(int i=0;i<_tailPath.Count;i+=2)Stroke(p.Faction,layer,_tailPath[i],_tailPath[i+1]);
    }

    private void DrawProjectiles()
    {
        foreach(var batch in _effects)batch.Clear();
        float time=(float)Simulation.Time;
        foreach(var p in Battle.Projectiles)
        {
            if(!p.Active)continue;
            if(p.Kind==WeaponKind.Normal && (p.Source==Ability.Gun || p.Source==Ability.Shotgun || p.Source==Ability.Storm))continue;
            Vector2 direction=p.Velocity.Normalized(),side=direction.Orthogonal();
            if(p.Kind==WeaponKind.Sniper)
            {
                Tail(p,100,4);Tail(p,72,5);
                float envelope=Math.Clamp(1-(float)(Simulation.Time-p.LastPaintAt)/0.18f,0,1);
                float flash=envelope*(0.35f+0.65f*MathF.Pow(0.5f+0.5f*MathF.Sin(time*Mathf.Tau*9),2));
                float size=3+flash*7;
                Stroke(p.Faction,5,p.Position-direction*size,p.Position+direction*size);
                Stroke(p.Faction,5,p.Position-side*size*0.65f,p.Position+side*size*0.65f);
                Stroke(p.Faction,4,p.Position-side*size,p.Position+side*size);
                if(_particlesEnabled)
                {
                    // Counter-rotating elliptical satellites, on the simulation clock.
                    for(int k=0;k<8;k++)
                    {
                        float angle=time*(k%2==0?5:-4)+k*Mathf.Tau/8+p.Id*0.17f;
                        float radius=11+(k%2)*5+flash*4;
                        Vector2 Orbit(float a)=>p.Position+direction*MathF.Cos(a)*radius*0.55f+side*MathF.Sin(a)*radius;
                        for(int j=0;j<3;j++)Stroke(p.Faction,6,Orbit(angle-j*0.08f),Orbit(angle-(j+1)*0.08f));
                    }
                }
            }
            else
            {
                bool fragment=p.Kind==WeaponKind.Fragment;
                Tail(p,fragment?22:38,0);Tail(p,fragment?12:24,1);
                Stroke(p.Faction,fragment?5:1,p.Position-direction*2,p.Position+direction*2);
            }
        }
        for(int f=0;f<4;f++)for(int layer=0;layer<7;layer++)
        {
            var batch=_effects[f*7+layer];if(batch.Count==0)continue;
            Color tint=Main.FactionColors[f].Lightened(layer==5?0.85f:layer==6?0.65f:layer%2==1?0.35f:0);
            DrawMultiline(CollectionsMarshal.AsSpan(batch),new Color(tint,EffectAlpha[layer]),EffectWidths[layer],true);
        }
    }
}
