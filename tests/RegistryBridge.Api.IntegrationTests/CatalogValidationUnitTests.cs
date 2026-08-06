using RegistryBridge.Api.Catalog;
using RegistryBridge.Api.Deployment;

namespace RegistryBridge.Api.IntegrationTests;

public sealed class CatalogValidationUnitTests
{
    [Fact]
    public void RejectsMalformedInputsDuplicateActiveRepositoriesAndInvalidCredentialHandles()
    {
        var request = new CatalogSaveRequest(
            null,
            [
                ImageMirror(
                    "registry.example.test/team/widget:latest",
                    "Mirrors/team/widget",
                    "release tag",
                    "unknown-handle"),
                ImageMirror(
                    "registry.example.test/team/second@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "Mirrors/team/widget",
                    "stable",
                    null)
            ]);

        var errors = CatalogValidation.Validate(
            request,
            [new DeclaredCredentialHandle { Name = "git-source", Type = "HttpsGit" }]);

        Assert.Contains("entries[0].sourceReference", errors.Keys);
        Assert.Contains("entries[0].targetRepository", errors.Keys);
        Assert.Contains("entries[0].targetTag", errors.Keys);
        Assert.Contains("entries[0].credentialHandle", errors.Keys);
        Assert.Contains("entries[1].targetRepository", errors.Keys);
    }

    [Fact]
    public void RejectsATargetTagMappedToADifferentDigestInAPriorRevision()
    {
        var priorDefinition = new CatalogDefinition(
            [
                new CatalogEntryDefinition(
                    Guid.NewGuid(),
                    "registry.example.test/team/widget@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "mirrors/team/widget",
                    "stable",
                    null,
                    true)
            ]);
        var request = new CatalogSaveRequest(
            null,
            [
                ImageMirror(
                    "registry.example.test/team/widget@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "mirrors/team/widget",
                    "stable",
                    null)
            ]);

        var errors = CatalogValidation.ValidateTargetTagImmutability(request, [priorDefinition]);

        Assert.Contains("entries[0].targetTag", errors.Keys);
    }

    private static CatalogEntryInput ImageMirror(
        string sourceReference,
        string targetRepository,
        string targetTag,
        string? credentialHandle) =>
        new(
            Guid.NewGuid(),
            sourceReference,
            targetRepository,
            targetTag,
            credentialHandle,
            true);
}
