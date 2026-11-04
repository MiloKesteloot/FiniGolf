// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Game feel for the ball: squash &amp; stretch (charging, launch, wall hits), a motion trail,
    /// grass clippings while rolling and a puff of smoke on wall bounces.
    /// Visual hierarchy: Ball (rigidbody) → SquashPivot (world-aligned squash axis) → Spinner (follows the
    /// ball's physical roll) → models. That lets the squash axis point anywhere without disturbing the roll.
    /// </summary>
    public class BallFX : MonoBehaviour
    {
        [Header("Squash & stretch")]
        [Tooltip("How much the ball stretches along the shot line at full power.")]
        public float chargeStretch = 0.38f;
        public float releaseStretch = 7f;
        public float wallSquash = 5f;
        public float springStiffness = 320f;
        public float springDamping = 13f;

        [Header("Trail")]
        public float trailTime = 0.3f;
        public Color trailColor = new Color(1f, 1f, 1f, 0.55f);

        [Header("Grass")]
        public float grassPerMetre = 22f;
        public float minGrassSpeed = 0.35f;

        public Transform SquashPivot { get; private set; }
        public Transform Spinner { get; private set; }

        private BallManager owner;
        private TrailRenderer trail;
        private ParticleSystem grass;
        private ParticleSystem puff;
        private float scale = 1f;

        // Spring state: s < 0 squashes along the axis, s > 0 stretches.
        private float squash, squashVelocity, squashTarget;
        private Vector3 squashAxis = Vector3.forward;
        private float grassBudget;
        private bool charging;
        private float chargePower;

        [Header("Full power")]
        [Tooltip("Power above which the ball starts buzzing.")]
        public float fullPowerThreshold = 0.95f;
        [Tooltip("Jitter distance as a fraction of the ball radius.")]
        public float fullPowerShake = 0.3f;
        [Tooltip("Extra rapid stretch wobble at full power.")]
        public float fullPowerWobble = 0.14f;

        private static readonly Color[] GrassColors =
        {
            new Color(0.36f, 0.72f, 0.28f), new Color(0.45f, 0.8f, 0.3f),
            new Color(0.28f, 0.6f, 0.22f), new Color(0.55f, 0.85f, 0.38f),
        };

        public void Init(BallManager ballManager, GameAssets assets, float tileScale)
        {
            owner = ballManager;
            scale = tileScale / 2f;

            SquashPivot = new GameObject("SquashPivot").transform;
            SquashPivot.SetParent(transform, false);
            Spinner = new GameObject("Spinner").transform;
            Spinner.SetParent(SquashPivot, false);

            var baseMat = assets != null ? assets.pathMaterial : null;

            // ---- Trail ----
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(transform, false);
            trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.minVertexDistance = 0.02f * scale;
            trail.widthMultiplier = 0.11f * scale;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.numCapVertices = 4;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.View;
            trail.sharedMaterial = MakeParticleMaterial(baseMat, null, "BallTrail");
            SetTrailColor(trailColor);
            trail.emitting = false;

            // ---- Grass clippings ----
            grass = MakeSystem("GrassFX", MakeParticleMaterial(baseMat, null, "Grass"));
            var gm = grass.main;
            gm.gravityModifier = 1.4f;
            gm.maxParticles = 1200;
            var gRot = grass.rotationOverLifetime;
            gRot.enabled = true;
            gRot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var gSize = grass.sizeOverLifetime;
            gSize.enabled = true;
            gSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));

            // ---- Smoke puff ----
            puff = MakeSystem("PuffFX", MakeParticleMaterial(baseMat, MakeSoftCircle(), "Puff"));
            var pm = puff.main;
            pm.gravityModifier = -0.05f;
            pm.maxParticles = 500;
            var pSize = puff.sizeOverLifetime;
            pSize.enabled = true;
            pSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.3f)));
            var pColor = puff.colorOverLifetime;
            pColor.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            pColor.color = fade;
            var pVel = puff.limitVelocityOverLifetime;
            pVel.enabled = true;
            pVel.dampen = 0.15f;
            pVel.limit = 0f;
        }

        public void SetBeachBall(bool beach)
        {
            SetTrailColor(beach ? new Color(1f, 0.75f, 0.35f, 0.6f) : trailColor);
        }

        private void SetTrailColor(Color c)
        {
            if (trail == null) return;
            var end = c;
            end.a = 0f;
            trail.startColor = c;
            trail.endColor = end;
        }

        // ------------------------------------------------------------------
        // Events from BallManager
        // ------------------------------------------------------------------

        /// <summary>Clears trail and squash (ball teleported / respawned).</summary>
        public void ResetFX()
        {
            squash = squashVelocity = squashTarget = 0f;
            charging = false;
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        /// <summary>While charging: stretch along the pull / shot line, trembling a little near full power.</summary>
        public void Charge(Vector3 pullDirection, float power)
        {
            if (pullDirection.sqrMagnitude > 1e-4f) squashAxis = pullDirection.normalized;
            charging = true;
            chargePower = power;
            float tremble = power > 0.75f ? Mathf.Sin(Time.time * 70f) * 0.04f * (power - 0.75f) * 4f : 0f;
            squashTarget = chargeStretch * power + tremble;
        }

        public void CancelCharge()
        {
            squashTarget = 0f;
            charging = false;
        }

        /// <summary>Shot struck: snap into a stretch along the shot, kick up a divot of grass.</summary>
        public void Launch(Vector3 direction, float power, Vector3 floorPoint)
        {
            squashAxis = direction.normalized;
            squashTarget = 0f;
            charging = false;
            squashVelocity += releaseStretch * (0.4f + power);
            EmitGrass(floorPoint, -direction * (0.8f * scale), Mathf.RoundToInt(14 + power * 36), 1.6f);
        }

        /// <summary>Wall bounce: squash against the wall and puff some smoke.</summary>
        public void WallHit(Vector3 point, Vector3 normal, float strength)
        {
            squashAxis = normal;
            squashVelocity -= wallSquash * (0.3f + strength);

            int count = Mathf.RoundToInt(14 + strength * 22);
            var tangent = Vector3.Cross(normal, Vector3.up);
            for (int i = 0; i < count; i++)
            {
                var spread = tangent * Random.Range(-1f, 1f) + Vector3.up * Random.Range(0f, 0.8f);
                var p = new ParticleSystem.EmitParams
                {
                    position = point + normal * (0.05f * scale) + Random.insideUnitSphere * (0.04f * scale),
                    velocity = (normal * Random.Range(0.4f, 1.2f) + spread * 0.8f) * scale * (0.7f + strength),
                    startSize = Random.Range(0.14f, 0.3f) * scale * (0.8f + strength * 0.6f),
                    startLifetime = Random.Range(0.5f, 0.9f),
                    startColor = new Color(1f, 1f, 1f, Random.Range(0.6f, 0.9f)),
                    rotation = Random.Range(0f, 360f),
                };
                puff.Emit(p, 1);
            }
        }

        /// <summary>Called every physics step while the ball rolls.</summary>
        public void Roll(Vector3 velocity, bool grounded, Vector3 floorPoint, float dt)
        {
            var h = new Vector3(velocity.x, 0f, velocity.z);
            float speed = h.magnitude;
            if (trail != null) trail.emitting = speed > 0.25f * scale;

            if (!grounded || speed < minGrassSpeed * scale) return;
            grassBudget += speed * dt * grassPerMetre / scale;
            int n = Mathf.FloorToInt(grassBudget);
            if (n <= 0) return;
            grassBudget -= n;
            EmitGrass(floorPoint, -h * 0.12f, n, 0.6f + Mathf.Min(speed / (6f * scale), 1f));
        }

        public void StopRolling()
        {
            if (trail != null) trail.emitting = false;
        }

        private void EmitGrass(Vector3 point, Vector3 kick, int count, float lift)
        {
            for (int i = 0; i < count; i++)
            {
                var side = Random.insideUnitSphere;
                side.y = 0f;
                var p = new ParticleSystem.EmitParams
                {
                    position = point + side * (0.04f * scale) + Vector3.up * (0.005f * scale),
                    velocity = kick + side * (0.5f * scale) + Vector3.up * Random.Range(0.5f, 1.2f) * lift * scale,
                    startSize = Random.Range(0.015f, 0.032f) * scale,
                    startLifetime = Random.Range(0.4f, 0.85f),
                    startColor = GrassColors[Random.Range(0, GrassColors.Length)],
                    rotation = Random.Range(0f, 360f),
                };
                grass.Emit(p, 1);
            }
        }

        // ------------------------------------------------------------------
        // Squash spring + visual hierarchy
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            if (SquashPivot == null) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

            float accel = -springStiffness * (squash - squashTarget) - springDamping * squashVelocity;
            squashVelocity += accel * dt;
            squash = Mathf.Clamp(squash + squashVelocity * dt, -0.45f, 0.6f);

            // "Full of power": buzz when charging at max.
            float buzz = 0f;
            if (charging && chargePower >= fullPowerThreshold)
                buzz = Mathf.Clamp01((chargePower - fullPowerThreshold) / Mathf.Max(0.001f, 1f - fullPowerThreshold) + 0.5f);

            float along = 1f + squash + Mathf.Sin(Time.time * 95f) * fullPowerWobble * buzz;
            float across = 1f / Mathf.Sqrt(Mathf.Max(0.2f, along));

            var axis = squashAxis.sqrMagnitude > 1e-4f ? squashAxis : Vector3.forward;
            var up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            SquashPivot.rotation = Quaternion.LookRotation(axis, up);
            SquashPivot.localScale = new Vector3(across, across, along);
            // Keep the bottom of the ball on the ground while it gets taller / flatter.
            float radius = owner != null ? owner.Radius : 0.07f;
            float vertical = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.7f ? along : across;
            SquashPivot.position = transform.position + Vector3.up * ((vertical - 1f) * radius);
            if (buzz > 0f)
            {
                var jitter = Random.insideUnitSphere * (radius * fullPowerShake * buzz);
                jitter.y *= 0.4f;
                SquashPivot.position += jitter;
            }

            // Models keep showing the real physical roll.
            Spinner.rotation = transform.rotation;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ParticleSystem MakeSystem(string name, Material material)
        {
            var go = new GameObject(name);
            go.layer = 2;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static Material MakeParticleMaterial(Material template, Texture texture, string name)
        {
            var m = template != null ? new Material(template) : MaterialUtil.CreateTransparentUnlit();
            if (m == null) return null;
            m.name = name;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }

        private static Texture2D MakeSoftCircle()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftCircle", wrapMode = TextureWrapMode.Clamp };
            var c = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (size * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
