using UnityEngine;

/// <summary>Sound effects generated in code (no audio files needed).</summary>
public static class Sfx
{
    const int Rate = 44100;
    static AudioClip laser, impact, crumble, clank, whoosh, steam, sizzle;

    public static AudioClip Laser => laser ? laser : laser = Make("Laser", 1f, LaserGen, true);
    public static AudioClip Impact => impact ? impact : impact = Make("Impact", 1.1f, ImpactGen, false);
    public static AudioClip Crumble => crumble ? crumble : crumble = Make("Crumble", 5f, CrumbleGen, false);
    public static AudioClip Clank => clank ? clank : clank = Make("Clank", 0.9f, ClankGen, false);
    public static AudioClip Whoosh => whoosh ? whoosh : whoosh = Make("Whoosh", 0.35f, WhooshGen, false);
    public static AudioClip Steam => steam ? steam : steam = Make("Steam", 1f, SteamGen, true);
    public static AudioClip Sizzle => sizzle ? sizzle : sizzle = Make("Sizzle", 1f, SizzleGen, true);

    static AudioClip wind, rush, step, engine, explosion, scream, splat, crunch, jet, splash;
    public static AudioClip Wind => wind ? wind : wind = Make("Wind", 2f, WindGen, true);
    static AudioClip flight, boostTear;
    public static AudioClip Flight => flight ? flight : flight = Make("Flight", 2f, FlightGen, true);
    public static AudioClip BoostTear => boostTear ? boostTear : boostTear = Make("BoostTear", 1f, BoostTearGen, true);
    public static AudioClip Rush => rush ? rush : rush = Make("Rush", 1f, RushGen, true);
    public static AudioClip Step => step ? step : step = Make("Step", 0.18f, StepGen, false);
    public static AudioClip Engine => engine ? engine : engine = Make("Engine", 1f, EngineGen, true);
    public static AudioClip Explosion => explosion ? explosion : explosion = Make("Explosion", 2.6f, ExplosionGen, false);
    public static AudioClip Scream => scream ? scream : scream = Make("Scream", 1.3f, ScreamGen, false);
    public static AudioClip Splat => splat ? splat : splat = Make("Splat", 0.5f, SplatGen, false);
    public static AudioClip Crunch => crunch ? crunch : crunch = Make("Crunch", 0.8f, CrunchGen, false);
    public static AudioClip Jet => jet ? jet : jet = Make("Jet", 2f, JetGen, true);
    public static AudioClip Splash => splash ? splash : splash = Make("Splash", 1.2f, SplashGen, false);

    static readonly System.Collections.Generic.Dictionary<AudioClip, float> lastPlayed = new System.Collections.Generic.Dictionary<AudioClip, float>();
    static readonly System.Collections.Generic.Dictionary<AudioClip, int> playing = new System.Collections.Generic.Dictionary<AudioClip, int>();

