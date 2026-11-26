using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerritoryWar;

public static class ProjectileTrailTests
{
    public static void Run(Action<bool, string> check)
    {
        var bounds = TerritoryGrid.Bounds.Grow(-3);
        var segments = new List<Vector2>();
        Vector2 start = new(bounds.End.X - 2, bounds.GetCenter().Y);
        ProjectileTrailGeometry.Append(segments, start, Vector2.Left, 22, bounds);
        check(segments.Count == 4 && segments[1].IsEqualApprox(new(bounds.End.X, start.Y)) &&
            segments[2] == segments[1] && segments[3].IsEqualApprox(new(bounds.End.X - 20, start.Y)),
            "post-bounce tail retains connected incoming and outgoing legs inside wall");
        segments.Clear();
        ProjectileTrailGeometry.Append(segments, bounds.End, new Vector2(-1, -1), 95, bounds);
        check(segments.Count == 2 && segments[0] == bounds.End &&
            segments[1].X < bounds.End.X && segments[1].Y < bounds.End.Y,
            "exact corner reflects both tail axes without a zero-length segment");
        bool contained = true, correctLength = true;
        foreach (Vector2 point in new[] { bounds.Position, bounds.End,
            new Vector2(bounds.Position.X, bounds.End.Y), new Vector2(bounds.End.X, bounds.Position.Y),
            bounds.GetCenter() })
        for (int angle = 0; angle < 360; angle += 5)
        foreach (float length in new[] { 22f, 32f, 95f })
        {
            segments.Clear();
            ProjectileTrailGeometry.Append(segments, point, Vector2.FromAngle(Mathf.DegToRad(angle)), length, bounds);
            contained &= segments.All(p => p.X >= bounds.Position.X && p.X <= bounds.End.X &&
                p.Y >= bounds.Position.Y && p.Y <= bounds.End.Y);
            float total = 0;
            for (int i = 0; i < segments.Count; i += 2) total += segments[i].DistanceTo(segments[i + 1]);
            correctLength &= Math.Abs(total - length) < 0.01f;
        }
        check(contained, "all weapon tails stay within inset board at every wall and corner");
        check(correctLength, "wall reflections preserve normal, fragment and sniper tail lengths");
        segments.Clear();
        ProjectileTrailGeometry.Append(segments, start, Vector2.Zero, 22, bounds);
        check(segments.Count == 0, "stationary projectiles do not produce invalid tail geometry");
        Vector2 a=new(750,500),b=new(1850,500);
        check(ProjectileTrailGeometry.Clip(ref a,ref b,bounds)&&a.X==bounds.Position.X&&b.X==bounds.End.X,
            "wide effect strokes clip to both board edges");
        a=new(750,10);b=new(1850,10);
        check(!ProjectileTrailGeometry.Clip(ref a,ref b,bounds),"outside sweep and orbital strokes are omitted");
        var battle=new BattleSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0},_=>{});
        battle.SpawnWeapon(0,new Vector2(1200,500),new Vector2(300,0),10000,WeaponKind.Sniper,Ability.Sniper);
        var shot=battle.Projectiles.First(p=>p.Active);
        check(double.IsNegativeInfinity(shot.LastPaintAt),"new sniper has no paint flash before painting");
        battle.Tick();
        check(shot.LastPaintAt>0&&shot.LastPaintAt<=battle.Time+0.00001,"paint flash follows actual paid paint event");
        battle.Reset();battle.Territory.SeedDisk(new Vector2(1200,500),100,0);
        battle.SpawnWeapon(0,new Vector2(1200,500),new Vector2(300,0),10000,WeaponKind.Sniper,Ability.Sniper);
        battle.Tick();
        check(double.IsNegativeInfinity(battle.Projectiles.First(p=>p.Active).LastPaintAt),
            "reused slot clears flash and friendly territory does not trigger painting flash");
    }
}
