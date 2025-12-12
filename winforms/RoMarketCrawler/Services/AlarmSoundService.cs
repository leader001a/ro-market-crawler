using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace RoMarketCrawler.Services;

/// <summary>
/// Service for generating and playing different alarm sounds using NAudio
/// </summary>
public static class AlarmSoundService
{
    private static WaveOutEvent? _waveOut;
    private static readonly object _lock = new();

    /// <summary>
    /// Play the specified alarm sound type
    /// </summary>
    public static void PlaySound(AlarmSoundType soundType)
    {
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    // Stop any currently playing sound
                    _waveOut?.Stop();
                    _waveOut?.Dispose();

                    var provider = CreateSoundProvider(soundType);
                    if (provider == null) return;

                    _waveOut = new WaveOutEvent();
                    _waveOut.Init(provider);
                    _waveOut.Play();

                    // Wait for playback to complete
                    while (_waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        Thread.Sleep(50);
                    }

                    _waveOut.Dispose();
                    _waveOut = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlarmSoundService] Error playing sound: {ex.Message}");
                // Fallback to system sound
                System.Media.SystemSounds.Exclamation.Play();
            }
        });
    }

    private static ISampleProvider? CreateSoundProvider(AlarmSoundType soundType)
    {
        return soundType switch
        {
            AlarmSoundType.SystemSound => null, // Use system sound instead
            AlarmSoundType.Chime => CreateChimeSound(),
            AlarmSoundType.DingDong => CreateDingDongSound(),
            AlarmSoundType.Rising => CreateRisingSound(),
            AlarmSoundType.Alert => CreateAlertSound(),
            _ => null
        };
    }

    /// <summary>
    /// Creates a pleasant chime sound (single tone with fade)
    /// </summary>
    private static ISampleProvider CreateChimeSound()
    {
        var sampleRate = 44100;
        var duration = 0.5f;
        var frequency = 880f; // A5 note

        var signal = new SignalGenerator(sampleRate, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = frequency,
            Gain = 0.3
        };

        return ApplyEnvelope(signal.Take(TimeSpan.FromSeconds(duration)), duration, sampleRate);
    }

    /// <summary>
    /// Creates a two-tone doorbell sound
    /// </summary>
    private static ISampleProvider CreateDingDongSound()
    {
        var sampleRate = 44100;
        var toneDuration = 0.25f;

        // First tone (higher) - E5
        var ding = new SignalGenerator(sampleRate, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 659.25f,
            Gain = 0.3
        };

        // Second tone (lower) - C5
        var dong = new SignalGenerator(sampleRate, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 523.25f,
            Gain = 0.3
        };

        var dingWithEnvelope = ApplyEnvelope(ding.Take(TimeSpan.FromSeconds(toneDuration)), toneDuration, sampleRate);
        var dongWithEnvelope = ApplyEnvelope(dong.Take(TimeSpan.FromSeconds(toneDuration)), toneDuration, sampleRate);

        return dingWithEnvelope.FollowedBy(dongWithEnvelope);
    }

    /// <summary>
    /// Creates a rising tone sound
    /// </summary>
    private static ISampleProvider CreateRisingSound()
    {
        var sampleRate = 44100;
        var duration = 0.4f;
        var samples = (int)(sampleRate * duration);

        var buffer = new float[samples];
        var startFreq = 400f;
        var endFreq = 1200f;

        for (int i = 0; i < samples; i++)
        {
            var t = (float)i / sampleRate;
            var progress = (float)i / samples;
            var frequency = startFreq + (endFreq - startFreq) * progress;
            var envelope = GetEnvelopeValue(progress, duration);
            buffer[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * 0.3 * envelope);
        }

        return new RawSourceWaveStream(
            new MemoryStream(FloatToByteArray(buffer)),
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)
        ).ToSampleProvider();
    }

    /// <summary>
    /// Creates an alert sound (three quick beeps)
    /// </summary>
    private static ISampleProvider CreateAlertSound()
    {
        var sampleRate = 44100;
        var beepDuration = 0.1f;
        var pauseDuration = 0.05f;
        var frequency = 1000f;

        var beep = new SignalGenerator(sampleRate, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = frequency,
            Gain = 0.3
        };

        var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1))
            .ToSampleProvider()
            .Take(TimeSpan.FromSeconds(pauseDuration));

        var beepWithEnvelope = ApplyEnvelope(beep.Take(TimeSpan.FromSeconds(beepDuration)), beepDuration, sampleRate);

        return beepWithEnvelope
            .FollowedBy(silence)
            .FollowedBy(ApplyEnvelope(beep.Take(TimeSpan.FromSeconds(beepDuration)), beepDuration, sampleRate))
            .FollowedBy(silence)
            .FollowedBy(ApplyEnvelope(beep.Take(TimeSpan.FromSeconds(beepDuration)), beepDuration, sampleRate));
    }

    /// <summary>
    /// Applies an ADSR-like envelope to reduce clicks
    /// </summary>
    private static ISampleProvider ApplyEnvelope(ISampleProvider source, float duration, int sampleRate)
    {
        return new EnvelopeProvider(source, duration, sampleRate);
    }

    private static double GetEnvelopeValue(float progress, float duration)
    {
        var attackTime = 0.01f / duration;
        var releaseTime = 0.1f / duration;

        if (progress < attackTime)
            return progress / attackTime;
        else if (progress > 1 - releaseTime)
            return (1 - progress) / releaseTime;
        else
            return 1.0;
    }

    private static byte[] FloatToByteArray(float[] floatArray)
    {
        var byteArray = new byte[floatArray.Length * 4];
        Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    /// <summary>
    /// Simple envelope provider to avoid clicks
    /// </summary>
    private class EnvelopeProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _totalSamples;
        private readonly int _attackSamples;
        private readonly int _releaseSamples;
        private int _position;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EnvelopeProvider(ISampleProvider source, float duration, int sampleRate)
        {
            _source = source;
            _totalSamples = (int)(duration * sampleRate);
            _attackSamples = (int)(0.01f * sampleRate); // 10ms attack
            _releaseSamples = (int)(0.05f * sampleRate); // 50ms release
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = _source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                var sampleIndex = _position + i;
                float envelope = 1.0f;

                if (sampleIndex < _attackSamples)
                {
                    envelope = (float)sampleIndex / _attackSamples;
                }
                else if (sampleIndex > _totalSamples - _releaseSamples)
                {
                    envelope = (float)(_totalSamples - sampleIndex) / _releaseSamples;
                    if (envelope < 0) envelope = 0;
                }

                buffer[offset + i] *= envelope;
            }

            _position += samplesRead;
            return samplesRead;
        }
    }
}
