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

    string DetermineVersionPrefix()
    {
        var versionPrefix = GitRepository.Tags.SingleOrDefault(x => x.StartsWith('v'))?[1..];
        if (!string.IsNullOrWhiteSpace(versionPrefix))
        {
            IsTaggedBuild = true;
            Serilog.Log.Information($"Tag version {VersionPrefix} from Git found, using it as version prefix", versionPrefix);
        }
        else
        {
            var propsDocument = XDocument.Parse((RootDirectory / "Directory.Build.props").ReadAllText());
            versionPrefix = propsDocument.Element("Project").Element("PropertyGroup").Element("VersionPrefix").Value;
            Serilog.Log.Information("Version prefix {VersionPrefix} read from Directory.Build.props", versionPrefix);
        }

        return versionPrefix;
    }

    protected override void OnBuildInitialized()
    {
        VersionPrefix = DetermineVersionPrefix();

        VersionSuffix = !IsTaggedBuild
            ? $"preview-{DateTime.UtcNow:yyyyMMdd-HHmm}"
            : "";

        if (IsLocalBuild)
        {
            VersionSuffix = $"dev-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        }

        Serilog.Log.Information("BUILD SETUP");
        Serilog.Log.Information("Configuration:\t{Configuration}", ((ICompile) this).Configuration);
        Serilog.Log.Information("Version prefix:\t{VersionPrefix}", VersionPrefix);
        Serilog.Log.Information("Version suffix:\t{VersionSuffix}", VersionSuffix);
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
        .SetInformationalVersion(VersionPrefix);

    public IEnumerable<Project> TestProjects => ((ICompile) this).Solution.AllProjects.Where(x => x.Name.EndsWith("Tests"));

    public Configure<DotNetPackSettings> PackSettings => _ => _
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix);
}
