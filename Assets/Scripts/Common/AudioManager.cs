using System;
using System.Collections.Generic;
using UnityEngine;

public enum Sfx
{
    Pickup,
    Rare,
    Hit,
    Dash,
    Kill,
    Spawn,
    Unlock,
    Win,
    Lose,
    Click
}

/// <summary>Semua suara disintesis lewat kode (tanpa file audio): SFX + musik ambient yang loop.</summary>
public class AudioManager : MonoBehaviour
{
    private const int Rate = 44100;

    private static AudioManager instance;

    [SerializeField] private float musicVolume = 0.22f;
    [SerializeField] private float sfxVolume = 0.8f;

    private readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
    private AudioSource sfxSource;
    private AudioSource musicSource;
    private bool muted;

    public static void Play(Sfx id, float volume = 1f, float pitch = 1f)
    {
        if (instance == null || !instance.clips.TryGetValue(id, out AudioClip clip))
        {
            return;
        }

        instance.sfxSource.pitch = pitch;
        instance.sfxSource.PlayOneShot(clip, volume * instance.sfxVolume);
    }

    private void Awake()
    {
        instance = this;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.volume = musicVolume;

        BuildSfx();

        musicSource.clip = BuildMusic();
        musicSource.Play();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            muted = !muted;
            AudioListener.volume = muted ? 0f : 1f;
        }
    }

    // ------------------------------------------------------------------ SFX

    private void BuildSfx()
    {
        System.Random rng = new System.Random(7);
        Func<float> noise = () => (float)(rng.NextDouble() * 2.0 - 1.0);

        clips[Sfx.Pickup] = Clip("Pickup", 0.28f, t =>
            (Tone(t < 0.07f ? 740f : 1110f, t) + 0.3f * Tone(t < 0.07f ? 1480f : 2220f, t)) * Mathf.Exp(-t * 13f) * 0.5f);

        clips[Sfx.Rare] = Clip("Rare", 0.6f, t =>
            (Note(740f, t, 0f, 6f) + Note(932f, t, 0.07f, 6f) + Note(1175f, t, 0.14f, 6f) + Note(1480f, t, 0.21f, 5f)) * 0.3f);

        clips[Sfx.Hit] = Clip("Hit", 0.4f, t =>
            (noise() * Mathf.Exp(-t * 12f) * 0.6f + Tone(130f - 70f * t, t) * Mathf.Exp(-t * 7f) * 0.8f));

        clips[Sfx.Dash] = Clip("Dash", 0.28f, t =>
        {
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.28f));
            return (noise() * 0.35f + Tone(220f + 1500f * t, t) * 0.25f) * env;
        });

        clips[Sfx.Kill] = Clip("Kill", 0.3f, t =>
            (Mathf.Sin(2f * Mathf.PI * (460f * t - 520f * t * t)) * Mathf.Exp(-t * 9f) * 0.7f + noise() * Mathf.Exp(-t * 28f) * 0.3f));

        clips[Sfx.Spawn] = Clip("Spawn", 0.07f, t => Tone(1500f, t) * Mathf.Exp(-t * 70f) * 0.5f);

        clips[Sfx.Unlock] = Clip("Unlock", 0.9f, t =>
            (Note(523f, t, 0f, 4f) + Note(659f, t, 0.09f, 4f) + Note(784f, t, 0.18f, 4f) + Note(1047f, t, 0.27f, 3.5f)) * 0.28f);

        clips[Sfx.Win] = Clip("Win", 1.4f, t =>
            (Note(523f, t, 0f, 3f) + Note(659f, t, 0.12f, 3f) + Note(784f, t, 0.24f, 3f) + Note(1047f, t, 0.36f, 2.5f) + Note(1319f, t, 0.48f, 2f)) * 0.25f);

        clips[Sfx.Lose] = Clip("Lose", 1.6f, t =>
            (Note(392f, t, 0f, 3f, true) + Note(330f, t, 0.24f, 3f, true) + Note(262f, t, 0.48f, 3f, true) + Note(196f, t, 0.72f, 2.2f, true)) * 0.3f);

        clips[Sfx.Click] = Clip("Click", 0.05f, t => Tone(1000f, t) * Mathf.Exp(-t * 50f) * 0.4f);
    }

    private static AudioClip Clip(string name, float seconds, Func<float, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Tone(float frequency, float t)
    {
        return Mathf.Sin(2f * Mathf.PI * frequency * t);
    }

    private static float Note(float frequency, float t, float start, float decay, bool rich = false)
    {
        if (t < start)
        {
            return 0f;
        }

        float local = t - start;
        float value = Tone(frequency, local);

        if (rich)
        {
            value += 0.4f * Tone(frequency * 2f, local) + 0.2f * Tone(frequency * 3f, local);
        }

        return value * Mathf.Exp(-local * decay);
    }

    // ------------------------------------------------------------------ Musik

    private static AudioClip BuildMusic()
    {
        const float chordLength = 4f;
        const float stepLength = 0.5f;

        float[][] chords =
        {
            new[] { 220.00f, 261.63f, 329.63f },
            new[] { 174.61f, 220.00f, 261.63f },
            new[] { 261.63f, 329.63f, 392.00f },
            new[] { 196.00f, 246.94f, 293.66f }
        };

        int[] pattern = { 0, 1, 2, 1, 2, 1, 2, 0 };

        return Clip("Music", chordLength * chords.Length, t =>
        {
            int chordIndex = Mathf.Min((int)(t / chordLength), chords.Length - 1);
            float[] chord = chords[chordIndex];

            float local = t - chordIndex * chordLength;
            float u = local / chordLength;
            float padEnv = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);

            float value = 0f;

            foreach (float f in chord)
            {
                value += (Tone(f, t) + 0.5f * Tone(f * 1.005f, t)) * 0.05f * padEnv;
            }

            value += Tone(chord[0] * 0.5f, t) * 0.09f * padEnv;

            int step = Mathf.Min((int)(local / stepLength), pattern.Length - 1);
            float stepLocal = local - step * stepLength;
            float pluckFrequency = chord[pattern[step]] * 2f;

            value += (Tone(pluckFrequency, stepLocal) + 0.3f * Tone(pluckFrequency * 2f, stepLocal)) * Mathf.Exp(-stepLocal * 4.5f) * 0.1f;

            return value;
        });
    }
}
