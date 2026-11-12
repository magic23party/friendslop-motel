using System;
using UnityEngine;

namespace Friendslop
{
    public enum Snd { Bell, Hit, Whoosh, Pickup, Put, Cash, Fine, Alarm, Step, Click, Thud }

    /// <summary>
    /// Процедурный звук (P3): клипы синтезируются в коде (AudioClip.Create), файлов нет.
    /// Play3D — звук в мире (линейное затухание до sfxMaxDistance), Play2D — интерфейс/сигналы всем.
    /// У каждого звука свой кулдаун (не спамить). Громкость — GameConfig.sfxVolume / uiVolume.
    /// В batchmode (самотесты, сервер) звук не играет.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        const int PoolSize = 14;
        static readonly int Count = Enum.GetValues(typeof(Snd)).Length;
        static readonly AudioClip[] clips = new AudioClip[Count];
        static readonly float[] lastAt = new float[Count];
        // минимальный интервал между одинаковыми звуками, с (realtime)
        static readonly float[] gap = { 0.40f, 0.07f, 0.12f, 0.06f, 0.08f, 0.10f, 0.15f, 1.2f, 0.05f, 0.03f, 0.25f };

        static AudioSource[] pool;
        static int next;
        static bool disabled = Application.isBatchMode;

        static float Master => GameConfig.Active != null ? GameConfig.Active.sfxVolume : 0.7f;
        static float Ui => GameConfig.Active != null ? GameConfig.Active.uiVolume : 0.6f;

        /// <summary>Звук в мире. range &gt; 0 — своя дальность слышимости (шаги тише и ближе).</summary>
        public static void Play3D(Snd s, Vector3 pos, float vol = 1f, float pitch = 1f, float range = 0f)
        {
            var src = Prepare(s);
            if (src == null) return;
            var cfg = GameConfig.Active;
            src.transform.position = pos;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = cfg != null ? cfg.sfxMinDistance : 2.5f;
            src.maxDistance = range > 0f ? range : (cfg != null ? cfg.sfxMaxDistance : 26f);
            Fire(src, s, vol * Master, pitch);
        }

        /// <summary>Звук без позиции: клики интерфейса и общие сигналы (тревога налога).</summary>
        public static void Play2D(Snd s, float vol = 1f, float pitch = 1f)
        {
            var src = Prepare(s);
            if (src == null) return;
            src.spatialBlend = 0f;
            Fire(src, s, vol * (s == Snd.Click ? Ui : Master), pitch);
        }

        static void Fire(AudioSource src, Snd s, float vol, float pitch)
        {
            src.clip = clips[(int)s];
            src.volume = Mathf.Clamp01(vol);
            src.pitch = pitch;
            src.Play();
            lastAt[(int)s] = Time.realtimeSinceStartup;
        }

        static AudioSource Prepare(Snd s)
        {
            if (disabled) return null;
            int i = (int)s;
            if (Time.realtimeSinceStartup - lastAt[i] < gap[i]) return null;
            if (pool == null || pool[0] == null)
            {
                var host = new GameObject("Sfx");
                UnityEngine.Object.DontDestroyOnLoad(host);
                pool = new AudioSource[PoolSize];
                for (int k = 0; k < PoolSize; k++)
                {
                    var a = new GameObject("src" + k).AddComponent<AudioSource>();
                    a.transform.SetParent(host.transform, false);
                    a.playOnAwake = false;
                    a.dopplerLevel = 0f;
                    pool[k] = a;
                }
            }
            if (clips[i] == null) clips[i] = Synth(s);
            for (int k = 0; k < PoolSize; k++)
            {
                var a = pool[(next + k) % PoolSize];
                if (!a.isPlaying) { next = (next + k + 1) % PoolSize; return a; }
            }
            var steal = pool[next]; next = (next + 1) % PoolSize;      // все заняты — перебиваем самый старый
            return steal;
        }

        // ------------------------------------------------------------ синтез

        static AudioClip Make(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float v = f(i / (float)Rate);
                d[i] = v;
                float a = Mathf.Abs(v); if (a > peak) peak = a;
            }
            float k = 0.85f / peak;
            for (int i = 0; i < n; i++) d[i] *= k;
            // короткое затухание на конце, без щелчка
            int tail = Mathf.Min(n, 200);
            for (int i = 0; i < tail; i++) d[n - 1 - i] *= i / (float)tail;
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Env(float t, float attack, float decay) => Mathf.Clamp01(t / attack) * Mathf.Exp(-t * decay);

