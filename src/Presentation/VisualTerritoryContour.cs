using System;
using System.Threading.Tasks;

namespace TerritoryWar;

// A filtered signed-distance field for display only. Logical ownership stays untouched.
// Four channels store distance in cells, negative inside the corresponding faction.
public sealed class VisualTerritoryContour
{
    private const int Width = TerritoryGrid.Width, Height = TerritoryGrid.Height;
    private const float Range = 4f, Diagonal = 1.41421356f;
    private const int TileSize = 32;
    // Five cells of capped distance, one smoothing cell, and up to five cells of tangent sampling.
    private const int Halo = 12;
    private const int Columns = (Width + TileSize - 1) / TileSize;
    private readonly Tile[] _tiles = new Tile[Columns * ((Height + TileSize - 1) / TileSize)];
    private byte[] _lastPixels, _lastCoverage;

    public VisualTerritoryContour()
    {
        for (int i = 0; i < _tiles.Length; i++)
            _tiles[i] = new Tile(i % Columns * TileSize, i / Columns * TileSize);
    }

    public void Update(byte[] owners, byte[] pixels, byte[] coverage)
    {
        bool initialize = pixels != _lastPixels || coverage != _lastCoverage;
        Parallel.For(0, _tiles.Length, i => _tiles[i].Update(owners, pixels, coverage, initialize));
        _lastPixels = pixels;
        _lastCoverage = coverage;
    }

    private sealed class Tile
    {
        private readonly int _left, _top, _width, _height;
        private readonly int _sourceX, _sourceY, _stride;
        private readonly byte[] _owners;
        private readonly Channel _channel;

        public Tile(int left, int top)
        {
            _left = left;
            _top = top;
            _width = Math.Min(TileSize, Width - left);
            _height = Math.Min(TileSize, Height - top);
            _sourceX = Math.Max(0, left - Halo);
            _sourceY = Math.Max(0, top - Halo);
            _stride = Math.Min(Width, left + _width + Halo) - _sourceX;
            int rows = Math.Min(Height, top + _height + Halo) - _sourceY;
            _owners = new byte[_stride * rows];
            _channel = new Channel(_stride, rows);
        }

        public void Update(byte[] owners, byte[] pixels, byte[] coverage, bool initialize)
        {
            int dirty = initialize ? 31 : 0;
            for (int row = 0; row < _owners.Length; row += _stride)
            {
                var current = owners.AsSpan((_sourceY + row / _stride) * Width + _sourceX, _stride);
                var previous = _owners.AsSpan(row, _stride);
                if (current.SequenceEqual(previous)) continue;
                for (int x = 0; x < _stride; x++)
                {
                    if (current[x] != previous[x])
                        dirty |= (1 << current[x]) | (1 << previous[x]);
                }
                current.CopyTo(previous);
            }
            bool updateCoverage = (dirty & 1) != 0;
            for (int f = 1; f <= 4; f++)
            {
                if ((dirty & (1 << f)) == 0) continue;
                _channel.Write(_owners, pixels, f,
                    _left - _sourceX, _top - _sourceY, _width, _height, _left, _top);
                // At an isolated faction's frontier the union has exactly the same contour.
                if (updateCoverage && _owners.AsSpan().IndexOfAnyExcept((byte)0, (byte)f) < 0)
                {
                    for (int y = _top; y < _top + _height; y++)
                        for (int cell = y * Width + _left; cell < y * Width + _left + _width; cell++)
                            coverage[cell] = pixels[cell * 4 + f - 1];
                    updateCoverage = false;
                }
            }
            if (updateCoverage)
                _channel.Write(_owners, coverage, 0,
                    _left - _sourceX, _top - _sourceY, _width, _height, _left, _top);
        }
    }

    // Alpha follows the union of all territory, never an individual faction's edge.
    public void WriteCoverage(byte[] owners,byte[] pixels)
    {
        if(owners.Length!=Width*Height||pixels.Length!=owners.Length)
            throw new ArgumentException("Coverage dimensions must match the logical territory grid.");
        new Channel(Width, Height).Write(owners,pixels,0,0,0,Width,Height,0,0);
    }

    public void WritePixels(byte[] owners, byte[] pixels)
    {
        if (owners.Length != Width * Height || pixels.Length != owners.Length * 4)
            throw new ArgumentException("Contour dimensions must match the logical territory grid.");
        var channel = new Channel(Width, Height);
        for (int f = 1; f <= 4; f++)
            channel.Write(owners, pixels, f, 0, 0, Width, Height, 0, 0);
    }

    private sealed class Channel(int width, int height)
    {
        private readonly float[] _inside = new float[width * height];
        private readonly float[] _outside = new float[width * height];
        private readonly float[] _signed = new float[width * height];
        private readonly float[] _horizontal = new float[width * height];
        private readonly float[] _smoothed = new float[width * height];

