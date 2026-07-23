using System.Collections.Generic;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.CI.GitHubActions.Configuration;
using Fallout.Common.Execution;
using Fallout.Common.Utilities;
using Fallout.Components;

[CustomGitHubActions(
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
[CustomGitHubActions(
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
[CustomGitHubActions(
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


class CustomGitHubActionsAttribute : GitHubActionsAttribute
{
    public CustomGitHubActionsAttribute(string name, GitHubActionsImage image, params GitHubActionsImage[] images) : base(name, image, images)
    {
    }

    protected override GitHubActionsJob GetJobs(GitHubActionsImage image, IReadOnlyCollection<ExecutableTarget> relevantTargets)
    {
        var job = base.GetJobs(image, relevantTargets);

        // Fallout pins the checkout / upload-artifact action versions to its own release
        // (checkout@v6, upload-artifact@v5). Swap them for version-pinned custom steps so the
        // generated workflows track the latest action majors independently of the Fallout version
        // we happen to build with. setup-dotnet needs no override: Fallout's run step emits it,
        // driven by global.json.
        var newSteps = new List<GitHubActionsStep>();
        foreach (var step in job.Steps)
        {
            newSteps.Add(step switch
            {
                GitHubActionsCheckoutStep => new PinnedUsesStep("actions/checkout@v7"),
                GitHubActionsArtifactStep artifact => new PinnedUploadArtifactStep(artifact.Name, artifact.Path, artifact.Condition),
                _ => step,
            });
        }

        job.Steps = newSteps.ToArray();
        return job;
    }
}

/// <summary>Emits a bare <c>- uses: &lt;action&gt;</c> step, letting us pin an action version Nuke would otherwise hard-code.</summary>
class PinnedUsesStep : GitHubActionsStep
{
    public PinnedUsesStep(string uses)
    {
        Uses = uses;
    }

    string Uses { get; }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine($"- uses: {Uses}");
    }
}

/// <summary>Reproduces Nuke's upload-artifact step (name / condition / path) but on a pinned action version.</summary>
class PinnedUploadArtifactStep : GitHubActionsStep
{
    public PinnedUploadArtifactStep(string name, string path, string? condition)
    {
        Name = name;
        Path = path;
        Condition = condition;
    }

    string Name { get; }
    string Path { get; }
    string? Condition { get; }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine($"- name: 'Publish: {Name}'");

        using (writer.Indent())
        {
            writer.WriteLine("uses: actions/upload-artifact@v7");

            if (!string.IsNullOrWhiteSpace(Condition))
            {
                writer.WriteLine($"if: {Condition}");
            }

            writer.WriteLine("with:");
            using (writer.Indent())
            {
                writer.WriteLine($"name: {Name}");
                writer.WriteLine($"path: {Path}");
            }
        }
    }
}
