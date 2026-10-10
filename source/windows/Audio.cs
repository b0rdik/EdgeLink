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
    private readonly int minimumTarget;
    private int target, renderFrames = 480;
    private double filteredError;
    private float lastLeft, lastRight;
    private int fadeIn;
    public long Underruns { get; private set; }
    public long DroppedFrames { get; private set; }
    public double BufferedMs { get { lock (gate) return (written - read) / 48.0; } }
    public double TargetMs { get { lock (gate) return target / 48.0; } }
    public AudioBuffer(int bufferMs) { minimumTarget = target = Math.Clamp(bufferMs, 10, 60) * 48; }
    public void ObserveCaptureBatch(int frames)
    {
        // Capture callbacks can contain several 5 ms wire packets at once.
        if (frames < 1 || frames > 48000) return;
        lock (gate) target = Math.Max(target, Math.Clamp(frames, minimumTarget, 2880));
    }

    public void Feed(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != 977 || packet[0] != 2) return;
        ulong first = BinaryPrimitives.ReadUInt64LittleEndian(packet[9..]);
        lock (gate)
        {
            if (hasFrame && first < nextFrame) return; // discard duplicates / late packets
            ulong gap = hasFrame ? first - nextFrame : 0;
            if (gap > 1920) { read = written; playing = false; fraction = 0; filteredError = 0; }
            else for (ulong i = 0; i < gap; i++) Put(0, 0);
            for (int i = 17; i < packet.Length; i += 4)
                Put(BinaryPrimitives.ReadInt16LittleEndian(packet[i..]) / 32768f,
                    BinaryPrimitives.ReadInt16LittleEndian(packet[(i + 2)..]) / 32768f);
            nextFrame = first + 240; hasFrame = true;
            // Bound latency even if a capture callback or network delivery arrives in bursts.
            int maximum = Math.Max(target * 3, target + renderFrames + 960);
            if (written - read > maximum)
            {
                long discard = written - read - target;
                read += discard; DroppedFrames += discard; fraction = 0; fadeIn = 48;
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
            int requested = count / 8;
            renderFrames = Math.Max(renderFrames, requested);
            // Reserve enough for the actual WASAPI request before playback.
            if (!playing) {
                if (written - read < target + requested + 3) return count;
                playing = true; fadeIn = 48;
            }
            filteredError += 0.02 * ((written - read - requested - target) - filteredError);
            double rate = 1 + Math.Clamp(filteredError * 0.000001, -0.001, 0.001);
            int needed = (int)Math.Ceiling(requested * rate + fraction) + 2;
            if (written - read < needed) {
                // Rebuffer at the live edge, with a 1 ms fade instead of a click.
                for (int i = 0; i < Math.Min(48, requested); i++) WriteSample(buffer, offset + i * 8,
                    lastLeft * (1 - (i + 1) / 48f), lastRight * (1 - (i + 1) / 48f));
                read = written; playing = false; fraction = 0; filteredError = 0;
                lastLeft = lastRight = 0; Underruns++;
                target = Math.Min(2880, target + 240);
                return count;
            }
            for (int outOffset = offset; outOffset + 8 <= offset + count; outOffset += 8)
            {
                int a = (int)(read % 48000) * 2, b = (int)((read + 1) % 48000) * 2;
                float l = ring[a] + (ring[b] - ring[a]) * (float)fraction;
                float r = ring[a + 1] + (ring[b + 1] - ring[a + 1]) * (float)fraction;
                if (fadeIn > 0) { float gain = (49 - fadeIn) / 48f; l *= gain; r *= gain; fadeIn--; }
                WriteSample(buffer, outOffset, l, r); lastLeft = l; lastRight = r;
                fraction += rate; long advance = (long)fraction; fraction -= advance; read += advance;
            }
        }
        return count;
    }
    private static void WriteSample(byte[] buffer, int offset, float left, float right)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), BitConverter.SingleToInt32Bits(left));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset + 4), BitConverter.SingleToInt32Bits(right));
    }
}
