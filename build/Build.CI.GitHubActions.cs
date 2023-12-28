using Nuke.Common.CI.GitHubActions;

[GitHubActions(
    "pr",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["master", "main"],
    OnPullRequestIncludePaths = ["**/*.*"],
    OnPullRequestExcludePaths = ["**/*.md"],
    PublishArtifacts = false,
    CacheKeyFiles = new string[0],
    InvokedTargets = [nameof(Compile), nameof(Test), nameof(Pack)])
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
    InvokedTargets = [nameof(Compile), nameof(Test), nameof(Pack), nameof(Publish)],
    ImportSecrets = ["NUGET_API_KEY", "MYGET_API_KEY"])
]
public partial class Build
{
}
