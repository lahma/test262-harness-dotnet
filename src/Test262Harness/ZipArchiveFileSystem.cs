using System.IO.Compression;
using Zio;
using Zio.FileSystems;

namespace Test262Harness;

internal sealed class ZipArchiveFileSystem : MemoryFileSystem
{
    private readonly string _rootName;
    private readonly ZipArchive _archive;

    private ZipArchiveFileSystem(UPath file, string rootName)
    {
        _rootName = rootName;
        _archive = new ZipArchive(File.OpenRead(file.FullName), ZipArchiveMode.Read);

        var item1 = rootName.TrimEnd('/') + "/" + "test";
        var item2 = rootName.TrimEnd('/') + "/" + "harness";

        // trigger file system creation to build a faster tree
        var createdDirectories = new HashSet<UPath>();
        foreach (var entry in _archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith(item1) && !name.StartsWith(item2))
            {
                continue;
            }

            var transformed = name.Substring(rootName.Length);
            if (name.EndsWith("/"))
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
            }
        }
    }

    public static IFileSystem Create(UPath file, string subDirectory)
    {
        return new ZipArchiveFileSystem(file, subDirectory);
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        var archivePath = _rootName + path.FullName;
        var entry = _archive.GetEntry(archivePath) ?? throw new FileNotFoundException($"Could not find file `{path}`.");
        return entry.Open();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _archive.Dispose();
        }
    }
}
