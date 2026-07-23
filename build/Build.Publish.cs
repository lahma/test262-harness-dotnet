using System;
using System.Net.Http;
using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Net;
using Fallout.Components;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    string MyGetSource => "https://www.myget.org/F/test262harness/api/v2/package";
    [Parameter] [Secret] string MyGetApiKey;

    // Trusted publishing (https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): the
    // audience and token exchange endpoint nuget.org expects, and the nuget.org profile name of the
    // account that created the trusted publishing policy.
    const string NuGetAudience = "https://www.nuget.org";
    const string NuGetTokenServiceUrl = "https://www.nuget.org/api/v2/token";
    const string NuGetUser = "lahma";

    public Target Publish => _ => _
        .OnlyWhenDynamic(() => !IsRunningOnWindows && (GitRepository.IsOnMainOrMasterBranch() || IsTaggedBuild))
        .DependsOn<IPack>()
        .Executes(() =>
        {
            var (source, apiKey) = IsTaggedBuild
                ? (((IPublish) this).NuGetSource, GetTrustedPublishingApiKey())
                : (MyGetSource, Assert.NotNullOrWhiteSpace(MyGetApiKey, "MyGet API key is required for preview pushes"));

            DotNetNuGetPush(_ => _
                    .SetSource(source)
                    .SetApiKey(apiKey)
                    .EnableSkipDuplicate()
                    .CombineWith(((IPublish) this).PushPackageFiles, (_, v) => _
                        .SetTargetPath(v)),
                ((IPublish) this).PushDegreeOfParallelism,
                ((IPublish) this).PushCompleteOnFailure);
        });

    /// <summary>
    /// Exchanges the job's GitHub OIDC token for a short-lived nuget.org API key, so no long-lived
    /// key has to be stored anywhere. Does what <c>NuGet/login@v1</c> does, without taking a
    /// dependency on the marketplace action.
    /// </summary>
    static string GetTrustedPublishingApiKey()
    {
        const string missingOidc = "GitHub OIDC is unavailable — the job needs 'permissions: id-token: write'";
        var requestUrl = Assert.NotNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_URL"), missingOidc);
        var requestToken = Assert.NotNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_TOKEN"), missingOidc);

        using var client = new HttpClient();

        var idToken = client
            .CreateRequest(HttpMethod.Get, $"{requestUrl}&audience={Uri.EscapeDataString(NuGetAudience)}")
            .WithBearerAuthentication(requestToken)
            .GetResponse()
            .AssertSuccessfulStatusCode()
            .GetBodyAsJsonObject().GetAwaiter().GetResult()["value"].GetValue<string>();

        var body = client
            .CreateRequest(HttpMethod.Post, NuGetTokenServiceUrl)
            .WithBearerAuthentication(idToken)
            .WithJsonContent(new { username = NuGetUser, tokenType = "ApiKey" })
            .GetResponse()
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"nuget.org token exchange failed ({(int) x.StatusCode}). Check that a trusted publishing policy "
                + $"exists for this repository and workflow file, and that '{NuGetUser}' created it.")
            .GetBodyAsJsonObject().GetAwaiter().GetResult();

        // The action reads 'apiKey', the original design document says 'api_key' — accept either.
        var apiKey = (body["apiKey"] ?? body["api_key"]).GetValue<string>();
        GitHubActions.Instance?.WriteCommand("add-mask", apiKey);
        Serilog.Log.Information("Obtained short-lived nuget.org API key, expires {Expires}", body["expires"]?.GetValue<string>());
        return apiKey;
    }
}
