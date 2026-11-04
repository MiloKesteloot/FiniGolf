// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace FiniGolf
{
    public enum BallState { Hidden, Ready, Aiming, Moving, Sinking, Fallen }

    /// <summary>
    /// Singleton manager that owns the golf ball: click-and-drag aiming, shooting, rolling physics,
    /// upgrade profiles, sinking into the cup and falling off the course.
    /// </summary>
    public class BallManager : Singleton<BallManager>
    {
        [Header("References")]
        public GameAssets assets;
        public Camera cam;

        [Header("Shot")]
        [Tooltip("Ball speed at full power (m/s, for a 2 m tile).")]
        public float maxShotSpeed = 8f;
        public float minShotSpeed = 0.35f;
        [Tooltip("How far (fraction of screen height) you must drag for full power.")]
        [Range(0.05f, 0.8f)] public float maxDragScreenFraction = 0.3f;
        [Tooltip("Releasing below this power cancels the shot.")]
        public float cancelPower = 0.05f;

        [Header("Walls")]
        [Tooltip("Fraction of the speed into the wall that is kept after bouncing (1 = perfectly elastic).")]
        [Range(0f, 1f)] public float wallBounciness = 0.9f;
        [Tooltip("While rolling, any hop off the surface slower than this (m/s) is cancelled, so seams never kick the ball. Landings from the air still bounce.")]
        public float seamHopFilter = 3f;

        [Header("Resting")]
        public float restSpeed = 0.1f;
        public float restTime = 0.35f;
        public float maxRollTime = 25f;

        [Header("Cup")]
        public float sinkMaxSpeed = 2.4f;
        public float lipAssist = 2.5f;

        public BallState State { get; private set; } = BallState.Hidden;
        public Rigidbody Body { get; private set; }
        public Transform BallTransform => Body != null ? Body.transform : null;
        public bool IsResting => State == BallState.Ready || State == BallState.Aiming;
        public bool IsMoving => State == BallState.Moving;
        public float Radius => baseRadius * radiusMultiplier;
        public float AimPower { get; private set; }
        public int LastAimCancelFrame { get; private set; } = -1;

        private Transform root;
        private SphereCollider sphere;
        private GameObject golfVisual;
        private GameObject beachVisual;
        private AimArrow arrow;
        private BallFX fx;
        private PhysicsMaterial physicsMaterial;
        private BallProfile profile = BallProfile.Default;
        private float baseRadius = 0.07f;
        private float radiusMultiplier = 1f;
        private float restTimer;
        private float rollTimer;
        private Vector3 aimDirection = Vector3.forward;
        private Vector3 preStepVelocity;
        private bool wasGrounded;
        // Speed right after the last wall bounce; nothing may push the ball faster than this shortly after.
        private float bounceSpeedCap;
        private int bounceCapSteps;
        private float lastWallBounceTime = -1f;
        private Coroutine resizeRoutine;

        private float TileScale => CourseManager.Instance != null ? CourseManager.Instance.tileScale : 2f;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            Physics.defaultContactOffset = 0.004f;
            Physics.bounceThreshold = 0.6f;
            // 100 Hz physics: at full power the ball moved ~16 cm (more than its diameter) per 50 Hz step.
            Time.fixedDeltaTime = 0.01f;
            if (cam == null) cam = Camera.main;
            EnsureBall();
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureBall()
        {
            if (Body != null) return;

            var course = CourseManager.Instance;
            if (course != null) course.Calibrate();
            float s = TileScale;
            float modelScale = course != null ? course.ModelScale : s;
            baseRadius = 0.035f * s;

            var go = new GameObject("Ball");
            go.layer = 2; // Ignore Raycast: keeps the ball out of pathfinding samples
            root = go.transform;

            Body = go.AddComponent<Rigidbody>();
            Body.useGravity = false;
            Body.mass = profile.mass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.maxAngularVelocity = 150f;
            Body.sleepThreshold = 0.0001f;
            // Never let PhysX pop the ball out of an overlap at high speed (this is what launched it over walls).
            Body.maxDepenetrationVelocity = 0.4f;
            Body.isKinematic = true;

            physicsMaterial = new PhysicsMaterial("BallPhysics")
            {
                dynamicFriction = 0.35f,
                staticFriction = 0.4f,
                bounciness = profile.bounciness,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };

            sphere = go.AddComponent<SphereCollider>();
            sphere.radius = baseRadius;
            sphere.sharedMaterial = physicsMaterial;

            go.AddComponent<BallContact>();
            fx = go.AddComponent<BallFX>();
            fx.Init(this, assets, s);
            var visualParent = fx.Spinner;

            if (assets != null)
            {
                golfVisual = assets.Spawn("ball-red", visualParent, root.position, Quaternion.identity, Vector3.one * modelScale);
                golfVisual.name = "GolfBallVisual";
            }
            else
            {
                golfVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(golfVisual.GetComponent<Collider>());
                golfVisual.transform.SetParent(visualParent, false);
                golfVisual.transform.localScale = Vector3.one * baseRadius * 2f;
            }

            beachVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beachVisual.name = "BeachBallVisual";
            Destroy(beachVisual.GetComponent<Collider>());
            beachVisual.transform.SetParent(visualParent, false);
            beachVisual.transform.localScale = Vector3.one * baseRadius * 2f;
            if (assets != null && assets.beachBallMaterial != null)
                beachVisual.GetComponent<Renderer>().sharedMaterial = assets.beachBallMaterial;
            beachVisual.SetActive(false);

            arrow = AimArrow.Create(assets != null ? assets.arrowMaterial : null);
            arrow.width = 0.04f * s;
            arrow.headLength = 0.1f * s;

            go.SetActive(false);
        }

        /// <summary>Puts the ball (frozen, ready to shoot) on the floor point given.</summary>
        public void PlaceAt(Vector3 floorPoint)
        {
            EnsureBall();
            StopAllCoroutines();
            resizeRoutine = null;
            ApplyRadius(profile.radiusMultiplier, false);

            root.gameObject.SetActive(true);
            Body.isKinematic = true;
            var p = floorPoint + Vector3.up * (Radius + 0.002f);
            root.SetPositionAndRotation(p, Quaternion.identity);
            Body.position = p;
            restTimer = rollTimer = 0f;
            arrow.Hide();
            fx.ResetFX();
            State = BallState.Ready;
        }

        public void Hide()
        {
            EnsureBall();
            StopAllCoroutines();
            arrow.Hide();
            Body.isKinematic = true;
            root.gameObject.SetActive(false);
            State = BallState.Hidden;
        }

        /// <summary>Switches the ball's physics + look to the given upgrade (None = normal golf ball).</summary>
        public void SetUpgrade(UpgradeType type)
        {
            EnsureBall();
            profile = Upgrades.Profile(type);
            Body.mass = profile.mass;
            Body.linearDamping = profile.linearDamping;
            Body.angularDamping = profile.angularDamping;
            physicsMaterial.bounciness = profile.bounciness;

            if (golfVisual != null) golfVisual.SetActive(!profile.beachBallVisual);
            beachVisual.SetActive(profile.beachBallVisual);
            fx.SetBeachBall(profile.beachBallVisual);

            if (resizeRoutine != null) StopCoroutine(resizeRoutine);
            if (root.gameObject.activeInHierarchy)
                resizeRoutine = StartCoroutine(ResizeRoutine(profile.radiusMultiplier));
            else
                ApplyRadius(profile.radiusMultiplier, false);
        }

        private IEnumerator ResizeRoutine(float target)
        {
            float from = radiusMultiplier;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.3f;
                float k = Mathf.SmoothStep(0f, 1f, t);
                // Little overshoot "boing" when growing.
                float boing = Mathf.Sin(k * Mathf.PI) * 0.12f * Mathf.Sign(target - from);
                ApplyRadius(Mathf.Lerp(from, target, k) + boing, true);
                yield return null;
            }
            ApplyRadius(target, true);
            resizeRoutine = null;
        }

        private void ApplyRadius(float multiplier, bool keepOnFloor)
        {
            float oldRadius = Radius;
            radiusMultiplier = Mathf.Max(0.1f, multiplier);
            root.localScale = Vector3.one * radiusMultiplier;
            if (keepOnFloor && Body.isKinematic)
            {
                var p = Body.position;
                p.y += Radius - oldRadius;
                Body.position = p;
                root.position = p;
            }
        }

        // ------------------------------------------------------------------
        // Aiming + shooting
        // ------------------------------------------------------------------

        private void Update()
        {
            if (State == BallState.Ready || State == BallState.Aiming)
                HandleAim();
        }

        private void HandleAim()
        {
            var gm = GameManager.Instance;
            bool locked = gm != null && !gm.CanShoot;
            var pointer = Pointer.current;
            if (pointer == null || cam == null) return;

            if (State == BallState.Ready)
            {
                if (locked) return;
                if (!pointer.press.wasPressedThisFrame || PointerOverUI()) return;
                State = BallState.Aiming;
            }

            if (locked)
            {
                CancelAim();
                return;
            }

            bool cancel = (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ||
                          (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
            if (cancel)
            {
                CancelAim();
                return;
            }

            ComputeAim(pointer.position.ReadValue());

            if (AimPower > cancelPower)
            {
                float s = TileScale;
                var origin = Body.position + Vector3.down * (Radius - 0.012f * s);
                // Base of the arrow sits directly under the ball.
                arrow.Show(origin, aimDirection, 0f, Radius + Mathf.Lerp(0.12f, 1.1f, AimPower) * s, AimPower);
                fx.Charge(-aimDirection, AimPower);
            }
            else
            {
                arrow.Hide();
                fx.CancelCharge();
            }

            if (pointer.press.wasReleasedThisFrame || !pointer.press.isPressed)
            {
                if (AimPower > cancelPower) Shoot();
                else CancelAim();
            }
        }

        private void ComputeAim(Vector2 screenPoint)
        {
            var ballPos = Body.position;
            Vector2 ballScreen = cam.WorldToScreenPoint(ballPos);
            Vector2 delta = screenPoint - ballScreen;
            AimPower = Mathf.Clamp01(delta.magnitude / (Screen.height * maxDragScreenFraction));

            // Arrow points away from the mouse: project the mouse onto the ball's ground plane.
            Vector3 dir = Vector3.zero;
            var plane = new Plane(Vector3.up, ballPos);
            var ray = cam.ScreenPointToRay(screenPoint);
            if (delta.sqrMagnitude > 4f && plane.Raycast(ray, out float enter))
            {
                dir = ballPos - ray.GetPoint(enter);
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 1e-6f && delta.sqrMagnitude > 4f)
            {
                var right = cam.transform.right; right.y = 0f;
                var fwd = cam.transform.forward; fwd.y = 0f;
                dir = -(right.normalized * delta.x + fwd.normalized * delta.y);
            }

            if (dir.sqrMagnitude > 1e-6f) aimDirection = dir.normalized;
        }

        private void CancelAim()
        {
            arrow.Hide();
            if (fx != null) fx.CancelCharge();
            AimPower = 0f;
            LastAimCancelFrame = Time.frameCount;
            if (State == BallState.Aiming) State = BallState.Ready;
        }

        private void Shoot()
        {
            arrow.Hide();
            float speed = Mathf.Lerp(minShotSpeed, maxShotSpeed, AimPower) * profile.powerMultiplier * (TileScale / 2f);

            GameManager.Instance?.RegisterShot();
            if (PathGuide.Instance != null) PathGuide.Instance.Hide();
            Sfx.Instance?.Hit(AimPower);

            Body.isKinematic = false;
            Body.linearVelocity = aimDirection * speed;
            // Start already rolling: otherwise friction eats ~30% of the speed spinning the ball up.
            Body.angularVelocity = RollingSpin(aimDirection * speed);
            preStepVelocity = Body.linearVelocity;
            Body.WakeUp();
            fx.Launch(aimDirection, AimPower, Body.position + Vector3.down * Radius);

            restTimer = rollTimer = 0f;
            wasGrounded = false;
            State = BallState.Moving;
        }

        private static bool PointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        // ------------------------------------------------------------------
        // Physics
        // ------------------------------------------------------------------

        private void FixedUpdate()
        {
            if (Body == null || Body.isKinematic || !root.gameObject.activeInHierarchy) return;

            Body.AddForce(Physics.gravity * profile.gravityScale, ForceMode.Acceleration);
            if (State != BallState.Moving) return;

            // A wall bounce must never leave the ball faster than it arrived.
            if (bounceCapSteps > 0)
            {
                bounceCapSteps--;
                var cur = Body.linearVelocity;
                var curH = new Vector3(cur.x, 0f, cur.z);
                float curSpeed = curH.magnitude;
                if (curSpeed > bounceSpeedCap && curSpeed > 0.001f)
                {
                    curH *= bounceSpeedCap / curSpeed;
                    Body.linearVelocity = new Vector3(curH.x, Mathf.Min(cur.y, 0f), curH.z);
                    Body.angularVelocity = RollingSpin(curH);
                }
            }

            PredictWallBounce(Time.fixedDeltaTime);

            var course = CourseManager.Instance;
            float s = TileScale;
            float dt = Time.fixedDeltaTime;
            var pos = Body.position;
            var v = Body.linearVelocity;
            float speed = v.magnitude;

            bool grounded = Physics.Raycast(pos, Vector3.down, out var groundHit, Radius + 0.025f * s, ~0, QueryTriggerInteraction.Ignore);

            // Safety net against seam "ghost bumps": if the ball was already rolling on the surface last step,
            // it has no business leaving it now, so cancel motion away from the real face under it
            // (works on ramps and hills too). A ball landing from the air is not affected.
            if (grounded && wasGrounded && !profile.beachBallVisual)
            {
                var n = groundHit.normal;
                float away = Vector3.Dot(v, n);
                if (away > 0.02f && away < seamHopFilter)
                {
                    v -= n * away;
                    Body.linearVelocity = v;
                }
            }
            wasGrounded = grounded;

            // Keep the ball purely rolling on the ground: no side-spin or yaw spin left over from wall contacts.
            if (grounded)
                Body.angularVelocity = Vector3.Lerp(Body.angularVelocity, RollingSpin(v), 0.4f);
            fx.Roll(v, grounded, grounded ? groundHit.point : pos + Vector3.down * Radius, dt);

            // Rolling resistance (PhysX spheres would otherwise roll forever).
            var vh = new Vector3(v.x, 0f, v.z);
            float hSpeed = vh.magnitude;
            if (grounded && hSpeed > 0.001f)
            {
                float decel = Mathf.Min(profile.rollingDeceleration, hSpeed / dt);
                Body.AddForce(-vh / hSpeed * decel, ForceMode.Acceleration);
            }

            if (course != null)
            {
                // Cup: magnet upgrade, lip assist and capture.
                var hole = course.HolePosition;
                var toHole = hole - pos;
                toHole.y = 0f;
                float d = toHole.magnitude;

                if (profile.magnetStrength > 0f && d > 0.01f && d < 2.5f * s)
                    Body.AddForce(toHole / d * profile.magnetStrength * (1f - d / (2.5f * s)), ForceMode.Acceleration);

                if (d > 0.001f && d < course.CupRadius * 1.8f && speed < sinkMaxSpeed && grounded)
                    Body.AddForce(toHole / d * lipAssist, ForceMode.Acceleration);

                float capture = Mathf.Max(course.CupRadius - Radius * 0.35f, course.CupRadius * 0.45f);
                if (d < capture && speed < sinkMaxSpeed && pos.y < hole.y + Radius + 0.05f * s)
                {
                    StartCoroutine(SinkRoutine());
                    return;
                }

                if (pos.y < course.KillY)
                {
                    State = BallState.Fallen;
                    arrow.Hide();
                    Sfx.Instance?.Fall();
                    GameManager.Instance?.NotifyBallFell();
                    return;
                }
            }

            preStepVelocity = Body.linearVelocity;
            rollTimer += dt;
            if (grounded && speed < restSpeed) restTimer += dt;
            else restTimer = 0f;

            if (restTimer >= restTime || rollTimer > maxRollTime)
                Stop();
        }

        private void Stop()
        {
            fx.StopRolling();
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            State = BallState.Ready;
            GameManager.Instance?.NotifyBallStopped();
        }

        private IEnumerator SinkRoutine()
        {
            State = BallState.Sinking;
            fx.StopRolling();
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;

            var course = CourseManager.Instance;
            var cup = course != null ? course.HolePosition : Body.position;
            var from = Body.position;
            var to = new Vector3(cup.x, cup.y - Radius * 1.2f, cup.z);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                float k = Mathf.Clamp01(t);
                var p = Vector3.Lerp(from, new Vector3(to.x, from.y, to.z), Mathf.SmoothStep(0f, 1f, k));
                p.y = Mathf.Lerp(from.y, to.y, k * k);
                Body.position = p;
                root.position = p;
                yield return null;
            }

            Sfx.Instance?.Sink();
            yield return new WaitForSeconds(0.15f);
            root.gameObject.SetActive(false);
            GameManager.Instance?.NotifyHoleSunk();
        }

        /// <summary>
        /// Called on collision Enter AND Stay. The whole course is one merged collider that the ball is
        /// always touching through the floor, so wall hits usually arrive as "Stay" contacts.
        /// </summary>
        public void OnBallCollision(Collision collision, bool entered)
        {
            if (State != BallState.Moving || collision.contactCount == 0) return;

            // Find the wall contact (mostly horizontal normal) we are driving into hardest.
            var vh = new Vector3(preStepVelocity.x, 0f, preStepVelocity.z);
            float bestInto = -0.05f;
            Vector3 bestNormal = Vector3.zero, bestPoint = Vector3.zero;
            bool floorImpact = false;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var c = collision.GetContact(i);
                if (Mathf.Abs(c.normal.y) >= 0.5f) { floorImpact = true; continue; }
                // Wall normal from the contact toward the ball centre (independent of Unity's normal convention).
                var n = Body.position - c.point;
                n.y = 0f;
                if (n.sqrMagnitude < 1e-8f) n = new Vector3(c.normal.x, 0f, c.normal.z);
                n.Normalize();
                float into = Vector3.Dot(vh, n);
                if (into < bestInto) { bestInto = into; bestNormal = n; bestPoint = c.point; }
            }

            // Static walls get an arcade bounce; moving hazards are left to PhysX.
            if (bestNormal != Vector3.zero && collision.rigidbody == null)
                BounceOffWall(bestNormal, bestPoint, -bestInto);
            else if (bestNormal != Vector3.zero && entered)
                Sfx.Instance?.Wall(Mathf.Clamp01(-bestInto / 6f));

            if (entered && floorImpact && collision.relativeVelocity.magnitude > 1.6f)
                Sfx.Instance?.Bounce(Mathf.Clamp01(collision.relativeVelocity.magnitude / 8f));
        }

        /// <summary>
        /// Reflects the pre-impact velocity off the wall, keeping almost all of the speed
        /// (PhysX friction + restitution would otherwise kill most of it), with no added spin.
        /// </summary>
        private static int WallMask => Physics.DefaultRaycastLayers & ~(1 << CourseManager.OcclusionLayer);

        /// <summary>
        /// Sweeps the ball along this step's motion. If it would reach a wall, it is moved onto the wall
        /// surface and bounced *before* PhysX ever sees a high-speed overlap with the wall.
        /// Floors, ramps and bumps (non-vertical surfaces) are ignored; moving hazards (Ignore Raycast layer)
        /// are left to PhysX.
        /// </summary>
        private void PredictWallBounce(float dt)
        {
            var v = Body.linearVelocity;
            var vh = new Vector3(v.x, 0f, v.z);
            float speed = vh.magnitude;
            if (speed < 0.3f) return;

            var dir = vh / speed;
            float reach = speed * dt * 1.5f + 0.005f * TileScale;
            var start = Body.position;
            if (!Physics.SphereCast(start, Radius * 0.95f, dir, out var hit, reach, WallMask, QueryTriggerInteraction.Ignore))
                return;
            if (Mathf.Abs(hit.normal.y) > 0.5f) return; // floor / ramp / bump, not a wall

            var centreAtHit = start + dir * hit.distance;
            var n = centreAtHit - hit.point;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-8f) return;
            n.Normalize();
            float into = Vector3.Dot(vh, n);
            if (into > -0.05f) return;

            // Touch the wall, then bounce.
            // The sweep used a slightly smaller sphere, so back the real ball off until it just touches
            // (otherwise it starts inside the wall and PhysX pushes it out = free extra speed).
            var touch = centreAtHit + n * (Radius * 0.05f + 0.002f);
            Body.position = touch;
            preStepVelocity = v;
            BounceOffWall(n, hit.point, -into);
        }

        private void BounceOffWall(Vector3 n, Vector3 point, float intoSpeed)
        {
            if (Mathf.Approximately(lastWallBounceTime, Time.fixedTime)) return;
            lastWallBounceTime = Time.fixedTime;

            var v = preStepVelocity;
            var vh = new Vector3(v.x, 0f, v.z);
            float into = Vector3.Dot(vh, n); // negative: moving into the wall
            var tangential = vh - into * n;
            var bounced = tangential - into * wallBounciness * n;
            float vy = Mathf.Min(Body.linearVelocity.y, Mathf.Max(v.y, 0f));
            Body.linearVelocity = new Vector3(bounced.x, vy, bounced.z);
            Body.angularVelocity = RollingSpin(bounced);
            preStepVelocity = Body.linearVelocity;

            bounceSpeedCap = bounced.magnitude;
            bounceCapSteps = 6;

            float strength = Mathf.Clamp01(intoSpeed / (6f * TileScale / 2f));
            Sfx.Instance?.Wall(strength);
            if (intoSpeed > 0.25f) fx.WallHit(point, n, strength);
        }

        private Vector3 RollingSpin(Vector3 velocity)
        {
            var h = new Vector3(velocity.x, 0f, velocity.z);
            return Vector3.Cross(Vector3.up, h) / Mathf.Max(0.001f, Radius);
        }
    }
}
