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

    [Fact]
    public void AcceptsPinnedWrapperBuildAndHelmChartInputsWithCompatibleCredentialHandles()
    {
        var request = new CatalogSaveRequest(
            null,
            [
                new CatalogEntryInput(
                    Guid.NewGuid(),
                    "https://git.example.test/team/widget.git",
                    "builds/team/widget",
                    "release-2026",
                    "git-source",
                    true,
                    CatalogEntryKind.WrapperBuild,
                    "0123456789abcdef0123456789abcdef01234567"),
                new CatalogEntryInput(
                    Guid.NewGuid(),
                    "oci://charts.example.test/helm/widget",
                    "charts/widget",
                    "1.2.3",
                    "chart-source",
                    true,
                    CatalogEntryKind.HelmChart,
                    "1.2.3",
                    "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
            ]);

        var errors = CatalogValidation.Validate(
            request,
            [
                new DeclaredCredentialHandle { Name = "git-source", Type = "HttpsGit" },
                new DeclaredCredentialHandle { Name = "chart-source", Type = "OciRegistry" }
            ]);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsWrapperBuildsWithoutFullCommitIdsAndHelmChartsWithMismatchedTargetVersions()
    {
        var request = new CatalogSaveRequest(
            null,
            [
                new CatalogEntryInput(
                    Guid.NewGuid(),
                    "https://git.example.test/team/widget.git",
                    "builds/team/widget",
                    "release-2026",
                    null,
                    true,
                    CatalogEntryKind.WrapperBuild,
                    "deadbeef"),
                new CatalogEntryInput(
                    Guid.NewGuid(),
                    "https://charts.example.test",
                    "charts/widget",
                    "1.2.2",
                    null,
                    true,
                    CatalogEntryKind.HelmChart,
                    "1.2.3",
                    "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
            ]);

        var errors = CatalogValidation.Validate(request, []);

        Assert.Contains("entries[0].sourceVersion", errors.Keys);
        Assert.Contains("entries[1].targetTag", errors.Keys);
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
