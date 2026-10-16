using UnityEngine;

/// <summary>
/// Movement sounds for the hero: a jet-like roar that rises with flying speed, air tearing on boost, the rush of super speed,
/// footsteps, take-off, sonic boom, landing, splashing into the sea.
/// </summary>
[RequireComponent(typeof(HeroController))]
public class HeroAudio : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.9f;

    HeroController hero;
    AudioSource flight, tear, rush;
    float stepTimer;
    HeroController.State lastState;

    void Start()
    {
        hero = GetComponent<HeroController>();
        flight = Sfx.Loop(gameObject, Sfx.Flight, 0f);
        tear = Sfx.Loop(gameObject, Sfx.BoostTear, 0f);
        rush = Sfx.Loop(gameObject, Sfx.Rush, 0f);
        flight.Play(); tear.Play(); rush.Play();
        hero.HardLanded += (p, n, speed) => Sfx.Play(Sfx.Impact, p, volume, 0.75f, 0.5f);
        hero.SplashedIntoSea += p => Sfx.Play(Sfx.Splash, p, volume, 1f, 0.5f);
        hero.BoostStarted += d =>
        {
            Sfx.Play(Sfx.Impact, transform.position, volume * 0.7f, 1.5f, 0.3f);     // sonic boom crack
            Sfx.Play(Sfx.Whoosh, transform.position, volume, 0.6f, 0.2f);
        };
        lastState = hero.Current;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 v = hero.Velocity;
        float speed = v.magnitude;
        bool flying = hero.Current == HeroController.State.Fly;
        bool ground = hero.Current == HeroController.State.Ground;

        // flight roar: rises smoothly with speed (also when falling fast)
        float flyK = flying || hero.Current == HeroController.State.Air ? Mathf.InverseLerp(3f, hero.boostSpeed, speed) : 0f;
        float flyVol = flying ? 0.05f + 0.75f * flyK : flyK * 0.8f;       // hovering is almost silent
        flight.volume = Mathf.Lerp(flight.volume, volume * flyVol * (flying || flyK > 0.1f ? 1f : 0f), 1f - Mathf.Exp(-5f * dt));
        flight.pitch = Mathf.Lerp(0.8f, 1.3f, flyK);
        // boost: air tearing on top
        float tearK = flying && hero.IsBoosting ? Mathf.InverseLerp(hero.flySpeed, hero.boostSpeed, speed) : 0f;
        tear.volume = Mathf.Lerp(tear.volume, volume * tearK * 0.5f, 1f - Mathf.Exp(-6f * dt));
        tear.pitch = Mathf.Lerp(0.9f, 1.2f, tearK);

        // super speed rush
        float rushK = ground ? hero.SuperSpeed01 : 0f;
        rush.volume = Mathf.Lerp(rush.volume, volume * rushK * 0.7f, 1f - Mathf.Exp(-8f * dt));
        rush.pitch = Mathf.Lerp(0.8f, 1.4f, rushK);

        // footsteps while walking / running (super speed blurs them into the rush)
        float planar = new Vector2(v.x, v.z).magnitude;
        if (ground && planar > 0.6f && rushK < 0.3f)
        {
            float interval = Mathf.Lerp(0.55f, 0.27f, Mathf.InverseLerp(1.5f, hero.sprintSpeed, planar));
            stepTimer -= dt;
            if (stepTimer <= 0f)
            {
                stepTimer = interval;
                Sfx.Play(Sfx.Step, transform.position, volume * Mathf.Lerp(0.35f, 0.7f, planar / hero.sprintSpeed), Random.Range(0.9f, 1.1f), 0.2f);
            }
        }
        else stepTimer = 0.05f;

        // take-off whoosh
        if (flying && lastState != HeroController.State.Fly)
            Sfx.Play(Sfx.Whoosh, transform.position, volume, 0.7f, 0.2f);
        lastState = hero.Current;
    }
}
