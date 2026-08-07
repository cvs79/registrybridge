using System.Text.RegularExpressions;
using RegistryBridge.Api.Deployment;

namespace RegistryBridge.Api.Catalog;

public static partial class CatalogValidation
{
    public static Dictionary<string, string[]> Validate(
        CatalogSaveRequest request,
        IReadOnlyList<DeclaredCredentialHandle> declaredCredentialHandles)
    {
        var errors = new Dictionary<string, string[]>();
        var activeTargetRepositories = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < request.Entries.Count; index++)
        {
            var entry = request.Entries[index];

            if (!TargetRepository().IsMatch(entry.TargetRepository))
            {
                errors[$"entries[{index}].targetRepository"] =
                [
                    "The Target Repository must be a lowercase repository path."
                ];
            }

            if (!TargetTag().IsMatch(entry.TargetTag))
            {
                errors[$"entries[{index}].targetTag"] =
                [
                    "The Target Tag must be a valid OCI tag."
                ];
            }

            ValidateEntryKind(entry, index, declaredCredentialHandles, errors);

            if (entry.Enabled && !activeTargetRepositories.Add(entry.TargetRepository))
            {
                errors[$"entries[{index}].targetRepository"] =
                [
                    "An active Target Repository can belong to only one Catalog Entry."
                ];
            }
        }

        ValidateVulnerabilityExceptions(request, errors);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateTargetTagImmutability(
        CatalogSaveRequest request,
        IEnumerable<CatalogDefinition> priorDefinitions)
    {
        var errors = new Dictionary<string, string[]>();
        var targetTagDigests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in priorDefinitions.SelectMany(definition => definition.Entries))
        {
            if (entry.Kind == CatalogEntryKind.ImageMirror
                && TryGetDigest(entry.SourceReference, out var digest))
            {
                targetTagDigests[GetTargetTagKey(entry.TargetRepository, entry.TargetTag)] = digest;
            }
        }

        for (var index = 0; index < request.Entries.Count; index++)
        {
            var entry = request.Entries[index];
            if (entry.Kind != CatalogEntryKind.ImageMirror
                || !TryGetDigest(entry.SourceReference, out var digest))
            {
                continue;
            }

            var targetTagKey = GetTargetTagKey(entry.TargetRepository, entry.TargetTag);
            if (targetTagDigests.TryGetValue(targetTagKey, out var pinnedDigest)
                && !string.Equals(pinnedDigest, digest, StringComparison.Ordinal))
            {
                errors[$"entries[{index}].targetTag"] =
                [
                    "The Target Tag is already pinned to a different digest."
                ];
            }
            else
            {
                targetTagDigests[targetTagKey] = digest;
            }
        }

