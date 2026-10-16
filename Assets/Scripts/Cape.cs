using System.Collections;
using UnityEngine;

/// <summary>
/// Superman's cape: a cloth that hangs from his shoulders, swings as he walks and, when he flies, streams out
/// behind him and flaps like a flag in the wind (faster = straighter and wilder).
/// Built at the start of play from his standing pose (the cloth is pinned along the top to his upper chest),
/// with invisible capsules on his body and legs so it drapes over him instead of passing through.
/// </summary>
public class Cape : MonoBehaviour
{
    public Material material;
    public int columns = 12, rows = 18;
    public float topWidthScale = 1.15f;     // relative to the gap between his upper arms
    public float bottomWidth = 0.95f;
    public float length = 1.25f;

    public Cloth Cloth { get; private set; }
    public SkinnedMeshRenderer Renderer { get; private set; }

    HeroController hero;
    Animator anim;
    Vector3 lastPos;
    float flutterSeed;

    IEnumerator Start()
    {
        hero = GetComponent<HeroController>();
        anim = GetComponentInChildren<Animator>();
        if (!anim || !anim.isHuman || !material) yield break;
        yield return null;                          // let the animator put him in his standing pose first
        yield return null;
        Build();
        lastPos = transform.position;
        flutterSeed = Random.value * 100f;
    }

    Transform Bone(HumanBodyBones b) => anim.GetBoneTransform(b);

    void Build()
    {
        Transform chest = Bone(HumanBodyBones.UpperChest) ? Bone(HumanBodyBones.UpperChest) : Bone(HumanBodyBones.Chest);
        Transform lArm = Bone(HumanBodyBones.LeftUpperArm), rArm = Bone(HumanBodyBones.RightUpperArm);
        Transform neck = Bone(HumanBodyBones.Neck);
        if (!chest || !lArm || !rArm || !neck) return;

        var go = new GameObject("Cape");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);

        // the top edge runs across his back between the shoulders, a little below the neck and behind the body
        Vector3 back = -transform.forward;
        Vector3 right = transform.right;
        Vector3 up = Vector3.up;
        float shoulderSpan = Vector3.Distance(lArm.position, rArm.position);
        float topW = shoulderSpan * topWidthScale;
        Vector3 topCentre = Vector3.Lerp((lArm.position + rArm.position) * 0.5f, neck.position, 0.25f) + back * 0.11f + up * 0.02f;

