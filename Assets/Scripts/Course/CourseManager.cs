// This file and every function within was written by Claude Opus 5.5
using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Singleton manager that procedurally generates minigolf holes out of Kenney kit tiles.
    /// A hole is a self-avoiding random walk on a grid: start tile -> straights / corners / ramps -> cup.
    /// </summary>
    public class CourseManager : Singleton<CourseManager>
    {
        /// <summary>
        /// Layer for per-tile "occlusion only" colliders of fadeable tiles. It collides with nothing;
        /// it only exists so the camera can tell which structure is hiding the ball.
        /// </summary>
        public const int OcclusionLayer = 31;

        protected override void Awake()
        {
            base.Awake();
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(OcclusionLayer, i, true);
        }

        [Header("References")]
        public GameAssets assets;
        public Transform courseRoot;

        [Header("Layout")]
        [Tooltip("World size of one kit tile in metres.")]
        public float tileScale = 2f;
        [Tooltip("Highest the course may climb, in tile units.")]
        public float maxHeight = 1f;

        // ---- Results of the last Generate() ----
        public Vector3 StartPosition { get; private set; }
        public Vector3 StartForward { get; private set; }
        public Vector3 HolePosition { get; private set; }
        public int Par { get; private set; }
        /// <summary>The upgrade actually placed on this hole (None if there was nowhere to put it).</summary>
        public UpgradeType PlacedPickup { get; private set; }
        public int Length { get; private set; }
        public Bounds Bounds { get; private set; }
        public float KillY { get; private set; }
        public float CupRadius => 0.067f * tileScale;
        public float ModelScale => tileScale / nativeSize;

        /// <summary>World-space floor height range (min, max) of every tile cell on the current hole.</summary>
        public IReadOnlyDictionary<Vector2Int, Vector2> TileFloors => tileFloors;
        private readonly Dictionary<Vector2Int, Vector2> tileFloors = new Dictionary<Vector2Int, Vector2>();

        private CoursePathfinder pathfinder;
        /// <summary>Walkability grid + A* for the current hole (built lazily on first use).</summary>
        public CoursePathfinder Pathfinder
        {
            get
            {
                if (pathfinder == null)
                {
                    pathfinder = new CoursePathfinder();
                    pathfinder.Build(this);
                }
                return pathfinder;
            }
        }

        private float nativeSize = 1f;
        private float floorBase = 0.0633f;
        private EdgeMap edgeMap = new EdgeMap { rotation = 0, mirrorX = true };
        private bool calibrated;

        private class Slot
        {
            public Vector2Int cell;
            public int entry = -1;
            public int exit = -1;
            public bool isStraightRun;
            public TileDef def;
            public float hIn;
            public float hOut;
            public float yPos;
            public int rotation;
            public bool hasHazard;
            public bool hasPickup;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<GameObject> spawnedTiles = new List<GameObject>();
        private Mesh courseCollisionMesh;
        private PickupPool pickupPool;

        // ------------------------------------------------------------------
        // Calibration: probe the imported meshes to learn scale and orientation.
        // ------------------------------------------------------------------

        public void Calibrate()
        {
            if (calibrated) return;
            calibrated = true;

            if (assets == null)
            {
                Debug.LogError("[FiniGolf] CourseManager has no GameAssets assigned. Run FiniGolf > Build Game.");
                return;
            }

            var probeRoot = new GameObject("~TileProbe");
            try
            {
                var basePos = new Vector3(5000f, -5000f, 5000f);
                var straight = SpawnProbe("straight", probeRoot.transform, basePos);
                var b = RendererBounds(straight);
                nativeSize = Mathf.Max(b.size.x, b.size.z);
                if (nativeSize < 0.001f) nativeSize = 1f;

                var cornerPos = basePos + Vector3.right * 10f * nativeSize;
                var rampPos = basePos + Vector3.right * 20f * nativeSize;
                SpawnProbe("corner", probeRoot.transform, cornerPos);
                SpawnProbe("ramp", probeRoot.transform, rampPos);
                Physics.SyncTransforms();

                floorBase = Probe(basePos, Vector3.zero);
                float[] hs = EdgeProbes(basePos);
                float[] hc = EdgeProbes(cornerPos);
                float[] hr = EdgeProbes(rampPos);

                bool found = false;
                if (!float.IsNaN(floorBase))
                {
                    foreach (bool mirror in new[] { true, false })
                    {
                        for (int rot = 0; rot < 4 && !found; rot++)
                        {
                            var m = new EdgeMap { rotation = rot, mirrorX = mirror };
                            bool ok =
                                hs[m.Map(Edge.E)] > hs[m.Map(Edge.N)] + 0.04f &&
                                hs[m.Map(Edge.W)] > hs[m.Map(Edge.S)] + 0.04f &&
                                hc[m.Map(Edge.N)] > hc[m.Map(Edge.S)] + 0.04f &&
                                hc[m.Map(Edge.E)] > hc[m.Map(Edge.W)] + 0.04f &&
                                hr[m.Map(Edge.N)] > hr[m.Map(Edge.S)] + 0.15f;
                            if (ok)
                            {
                                edgeMap = m;
                                found = true;
                            }
                        }
                        if (found) break;
                    }
                }

                if (!found)
                {
                    Debug.LogWarning("[FiniGolf] Tile orientation probe failed, falling back to defaults. " +
                                     $"floor={floorBase} straight=[{string.Join(",", hs)}] corner=[{string.Join(",", hc)}] ramp=[{string.Join(",", hr)}]");
                    edgeMap = new EdgeMap { rotation = 0, mirrorX = true };
                    if (float.IsNaN(floorBase)) floorBase = 0.0633f;
                }
                else
                {
                    Debug.Log($"[FiniGolf] Tiles calibrated: native size {nativeSize:0.###}, floor {floorBase:0.###}, {edgeMap}");
                }
            }
            finally
            {
                DestroyImmediate(probeRoot);
            }

            foreach (var def in TileCatalog.All) def.Resolve(edgeMap);
        }

        private GameObject SpawnProbe(string model, Transform parent, Vector3 pos)
        {
            var go = assets.Spawn(model, parent, pos, Quaternion.identity, Vector3.one, false);
            AddColliders(go);
            return go;
        }

        /// <summary>Top-down raycast. Returns surface height in tile units relative to the tile origin.</summary>
        private float Probe(Vector3 tileOrigin, Vector3 offsetTileUnits)
        {
            var origin = tileOrigin + offsetTileUnits * nativeSize + Vector3.up * 10f * nativeSize;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 20f * nativeSize, ~0, QueryTriggerInteraction.Ignore))
                return (hit.point.y - tileOrigin.y) / nativeSize;
            return float.NaN;
        }

        private float[] EdgeProbes(Vector3 tileOrigin)
        {
            var h = new float[4];
            for (int e = 0; e < 4; e++)
            {
                h[e] = Probe(tileOrigin, Edge.ToVector(e) * 0.45f);
                if (float.IsNaN(h[e])) h[e] = -10f;
            }
            return h;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        public static void AddColliders(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                string n = mf.gameObject.name.ToLowerInvariant();
                if (n.Contains("blade") || n.Contains("flag")) continue;
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }

        // ------------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------------

        public void Clear()
        {
            // Pooled pickups are disabled and kept, not destroyed with the course.
            pickupPool?.ReleaseAll();

            if (courseRoot == null)
            {
                courseRoot = new GameObject("Course").transform;
            }
            for (int i = courseRoot.childCount - 1; i >= 0; i--)
            {
                var child = courseRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            slots.Clear();
            spawnedTiles.Clear();
            tileFloors.Clear();
            pathfinder = null;
            if (courseCollisionMesh != null) Destroy(courseCollisionMesh);
            courseCollisionMesh = null;
        }

        /// <summary>Builds a brand new hole. Harder levels are longer and use more varied pieces.</summary>
        public void Generate(int level, int seed, UpgradeType pickup = UpgradeType.None)
        {
            Calibrate();
            Clear();
            if (assets == null) return;

            var rng = new System.Random(seed);
            level = Mathf.Max(1, level);

            int length = Mathf.Clamp(4 + level + rng.Next(0, 3), 5, 22);
            float turnChance = Mathf.Min(0.28f + level * 0.02f, 0.5f);
            float rampChance = level >= 2 ? Mathf.Min(0.1f + level * 0.025f, 0.35f) : 0f;
            float featureChance = Mathf.Min(0.2f + level * 0.05f, 0.65f);
            float hazardChance = level >= 3 ? Mathf.Min(0.12f + level * 0.02f, 0.3f) : 0f;
            int maxHazards = 1 + level / 6;

            var cells = new List<Vector2Int>();
            var dirs = new List<int>();
            BuildPath(length, turnChance, rng, cells, dirs);
            length = dirs.Count;
            Length = length;

            // ---- Assign a tile to every cell ----
            var usage = new Dictionary<TileDef, int>();
            float h = 0f;
            int corners = 0, rampsUp = 0, features = 0, hazards = 0;
            bool lastWasRamp = false;

            for (int i = 0; i <= length; i++)
            {
                var slot = new Slot { cell = cells[i], hIn = h };

                if (i == 0)
                {
                    slot.exit = dirs[0];
                    slot.def = TileCatalog.Start;
                    Place(slot, Edge.Bit(slot.exit), slot.exit, -1, 0f, rng);
                }
                else if (i == length)
                {
                    slot.entry = Edge.Opposite(dirs[i - 1]);
                    slot.def = TileCatalog.Hole;
                    Place(slot, Edge.Bit(slot.entry), slot.entry, -1, 0f, rng);
                }
                else
                {
                    slot.entry = Edge.Opposite(dirs[i - 1]);
                    slot.exit = dirs[i];
                    slot.isStraightRun = dirs[i] == dirs[i - 1];
                    int mask = Edge.Bit(slot.entry) | Edge.Bit(slot.exit);

                    if (!slot.isStraightRun)
                    {
                        slot.def = TileCatalog.Corner;
                        corners++;
                        Place(slot, mask, slot.entry, slot.exit, 0f, rng);
                        lastWasRamp = false;
                    }
                    else
                    {
                        bool placed = false;
                        bool rampAllowed = i >= 2 && i <= length - 2 && !lastWasRamp;
                        if (rampAllowed && rng.NextDouble() < rampChance)
                        {
                            var ramp = PickWeighted(TileCatalog.Ramps, level, usage, rng);
                            if (ramp != null)
                            {
                                bool canUp = h + ramp.rise <= maxHeight + 0.001f;
                                bool canDown = h - ramp.rise >= -0.001f;
                                float delta = 0f;
                                if (canUp && (!canDown || rng.NextDouble() < 0.6)) delta = ramp.rise;
                                else if (canDown) delta = -ramp.rise;

                                if (delta != 0f)
                                {
                                    slot.def = ramp;
                                    placed = Place(slot, mask, slot.entry, slot.exit, delta, rng);
                                    if (placed)
                                    {
                                        if (delta > 0) rampsUp++;
                                        Count(usage, ramp);
                                    }
                                }
                            }
                        }

                        lastWasRamp = placed;

                        if (!placed)
                        {
                            TileDef def = TileCatalog.Straights[0];
                            if (i >= 2 && rng.NextDouble() < featureChance)
                            {
                                var feature = PickWeighted(TileCatalog.Straights, level, usage, rng, skipFirst: true);
                                if (feature != null) def = feature;
                            }
                            slot.def = def;
                            Place(slot, mask, slot.entry, slot.exit, 0f, rng);
                            Count(usage, def);
                            if (!def.plain) features++;
                        }
                    }
                }

                h = slot.hOut;
                slots.Add(slot);
            }

            // ---- Moving hazards on some plain straights ----
            for (int i = 2; i < slots.Count - 1 && hazards < maxHazards; i++)
            {
                var s = slots[i];
                if (s.def.plain && rng.NextDouble() < hazardChance)
                {
                    s.hasHazard = true;
                    hazards++;
                }
            }

            // ---- Upgrade pickup near the middle of the hole ----
            // Prefer plain straights, then any other open (non-tunnel) tile incl. corners, so it always spawns.
            PlacedPickup = UpgradeType.None;
            if (pickup != UpgradeType.None)
            {
                Slot best = null;
                float bestScore = float.MaxValue;
                for (int i = 1; i < slots.Count - 1; i++)
                {
                    var s = slots[i];
                    if (s.hasHazard || s.def.tall) continue;
                    float score = Mathf.Abs(i - slots.Count * 0.5f) + (float)rng.NextDouble();
                    if (!s.def.plain) score += s.def.role == TileRole.Corner ? 6f : 4f;
                    if (score < bestScore) { bestScore = score; best = s; }
                }
                if (best != null)
                {
                    best.hasPickup = true;
                    PlacedPickup = pickup;
                }
            }

            // ---- Spawn everything ----
            var bounds = new Bounds(CellToWorld(slots[0].cell, 0f), Vector3.zero);
            float minY = float.MaxValue;
            foreach (var s in slots)
            {
                SpawnSlot(s, pickup, rng);
                var c = CellToWorld(s.cell, s.yPos);
                bounds.Encapsulate(c + new Vector3(-0.5f, 0f, -0.5f) * tileScale);
                bounds.Encapsulate(c + new Vector3(0.5f, 0.3f, 0.5f) * tileScale);
                minY = Mathf.Min(minY, c.y);
            }
            Bounds = bounds;
            KillY = minY - 0.5f * tileScale;
            MergeCourseColliders();

            foreach (var s in slots)
            {
                float a = (s.hIn + floorBase) * tileScale, b = (s.hOut + floorBase) * tileScale;
                tileFloors[s.cell] = new Vector2(Mathf.Min(a, b), Mathf.Max(a, b));
            }

            var start = slots[0];
            StartPosition = CellToWorld(start.cell, start.yPos + floorBase);
            StartForward = Edge.ToVector(start.exit);

            var hole = slots[slots.Count - 1];
            HolePosition = CellToWorld(hole.cell, hole.yPos + floorBase);

            // ---- Par ----
            float parF = 2f + length * 0.11f + corners * 0.3f + rampsUp * 0.35f + features * 0.1f + hazards * 0.3f;
            Par = Mathf.Clamp(Mathf.RoundToInt(parF), 2, 7);
        }

        private Vector3 CellToWorld(Vector2Int cell, float yTileUnits)
        {
            return new Vector3(cell.x * tileScale, yTileUnits * tileScale, cell.y * tileScale);
        }

        private static void Count(Dictionary<TileDef, int> usage, TileDef def)
        {
            usage.TryGetValue(def, out int n);
            usage[def] = n + 1;
        }

        private static TileDef PickWeighted(List<TileDef> list, int level, Dictionary<TileDef, int> usage, System.Random rng, bool skipFirst = false)
        {
            float total = 0f;
            for (int i = skipFirst ? 1 : 0; i < list.Count; i++)
            {
                var d = list[i];
                usage.TryGetValue(d, out int used);
                if (d.minLevel <= level && used < d.maxPerCourse) total += d.weight;
            }
            if (total <= 0f) return null;

            float roll = (float)rng.NextDouble() * total;
            for (int i = skipFirst ? 1 : 0; i < list.Count; i++)
            {
                var d = list[i];
                usage.TryGetValue(d, out int used);
                if (d.minLevel > level || used >= d.maxPerCourse) continue;
                roll -= d.weight;
                if (roll <= 0f) return d;
            }
            return null;
        }

        /// <summary>
        /// Finds a rotation of slot.def whose openings match <paramref name="worldMask"/> and whose
        /// height change from entry to exit equals <paramref name="delta"/>. Falls back to a plain straight.
        /// </summary>
        private bool Place(Slot slot, int worldMask, int entryWorld, int exitWorld, float delta, System.Random rng)
        {
            var def = slot.def;
            int start = rng.Next(4);
            int chosen = -1;
            float chosenEntryHeight = 0f;

            for (int k = 0; k < 4; k++)
            {
                int r = (start + k) & 3;
                int rotatedMask = 0;
                for (int e = 0; e < 4; e++)
                    if ((def.localOpenMask & Edge.Bit(e)) != 0) rotatedMask |= Edge.Bit((e + r) & 3);
                if (rotatedMask != worldMask) continue;

                int localEntry = (entryWorld - r) & 3;
                float entryHeight = def.localHeight[localEntry];
                if (exitWorld >= 0)
                {
                    float d = def.localHeight[(exitWorld - r) & 3] - entryHeight;
                    if (Mathf.Abs(d - delta) > 0.02f) continue;
                }

                bool preferred = def.preferredEntry >= 0 && edgeMap.Map(def.preferredEntry) == localEntry;
                if (chosen < 0 || preferred)
                {
                    chosen = r;
                    chosenEntryHeight = entryHeight;
                }
                if (preferred || def.preferredEntry < 0) break;
            }

            if (chosen < 0)
            {
                if (def.role == TileRole.Straight && def != TileCatalog.Straights[0])
                {
                    slot.def = TileCatalog.Straights[0];
                    return Place(slot, worldMask, entryWorld, exitWorld, 0f, rng);
                }
                if (def.role == TileRole.Ramp)
                {
                    slot.def = TileCatalog.Straights[0];
                    Place(slot, worldMask, entryWorld, exitWorld, 0f, rng);
                    return false;
                }
                Debug.LogWarning($"[FiniGolf] Could not orient tile '{def.model}' for mask {worldMask}.");
                chosen = 0;
            }

            slot.rotation = chosen;
            slot.yPos = slot.hIn - chosenEntryHeight;
            slot.hOut = slot.hIn + (exitWorld >= 0 ? delta : 0f);
            return true;
        }

        private void SpawnSlot(Slot s, UpgradeType pickup, System.Random rng)
        {
            var pos = CellToWorld(s.cell, s.yPos);
            var rot = Quaternion.Euler(0f, 90f * s.rotation, 0f);
            var tile = assets.Spawn(s.def.model, courseRoot, pos, rot, Vector3.one * ModelScale);
            tile.name = $"{slots.IndexOf(s):00}_{s.def.model}";
            AddColliders(tile);
            spawnedTiles.Add(tile);

            // Windmills, castles, tunnels and raised "bridge" tiles fade when they block the view.
            if (s.def.tall || s.yPos > 0.01f)
            {
                var fade = tile.AddComponent<FadeableTile>();
                fade.isStructure = s.def.tall;
                fade.tileSize = tileScale;
                // Grass + lane walls stay solid; only what rises above the walls fades.
                if (fade.isStructure) fade.SplitStructure(pos.y + 0.16f * tileScale);
            }

            if (s.def.windmill)
            {
                var w = tile.AddComponent<Windmill>();
                w.degreesPerSecond = 50f + (float)rng.NextDouble() * 60f;
            }

            // Supports under raised tiles.
            if (s.yPos > 0.01f)
            {
                // Support model is one tile tall; parent is scaled by ModelScale, so local Y = height in tile units.
                var support = assets.Spawn("supports", tile.transform, CellToWorld(s.cell, 0f), rot, new Vector3(1f, s.yPos, 1f));
                support.name = "supports";
            }

            var floor = CellToWorld(s.cell, s.yPos + floorBase);
            Vector3 along = s.exit >= 0 ? Edge.ToVector(s.exit) : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, along);

            if (s.def.role == TileRole.Hole)
            {
                // Parent tile is already scaled by ModelScale, so the flag keeps unit local scale.
                var flag = assets.Spawn("flag-red", tile.transform, pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one);
                flag.name = "flag";
                var hf = flag.AddComponent<HoleFlag>();
                hf.liftHeight = 0.35f * tileScale;
            }

            if (s.hasHazard)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "SlidingBlock";
                block.layer = 2; // Ignore Raycast: moving, so the pathfinder must not bake it in
                block.transform.SetParent(tile.transform, true);
                var size = new Vector3(0.16f, 0.1f, 0.12f) * tileScale;
                block.transform.localScale = new Vector3(size.x / ModelScale, size.y / ModelScale, size.z / ModelScale);
                block.transform.rotation = Quaternion.LookRotation(along);
                block.transform.position = floor + Vector3.up * size.y * 0.5f;
                if (assets.hazardMaterial != null) block.GetComponent<Renderer>().sharedMaterial = assets.hazardMaterial;
                var rb = block.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var hz = block.AddComponent<SlidingHazard>();
                hz.axis = across;
                hz.amplitude = (0.4f * tileScale) - size.x * 0.5f - 0.02f * tileScale;
                hz.period = 2.2f + (float)rng.NextDouble() * 1.6f;
                hz.phase = (float)rng.NextDouble() * hz.period;
            }

            if (s.hasPickup && pickup != UpgradeType.None)
            {
                if (pickupPool == null) pickupPool = new PickupPool(transform, assets, tileScale);
                pickupPool.Spawn(pickup, floor + Vector3.up * 0.12f * tileScale);
            }
        }

        /// <summary>
        /// Replaces the per-tile MeshColliders with ONE welded collision mesh for the whole hole.
        /// With separate colliders PhysX sees the edge of the next tile as a tiny step and kicks the
        /// ball upward at seams. In a single welded mesh those seams are shared, interior edges that
        /// PhysX ignores. The vertical faces on each tile's outer border (back-to-back between
        /// neighbouring tiles) are dropped so the seam edges stay clean.
        /// </summary>
        private void MergeCourseColliders()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var weld = new Dictionary<Vector3Int, int>();
            var merged = new List<MeshCollider>();
            const float weldResolution = 2000f; // 0.5 mm

            int Weld(Vector3 p)
            {
                var key = new Vector3Int(Mathf.RoundToInt(p.x * weldResolution), Mathf.RoundToInt(p.y * weldResolution), Mathf.RoundToInt(p.z * weldResolution));
                if (weld.TryGetValue(key, out int index)) return index;
                index = vertices.Count;
                vertices.Add(p);
                weld[key] = index;
                return index;
            }

            bool OnBorder(Vector3 a, Vector3 b, Vector3 c, int axis)
            {
                const float edge = 0.495f;
                float pa = a[axis], pb = b[axis], pc = c[axis];
                return (pa > edge && pb > edge && pc > edge) || (pa < -edge && pb < -edge && pc < -edge);
            }

            foreach (var tile in spawnedTiles)
            {
                if (tile == null) continue;
                var toTile = tile.transform.worldToLocalMatrix;
                foreach (var mc in tile.GetComponentsInChildren<MeshCollider>())
                {
                    var mesh = mc.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue; // keeps its own collider

                    var l2w = mc.transform.localToWorldMatrix;
                    bool flip = l2w.determinant < 0f;
                    var mv = mesh.vertices;
                    var mt = mesh.triangles;
                    var world = new Vector3[mv.Length];
                    var local = new Vector3[mv.Length];
                    for (int i = 0; i < mv.Length; i++)
                    {
                        world[i] = l2w.MultiplyPoint3x4(mv[i]);
                        local[i] = toTile.MultiplyPoint3x4(world[i]) / nativeSize;
                    }

                    for (int t = 0; t + 2 < mt.Length; t += 3)
                    {
                        int a = mt[t], b = mt[t + 1], c = mt[t + 2];
                        if (OnBorder(local[a], local[b], local[c], 0) || OnBorder(local[a], local[b], local[c], 2)) continue;
                        int ia = Weld(world[a]), ib = Weld(world[b]), ic = Weld(world[c]);
                        if (ia == ib || ib == ic || ia == ic) continue;
                        triangles.Add(ia);
                        triangles.Add(flip ? ic : ib);
                        triangles.Add(flip ? ib : ic);
                    }
                    merged.Add(mc);
                }
            }

            if (triangles.Count == 0) return;
            int tSplits = FixTJunctions(vertices, triangles, 0.001f * tileScale);
            if (tSplits > 0) Debug.Log($"[FiniGolf] Course collider: repaired {tSplits} T-junction seams.");

            var combined = new Mesh { name = "CourseCollision", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.SetVertices(vertices);
            combined.SetTriangles(triangles, 0);
            combined.RecalculateBounds();

            var go = new GameObject("CourseCollider");
            go.transform.SetParent(courseRoot, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var collider = go.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                      MeshColliderCookingOptions.EnableMeshCleaning |
                                      MeshColliderCookingOptions.WeldColocatedVertices |
                                      MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMesh = combined;
            courseCollisionMesh = combined;

            foreach (var mc in merged)
            {
                if (mc.GetComponentInParent<FadeableTile>() != null)
                {
                    // Keep a non-colliding copy for camera occlusion checks.
                    mc.gameObject.layer = OcclusionLayer;
                    continue;
                }
                mc.enabled = false;
                Destroy(mc);
            }
        }

        /// <summary>
        /// Repairs T-junctions: where a vertex of one tile lies part-way along an edge of its neighbour
        /// (e.g. a straight meeting a corner, or the top of a ramp), that edge can't be shared, so PhysX
        /// treats it as a real edge and kicks the ball up. Splitting the triangle at the vertex makes both
        /// sides share identical edges, which PhysX then ignores. Returns the number of splits made.
        /// </summary>
        private static int FixTJunctions(List<Vector3> verts, List<int> tris, float tolerance)
        {
            const float cell = 0.5f;
            int total = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                var edgeUse = new Dictionary<long, int>();
                long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        long key = Key(tris[t + k], tris[t + (k + 1) % 3]);
                        edgeUse.TryGetValue(key, out int c);
                        edgeUse[key] = c + 1;
                    }
                }

                // Vertices on open (unshared) edges, hashed on a coarse grid.
                var grid = new Dictionary<Vector3Int, List<int>>();
                var seen = new HashSet<int>();
                Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                for (int t = 0; t < tris.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tris[t + k], b = tris[t + (k + 1) % 3];
                        if (edgeUse[Key(a, b)] != 1) continue;
                        foreach (int v in new[] { a, b })
                        {
                            if (!seen.Add(v)) continue;
                            var c = CellOf(verts[v]);
                            if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                            list.Add(v);
                        }
                    }
                }
                if (grid.Count == 0) break;

                int FindInterior(int a, int b)
                {
                    var pa = verts[a];
                    var pb = verts[b];
                    var ab = pb - pa;
                    float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-8f) return -1;
                    var min = CellOf(Vector3.Min(pa, pb) - Vector3.one * tolerance);
                    var max = CellOf(Vector3.Max(pa, pb) + Vector3.one * tolerance);
                    for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                    {
                        if (!grid.TryGetValue(new Vector3Int(x, y, z), out var list)) continue;
                        foreach (int v in list)
                        {
                            if (v == a || v == b) continue;
                            var pv = verts[v];
                            float t = Vector3.Dot(pv - pa, ab) / len2;
                            if (t <= 0.001f || t >= 0.999f) continue;
                            if ((pa + ab * t - pv).sqrMagnitude <= tolerance * tolerance) return v;
                        }
                    }
                    return -1;
                }

                var result = new List<int>(tris.Count + 64);
                int splits = 0;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    bool split = false;
                    for (int k = 0; k < 3 && !split; k++)
                    {
                        int p0 = tris[t + k], p1 = tris[t + (k + 1) % 3], p2 = tris[t + (k + 2) % 3];
                        if (edgeUse[Key(p0, p1)] != 1) continue;
                        int v = FindInterior(p0, p1);
                        if (v < 0) continue;
                        // Same winding: (p0, v, p2) + (v, p1, p2)
                        result.Add(p0); result.Add(v); result.Add(p2);
                        result.Add(v); result.Add(p1); result.Add(p2);
                        split = true;
                        splits++;
                    }
                    if (!split) { result.Add(tris[t]); result.Add(tris[t + 1]); result.Add(tris[t + 2]); }
                }

                tris.Clear();
                tris.AddRange(result);
                total += splits;
                if (splits == 0) break;
            }
            return total;
        }

        /// <summary>Self-avoiding random walk with backtracking. dirs[i] leads from cells[i] to cells[i+1].</summary>
        private static void BuildPath(int length, float turnChance, System.Random rng, List<Vector2Int> cells, List<int> dirs)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                cells.Clear();
                dirs.Clear();
                var used = new HashSet<Vector2Int>();
                var pos = Vector2Int.zero;
                int dir = Edge.N;
                int sinceTurn = 0;
                cells.Add(pos);
                used.Add(pos);
                bool ok = true;

                for (int i = 0; i < length; i++)
                {
                    var options = new List<int>(3);
                    var weights = new List<float>(3);

                    void Consider(int d, float w)
                    {
                        var next = pos + Edge.Offsets[d];
                        if (used.Contains(next)) return;
                        if (i < length - 1)
                        {
                            int free = 0;
                            for (int e = 0; e < 4; e++)
                                if (!used.Contains(next + Edge.Offsets[e])) free++;
                            if (free == 0) return;
                        }
                        options.Add(d);
                        weights.Add(w);
                    }

                    // First two steps go straight so the player gets a clean opening shot.
                    if (i < 2)
                    {
                        Consider(dir, 1f);
                    }
                    else
                    {
                        float straightW = sinceTurn == 0 ? 0.6f : (1f - turnChance) * 2f;
                        Consider(dir, straightW);
                        Consider(Edge.Left(dir), turnChance);
                        Consider(Edge.Right(dir), turnChance);
                    }

                    if (options.Count == 0) { ok = false; break; }

                    float total = 0f;
                    foreach (var w in weights) total += w;
                    float roll = (float)rng.NextDouble() * total;
                    int pick = options[options.Count - 1];
                    for (int k = 0; k < options.Count; k++)
                    {
                        roll -= weights[k];
                        if (roll <= 0f) { pick = options[k]; break; }
                    }

                    sinceTurn = pick == dir ? sinceTurn + 1 : 0;
                    dir = pick;
                    dirs.Add(dir);
                    pos += Edge.Offsets[dir];
                    cells.Add(pos);
                    used.Add(pos);
                }

                if (ok) return;
            }

            // Fallback: a straight line always works.
            cells.Clear();
            dirs.Clear();
            for (int i = 0; i <= length; i++) cells.Add(new Vector2Int(0, i));
            for (int i = 0; i < length; i++) dirs.Add(Edge.N);
        }
    }
}
