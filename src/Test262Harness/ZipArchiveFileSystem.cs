using ICSharpCode.SharpZipLib.Zip;
using Zio;
using Zio.FileSystems;

namespace Test262Harness;

/// <summary>
/// An in-memory file system populated from a zip archive.
/// </summary>
/// <remarks>
/// The archive's contents are decompressed into memory up front and the archive is closed again before
/// the constructor returns. This matters for parallel consumers: <see cref="ZipFile"/> serves every read
/// from a single shared base stream guarded by one lock, so serving reads straight from the archive turns
/// concurrent test execution into a serialized one. The test262 <c>test/</c> and <c>harness/</c> trees are
/// roughly 83 MB uncompressed, which is a worthwhile trade for lock-free reads.
/// <para>
/// The content is held here rather than written into the base <see cref="MemoryFileSystem"/> so that a read
/// hands back a private stream over a shared, immutable buffer and never enters the node's sharing bookkeeping.
/// That bookkeeping is what a read-only corpus cannot satisfy: callers reach the same file through different
/// overloads, and Zio's <c>OpenFile(path, mode, access)</c> defaults to <see cref="FileShare.None"/>, so two
/// concurrent readers of one file can refuse each other. The base file system still holds the tree, so
/// enumeration is unaffected.
/// </para>
/// </remarks>
internal sealed class ZipArchiveFileSystem : MemoryFileSystem
{
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);

    private ZipArchiveFileSystem(UPath file, string rootName)
    {
        using var archive = new ZipFile(File.OpenRead(file.FullName));

        var item1 = $"{rootName.TrimEnd('/')}/test";
        var item2 = $"{rootName.TrimEnd('/')}/harness";

        // trigger file system creation to build a faster tree
        var createdDirectories = new HashSet<UPath>();
        foreach (ZipEntry entry in archive)
        {
            var name = entry.Name;
            if (!name.StartsWith(item1, StringComparison.Ordinal) && !name.StartsWith(item2, StringComparison.Ordinal))
            {
                continue;
            }

            UPath transformed = name.Substring(rootName.Length);
            if (name.EndsWith('/'))
            {
                // directory
                EnsureDirectory(transformed, createdDirectories);
            }
            else
            {
                // Not every archive writes explicit directory entries, so the parent is created on
                // demand rather than assumed to have been seen already.
                EnsureDirectory(transformed.GetDirectory(), createdDirectories);

                // The node carries the tree; the bytes below are what a read is actually served from.
                using (base.OpenFileImpl(transformed, FileMode.CreateNew, FileAccess.Read, FileShare.ReadWrite))
                {
                }

                using var source = archive.GetInputStream(entry);
                _contents[transformed.FullName] = ReadAllBytes(source, entry.Size);
            }
        }
    }

    private void EnsureDirectory(UPath directory, HashSet<UPath> created)
    {
        if (directory.IsNull || directory == UPath.Root || !created.Add(directory))
        {
            return;
        }

        // Zio creates the intermediate directories too, but they are recorded here as well so that
        // a later entry for one of them does not trigger a redundant call.
        base.CreateDirectory(directory);
        for (var parent = directory.GetDirectory(); !parent.IsNull && parent != UPath.Root; parent = parent.GetDirectory())
        {
            created.Add(parent);
        }
    }

    private static byte[] ReadAllBytes(Stream source, long expectedSize)
    {
        // Size is known for well-formed archives; pre-sizing avoids the repeated doubling a
        // MemoryStream would otherwise do for each of the ~54k small files.
        if (expectedSize > 0 && expectedSize <= int.MaxValue)
        {
            var buffer = new byte[(int) expectedSize];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = source.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }

            return offset == buffer.Length ? buffer : buffer.AsSpan(0, offset).ToArray();
        }

        using var all = new MemoryStream();
        source.CopyTo(all);
        return all.ToArray();
    }

    public static IFileSystem Create(UPath file, string subDirectory)
    {
        return new ZipArchiveFileSystem(file, subDirectory);
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        if (!_contents.TryGetValue(path.FullName, out var content))
        {
            throw new FileNotFoundException($"Could not find file `{path}`.");
        }

        // A private, non-writable view over a buffer nothing mutates after construction: any number of
        // threads may hold one at once, whichever share mode each of them asked for.
        return new MemoryStream(content, writable: false);
    }
}
