using System.Net.Http.Json;
using Binexus.Platform.Branching.Activation;
using Binexus.Platform.Branching.Contracts;

namespace Binexus.Platform.Branching.Client;

public sealed class CloudActivationHttpClient(HttpClient httpClient) : ICloudActivationClient
{
    public const string HttpClientName = "BranchCloudActivation";

    public async Task<CreateBranchActivationChallengeResult> CreateChallengeAsync(
        Guid branchInstanceId,
        string publicKey,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/cloud/branch-activations/challenges",
            new { branchInstanceId, publicKey, installationTokenHash },
            cancellationToken);
        return await ReadRequiredAsync<CreateBranchActivationChallengeResult>(response, cancellationToken);
    }

    public async Task<ExchangeBranchActivationResult> ExchangeAsync(
        string activationCode,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/cloud/branch-activations/exchange",
            new
            {
                code = activationCode,
                branchInstanceId,
                publicKey,
                challengeId,
                signature,
                installationTokenHash,
            },
            cancellationToken);
        return await ReadRequiredAsync<ExchangeBranchActivationResult>(response, cancellationToken);
    }

    public async Task<ConfirmBranchActivationResult> ConfirmAsync(
        Guid activationId,
        string receipt,
        string installationToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/cloud/branch-activations/confirm")
        {
            Content = JsonContent.Create(new { activationId, receipt }),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Branch {installationToken}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredAsync<ConfirmBranchActivationResult>(response, cancellationToken);
    }

    public async Task<ResumeBranchActivationResult> ResumeAsync(
        Guid activationId,
        Guid branchInstanceId,
        string publicKey,
        Guid challengeId,
        string signature,
        string installationTokenHash,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"/cloud/branch-activations/{activationId:D}/resume",
            new { branchInstanceId, publicKey, challengeId, signature, installationTokenHash },
            cancellationToken);
        return await ReadRequiredAsync<ResumeBranchActivationResult>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ActivationProblem>(cancellationToken);
            throw new BranchActivationException(
                problem?.Code ?? BranchActivationErrorCodes.ActivationInvalid,
                problem?.Detail ?? "Cloud activation request failed.");
        }

        var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        return body ?? throw new BranchActivationException(
            BranchActivationErrorCodes.ActivationInvalid,
            "Cloud activation returned an empty body.");
    }

    private sealed record ActivationProblem(string? Code, string? Detail);
}
