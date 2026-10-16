using System;
using System.IO;
using System.Text;

namespace Madlands.Shared.Terrain
{
    /// <summary>
    /// Triangles of a map's static obstacles (trees, rocks, buildings): everything that blocks
    /// movement and line of sight besides the terrain itself, which the <see cref="HeightMap"/>
    /// covers. Exported from the Unity scene's static colliders (Madlands > Export Map); the server
    /// builds its navmesh from terrain + these triangles and casts line-of-sight rays against them.
    ///
    /// File format (little-endian): "MLCM" | ushort version | int vertexCount | int triangleCount |
    /// float[vertexCount * 3] (x, y, z) | int[triangleCount * 3] vertex indices.
    /// </summary>
    public sealed class CollisionMesh
    {
        public const string FileMagic = "MLCM";
        public const ushort FormatVersion = 1;
        public const int MaxVertices = 4 * 1024 * 1024;
        public const int MaxTriangles = 8 * 1024 * 1024;

        public static readonly CollisionMesh Empty = new CollisionMesh(new float[0], new int[0]);

        public CollisionMesh(float[] vertices, int[] indices)
        {
            if (vertices == null || vertices.Length % 3 != 0) throw new ArgumentException("Vertices must be (x, y, z) triples.");
            if (indices == null || indices.Length % 3 != 0) throw new ArgumentException("Indices must be triangle triples.");
            int vertexCount = vertices.Length / 3;
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] < 0 || indices[i] >= vertexCount) throw new ArgumentException("Index " + indices[i] + " out of range.");
            Vertices = vertices;
            Indices = indices;
        }

        /// <summary>x, y, z per vertex. Do not modify.</summary>
        public float[] Vertices { get; }
        /// <summary>Three vertex indices per triangle. Do not modify.</summary>
        public int[] Indices { get; }
        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => Indices.Length / 3;

        public Vec3 GetVertex(int index) => new Vec3(Vertices[index * 3], Vertices[index * 3 + 1], Vertices[index * 3 + 2]);

        public void Write(Stream stream)
        {
            using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                w.Write(Encoding.ASCII.GetBytes(FileMagic));
                w.Write(FormatVersion);
                w.Write(VertexCount);
                w.Write(TriangleCount);
                for (int i = 0; i < Vertices.Length; i++) w.Write(Vertices[i]);
                for (int i = 0; i < Indices.Length; i++) w.Write(Indices[i]);
            }
        }

        public static CollisionMesh Read(Stream stream)
        {
            using (var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
            {
                string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != FileMagic) throw new InvalidDataException("Not a Madlands collision mesh (bad magic).");
                ushort version = r.ReadUInt16();
                if (version != FormatVersion) throw new InvalidDataException("Unsupported collision mesh version " + version + ".");
                int vertexCount = r.ReadInt32();
                int triangleCount = r.ReadInt32();
                if (vertexCount < 0 || vertexCount > MaxVertices || triangleCount < 0 || triangleCount > MaxTriangles)
                    throw new InvalidDataException("Invalid collision mesh size: " + vertexCount + " vertices, " + triangleCount + " triangles.");
                var vertices = new float[vertexCount * 3];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = r.ReadSingle();
                var indices = new int[triangleCount * 3];
                for (int i = 0; i < indices.Length; i++) indices[i] = r.ReadInt32();
                try
                {
                    return new CollisionMesh(vertices, indices);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException(ex.Message);
                }
            }
        }

        public static CollisionMesh Load(string path)
        {
            using (var fs = File.OpenRead(path)) return Read(fs);
        }

        public void Save(string path)
        {
            using (var fs = File.Create(path)) Write(fs);
        }
    }
}
