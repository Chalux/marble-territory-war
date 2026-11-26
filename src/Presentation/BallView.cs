using Godot;
namespace TerritoryWar;

public partial class BallView : Node2D
{
    public bool ParticlesEnabled=true;
    private Label _label;
    private float _radius;
    private Color _color;
    private Vector2 _trail;
    private readonly Vector2[] _history=new Vector2[80];
    private readonly double[] _times=new double[80];
    private int _cursor,_count;
    private double _time;
    private bool _sparkles;
    public void Observe(BallState state,double time)
    {
        if(time<_time || state.Phase!=BallPhase.Flying){_count=0;_cursor=0;}
        _time=time;
        if(state.Phase!=BallPhase.Flying)return;
        if(_count>0)
        {
            Vector2 last=_history[(_cursor+79)%80];
            if(last.DistanceSquaredTo(state.Position)>40000){_count=0;_cursor=0;}
            else if(last.DistanceSquaredTo(state.Position)<4)return;
        }
        _history[_cursor]=state.Position;_times[_cursor]=time;
        _cursor=(_cursor+1)%80;_count=System.Math.Min(_count+1,80);
    }
    public override void _Ready() => _label = GetNode<Label>("Value");
    public void ShowState(BallState state)
    {
        Visible = state.Phase == BallPhase.Flying;
        if (!Visible) return;
        Position = state.Position; _radius = state.Radius;
        _sparkles=state.Value>=4096;
        _color = Main.FactionColors[state.Faction]; _trail = -state.Velocity * 0.045f;
        // The reference uses whole K/M values on moving balls so labels fit the orb.
        string value=state.Value>=1e9?(state.Value/1e9).ToString("0")+"B":
            state.Value>=1e6?(state.Value/1e6).ToString("0")+"M":
            state.Value>=1e3?(state.Value/1e3).ToString("0")+"K":state.Value.ToString("0");
        _label.Text = (state.IsDeath?"亡语 ":"")+value;
        _label.Position = new Vector2(-42, _radius>=17?-11:-_radius-22);
        QueueRedraw();
    }
    public override void _Draw()
    {
        for(int i=1;i<_count;i++)
        {
            int current=(_cursor-i+80)%80,previous=(_cursor-i-1+160)%80;
            float fade=1-(float)(_time-_times[current])/(_sparkles?0.32f:0.1f);
            if(fade<=0)break;
            Vector2 a=_history[current]-Position,b=_history[previous]-Position;
            DrawLine(a,b,new Color(_color,fade*0.26f),1.2f,true);
            if(ParticlesEnabled&&_sparkles&&i%3==0)
            {
                Vector2 jitter=new(Mathf.Sin(i*8.73f)*4,Mathf.Cos(i*3.17f)*4);
                DrawCircle(a+jitter,0.75f,new Color(_color.Lightened(0.8f),fade*0.7f));
            }
        }
        Vector2 side=_trail.Normalized().Orthogonal()*_radius*0.7f;
        DrawColoredPolygon(new[] { _trail*1.6f, side, -side }, new Color(_color, 0.12f));
        DrawColoredPolygon(new[] { _trail, side*0.65f, -side*0.65f }, new Color(_color, 0.28f));
        DrawCircle(Vector2.Zero, _radius + 3, new Color(_color, 0.10f));
        DrawCircle(Vector2.Zero, _radius, _color.Darkened(0.35f));
        for(int layer=5;layer>0;layer--)
        {
            float u=layer/6f;
            DrawCircle(new Vector2(-_radius*0.12f,-_radius*0.14f),_radius*u,_color.Darkened(0.72f-u*0.25f));
        }
        DrawArc(Vector2.Zero, _radius, 0, Mathf.Tau, 24, _color, 1.5f, true);
        DrawArc(Vector2.Zero,_radius*0.82f,3.65f,5.3f,12,new Color(_color.Lightened(0.65f),0.55f),1,true);
    }
}