    public static void Play(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f, float spatial = 0.75f)
    {
        if (!clip) return;
        // the same sound twice within 40 ms just doubles the loudness and distorts: skip it; and at most 6 of one kind at once
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(clip, out var last) && now - last < 0.04f) return;
        playing.TryGetValue(clip, out int count);
        if (count >= 6) return;
        lastPlayed[clip] = now;
        playing[clip] = count + 1;
        volume *= 0.85f;                                                  // headroom: many sounds together without clipping
        var go = new GameObject("Sfx_" + clip.name);
        go.transform.position = pos;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.volume = volume; s.pitch = pitch;
        s.spatialBlend = spatial; s.minDistance = 10f; s.maxDistance = 800f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.dopplerLevel = 0f;
        s.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        go.AddComponent<SfxDone>().clip = clip;
    }

    internal static void Finished(AudioClip clip)
    {
        if (clip && playing.TryGetValue(clip, out int c)) playing[clip] = Mathf.Max(0, c - 1);
    }

    public static AudioSource Loop(GameObject owner, AudioClip clip, float spatial = 0.3f)
    {
        var s = owner.AddComponent<AudioSource>();
        s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = spatial; s.dopplerLevel = 0f;
        s.playOnAwake = false;
        return s;
    }

    delegate void Gen(float[] d, System.Random rng);

    static AudioClip Make(string name, float seconds, Gen gen, bool loop)
    {
        int n = Mathf.RoundToInt(seconds * Rate);
        int extra = loop ? Rate / 10 : 0;               // generate a bit more and cross-fade for a seamless loop
        var d = new float[n + extra];
        gen(d, new System.Random(name.GetHashCode()));
        if (loop)
            for (int i = 0; i < extra; i++)
            {
                float k = i / (float)extra;
                d[i] = d[i] * k + d[n + i] * (1f - k);
            }
        var outD = new float[n];
        System.Array.Copy(d, outD, n);
        float peak = 0f;
        foreach (var v in outD) peak = Mathf.Max(peak, Mathf.Abs(v));
        if (peak > 0f) for (int i = 0; i < n; i++) outD[i] *= 0.9f / peak;
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(outD, 0);
        return clip;
    }

    static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;

    static void LaserGen(float[] d, System.Random r)
    {
        // smooth humming energy beam: two detuned tones that beat slowly, a soft electric shimmer on top
        var shimmer = new BandPass(2400f, 2.5f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float pulse = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 12f * t);
            float hum = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 183f * t) * 0.4f
                      + Mathf.Sin(2f * Mathf.PI * 360f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 720f * t) * 0.1f;
            float whine = Mathf.Sin(2f * Mathf.PI * 1440f * t + 0.6f * Mathf.Sin(2f * Mathf.PI * 6f * t)) * 0.07f;
            d[i] = hum * pulse + whine + shimmer.Run(Noise(r)) * 0.18f;
        }
    }

    static void ImpactGen(float[] d, System.Random r)
    {
        float phase = 0f, lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float f = 38f + 110f * Mathf.Exp(-t * 14f);
            phase += 2f * Mathf.PI * f / Rate;
            float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 4.5f);
            lp += (Noise(r) - lp) * 0.08f;
            float rumble = lp * Mathf.Exp(-t * 7f) * 2.2f;
            float crack = Noise(r) * Mathf.Exp(-t * 55f) * 0.7f;
            float thump = Mathf.Sin(2f * Mathf.PI * (120f + 160f * Mathf.Exp(-t * 25f)) * t) * Mathf.Exp(-t * 12f) * 0.8f;
            d[i] = boom + rumble + crack * 0.6f + thump;
        }
    }

    static void CrumbleGen(float[] d, System.Random r)
    {
        float brown = 0f, lp = 0f, pop = 0f;
        float total = d.Length / (float)Rate;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((total - t) / 1.8f);
            brown = (brown + Noise(r) * 0.04f) * 0.995f;
            lp += (Noise(r) - lp) * 0.2f;
            if (r.NextDouble() < 0.0009) pop = 1f;      // rocks cracking
            pop *= 0.9985f;
            d[i] = (brown * 6f + lp * 0.25f + Noise(r) * pop * 0.6f) * env;
        }
    }

    static void ClankGen(float[] d, System.Random r)
    {
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float ring = Mathf.Sin(2f * Mathf.PI * 523f * t) * Mathf.Exp(-t * 6f)
                       + Mathf.Sin(2f * Mathf.PI * 1347f * t) * 0.6f * Mathf.Exp(-t * 9f)
                       + Mathf.Sin(2f * Mathf.PI * 2211f * t) * 0.4f * Mathf.Exp(-t * 14f);
            d[i] = ring + Noise(r) * Mathf.Exp(-t * 80f);
        }
    }

    static void WhooshGen(float[] d, System.Random r)
    {
        // a swish that sweeps up then down (filtered noise, not a hiss)
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = i / (float)d.Length;
            float env = Mathf.Sin(k * Mathf.PI);
            float cut = Mathf.Lerp(0.02f, 0.18f, env);
            lp += (Noise(r) - lp) * cut;
            lp2 += (lp - lp2) * cut;
            d[i] = lp2 * env * env;
        }
    }

    static void SteamGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float n = Noise(r);
            lp += (n - lp) * 0.3f;
            d[i] = (n - lp) * 0.8f;                     // high-passed hiss
        }
    }

    // ---- one-pole / band-pass helpers
    struct BandPass
    {
        float x1, x2, y1, y2, b0, b2, a1, a2;
        public BandPass(float freq, float q)
        {
            float w = 2f * Mathf.PI * freq / Rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
            b0 = alpha / a0; b2 = -alpha / a0; a1 = -2f * Mathf.Cos(w) / a0; a2 = (1f - alpha) / a0;
            x1 = x2 = y1 = y2 = 0f;
        }
        public float Run(float x)
        {
            float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    static void WindGen(float[] d, System.Random r)
    {
        var bp = new BandPass(500f, 0.7f);
        var bp2 = new BandPass(1400f, 1.2f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float gust = 0.6f + 0.4f * Mathf.Sin(t * 2.1f) * Mathf.Sin(t * 0.7f + 1f);
            float n = Noise(r);
            d[i] = (bp.Run(n) * 1.4f + bp2.Run(n) * 0.5f) * gust;
        }
    }

    /// Steady jet-like flight roar: deep body + smooth airflow, no gusts.
    static void FlightGen(float[] d, System.Random r)
    {
        // rushing air, darker and smoother (no hiss): pitch rises with speed in HeroAudio
        var low = new BandPass(160f, 0.7f);
        var body = new BandPass(380f, 0.9f);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float n = Noise(r);
            lp += (n - lp) * 0.05f;
            lp2 += (lp - lp2) * 0.05f;
            float gust = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.5f * t) + 0.08f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            d[i] = (low.Run(n) * 1.6f + body.Run(n) * 0.8f + lp2 * 3f) * gust;
        }
    }

    /// Air tearing apart around him during a boost.
    static void BoostTearGen(float[] d, System.Random r)
    {
        // deep roaring blast instead of a sharp hiss
        var roar = new BandPass(240f, 0.8f);
        var tearB = new BandPass(1100f, 1.2f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float n = Noise(r);
            float flutter = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 22f * t);
            d[i] = roar.Run(n) * 1.4f * flutter + tearB.Run(n) * 0.35f;
        }
    }

    static void RushGen(float[] d, System.Random r)
    {
        var bp = new BandPass(700f, 0.8f);
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float n = Noise(r);
            lp += (n - lp) * 0.04f;
            float flutter = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 18f * t);   // blurred footfalls
            d[i] = (bp.Run(n) * 0.7f + lp * 1.6f) * flutter;
        }
    }

    static void StepGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += (Noise(r) - lp) * 0.1f;
            // thud you can hear on a phone speaker (140 Hz) plus a soft scuff
            d[i] = (Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.4f) * Mathf.Exp(-t * 40f)
                 + lp * 0.5f * Mathf.Exp(-t * 60f);
        }
    }

    static void EngineGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float f = 48f;                                     // integer cycles per second: seamless loop
            float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * f * t)), 6f);
            lp += (Noise(r) - lp) * 0.05f;
            d[i] = pulse * 0.8f + Mathf.Sin(2f * Mathf.PI * 96f * t) * 0.25f + lp * 0.3f;
        }
    }

    static void ExplosionGen(float[] d, System.Random r)
    {
        float lp = 0f, lp2 = 0f, phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += (Noise(r) - lp) * 0.12f;
            lp2 += (Noise(r) - lp2) * 0.015f;
            phase += 2f * Mathf.PI * (30f + 60f * Mathf.Exp(-t * 6f)) / Rate;
            float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 2.2f) * 1.2f;
            float roar = lp * Mathf.Exp(-t * 3f) * 1.6f + lp2 * Mathf.Exp(-t * 1.2f) * 5f;
            float crack = Noise(r) * Mathf.Exp(-t * 40f);
            float debris = r.NextDouble() < 0.002 * Mathf.Exp(-t) ? Noise(r) * 2f : 0f;
            d[i] = boom + roar + crack + debris;
        }
    }

    static void ScreamGen(float[] d, System.Random r)
    {
        // glottal pulse train shaped by two "ah" formants, pitch rising then breaking
        var f1 = new BandPass(850f, 5f);
        var f2 = new BandPass(1300f, 6f);
        var f3 = new BandPass(2800f, 8f);
        float phase = 0f;
        float total = d.Length / (float)Rate;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float pitch = Mathf.Lerp(420f, 640f, Mathf.Clamp01(t / 0.25f)) * (1f + 0.05f * Mathf.Sin(t * 38f)) - 120f * Mathf.Clamp01((t - 0.9f) / 0.4f);
            phase += pitch / Rate;
            float src = (phase % 1f) * 2f - 1f + Noise(r) * 0.25f;
            float env = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((total - t) / 0.35f);
            d[i] = (f1.Run(src) * 1.2f + f2.Run(src) + f3.Run(src) * 0.5f) * env;
        }
    }

    static void SplatGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += (Noise(r) - lp) * 0.3f;
            float thud = Mathf.Sin(2f * Mathf.PI * (90f - 40f * t) * t) * Mathf.Exp(-t * 18f);
            float wet = lp * Mathf.Exp(-t * 9f) * (0.6f + 0.4f * Mathf.Sin(t * 260f));
            d[i] = thud * 1.2f + wet;
        }
    }

    static void CrunchGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += (Noise(r) - lp) * 0.4f;
            float grit = r.NextDouble() < 0.02 * Mathf.Exp(-t * 4f) ? Noise(r) * 2.5f : 0f;
            float ring = Mathf.Sin(2f * Mathf.PI * 310f * t) * Mathf.Sin(2f * Mathf.PI * 733f * t) * Mathf.Exp(-t * 6f);
            d[i] = lp * Mathf.Exp(-t * 7f) + grit + ring * 0.7f;
        }
    }

    static void JetGen(float[] d, System.Random r)
    {
        var bp = new BandPass(260f, 0.6f);
        var whine = new BandPass(3200f, 12f);
        for (int i = 0; i < d.Length; i++)
        {
            float n = Noise(r);
            d[i] = bp.Run(n) * 1.5f + whine.Run(n) * 0.4f;
        }
    }



    static void SplashGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float n = Noise(r);
            lp += (n - lp) * 0.2f;
            d[i] = (n - lp) * Mathf.Exp(-t * 3.5f) + Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 20f) * 0.6f;
        }
    }


    static void SizzleGen(float[] d, System.Random r)
    {
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float n = Noise(r);
            lp += (n - lp) * 0.5f;
            float crackle = r.NextDouble() < 0.004 ? Noise(r) * 3f : 0f;
            d[i] = (n - lp) * 0.4f + crackle;
        }
    }
}

/// Counts a one-shot sound as finished when its object goes away (for Sfx's per-sound limit).
class SfxDone : MonoBehaviour
{
    public AudioClip clip;
    void OnDestroy() => Sfx.Finished(clip);
}
