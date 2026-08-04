using System;
using NAudio.Wave;

namespace AsioSignalGenerator.Audio
{
    /// <summary>
    /// Minimal adapter that turns a 32-bit IEEE float ISampleProvider into an IWaveProvider,
    /// which is what AsioOut.Init(...) expects.
    /// </summary>
    public sealed class FloatSampleToWaveProvider : IWaveProvider
    {
        private readonly ISampleProvider source;
        private float[] floatBuffer = Array.Empty<float>();

        public FloatSampleToWaveProvider(ISampleProvider source)
        {
            if (source.WaveFormat.BitsPerSample != 32 || source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                throw new ArgumentException("Source must be 32-bit IEEE float.", nameof(source));

            this.source = source;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            int samplesRequired = count / 4; // 4 bytes per float sample

            if (floatBuffer.Length < samplesRequired)
                floatBuffer = new float[samplesRequired];

            int samplesRead = source.Read(floatBuffer, 0, samplesRequired);
            Buffer.BlockCopy(floatBuffer, 0, buffer, offset, samplesRead * 4);
            return samplesRead * 4;
        }
    }
}
