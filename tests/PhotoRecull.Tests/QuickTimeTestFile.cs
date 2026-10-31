using System.Buffers.Binary;
using System.Text;

namespace PhotoRecull.Tests;

// The smallest QuickTime movie the metadata reader accepts: a movie header and
// Apple's keyed metadata (make, model, creation date), as an iPhone writes them.
internal static class QuickTimeTestFile
{
    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Write(string path, DateTime movieHeaderCreatedUtc, params (string Key, string Value)[] appleMetadata)
    {
        var keys = appleMetadata
            .SelectMany(m => (byte[])[.. U32((uint)(8 + m.Key.Length)), .. "mdta"u8, .. Encoding.ASCII.GetBytes(m.Key)])
            .ToArray();
        var items = appleMetadata.Select((m, i) => Atom(U32((uint)(i + 1)),
                Atom("data", [.. U32(1), .. U32(0), .. Encoding.UTF8.GetBytes(m.Value)])))
            .SelectMany(item => item)
            .ToArray();
        var meta = Atom("meta", [
            .. Atom("hdlr", [.. U32(0), .. U32(0), .. "mdta"u8, .. new byte[12], 0]),
            .. Atom("keys", [.. U32(0), .. U32((uint)appleMetadata.Length), .. keys]),
            .. Atom("ilst", items),
        ]);
        File.WriteAllBytes(path, [
            .. Atom("ftyp", [.. "qt  "u8, .. U32(0), .. "qt  "u8]),
            .. Atom("moov", [.. Atom("mvhd", MovieHeader(movieHeaderCreatedUtc)), .. meta]),
        ]);
    }

    private static byte[] MovieHeader(DateTime createdUtc)
    {
        var header = new byte[100];
        var seconds = (uint)(createdUtc - QuickTimeEpoch).TotalSeconds;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 600);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(24), 0x0100);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(96), 1);
        return header;
    }

    private static byte[] Atom(string type, byte[] payload) => Atom(Encoding.ASCII.GetBytes(type.PadRight(4)), payload);

    private static byte[] Atom(byte[] type, byte[] payload) => [.. U32((uint)(8 + payload.Length)), .. type, .. payload];

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