        return errors;
    }

    private static string GetTargetTagKey(string targetRepository, string targetTag) =>
        $"{targetRepository}\0{targetTag}";

    private static void ValidateEntryKind(
        CatalogEntryInput entry,
        int index,
        IReadOnlyList<DeclaredCredentialHandle> declaredCredentialHandles,
        Dictionary<string, string[]> errors)
    {
        var expectedCredentialType = entry.Kind switch
        {
            CatalogEntryKind.ImageMirror => "OciRegistry",
            CatalogEntryKind.WrapperBuild => "HttpsGit",
            CatalogEntryKind.HelmChart when entry.SourceReference.StartsWith(
                "oci://",
                StringComparison.OrdinalIgnoreCase) => "OciRegistry",
            CatalogEntryKind.HelmChart => "HttpsHelm",
            _ => null
        };

        if (entry.Kind == CatalogEntryKind.ImageMirror
            && !PinnedImageReference().IsMatch(entry.SourceReference))
        {
            errors[$"entries[{index}].sourceReference"] =
            [
                "An image mirror source must end with a pinned sha256 digest."
            ];
        }

        if (entry.Kind == CatalogEntryKind.WrapperBuild)
        {
            if (!HttpsGitRepository().IsMatch(entry.SourceReference))
            {
                errors[$"entries[{index}].sourceReference"] =
                [
                    "A Wrapper Build source must be an HTTPS Git repository."
                ];
            }

            if (entry.SourceVersion is null || !FullGitCommit().IsMatch(entry.SourceVersion))
            {
                errors[$"entries[{index}].sourceVersion"] =
                [
                    "A Wrapper Build source must use a full Git commit ID."
                ];
            }
        }

        if (entry.Kind == CatalogEntryKind.HelmChart)
        {
            if (!HttpsOrOciHelmSource().IsMatch(entry.SourceReference))
            {
                errors[$"entries[{index}].sourceReference"] =
                [
                    "A Helm Chart source must be an HTTPS repository or OCI registry."
                ];
            }

            if (entry.SourceVersion is null || !ExactVersion().IsMatch(entry.SourceVersion))
            {
                errors[$"entries[{index}].sourceVersion"] =
                [
                    "A Helm Chart requires an exact version."
                ];
            }
            else if (!string.Equals(entry.TargetTag, entry.SourceVersion, StringComparison.Ordinal))
            {
                errors[$"entries[{index}].targetTag"] =
                [
                    "A Helm Chart Target Tag must match its configured chart version."
                ];
            }

            if (entry.ExpectedDigest is null || !Sha256Digest().IsMatch(entry.ExpectedDigest))
            {
                errors[$"entries[{index}].expectedDigest"] =
                [
                    "A Helm Chart requires an expected sha256 source digest."
                ];
            }
        }

        if (entry.CredentialHandle is null)
        {
            return;
        }

        var credentialHandle = declaredCredentialHandles.FirstOrDefault(
            handle => string.Equals(
                handle.Name,
                entry.CredentialHandle,
                StringComparison.Ordinal));
        if (credentialHandle is null)
        {
            errors[$"entries[{index}].credentialHandle"] =
            [
                "The Credential Handle is not declared for this deployment."
            ];
        }
        else if (expectedCredentialType is not null
                 && !string.Equals(credentialHandle.Type, expectedCredentialType, StringComparison.Ordinal))
        {
            errors[$"entries[{index}].credentialHandle"] =
            [
                $"A {entry.Kind} requires a {expectedCredentialType} Credential Handle."
            ];
        }
    }

    private static void ValidateVulnerabilityExceptions(
        CatalogSaveRequest request,
        Dictionary<string, string[]> errors)
    {
        foreach (var (exception, index) in (request.VulnerabilityExceptions ?? [])
                     .Select((exception, index) => (exception, index)))
        {
            if (!Sha256Digest().IsMatch(exception.ImageDigest))
            {
                errors[$"vulnerabilityExceptions[{index}].imageDigest"] =
                [
                    "A Vulnerability Exception must name one sha256 image digest."
                ];
            }

            if (exception.VulnerabilityIds.Count == 0
                || exception.VulnerabilityIds.Any(string.IsNullOrWhiteSpace))
            {
                errors[$"vulnerabilityExceptions[{index}].vulnerabilityIds"] =
                [
                    "A Vulnerability Exception requires specific vulnerability IDs."
                ];
            }

            if (string.IsNullOrWhiteSpace(exception.Reason))
            {
                errors[$"vulnerabilityExceptions[{index}].reason"] =
                [
                    "A Vulnerability Exception requires a written reason."
                ];
            }
        }
    }

    private static bool TryGetDigest(string sourceReference, out string digest)
    {
        var separatorIndex = sourceReference.LastIndexOf('@');
        if (separatorIndex < 0 || separatorIndex == sourceReference.Length - 1)
        {
            digest = string.Empty;
            return false;
        }

        digest = sourceReference[(separatorIndex + 1)..];
        return true;
    }

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9._/-]*@sha256:[a-f0-9]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PinnedImageReference();

    [GeneratedRegex(
        "^https://[^\\s]+(?:\\.git)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HttpsGitRepository();

    [GeneratedRegex(
        "^[a-f0-9]{40,64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FullGitCommit();

    [GeneratedRegex(
        "^(?:https://[^\\s]+|oci://[a-z0-9][a-z0-9._/-]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HttpsOrOciHelmSource();

    [GeneratedRegex(
        "^v?\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExactVersion();

    [GeneratedRegex(
        "^sha256:[a-f0-9]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Digest();

    [GeneratedRegex(
        "^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetRepository();

    [GeneratedRegex(
        "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetTag();
}
