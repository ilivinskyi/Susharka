using System;
using System.IO;
using System.Media;

namespace Susharka.Core;

/// <summary>Tiny synthesized sound effects, so the exe ships no audio assets.</summary>
internal static class Sounds
{
    public static bool Enabled { get; set; } = true;

    private const int Rate = 44100;
    private static readonly Lazy<SoundPlayer> ShutterPlayer = new(() => Make(Shutter()));
    private static readonly Lazy<SoundPlayer> PegPlayer = new(() => Make(Peg()));
    private static readonly Lazy<SoundPlayer> WhooshPlayer = new(() => Make(Whoosh()));

    public static void PlayShutter() => Play(ShutterPlayer);
    public static void PlayPeg() => Play(PegPlayer);
    public static void PlayWhoosh() => Play(WhooshPlayer);

    private static void Play(Lazy<SoundPlayer> p)
    {
        if (!Enabled) return;
        try { p.Value.Play(); } catch (Exception) { /* no audio device */ }
    }

    private static SoundPlayer Make(float[] samples)
    {
        var player = new SoundPlayer(new MemoryStream(Wav(samples)));
        player.Load();
        return player;
    }

    /// <summary>Two soft filtered-noise clicks, like a camera shutter.</summary>
    private static float[] Shutter()
    {
        var rnd = new Random(7);
        var s = new float[(int)(Rate * 0.16)];
        float lp = 0;
        foreach (var (start, amp) in new[] { (0.0, 0.55), (0.065, 0.4) })
        {
            int off = (int)(start * Rate);
            for (int i = 0; i < Rate * 0.05 && off + i < s.Length; i++)
            {
                double t = i / (double)Rate;
                float n = (float)(rnd.NextDouble() * 2 - 1);
                lp += 0.35f * (n - lp);
                s[off + i] += (float)(lp * amp * Math.Exp(-t * 90));
            }
        }
        return s;
    }

    /// <summary>A short woody "tock" for a photo landing on its peg.</summary>
    private static float[] Peg()
    {
        var s = new float[(int)(Rate * 0.09)];
        double phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = i / (double)Rate;
            double f = 950 - 350 * Math.Min(1, t / 0.03);
            phase += 2 * Math.PI * f / Rate;
            s[i] = (float)(0.35 * Math.Sin(phase) * Math.Exp(-t * 60) + 0.15 * Math.Sin(phase * 2.7) * Math.Exp(-t * 120));
        }
        return s;
    }

    /// <summary>A soft falling whoosh for photos let go.</summary>
    private static float[] Whoosh()
    {
        var rnd = new Random(3);
        var s = new float[(int)(Rate * 0.28)];
        float lp = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = i / (double)s.Length;
            float n = (float)(rnd.NextDouble() * 2 - 1);
            lp += (float)(0.05 + 0.15 * (1 - t)) * (n - lp);
            s[i] = (float)(lp * 0.5 * Math.Sin(Math.PI * t));
        }
        return s;
    }

    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var f in samples) w.Write((short)(Math.Clamp(f, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
