using Godot;
using System;
using System.Collections.Generic;

namespace TerritoryWar;

internal static class ProjectileTrailGeometry
{
    public static bool Clip(ref Vector2 from,ref Vector2 to,Rect2 bounds)
    {
        Vector2 delta=to-from;float first=0,last=1;
        for(int axis=0;axis<2;axis++)
        {
            float d=delta[axis],p=from[axis],min=bounds.Position[axis],max=bounds.End[axis];
            if(Math.Abs(d)<0.000001f){if(p<min||p>max)return false;continue;}
            float a=(min-p)/d,b=(max-p)/d;
            first=Math.Max(first,Math.Min(a,b));last=Math.Min(last,Math.Max(a,b));
            if(first>=last)return false;
        }
        to=from+delta*last;from+=delta*first;return true;
    }
    // Trace backwards through wall reflections, preserving the tail's length and
    // the incoming leg at a bounce instead of projecting it outside the board.
    public static void Append(List<Vector2> segments, Vector2 position, Vector2 velocity,
        float length, Rect2 bounds)
    {
        if (velocity.LengthSquared() < 0.000001f || length <= 0 ||
            bounds.Size.X <= 0 || bounds.Size.Y <= 0) return;
        Vector2 point = position.Clamp(bounds.Position, bounds.End);
        Vector2 direction = -velocity.Normalized();
        while (length > 0.0001f)
        {
            float tx = direction.X == 0 ? float.PositiveInfinity :
                ((direction.X > 0 ? bounds.End.X : bounds.Position.X) - point.X) / direction.X;
            float ty = direction.Y == 0 ? float.PositiveInfinity :
                ((direction.Y > 0 ? bounds.End.Y : bounds.Position.Y) - point.Y) / direction.Y;
            float distance = Math.Max(0, Math.Min(length, Math.Min(tx, ty)));
            Vector2 end = (point + direction * distance).Clamp(bounds.Position, bounds.End);
            if (distance > 0.0001f)
            {
                segments.Add(point);
                segments.Add(end);
            }
            length -= distance;
            if (length <= 0.0001f) break;
            // Reflect both axes for an exact corner, including zero-distance hits.
            if (tx <= ty) direction.X = -direction.X;
            if (ty <= tx) direction.Y = -direction.Y;
            point = end;
        }
    }
}
