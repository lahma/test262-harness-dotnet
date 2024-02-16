using System.Collections.Generic;

using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Components;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    string NuGetSource => "https://api.nuget.org/v3/index.json";
    [Parameter] [Secret] string NuGetApiKey;

    string MyGetGetSource => "https://www.myget.org/F/test262harness/api/v2/package";
    [Parameter] [Secret] string MyGetApiKey;

    string ApiKeyToUse => IsTaggedBuild ? NuGetApiKey : MyGetApiKey;
    string SourceToUse => IsTaggedBuild ? NuGetSource : MyGetGetSource;

    Target Publish => _ => _
        .OnlyWhenDynamic(() => IsRunningOnWindows && (GitRepository.IsOnMainOrMasterBranch() || IsTaggedBuild))
        .DependsOn<IPack>()
        .Requires(() => NuGetApiKey, () => MyGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                    .Apply(PushSettingsBase)
                    .Apply(PushSettings)
                    .CombineWith(PushPackageFiles, (_, v) => _
                        .SetTargetPath(v))
                    .Apply(PackagePushSettings),
                PushDegreeOfParallelism,
                PushCompleteOnFailure);
        });

    Configure<DotNetNuGetPushSettings> PushSettingsBase => _ => _
        .SetSource(SourceToUse)
        .SetApiKey(ApiKeyToUse)
        .EnableSkipDuplicate();

    Configure<DotNetNuGetPushSettings> PushSettings => _ => _;
    Configure<DotNetNuGetPushSettings> PackagePushSettings => _ => _;

    IEnumerable<AbsolutePath> PushPackageFiles => ArtifactsDirectory.GlobFiles("*.nupkg");

    bool PushCompleteOnFailure => true;
    int PushDegreeOfParallelism => 2;
}