        public void Write(byte[] owners, byte[] pixels, int faction,
            int startX, int startY, int columns, int rows, int destX, int destY)
        {
            var cells = owners.AsSpan();
            bool empty = faction == 0 ? cells.IndexOfAnyExcept((byte)0) < 0 : cells.IndexOf((byte)faction) < 0;
            bool full = faction == 0 ? cells.IndexOf((byte)0) < 0 : cells.IndexOfAnyExcept((byte)faction) < 0;
            if (empty || full)
            {
                byte value = empty ? (byte)255 : (byte)0;
                for (int y = destY; y < destY + rows; y++)
                {
                    int cell = y * Width + destX;
                    if (faction == 0) pixels.AsSpan(cell, columns).Fill(value);
                    else for (int x = 0; x < columns; x++) pixels[(cell + x) * 4 + faction - 1] = value;
                }
                return;
            }
            for (int i = 0; i < owners.Length; i++)
            {
                bool owned = faction==0 ? owners[i]!=0 : owners[i]==faction;
                _inside[i] = owned ? Range + 1 : 0;
                _outside[i] = owned ? 0 : Range + 1;
            }
            DistanceTransform(_inside);
            DistanceTransform(_outside);
            for (int i = 0; i < owners.Length; i++)
                _signed[i] = (faction==0 ? owners[i]!=0 : owners[i]==faction) ? 0.5f - _inside[i] : _outside[i] - 0.5f;

            // Smooth geometry rather than blurring the final colour. This removes
            // grid-aligned corners before the shader reconstructs the zero contour.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                _horizontal[i] = _signed[y * width + Math.Max(x - 1, 0)] * 0.2f
                    + _signed[i] * 0.6f + _signed[y * width + Math.Min(x + 1, width - 1)] * 0.2f;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                _smoothed[i] = _horizontal[Math.Max(y - 1, 0) * width + x] * 0.2f
                    + _horizontal[i] * 0.6f + _horizontal[Math.Min(y + 1, height - 1) * width + x] * 0.2f;
            }
            for (int y = startY; y < startY + rows; y++)
            for (int x = startX; x < startX + columns; x++)
            {
                int i = y * width + x;
                float distance = _smoothed[i];
                if (Math.Abs(distance) < 2.5f)
                    distance = SmoothTangent(x, y, distance);
                // Preserve earned one-cell splats and narrow paths. Do not clamp
                // every exterior cell positive: that would reintroduce the grid's
                // staircase after smoothing. Only the drawn contour can move.
                if (faction==0 ? owners[i]!=0 : owners[i]==faction) distance = Math.Min(distance, -0.08f);
                int cell = (destY + y - startY) * Width + destX + x - startX;
                int output = faction == 0 ? cell : cell * 4 + faction - 1;
                pixels[output] = (byte)Math.Clamp((int)MathF.Round((distance / Range + 1) * 127.5f), 0, 255);
            }
        }

        private float SmoothTangent(int x, int y, float distance)
        {
            // Structure tensor: opposite sides of a thin stroke reinforce the same
            // tangent. A plain average of normals would cancel them out.
            float xx = 0, xy = 0, yy = 0;
            for (int oy = -2; oy <= 2; oy += 2)
            for (int ox = -2; ox <= 2; ox += 2)
            {
                int sx = Math.Clamp(x + ox, 1, width - 2), sy = Math.Clamp(y + oy, 1, height - 2);
                int i = sy * width + sx;
                float gx = _signed[i + 1] - _signed[i - 1], gy = _signed[i + width] - _signed[i - width];
                xx += gx * gx; xy += gx * gy; yy += gy * gy;
            }
            float coherence = MathF.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy) / (xx + yy + 0.0001f);
            float strength = Math.Clamp((coherence - 0.3f) * 2, 0, 1);
            if (strength <= 0) return distance;
            float angle = 0.5f * MathF.Atan2(2 * xy, xx - yy);
            float tx = -MathF.Sin(angle) * 2.5f, ty = MathF.Cos(angle) * 2.5f;
            // Smooth along the edge over several cells, not across its width. This
            // removes the long staircase runs on nearly horizontal/vertical shots.
            float filtered = distance * 0.375f
                + (Sample(x + tx, y + ty) + Sample(x - tx, y - ty)) * 0.25f
                + (Sample(x + tx * 2, y + ty * 2) + Sample(x - tx * 2, y - ty * 2)) * 0.0625f;
            return distance + (filtered - distance) * strength;
        }

        private float Sample(float x, float y)
        {
            x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1);
            int ix = (int)x, iy = (int)y, nx = Math.Min(ix + 1, width - 1), ny = Math.Min(iy + 1, height - 1);
            float fx = x - ix, fy = y - iy;
            float a = _smoothed[iy * width + ix] * (1 - fx) + _smoothed[iy * width + nx] * fx;
            float b = _smoothed[ny * width + ix] * (1 - fx) + _smoothed[ny * width + nx] * fx;
            return a * (1 - fy) + b * fy;
        }

        private void DistanceTransform(float[] distance)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float d = distance[i];
                if (d == 0) continue;
                if (x > 0) d = Math.Min(d, distance[i - 1] + 1);
                if (y > 0)
                {
                    d = Math.Min(d, distance[i - width] + 1);
                    if (x > 0) d = Math.Min(d, distance[i - width - 1] + Diagonal);
                    if (x + 1 < width) d = Math.Min(d, distance[i - width + 1] + Diagonal);
                }
                distance[i] = d;
            }
            for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                float d = distance[i];
                if (d <= 1) continue;
                if (x + 1 < width) d = Math.Min(d, distance[i + 1] + 1);
                if (y + 1 < height)
                {
                    d = Math.Min(d, distance[i + width] + 1);
                    if (x > 0) d = Math.Min(d, distance[i + width - 1] + Diagonal);
                    if (x + 1 < width) d = Math.Min(d, distance[i + width + 1] + Diagonal);
                }
                distance[i] = d;
            }
        }
    }
}
