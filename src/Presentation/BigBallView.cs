using Godot;
namespace TerritoryWar;
public partial class BigBallView:Node2D
{
    private ColorRect _body;private Label _label;private ShaderMaterial _material;
    private bool _neutron;
    private float _radius;
    private int _faction;
    private double _time;
    private double _collapseAt=double.NegativeInfinity;
    public override void _Ready(){_body=GetNode<ColorRect>("Body");_label=GetNode<Label>("Value");_material=(ShaderMaterial)_body.Material;}
    public void ShowState(ProjectileState state,double time)
    {
        Visible=state.Active;if(!Visible)return;
        bool wasNeutron=_neutron;
        if(state.Neutron&&!wasNeutron)_collapseAt=time;
        _neutron=state.Neutron;_radius=state.Radius;_faction=state.Faction;_time=time;
        Position=state.Position;
        // The compact neutron core keeps the same UV layout; the shader paints the collapsed body.
        // A neutron star renders a wide lensing halo around its small collision radius.
        float visual=_neutron?_radius*4.2f:_radius*2.28f;
        _body.Position=Vector2.One*(-visual*0.5f);_body.Size=Vector2.One*visual;
        _material.SetShaderParameter("tint",Main.FactionColors[_faction]);
        _material.SetShaderParameter("neutron",_neutron?1f:0f);
        _label.Text=Main.Number(state.Value);
        int fontSize=Mathf.Clamp((int)(_radius*(_neutron?0.72f:0.48f)),_neutron?14:18,40);
        _label.AddThemeFontSizeOverride("font_size",fontSize);
        _label.AddThemeColorOverride("font_color",_neutron?new Color("d8f0ff"):Colors.White);
        _label.Position=new Vector2(-55,_neutron?-fontSize*2.6f:-fontSize*0.7f);
        if(_neutron||wasNeutron)QueueRedraw();
    }
    public override void _Draw()
    {
        if(!Visible||!_neutron)return;
        Color tint=Main.FactionColors[_faction];
        // Faint orbital rings read as a gravity well; the burst marks the collapse moment.
        for(int i=0;i<3;i++)
        {
            float phase=(float)((_time*0.30+i*0.37)%1.0);
            float ring=_radius*(1.5f+phase*4.2f);
            float alpha=(1-phase)*0.16f;
            DrawArc(Vector2.Zero,ring,0,Mathf.Tau,48,new Color(tint,alpha),1.2f,true);
        }
        for(int i=0;i<3;i++)
        {
            float angle=(float)(_time*1.6+i*Mathf.Tau/3);
            Vector2 point=Vector2.FromAngle(angle)*(_radius*2.1f);
            DrawCircle(point,1.3f,new Color(0.82f,0.92f,1f,0.65f));
        }
        double age=_time-_collapseAt;
        if(age>=0&&age<0.8)
        {
            float wave=_radius*(1+ (float)age*7);
            DrawArc(Vector2.Zero,wave,0,Mathf.Tau,64,new Color(0.8f,0.92f,1f,(float)((0.8-age)*0.5)),2,true);
        }
    }
}
