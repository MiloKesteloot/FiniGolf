using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Central registry of every asset the game spawns at runtime (Kenney models, materials, sprites).
    /// Filled in automatically by the FiniGolf > Build Game menu.
    /// </summary>
    [CreateAssetMenu(menuName = "FiniGolf/Game Assets", fileName = "GameAssets")]
    public class GameAssets : ScriptableObject
    {
        [System.Serializable]
        public struct NamedModel
        {
            public string name;
            public GameObject model;
        }

        [Header("Kenney Minigolf Kit models (by file name)")]
        public List<NamedModel> models = new List<NamedModel>();

        [Header("Materials")]
        public Material tileMaterial;
        public Material arrowMaterial;
        public Material beachBallMaterial;
        public Material hazardMaterial;
        public Material pickupMaterial;
        public Material pathMaterial;
        [Tooltip("Transparent copy of the tile material, used when structures fade.")]
        public Material fadedTileMaterial;

        [Header("UI")]
        public Sprite heartSprite;
        public Sprite panelSprite;

        private Dictionary<string, GameObject> lookup;

        public GameObject GetModel(string modelName)
        {
            if (lookup == null || lookup.Count != models.Count)
            {
                lookup = new Dictionary<string, GameObject>();
                foreach (var m in models)
                    if (m.model != null && !string.IsNullOrEmpty(m.name))
                        lookup[m.name] = m.model;
            }

            lookup.TryGetValue(modelName, out var go);
            if (go == null)
                Debug.LogWarning($"[FiniGolf] Model '{modelName}' not found in GameAssets. Run FiniGolf > Build Game.");
            return go;
        }

        /// <summary>Instantiates a model under a clean parent so the FBX root transform (axis conversion) is preserved.</summary>
        public GameObject Spawn(string modelName, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, bool applyTileMaterial = true)
        {
            var prefab = GetModel(modelName);
            var holder = new GameObject(modelName);
            holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(position, rotation);
            holder.transform.localScale = scale;
            if (prefab == null) return holder;

            var model = Instantiate(prefab, holder.transform, false);
            model.name = "model";
            model.transform.localPosition = prefab.transform.localPosition;
            model.transform.localRotation = prefab.transform.localRotation;
            model.transform.localScale = prefab.transform.localScale;

            if (applyTileMaterial && tileMaterial != null)
            {
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = tileMaterial;
                    r.sharedMaterials = mats;
                }
            }
            return holder;
        }
    }
}
