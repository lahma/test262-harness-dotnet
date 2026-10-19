using System.Diagnostics;
using System.IO.Compression;
using Zio.FileSystems;

namespace Test262Harness;

public static class Test262StreamExtensions
{
    private static readonly SemaphoreSlim _downloadLock = new(1, 1);

    /// <summary>
    /// Downloads the test262 repository and initializes Test262Stream instance based on download test suite.
    /// </summary>
    /// <param name="commitSha">SHA of the github commit that will be the version to download.</param>
    /// <param name="tempPath">Optional temp file path to use storing the file, defaults to Path.GetTempPath().</param>
    /// <param name="extract">
    /// Whether to extract the downloaded archive, makes read operations faster and allows multi-threaded reading, with cost of initial slow extract and disk space usage.
    /// </param>
    /// <param name="configure">Callback to call to configure options.</param>
    /// <returns>Test262Stream instance initialized from GitHub repository.</returns>
    public static async Task<Test262Stream> FromGitHub(
        string commitSha,
        string? tempPath = null,
        bool extract = false,
        Action<Test262StreamOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new ArgumentException($"Invalid commit SHA: {commitSha}", nameof(commitSha));
        }

        tempPath ??= Path.GetTempPath();
        var tempFile = Path.Combine(tempPath, $"test262-{commitSha}.zip");
        var zipSubDirectory = $"test262-{commitSha}";

        var tempOptions = new Test262StreamOptions(null!);
        configure?.Invoke(tempOptions);

        if (File.Exists(tempFile))
        {
            tempOptions.LogInfo("Found test262 repository archive from {0}", tempFile);
            try
            {
                // Opening the suite reads the whole archive anyway, so that is the validation too.
                return Open(tempFile, tempPath, zipSubDirectory, extract, configure, tempOptions);
            }
            catch (InvalidDataException)
            {
                // A truncated or damaged download; anything else would fail the same way on a fresh copy.
                tempOptions.LogError("Could not open the archive, deleting it and downloading again.");
            }
        }

        await Download(commitSha, tempFile, tempOptions.LogInfo);
        return Open(tempFile, tempPath, zipSubDirectory, extract, configure, tempOptions);
    }

    private static Test262Stream Open(
        string archive,
        string tempPath,
        string zipSubDirectory,
        bool extract,
        Action<Test262StreamOptions>? configure,
        Test262StreamOptions logOptions)
    {
        if (!extract)
        {
            return Test262Stream.FromZipArchive(archive, zipSubDirectory, configure);
        }

        var directory = Path.Combine(tempPath, zipSubDirectory);
        if (!Directory.Exists(directory))
        {
            // Extract next to the target and move it into place, so an interrupted extraction never leaves
            // a partial tree behind that a later run would take for a complete one.
            logOptions.LogInfo("Extracting archive...");
            var staging = Path.Combine(tempPath, $"{zipSubDirectory}.extracting-{Guid.NewGuid():N}");
            try
            {
                ZipFile.ExtractToDirectory(archive, staging);
                try
                {
                    Directory.Move(Path.Combine(staging, zipSubDirectory), directory);
                }
                catch (IOException) when (Directory.Exists(directory))
                {
                    // another process finished extracting the same commit first
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }

        // Zio addresses the physical file system with its own path syntax (/mnt/c/... on Windows).
        var sourcePath = new PhysicalFileSystem().ConvertPathFromInternal(directory).FullName;
        logOptions.LogInfo("Building test262stream from path {0}", sourcePath);

        return Test262Stream.FromDirectory(sourcePath, configure);
    }

    private static async Task Download(
        string commitSha,
        string tempFile,
        Logger logger)
    {
        await _downloadLock.WaitAsync();

        try
        {
            var uri = $"https://github.com/tc39/test262/archive/{commitSha}.zip";

            logger("Loading test262 repository archive from {0}", uri);

            var sw = Stopwatch.StartNew();

            // Download beside the target and move it into place, so neither an interrupted download nor a
            // second process downloading the same commit can leave a half-written archive under the real name.
            var partial = $"{tempFile}.{Guid.NewGuid():N}.partial";
            try
            {
                using (var client = new HttpClient())
                using (var downloadStream = await client.GetStreamAsync(uri))
                using (var writeStream = File.Create(partial))
                {
                    await downloadStream.CopyToAsync(writeStream);
                }

                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                try
                {
                    File.Move(partial, tempFile);
                }
                catch (IOException) when (File.Exists(tempFile))
                {
                    // another process finished downloading the same commit first
                }
            }
            finally
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }

            sw.Stop();

            logger("File downloaded and saved to {0} in {1}", tempFile, sw.Elapsed);
        }
        finally
        {
            _downloadLock.Release();
        }
    }
}
