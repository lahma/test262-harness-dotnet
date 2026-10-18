using System.Runtime.ExceptionServices;
using Zio;
using Zio.FileSystems;

namespace Test262Harness;

/// <summary>
/// A read-only, in-memory file system populated from a zip archive.
/// </summary>
/// <remarks>
/// The archive's contents are decompressed into memory up front and the archive is closed again before
/// the constructor returns. This matters for parallel consumers: a zip archive serves every read from a
/// single shared base stream, so serving reads straight from it turns concurrent test execution into a
/// serialized one. The test262 <c>test/</c> and <c>harness/</c> trees are roughly 83 MB uncompressed, which
/// is a worthwhile trade for lock-free reads.
/// <para>
/// Every file's bytes live in one buffer. The central directory is read once (<see cref="ZipCentralDirectory"/>)
/// and the entries are then inflated in parallel through the platform's native deflate implementation, each
/// worker writing only to its own disjoint ranges of the buffer.
/// </para>
/// <para>
/// The tree is a fixed set of precomputed entries rather than a <see cref="MemoryFileSystem"/>: nothing here
/// ever changes after construction, so there is nothing to lock, no node per file to create, and no path to
/// combine and normalize again on every enumeration. A read hands back a private stream over the shared,
/// immutable buffer, so any number of threads may read the same file at once whichever share mode each
/// asked for. Enumeration visits entries in the order <see cref="MemoryFileSystem"/> did - each directory's
/// children in archive order, then its sub-directories depth first - so consumers see the same order as before.
/// </para>
/// </remarks>
internal sealed class ZipArchiveFileSystem : FileSystem
{
    private readonly Dictionary<string, (int Offset, int Length)> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Child>> _directories = new(StringComparer.Ordinal);
    private readonly byte[] _content;
    private readonly DateTime _lastWriteTime;

    private readonly struct Child
    {
        public Child(UPath path, bool isDirectory, int length)
        {
            Path = path;
            Name = path.GetName();
            IsDirectory = isDirectory;
            Length = length;
        }

        public UPath Path { get; }
        public string Name { get; }
        public bool IsDirectory { get; }
        public int Length { get; }
    }

    private ZipArchiveFileSystem(UPath file, string rootName)
    {
        var archive = File.ReadAllBytes(file.FullName);
        _lastWriteTime = File.GetLastWriteTime(file.FullName);

        var item1 = $"{rootName.TrimEnd('/')}/test";
        var item2 = $"{rootName.TrimEnd('/')}/harness";

        // The entries to inflate, and where each one's bytes go in the shared buffer.
        var work = new List<(ZipCentralDirectory.Entry Entry, int Offset)>();
        var total = 0L;

        _directories[UPath.Root.FullName] = [];
        foreach (var entry in ZipCentralDirectory.Read(archive))
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
                EnsureDirectory(transformed);
                continue;
            }

            // Not every archive writes explicit directory entries, so the parent is created on
            // demand rather than assumed to have been seen already.
            var directory = transformed.GetDirectory();
            EnsureDirectory(directory);

            if (entry.Length > int.MaxValue || total + entry.Length > int.MaxValue)
            {
                throw new NotSupportedException($"Archive {file} is too large to hold in memory: {total + entry.Length} bytes uncompressed.");
            }

            if (_files.ContainsKey(transformed.FullName) || _directories.ContainsKey(transformed.FullName))
            {
                throw new IOException($"Archive {file} contains {transformed} more than once.");
            }

