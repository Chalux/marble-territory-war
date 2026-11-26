using Godot;
using System.Runtime.InteropServices;
using System;
namespace TerritoryWar;
// perf pass: presentation layer optimization (batching + allocation reuse)

public partial class Battlefield : Node2D
{
    public PinballSimulation Simulation;
    public BattleSimulation Battle;
    private Font _font;
    private readonly ColorRect[] _shields=new ColorRect[4];
    private readonly FactionCoreView[] _cores=new FactionCoreView[4];
    private readonly ShaderMaterial[] _shieldMaterials=new ShaderMaterial[4];
    private readonly Label[] _reserves = new Label[4];
    private readonly Label[] _values = new Label[4];
    private readonly Label[] _experience = new Label[4];
    private readonly Label[] _percentages = new Label[4];
    private readonly BigBallView[] _bigViews=new BigBallView[BattleSimulation.BigBallCapacity];
    private readonly byte[] _pixels = new byte[TerritoryGrid.Width * TerritoryGrid.Height * 4];
    private readonly byte[] _coveragePixels=new byte[TerritoryGrid.Width*TerritoryGrid.Height];
    private Image _coverageImage;
    private ImageTexture _coverageTexture;
    private readonly VisualTerritoryContour _contour = new();
    private Image _image;
    private ImageTexture _texture;
    private ShaderMaterial _material;
    private VisualPaintAge _paintAge;
    private TerritoryFlowView _flowView;
    private long _revision = -1;
    private readonly record struct Trail(Vector2 From,Vector2 To,int Faction,double Expires,long ProjectileId,double Lifetime);
    private readonly Trail[] _trails=new Trail[65536];
    private readonly int[] _trailSlots=new int[BattleSimulation.ProjectileCapacity];
    private readonly long[] _trailIds=new long[BattleSimulation.ProjectileCapacity];
    private readonly System.Collections.Generic.List<Vector2>[] _trailBatches=new System.Collections.Generic.List<Vector2>[16];
    private int _trailCursor, _trailCount;
    private double _lastTrailTime=-1;
    private BattleSimulation _observedBattle;
    // Called after each fixed simulation step so trails do not depend on frame rate.
    public void ObserveStep()
    {
        _paintAge?.Observe(Battle.Territory,Simulation.Time);
        if(Battle!=_observedBattle || Simulation.Time<_lastTrailTime)
        {
            Array.Clear(_trails);Array.Fill(_trailSlots,-1);Array.Fill(_trailIds,-1);
            _trailCursor=0;_trailCount=0;_revision=-1;_observedBattle=Battle;
        }
        if(Simulation.Time==_lastTrailTime)return;
        _flowView?.Observe(Battle,Simulation.Time);
        for(int i=0;i<Battle.Projectiles.Length;i++)
        {
            var p=Battle.Projectiles[i];
            if(!p.Active){_trailSlots[i]=-1;continue;}
            if(p.Source==Ability.Gun && p.Kind==WeaponKind.Normal){_trailSlots[i]=-1;continue;}
            // Shotgun pellets reuse the gun projectile art and do not need lingering trails.
            if(p.Source==Ability.Shotgun && p.Kind==WeaponKind.Normal){_trailSlots[i]=-1;continue;}
            // A storm reads as a moving front, without a forest of lingering gun trails.
            if(p.Source==Ability.Storm && p.Kind==WeaponKind.Normal){_trailSlots[i]=-1;continue;}
            double lifetime=p.Kind==WeaponKind.Sniper?1.2:p.Kind==WeaponKind.Fragment?0.3:0.45;
            int slot=_trailSlots[i];
            bool extend=slot>=0 && _trailIds[i]==p.Id;
            if(extend)
            {
                var old=_trails[slot];
                Vector2 delta=p.Position-old.To;
                extend=old.ProjectileId==p.Id && old.To.DistanceSquaredTo(p.Previous)<0.01f &&
                    (old.To-old.From).Normalized().Dot(delta.Normalized())>0.995f &&
                    old.From.DistanceSquaredTo(p.Position)<900;
                if(extend)_trails[slot]=old with {To=p.Position,Expires=Simulation.Time+lifetime};
            }
            if(!extend && p.Previous.DistanceSquaredTo(p.Position)>0.0001f)
            {
                _trails[_trailCursor]=new(p.Previous,p.Position,p.Faction,Simulation.Time+lifetime,p.Id,lifetime);
                _trailSlots[i]=_trailCursor;_trailIds[i]=p.Id;
                _trailCursor=(_trailCursor+1)%_trails.Length;
                _trailCount=Math.Min(_trailCount+1,_trails.Length);
            }
        }
        _lastTrailTime=Simulation.Time;
    }
    public override void _Ready()
    {
        _font=GD.Load<Font>("res://resource/reference_font.tres");
        for(int i=0;i<_trailBatches.Length;i++)_trailBatches[i]=new();
        for(int i=0;i<_bigViews.Length;i++)_bigViews[i]=GetNode<BigBallView>($"BigBallPool/Ball{i:00}");
        for(int f=0;f<4;f++)
        {
            _shields[f]=GetNode<ColorRect>($"Shield{f}");
            _cores[f]=GetNode<FactionCoreView>($"Base{f}");
            _shieldMaterials[f]=(ShaderMaterial)_shields[f].Material;
            _shieldMaterials[f].SetShaderParameter("tint",Main.FactionColors[f]);
            _values[f]=GetNode<Label>($"Base{f}/Values");
            _reserves[f]=GetNode<Label>($"Base{f}/Reserve");
            _experience[f]=GetNode<Label>($"Experience{f}");
            _percentages[f]=GetNode<Label>($"Percentage{f}");
        }
        _image=Image.CreateFromData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.Rgba8,_pixels);
        _texture=ImageTexture.CreateFromImage(_image);
        _material=(ShaderMaterial)GetNode<ColorRect>("Territory").Material;
        _material.SetShaderParameter("contour_field",_texture);
        _coverageImage=Image.CreateFromData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.R8,_coveragePixels);
        _coverageTexture=ImageTexture.CreateFromImage(_coverageImage);
        _material.SetShaderParameter("coverage_field",_coverageTexture);
        _paintAge=new VisualPaintAge();
        _material.SetShaderParameter("paint_age_field",_paintAge.Texture);
        _flowView=GetNode<TerritoryFlowView>("TerritoryFlow");
        InitializeGunVisuals();
    }
    public void Refresh()
    {
        ObserveStep();
        RefreshGunVisuals();
        for(int i=0;i<_bigViews.Length;i++)_bigViews[i].ShowState(Battle.BigBalls[i],Simulation.Time);
        float frameDelta=Mathf.Clamp((float)GetProcessDeltaTime(),0,0.1f);
        for(int f=0;f<4;f++)
        {
            var state=Battle.Factions[f];
            var core=_cores[f];
            core.Tint=Main.FactionColors[f];core.Angle=Battle.Angle(f);core.Alive=state.Alive;core.QueueRedraw();
            float radius=AnimateShield(f,frameDelta);
            _shields[f].Visible=radius>0.01f;
            _shields[f].Position=BattleSimulation.Centers[f]-Vector2.One*radius*1.22f;
            _shields[f].Size=Vector2.One*radius*2.44f;
            var shieldMaterial=_shieldMaterials[f];
            shieldMaterial.SetShaderParameter("simulation_time",(float)Simulation.Time);
            _values[f].Text=state.Alive?DisplayReserve(state.Shield):"已淘汰";
            _values[f].Modulate=Colors.White;
            _reserves[f].Text=state.Alive?DisplayReserve(state.GunReserve):$"{(int)state.EliminatedAt/60:00}:{(int)state.EliminatedAt%60:00}";
            _reserves[f].Modulate=Main.FactionColors[f].Lightened(0.65f);
            _percentages[f].Text=$"{state.Experience/state.Threshold*100:0.00}%";
            _percentages[f].Modulate=Main.FactionColors[f];
            _experience[f].Text=$"{state.Experience:N0}\n/ {state.Threshold:N0}";
            _experience[f].Modulate=new Color("939ba5");
        }
        // Present ownership from the same simulation step as the projectile positions.
        // Throttling this upload lets shots visibly outrun the painted frontier.
        if(Battle.Territory.Revision!=_revision)
        {
            _contour.Update(Battle.Territory.Owners,_pixels,_coveragePixels);
            _coverageImage.SetData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.R8,_coveragePixels);
            _coverageTexture.Update(_coverageImage);
            _image.SetData(TerritoryGrid.Width,TerritoryGrid.Height,false,Image.Format.Rgba8,_pixels);
            _texture.Update(_image);_revision=Battle.Territory.Revision;
        }
        _material.SetShaderParameter("simulation_time",(float)Simulation.Time);
        _flowView.Refresh();
        _paintAge.Upload();
        QueueRedraw();
    }
    public override void _Draw()
    {
        if(Battle==null)return;
        foreach(var batch in _trailBatches)batch.Clear();
        foreach(var trail in _trails.AsSpan(0,_trailCount))
        {
            double remaining=trail.Expires-Simulation.Time;
            if(remaining<=0)continue;
            int age=Math.Clamp((int)(remaining/trail.Lifetime*4),0,3);
            var batch=_trailBatches[trail.Faction*4+age];
            batch.Add(trail.From);batch.Add(trail.To);
        }
        for(int i=0;i<_trailBatches.Length;i++)
            if(_trailBatches[i].Count>0)
            {
                var points=CollectionsMarshal.AsSpan(_trailBatches[i]);Color tint=Main.FactionColors[i/4];
                DrawMultiline(points,new Color(tint,0.025f+0.025f*(i%4)),4.5f,true);
                DrawMultiline(points,new Color(tint.Lightened(0.1f),0.035f+0.11f*(i%4)),1.1f,true);
            }
        DrawProjectiles();
        for(int f=0;f<4;f++)
        {
            var state=Battle.Factions[f];
            Color color=Main.FactionColors[f];
            // Attraction is drawn by TerritoryFlowView from actual owned cells.
            float railX=f%2==0?720:1878; bool top=f<2;
            Vector2 from=new(railX,top?143:937),to=new(railX,top?480:600);
            DrawLine(from,to,new Color(color,0.18f),2,true);
            Vector2 progress=to.Lerp(from,Math.Clamp((float)(state.Experience/state.Threshold),0,1));
            DrawLine(to,progress,new Color(color,0.1f),12,true);
            DrawLine(to,progress,new Color(color,0.28f),6,true);
            DrawLine(to,progress,color.Lightened(0.65f),2,true);
            for(int k=1;k<=3;k++)
            {
                float y=top?143+k*84:937-k*84;
                DrawCircle(new(railX,y),7,new Color(color,0.08f));
                DrawPolyline(new[]{new Vector2(railX,y-6),new Vector2(railX+5,y),new Vector2(railX,y+6),new Vector2(railX-5,y),new Vector2(railX,y-6)},new Color(color,0.6f),0.8f,true);
                DrawString(_font,new(railX-13,y-10),$"{100-k*25}%",HorizontalAlignment.Left,-1,10,new Color("a3acb5"));
            }
            Vector2 badge=new(railX,top?120:960);
            DrawCircle(badge,28,new Color(color,0.07f));
            DrawArc(badge,28,0,Mathf.Tau,48,new Color(color,0.35f),1,true);
            DrawArc(badge,24,0,Mathf.Tau,48,new Color(color,0.6f),1,true);
            DrawCircle(badge,21,new Color(color,0.22f)); DrawArc(badge,21,0,Mathf.Tau,40,color,1.5f,true);
            DrawString(_font,badge+new Vector2(-5,6),state.Level.ToString(),HorizontalAlignment.Left,-1,19,Colors.White);
            DrawString(_font,new(railX-22,top?91:995),"充能升级",HorizontalAlignment.Left,-1,10,new Color("a3acb5"));
            DrawString(_font,new(railX-14,top?156:927),"100%",HorizontalAlignment.Left,-1,10,new Color("a3acb5"));

        }
    }
    private static string DisplayReserve(double value)=>value==0?"0.00":value<1e6?value.ToString("N0",System.Globalization.CultureInfo.InvariantCulture):Main.Number(value);
    public override void _ExitTree(){_coverageTexture?.Dispose();_coverageImage?.Dispose();_paintAge?.Dispose();_texture?.Dispose();_image?.Dispose();}
}
