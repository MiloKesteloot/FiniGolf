// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Generic MonoBehaviour singleton. Every "Manager" in the game derives from this.
    /// Access the live instance with <c>T.Instance</c>. Duplicate instances destroy themselves.
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;

        [Tooltip("Keep this manager alive when a new scene loads.")]
        [SerializeField] private bool persistAcrossScenes = false;

        public static T Instance
        {
            get
            {
                if (instance == null)
                    instance = FindFirstObjectByType<T>();
                return instance;
            }
        }

        public static bool HasInstance => instance != null || FindFirstObjectByType<T>() != null;

        protected virtual void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this as T;

            if (persistAcrossScenes)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
