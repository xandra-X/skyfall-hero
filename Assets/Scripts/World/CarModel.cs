using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds car bodies from side-profile outlines: a lower body (bumpers, hood, trunk) and a glass cabin
/// that narrows toward the roof, plus roof, pillars, wheels, lights, grille, mirrors and plates.
/// Body meshes are shared between all cars of the same type.
/// </summary>
public static class CarModel
{
    public enum Type { Sedan, Hatchback, Suv, Van, Pickup, Taxi }

    public struct Spec
    {
        public Vector2[] lower, cabin;   // (z forward, y up)
        public float halfWidth, cabinBottomHalf, cabinTopHalf, wheelRadius, wheelBase;
        public bool cabinIsBody;         // van: the cabin is painted metal with separate windows
    }

    public class Result
    {
        public List<Transform> wheels = new List<Transform>();
        public Bounds bounds;
        public float mass;
    }

    static Vector2 V(float z, float y) => new Vector2(z, y);

    public static Spec Get(Type t)
    {
        switch (t)
        {
            case Type.Hatchback:
                return new Spec
                {
                    lower = new[] { V(-2.05f, .32f), V(2.03f, .32f), V(2.09f, .50f), V(2.05f, .68f), V(1.80f, .80f), V(1.00f, .93f), V(-1.95f, .98f), V(-2.07f, .82f), V(-2.09f, .55f) },
                    cabin = new[] { V(1.05f, .90f), V(.25f, 1.42f), V(-1.62f, 1.40f), V(-1.95f, .94f) },
                    halfWidth = .87f, cabinBottomHalf = .82f, cabinTopHalf = .64f, wheelRadius = .33f, wheelBase = 1.30f,
                };
            case Type.Suv:
                return new Spec
                {
                    lower = new[] { V(-2.35f, .42f), V(2.33f, .42f), V(2.39f, .65f), V(2.35f, .90f), V(2.10f, 1.05f), V(1.30f, 1.14f), V(-2.25f, 1.16f), V(-2.39f, .98f), V(-2.39f, .65f) },
                    cabin = new[] { V(1.35f, 1.12f), V(.55f, 1.74f), V(-2.02f, 1.76f), V(-2.25f, 1.15f) },
                    halfWidth = .97f, cabinBottomHalf = .92f, cabinTopHalf = .76f, wheelRadius = .41f, wheelBase = 1.45f,
                };
            case Type.Van:
                return new Spec
                {
                    lower = new[] { V(-2.50f, .38f), V(2.48f, .38f), V(2.54f, .60f), V(2.50f, .86f), V(2.30f, 1.02f), V(-2.48f, 1.05f), V(-2.54f, .62f) },
                    cabin = new[] { V(2.30f, 1.00f), V(1.62f, 1.95f), V(1.35f, 2.22f), V(-2.42f, 2.24f), V(-2.50f, 1.03f) },
                    halfWidth = .98f, cabinBottomHalf = .97f, cabinTopHalf = .90f, wheelRadius = .37f, wheelBase = 1.60f, cabinIsBody = true,
                };
            case Type.Pickup:
                return new Spec
                {
                    lower = new[] { V(-2.55f, .44f), V(2.53f, .44f), V(2.59f, .68f), V(2.55f, .92f), V(2.30f, 1.07f), V(1.45f, 1.15f), V(-2.50f, 1.16f), V(-2.59f, .98f), V(-2.59f, .66f) },
                    cabin = new[] { V(1.45f, 1.13f), V(.75f, 1.76f), V(-.45f, 1.78f), V(-.70f, 1.14f) },
                    halfWidth = .98f, cabinBottomHalf = .93f, cabinTopHalf = .78f, wheelRadius = .42f, wheelBase = 1.65f,
                };
            default: // Sedan, Taxi
                return new Spec
                {
                    lower = new[] { V(-2.30f, .32f), V(2.28f, .32f), V(2.34f, .50f), V(2.30f, .68f), V(2.05f, .80f), V(1.20f, .92f), V(-1.50f, .98f), V(-2.10f, .95f), V(-2.30f, .80f), V(-2.34f, .55f) },
                    cabin = new[] { V(1.25f, .90f), V(.40f, 1.40f), V(-.90f, 1.42f), V(-1.60f, .93f) },
                    halfWidth = .90f, cabinBottomHalf = .85f, cabinTopHalf = .66f, wheelRadius = .34f, wheelBase = 1.42f,
                };
        }
    }

    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    public static Result Build(Transform model, Type type, Color paintColor)
    {
        var s = Get(type);
        var res = new Result();
        var paint = Palette.Lit(paintColor, 0.75f);
        var glass = Palette.Lit(new Color(0.06f, 0.08f, 0.1f), 0.95f);
        var trim = Palette.Lit(new Color(0.08f, 0.08f, 0.09f), 0.35f);
        var chrome = Palette.Lit(new Color(0.75f, 0.76f, 0.78f), 0.9f);
        var tyre = Palette.Lit(new Color(0.04f, 0.04f, 0.04f), 0.15f);
        var plate = Palette.Lit(new Color(0.92f, 0.92f, 0.88f), 0.4f);
        var head = Palette.Glow(new Color(1f, 0.96f, 0.85f));
        var tail = Palette.Glow(new Color(1f, 0.06f, 0.04f));

        // ---- body + cabin
        string key = type == Type.Taxi ? Type.Sedan.ToString() : type.ToString();
        MeshPart(model, ProfileMesh(key + "_lower", s.lower, s.halfWidth, s.halfWidth * 0.97f), paint, "Body");
        MeshPart(model, ProfileMesh(key + "_cabin", s.cabin, s.cabinBottomHalf, s.cabinTopHalf), s.cabinIsBody ? paint : glass, "Cabin");

        // roof panel + pillars (glass cabins only)
        Vector2 cb0 = s.cabin[0], wTop = s.cabin[1], rTop = s.cabin[s.cabin.Length - 2], cbN = s.cabin[s.cabin.Length - 1];
        float topY = Mathf.Max(wTop.y, rTop.y);
        if (!s.cabinIsBody)
        {
            Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, topY + 0.02f, (wTop.x + rTop.x) / 2f),
                new Vector3(s.cabinTopHalf * 2f + 0.03f, 0.05f, Mathf.Abs(wTop.x - rTop.x) + 0.04f), paint, "Roof");
            foreach (float side in new[] { -1f, 1f })
            {
                float xb = side * (s.cabinBottomHalf + 0.005f), xt = side * (s.cabinTopHalf + 0.005f);
                Strut(model, new Vector3(xb, cb0.y, cb0.x), new Vector3(xt, wTop.y, wTop.x), 0.07f, paint, "PillarA");
                Strut(model, new Vector3(xb, cbN.y, cbN.x), new Vector3(xt, rTop.y, rTop.x), 0.1f, paint, "PillarC");
                if (type != Type.Pickup)
                {
                    float zb = Mathf.Lerp(cb0.x, cbN.x, 0.42f);
                    Strut(model, new Vector3(xb, cb0.y, zb), new Vector3(xt, topY, zb), 0.07f, paint, "PillarB");
                }
            }
        }
        else
        {
            // van: windscreen and front side windows on the painted box
            Vector3 a = new Vector3(0f, cb0.y + 0.08f, cb0.x - 0.02f), b = new Vector3(0f, wTop.y - 0.05f, wTop.x - 0.02f);
            var ws = Strut(model, a, b, 0.05f, glass, "Windscreen");
            ws.localScale = new Vector3(s.cabinBottomHalf * 1.85f, ws.localScale.y, 0.03f);
            foreach (float side in new[] { -1f, 1f })
                Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.cabinBottomHalf + 0.005f), 1.55f, 1.05f), new Vector3(0.02f, 0.55f, 0.85f), glass, "SideWindow");
        }

        if (type == Type.Taxi)
        {
            Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, topY + 0.15f, (wTop.x + rTop.x) / 2f), new Vector3(0.75f, 0.2f, 0.28f), Palette.Glow(new Color(1f, 0.92f, 0.55f)), "TaxiSign");
            foreach (float side in new[] { -1f, 1f })
                Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth + 0.004f), 0.62f, 0f), new Vector3(0.01f, 0.08f, 2.6f), trim, "Checker");
        }
        if (type == Type.Pickup)
        {
            float bedTop = 1.16f, z0 = cbN.x - 0.1f, z1 = s.lower[0].x + 0.05f;
            foreach (float side in new[] { -1f, 1f })
                Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth - 0.04f), bedTop + 0.17f, (z0 + z1) / 2f), new Vector3(0.08f, 0.34f, z0 - z1), paint, "BedWall");
            Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bedTop + 0.17f, z1 + 0.04f), new Vector3(s.halfWidth * 2f - 0.05f, 0.34f, 0.08f), paint, "Tailgate");
            Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bedTop + 0.02f, (z0 + z1) / 2f), new Vector3(s.halfWidth * 2f - 0.16f, 0.03f, z0 - z1 - 0.1f), trim, "BedFloor");
        }

        // ---- front / back details
        Vector2 front = s.lower[2], rear = s.lower[s.lower.Length - 1];
        float fz = front.x + 0.02f, rz = rear.x - 0.02f, bumperY = s.lower[0].y + 0.12f;
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bumperY, fz), new Vector3(s.halfWidth * 2f + 0.04f, 0.2f, 0.16f), trim, "BumperF");
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bumperY, rz), new Vector3(s.halfWidth * 2f + 0.04f, 0.2f, 0.16f), trim, "BumperR");
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, front.y + 0.06f, fz), new Vector3(s.halfWidth * 0.95f, 0.16f, 0.05f), trim, "Grille");
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, front.y + 0.06f, fz + 0.02f), new Vector3(s.halfWidth * 0.95f, 0.02f, 0.02f), chrome, "GrilleBar");
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bumperY, fz + 0.09f), new Vector3(0.48f, 0.12f, 0.02f), plate, "PlateF");
        Palette.Part(PrimitiveType.Cube, model, new Vector3(0f, bumperY + 0.2f, rz - 0.01f), new Vector3(0.48f, 0.12f, 0.02f), plate, "PlateR");
        foreach (float side in new[] { -1f, 1f })
        {
            Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth - 0.22f), front.y + 0.07f, fz - 0.01f), new Vector3(0.34f, 0.13f, 0.06f), head, "Headlight");
            Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth - 0.16f), rear.y + 0.1f, rz + 0.01f), new Vector3(0.28f, 0.16f, 0.06f), tail, "Taillight");
            // mirror
            Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.cabinBottomHalf + 0.1f), cb0.y + 0.08f, cb0.x - 0.15f), new Vector3(0.14f, 0.1f, 0.16f), paint, "Mirror");
            // dark sill along the bottom and a door seam
            Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth + 0.003f), s.lower[0].y + 0.06f, 0f), new Vector3(0.01f, 0.12f, s.wheelBase * 2f - s.wheelRadius * 2.2f), trim, "Sill");
            float seamZ = Mathf.Lerp(cb0.x, cbN.x, 0.42f);
            if (!s.cabinIsBody)
                Palette.Part(PrimitiveType.Cube, model, new Vector3(side * (s.halfWidth + 0.004f), (s.lower[0].y + cb0.y) / 2f + 0.05f, seamZ), new Vector3(0.006f, cb0.y - s.lower[0].y - 0.1f, 0.012f), trim, "DoorSeam");
            // wheels: dark arch behind, tyre, rim, hub
            foreach (float z in new[] { s.wheelBase, -s.wheelBase })
            {
                float r = s.wheelRadius;
                Palette.Part(PrimitiveType.Cylinder, model, new Vector3(side * (s.halfWidth + 0.002f), r, z), new Vector3(r * 2.35f, 0.004f, r * 2.35f), trim, "Arch", Quaternion.Euler(0f, 0f, 90f));
                var w = Palette.Pivot(model, new Vector3(side * (s.halfWidth - 0.1f), r, z), "Wheel");
                Palette.Part(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(r * 2f, 0.125f, r * 2f), tyre, "Tyre", Quaternion.Euler(0f, 0f, 90f));
                Palette.Part(PrimitiveType.Cylinder, w, new Vector3(side * 0.115f, 0f, 0f), new Vector3(r * 1.25f, 0.015f, r * 1.25f), chrome, "Rim", Quaternion.Euler(0f, 0f, 90f));
                Palette.Part(PrimitiveType.Cube, w, new Vector3(side * 0.125f, 0f, 0f), new Vector3(0.02f, r * 1.1f, 0.08f), trim, "Spoke");
                Palette.Part(PrimitiveType.Cube, w, new Vector3(side * 0.125f, 0f, 0f), new Vector3(0.02f, 0.08f, r * 1.1f), trim, "Spoke");
                res.wheels.Add(w);
            }
        }

        var bb = new Bounds(new Vector3(0f, topY / 2f, 0f), Vector3.zero);
        foreach (var p in s.lower) bb.Encapsulate(new Vector3(s.halfWidth, p.y, p.x));
        foreach (var p in s.lower) bb.Encapsulate(new Vector3(-s.halfWidth, p.y, p.x));
        bb.Encapsulate(new Vector3(0f, topY + 0.05f, 0f));
        bb.Encapsulate(new Vector3(0f, 0.05f, 0f));
        res.bounds = bb;
        res.mass = type == Type.Van ? 2200f : type == Type.Suv || type == Type.Pickup ? 2000f : type == Type.Hatchback ? 1150f : 1400f;
        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return res;
    }

    static void MeshPart(Transform parent, Mesh mesh, Material mat, string name)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// A thin box from a to b.
    static Transform Strut(Transform parent, Vector3 a, Vector3 b, float thickness, Material mat, string name)
    {
        Vector3 d = b - a;
        return Palette.Part(PrimitiveType.Cube, parent, (a + b) / 2f, new Vector3(thickness, d.magnitude, thickness), mat, name,
            Quaternion.FromToRotation(Vector3.up, d.normalized));
    }

    /// Extrude a convex side profile (z, y) across the car, half-width shrinking from wBottom to wTop with height.
    static Mesh ProfileMesh(string key, Vector2[] prof, float wBottom, float wTop)
    {
        if (cache.TryGetValue(key, out var cached) && cached) return cached;
        float ymin = float.MaxValue, ymax = float.MinValue;
        foreach (var p in prof) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
        float W(float y) => Mathf.Lerp(wBottom, wTop, Mathf.InverseLerp(ymin, ymax, y));
        Vector3 P(Vector2 p, float side) => new Vector3(side * W(p.y), p.y, p.x);

        var verts = new List<Vector3>(); var tris = new List<int>();
        Vector3 center = Vector3.zero;
        foreach (var p in prof) center += new Vector3(0f, p.y, p.x);
        center /= prof.Length;

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f) { var t = b; b = c; c = t; }   // face outward
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        int n = prof.Length;
        for (int i = 0; i < n; i++)                       // band around the outline
        {
            Vector2 a = prof[i], b = prof[(i + 1) % n];
            Tri(P(a, 1f), P(b, 1f), P(b, -1f));
            Tri(P(a, 1f), P(b, -1f), P(a, -1f));
        }
        foreach (float side in new[] { -1f, 1f })        // the two sides
        {
            Vector3 c = Vector3.zero;
            foreach (var p in prof) c += P(p, side);
            c /= n;
            for (int i = 0; i < n; i++) Tri(c, P(prof[i], side), P(prof[(i + 1) % n], side));
        }
        var mesh = new Mesh { name = key };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        cache[key] = mesh;
        return mesh;
    }
}
