using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EdgeLink;

// v1: nonce(12) | ciphertext | GCM tag(16). A fresh random nonce for every packet.
internal static class Wire
{
    public static byte[] Seal(ReadOnlySpan<byte> data, byte[] key)
    {
        var packet = new byte[data.Length + 28];
        RandomNumberGenerator.Fill(packet.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(packet.AsSpan(0, 12), data, packet.AsSpan(12, data.Length), packet.AsSpan(12 + data.Length, 16));
        return packet;
    }
    public static byte[] Open(ReadOnlySpan<byte> packet, byte[] key)
    {
        if (packet.Length < 28 || packet.Length > 16384) throw new InvalidDataException("Invalid packet length");
        var plain = new byte[packet.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(packet[..12], packet.Slice(12, plain.Length), packet[^16..], plain);
        return plain;
    }
    public static byte[] SessionKey(byte[] master, byte[] clientNonce, byte[] serverNonce) =>
        SHA256.HashData(master.Concat(clientNonce).Concat(serverNonce).Concat(Encoding.ASCII.GetBytes("EdgeLink-v1")).ToArray());

    public static async Task Send(Stream stream, object message, byte[] key, CancellationToken token)
    {
        var packet = Seal(JsonSerializer.SerializeToUtf8Bytes(message), key);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, packet.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(packet, token);
    }
    public static async Task<JsonElement> Receive(Stream stream, byte[] key, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 28 || length > 16384) throw new InvalidDataException("Invalid frame length");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        using var document = JsonDocument.Parse(Open(data, key));
        return document.RootElement.Clone();
    }

    public static byte[] Move(uint epoch, ulong sequence, double x, double y)
    {
        var data = new byte[29]; data[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(5), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(13), BitConverter.DoubleToInt64Bits(x));
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(21), BitConverter.DoubleToInt64Bits(y));
        return data;
    }
}
