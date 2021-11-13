using Nuke.Common.CI.GitHubActions;

[GitHubActions(
    "ci",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    //OnPushBranchesIgnore = new[] { MainBranch },
    //OnPullRequestBranches = new[] { MainBranch },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Test), nameof(Pack) },
    CacheKeyFiles = new[] { "global.json", "source/**/*.csproj" })]
partial class Build
{
}
