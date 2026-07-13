using System.Collections.Generic;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;
using Nuke.Components;

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
    OnPushTags = ["v*.*.*"],
    OnPushIncludePaths = ["**/*.*"],
    OnPushExcludePaths = ["**/*.md"],
    PublishArtifacts = true,
    CacheKeyFiles = [],
    InvokedTargets = [nameof(ICompile.Compile), nameof(ITest.Test), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["NUGET_API_KEY", "MYGET_API_KEY"],
    PublishCondition = "runner.os == 'Windows'")
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

        // Nuke pins the checkout / upload-artifact action versions to its own release. Swap them for
        // version-pinned custom steps so the generated workflows track the latest action majors
        // independently of the Nuke version we happen to build with.
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

        // only need to list the ones that are missing from default image
        newSteps.Insert(0, new GitHubActionsSetupDotNetStep(["10.0"]));

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

class GitHubActionsSetupDotNetStep : GitHubActionsStep
{
    public GitHubActionsSetupDotNetStep(string[] versions)
    {
        Versions = versions;
    }

    string[] Versions { get; }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine("- uses: actions/setup-dotnet@v5");

        using (writer.Indent())
        {
            writer.WriteLine("with:");
            using (writer.Indent())
            {
                writer.WriteLine("dotnet-version: |");
                using (writer.Indent())
                {
                    foreach (var version in Versions)
                    {
                        writer.WriteLine(version);
                    }
                }
            }
        }
    }
}