            _files[transformed.FullName] = ((int) total, (int) entry.Length);
            _directories[directory.FullName].Add(new Child(transformed, isDirectory: false, (int) entry.Length));
            work.Add((entry, (int) total));
            total += entry.Length;
        }

        _content = AllocateUninitialized((int) total);
        Decompress(archive, work, _content);
    }

    public static IFileSystem Create(UPath file, string subDirectory)
    {
        return new ZipArchiveFileSystem(file, subDirectory);
    }

    private static byte[] AllocateUninitialized(int length)
    {
#if NET5_0_OR_GREATER
        return GC.AllocateUninitializedArray<byte>(length);
#else
        return new byte[length];
#endif
    }

    private static void Decompress(byte[] archive, List<(ZipCentralDirectory.Entry Entry, int Offset)> work, byte[] content)
    {
        // Contiguous ranges of roughly equal uncompressed size; a handful of workers already saturates
        // memory bandwidth for entries this small.
        var workers = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
        var bounds = new int[workers + 1];
        var next = 1;
        for (var i = 0; i < work.Count && next < workers; i++)
        {
            if (work[i].Offset >= (long) content.Length * next / workers)
            {
                bounds[next++] = i;
            }
        }

        for (; next <= workers; next++)
        {
            bounds[next] = work.Count;
        }

        try
        {
            Parallel.For(0, workers, worker =>
            {
                for (var i = bounds[worker]; i < bounds[worker + 1]; i++)
                {
                    ZipCentralDirectory.Extract(archive, work[i].Entry, content, work[i].Offset);
                }
            });
        }
        catch (AggregateException ex)
        {
            // Callers tell a corrupt download from other failures by the exception type, so surface the
            // worker's own exception rather than the aggregate.
            ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
            throw;
        }
    }

    private void EnsureDirectory(UPath directory)
    {
        if (directory.IsNull || _directories.ContainsKey(directory.FullName))
        {
            return;
        }

        if (_files.ContainsKey(directory.FullName))
        {
            throw new IOException($"Archive contains {directory} both as a file and as a directory.");
        }

        // parents first, so every directory is listed under its parent in the order it was first seen
        var parent = directory.GetDirectory();
        EnsureDirectory(parent);
        _directories[directory.FullName] = [];
        _directories[parent.FullName].Add(new Child(directory, isDirectory: true, length: 0));
    }

    private List<Child> GetDirectory(UPath path)
    {
        if (_directories.TryGetValue(path.FullName, out var children))
        {
            return children;
        }

        if (_files.ContainsKey(path.FullName))
        {
            throw new IOException($"The path `{path}` is a file, not a directory.");
        }

        throw new DirectoryNotFoundException($"Could not find directory `{path}`.");
    }

    private static UnauthorizedAccessException ReadOnly(UPath path)
    {
        return new UnauthorizedAccessException($"The test262 archive file system is read-only: `{path}`.");
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        if (mode != FileMode.Open || (access & FileAccess.Write) != 0)
        {
            throw ReadOnly(path);
        }

        if (!_files.TryGetValue(path.FullName, out var file))
        {
            throw new FileNotFoundException($"Could not find file `{path}`.");
        }

        // A private, non-writable view over a buffer nothing mutates after construction. The buffer is visible
        // so a reader that knows it is in memory can decode it in place instead of copying it out first.
        return new MemoryStream(_content, file.Offset, file.Length, writable: false, publiclyVisible: true);
    }

    protected override bool FileExistsImpl(UPath path) => _files.ContainsKey(path.FullName);

    protected override bool DirectoryExistsImpl(UPath path) => _directories.ContainsKey(path.FullName);

    protected override long GetFileLengthImpl(UPath path)
    {
        if (!_files.TryGetValue(path.FullName, out var file))
        {
            throw new FileNotFoundException($"Could not find file `{path}`.");
        }

        return file.Length;
    }

    protected override FileAttributes GetAttributesImpl(UPath path)
    {
        if (_files.ContainsKey(path.FullName))
        {
            return FileAttributes.ReadOnly;
        }

        if (_directories.ContainsKey(path.FullName))
        {
            return FileAttributes.Directory | FileAttributes.ReadOnly;
        }

        throw new FileNotFoundException($"Could not find `{path}`.");
    }

    protected override DateTime GetCreationTimeImpl(UPath path) => GetTime(path);

    protected override DateTime GetLastAccessTimeImpl(UPath path) => GetTime(path);

    protected override DateTime GetLastWriteTimeImpl(UPath path) => GetTime(path);

    private DateTime GetTime(UPath path)
    {
        return _files.ContainsKey(path.FullName) || _directories.ContainsKey(path.FullName) ? _lastWriteTime : DefaultFileTime;
    }

    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        _ = GetDirectory(path);
        return EnumerateItemsCore(path, searchOption, searchPredicate);
    }

    private IEnumerable<FileSystemItem> EnumerateItemsCore(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        DateTimeOffset time = _lastWriteTime;
        var foldersToProcess = new List<UPath> { path };
        while (foldersToProcess.Count > 0)
        {
            var directoryPath = foldersToProcess[0];
            foldersToProcess.RemoveAt(0);
            var dirIndex = 0;

            foreach (var child in _directories[directoryPath.FullName])
            {
                if (child.IsDirectory && searchOption == SearchOption.AllDirectories)
                {
                    foldersToProcess.Insert(dirIndex++, child.Path);
                }

                var item = new FileSystemItem
                {
                    FileSystem = this,
                    AbsolutePath = child.Path,
                    Path = child.Path,
                    Attributes = child.IsDirectory ? FileAttributes.Directory | FileAttributes.ReadOnly : FileAttributes.ReadOnly,
                    CreationTime = time,
                    LastWriteTime = time,
                    LastAccessTime = time,
                    Length = child.Length,
                };

                if (searchPredicate is null || searchPredicate(ref item))
                {
                    yield return item;
                }
            }
        }
    }

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        var search = SearchPattern.Parse(ref path, ref searchPattern);
        _ = GetDirectory(path);
        return EnumeratePathsCore(path, search, searchOption, searchTarget);
    }

    private IEnumerable<UPath> EnumeratePathsCore(UPath path, SearchPattern search, SearchOption searchOption, SearchTarget searchTarget)
    {
        // Within one directory the matches come back sorted, as MemoryFileSystem returned them.
        var foldersToProcess = new List<UPath> { path };
        var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
        while (foldersToProcess.Count > 0)
        {
            var directoryPath = foldersToProcess[0];
            foldersToProcess.RemoveAt(0);
            var dirIndex = 0;
            entries.Clear();

            foreach (var child in _directories[directoryPath.FullName])
            {
                if (child.IsDirectory && searchOption == SearchOption.AllDirectories)
                {
                    foldersToProcess.Insert(dirIndex++, child.Path);
                }

                var wanted = child.IsDirectory ? searchTarget != SearchTarget.File : searchTarget != SearchTarget.Directory;
                if (wanted && search.Match(child.Name))
                {
                    entries.Add(child.Path);
                }
            }

            foreach (var entry in entries)
            {
                yield return entry;
            }
        }
    }

    protected override bool TryResolveLinkTargetImpl(UPath linkPath, out UPath resolvedPath)
    {
        resolvedPath = default;
        return false;
    }

    protected override bool CanWatchImpl(UPath path) => false;

    protected override IFileSystemWatcher WatchImpl(UPath path) => throw new NotSupportedException("The test262 archive file system does not change, so it cannot be watched.");

    protected override string ConvertPathToInternalImpl(UPath path) => path.FullName;

    protected override UPath ConvertPathFromInternalImpl(string innerPath) => new(innerPath);

    protected override void CreateDirectoryImpl(UPath path) => throw ReadOnly(path);

    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath) => throw ReadOnly(srcPath);

    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive) => throw ReadOnly(path);

    protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite) => throw ReadOnly(destPath);

    protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors) => throw ReadOnly(destPath);

    protected override void MoveFileImpl(UPath srcPath, UPath destPath) => throw ReadOnly(srcPath);

    protected override void DeleteFileImpl(UPath path) => throw ReadOnly(path);

    protected override void SetAttributesImpl(UPath path, FileAttributes attributes) => throw ReadOnly(path);

    protected override void SetCreationTimeImpl(UPath path, DateTime time) => throw ReadOnly(path);

    protected override void SetLastAccessTimeImpl(UPath path, DateTime time) => throw ReadOnly(path);

    protected override void SetLastWriteTimeImpl(UPath path, DateTime time) => throw ReadOnly(path);

    protected override void CreateSymbolicLinkImpl(UPath path, UPath pathToTarget) => throw ReadOnly(path);
}
