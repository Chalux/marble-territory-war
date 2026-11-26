using System;
using System.Linq;
using Godot;

namespace TerritoryWar;

public static class TerritoryContourTests
{
    public static void Run(Action<bool, string> check)
    {
        const int w = TerritoryGrid.Width, h = TerritoryGrid.Height;
        var owners = new byte[w * h];
        var pixels = new byte[owners.Length * 4];
        var contour = new VisualTerritoryContour();
        contour.WritePixels(owners, pixels);
        check(pixels.All(p => p == 255), "empty territory has no contour remnants");
        Array.Fill(owners, (byte)2);
        contour.WritePixels(owners, pixels);
        check(Enumerable.Range(0, owners.Length).All(i => pixels[i * 4 + 1] == 0
            && pixels[i * 4] == 255 && pixels[i * 4 + 2] == 255 && pixels[i * 4 + 3] == 255),
            "full territory including its screen edges keeps a single faction");

        Array.Clear(owners);
        int isolated = 60 * w + 80;
        owners[isolated] = 1;
        contour.WritePixels(owners, pixels);
        check(pixels[isolated * 4] < 128, "one-cell splat remains visible");
        check(pixels[(isolated + 12) * 4] == 255, "smoothing has bounded reach into empty space");

        Array.Clear(owners);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (x >= 70 + y * 0.15 && x < 73 + y * 0.15) owners[y * w + x] = 1;
        var original = (byte[])owners.Clone();
        contour.WritePixels(owners, pixels);
        check(owners.SequenceEqual(original), "visual reconstruction does not change logical paint or area");

        double sum = 0, sum2 = 0, rawSum = 0, rawSum2 = 0;
        int samples = 0;
        for (int y = 20; y < h - 20; y++)
        {
            double ideal = 70 + y * 0.15;
            double raw = Math.Ceiling(ideal) - 0.5 - ideal;
            for (int x = 60; x < 110; x++)
            {
                float a = pixels[(y * w + x) * 4] - 127.5f;
                float b = pixels[(y * w + x + 1) * 4] - 127.5f;
                if (a <= 0 || b > 0) continue;
                double error = x + a / (a - b) - ideal;
                sum += error; sum2 += error * error; rawSum += raw; rawSum2 += raw * raw; samples++;
                break;
            }
        }
        double smoothRms = Math.Sqrt(sum2 / samples - Math.Pow(sum / samples, 2));
        double rawRms = Math.Sqrt(rawSum2 / samples - Math.Pow(rawSum / samples, 2));
        check(samples == h - 40, "thin oblique stroke remains continuous along every row");
        check(smoothRms < rawRms * 0.75, "shallow-angle stroke staircase deviation falls by at least 25 percent");
        GD.Print($"CONTOUR: shallow-angle edge deviation {rawRms * 5:F3}px -> {smoothRms * 5:F3}px");

        var first = (byte[])pixels.Clone();
        contour.WritePixels(owners, pixels);
        check(first.SequenceEqual(pixels), "contour reconstruction is deterministic");
        for (int i = 0; i < owners.Length; i++) if (owners[i] == 1) owners[i] = 4;
        contour.WritePixels(owners, pixels);
        check(Enumerable.Range(0, owners.Length).All(i => first[i * 4] == pixels[i * 4 + 3]),
            "all four factions use the same contour geometry");
        Array.Clear(owners);
        contour.WritePixels(owners, pixels);
        check(pixels.All(p => p == 255), "reset clears old paint contours");
        var coverage=new byte[w*h];
        contour.WriteCoverage(owners,coverage);
        check(coverage.All(p=>p==255),"empty union coverage stays transparent");
        // Two-colour slants and a four-colour junction have no exterior boundary.
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            owners[y*w+x]=(byte)((x<w/2+y*0.12?1:2)+(y<h/2?0:2));
        contour.WriteCoverage(owners,coverage);
        check(coverage.All(p=>p==0),"internal two- and four-colour borders remain fully opaque");
        var beforeCoverage=(byte[])coverage.Clone();
        for(int i=0;i<owners.Length;i++)owners[i]=(byte)(owners[i]%4+1);
        contour.WriteCoverage(owners,coverage);
        check(coverage.SequenceEqual(beforeCoverage),"changing faction colours cannot change territory opacity");
        // Preserve a genuine neutral channel between two occupied regions.
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            owners[y*w+x]=(byte)(x<100?1:x<104?0:2);
        contour.WriteCoverage(owners,coverage);
        check(coverage[(h/2)*w+101]>127&&coverage[(h/2)*w+98]<128&&coverage[(h/2)*w+105]<128,
            "real neutral gaps remain transparent between touching-looking territories");
        Array.Clear(owners);contour.WriteCoverage(owners,coverage);
        check(coverage.All(p=>p==255),"coverage clears on reset without stale opaque territory");

        // Compare every output byte against a whole-board reconstruction, including tile seams.
        var tiled = new byte[pixels.Length];
        var tiledCoverage = new byte[coverage.Length];
        void Compare(string step)
        {
            contour.Update(owners, tiled, tiledCoverage);
            contour.WritePixels(owners, pixels);
            contour.WriteCoverage(owners, coverage);
            check(tiled.SequenceEqual(pixels) && tiledCoverage.SequenceEqual(coverage), step);
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            owners[y*w+x] = (byte)((x/33 + y/31 + (x+y)/47) % 5);
        Compare("tiled distances match full reconstruction at seams and board edges");
        var random = new Random(821);
        for (int i = 0; i < 100; i++) owners[random.Next(owners.Length)] = (byte)random.Next(5);
        Compare("sparse gains, losses and recolouring invalidate neighbouring tiles");
        tiled = new byte[pixels.Length];
        Compare("replacing a texture buffer repopulates every tile");
        Array.Clear(owners);
        Compare("reset clears all cached tile channels");
    }
}
