using ICSharpCode.SharpZipLib.Zip;
using Zio;
using Zio.FileSystems;

namespace Test262Harness;

internal sealed class ZipArchiveFileSystem : MemoryFileSystem
{
    private readonly string _rootName;
    private readonly ZipFile _archive;
    private readonly Dictionary<string, long> _entryCache = new();

    private ZipArchiveFileSystem(UPath file, string rootName)
    {
        _rootName = rootName;
        _archive = new ZipFile(File.OpenRead(file.FullName));

        var item1 = $"{rootName.TrimEnd('/')}/test";
        var item2 = $"{rootName.TrimEnd('/')}/harness";

        // trigger file system creation to build a faster tree
        var createdDirectories = new HashSet<UPath>();
        foreach (ZipEntry entry in _archive)
        {
            var name = entry.Name;
            if (!name.StartsWith(item1, StringComparison.Ordinal) && !name.StartsWith(item2, StringComparison.Ordinal))
            {
                continue;
            }

            var transformed = name.Substring(rootName.Length);
            if (name.EndsWith('/'))
            {
                // directory
                if (createdDirectories.Add(transformed))
                {
                    base.CreateDirectory(transformed);
                }
            }
            else
            {
                // file
                using var _ = base.OpenFileImpl(transformed, FileMode.CreateNew, FileAccess.Read, FileShare.ReadWrite);
                _entryCache[transformed] = entry.ZipFileIndex;
            }
        }
    }

    public static IFileSystem Create(UPath file, string subDirectory)
    {
        return new ZipArchiveFileSystem(file, subDirectory);
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        if (!_entryCache.TryGetValue(path.FullName, out var entryIndex))
        {
            throw new FileNotFoundException($"Could not find file `{path}`.");
        }
        return _archive.GetInputStream(entryIndex);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _archive.Close();
        }
    }
}
