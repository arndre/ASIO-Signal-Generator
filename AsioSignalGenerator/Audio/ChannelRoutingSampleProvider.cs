using System;
using NAudio.Wave;

namespace AsioSignalGenerator.Audio
{
    /// <summary>
    /// Takes a mono ISampleProvider and duplicates it into an arbitrary subset of channels
    /// within a multi-channel output buffer (all other channels are silent). This lets the
    /// user route a single generated signal to any one or several ASIO output channels,
    /// regardless of whether they are contiguous.
    /// </summary>
    public sealed class ChannelRoutingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly WaveFormat waveFormat;
        private float[] sourceBuffer = Array.Empty<float>();

        /// <summary>
        /// Mutable - toggle channels on/off live, even while playing.
        /// Length must equal the total output channel count.
        /// </summary>
        public bool[] ActiveChannels { get; }

        public ChannelRoutingSampleProvider(ISampleProvider monoSource, int totalOutputChannels, bool[] activeChannels)
        {
            if (monoSource.WaveFormat.Channels != 1)
                throw new ArgumentException("Source sample provider must be mono.", nameof(monoSource));
            if (totalOutputChannels < 1)
                throw new ArgumentException("Must have at least one output channel.", nameof(totalOutputChannels));
            if (activeChannels.Length != totalOutputChannels)
                throw new ArgumentException("activeChannels length must match totalOutputChannels.", nameof(activeChannels));

            source = monoSource;
            ActiveChannels = activeChannels;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(monoSource.WaveFormat.SampleRate, totalOutputChannels);
        }

        public WaveFormat WaveFormat => waveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int channels = waveFormat.Channels;
            int framesRequested = count / channels;

            if (sourceBuffer.Length < framesRequested)
                sourceBuffer = new float[framesRequested];

            int framesRead = source.Read(sourceBuffer, 0, framesRequested);

            int idx = offset;
            for (int frame = 0; frame < framesRequested; frame++)
            {
                float sample = frame < framesRead ? sourceBuffer[frame] : 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[idx++] = ActiveChannels[ch] ? sample : 0f;
                }
            }

            return framesRequested * channels;
        }
    }
}
