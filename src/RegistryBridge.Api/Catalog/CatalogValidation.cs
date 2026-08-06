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

            if (!PinnedImageReference().IsMatch(entry.SourceReference))
            {
                errors[$"entries[{index}].sourceReference"] =
                [
                    "An image mirror source must end with a pinned sha256 digest."
                ];
            }

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

            if (entry.Enabled && !activeTargetRepositories.Add(entry.TargetRepository))
            {
                errors[$"entries[{index}].targetRepository"] =
                [
                    "An active Target Repository can belong to only one Catalog Entry."
                ];
            }

            if (entry.CredentialHandle is not null)
            {
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
                else if (!string.Equals(
                             credentialHandle.Type,
                             "OciRegistry",
                             StringComparison.Ordinal))
                {
                    errors[$"entries[{index}].credentialHandle"] =
                    [
                        "An image mirror requires an OciRegistry Credential Handle."
                    ];
                }
            }
        }

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
            if (TryGetDigest(entry.SourceReference, out var digest))
            {
                targetTagDigests[GetTargetTagKey(entry.TargetRepository, entry.TargetTag)] = digest;
            }
        }

        for (var index = 0; index < request.Entries.Count; index++)
        {
            var entry = request.Entries[index];
            if (!TryGetDigest(entry.SourceReference, out var digest))
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
        "^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetRepository();

    [GeneratedRegex(
        "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TargetTag();
}
