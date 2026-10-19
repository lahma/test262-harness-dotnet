using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Fallout.Common;
using Fallout.Common.CI;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Collections;
using Fallout.Components;
using Fallout.Solutions;

[ShutdownDotNetAfterServerBuild]
partial class Build : FalloutBuild, ITest, IPack, IPublish
{
    public static int Main() => Execute<Build>(x => ((ICompile) x).Compile);

    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    bool IsTaggedBuild;
    string VersionPrefix;
    string VersionSuffix;

    string FullVersion => string.IsNullOrWhiteSpace(VersionSuffix) ? VersionPrefix : $"{VersionPrefix}-{VersionSuffix}";

    void DetermineVersion()
    {
        var tagVersion = GitRepository.Tags.SingleOrDefault(x => x.StartsWith('v'))?[1..];
        if (!string.IsNullOrWhiteSpace(tagVersion))
        {
            IsTaggedBuild = true;

            // A prerelease tag (v1.2.0-rc.1) has to be split — AssemblyVersion only accepts the numeric part.
            var separator = tagVersion.IndexOf('-');
            VersionPrefix = separator < 0 ? tagVersion : tagVersion[..separator];
            VersionSuffix = separator < 0 ? null : tagVersion[(separator + 1)..];

            Serilog.Log.Information("Version {FullVersion} taken from Git tag v{TagVersion}", FullVersion, tagVersion);
            return;
        }

        // Untagged: <VersionPrefix> in Directory.Build.props is the placeholder preview builds carry.
        var propsDocument = XDocument.Parse((RootDirectory / "Directory.Build.props").ReadAllText());
        VersionPrefix = propsDocument.Element("Project").Element("PropertyGroup").Element("VersionPrefix").Value;
        VersionSuffix = $"preview-{DateTime.UtcNow:yyyyMMdd-HHmm}";

        Serilog.Log.Information("Version prefix {VersionPrefix} read from Directory.Build.props", VersionPrefix);
    }

    protected override void OnBuildInitialized()
    {
        DetermineVersion();

        if (IsLocalBuild)
        {
            VersionSuffix = $"dev-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        }

        Serilog.Log.Information("BUILD SETUP");
        Serilog.Log.Information("Configuration:\t{Configuration}", ((ICompile) this).Configuration);
        Serilog.Log.Information("Version:\t{FullVersion}", FullVersion);
        Serilog.Log.Information("Tagged build:\t{IsTaggedBuild}", IsTaggedBuild);
    }

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    public Configure<DotNetBuildSettings> CompileSettings => _ => _
        .SetAssemblyVersion(VersionPrefix)
        .SetFileVersion(VersionPrefix)
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix);

    public IEnumerable<Project> TestProjects => ((ICompile) this).Solution.AllProjects.Where(x => x.Name.EndsWith("Tests"));

    public Configure<DotNetPackSettings> PackSettings => _ => _
        .SetAssemblyVersion(VersionPrefix)
        .SetFileVersion(VersionPrefix)
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix);
}