        static AudioClip Synth(Snd s)
        {
            var rnd = new System.Random(1234 + (int)s);
            Func<float> noise = () => (float)(rnd.NextDouble() * 2.0 - 1.0);
            switch (s)
            {
                case Snd.Bell:      // настольный звонок: два «динь» с негармоничными обертонами
                {
                    Func<float, float> ding = t => t < 0f ? 0f :
                        Sin(2093f, t) * Env(t, 0.002f, 4.5f) + Sin(2093f * 2.76f, t) * 0.45f * Env(t, 0.002f, 9f) + Sin(2093f * 5.4f, t) * 0.2f * Env(t, 0.002f, 15f);
                    return Make("bell", 1.2f, t => ding(t) + 0.8f * ding(t - 0.2f));
                }
                case Snd.Hit:       // глухой удар: падающий по тону «бум» + щелчок шума
                {
                    float lp = 0f;
                    return Make("hit", 0.32f, t =>
                    {
                        float ph = 2f * Mathf.PI * (48f * t + 130f / 28f * (1f - Mathf.Exp(-28f * t)));
                        lp += (noise() - lp) * 0.35f;
                        return Mathf.Sin(ph) * Mathf.Exp(-t * 13f) + lp * 0.9f * Mathf.Exp(-t * 55f);
                    });
                }
                case Snd.Whoosh:    // взмах: шум с «ползущим» фильтром
                {
                    float lp = 0f;
                    return Make("whoosh", 0.26f, t =>
                    {
                        float k = Mathf.Lerp(0.05f, 0.35f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.26f)));
                        lp += (noise() - lp) * k;
                        float a = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.26f));
                        return lp * a * a;
                    });
                }
                case Snd.Pickup:    // взять: короткий восходящий «пик»
                    return Make("pickup", 0.11f, t => Sin(620f + 3600f * t, t) * Env(t, 0.004f, 26f));
                case Snd.Put:       // положить/отдать: мягкий нисходящий «тук»
                    return Make("put", 0.12f, t => Sin(330f - 1100f * t, t) * Env(t, 0.003f, 30f));
                case Snd.Cash:      // касса: щелчок и два металлических «дзынь»
                {
                    Func<float, float, float> coin = (hz, t) => t < 0f ? 0f :
                        (Sin(hz, t) + Sin(hz * 2.4f, t) * 0.35f + Sin(hz * 3.9f, t) * 0.15f) * Env(t, 0.002f, 7f);
                    return Make("cash", 0.55f, t => noise() * Mathf.Exp(-t * 180f) * 0.5f + coin(1568f, t - 0.03f) + 0.9f * coin(2349f, t - 0.10f));
                }
                case Snd.Fine:      // штраф: два нисходящих глухих тона
                    return Make("fine", 0.34f, t =>
                    {
                        float a = Sin(330f, t) * Env(t, 0.004f, 11f);
                        float b = t > 0.13f ? Sin(247f, t) * Env(t - 0.13f, 0.004f, 9f) : 0f;
                        return (a + b) * (0.7f + 0.3f * Mathf.Sign(Sin(55f, t)));
                    });
                case Snd.Alarm:     // тревога: чередование двух тонов, квадратная волна
                    return Make("alarm", 0.9f, t =>
                    {
                        int beep = Mathf.FloorToInt(t / 0.15f);
                        float lt = t - beep * 0.15f;
                        float hz = beep % 2 == 0 ? 880f : 660f;
                        float sq = Mathf.Sign(Sin(hz, t)) * 0.6f + Sin(hz, t) * 0.4f;
                        return sq * Mathf.Clamp01(lt / 0.005f) * Mathf.Clamp01((0.15f - lt) / 0.01f) * (t < 0.9f ? 1f : 0f);
                    });
                case Snd.Step:      // шаг: глухой приглушённый шум
                {
                    float lp = 0f;
                    return Make("step", 0.11f, t =>
                    {
                        lp += (noise() - lp) * 0.22f;
                        return lp * Mathf.Exp(-t * 48f) + Sin(95f, t) * 0.5f * Mathf.Exp(-t * 40f);
                    });
                }
                case Snd.Click:     // интерфейс: сухой «тик»
                    return Make("click", 0.045f, t => Sin(1250f, t) * Env(t, 0.001f, 90f) + noise() * 0.15f * Mathf.Exp(-t * 300f));
                default:            // Thud: падение тела
                {
                    float lp = 0f;
                    return Make("thud", 0.4f, t =>
                    {
                        float ph = 2f * Mathf.PI * (38f * t + 70f / 14f * (1f - Mathf.Exp(-14f * t)));
                        lp += (noise() - lp) * 0.15f;
                        return Mathf.Sin(ph) * Mathf.Exp(-t * 9f) + lp * 0.7f * Mathf.Exp(-t * 30f);
                    });
                }
            }
        }
    }
}
