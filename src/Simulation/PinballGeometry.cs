using System;
using Godot;

namespace TerritoryWar;

public readonly record struct BoardSegment(Vector2 Start, Vector2 End);
public readonly record struct MultiplierPocket(Vector2 Center, int Factor);

/// <summary>The visible rails ARE the collision geometry, including pocket partitions and floors.</summary>
public static class PinballGeometry
{
    // Reference-matched slot centers, with the pocket group scaled 1.2x from the measured
    // 30/43/55 reference channels so the ball-flow economy still completes natural matches.
    // Channels: x8 = 36, x4 = 52, x2 = 66 logical units, widening toward the center.
    public const float X8Left = 45, X8Right = 81, X4Left = 123, X4Right = 175, X2Left = 208, X2Right = 274;
    public const float SlotRadius = 25;
    public static readonly MultiplierPocket[] Slots = {
        new(new((X8Left+X8Right)/2,556),8), new(new((X4Left+X4Right)/2,586),4), new(new((X2Left+X2Right)/2,616),2),
        new(new(680-(X2Left+X2Right)/2,616),2), new(new(680-(X4Left+X4Right)/2,586),4), new(new(680-(X8Left+X8Right)/2,556),8)
    };
    // Solid contour around the three left channels; right side is mirrored across x = 340.
    public static readonly Vector2[] LeftSolid = {
        new(30,485),new(X8Left,485),new(X8Left,582),new(X8Right,582),new(X8Right,495),
        new(X4Left,511),new(X4Left,612),new(X4Right,612),new(X4Right,525),
        new(X2Left,539),new(X2Left,642),new(X2Right,642),new(X2Right,557),
        new(X2Right+7,557),new(X2Right+7,642),new(300,659),new(286,697),new(38,790),new(30,806)
    };
    public static readonly Vector2[] RightSolid = Array.ConvertAll(LeftSolid,p=>new Vector2(680-p.X,p.Y));
    // Baffles sit inside each pocket, hidden below the multiplier ring at rest.
    public static int ChannelDepth(int slot) => slot <= 2 ? slot : 5 - slot;
    public static float ChannelLeft(int slot) => slot switch { 0 => X8Left, 1 => X4Left, 2 => X2Left, 3 => 680 - X2Right, 4 => 680 - X4Right, _ => 680 - X8Right };
    public static float ChannelRight(int slot) => slot switch { 0 => X8Right, 1 => X4Right, 2 => X2Right, 3 => 680 - X2Left, 4 => 680 - X4Left, _ => 680 - X8Left };
    public static float BaffleRestY(int slot) => 582 + ChannelDepth(slot) * 30;
    public static float BaffleLiftY(int slot) => 470 + ChannelDepth(slot) * 27;
    public static readonly BoardSegment[] Walls = BuildWalls();
    private static BoardSegment[] BuildWalls()
    {
        var walls = new System.Collections.Generic.List<BoardSegment> {
            new(new(30,34),new(650,34)),new(new(30,34),new(30,976)),
            new(new(650,34),new(650,976)),new(new(30,976),new(650,976))
        };
        // Full-width separators are part of the visible and collidable solid contour.
        for(int i=0;i<LeftSolid.Length-1;i++)
        {
            var segment=new BoardSegment(LeftSolid[i],LeftSolid[i+1]);
            walls.Add(segment);
            walls.Add(new(new(680-segment.Start.X,segment.Start.Y),new(680-segment.End.X,segment.End.Y)));
        }
        walls.Add(new(new(286,566),new(300,659)));
        walls.Add(new(new(394,566),new(380,659)));
        return walls.ToArray();
    }
    public static float CircleSweep(Vector2 start, Vector2 motion, Vector2 center, float radius, out Vector2 normal)
    {
        normal=Vector2.Zero;
        Vector2 offset=start-center;
        float a=motion.LengthSquared(), b=offset.Dot(motion), c=offset.LengthSquared()-radius*radius;
        if(c<=0)
        {
            normal=offset.LengthSquared()>1e-8f?offset.Normalized():-motion.Normalized();
            return b<0?-0f:float.PositiveInfinity;
        }
        if(a<1e-12f||b>=0)return float.PositiveInfinity;
        float discriminant=b*b-a*c;
        if(discriminant<0)return float.PositiveInfinity;
        float t=(-b-MathF.Sqrt(discriminant))/a;
        if(t<0||t>1)return float.PositiveInfinity;
        normal=(offset+motion*t).Normalized();return t;
    }
    public static float SegmentSweep(Vector2 start, Vector2 motion, float radius, BoardSegment wall, out Vector2 normal)
    {
        Vector2 tangent=(wall.End-wall.Start).Normalized();
        Vector2 n=new(-tangent.Y,tangent.X);
        float length=wall.Start.DistanceTo(wall.End),distance=(start-wall.Start).Dot(n),speed=motion.Dot(n);
        float best=float.PositiveInfinity;normal=Vector2.Zero;
        for(int sign=-1;sign<=1;sign+=2)
        {
            if(speed*sign>=-1e-8f)continue;
            float t=(sign*radius-distance)/speed;
            // Contact at the beginning is valid; it must not be ignored because of roundoff.
            if(t< -0.00001f||t>1)continue;
            t=Math.Max(0,t);
            float along=(start+motion*t-wall.Start).Dot(tangent);
            if(along<0||along>length||t>=best)continue;
            best=t;normal=n*sign;
        }
        float first=CircleSweep(start,motion,wall.Start,radius,out Vector2 firstNormal);
        if(first<best){best=first;normal=firstNormal;}
        float last=CircleSweep(start,motion,wall.End,radius,out Vector2 lastNormal);
        if(last<best){best=last;normal=lastNormal;}
        return best;
    }
    public static float StaticSweep(Vector2 start, Vector2 motion, float radius, out Vector2 normal, out bool bumper)
    {
        float best=float.PositiveInfinity;normal=Vector2.Zero;bumper=false;
        foreach(var wall in Walls)
        {
            float t=SegmentSweep(start,motion,radius,wall,out Vector2 n);
            if(t<best){best=t;normal=n;bumper=false;}
        }
        foreach(var peg in PinballSimulation.Pegs)
        {
            float t=CircleSweep(start,motion,peg,radius+10,out Vector2 n);
            if(t<best){best=t;normal=n;bumper=true;}
        }
        return best;
    }
    public static float SlotSweep(Vector2 start, Vector2 motion, float radius, out int slot)
    {
        float best=float.PositiveInfinity;slot=-1;
        for(int i=0;i<Slots.Length;i++)
        {
            float t=start.DistanceSquaredTo(Slots[i].Center)<=(radius+SlotRadius)*(radius+SlotRadius)
                ?0:CircleSweep(start,motion,Slots[i].Center,radius+SlotRadius,out _);
            if(t<best){best=t;slot=i;}
        }
        return best;
    }
}
