using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// A* pathfinding over a fine walkability grid of the generated hole.
    /// Every tile is split into <see cref="CellsPerTile"/>² nodes. A node is walkable when a ray finds floor
    /// and the ball physically fits there; two neighbouring nodes are linked when the height step is
    /// climbable and nothing blocks the ball between them. Nodes close to walls cost more, so the
    /// resulting "best path" stays nicely in the middle of the lane.
    /// </summary>
    public class CoursePathfinder
    {
        public const int CellsPerTile = 10;

        private class Node
        {
            public Vector2Int grid;
            public Vector3 position;     // floor point
            public float penalty;        // extra cost multiplier near walls
            public readonly List<int> links = new List<int>(8);
        }

        private readonly List<Node> nodes = new List<Node>();
        private readonly Dictionary<Vector2Int, int> lookup = new Dictionary<Vector2Int, int>();
        private CourseManager course;
        private float cellSize;
        private float ballRadius;
        private float maxStepPerMetre = 0.55f;
        private int mask;

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        public int NodeCount => nodes.Count;

        // ------------------------------------------------------------------
        // Grid construction
        // ------------------------------------------------------------------

        public void Build(CourseManager owner)
        {
            course = owner;
            nodes.Clear();
            lookup.Clear();
            float s = course.tileScale;
            cellSize = s / CellsPerTile;
            ballRadius = 0.035f * s;
            // Hazards + ball live on "Ignore Raycast", so only the static course is sampled.
            mask = Physics.DefaultRaycastLayers & ~(1 << CourseManager.OcclusionLayer);
            Physics.SyncTransforms();

            foreach (var kv in course.TileFloors)
            {
                var tile = kv.Key;
                for (int i = 0; i < CellsPerTile; i++)
                {
                    for (int j = 0; j < CellsPerTile; j++)
                    {
                        var g = new Vector2Int(tile.x * CellsPerTile + i, tile.y * CellsPerTile + j);
                        if (TrySampleNode(GridToXZ(g), kv.Value, out var pos, out float penalty))
                        {
                            lookup[g] = nodes.Count;
                            nodes.Add(new Node { grid = g, position = pos, penalty = penalty });
                        }
                    }
                }
            }

            foreach (var n in nodes)
            {
                foreach (var d in Neighbours)
                {
                    if (!lookup.TryGetValue(n.grid + d, out int other)) continue;
                    // Diagonals need both side cells too, so paths never squeeze through wall corners.
                    if (d.x != 0 && d.y != 0 &&
                        (!lookup.ContainsKey(n.grid + new Vector2Int(d.x, 0)) || !lookup.ContainsKey(n.grid + new Vector2Int(0, d.y))))
                        continue;
                    if (CanTraverse(n.position, nodes[other].position)) n.links.Add(other);
                }
            }
        }

        private Vector3 GridToXZ(Vector2Int g)
        {
            // Tile (0,0) is centred on the origin, so its first cell starts at -tileScale/2.
            float half = course.tileScale * 0.5f;
            return new Vector3(g.x * cellSize - half + cellSize * 0.5f, 0f, g.y * cellSize - half + cellSize * 0.5f);
        }

        private Vector2Int WorldToGrid(Vector3 p)
        {
            float half = course.tileScale * 0.5f;
            return new Vector2Int(Mathf.FloorToInt((p.x + half) / cellSize), Mathf.FloorToInt((p.z + half) / cellSize));
        }

        private Vector2Int TileOf(Vector2Int g) =>
            new Vector2Int(Mathf.FloorToInt(g.x / (float)CellsPerTile), Mathf.FloorToInt(g.y / (float)CellsPerTile));

        /// <summary>
        /// Rays start just above the tile's highest floor but below tunnel / windmill ceilings,
        /// so passages are found while wall tops still read as "too high".
        /// </summary>
        private bool SampleFloor(Vector3 xz, Vector2 floorRange, out RaycastHit hit)
        {
            float top = floorRange.y + 0.13f * course.tileScale;
            float bottom = floorRange.x - 0.15f * course.tileScale;
            var origin = new Vector3(xz.x, top, xz.z);
            return Physics.Raycast(origin, Vector3.down, out hit, top - bottom, mask, QueryTriggerInteraction.Ignore);
        }

        private bool TrySampleNode(Vector3 xz, Vector2 floorRange, out Vector3 pos, out float penalty)
        {
            pos = xz;
            penalty = 0f;
            if (!SampleFloor(xz, floorRange, out var hit) || hit.normal.y < 0.6f) return false;
            pos = hit.point;

            float r = ballRadius;
            // Does the ball fit here?
            if (Physics.CheckSphere(pos + hit.normal * (r * 1.15f), r * 0.8f, mask, QueryTriggerInteraction.Ignore))
                return false;

            // Near a wall? (sphere raised so it doesn't touch the floor itself)
            if (Physics.CheckSphere(pos + Vector3.up * (r * 2.4f), r * 2.2f, mask, QueryTriggerInteraction.Ignore))
                penalty = 2.5f;
            else if (Physics.CheckSphere(pos + Vector3.up * (r * 3.6f), r * 3.4f, mask, QueryTriggerInteraction.Ignore))
                penalty = 0.8f;
            return true;
        }

        private bool CanTraverse(Vector3 a, Vector3 b)
        {
            var flat = b - a;
            float dy = Mathf.Abs(flat.y);
            flat.y = 0f;
            float dist = flat.magnitude;
            if (dy > dist * maxStepPerMetre + 0.01f) return false;

            // Cast both ways at ball height: mesh colliders are one-sided for queries.
            var up = Vector3.up * (ballRadius * 1.2f);
            var from = a + up;
            var to = b + up;
            var dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-4f) return true;
            dir /= len;
            if (Physics.Raycast(from, dir, len, mask, QueryTriggerInteraction.Ignore)) return false;
            if (Physics.Raycast(to, -dir, len, mask, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        // ------------------------------------------------------------------
        // A*
        // ------------------------------------------------------------------

        private int Nearest(Vector3 p, int searchRadius = 3)
        {
            var g = WorldToGrid(p);
            int best = -1;
            float bestD = float.MaxValue;
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dz = -searchRadius; dz <= searchRadius; dz++)
                {
                    if (!lookup.TryGetValue(g + new Vector2Int(dx, dz), out int i)) continue;
                    float d = (nodes[i].position - p).sqrMagnitude + nodes[i].penalty * 0.001f;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        /// <summary>Finds the best path from <paramref name="from"/> to <paramref name="to"/>. Returns a smoothed polyline on the floor.</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            int start = Nearest(from);
            int goal = Nearest(to);
            if (start < 0 || goal < 0) return false;

            int n = nodes.Count;
            var g = new float[n];
            var cameFrom = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; cameFrom[i] = -1; }

            var open = new MinHeap(n);
            g[start] = 0f;
            open.Push(start, Heuristic(start, goal));

            bool found = false;
            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current]) continue;
                if (current == goal) { found = true; break; }
                closed[current] = true;

                var cn = nodes[current];
                foreach (int next in cn.links)
                {
                    if (closed[next]) continue;
                    var nn = nodes[next];
                    float step = Vector3.Distance(cn.position, nn.position) * (1f + nn.penalty);
                    float cost = g[current] + step;
                    if (cost < g[next])
                    {
                        g[next] = cost;
                        cameFrom[next] = current;
                        open.Push(next, cost + Heuristic(next, goal));
                    }
                }
            }

            if (!found) return false;

            var raw = new List<int>();
            for (int c = goal; c != -1; c = cameFrom[c]) raw.Add(c);
            raw.Reverse();

            BuildSmoothPath(raw, from, to, result);
            return result.Count >= 2;
        }

        private float Heuristic(int a, int b)
        {
            var d = nodes[a].position - nodes[b].position;
            d.y = 0f;
            return d.magnitude;
        }

        // ------------------------------------------------------------------
        // Smoothing
        // ------------------------------------------------------------------

        private void BuildSmoothPath(List<int> raw, Vector3 from, Vector3 to, List<Vector3> result)
        {
            // 1) String pulling: skip nodes while a straight walk between them stays on linked, wall-free cells.
            var pulled = new List<Vector3> { from };
            int anchor = 0;
            while (anchor < raw.Count - 1)
            {
                int furthest = anchor + 1;
                for (int j = raw.Count - 1; j > anchor + 1; j--)
                {
                    if (Clear(nodes[raw[anchor]], nodes[raw[j]])) { furthest = j; break; }
                }
                anchor = furthest;
                pulled.Add(anchor == raw.Count - 1 ? to : nodes[raw[anchor]].position);
            }
            if (pulled.Count < 2) pulled.Add(to);

            // 2) Densify then Chaikin-smooth for soft curves.
            var dense = new List<Vector3>();
            float spacing = cellSize * 1.5f;
            for (int i = 0; i < pulled.Count - 1; i++)
            {
                var a = pulled[i];
                var b = pulled[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / spacing));
                for (int k = 0; k < steps; k++) dense.Add(Vector3.Lerp(a, b, k / (float)steps));
            }
            dense.Add(pulled[pulled.Count - 1]);

            for (int iteration = 0; iteration < 2; iteration++)
            {
                var smoothed = new List<Vector3>(dense.Count * 2) { dense[0] };
                for (int i = 0; i < dense.Count - 1; i++)
                {
                    smoothed.Add(Vector3.Lerp(dense[i], dense[i + 1], 0.25f));
                    smoothed.Add(Vector3.Lerp(dense[i], dense[i + 1], 0.75f));
                }
                smoothed.Add(dense[dense.Count - 1]);
                dense = smoothed;
            }

            // 3) Drop every point back onto the real floor (ramps, bumps, hills).
            float lastY = from.y;
            foreach (var p in dense)
            {
                var tile = TileOf(WorldToGrid(p));
                float y = lastY;
                if (course.TileFloors.TryGetValue(tile, out var range) && SampleFloor(p, range, out var hit) && hit.normal.y > 0.5f)
                    y = hit.point.y;
                lastY = y;
                result.Add(new Vector3(p.x, y, p.z));
            }
        }

        /// <summary>True when walking the grid in a straight line from a to b only uses linked, low-penalty nodes.</summary>
        private bool Clear(Node a, Node b)
        {
            var delta = b.position - a.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            int samples = Mathf.Max(1, Mathf.CeilToInt(dist / (cellSize * 0.5f)));
            int previous = lookup[a.grid];
            for (int k = 1; k <= samples; k++)
            {
                var p = a.position + delta * (k / (float)samples);
                if (!lookup.TryGetValue(WorldToGrid(p), out int idx)) return false;
                if (idx == previous) continue;
                var node = nodes[idx];
                if (node.penalty > Mathf.Max(a.penalty, b.penalty) + 0.01f) return false;
                if (!nodes[previous].links.Contains(idx)) return false;
                previous = idx;
            }
            return true;
        }

        /// <summary>Minimal binary min-heap (netstandard2.1 has no PriorityQueue).</summary>
        private class MinHeap
        {
            private readonly List<(int item, float priority)> heap;
            public int Count => heap.Count;
            public MinHeap(int capacity) => heap = new List<(int, float)>(capacity);

            public void Push(int item, float priority)
            {
                heap.Add((item, priority));
                int i = heap.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (heap[parent].priority <= heap[i].priority) break;
                    (heap[parent], heap[i]) = (heap[i], heap[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                var top = heap[0].item;
                int last = heap.Count - 1;
                heap[0] = heap[last];
                heap.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < heap.Count && heap[l].priority < heap[smallest].priority) smallest = l;
                    if (r < heap.Count && heap[r].priority < heap[smallest].priority) smallest = r;
                    if (smallest == i) break;
                    (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
                    i = smallest;
                }
                return top;
            }
        }
    }
}
