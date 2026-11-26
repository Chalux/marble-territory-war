using Godot;

namespace TerritoryWar;

public partial class Battlefield
{
    private MultiMesh _gunMesh, _stormMesh;
    private ShaderMaterial _gunMaterial, _stormMaterial;

    private void InitializeGunVisuals()
    {
        _gunMesh=CreateProjectileMesh("GunProjectiles","gun_projectile",new Vector2(48,18),out _gunMaterial);
        _stormMesh=CreateProjectileMesh("StormProjectiles","storm_projectile",new Vector2(26,28),out _stormMaterial);
    }

    private MultiMesh CreateProjectileMesh(string nodeName,string shaderName,Vector2 size,out ShaderMaterial material)
    {
        var mesh=new MultiMesh {
            TransformFormat=MultiMesh.TransformFormatEnum.Transform2D,
            UseColors=true,UseCustomData=true,
            Mesh=new QuadMesh {Size=size},
            InstanceCount=BattleSimulation.ProjectileCapacity,VisibleInstanceCount=0
        };
        material=new ShaderMaterial {Shader=GD.Load<Shader>($"res://resource/shaders/{shaderName}.gdshader")};
        var view=new MultiMeshInstance2D {Name=nodeName,Multimesh=mesh,Material=material};
        AddChild(view);MoveChild(view,1);
        return mesh;
    }

    private void RefreshGunVisuals()
    {
        int count=0, storms=0;
        foreach(var p in Battle.Projectiles)
        {
            if(!p.Active || p.Kind!=WeaponKind.Normal)continue;
            if(p.Source==Ability.Storm)
            {
                _stormMesh.SetInstanceTransform2D(storms,new Transform2D(p.Velocity.Angle(),p.Position));
                _stormMesh.SetInstanceColor(storms,Main.FactionColors[p.Faction]);
                _stormMesh.SetInstanceCustomData(storms,new Color(p.Faction/4f,0,0,1));
                storms++;
                continue;
            }
            if(p.Source!=Ability.Gun && p.Source!=Ability.Shotgun)continue;
            Vector2 direction=p.Velocity.Normalized();
            _gunMesh.SetInstanceTransform2D(count,new Transform2D(direction.Angle(),p.Position-direction*14));
            _gunMesh.SetInstanceColor(count,Main.FactionColors[p.Faction]);
            _gunMesh.SetInstanceCustomData(count,new Color((p.Id%997)/997f,0,0,1));
            count++;
        }
        _gunMesh.VisibleInstanceCount=count;
        _stormMesh.VisibleInstanceCount=storms;
        _stormMaterial.SetShaderParameter("simulation_time",(float)Simulation.Time);
        _gunMaterial.SetShaderParameter("simulation_time",(float)Simulation.Time);
    }
}
