using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person superhero controller: walk / run / sprint, jump, fly, hover, land, blast, punch.
///
/// Keyboard + mouse (Mac test):
///   WASD move · Shift sprint (boost when flying) · Alt walk · Space jump
///   F or double-tap Space = take off / stop flying · in flight: Space up, Ctrl or C down
///   Hold left mouse = blast · Q = punch
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class HeroController : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public Animator animator;
    public Transform visual;                 // the model; tilted while flying

    [Header("Ground")]
    public float walkSpeed = 1.7f;
    public float runSpeed = 5.9f;
    public float sprintSpeed = 9f;
    public float turnSpeed = 12f;
    public float jumpSpeed = 7f;
    public float gravity = -20f;
    public float hardLandingSpeed = 14f;     // falling faster than this plays the superhero landing

    [Header("Super speed (R)")]
    public float superSpeed = 45f;           // ~160 km/h
    public float superAcceleration = 90f;
    public float superLean = 16f;            // degrees of forward lean at full super speed

    [Header("Energy (super speed + flight boost)")]
    public float maxEnergy = 100f;
    public float superSpeedDrain = 12f;      // per second while running at super speed
    public float flightBoostDrain = 20f;     // per second while boosting in the air
    public float energyRegen = 18f;          // per second when not boosting
    public float regenDelay = 1f;            // seconds after boosting before it refills

    [Header("Map edge")]
    public float edgeMargin = 1f;            // how far inside the town edge the wall stands
    public float seaMargin = 120f;           // with a sea: how far out over the water he may go

    [Header("Flight")]
    public float flySpeed = 22f;
    public float boostSpeed = 120f;          // Shift while flying (uses energy)
    public float verticalFlySpeed = 9f;
    public float flyAcceleration = 3f;
    public float flyAnimThreshold = 4f;      // below this speed he hovers, above it he flies
    [Tooltip("While flying he never comes closer than this to the ground or the sea (press F to land)")]
    public float flyClearance = 1.1f;
    [Tooltip("Diving straight down faster than this onto land = superhero slam landing")]
    public float slamSpeed = 25f;

    public enum State { Ground, Air, Fly, Land }
    public State Current { get; private set; }
    public Vector3 Velocity { get => velocity; set => velocity = value; }
    public bool IsFlyingFast => Current == State.Fly && velocity.magnitude > flySpeed * 0.8f;
    public bool SuperMode { get; private set; }

    // ---- set by SpaceSystem
    /// Flight speeds are multiplied by this (huge in open space so planets are reachable).
    public float SpeedMultiplier { get; set; } = 1f;
    public float BoostDrainScale { get; set; } = 1f;     // 0 = flight boost is free (high in the sky / space travel)
    /// 1 on Earth, 1/6 on the Moon.
    public float GravityScale { get; set; } = 1f;
    /// False in deep space: pressing F does not drop him out of flight.
    public bool CanStopFlying { get; set; } = true;
    /// Map edge walls only apply below this height.
    public float MapEdgeCeiling { get; set; } = 1500f;
    public float Energy { get; private set; }
    public float Energy01 => Energy / maxEnergy;
    public bool Exhausted { get; private set; }   // ran dry: must refill to 25% before boosting again
    public bool IsBoosting { get; private set; }
    public event System.Action<Vector3> BoostStarted;   // direction of the burst
    public Rect MapRect { get; private set; }            // x/z limits
    public float SeaLevel { get; private set; } = float.NegativeInfinity;
    public bool SeaNearby { get; set; } = true;          // false out in space / on the Moon: the city's sea doesn't apply there
    public bool OnWater { get; private set; }           // running across the sea surface at super speed
    public float Bank { get; private set; }             // degrees he leans into a turn while flying (+ = left)
    public float Turbulence01 { get; private set; }     // 0..1 how hard the wind shakes him at speed
    float lastYaw;
    public event System.Action<Vector3> SplashedIntoSea;

    // ---- combat hooks (HeroCombat listens) ----
    public event System.Action PunchStarted;
    public event System.Action KickStarted;
    public bool Blasting { get; private set; }          // heat vision held
    public bool ForceBlast { get; set; }                // tests / touch buttons
    public bool Kicking => kickTimer > 0f;
    /// Holding something overhead (HeroGrab): arms up, no heat vision or punches.
    public bool HoldPose { get; set; }
    float throwTimer;
    bool blastLock;                                     // after a throw, heat vision waits for the mouse to be released

    public void PlayThrow()
    {
        throwTimer = 0.45f;
        blastLock = true;
        PlayUpper("Throw");
    }
    float kickTimer;
    float lungeTime;
    Vector3 lungeVel;

    /// Dash in a straight line (used to close the distance before a punch).
    public void Lunge(Vector3 vel, float time)
    {
        lungeVel = vel; lungeTime = time;
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z).sqrMagnitude > 0.01f ? new Vector3(vel.x, 0, vel.z) : transform.forward);
    }

    public void TriggerPunch()
    {
        if (punchTimer > 0.25f || Blasting || HoldPose || throwTimer > 0f) return;
        punchTimer = 0.9f;
        PlayUpper("Punch");
        PunchStarted?.Invoke();
    }

    public void TriggerKick()
    {
        if (HoldPose || throwTimer > 0f) return;
        if (Current != State.Ground || kickTimer > 0f || !HasState("Kick")) { TriggerPunch(); return; }
        kickTimer = 0.75f;
        planarSpeed = 0f;
        Play("Kick", 0.06f);
        KickStarted?.Invoke();
    }
    Collider seaSurface;
    /// 0..1: how close he is to full super speed on the ground (drives the effects)
    public float SuperSpeed01 => Current == State.Ground || Current == State.Air
        ? Mathf.InverseLerp(sprintSpeed + 3f, superSpeed, new Vector3(velocity.x, 0, velocity.z).magnitude) : 0f;
    public event System.Action<Vector3, Vector3, float> HardLanded;   // position, ground normal, impact speed

    CharacterController cc;
    Vector3 velocity;
    float planarSpeed;
    float airTime;
    float landTimer;
    float lastSpaceTime = -10f;
    float upperWeight;
    float punchTimer;
    float takeoffTimer;
    float lastShiftTime = -10f;
    float animSpeed = 1f;
    string baseAnim;
    string upperAnim;

    static readonly int SpeedParam = Animator.StringToHash("Speed");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!visual && animator) visual = animator.transform;
        if (animator) animator.applyRootMotion = false;
        if (!GetComponent<SuperEffects>()) gameObject.AddComponent<SuperEffects>();
        Energy = maxEnergy;
        MapRect = FindMapRect();
        var sea = GameObject.Find("SeaSurface");
        if (sea && sea.TryGetComponent(out seaSurface)) SeaLevel = seaSurface.bounds.max.y;
    }

    /// The playable area = everything in "Town" and every "District_..." object, shrunk by edgeMargin.
    Rect FindMapRect()
    {
        bool any = false;
        var b = new Bounds();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name != "Town" && !root.name.StartsWith("District_")) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }
        if (!any) b = new Bounds(Vector3.zero, new Vector3(200f, 0f, 200f));
        float m = GameObject.Find("SeaSurface") ? -seaMargin : edgeMargin;
        return Rect.MinMaxRect(b.min.x + m, b.min.z + m, b.max.x - m, b.max.z - m);
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        Vector2 move = GameInput.Move;

        bool spaceDown = GameInput.Down(GameInput.Btn.Jump);
        bool doubleSpace = spaceDown && Time.time - lastSpaceTime < 0.3f;
        if (spaceDown) lastSpaceTime = Time.time;
        bool toggleFly = GameInput.Down(GameInput.Btn.Fly) || doubleSpace;

        // super speed: R, or double-tap Shift
        bool shiftDown = GameInput.Down(GameInput.Btn.Boost);
        if (GameInput.Down(GameInput.Btn.Super) || (shiftDown && Time.time - lastShiftTime < 0.3f)) SuperMode = !SuperMode;
        if (shiftDown) lastShiftTime = Time.time;
        animSpeed = 1f;
        bool wasBoosting = IsBoosting;
        IsBoosting = false;

        UpdateSea();
        if (lungeTime > 0f)
        {
            lungeTime -= Time.deltaTime;
            cc.Move(lungeVel * Time.deltaTime);
            if (lungeTime <= 0f) { planarSpeed = 0f; if (Current == State.Fly) velocity = lungeVel.normalized * 2f; }
        }
        else switch (Current)
        {
            case State.Ground: UpdateGround(kb, move, spaceDown, toggleFly); break;
            case State.Air: UpdateAir(move, toggleFly); break;
            case State.Fly: UpdateFly(kb, move, toggleFly); break;
            case State.Land: UpdateLand(move); break;
        }

        UpdateUpperBody(kb, mouse);
        animator.speed = animSpeed;
        UpdateEnergy();
        lastYaw = transform.eulerAngles.y;
        if (Current != State.Fly) { Bank = 0f; Turbulence01 = 0f; }
        if (IsBoosting && !wasBoosting) BoostStarted?.Invoke(velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward);
        KeepInsideMap();

        // fell through the city's ground: back to the start (out in space / at the Moon, low Y is normal)
        if (SeaNearby && transform.position.y < -100f)
        {
            cc.enabled = false; transform.position = new Vector3(0f, 2f, -6f); cc.enabled = true;
            velocity = Vector3.zero; Current = State.Air;
        }
    }

    // ------------------------------------------------------------------ ground
    void UpdateGround(Keyboard kb, Vector2 move, bool jump, bool toggleFly)
    {
        if (kickTimer > 0f)
        {
            kickTimer -= Time.deltaTime;
            velocity = new Vector3(0f, -2f, 0f);
            cc.Move(GroundStep(velocity));
            if (kickTimer <= 0f) Play("Locomotion", 0.25f);
            animator.SetFloat(SpeedParam, 0f);
            TiltVisual(Quaternion.identity, 10f);
            return;
        }
        Vector3 dir = CameraRelative(move, flatten: true);
        float target = move == Vector2.zero ? 0f
            : SuperMode && CanBoost ? superSpeed
            : GameInput.Held(GameInput.Btn.Boost) ? sprintSpeed
            : GameInput.Held(GameInput.Btn.Walk) ? walkSpeed
            : runSpeed;
        float accel = planarSpeed > sprintSpeed || target > sprintSpeed ? superAcceleration : 25f;
        planarSpeed = Mathf.MoveTowards(planarSpeed, target, accel * Time.deltaTime);

        if (dir.sqrMagnitude > 0.001f)
            FaceDirection(dir, planarSpeed > sprintSpeed ? turnSpeed * 0.7f : turnSpeed);
        Vector3 planar = transform.forward * planarSpeed;
        velocity = new Vector3(planar.x, -2f, planar.z);
        if (SuperMode && target > sprintSpeed && planarSpeed > sprintSpeed + 3f) IsBoosting = true;

        if (toggleFly) { EnterFly(); return; }
        if (jump)
        {
            velocity.y = jumpSpeed;
            Play("JumpUp", 0.1f);
            Current = State.Air;
            airTime = 0f;
            cc.Move(velocity * Time.deltaTime);
            return;
        }

        cc.Move(GroundStep(velocity));
        if (!cc.isGrounded)
        {
            airTime += Time.deltaTime;
            if (airTime > 0.2f) { Current = State.Air; Play("Fall", 0.25f); }
        }
        else airTime = 0f;

        Play("Locomotion", 0.2f);
        // above sprint speed: keep the sprint pose and play it faster (legs become a blur)
        animator.SetFloat(SpeedParam, Mathf.Min(planarSpeed, sprintSpeed), 0.1f, Time.deltaTime);
        float super01 = Mathf.InverseLerp(sprintSpeed, superSpeed, planarSpeed);
        animSpeed = Mathf.Lerp(1f, 2.6f, super01);
        TiltVisual(Quaternion.Euler(superLean * super01, 0, 0), 10f);
    }

    /// One frame of ground movement. The downward part is at least a bit more than the controller's skin width,
    /// otherwise at high frame rates the step is too small to touch the ground and isGrounded flickers off.
    Vector3 GroundStep(Vector3 v)
    {
        Vector3 step = v * Time.deltaTime;
        step.y = -Mathf.Max(-v.y * Time.deltaTime, cc.skinWidth + 0.03f);
        return step;
    }

    // ------------------------------------------------------------------ jump / fall
    void UpdateAir(Vector2 move, bool toggleFly)
    {
        if (toggleFly) { EnterFly(); return; }

        Vector3 dir = CameraRelative(move, flatten: true);
        Vector3 planar = new Vector3(velocity.x, 0, velocity.z);
        planar = Vector3.MoveTowards(planar, dir * Mathf.Max(planarSpeed, runSpeed), 8f * Time.deltaTime);
        if (dir.sqrMagnitude > 0.001f) FaceDirection(dir, turnSpeed * 0.5f);
        velocity.x = planar.x; velocity.z = planar.z;
        velocity.y += gravity * GravityScale * Time.deltaTime;
        airTime += Time.deltaTime;

        if (velocity.y < 0f && baseAnim != "Fall") Play("Fall", 0.3f);

        float fallSpeed = -velocity.y;
        cc.Move(velocity * Time.deltaTime);
        if (cc.isGrounded && velocity.y <= 0f) Touchdown(fallSpeed);
        TiltVisual(Quaternion.identity, 10f);
    }

    // ------------------------------------------------------------------ flight
    void EnterFly()
    {
        bool fromGround = Current == State.Ground;
        Current = State.Fly;
        if (fromGround && HasState("TakeOff"))
        {
            // crouch, then launch upward with a fist raised
            takeoffTimer = 0.55f;
            Play("TakeOff", 0.08f);
        }
        else
        {
            velocity.y = Mathf.Max(velocity.y, 3f);   // little lift-off
            Play("Hover", 0.3f);
        }
    }

    void UpdateFly(Keyboard kb, Vector2 move, bool toggleFly)
    {
        if (toggleFly && CanStopFlying)
        {
            Current = State.Air;
            Play("Fall", 0.3f);
            return;
        }

        if (takeoffTimer > 0f)
        {
            takeoffTimer -= Time.deltaTime;
            // first part of the clip is the crouch; then shoot up
            velocity = takeoffTimer < 0.33f ? Vector3.up * 14f : Vector3.zero;
            cc.Move(velocity * Time.deltaTime);
            TiltVisual(Quaternion.identity, 10f);
            return;
        }

        float vertical = (GameInput.Held(GameInput.Btn.Jump) ? 1f : 0f) - (GameInput.Held(GameInput.Btn.Descend) ? 1f : 0f);
        Vector3 dir = CameraRelative(move, flatten: false);
        bool boost = GameInput.Held(GameInput.Btn.Boost) && (move != Vector2.zero || vertical != 0f) && CanBoost;
        float speed = (boost ? boostSpeed : flySpeed) * SpeedMultiplier;
        if (boost) IsBoosting = true;
        // Space / Ctrl climb and dive; with Shift held they go at full boost speed (straight up to space)
        float climb = boost ? speed : verticalFlySpeed * Mathf.Sqrt(SpeedMultiplier);
        Vector3 target = dir * (move == Vector2.zero ? 0f : speed) + Vector3.up * vertical * climb;
        if (target.magnitude > speed && boost) target = target.normalized * speed;

        float accel = boost ? flyAcceleration * 1.6f
            : target.magnitude > velocity.magnitude ? flyAcceleration : flyAcceleration * 1.5f;
        if (GameInput.TouchActive && target.sqrMagnitude > 1f && velocity.sqrMagnitude > 4f)
        {
            // steer: swing the direction of travel round quickly, then ease the speed (tight turns at any speed)
            float turnDeg = Mathf.Lerp(220f, 140f, Mathf.InverseLerp(flySpeed, boostSpeed, velocity.magnitude)) * Time.deltaTime;
            Vector3 heading = Vector3.RotateTowards(velocity.normalized, target.normalized, turnDeg * Mathf.Deg2Rad, 0f);
            float mag = Mathf.Lerp(velocity.magnitude, target.magnitude, 1f - Mathf.Exp(-accel * Time.deltaTime));
            velocity = heading * mag;
        }
        else velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-accel * Time.deltaTime));

        cc.Move(velocity * Time.deltaTime);

        Vector3 planar = new Vector3(velocity.x, 0, velocity.z);
        if (velocity.magnitude > flyAnimThreshold)
        {
            // both fists forward when going really fast
            Play(velocity.magnitude > flySpeed * 1.3f && HasState("FlyFast") ? "FlyFast" : "Fly", 0.35f);
            if (planar.sqrMagnitude > 0.5f) FaceDirection(planar, GameInput.TouchActive ? 10f : 4f);
            // nose follows the climb / dive angle (the Fly clip is already lying horizontal)
            float pitch = Mathf.Atan2(velocity.y, planar.magnitude) * Mathf.Rad2Deg;
            // bank into turns like a jet: roll toward the inside of the turn, more at higher speed
            float yawRate = Mathf.DeltaAngle(lastYaw, transform.eulerAngles.y) / Mathf.Max(Time.deltaTime, 0.0001f);
            float speed01 = Mathf.InverseLerp(flyAnimThreshold, boostSpeed, velocity.magnitude);
            float targetBank = Mathf.Clamp(-yawRate * Mathf.Lerp(0.25f, 0.55f, speed01), -60f, 60f);
            Bank = Mathf.Lerp(Bank, targetBank, 1f - Mathf.Exp(-4f * Time.deltaTime));

            // wind turbulence: small fast wobble of the body that grows with speed
            Turbulence01 = Mathf.InverseLerp(flySpeed, boostSpeed, velocity.magnitude);
            float tt = Time.time;
            float wobRoll = (Mathf.PerlinNoise(tt * 7f, 1.3f) - 0.5f) * 10f * Turbulence01 + Mathf.Sin(tt * 2.1f) * 2.5f * speed01;
            float wobPitch = (Mathf.PerlinNoise(2.7f, tt * 6f) - 0.5f) * 6f * Turbulence01 + Mathf.Sin(tt * 1.4f) * 1.5f;

            TiltVisual(Quaternion.Euler(-Mathf.Clamp(pitch, -80f, 80f) + wobPitch, 0, Bank + wobRoll), 8f);
        }
        else
        {
            Bank = Mathf.Lerp(Bank, 0f, 1f - Mathf.Exp(-4f * Time.deltaTime));
            Turbulence01 = 0f;
            Play("Hover", 0.4f);
            Vector3 camFlat = CameraRelative(Vector2.up, flatten: true);
            FaceDirection(camFlat, 3f);
            TiltVisual(Quaternion.identity, 5f);
        }

        KeepFlightClearance();
    }

    /// Flying low skims over the ground and the water instead of landing or sinking.
    /// Only a steep, fast dive onto land ends the flight (superhero landing); F lands normally.
    void KeepFlightClearance()
    {
        int mask = ~((1 << 2) | (1 << 3));            // not the hero / debris, not bodies
        Vector3 p = transform.position;
        float floor = float.NegativeInfinity;
        bool water = false;
        if (Physics.SphereCast(p + Vector3.up * 1.5f, 0.3f, Vector3.down, out var gh, 1.5f + flyClearance + 3f, mask, QueryTriggerInteraction.Ignore))
        {
            floor = gh.point.y;
            water = seaSurface && gh.collider == seaSurface;
        }
        if (seaSurface && SeaNearby && SeaLevel > floor) { floor = SeaLevel; water = true; }
        if (float.IsNegativeInfinity(floor)) return;

        float gap = p.y - floor;
        bool steepDive = velocity.y < -slamSpeed && -velocity.y > 0.7f * velocity.magnitude;
        if (!water && steepDive && gap < flyClearance + 0.4f)
        {
            Touchdown(velocity.magnitude);
            return;
        }
        if (gap < flyClearance)
        {
            cc.Move(Vector3.up * (flyClearance - gap));
            if (velocity.y < 0f) velocity.y = 0f;
        }
    }

    // ------------------------------------------------------------------ sea
    void UpdateSea()
    {
        if (!seaSurface || !SeaNearby) return;
        Vector3 p = transform.position;
        // the water is only solid while he runs at super speed
        bool canRunOnWater = Current == State.Ground && planarSpeed > sprintSpeed + 6f && p.y > SeaLevel - 0.2f;
        Physics.IgnoreCollision(cc, seaSurface, !canRunOnWater);
        OnWater = canRunOnWater && p.y < SeaLevel + 0.25f;

        // fell in: splash, then Superman lifts himself out of the water
        if (p.y < SeaLevel - 0.7f && Current != State.Fly)
        {
            SplashedIntoSea?.Invoke(new Vector3(p.x, SeaLevel, p.z));
            Current = State.Fly;
            takeoffTimer = 0f;
            velocity = Vector3.up * 9f;
            Play("Hover", 0.2f);
        }
    }

    // ------------------------------------------------------------------ energy
    bool CanBoost => true;                 // no energy limit any more
    float sinceBoost;

    void UpdateEnergy()
    {
        // energy is unlimited now (the bar was removed): boost and super speed never run out
        Energy = maxEnergy;
        Exhausted = false;
        if (IsBoosting) sinceBoost = 0f; else sinceBoost += Time.deltaTime;
    }

    // ------------------------------------------------------------------ map edge
    void KeepInsideMap()
    {
        Vector3 p = transform.position;
        if (p.y > MapEdgeCeiling) return;                // above the clouds / in space: no walls
        float x = Mathf.Clamp(p.x, MapRect.xMin, MapRect.xMax);
        float z = Mathf.Clamp(p.z, MapRect.yMin, MapRect.yMax);
        if (x == p.x && z == p.z) return;
        cc.enabled = false;
        transform.position = new Vector3(x, p.y, z);
        cc.enabled = true;
        // stop moving into the wall, keep sliding along it
        if (x != p.x) { velocity.x = 0f; }
        if (z != p.z) { velocity.z = 0f; }
        if (Current == State.Ground) planarSpeed = Mathf.Min(planarSpeed, new Vector3(velocity.x, 0, velocity.z).magnitude);
    }

    /// 0 = far from the edge, 1 = touching it (for the wall effect)
    public float EdgeProximity(out Vector3 nearestEdgePoint)
    {
        Vector3 p = transform.position;
        float dl = p.x - MapRect.xMin, dr = MapRect.xMax - p.x, db = p.z - MapRect.yMin, df = MapRect.yMax - p.z;
        float d = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, df));
        nearestEdgePoint = d == dl ? new Vector3(MapRect.xMin, p.y, p.z) : d == dr ? new Vector3(MapRect.xMax, p.y, p.z)
            : d == db ? new Vector3(p.x, p.y, MapRect.yMin) : new Vector3(p.x, p.y, MapRect.yMax);
        return Mathf.Clamp01(1f - d / 12f);
    }

    // ------------------------------------------------------------------ landing
    void Touchdown(float impactSpeed)
    {
        if (impactSpeed > hardLandingSpeed)
        {
            Vector3 n = Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 3f,
                ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.normal : Vector3.up;
            Vector3 p = hit.collider ? hit.point : transform.position;
            HardLanded?.Invoke(p, n, impactSpeed);
        }
        velocity = Vector3.zero;
        planarSpeed = 0f;
        TiltVisual(Quaternion.identity, 100f);
        if (impactSpeed > hardLandingSpeed)
        {
            Current = State.Land;
            landTimer = 1.1f;
            Play("Land", 0.1f);
        }
        else
        {
            Current = State.Ground;
            Play("Locomotion", 0.2f);
        }
    }

    void UpdateLand(Vector2 move)
    {
        landTimer -= Time.deltaTime;
        cc.Move(Vector3.down * 2f * Time.deltaTime);
        // after the impact part of the animation the player may run out of it
        if (landTimer <= 0f || (landTimer < 0.5f && move != Vector2.zero))
        {
            Current = State.Ground;
            Play("Locomotion", 0.35f);
        }
    }

    // ------------------------------------------------------------------ powers (upper body layer)
    void UpdateUpperBody(Keyboard kb, Mouse mouse)
    {
        bool fireHeld = GameInput.Held(GameInput.Btn.Fire);
        if (blastLock && !fireHeld) blastLock = false;
        throwTimer -= Time.deltaTime;
        bool blasting = !HoldPose && throwTimer <= 0f && !blastLock && (ForceBlast || fireHeld);
        Blasting = blasting;
        if (GameInput.Down(GameInput.Btn.Punch)) TriggerPunch();
        if (GameInput.Down(GameInput.Btn.Kick)) TriggerKick();
        punchTimer -= Time.deltaTime;

        if (blasting)
        {
            PlayUpper("Blast");
            if (Current != State.Fly || velocity.magnitude < flyAnimThreshold)
                FaceDirection(CameraRelative(Vector2.up, flatten: true), 15f);   // aim where the camera looks
        }
        if (HoldPose && throwTimer <= 0f) PlayUpper("Lift");
        bool active = blasting || punchTimer > 0f || HoldPose || throwTimer > 0f;
        if (!active && upperAnim != "Empty") PlayUpper("Empty");

        upperWeight = Mathf.MoveTowards(upperWeight, active ? 1f : 0f, 6f * Time.deltaTime);
        animator.SetLayerWeight(1, upperWeight);
    }

    // ------------------------------------------------------------------ helpers
    Vector3 CameraRelative(Vector2 input, bool flatten)
    {
        Transform cam = cameraTransform ? cameraTransform : transform;
        Vector3 f = cam.forward, r = cam.right;
        if (flatten) { f.y = 0; r.y = 0; f.Normalize(); r.Normalize(); }
        Vector3 d = f * input.y + r * input.x;
        return d.sqrMagnitude > 1f ? d.normalized : d;
    }

    void FaceDirection(Vector3 dir, float speed)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;
        var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    void TiltVisual(Quaternion local, float speed)
    {
        if (!visual) return;
        visual.localRotation = Quaternion.Slerp(visual.localRotation, local, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    bool HasState(string state) => animator && animator.HasState(0, Animator.StringToHash(state));

    void Play(string state, float fade)
    {
        if (baseAnim == state) return;
        baseAnim = state;
        animator.CrossFadeInFixedTime(state, fade, 0);
    }

    void PlayUpper(string state)
    {
        if (upperAnim == state) return;
        upperAnim = state;
        animator.CrossFadeInFixedTime(state, 0.15f, 1);
    }
}
