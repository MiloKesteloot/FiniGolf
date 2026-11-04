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
        [Range(0f, 1f)] public float wallBounciness = 0.95f;
        [Tooltip("Upward hops smaller than this (m/s) on flat ground are cancelled, so seams never kick the ball.")]
        public float seamHopFilter = 0.9f;

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
        private PhysicsMaterial physicsMaterial;
        private BallProfile profile = BallProfile.Default;
        private float baseRadius = 0.07f;
        private float radiusMultiplier = 1f;
        private float restTimer;
        private float rollTimer;
        private Vector3 aimDirection = Vector3.forward;
        private Vector3 preStepVelocity;
        private float lastWallBounceTime = -1f;
        private Coroutine resizeRoutine;

        private float TileScale => CourseManager.Instance != null ? CourseManager.Instance.tileScale : 2f;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            Physics.defaultContactOffset = 0.004f;
            Physics.bounceThreshold = 0.6f;
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
            root = go.transform;

            Body = go.AddComponent<Rigidbody>();
            Body.useGravity = false;
            Body.mass = profile.mass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.maxAngularVelocity = 150f;
            Body.sleepThreshold = 0.0001f;
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

            if (assets != null)
            {
                golfVisual = assets.Spawn("ball-red", root, root.position, Quaternion.identity, Vector3.one * modelScale);
                golfVisual.name = "GolfBallVisual";
            }
            else
            {
                golfVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(golfVisual.GetComponent<Collider>());
                golfVisual.transform.SetParent(root, false);
                golfVisual.transform.localScale = Vector3.one * baseRadius * 2f;
            }

            beachVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beachVisual.name = "BeachBallVisual";
            Destroy(beachVisual.GetComponent<Collider>());
            beachVisual.transform.SetParent(root, false);
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
                arrow.Show(origin, aimDirection, Radius * 1.4f, Mathf.Lerp(0.12f, 1.1f, AimPower) * s, AimPower);
            }
            else
            {
                arrow.Hide();
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
            AimPower = 0f;
            LastAimCancelFrame = Time.frameCount;
            if (State == BallState.Aiming) State = BallState.Ready;
        }

        private void Shoot()
        {
            arrow.Hide();
            float speed = Mathf.Lerp(minShotSpeed, maxShotSpeed, AimPower) * profile.powerMultiplier * (TileScale / 2f);

            GameManager.Instance?.RegisterShot();
            Sfx.Instance?.Hit(AimPower);

            Body.isKinematic = false;
            Body.linearVelocity = aimDirection * speed;
            // Start already rolling: otherwise friction eats ~30% of the speed spinning the ball up.
            Body.angularVelocity = RollingSpin(aimDirection * speed);
            preStepVelocity = Body.linearVelocity;
            Body.WakeUp();

            restTimer = rollTimer = 0f;
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

            var course = CourseManager.Instance;
            float s = TileScale;
            float dt = Time.fixedDeltaTime;
            var pos = Body.position;
            var v = Body.linearVelocity;
            float speed = v.magnitude;

            bool grounded = Physics.Raycast(pos, Vector3.down, out var groundHit, Radius + 0.025f * s, ~0, QueryTriggerInteraction.Ignore);

            // Safety net against seam "ghost bumps": on flat ground, small upward hops are just noise.
            if (grounded && !profile.beachBallVisual && groundHit.normal.y > 0.985f && v.y > 0f && v.y < seamHopFilter)
            {
                v.y = 0f;
                Body.linearVelocity = v;
            }

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
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            State = BallState.Ready;
            GameManager.Instance?.NotifyBallStopped();
        }

        private IEnumerator SinkRoutine()
        {
            State = BallState.Sinking;
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

        public void OnBallCollision(Collision collision)
        {
            if (State != BallState.Moving || collision.contactCount == 0) return;
            float impact = collision.relativeVelocity.magnitude;

            var normal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++) normal += collision.GetContact(i).normal;
            normal.Normalize();

            bool isWall = Mathf.Abs(normal.y) < 0.5f;
            // Static walls get an arcade bounce; moving hazards are left to PhysX.
            if (isWall && collision.rigidbody == null)
                BounceOffWall(normal);

            if (isWall && impact > 0.5f)
                Sfx.Instance?.Wall(Mathf.Clamp01(impact / 6f));
            else if (impact > 1.6f)
                Sfx.Instance?.Bounce(Mathf.Clamp01(impact / 8f));
        }

        /// <summary>
        /// Reflects the pre-impact velocity off the wall, keeping almost all of the speed
        /// (PhysX friction + restitution would otherwise kill most of it), and keeps the ball rolling.
        /// </summary>
        private void BounceOffWall(Vector3 contactNormal)
        {
            if (Mathf.Approximately(lastWallBounceTime, Time.fixedTime)) return;

            var n = new Vector3(contactNormal.x, 0f, contactNormal.z);
            if (n.sqrMagnitude < 1e-4f) return;
            n.Normalize();

            var v = preStepVelocity;
            var vh = new Vector3(v.x, 0f, v.z);
            float into = Vector3.Dot(vh, n);
            if (into > 0f) { n = -n; into = -into; } // make n face the ball, whatever Unity's convention
            if (into > -0.05f) return;               // grazing / already moving away

            lastWallBounceTime = Time.fixedTime;
            var tangential = vh - into * n;
            var bounced = tangential - into * wallBounciness * n;
            float vy = Mathf.Min(Body.linearVelocity.y, Mathf.Max(v.y, 0f));
            Body.linearVelocity = new Vector3(bounced.x, vy, bounced.z);
            Body.angularVelocity = RollingSpin(bounced);
            preStepVelocity = Body.linearVelocity;
        }

        private Vector3 RollingSpin(Vector3 velocity)
        {
            var h = new Vector3(velocity.x, 0f, velocity.z);
            return Vector3.Cross(Vector3.up, h) / Mathf.Max(0.001f, Radius);
        }
    }
}
