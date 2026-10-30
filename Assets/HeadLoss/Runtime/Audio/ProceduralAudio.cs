using System;
using System.Threading;
using HeadLoss.Core.Data.Defs;
using UnityEngine;

namespace HeadLoss.Unity.Audio
{
    /// <summary>
    /// Calm procedural audio (<c>11-ui-visuals-audio.md</c> §Procedural audio), synthesised in
    /// <c>OnAudioFilterRead</c> on the listener: soft airflow (pink noise through a low-pass whose cutoff and gain
    /// follow air throughput, log scale), a quiet hum per running fan at 40–120 Hz by speed with a soft harmonic,
    /// short sine-blip UI clicks, a soft "tock" for placing a part and a two-note chime on Commit. A soft limiter
    /// keeps the sum under the ceiling. No audio files. Parameters are written on the main thread and read on the
    /// audio thread (single floats and counters only).
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        const int MaxFans = 4;
        AudioDef _def = new AudioDef();
        int _sampleRate = 48000;

        // Main thread → audio thread.
        volatile float _airLevel;
        volatile float _master;
        volatile float _ambience = 1, _ui = 1;
        readonly float[] _fanSpeed = new float[MaxFans];
        int _clickRequests, _tockRequests, _chimeRequests;

        // Audio thread state.
        uint _rng = 0x9E3779B9u;
        float _b0, _b1, _b2, _b3, _b4, _b5, _b6, _lp, _airSmooth;
        readonly double[] _fanPhase = new double[MaxFans];
        readonly float[] _fanSmooth = new float[MaxFans];
        int _clickDone, _tockDone, _chimeDone;
        float _clickEnv, _tockEnv, _chimeEnv1, _chimeEnv2;
        double _clickPhase, _tockPhase, _chimePhase1, _chimePhase2;
        int _chimeDelay;

        public void Init(AudioDef def, float masterVolume)
        {
            _def = def;
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _master = Mathf.Clamp01(masterVolume);
        }

        public float Volume
        {
            get => _master;
            set => _master = Mathf.Clamp01(value);
        }

        /// <summary>Total air throughput in the open building, m³/h (0 outside a building).</summary>
        /// <summary>Settings volumes, 0–1: ambience scales airflow and fan hum, UI scales clicks, tocks and chimes.</summary>
        public void SetVolumes(float master, float ambience, float ui)
        {
            _master = Mathf.Clamp01(master);
            _ambience = Mathf.Clamp01(ambience);
            _ui = Mathf.Clamp01(ui);
        }

        public void SetAirflow(double m3h)
        {
            double full = Math.Max(1, _def.AirflowFullM3h);
            _airLevel = (float)Math.Min(1, Math.Log10(1 + Math.Max(0, m3h) / 10) / Math.Log10(1 + full / 10));
        }

        /// <summary>Running fan speeds (0–1, 0 = off); at most four hum.</summary>
        public void SetFan(int index, double speed)
        {
            if (index >= 0 && index < MaxFans) _fanSpeed[index] = Mathf.Clamp01((float)speed);
        }

        public void ClearFans()
        {
            for (int i = 0; i < MaxFans; i++) _fanSpeed[i] = 0;
        }

        public void PlayClick() => Interlocked.Increment(ref _clickRequests);
        public void PlayTock() => Interlocked.Increment(ref _tockRequests);
        public void PlayChime() => Interlocked.Increment(ref _chimeRequests);

        float Noise()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float sr = _sampleRate;
            float dt = 1f / sr;
            float master = _master;
            int click = Volatile.Read(ref _clickRequests), tock = Volatile.Read(ref _tockRequests), chime = Volatile.Read(ref _chimeRequests);
            if (click != _clickDone) { _clickDone = click; _clickEnv = 1; _clickPhase = 0; }
            if (tock != _tockDone) { _tockDone = tock; _tockEnv = 1; _tockPhase = 0; }
            if (chime != _chimeDone) { _chimeDone = chime; _chimeEnv1 = 1; _chimePhase1 = 0; _chimeDelay = (int)(0.14f * sr); }

