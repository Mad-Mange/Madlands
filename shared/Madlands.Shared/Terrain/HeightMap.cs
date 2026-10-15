using System;
using System.IO;
using System.Text;

namespace Madlands.Shared.Terrain
{
    /// <summary>
    /// Regular grid of ground heights for one map, exported from the Unity terrain
    /// (Madlands > Export Map) and used by the server for movement validation.
    /// Sample (i, j) lies at world (OriginX + i * CellSize, OriginZ + j * CellSize).
    ///
    /// File format (little-endian): "MLHM" | ushort version | float originX | float originZ |
    /// float cellSize | int width | int depth | float[width * depth] heights, row-major by j.
    /// </summary>
    public sealed class HeightMap
    {
        public const string FileMagic = "MLHM";
        public const ushort FormatVersion = 1;

        private readonly float[] _heights;

        public HeightMap(float originX, float originZ, float cellSize, int width, int depth, float[] heights)
        {
            if (width < 2 || depth < 2) throw new ArgumentException("Height map must be at least 2x2.");
            if (cellSize <= 0f) throw new ArgumentException("Cell size must be positive.");
            if (heights == null || heights.Length != width * depth)
                throw new ArgumentException("Expected " + (width * depth) + " heights.");
            OriginX = originX;
            OriginZ = originZ;
            CellSize = cellSize;
            Width = width;
            Depth = depth;
            _heights = heights;
        }

        public float OriginX { get; }
        public float OriginZ { get; }
        public float CellSize { get; }
        public int Width { get; }
        public int Depth { get; }
        public float MaxX => OriginX + (Width - 1) * CellSize;
        public float MaxZ => OriginZ + (Depth - 1) * CellSize;

        public static HeightMap CreateFlat(float originX, float originZ, float cellSize, int width, int depth, float height)
        {
            var heights = new float[width * depth];
            for (int i = 0; i < heights.Length; i++) heights[i] = height;
            return new HeightMap(originX, originZ, cellSize, width, depth, heights);
        }

        public static HeightMap Create(float originX, float originZ, float cellSize, int width, int depth, Func<float, float, float> heightAt)
        {
            var heights = new float[width * depth];
            for (int j = 0; j < depth; j++)
                for (int i = 0; i < width; i++)
                    heights[j * width + i] = heightAt(originX + i * cellSize, originZ + j * cellSize);
            return new HeightMap(originX, originZ, cellSize, width, depth, heights);
        }

        public float GetSample(int i, int j) => _heights[j * Width + i];

        public bool Contains(float x, float z) => x >= OriginX && x <= MaxX && z >= OriginZ && z <= MaxZ;

        /// <summary>Bilinearly interpolated ground height; positions outside are clamped to the edge.</summary>
        public float GetHeight(float x, float z)
        {
            float fx = MathUtil.Clamp((x - OriginX) / CellSize, 0f, Width - 1);
            float fz = MathUtil.Clamp((z - OriginZ) / CellSize, 0f, Depth - 1);
            int i0 = Math.Min((int)fx, Width - 2);
            int j0 = Math.Min((int)fz, Depth - 2);
            float tx = fx - i0;
            float tz = fz - j0;
            float h00 = GetSample(i0, j0);
            float h10 = GetSample(i0 + 1, j0);
            float h01 = GetSample(i0, j0 + 1);
            float h11 = GetSample(i0 + 1, j0 + 1);
            float a = h00 + (h10 - h00) * tx;
            float b = h01 + (h11 - h01) * tx;
            return a + (b - a) * tz;
        }

        public void Write(Stream stream)
        {
            using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                w.Write(Encoding.ASCII.GetBytes(FileMagic));
                w.Write(FormatVersion);
                w.Write(OriginX);
                w.Write(OriginZ);
                w.Write(CellSize);
                w.Write(Width);
                w.Write(Depth);
                for (int k = 0; k < _heights.Length; k++) w.Write(_heights[k]);
            }
        }

        public static HeightMap Read(Stream stream)
        {
            using (var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
            {
                string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != FileMagic) throw new InvalidDataException("Not a Madlands height map (bad magic).");
                ushort version = r.ReadUInt16();
                if (version != FormatVersion) throw new InvalidDataException("Unsupported height map version " + version + ".");
                float originX = r.ReadSingle();
                float originZ = r.ReadSingle();
                float cellSize = r.ReadSingle();
                int width = r.ReadInt32();
                int depth = r.ReadInt32();
                if (width < 2 || depth < 2 || (long)width * depth > 64L * 1024 * 1024)
                    throw new InvalidDataException("Invalid height map size " + width + "x" + depth + ".");
                var heights = new float[width * depth];
                for (int k = 0; k < heights.Length; k++) heights[k] = r.ReadSingle();
                return new HeightMap(originX, originZ, cellSize, width, depth, heights);
            }
        }

        public static HeightMap Load(string path)
        {
            using (var fs = File.OpenRead(path)) return Read(fs);
        }

        public void Save(string path)
        {
            using (var fs = File.Create(path)) Write(fs);
        }
    }
}
