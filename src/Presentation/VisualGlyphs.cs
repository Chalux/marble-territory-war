using Godot;
namespace TerritoryWar;

// Shared vector symbols stay crisp at every viewport scale.
public static class VisualGlyphs
{
    public static void Draw(Node2D canvas, Vector2 center, int ability, Color color, float scale=1)
    {
        Vector2 P(float x,float y)=>center+new Vector2(x,y)*scale;
        void L(float x,float y,float u,float v)=>canvas.DrawLine(P(x,y),P(u,v),color,1.4f*scale,true);
        if(ability==0)
        {
            for(int i=0;i<3;i++)canvas.DrawArc(center+Vector2.FromAngle(i*Mathf.Tau/3)*2*scale,6*scale,i*Mathf.Tau/3,i*Mathf.Tau/3+3.5f,16,color,scale,true);
        }
        else if(ability==1)
        {
            L(-10,-3,9,-3);L(-8,0,7,0);L(-7,0,-9,5);L(-2,0,-3,5);L(2,0,4,4);L(6,-5,6,2);L(-5,-5,1,-5);
        }
        else if(ability==2)
        {
            canvas.DrawPolyline(new[]{P(0,-8),P(7,-5),P(6,2),P(0,8),P(-6,2),P(-7,-5),P(0,-8)},color,1.4f*scale,true);
            L(-3,-1,0,2);L(0,2,4,-3);
        }
        else if(ability==3)
        {
            canvas.DrawArc(center,6*scale,0,Mathf.Tau,24,color,1.3f*scale,true);L(-10,3,-6,3);L(5,-5,9,-7);
        }
        else
        {
            canvas.DrawArc(center,6*scale,0,Mathf.Tau,24,color,scale,true);L(-10,0,-3,0);L(3,0,10,0);L(0,-10,0,-3);L(0,3,0,10);
        }
    }
}