        int cols = columns + 1, rws = rows + 1;
        var verts = new Vector3[cols * rws];
        var uvs = new Vector2[verts.Length];
        for (int r = 0; r < rws; r++)
        {
            float v = r / (float)rows;
            float w = Mathf.Lerp(topW, bottomWidth, Mathf.SmoothStep(0f, 1f, v));
            for (int c = 0; c < cols; c++)
            {
                float u = c / (float)columns;
                float x = (u - 0.5f) * w;
                float hang = v * length + (1f - Mathf.Pow(2f * u - 1f, 2f)) * 0.06f * v;        // middle a bit longer
                // the top wraps round the shoulders a little; lower down it stands off the back
                float wrap = r == 0 ? Mathf.Pow(2f * u - 1f, 2f) * 0.07f : 0f;
                Vector3 p = topCentre + right * x - up * hang + back * (0.02f + v * 0.12f) - back * wrap;
                verts[r * cols + c] = go.transform.InverseTransformPoint(p);
                uvs[r * cols + c] = new Vector2(u, 1f - v);
            }
        }
        var tris = new int[columns * rows * 6];
        int t = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int i = r * cols + c;
                tris[t++] = i; tris[t++] = i + 1; tris[t++] = i + cols;
                tris[t++] = i + 1; tris[t++] = i + cols + 1; tris[t++] = i + cols;
            }
        var mesh = new Mesh { name = "CapeMesh" };
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        var bw = new BoneWeight[verts.Length];
        for (int i = 0; i < bw.Length; i++) bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = bw;
        mesh.bindposes = new[] { chest.worldToLocalMatrix * go.transform.localToWorldMatrix };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = new[] { chest };
        smr.rootBone = chest;
        smr.sharedMaterial = material;
        smr.updateWhenOffscreen = true;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Renderer = smr;

        var cloth = go.AddComponent<Cloth>();
        var co = new ClothSkinningCoefficient[verts.Length];
        for (int r = 0; r < rws; r++)
            for (int c = 0; c < cols; c++)
            {
                // top row pinned to his shoulders; lower down it may swing further, but never round to his front
                co[r * cols + c].maxDistance = r == 0 ? 0f : 0.1f + r / (float)rows * 0.85f;
                co[r * cols + c].collisionSphereDistance = r == 0 ? float.MaxValue : 0.02f;
            }
        cloth.coefficients = co;
        cloth.stretchingStiffness = 0.95f;
        cloth.bendingStiffness = 0.15f;
        cloth.damping = 0.12f;
        cloth.friction = 0.4f;
        cloth.useGravity = true;
        cloth.worldVelocityScale = 0.5f;
        cloth.worldAccelerationScale = 0.6f;
        cloth.useVirtualParticles = 1f;
        cloth.clothSolverFrequency = 120f;
        cloth.sphereColliders = BodyColliders();
        Cloth = cloth;
    }

    /// Tapered capsules round his back and legs (ball pairs) so the cloth drapes over him.
    ClothSphereColliderPair[] BodyColliders()
    {
        SphereCollider Ball(HumanBodyBones b, float radius, Vector3 offset = default)
        {
            var bone = Bone(b);
            if (!bone) return null;
            var g = new GameObject("CapeCollider_" + b);
            g.layer = gameObject.layer;
            g.transform.SetParent(bone, false);
            g.transform.position = bone.position + offset;
            var s = g.AddComponent<SphereCollider>();
            s.radius = radius / Mathf.Max(bone.lossyScale.x, 0.0001f);
            s.isTrigger = true;                                       // only the cloth feels it
            return s;
        }
        Vector3 back = -transform.forward * 0.03f;
        var hips = Ball(HumanBodyBones.Hips, 0.17f, back);
        var spine = Ball(HumanBodyBones.Spine, 0.16f, back);
        var chest = Ball(HumanBodyBones.UpperChest, 0.18f, back) ?? Ball(HumanBodyBones.Chest, 0.18f, back);
        var neck = Ball(HumanBodyBones.Neck, 0.1f);
        var lUp = Ball(HumanBodyBones.LeftUpperLeg, 0.11f); var lLow = Ball(HumanBodyBones.LeftLowerLeg, 0.08f); var lFoot = Ball(HumanBodyBones.LeftFoot, 0.06f);
        var rUp = Ball(HumanBodyBones.RightUpperLeg, 0.11f); var rLow = Ball(HumanBodyBones.RightLowerLeg, 0.08f); var rFoot = Ball(HumanBodyBones.RightFoot, 0.06f);
        var lArm = Ball(HumanBodyBones.LeftUpperArm, 0.07f); var rArm = Ball(HumanBodyBones.RightUpperArm, 0.07f);
        return new[]
        {
            new ClothSphereColliderPair(hips, spine), new ClothSphereColliderPair(spine, chest), new ClothSphereColliderPair(chest, neck),
            new ClothSphereColliderPair(lUp, lLow), new ClothSphereColliderPair(lLow, lFoot),
            new ClothSphereColliderPair(rUp, rLow), new ClothSphereColliderPair(rLow, rFoot),
            new ClothSphereColliderPair(chest, lArm), new ClothSphereColliderPair(chest, rArm),
        };
    }

    void LateUpdate()
    {
        if (!Cloth || !hero) return;
        Vector3 v = hero.Velocity;
        float speed = v.magnitude;
        float flying = hero.Current == HeroController.State.Fly ? 1f : 0f;

        // a sudden jump (teleport, the Moon turning under him...) must not fling the cloth
        Vector3 moved = transform.position - lastPos;
        lastPos = transform.position;
        if (moved.magnitude > Mathf.Max(8f, speed * Time.deltaTime * 3f)) Cloth.ClearTransformMotion();

        // the air: walking it just swings; flying fast the wind blows it straight back and makes it snap like a flag
        float wind = Mathf.Min(speed, 90f);
        Cloth.worldVelocityScale = speed > 40f ? 0.02f : 0.5f;                   // at huge speeds, wind does the work
        Cloth.worldAccelerationScale = speed > 40f ? 0.1f : 0.6f;
        Vector3 airflow = speed > 0.5f ? -v / speed : Vector3.zero;
        float gust = Mathf.PerlinNoise(Time.time * 1.7f, flutterSeed);
        Cloth.externalAcceleration = airflow * wind * (0.9f + 0.4f * gust) * Mathf.Lerp(0.6f, 1.2f, flying)
                                     + (flying > 0f ? Vector3.up * Mathf.Min(wind, 20f) * 0.35f : Vector3.zero)   // keeps it off his legs
                                     + transform.right * (gust - 0.5f) * 0.4f;                                    // a little breeze
        Cloth.randomAcceleration = Vector3.one * (0.6f + Mathf.Min(speed, 120f) * (0.18f + 0.1f * flying));
    }
}
