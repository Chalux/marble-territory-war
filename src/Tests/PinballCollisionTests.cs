using System;
using Godot;

namespace TerritoryWar;

public static class PinballCollisionTests
{
    private static PinballSimulation Empty(SimulationConfig config=null)
    {
        var sim=new PinballSimulation(config??new SimulationConfig {Gravity=0,LiftForce=0,BumperKick=0});
        foreach(var b in sim.Balls)b.Phase=BallPhase.Inactive;
        return sim;
    }
    private static BallState Place(PinballSimulation sim,int id,Vector2 position,Vector2 velocity,double value=1)
    {
        var b=sim.Balls[id];b.Phase=BallPhase.Flying;b.Position=b.Previous=position;b.Velocity=velocity;b.Value=value;
        sim.UpdateSize(b);return b;
    }
    public static void Run(Action<bool,string> check)
    {
        check(PinballGeometry.X8Right-PinballGeometry.X8Left < PinballGeometry.X4Right-PinballGeometry.X4Left && PinballGeometry.X4Right-PinballGeometry.X4Left < PinballGeometry.X2Right-PinballGeometry.X2Left,"multiplier entrance widths increase from x8 to x4 to x2");
        var sim=Empty();var ball=Place(sim,0,new(72,520),new(600,0));sim.Tick();
        check(ball.Position.X+ball.Radius<=PinballGeometry.X8Right+0.01f&&ball.Velocity.X<0,"multiplier partition is solid, not a decorative line");
        sim=Empty();ball=Place(sim,0,new(37.5f,470),new(0,600));ball.Lower=true;
        for(int i=0;i<6;i++)sim.Tick();
        check(ball.Phase==BallPhase.Flying&&ball.Position.Y+ball.Radius<=486.01f,"outer pocket ledge is solid");
        sim=Empty();ball=Place(sim,0,new(134,520),new(-600,0));sim.Tick();
        check(ball.Position.X-ball.Radius>=PinballGeometry.X4Left-0.01f&&ball.Velocity.X>0,"wide separator blocks its opposite face");
        float cap=PinballGeometry.StaticSweep(new(100,480),new(0,35),3,out Vector2 capNormal,out _);
        check(cap<1&&capNormal.Y<0,"sloping divider top blocks a falling ball");
        for(int i=0;i<6;i++)
        {
            sim=Empty();
            for(int k=0;k<240;k++)sim.Tick(); // start while every baffle is at rest
            var pocket=PinballGeometry.Slots[i];
            ball=Place(sim,0,pocket.Center+new Vector2(0,-40),new(0,600));ball.Lower=true;
            for(int k=0;k<240&&ball.Phase==BallPhase.Flying;k++)sim.Tick();
            check(ball.Phase==BallPhase.Waiting&&ball.Value==pocket.Factor&&sim.MultiplierHits==1,"each physical slot catches a returning lower-board ball once");
        }
        sim=Empty();
        float baffleRest=PinballGeometry.BaffleRestY(5);
        check(Math.Abs(sim.BaffleY(5)-baffleRest)<0.5f,"baffle rests below its multiplier slot");
        for(int i=0;i<120*6+90;i++)sim.Tick();
        check(sim.BaffleY(5)<baffleRest-100,"baffle rises periodically above the slot");
        sim=Empty();ball=Place(sim,0,new(241,515),Vector2.Zero,1e11);
        for(int i=0;i<156;i++)sim.Tick();
        check(ball.Phase==BallPhase.Flying&&(ball.Position.Y<510||ball.Position.X<208),"lifted baffle ejects an oversized ball from the pocket");
        sim=Empty();ball=Place(sim,0,new(37.01f,100),Vector2.Zero);
        Place(sim,1,new(46,100),Vector2.Zero);sim.Tick();
        check(ball.Position.X-ball.Radius>=29.99f,"ball-pair separation cannot push a ball through the outer wall");
        sim=Empty();
        Vector2 wallPoint=new(286-248f*23/93,720),inward=new Vector2(93,248).Normalized();
        ball=Place(sim,0,wallPoint+inward*7.01f,Vector2.Zero);
        Place(sim,1,ball.Position+inward*9,Vector2.Zero);sim.Tick();
        check((ball.Position-wallPoint).Dot(inward)>=ball.Radius-0.01f,"pair separation respects diagonal funnel walls");
        float t=PinballGeometry.SegmentSweep(new(150,500),new(0,300),3,new(new(108,612),new(200,612)),out Vector2 normal);
        check(Math.Abs(t-109f/300)<0.00001f&&normal.Y<0,"continuous sweep stops high-speed small balls before crossing a floor");
        t=PinballGeometry.SegmentSweep(new(108,490),new(0,50),3,new(new(108,510),new(108,612)),out normal);
        check(Math.Abs(t-17f/50)<0.00001f&&normal.Y<0,"partition endpoints collide, including exact centerline approaches");
        sim=Empty();ball=Place(sim,0,new(340,664),new(0,300));sim.Tick();
        check(ball.Lower&&ball.Position.Y>665,"central opening remains physically traversable");
        var standard=new SimulationConfig();sim=Empty(standard);ball=Place(sim,0,new(340,540),new(0,10));sim.Tick();
        float lightVelocity=ball.Velocity.Y;
        sim=Empty(standard);ball=Place(sim,0,new(340,540),new(0,10),64);sim.Tick();
        check(lightVelocity<10&&ball.Velocity.Y>10,"central lift remains a mass-dependent force, not a hard gate");
        sim=Empty(standard);ball=Place(sim,0,new(340,640),new(0,700));
        for(int i=0;i<10;i++)sim.Tick();
        check(ball.Lower,"fast light balls can still cross the lift region by inertia");
        sim=Empty();ball=Place(sim,0,new(340,965),new(0,600));sim.Tick();
        check(sim.AbilityHits==1&&ball.Phase==BallPhase.Waiting,"bottom conveyor catches swept contact exactly once");

        sim=new PinballSimulation(new SimulationConfig());
        for(int i=0;i<40;i++)sim.AwardBall(i%4);
        bool valid=true;
        for(int step=0;step<120*90;step++)
        {
            sim.Tick();
            foreach(var b in sim.Balls)
            {
                if(b.Phase!=BallPhase.Flying)continue;
                if(Inside(b.Position,PinballGeometry.LeftSolid)||Inside(b.Position,PinballGeometry.RightSolid)
                   ||b.Position.X-b.Radius<29.98f||b.Position.X+b.Radius>650.02f||b.Position.Y+b.Radius>976.02f)
                {
                    if(valid)GD.Print($"CROWD ESCAPE step={step} ball={b.Id} value={b.Value} radius={b.Radius} position={b.Position} velocity={b.Velocity}");
                    valid=false;
                }
            }
        }
        check(valid,"52-ball 90-second crowding run never enters side solids or leaves the board");
    }
    private static bool Inside(Vector2 p,Vector2[] polygon)
    {
        bool inside=false;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        {
            var a=polygon[i];var b=polygon[j];
            if((a.Y>p.Y)!=(b.Y>p.Y)&&p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
        }
        return inside;
    }
}

