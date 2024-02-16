using Nuke.Common.CI.GitHubActions;
using Nuke.Components;

[GitHubActions(
    "pr",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["master", "main"],
    OnPullRequestIncludePaths = ["**/*.*"],
    OnPullRequestExcludePaths = ["**/*.md"],
    PublishArtifacts = false,
    CacheKeyFiles = new string[0],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack)])
]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = ["master", "main"],
    OnPushTags = ["v*.*.*"],
    OnPushIncludePaths = ["**/*.*"],
    OnPushExcludePaths = ["**/*.md"],
    PublishArtifacts = true,
    CacheKeyFiles = new string[0],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["NUGET_API_KEY", "MYGET_API_KEY"])
]
public partial class Build
{
}
