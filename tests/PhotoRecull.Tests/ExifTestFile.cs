using System.Buffers.Binary;
using System.Text;

namespace PhotoRecull.Tests;

// The smallest iPhone JPEG the metadata reader accepts: EXIF with make, model and
// capture time, and Apple's maker note carrying a Live Photo's content identifier.
internal static class ExifTestFile
{
    private const ushort Ascii = 2;
    private const ushort Long = 4;
    private const ushort Undefined = 7;

    public static void WriteIphoneJpeg(string path, string dateTimeOriginal, string offset, string? contentIdentifier)
    {
        var exifIfdEntries = new List<(ushort Tag, ushort Type, byte[] Value)>
        {
            (0x9003, Ascii, AsciiZ(dateTimeOriginal)),
            (0x9011, Ascii, AsciiZ(offset)),
        };
        if (contentIdentifier is not null)
            exifIfdEntries.Add((0x927C, Undefined, AppleMakerNote(contentIdentifier)));

        // IFD0 at 8: make, model and the Exif IFD's offset, then the Exif IFD.
        const int ifd0Offset = 8;
        int ifd0Size = IfdSize(3, AsciiZ("Apple").Length + AsciiZ("iPhone 16 Pro").Length);
        int exifIfdOffset = ifd0Offset + ifd0Size;
        var tiff = new List<byte>();
        tiff.AddRange("MM"u8.ToArray());
        tiff.AddRange(U16(42));
        tiff.AddRange(U32(ifd0Offset));
        tiff.AddRange(Ifd(ifd0Offset,
        [
            (0x010F, Ascii, AsciiZ("Apple")),
            (0x0110, Ascii, AsciiZ("iPhone 16 Pro")),
            (0x8769, Long, U32((uint)exifIfdOffset)),
        ]));
        tiff.AddRange(Ifd(exifIfdOffset, exifIfdEntries));

        var app1 = (byte[])[.. "Exif\0\0"u8, .. tiff];
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE1, .. U16((ushort)(app1.Length + 2)), .. app1, 0xFF, 0xD9]);
    }

    // "Apple iOS", a version and byte order, then an IFD whose offsets count from
    // the start of the maker note.
    private static byte[] AppleMakerNote(string contentIdentifier)
    {
        byte[] header = [.. "Apple iOS\0"u8, 0, 1, .. "MM"u8];
        return [.. header, .. Ifd(header.Length, [(0x0011, Ascii, AsciiZ(contentIdentifier))])];
    }

    private static int IfdSize(int entryCount, int dataBytes) => 2 + entryCount * 12 + 4 + dataBytes;

    // Values over four bytes go in a data area right after the IFD, at offsets
    // from the start of the TIFF (or maker note) that the IFD itself starts at.
    private static byte[] Ifd(int ifdOffset, List<(ushort Tag, ushort Type, byte[] Value)> entries)
    {
        var table = new List<byte>(U16((ushort)entries.Count));
        var data = new List<byte>();
        int dataOffset = ifdOffset + 2 + entries.Count * 12 + 4;
        foreach (var (tag, type, value) in entries)
        {
            table.AddRange(U16(tag));
            table.AddRange(U16(type));
            table.AddRange(U32(type == Long ? 1u : (uint)value.Length));
            if (value.Length <= 4)
                table.AddRange(value.Concat(new byte[4 - value.Length]));
            else
            {
                table.AddRange(U32((uint)(dataOffset + data.Count)));
                data.AddRange(value);
            }
        }
        table.AddRange(U32(0));
        return [.. table, .. data];
    }

    private static byte[] AsciiZ(string text) => [.. Encoding.ASCII.GetBytes(text), 0];

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
