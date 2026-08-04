using System;
using NAudio.Wave;

namespace AsioSignalGenerator.Audio
{
    /// <summary>
    /// The supported test-tone / noise waveform types.
    /// </summary>
    public enum WaveformType
    {
        Sine,
        Square,
        Triangle,
        Sawtooth,
        WhiteNoise,
        PinkNoise
    }

    /// <summary>
    /// Implemented by generators that have an adjustable frequency (i.e. not noise).
    /// </summary>
    public interface IFrequencyControl
    {
        double Frequency { get; set; }
    }

    /// <summary>
    /// Base class for all mono (single channel) sample generators used as ASIO playback sources.
    /// </summary>
    public abstract class SignalGeneratorBase : ISampleProvider
    {
        protected readonly int sampleRate;
        private readonly WaveFormat waveFormat;

        protected SignalGeneratorBase(int sampleRate)
        {
            this.sampleRate = sampleRate;
            // Mono, IEEE float - matches the ASIO 32-bit float pipeline requirement.
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat => waveFormat;

        /// <summary>Linear amplitude (0.0 - 1.0). Overall dB gain is applied separately downstream.</summary>
        public double Amplitude { get; set; } = 0.9;

        public abstract int Read(float[] buffer, int offset, int count);
    }

    public sealed class SineGenerator : SignalGeneratorBase, IFrequencyControl
    {
        private double phase;
        public double Frequency { get; set; } = 440.0;

        public SineGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            double phaseIncrement = 2.0 * Math.PI * Frequency / sampleRate;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)(Amplitude * Math.Sin(phase));
                phase += phaseIncrement;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
            }
            return count;
        }
    }

    public sealed class SquareGenerator : SignalGeneratorBase, IFrequencyControl
    {
        private double phase;
        public double Frequency { get; set; } = 440.0;

        public SquareGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            double phaseIncrement = 2.0 * Math.PI * Frequency / sampleRate;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)(Amplitude * (Math.Sin(phase) >= 0 ? 1.0 : -1.0));
                phase += phaseIncrement;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
            }
            return count;
        }
    }

    public sealed class SawtoothGenerator : SignalGeneratorBase, IFrequencyControl
    {
        private double phase; // 0..1
        public double Frequency { get; set; } = 440.0;

        public SawtoothGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            double increment = Frequency / sampleRate;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)(Amplitude * (2.0 * phase - 1.0));
                phase += increment;
                if (phase >= 1.0) phase -= 1.0;
            }
            return count;
        }
    }

    public sealed class TriangleGenerator : SignalGeneratorBase, IFrequencyControl
    {
        private double phase; // 0..1
        public double Frequency { get; set; } = 440.0;

        public TriangleGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            double increment = Frequency / sampleRate;
            for (int i = 0; i < count; i++)
            {
                // Triangle wave from a phase ramp: 4*|phase - 0.5| - 1
                double value = 4.0 * Math.Abs(phase - 0.5) - 1.0;
                buffer[offset + i] = (float)(Amplitude * value);
                phase += increment;
                if (phase >= 1.0) phase -= 1.0;
            }
            return count;
        }
    }

    public sealed class WhiteNoiseGenerator : SignalGeneratorBase
    {
        private readonly Random random = new Random();

        public WhiteNoiseGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)(Amplitude * (random.NextDouble() * 2.0 - 1.0));
            }
            return count;
        }
    }

    /// <summary>
    /// Pink noise using Paul Kellet's economy filter approximation.
    /// </summary>
    public sealed class PinkNoiseGenerator : SignalGeneratorBase
    {
        private readonly Random random = new Random();
        private double b0, b1, b2;

        public PinkNoiseGenerator(int sampleRate) : base(sampleRate) { }

        public override int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double white = random.NextDouble() * 2.0 - 1.0;
                b0 = 0.99886 * b0 + white * 0.0555179;
                b1 = 0.99332 * b1 + white * 0.0750759;
                b2 = 0.96900 * b2 + white * 0.1538520;
                double pink = b0 + b1 + b2 + white * 0.1848;
                buffer[offset + i] = (float)(Amplitude * (pink * 0.2)); // scale down, sum can exceed +-1
            }
            return count;
        }
    }
}
