using System.Diagnostics;
using System.IO.Compression;

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

        var tempOptions = new Test262StreamOptions(null!);
        configure?.Invoke(tempOptions);

        var ok = false;
        var delete = false;
        if (File.Exists(tempFile))
        {
            tempOptions.LogInfo("Found test262 repository archive from {0}", tempFile);
            try
            {
                using var _ = ZipFile.OpenRead(tempFile);
                ok = true;
            }
            catch
            {
                tempOptions.LogError("Could not open the archive, deleting it and downloading again.");
                ok = false;
                delete = true;
            }
        }

        if (!ok)
        {
            await Download(commitSha, delete, tempFile, tempOptions.LogInfo);
        }

        var zipSubDirectory = $"test262-{commitSha}";

        if (extract)
        {
            tempOptions.LogInfo("Extracting archive...");
            ZipFile.ExtractToDirectory(tempFile, tempPath);

            // zio wants /mnt/c format
            var sourcePath = Path.Combine(tempPath, zipSubDirectory)
                .Replace(@"C:\", "/mnt/c/")
                .Replace(@"D:\", "/mnt/d/")
                .Replace(@"E:\", "/mnt/e/");

            tempOptions.LogInfo("Building test262stream from path {0}", sourcePath);

            return Test262Stream.FromDirectory(sourcePath);
        }

        return Test262Stream.FromZipArchive(tempFile, zipSubDirectory, configure);
    }

    private static async Task Download(
        string commitSha,
        bool delete,
        string tempFile,
        Logger logger)
    {
        await _downloadLock.WaitAsync();

        try
        {
            if (delete && File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            var uri = $"https://github.com/tc39/test262/archive/{commitSha}.zip";

            logger("Loading test262 repository archive from {0}", uri);

            var sw = Stopwatch.StartNew();

            using var client = new HttpClient();

            using var downloadStream = await client.GetStreamAsync(uri);
            using var writeStream = File.OpenWrite(tempFile);

            await downloadStream.CopyToAsync(writeStream);

            sw.Stop();

            logger("File downloaded and saved to {0} in {1}", tempFile, sw.Elapsed);
        }
        finally
        {
            _downloadLock.Release();
        }
    }
}
