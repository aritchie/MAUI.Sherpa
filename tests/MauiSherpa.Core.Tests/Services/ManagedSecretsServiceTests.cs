using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class ManagedSecretsServiceTests
{
    readonly Mock<ICloudSecretsService> _cloudService = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly ManagedSecretsService _sut;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ManagedSecretsServiceTests()
    {
        _sut = new ManagedSecretsService(_cloudService.Object, _logger.Object);
    }

    [Fact]
    public async Task ListAsync_NoActiveProvider_ReturnsEmpty()
    {
        _cloudService.Setup(x => x.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var result = await _sut.ListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithSecrets_ReturnsManagedSecrets()
    {
        SetupActiveProvider();
        var metaKeys = new List<string> { "sherpa-secrets-meta/api-key", "sherpa-secrets-meta/db-password" };
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secrets-meta/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metaKeys);

        SetupMetadataByFullKey("sherpa-secrets-meta/api-key", new ManagedSecret("api-key", ManagedSecretType.String, "API Key", null, DateTime.UtcNow, DateTime.UtcNow));
        SetupMetadataByFullKey("sherpa-secrets-meta/db-password", new ManagedSecret("db-password", ManagedSecretType.String, "DB Password", null, DateTime.UtcNow, DateTime.UtcNow));

        var result = await _sut.ListAsync();

        result.Should().HaveCount(2);
        result[0].Key.Should().Be("api-key");
        result[1].Key.Should().Be("db-password");
    }

    [Fact]
    public async Task ListAsync_WithMissingMetadata_SkipsSecret()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.ListSecretsAsync("sherpa-secrets-meta/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "sherpa-secrets-meta/orphan" });
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets-meta/orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _sut.ListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsMetadata()
    {
        SetupActiveProvider();
        var expected = new ManagedSecret("my-key", ManagedSecretType.File, "A file", "data.bin", DateTime.UtcNow, DateTime.UtcNow);
        SetupMetadata("my-key", expected);

        var result = await _sut.GetAsync("my-key");

        result.Should().NotBeNull();
        result!.Key.Should().Be("my-key");
        result.Type.Should().Be(ManagedSecretType.File);
        result.OriginalFileName.Should().Be("data.bin");
    }

    [Fact]
    public async Task GetValueAsync_ReturnsBytes()
    {
        SetupActiveProvider();
        var value = Encoding.UTF8.GetBytes("secret-value");
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(value);

        var result = await _sut.GetValueAsync("my-key");

        result.Should().BeEquivalentTo(value);
    }

    [Fact]
    public async Task CreateAsync_StoresValueAndMetadata()
    {
        SetupActiveProvider();
        var value = Encoding.UTF8.GetBytes("test-value");

        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/new-key", value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync(
                It.Is<string>(k => k.StartsWith("sherpa-secrets-meta/")),
                It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateAsync("new-key", value, ManagedSecretType.String, "A test secret");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.StoreSecretAsync("sherpa-secrets/new-key", value, null, It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets-meta/new-key",
            It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NoProvider_Throws()
    {
        _cloudService.Setup(x => x.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var act = () => _sut.CreateAsync("key", new byte[] { 1 }, ManagedSecretType.String);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateAsync_EmptyKey_Throws()
    {
        SetupActiveProvider();

        var act = () => _sut.CreateAsync("", new byte[] { 1 }, ManagedSecretType.String);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesValueAndMetadata()
    {
        SetupActiveProvider();
        var existing = new ManagedSecret("my-key", ManagedSecretType.String, "Old desc", null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1));
        SetupMetadata("my-key", existing);

        var newValue = Encoding.UTF8.GetBytes("new-value");
        _cloudService.Setup(x => x.StoreSecretAsync("sherpa-secrets/my-key", newValue, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.StoreSecretAsync(
                It.Is<string>(k => k.StartsWith("sherpa-secrets-meta/")),
                It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.UpdateAsync("my-key", newValue, "New desc");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsFalse()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.GetSecretAsync("sherpa-secrets-meta/missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _sut.UpdateAsync("missing", description: "test");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_DeletesValueAndMetadata()
    {
        SetupActiveProvider();
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudService.Setup(x => x.DeleteSecretAsync("sherpa-secrets-meta/my-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DeleteAsync("my-key");

        result.Should().BeTrue();
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets/my-key", It.IsAny<CancellationToken>()), Times.Once);
        _cloudService.Verify(x => x.DeleteSecretAsync("sherpa-secrets-meta/my-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_FileType_StoresOriginalFileName()
    {
        SetupActiveProvider();
        var value = new byte[] { 1, 2, 3 };

        _cloudService.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.CreateAsync("cert-file", value, ManagedSecretType.File, "My cert", "certificate.p12");

        result.Should().BeTrue();

        // Verify metadata contains original filename
        _cloudService.Verify(x => x.StoreSecretAsync(
            "sherpa-secrets-meta/cert-file",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("certificate.p12")),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    void SetupActiveProvider()
    {
        var provider = new CloudSecretsProviderConfig("test-id", "Test Provider", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudService.Setup(x => x.ActiveProvider).Returns(provider);
    }

    void SetupMetadata(string key, ManagedSecret meta)
    {
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cloudService.Setup(x => x.GetSecretAsync($"sherpa-secrets-meta/{key}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }

    void SetupMetadataByFullKey(string fullKey, ManagedSecret meta)
    {
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cloudService.Setup(x => x.GetSecretAsync(fullKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }
}
