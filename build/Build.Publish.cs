using Fallout.Common;
using Fallout.Common.Git;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Components;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    string MyGetGetSource => "https://www.myget.org/F/test262harness/api/v2/package";
    [Parameter] [Secret] string MyGetApiKey;

    string ApiKeyToUse => IsTaggedBuild ? ((IPublish) this).NuGetApiKey : MyGetApiKey;
    string SourceToUse => IsTaggedBuild ? ((IPublish) this).NuGetSource : MyGetGetSource;

    public Target Publish => _ => _
        .OnlyWhenDynamic(() => !IsRunningOnWindows && (GitRepository.IsOnMainOrMasterBranch() || IsTaggedBuild))
        .DependsOn<IPack>()
        .Requires(() => ((IPublish) this).NuGetApiKey, () => MyGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                    .Apply(((IPublish) this).PushSettingsBase)
                    .Apply(((IPublish) this).PushSettings)
                    .CombineWith(((IPublish) this).PushPackageFiles, (_, v) => _
                        .SetTargetPath(v))
                    .Apply(((IPublish) this).PackagePushSettings),
                ((IPublish) this).PushDegreeOfParallelism,
                ((IPublish) this).PushCompleteOnFailure);
        });

    public Configure<DotNetNuGetPushSettings> PushSettings => _ => _
        .SetSource(SourceToUse)
        .SetApiKey(ApiKeyToUse)
        .EnableSkipDuplicate();
}
