using System;
using Godot;

namespace TerritoryWar;

/// <summary>Fixed logical area, independent of window size and render quality.</summary>
public sealed class TerritoryGrid
{
    public const int Width = 216, Height = 208;
    public const float CellSize = 5;
    public const double CellArea = CellSize * CellSize;
    public static readonly Vector2 Origin = new(760, 20);
    public static readonly Rect2 Bounds = new(Origin, new Vector2(Width * CellSize, Height * CellSize));
    private readonly int[] _paintCells = new int[Width * Height];
    private readonly float[] _paintTimes = new float[Width * Height];
    public readonly byte[] Owners = new byte[Width * Height]; // 0 neutral, 1..4 factions
    // Observation metadata: detects even A -> B -> A changes within one fixed step.
    public readonly long[] ChangedAtRevision = new long[Width * Height];
    public readonly int[] Counts = new int[4];
    public long Revision { get; private set; }
    public void Reset()
    {
        Array.Clear(Owners); Array.Clear(Counts); Array.Clear(ChangedAtRevision); Revision++;
    }
    public void SetOwner(int index, int faction)
    {
        byte next = (byte)(faction + 1), old = Owners[index];
        if (old == next) return;
        if (old != 0) Counts[old - 1]--;
        Owners[index] = next;
        if (next != 0) Counts[faction]++;
        Revision++;
        ChangedAtRevision[index]=Revision;
    }
    public Vector2 CellCenter(int x, int y) => Origin + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
    public void SeedDisk(Vector2 center, float radius, int faction)
    {
        double budget = double.MaxValue;
        PaintDisk(center, radius, faction, 0, ref budget);
    }
    // Row-major ordering makes a partial disk deterministic. Owned cells never cost budget.
    public double PaintDisk(Vector2 center, float radius, int faction, double costPerArea, ref double budget)
    {
        bool blocked = false;
        return PaintDiskCore(center, radius, faction, costPerArea, ref budget, ref blocked);
    }
    private double PaintDiskCore(Vector2 center, float radius, int faction, double costPerArea, ref double budget, ref bool blocked)
    {
        int minX = Math.Max(0, (int)MathF.Floor((center.X - radius - Origin.X) / CellSize));
        int maxX = Math.Min(Width - 1, (int)MathF.Floor((center.X + radius - Origin.X) / CellSize));
        int minY = Math.Max(0, (int)MathF.Floor((center.Y - radius - Origin.Y) / CellSize));
        int maxY = Math.Min(Height - 1, (int)MathF.Floor((center.Y + radius - Origin.Y) / CellSize));
        double cost = costPerArea * CellArea, spent = 0;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int index = y * Width + x;
            if (Owners[index] == faction + 1 || CellCenter(x,y).DistanceSquaredTo(center) > radius * radius) continue;
            if (budget + 1e-12 < cost) { blocked = true; continue; }
            budget = Math.Max(0, budget - cost); spent += cost; SetOwner(index, faction);
        }
        return spent;
    }
    public double PaintSegment(Vector2 from, Vector2 to, float radius, int faction, double costPerArea, ref double budget)
        => PaintSegment(from, to, radius, faction, costPerArea, ref budget, out _);
    /// <summary>True when the swept paint disk first touches a cell not owned by the faction; contact is that point.</summary>
    public bool TryFirstContact(Vector2 from, Vector2 to, float radius, int faction, out Vector2 contact)
    {
        contact = from;
        Vector2 motion = to - from;
        float length = motion.Length();
        if (length < 0.000001f) return DiskTouchesForeign(from, radius, faction);
        Vector2 direction = motion / length;
        int minX = Math.Max(0, (int)MathF.Floor((Math.Min(from.X, to.X) - radius - Origin.X) / CellSize));
        int maxX = Math.Min(Width - 1, (int)MathF.Floor((Math.Max(from.X, to.X) + radius - Origin.X) / CellSize));
        int minY = Math.Max(0, (int)MathF.Floor((Math.Min(from.Y, to.Y) - radius - Origin.Y) / CellSize));
        int maxY = Math.Min(Height - 1, (int)MathF.Floor((Math.Max(from.Y, to.Y) + radius - Origin.Y) / CellSize));
        bool found = false;
        float best = 0;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int index = y * Width + x;
            if (Owners[index] == faction + 1) continue;
            Vector2 delta = CellCenter(x, y) - from;
            float along = delta.Dot(direction);
            float closest = Math.Clamp(along, 0, length);
            if ((delta - direction * closest).LengthSquared() > radius * radius + 0.00001f) continue;
            float perpendicular = delta.Cross(direction);
            float entry = Math.Max(0, along - MathF.Sqrt(Math.Max(0, radius * radius - perpendicular * perpendicular)));
            if (!found || entry < best) { best = entry; found = true; }
        }
        if (found) contact = from + direction * Math.Min(best, length);
        return found;
    }
    private bool DiskTouchesForeign(Vector2 center, float radius, int faction)
    {
        int minX = Math.Max(0, (int)MathF.Floor((center.X - radius - Origin.X) / CellSize));
        int maxX = Math.Min(Width - 1, (int)MathF.Floor((center.X + radius - Origin.X) / CellSize));
        int minY = Math.Max(0, (int)MathF.Floor((center.Y - radius - Origin.Y) / CellSize));
        int maxY = Math.Min(Height - 1, (int)MathF.Floor((center.Y + radius - Origin.Y) / CellSize));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int index = y * Width + x;
            if (Owners[index] == faction + 1) continue;
            if (CellCenter(x, y).DistanceSquaredTo(center) <= radius * radius + 0.00001f) return true;
        }
        return false;
    }
    public double PaintSegment(Vector2 from, Vector2 to, float radius, int faction, double costPerArea, ref double budget, out bool blocked)
    {
        blocked = false;
        // Exact swept disk versus cell centers: no gaps between sampled disks.
        // Order by first contact along the segment so a limited budget paints forward.
        Vector2 motion=to-from;
        float length=motion.Length();
        if(length<0.000001f)return PaintDiskCore(from,radius,faction,costPerArea,ref budget,ref blocked);
        Vector2 direction=motion/length;
        int minX=Math.Max(0,(int)MathF.Floor((Math.Min(from.X,to.X)-radius-Origin.X)/CellSize));
        int maxX=Math.Min(Width-1,(int)MathF.Floor((Math.Max(from.X,to.X)+radius-Origin.X)/CellSize));
        int minY=Math.Max(0,(int)MathF.Floor((Math.Min(from.Y,to.Y)-radius-Origin.Y)/CellSize));
        int maxY=Math.Min(Height-1,(int)MathF.Floor((Math.Max(from.Y,to.Y)+radius-Origin.Y)/CellSize));
        int count=0;
        for(int y=minY;y<=maxY;y++)
        for(int x=minX;x<=maxX;x++)
        {
            int index=y*Width+x;
            if(Owners[index]==faction+1)continue;
            Vector2 delta=CellCenter(x,y)-from;
            float along=delta.Dot(direction);
            float closest=Math.Clamp(along,0,length);
            if((delta-direction*closest).LengthSquared()>radius*radius+0.00001f)continue;
            float perpendicular=delta.Cross(direction);
            float entry=Math.Max(0,along-MathF.Sqrt(Math.Max(0,radius*radius-perpendicular*perpendicular)));
            _paintCells[count]=index;_paintTimes[count++]=entry;
        }
        Array.Sort(_paintTimes,_paintCells,0,count);
        double cost=costPerArea*CellArea,spent=0;
        for(int i=0;i<count;i++)
        {
            if(budget+1e-12<cost){blocked=true;break;}
            budget=Math.Max(0,budget-cost);spent+=cost;
            SetOwner(_paintCells[i],faction);
        }
        return spent;
    }
}
