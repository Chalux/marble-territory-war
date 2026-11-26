using Godot;
namespace TerritoryWar;

// Drawn after the shield shell, alongside the reserve labels.
public partial class FactionCoreView : Node2D
{
    public Color Tint;
    public float Angle;
    public bool Alive;
    public override void _Draw()
    {
        if(!Alive)
        {
            DrawLine(new(-8,-8),new(8,8),new Color(Tint,0.3f),2);
            DrawLine(new(-8,8),new(8,-8),new Color(Tint,0.3f),2);
            return;
        }
        VisualGlyphs.Draw(this,new(-53,-22),2,Tint.Lightened(0.6f),1.1f);
        VisualGlyphs.Draw(this,new(-66,28),1,Tint.Lightened(0.6f),1.05f);
        Vector2 direction=Vector2.FromAngle(Angle);
        DrawLine(Vector2.Zero,direction*27,new Color(Tint,0.25f),8,true);
        DrawLine(Vector2.Zero,direction*27,Tint.Lightened(0.6f),2,true);
        DrawCircle(Vector2.Zero,11,Tint.Lightened(0.7f));
        DrawCircle(Vector2.Zero,7,Tint);
        DrawCircle(new(-2,-2),2.5f,Colors.White);
    }
}
