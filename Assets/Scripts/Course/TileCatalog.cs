using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Grid edge helpers. N = +Z, E = +X, S = -Z, W = -X.</summary>
    public static class Edge
    {
        public const int N = 0, E = 1, S = 2, W = 3;

        public static readonly Vector2Int[] Offsets =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
        };

        public static int Opposite(int e) => (e + 2) & 3;
        public static int Left(int e) => (e + 3) & 3;
        public static int Right(int e) => (e + 1) & 3;
        public static int Bit(int e) => 1 << e;
        public static Vector3 ToVector(int e) => new Vector3(Offsets[e].x, 0f, Offsets[e].y);
    }

    /// <summary>
    /// Maps an edge in the kit's authoring space (the OBJ/Blender orientation) to the edge
    /// the imported Unity model actually has. Detected at runtime by probing the meshes, so the
    /// generator is robust to FBX axis conversion / mirroring.
    /// </summary>
    public struct EdgeMap
    {
        public int rotation;
        public bool mirrorX;

        public int Map(int kitEdge)
        {
            int e = kitEdge;
            if (mirrorX && (e == Edge.E || e == Edge.W)) e = 4 - e;
            return (e + rotation) & 3;
        }

        public override string ToString() => $"rot {rotation * 90}°, mirrorX {mirrorX}";
    }

    public enum TileRole { Start, Hole, Straight, Corner, Ramp }

    public class TileDef
    {
        public string model;
        public TileRole role;
        public float weight = 1f;
        public int minLevel = 1;
        public int maxPerCourse = 99;
        /// <summary>Height gained from the kit-space S edge to the N edge, in tile units (ramps only).</summary>
        public float rise;
        /// <summary>Plain tile: good for pickups and moving hazards.</summary>
        public bool plain;
        public bool windmill;
        /// <summary>Tall structure that fades out when it hides the ball or cup.</summary>
        public bool tall;
        /// <summary>Kit-space edge the ball should preferably enter from (-1 = any).</summary>
        public int preferredEntry = -1;

        // Resolved after calibration (Unity local space).
        public int localOpenMask;
        public readonly float[] localHeight = new float[4];

        /// <summary>Openings in kit space. Every straight-like piece in the kit runs along Z.</summary>
        public int KitOpenMask
        {
            get
            {
                switch (role)
                {
                    case TileRole.Start: return Edge.Bit(Edge.S);
                    case TileRole.Hole: return Edge.Bit(Edge.S);
                    case TileRole.Corner: return Edge.Bit(Edge.S) | Edge.Bit(Edge.W);
                    default: return Edge.Bit(Edge.N) | Edge.Bit(Edge.S);
                }
            }
        }

        public void Resolve(EdgeMap map)
        {
            localOpenMask = 0;
            for (int i = 0; i < 4; i++) localHeight[i] = 0f;
            for (int kit = 0; kit < 4; kit++)
            {
                int local = map.Map(kit);
                if ((KitOpenMask & Edge.Bit(kit)) != 0) localOpenMask |= Edge.Bit(local);
                if (role == TileRole.Ramp && kit == Edge.N) localHeight[local] = rise;
            }
        }
    }

    public static class TileCatalog
    {
        public static readonly TileDef Start = new TileDef { model = "end", role = TileRole.Start };
        public static readonly TileDef Hole = new TileDef { model = "hole-round", role = TileRole.Hole };
        public static readonly TileDef Corner = new TileDef { model = "corner", role = TileRole.Corner };

        public static readonly List<TileDef> Straights = new List<TileDef>
        {
            new TileDef { model = "straight", role = TileRole.Straight, weight = 10f, plain = true },
            new TileDef { model = "windmill", role = TileRole.Straight, weight = 3f, maxPerCourse = 1, windmill = true, tall = true, preferredEntry = Edge.S },
            new TileDef { model = "tunnel-wide", role = TileRole.Straight, weight = 1.5f, tall = true, maxPerCourse = 2 },
            new TileDef { model = "bump-walls", role = TileRole.Straight, weight = 2f, minLevel = 2 },
            new TileDef { model = "bump-down-walls", role = TileRole.Straight, weight = 1.5f, minLevel = 2 },
            new TileDef { model = "crest", role = TileRole.Straight, weight = 1.5f, minLevel = 2 },
            new TileDef { model = "castle", role = TileRole.Straight, weight = 1.5f, tall = true, minLevel = 2, maxPerCourse = 1 },
            new TileDef { model = "hill-round", role = TileRole.Straight, weight = 2f, minLevel = 3 },
            new TileDef { model = "hill-square", role = TileRole.Straight, weight = 1.5f, minLevel = 3 },
            new TileDef { model = "split", role = TileRole.Straight, weight = 1.5f, minLevel = 3 },
            new TileDef { model = "obstacle-block", role = TileRole.Straight, weight = 2f, minLevel = 3 },
            new TileDef { model = "narrow-block", role = TileRole.Straight, weight = 1.5f, minLevel = 4 },
            new TileDef { model = "narrow-round", role = TileRole.Straight, weight = 1.5f, minLevel = 4 },
            new TileDef { model = "tunnel-narrow", role = TileRole.Straight, weight = 1.2f, tall = true, minLevel = 4, maxPerCourse = 2 },
            new TileDef { model = "wall-left", role = TileRole.Straight, weight = 1f, minLevel = 5 },
            new TileDef { model = "wall-right", role = TileRole.Straight, weight = 1f, minLevel = 5 },
            new TileDef { model = "tunnel-double", role = TileRole.Straight, weight = 1f, tall = true, minLevel = 5, maxPerCourse = 1 },
            new TileDef { model = "narrow-square", role = TileRole.Straight, weight = 1f, minLevel = 6 },
        };

        public static readonly List<TileDef> Ramps = new List<TileDef>
        {
            new TileDef { model = "ramp-low", role = TileRole.Ramp, rise = 0.1f, minLevel = 2, weight = 3f },
            new TileDef { model = "ramp-medium", role = TileRole.Ramp, rise = 0.25f, minLevel = 3, weight = 2f },
            new TileDef { model = "ramp", role = TileRole.Ramp, rise = 0.5f, minLevel = 5, weight = 1f },
        };

        public static IEnumerable<TileDef> All
        {
            get
            {
                yield return Start;
                yield return Hole;
                yield return Corner;
                foreach (var t in Straights) yield return t;
                foreach (var t in Ramps) yield return t;
            }
        }
    }
}
