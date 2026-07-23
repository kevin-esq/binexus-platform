using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Binexus.Platform.Branching.DeviceAuth;

/// <summary>Public OpenAPI request contracts for Branch device authentication.</summary>
public sealed record CreateDeviceAuthChallengeRequest(
    [property: JsonPropertyName("deviceId")]
    [Required]
    Guid DeviceId);

public sealed record IssueDeviceAuthTokenRequest(
    [property: JsonPropertyName("challengeId")]
    [Required]
    Guid ChallengeId,
    [property: JsonPropertyName("deviceId")]
    [Required]
    Guid DeviceId,
    [property: JsonPropertyName("signature")]
    [Required]
    [MinLength(1)]
    string Signature,
    [property: JsonPropertyName("protocolVersion")]
    [Required]
    [MinLength(1)]
    string ProtocolVersion);
