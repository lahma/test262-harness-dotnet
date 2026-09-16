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
/// </remarks>
internal sealed class ZipArchiveFileSystem : MemoryFileSystem
{
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

                // file, materialize the contents so that later reads never touch the archive
                using var target = base.OpenFileImpl(transformed, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var source = archive.GetInputStream(entry);
                CopyTo(source, target, entry.Size);
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

    private static void CopyTo(Stream source, Stream target, long expectedSize)
    {
        // Size is known for well-formed archives; pre-sizing avoids the repeated doubling
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

            target.Write(buffer, 0, offset);
        }
        else
        {
            source.CopyTo(target);
        }
    }

    public static IFileSystem Create(UPath file, string subDirectory)
    {
        return new ZipArchiveFileSystem(file, subDirectory);
    }
}
