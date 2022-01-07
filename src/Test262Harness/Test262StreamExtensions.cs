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
        tempPath ??= Path.GetTempPath();
        var tempFile = Path.Combine(tempPath, $"test262-{commitSha}.zip");

        var ok = false;
        var delete = false;
        if (File.Exists(tempFile))
        {
            Console.WriteLine($"Found test262 repository archive from {tempFile}");
            try
            {
                using var _ = ZipFile.OpenRead(tempFile);
                ok = true;
            }
            catch
            {
                Console.Error.WriteLine("Could not open the archive, deleting it and downloading again.");
                ok = false;
                delete = true;
            }
        }

        if (!ok)
        {
            await Download(commitSha, delete, tempFile);
        }

        var zipSubDirectory = "test262-" + commitSha;

        if (extract)
        {
            Console.WriteLine("Extracting archive...");
            ZipFile.ExtractToDirectory(tempFile, tempPath);

            // zio wants /mnt/c format
            var sourcePath = Path.Combine(tempPath, zipSubDirectory)
                .Replace(@"C:\", "/mnt/c/")
                .Replace(@"D:\", "/mnt/d/")
                .Replace(@"E:\", "/mnt/e/");

            Console.WriteLine($"Building test262stream from path {sourcePath}");

            return Test262Stream.FromDirectory(sourcePath);
        }

        return Test262Stream.FromZipArchive(tempFile, zipSubDirectory, configure);
    }

    private static async Task Download(string commitSha, bool delete, string tempFile)
    {
        await _downloadLock.WaitAsync();

        try
        {
            if (delete && File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            var uri = $"https://github.com/tc39/test262/archive/{commitSha}.zip";

            Console.WriteLine($"Loading test262 repository archive from {uri}");

            var sw = Stopwatch.StartNew();

            using var client = new HttpClient();

            using var downloadStream = await client.GetStreamAsync(uri);
            using var writeStream = File.OpenWrite(tempFile);

            await downloadStream.CopyToAsync(writeStream);

            sw.Stop();

            Console.WriteLine($"File downloaded and saved to {tempFile} in {sw.Elapsed}");
        }
        finally
        {
            _downloadLock.Release();
        }
    }
}
