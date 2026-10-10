using System.Buffers.Binary;
using NAudio.Wave;

namespace EdgeLink;

// Bounded jitter buffer; adaptive read rate keeps independent audio clocks from
// slowly filling/draining it. The wire is 48 kHz / stereo / signed PCM16 LE.
internal sealed class AudioBuffer : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private readonly object gate = new();
    private readonly float[] ring = new float[48000 * 2];
    private long written, read;
    private double fraction;
    private ulong nextFrame;
    private bool hasFrame, playing;
    private readonly int target;
    public long Underruns { get; private set; }
    public long DroppedFrames { get; private set; }
    public double BufferedMs { get { lock (gate) return (written - read) / 48.0; } }
    public AudioBuffer(int bufferMs) { target = bufferMs * 48; }

    public void Feed(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != 977 || packet[0] != 2) return;
        ulong first = BinaryPrimitives.ReadUInt64LittleEndian(packet[9..]);
        lock (gate)
        {
            if (hasFrame && first < nextFrame) return; // discard duplicates / late packets
            ulong gap = hasFrame ? first - nextFrame : 0;
            if (gap > 1920) { read = written; playing = false; fraction = 0; }
            else for (ulong i = 0; i < gap; i++) Put(0, 0);
            for (int i = 17; i < packet.Length; i += 4)
                Put(BinaryPrimitives.ReadInt16LittleEndian(packet[i..]) / 32768f,
                    BinaryPrimitives.ReadInt16LittleEndian(packet[(i + 2)..]) / 32768f);
            nextFrame = first + 240; hasFrame = true;
            // Bound latency even if a capture callback or network delivery arrives in bursts.
            int maximum = Math.Max(target * 3, target + 960);
            if (written - read > maximum)
            {
                long discard = written - read - target;
                read += discard; DroppedFrames += discard; fraction = 0;
            }
        }
    }
    private void Put(float left, float right)
    {
        int index = (int)(written % 48000) * 2;
        ring[index] = left; ring[index + 1] = right; written++;
    }
    public int Read(byte[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        lock (gate)
        {
            if (!playing) { if (written - read < target + 2) return count; playing = true; }
            double rate = 1 + Math.Clamp(((written - read) - target) * 0.000001, -0.002, 0.002);
            for (int outOffset = offset; outOffset + 8 <= offset + count; outOffset += 8)
            {
                if (written - read < 2) { playing = false; fraction = 0; Underruns++; break; }
                int a = (int)(read % 48000) * 2, b = (int)((read + 1) % 48000) * 2;
                float l = ring[a] + (ring[b] - ring[a]) * (float)fraction;
                float r = ring[a + 1] + (ring[b + 1] - ring[a + 1]) * (float)fraction;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(outOffset), BitConverter.SingleToInt32Bits(l));
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(outOffset + 4), BitConverter.SingleToInt32Bits(r));
                fraction += rate; long advance = (long)fraction; fraction -= advance; read += advance;
            }
        }
        return count;
    }
}
