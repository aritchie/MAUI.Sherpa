using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class BackupServiceTests
{
    private readonly Mock<IEncryptedSettingsService> _mockSettingsService;
    private readonly Mock<IAppleIdentityService> _mockAppleIdentityService;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _mockSettingsService = new Mock<IEncryptedSettingsService>();
        _mockAppleIdentityService = new Mock<IAppleIdentityService>();
        _mockAppleIdentityService
            .Setup(x => x.GetIdentitiesAsync())
            .ReturnsAsync(Array.Empty<AppleIdentity>());

        _service = new BackupService(_mockSettingsService.Object, _mockAppleIdentityService.Object);
    }

    [Fact]
    public async Task ExportSettingsAsync_CreatesEncryptedBackup()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("1", "Test", "KEY", "ISS", "content", DateTime.UtcNow)
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(settings);

        var result = await _service.ExportSettingsAsync("password123");

        result.Should().NotBeEmpty();
        result.Length.Should().BeGreaterThan(68); // Header + salt + nonce + tag + some content
    }

    [Fact]
    public async Task ExportSettingsAsync_StartsWithMagicHeader()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var result = await _service.ExportSettingsAsync("password");

        var header = System.Text.Encoding.UTF8.GetString(result, 0, 8);
        header.Should().Be("MSSBAK01");
    }

    [Fact]
    public async Task ExportSettingsAsync_ThrowsOnEmptyPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExportSettingsAsync(""));
    }

    [Fact]
    public async Task ExportSettingsAsync_ThrowsOnNullPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExportSettingsAsync(null!));
    }

    [Fact]
    public async Task ImportSettingsAsync_DecryptsCorrectly()
    {
        var original = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "My Identity", "KEY123", "ISSUER456", "p8content", DateTime.UtcNow)
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("mypassword");
        var imported = await _service.ImportSettingsAsync(encrypted, "mypassword");

        imported.AppleIdentities.Should().HaveCount(1);
        imported.AppleIdentities[0].Name.Should().Be("My Identity");
        imported.AppleIdentities[0].KeyId.Should().Be("KEY123");
    }

    [Fact]
    public async Task ExportSettingsAsync_UsesAppleIdentityServiceForP8Content()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Old Name", "OLDKEY", "OLDISSUER", "", DateTime.UtcNow.AddDays(-1))
            }
        };

        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(settings);
        _mockAppleIdentityService.Setup(x => x.GetIdentitiesAsync()).ReturnsAsync(new List<AppleIdentity>
        {
            new("id1", "Identity 1", "KEY1", "ISS1", null, "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----")
        });

        var encrypted = await _service.ExportSettingsAsync("password123");
        var imported = await _service.ImportSettingsAsync(encrypted, "password123");

        imported.AppleIdentities.Should().ContainSingle();
        imported.AppleIdentities[0].Name.Should().Be("Identity 1");
        imported.AppleIdentities[0].KeyId.Should().Be("KEY1");
        imported.AppleIdentities[0].P8Content.Should().Contain("BEGIN PRIVATE KEY");
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnWrongPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("correct");

        // AES-GCM throws AuthenticationTagMismatchException (a CryptographicException subclass)
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(
            () => _service.ImportSettingsAsync(encrypted, "wrong"));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnEmptyPassword()
    {
        var dummyData = new byte[100];

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ImportSettingsAsync(dummyData, ""));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnInvalidFormat()
    {
        var invalidData = System.Text.Encoding.UTF8.GetBytes("not a valid backup");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportSettingsAsync(invalidData, "password"));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnTruncatedData()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("password");
        
        // Truncate the data
        var truncated = new byte[50];
        Array.Copy(encrypted, truncated, 50);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportSettingsAsync(truncated, "password"));
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsTrueForValidBackup()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("password");

        var isValid = await _service.ValidateBackupAsync(encrypted);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForInvalidHeader()
    {
        var invalidData = System.Text.Encoding.UTF8.GetBytes("NOTVALID rest of data...");

        var isValid = await _service.ValidateBackupAsync(invalidData);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForEmptyData()
    {
        var isValid = await _service.ValidateBackupAsync(Array.Empty<byte>());

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForNull()
    {
        var isValid = await _service.ValidateBackupAsync(null!);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForTooShortData()
    {
        var isValid = await _service.ValidateBackupAsync(new byte[] { 1, 2, 3 });

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllSettings()
    {
        var original = new MauiSherpaSettings
        {
            Version = 1,
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Identity 1", "K1", "I1", "p8-1", DateTime.UtcNow),
                new("id2", "Identity 2", "K2", "I2", "p8-2", DateTime.UtcNow)
            },
            CloudProviders = new List<CloudProviderData>
            {
                new("cp1", "Azure KV", CloudSecretsProviderType.AzureKeyVault,
                    new Dictionary<string, string> { ["vaultUrl"] = "https://test.vault.azure.net" })
            },
            SecretsPublishers = new List<SecretsPublisherData>
            {
                new("sp1", "cp1", "GitHub Actions",
                    new Dictionary<string, string> { ["owner"] = "test", ["repo"] = "test-repo" })
            },
            ActiveCloudProviderId = "cp1",
            Preferences = new AppPreferences { Theme = "Dark", AndroidSdkPath = "/path/to/sdk" }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("complexP@ssw0rd!");
        var imported = await _service.ImportSettingsAsync(encrypted, "complexP@ssw0rd!");

        imported.AppleIdentities.Should().HaveCount(2);
        imported.CloudProviders.Should().HaveCount(1);
        imported.SecretsPublishers.Should().HaveCount(1);
        imported.ActiveCloudProviderId.Should().Be("cp1");
        imported.Preferences.Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task DifferentPasswordsProduceDifferentCiphertexts()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var encrypted1 = await _service.ExportSettingsAsync("password1");
        var encrypted2 = await _service.ExportSettingsAsync("password2");

        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public async Task SamePasswordProducesDifferentCiphertexts_DueToDifferentSalt()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var encrypted1 = await _service.ExportSettingsAsync("samepassword");
        var encrypted2 = await _service.ExportSettingsAsync("samepassword");

        // Due to random salt and nonce, ciphertexts should differ
        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public async Task SpecialCharactersInPassword_WorkCorrectly()
    {
        var original = new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Special" }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);
        var specialPassword = "P@$$w0rd!#$%^&*()日本語🔐";

        var encrypted = await _service.ExportSettingsAsync(specialPassword);
        var imported = await _service.ImportSettingsAsync(encrypted, specialPassword);

        imported.Preferences.Theme.Should().Be("Special");
    }

    [Fact]
    public async Task LargeSettings_RoundTripCorrectly()
    {
        var identities = Enumerable.Range(1, 50)
            .Select(i => new AppleIdentityData($"id{i}", $"Identity {i}", $"KEY{i}", $"ISS{i}", 
                new string('X', 1000), DateTime.UtcNow))
            .ToList();

        var original = new MauiSherpaSettings { AppleIdentities = identities };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("password");
        var imported = await _service.ImportSettingsAsync(encrypted, "password");

        imported.AppleIdentities.Should().HaveCount(50);
    }
}
