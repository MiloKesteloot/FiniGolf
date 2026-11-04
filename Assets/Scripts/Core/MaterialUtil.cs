using UnityEngine;
using UnityEngine.Rendering;

namespace FiniGolf
{
    public static class MaterialUtil
    {
        public const string ParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        /// <summary>Configures a URP (Particles/)Unlit material as alpha-blended transparent that uses vertex colours.</summary>
        public static void SetupTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>Turns a copy of an opaque URP Lit material into an alpha-blended one (for fading structures).</summary>
        public static void SetupTransparentLit(Material m)
        {
            SetupTransparent(m);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("ShadowCaster", true);
        }

        /// <summary>Runtime fallback when the GameAssets has no path material yet.</summary>
        public static Material CreateTransparentUnlit()
        {
            var shader = Shader.Find(ParticlesUnlit) ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { name = "PathLine (runtime)" };
            if (shader.name == ParticlesUnlit) SetupTransparent(m);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }
    }
}
