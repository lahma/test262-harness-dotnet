using Fallout.Common.CI.GitHubActions;
using Fallout.Components;

[GitHubActions(
    "pr",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["master", "main"],
    OnPullRequestIncludePaths = ["**/*.*"],
    OnPullRequestExcludePaths = ["**/*.md"],
    PublishArtifacts = false,
    CacheKeyFiles = [],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack)],
    ConcurrencyCancelInProgress = true,
    PublishCondition = "runner.os == 'Windows'")
]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = ["master", "main"],
    OnPushIncludePaths = ["**/*.*"],
    OnPushExcludePaths = ["**/*.md"],
    PublishArtifacts = true,
    CacheKeyFiles = [],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["MYGET_API_KEY"],
    PublishCondition = "runner.os == 'Windows'")
]
// Releases live in their own workflow file because a nuget.org trusted publishing policy is scoped by
// workflow file name and offers no branch or tag filter — keeping this separate from 'build' is what
// stops every ordinary CI run from being able to mint a nuget.org API key. Ubuntu only, because
// Publish is gated to non-Windows.
[GitHubActions(
    "publish",
    GitHubActionsImage.UbuntuLatest,
    OnPushTags = ["v*.*.*"],
    PublishArtifacts = true,
    CacheKeyFiles = [],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack), nameof(Publish)],
    EnvironmentName = "nuget",
    ReadPermissions = [GitHubActionsPermissions.Contents],
    WritePermissions = [GitHubActionsPermissions.IdToken])
]
public partial class Build;
