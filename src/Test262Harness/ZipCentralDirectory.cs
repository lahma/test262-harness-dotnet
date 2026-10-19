using System.IO.Compression;
using System.Text;

namespace Test262Harness;

/// <summary>
/// Reads a zip archive's central directory straight out of the archive bytes, and inflates single entries
/// from them.
/// </summary>
/// <remarks>
/// <see cref="ZipArchive"/> can only be used by one thread at a time, so inflating entries in parallel
/// through it means one archive instance - and one parse of the ~10 MB central directory into ~59k
/// <see cref="ZipArchiveEntry"/> objects - per worker. Reading the directory once into these small records
/// lets every worker run its own <see cref="DeflateStream"/> over its slice of the shared, immutable bytes.
/// Zip64 archives are supported, since test262 is approaching the 65,535 entries a classic one can hold;
/// encrypted entries and compression methods other than stored and deflate are not.
/// </remarks>
internal static class ZipCentralDirectory
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint CentralDirectoryHeaderSignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const ushort Zip64ExtraFieldTag = 0x0001;

    private const ushort Stored = 0;
    private const ushort Deflate = 8;

    internal readonly struct Entry
    {
        public Entry(string name, long localHeaderOffset, long compressedLength, long length, ushort method, ushort flags)
        {
            Name = name;
            LocalHeaderOffset = localHeaderOffset;
            CompressedLength = compressedLength;
            Length = length;
            Method = method;
            Flags = flags;
        }

        public string Name { get; }
        public long LocalHeaderOffset { get; }
        public long CompressedLength { get; }
        public long Length { get; }
        public ushort Method { get; }
        public ushort Flags { get; }
    }

    public static List<Entry> Read(byte[] archive)
    {
        var end = FindEndOfCentralDirectory(archive);
        long count = ReadUInt16(archive, end + 10);
        long size = ReadUInt32(archive, end + 12);
        long offset = ReadUInt32(archive, end + 16);

        // A classic archive can hold exactly 65,535 entries, and writers such as git archive only add the Zip64
        // records once a value no longer fits: a count at the limit without a locator is the real count.
        var countAtLimitWithoutZip64 = false;
        if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
        {
            var locator = end - 20;
            if (locator >= 0 && ReadUInt32(archive, locator) == Zip64EndOfCentralDirectoryLocatorSignature)
            {
                var record = ToIndex(archive, ReadInt64(archive, locator + 8), 56);
                if (ReadUInt32(archive, record) != Zip64EndOfCentralDirectorySignature)
                {
                    throw new InvalidDataException("Zip64 end of central directory record not found.");
                }

                count = ReadInt64(archive, record + 32);
                offset = ReadInt64(archive, record + 48);
            }
            else if (size == uint.MaxValue || offset == uint.MaxValue)
            {
                throw new InvalidDataException("Zip64 end of central directory locator not found.");
            }
            else
            {
                countAtLimitWithoutZip64 = true;
            }
        }

        if (count > archive.Length / 46)
        {
            throw new InvalidDataException($"Central directory claims {count} entries, more than the archive can hold.");
        }

        var entries = new List<Entry>((int) count);
        var position = ToIndex(archive, offset, 0);
        for (long i = 0; i < count; i++)
        {
            if (ReadUInt32(archive, position) != CentralDirectoryHeaderSignature)
            {
                throw new InvalidDataException($"Central directory entry {i} is corrupt.");
            }

            var flags = ReadUInt16(archive, position + 8);
            var method = ReadUInt16(archive, position + 10);
            long compressedLength = ReadUInt32(archive, position + 20);
            long length = ReadUInt32(archive, position + 24);
            var nameLength = ReadUInt16(archive, position + 28);
            var extraLength = ReadUInt16(archive, position + 30);
            var commentLength = ReadUInt16(archive, position + 32);
            long localHeaderOffset = ReadUInt32(archive, position + 42);

            var nameStart = ToIndex(archive, position + 46, nameLength);
            var name = Encoding.UTF8.GetString(archive, nameStart, nameLength);

            if (length == uint.MaxValue || compressedLength == uint.MaxValue || localHeaderOffset == uint.MaxValue)
            {
                ReadZip64ExtraField(archive, ToIndex(archive, nameStart + nameLength, extraLength), extraLength, ref length, ref compressedLength, ref localHeaderOffset);
            }

            entries.Add(new Entry(name, localHeaderOffset, compressedLength, length, method, flags));
            position = nameStart + nameLength + extraLength + commentLength;
        }

        // An archive with more entries than the classic count holds has to say so in Zip64 records; without
        // them, reading 65,535 entries would silently drop the rest.
        if (countAtLimitWithoutZip64 && position != offset + size)
        {
            throw new InvalidDataException("Central directory holds more entries than its end record counts, and there is no Zip64 record.");
        }

        return entries;
    }

    /// <summary>
    /// Inflates one entry into <paramref name="destination"/> at <paramref name="destinationOffset"/>; the
    /// destination range must be exactly the entry's uncompressed length.
    /// </summary>
    public static void Extract(byte[] archive, Entry entry, byte[] destination, int destinationOffset)
    {
        if ((entry.Flags & 1) != 0)
        {
            throw new NotSupportedException($"Entry {entry.Name} is encrypted.");
        }

        var header = ToIndex(archive, entry.LocalHeaderOffset, 30);
        if (ReadUInt32(archive, header) != LocalFileHeaderSignature)
        {
            throw new InvalidDataException($"Local header of entry {entry.Name} is corrupt.");
        }

        var dataStart = header + 30 + ReadUInt16(archive, header + 26) + ReadUInt16(archive, header + 28);
        var data = ToIndex(archive, dataStart, entry.CompressedLength);
        var length = (int) entry.Length;

        switch (entry.Method)
        {
            case Stored:
                if (entry.CompressedLength != entry.Length)
                {
                    throw new InvalidDataException($"Stored entry {entry.Name} has mismatching sizes.");
                }

                Buffer.BlockCopy(archive, data, destination, destinationOffset, length);
                return;

            case Deflate:
                using (var inflater = new DeflateStream(new MemoryStream(archive, data, (int) entry.CompressedLength, writable: false), CompressionMode.Decompress))
                {
                    var read = 0;
                    while (read < length)
                    {
                        var n = inflater.Read(destination, destinationOffset + read, length - read);
                        if (n <= 0)
                        {
                            throw new InvalidDataException($"Entry {entry.Name} ended after {read} of {length} bytes.");
                        }
                        read += n;
                    }

                    if (inflater.ReadByte() >= 0)
                    {
                        throw new InvalidDataException($"Entry {entry.Name} is longer than the {length} bytes its header declares.");
                    }
                }
                return;

            default:
                throw new NotSupportedException($"Entry {entry.Name} uses unsupported compression method {entry.Method}.");
        }
    }

    private static int FindEndOfCentralDirectory(byte[] archive)
    {
        // The record is 22 bytes followed by a comment of at most 65,535 bytes, so it is near the end.
        var lowest = Math.Max(0, archive.Length - 22 - ushort.MaxValue);
        for (var i = archive.Length - 22; i >= lowest; i--)
        {
            if (archive[i] == 0x50 && ReadUInt32(archive, i) == EndOfCentralDirectorySignature)
            {
                return i;
            }
        }

        throw new InvalidDataException("End of central directory record not found; the file is not a zip archive or is truncated.");
    }

    private static void ReadZip64ExtraField(byte[] archive, int start, int length, ref long uncompressed, ref long compressed, ref long localHeaderOffset)
    {
        var end = start + length;
        while (start + 4 <= end)
        {
            var tag = ReadUInt16(archive, start);
            var size = ReadUInt16(archive, start + 2);
            var field = start + 4;
            if (tag == Zip64ExtraFieldTag)
            {
                // Only the values whose 32-bit fields overflowed are present, in this order.
                var fieldEnd = field + size;
                if (uncompressed == uint.MaxValue)
                {
                    uncompressed = ReadZip64Value(archive, ref field, fieldEnd);
                }

                if (compressed == uint.MaxValue)
                {
                    compressed = ReadZip64Value(archive, ref field, fieldEnd);
                }

                if (localHeaderOffset == uint.MaxValue)
                {
                    localHeaderOffset = ReadZip64Value(archive, ref field, fieldEnd);
                }

                return;
            }

            start = field + size;
        }

        throw new InvalidDataException("Zip64 extra field missing for an entry that needs it.");
    }

    private static long ReadZip64Value(byte[] archive, ref int field, int fieldEnd)
    {
        if (field + 8 > fieldEnd)
        {
            throw new InvalidDataException("Zip64 extra field is shorter than the values it has to hold.");
        }

        var value = ReadInt64(archive, field);
        field += 8;
        return value;
    }

    /// <summary>
    /// Checks that <paramref name="length"/> bytes at <paramref name="offset"/> lie inside the archive.
    /// </summary>
    private static int ToIndex(byte[] archive, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > archive.Length - length)
        {
            throw new InvalidDataException("Zip archive is truncated or corrupt.");
        }

        return (int) offset;
    }

    private static ushort ReadUInt16(byte[] archive, int offset)
    {
        ToIndex(archive, offset, 2);
        return (ushort) (archive[offset] | archive[offset + 1] << 8);
    }

    private static uint ReadUInt32(byte[] archive, int offset)
    {
        ToIndex(archive, offset, 4);
        return (uint) (archive[offset] | archive[offset + 1] << 8 | archive[offset + 2] << 16 | archive[offset + 3] << 24);
    }

    private static long ReadInt64(byte[] archive, long offset)
    {
        var index = ToIndex(archive, offset, 8);
        var value = ReadUInt32(archive, index) | (ulong) ReadUInt32(archive, index + 4) << 32;
        if (value > long.MaxValue)
        {
            throw new InvalidDataException("Zip64 value out of range.");
        }

        return (long) value;
    }
}
