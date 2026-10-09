using System.Text.RegularExpressions;
using BdoClient;
using BdoClient.Logging;

namespace BdoClient.Update;

public sealed class UpdateManifestValidator
{
    private static readonly Regex Sha256HexRegex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly Regex CommitShaRegex = new("^[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private static readonly Regex WorkflowRunIdRegex = new("^[1-9][0-9]*$", RegexOptions.Compiled);

    private readonly ILogger _logger;

    public UpdateManifestValidator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public UpdateManifestValidationResult Validate(UpdateManifest manifest, UpdateCandidate candidate)
        => ValidateInternal(manifest, candidate, expectedSchemaVersion: 1, rejectPackageFields: false);

    public UpdateManifestValidationResult ValidateBundle(
        UpdateManifest manifest,
        UpdateCandidate candidate,
        string expectedAssetName)
        => ValidateInternal(manifest, candidate, expectedSchemaVersion: 2, rejectPackageFields: true, expectedAssetName);

    public UpdateManifestValidationResult ValidateBundle(UpdateManifest manifest, UpdateCandidate candidate)
        => ValidateBundle(manifest, candidate, ApplicationTechnicalIdentity.ExecutableFileName);

    private UpdateManifestValidationResult ValidateInternal(
        UpdateManifest manifest,
        UpdateCandidate candidate,
        int expectedSchemaVersion,
        bool rejectPackageFields,
        string? expectedAssetName = null)
    {
        if (manifest.SchemaVersion != expectedSchemaVersion)
        {
            _logger.Warning($"Manifest: invalid schema_version={manifest.SchemaVersion}");
            return UpdateManifestValidationResult.Failure($"Invalid schema_version: {manifest.SchemaVersion}");
        }

        var coreVersion = AppVersion.TryParseCoreVersion(manifest.Version);
        if (!coreVersion.HasValue)
        {
            _logger.Warning($"Manifest: invalid version '{manifest.Version}'");
            return UpdateManifestValidationResult.Failure($"Invalid version: {manifest.Version}");
        }
        if (coreVersion.Value != candidate.Version)
        {
            _logger.Warning($"Manifest: version {coreVersion.Value} != candidate {candidate.Version}");
            return UpdateManifestValidationResult.Failure("Version mismatch");
        }

        if (!string.Equals(manifest.Tag, candidate.TagName, StringComparison.Ordinal))
        {
            _logger.Warning($"Manifest: tag '{manifest.Tag}' != candidate '{candidate.TagName}'");
            return UpdateManifestValidationResult.Failure("Tag mismatch");
        }

        if (!string.Equals(manifest.Platform, "win-x64", StringComparison.Ordinal))
        {
            _logger.Warning($"Manifest: invalid platform '{manifest.Platform}'");
            return UpdateManifestValidationResult.Failure($"Invalid platform: {manifest.Platform}");
        }

        if (string.IsNullOrWhiteSpace(manifest.AssetName))
        {
            _logger.Warning("Manifest: missing asset_name");
            return UpdateManifestValidationResult.Failure("Missing asset_name");
        }

        if (!ApplicationTechnicalIdentity.IsSupportedExecutableFileName(manifest.AssetName) ||
            expectedAssetName != null && !string.Equals(manifest.AssetName, expectedAssetName, StringComparison.Ordinal))
        {
            _logger.Warning($"Manifest: unsupported or unexpected asset_name '{manifest.AssetName}'");
            return UpdateManifestValidationResult.Failure("Asset name mismatch");
        }

        if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Sha256HexRegex.IsMatch(manifest.Sha256))
        {
            _logger.Warning($"Manifest: invalid sha256 '{manifest.Sha256}'");
            return UpdateManifestValidationResult.Failure("Invalid SHA-256");
        }

        var hasPackageName = !string.IsNullOrWhiteSpace(manifest.PackageName);
        var hasPackageSha = !string.IsNullOrWhiteSpace(manifest.PackageSha256);
        if (hasPackageName != hasPackageSha)
            return UpdateManifestValidationResult.Failure("package_name and package_sha256 must be provided together");

        if (rejectPackageFields && (hasPackageName || hasPackageSha))
            return UpdateManifestValidationResult.Failure("schema 2 bundle manifest must not contain package fields");

        if (hasPackageName && !rejectPackageFields)
        {
            var expectedPackageName = ApplicationTechnicalIdentity.BuildPackageFileName(manifest.AssetName!, candidate.Version.ToString());
            if (!string.Equals(manifest.PackageName, expectedPackageName, StringComparison.Ordinal))
                return UpdateManifestValidationResult.Failure("Package name mismatch");
            if (!Sha256HexRegex.IsMatch(manifest.PackageSha256!))
                return UpdateManifestValidationResult.Failure("Invalid package SHA-256");
        }

        if (string.IsNullOrWhiteSpace(manifest.CommitSha) || !CommitShaRegex.IsMatch(manifest.CommitSha))
        {
            _logger.Warning($"Manifest: invalid commit_sha '{manifest.CommitSha}'");
            return UpdateManifestValidationResult.Failure("Invalid commit_sha");
        }

        if (string.IsNullOrWhiteSpace(manifest.WorkflowRunId) || !WorkflowRunIdRegex.IsMatch(manifest.WorkflowRunId))
        {
            _logger.Warning($"Manifest: invalid workflow_run_id '{manifest.WorkflowRunId}'");
            return UpdateManifestValidationResult.Failure("Invalid workflow_run_id");
        }

        _logger.Debug($"Manifest: validated successfully (version={coreVersion.Value}, tag={manifest.Tag})");
        return UpdateManifestValidationResult.Success(manifest.Sha256.ToLowerInvariant());
    }
}

public sealed class UpdateManifestValidationResult
{
    public bool IsValid { get; }
    public string? NormalizedSha256 { get; }
    public string? ErrorMessage { get; }

    private UpdateManifestValidationResult(bool isValid, string? normalizedSha256, string? errorMessage)
    {
        IsValid = isValid;
        NormalizedSha256 = normalizedSha256;
        ErrorMessage = errorMessage;
    }

    public static UpdateManifestValidationResult Success(string normalizedSha256) => new(true, normalizedSha256, null);
    public static UpdateManifestValidationResult Failure(string errorMessage) => new(false, null, errorMessage);
}
