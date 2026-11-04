// This file and every function within was written by Claude Opus 5.5
using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Object pool for upgrade pickups. Pickups are built once per upgrade type and then just
    /// enabled / disabled, instead of being created and destroyed every hole.
    /// </summary>
    public class PickupPool
    {
        private readonly Transform root;
        private readonly GameAssets assets;
        private readonly float tileScale;
        private readonly Dictionary<UpgradeType, Stack<UpgradePickup>> available = new Dictionary<UpgradeType, Stack<UpgradePickup>>();
        private readonly List<UpgradePickup> active = new List<UpgradePickup>();

        public int CreatedCount { get; private set; }

        public PickupPool(Transform parent, GameAssets assets, float tileScale)
        {
            this.assets = assets;
            this.tileScale = tileScale;
            root = new GameObject("PickupPool").transform;
            root.SetParent(parent, false);
        }

        /// <summary>Takes a pickup of this type from the pool (building one only if none is free) and places it.</summary>
        public UpgradePickup Spawn(UpgradeType type, Vector3 position)
        {
            if (!available.TryGetValue(type, out var stack))
                available[type] = stack = new Stack<UpgradePickup>();

            UpgradePickup pickup = null;
            while (stack.Count > 0 && pickup == null) pickup = stack.Pop(); // skip anything destroyed externally

            if (pickup == null)
            {
                var go = new GameObject($"Pickup_{type}");
                go.SetActive(false);
                go.transform.SetParent(root, false);
                pickup = go.AddComponent<UpgradePickup>();
                pickup.Build(type, assets, tileScale);
                CreatedCount++;
            }

            pickup.Pool = this;
            active.Add(pickup);
            pickup.Activate(position);
            return pickup;
        }

        /// <summary>Disables the pickup and puts it back for reuse.</summary>
        public void Release(UpgradePickup pickup)
        {
            if (pickup == null || !active.Remove(pickup)) return;
            pickup.gameObject.SetActive(false);
            if (!available.TryGetValue(pickup.type, out var stack))
                available[pickup.type] = stack = new Stack<UpgradePickup>();
            stack.Push(pickup);
        }

        /// <summary>Returns every pickup still on the course (called when a hole is cleared).</summary>
        public void ReleaseAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) Release(active[i]);
        }
    }
}