            float airTarget = _airLevel;
            float clickDecay = Mathf.Exp(-dt / 0.008f), tockDecay = Mathf.Exp(-dt / 0.03f), chimeDecay = Mathf.Exp(-dt / 0.45f);
            float smooth = 1f - Mathf.Exp(-dt / 0.25f);
            float ceiling = (float)_def.LimiterCeiling;

            for (int i = 0; i < data.Length; i += channels)
            {
                // Airflow: pink noise (Paul Kellet) through a one-pole low-pass that opens with throughput.
                _airSmooth += (airTarget - _airSmooth) * smooth;
                float w = Noise();
                _b0 = 0.99886f * _b0 + w * 0.0555179f;
                _b1 = 0.99332f * _b1 + w * 0.0750759f;
                _b2 = 0.96900f * _b2 + w * 0.1538520f;
                _b3 = 0.86650f * _b3 + w * 0.3104856f;
                _b4 = 0.55000f * _b4 + w * 0.5329522f;
                _b5 = -0.7616f * _b5 - w * 0.0168980f;
                float pink = (_b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + w * 0.5362f) * 0.11f;
                _b6 = w * 0.115926f;
                float fc = (float)(_def.AirflowCutoffMinHz + (_def.AirflowCutoffMaxHz - _def.AirflowCutoffMinHz) * _airSmooth);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * fc * dt);
                _lp += (pink - _lp) * a;
                float s = _lp * (float)_def.AirflowGain * _airSmooth;

                // Fan hum.
                for (int f = 0; f < MaxFans; f++)
                {
                    _fanSmooth[f] += (_fanSpeed[f] - _fanSmooth[f]) * smooth;
                    float sp = _fanSmooth[f];
                    if (sp < 0.01f) continue;
                    double hz = _def.HumMinHz + (_def.HumMaxHz - _def.HumMinHz) * sp;
                    _fanPhase[f] += hz * dt;
                    if (_fanPhase[f] > 1) _fanPhase[f] -= 1;
                    double ph = _fanPhase[f] * 2 * Math.PI;
                    s += (float)((Math.Sin(ph) + 0.3 * Math.Sin(2 * ph)) * _def.HumGain * sp);
                }

                float ambientPart = s;

                // UI click: 1 kHz blip, ~30 ms.
                if (_clickEnv > 1e-4f)
                {
                    _clickPhase += 1000.0 * dt;
                    s += (float)Math.Sin(_clickPhase * 2 * Math.PI) * _clickEnv * (float)_def.ClickGain;
                    _clickEnv *= clickDecay;
                }
                // Placing: a soft low "tock".
                if (_tockEnv > 1e-4f)
                {
                    _tockPhase += 420.0 * dt;
                    s += ((float)Math.Sin(_tockPhase * 2 * Math.PI) * 0.8f + w * 0.1f) * _tockEnv * (float)_def.ClickGain;
                    _tockEnv *= tockDecay;
                }
                // Commit: two gentle notes.
                if (_chimeEnv1 > 1e-4f || _chimeEnv2 > 1e-4f || _chimeDelay > 0)
                {
                    _chimePhase1 += 659.25 * dt;
                    s += (float)Math.Sin(_chimePhase1 * 2 * Math.PI) * _chimeEnv1 * (float)_def.ClickGain * 0.7f;
                    _chimeEnv1 *= chimeDecay;
                    if (_chimeDelay > 0 && --_chimeDelay == 0) { _chimeEnv2 = 1; _chimePhase2 = 0; }
                    _chimePhase2 += 880.0 * dt;
                    s += (float)Math.Sin(_chimePhase2 * 2 * Math.PI) * _chimeEnv2 * (float)_def.ClickGain * 0.7f;
                    _chimeEnv2 *= chimeDecay;
                }

                // Soft limiter.
                s = ambientPart * _ambience + (s - ambientPart) * _ui;
                float y = ceiling * (float)Math.Tanh(s * master / Math.Max(1e-3f, ceiling));
                for (int c = 0; c < channels; c++) data[i + c] += y;
            }
        }
    }
}
