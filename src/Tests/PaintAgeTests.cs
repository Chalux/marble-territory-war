using System;
namespace TerritoryWar;

public static class PaintAgeTests
{
    public static void Run(Action<bool,string> check)
    {
        using var age=new VisualPaintAge();var grid=new TerritoryGrid();
        age.Observe(grid,0);
        check(age.PaintedAt(10,0)==VisualPaintAge.Unpainted,"empty territory never glows");
        grid.SetOwner(10,0);age.Observe(grid,1);
        check(age.PaintedAt(10,0)==1&&age.PaintedAt(10,1)==VisualPaintAge.Unpainted,"new paint lights only its owning faction");
        grid.SetOwner(10,0);age.Observe(grid,3);
        check(age.PaintedAt(10,0)==1,"friendly repaint does not restart the fade");
        grid.SetOwner(10,1);age.Observe(grid,4);
        check(age.PaintedAt(10,1)==4&&age.PaintedAt(10,0)==VisualPaintAge.Unpainted,"capture clears old glow and lights new owner");
        grid.SetOwner(10,0);grid.SetOwner(10,1);age.Observe(grid,5);
        check(age.PaintedAt(10,1)==5,"recapture within a single fixed step refreshes the glow");
        age.Observe(grid,5);check(age.PaintedAt(10,1)==5,"paused observation does not change paint time");
        grid.Reset();age.Observe(grid,5);
        check(age.PaintedAt(10,1)==VisualPaintAge.Unpainted,"reset clears paint ages even at unchanged simulation time");
        grid.SetOwner(10,2);age.Observe(grid,0);
        check(age.PaintedAt(10,2)==0&&age.PaintedAt(10,1)==VisualPaintAge.Unpainted,"clock rewind rebuilds paint age without stale highlights");
        age.Observe(new TerritoryGrid(),10);
        check(age.PaintedAt(10,2)==VisualPaintAge.Unpainted,"switching matches clears paint age");
    }
}
