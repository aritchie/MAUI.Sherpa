using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Backup service for password-protected export/import of settings
/// Uses PBKDF2 for key derivation and AES-256-GCM for encryption
/// </summary>
public class BackupService : IBackupService
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly byte[] MagicHeader = "MSSBAK01"u8.ToArray();
    
    private readonly IEncryptedSettingsService _settingsService;
    private readonly IAppleIdentityService? _appleIdentityService;

    public BackupService(
        IEncryptedSettingsService settingsService,
        IAppleIdentityService? appleIdentityService = null)
    {
        _settingsService = settingsService;
        _appleIdentityService = appleIdentityService;
    }

    public async Task<byte[]> ExportSettingsAsync(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required", nameof(password));

        var settings = await _settingsService.GetSettingsAsync();
        if (_appleIdentityService is not null)
        {
            var identities = await _appleIdentityService.GetIdentitiesAsync();
            if (identities.Count > 0)
            {
                var createdAtById = new Dictionary<string, DateTime>();
                foreach (var identity in settings.AppleIdentities)
                {
                    createdAtById[identity.Id] = identity.CreatedAt;
                }

                settings = settings with
                {
                    AppleIdentities = identities
                        .Select(identity => new AppleIdentityData(
                            Id: identity.Id,
                            Name: identity.Name,
                            KeyId: identity.KeyId,
                            IssuerId: identity.IssuerId,
                            P8Content: identity.P8KeyContent ?? string.Empty,
                            CreatedAt: createdAtById.TryGetValue(identity.Id, out var createdAt)
                                ? createdAt
                                : DateTime.UtcNow))
                        .ToList()
                };
            }
        }

        var json = JsonSerializer.Serialize(settings);
        var plaintext = Encoding.UTF8.GetBytes(json);

        // Generate salt and derive key
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        
        // Encrypt with AES-GCM
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [magic 8][salt 32][nonce 12][tag 16][ciphertext]
        var result = new byte[MagicHeader.Length + SaltSize + NonceSize + TagSize + ciphertext.Length];
        var offset = 0;
        
        Buffer.BlockCopy(MagicHeader, 0, result, offset, MagicHeader.Length);
        offset += MagicHeader.Length;
        
        Buffer.BlockCopy(salt, 0, result, offset, SaltSize);
        offset += SaltSize;
        
        Buffer.BlockCopy(nonce, 0, result, offset, NonceSize);
        offset += NonceSize;
        
        Buffer.BlockCopy(tag, 0, result, offset, TagSize);
        offset += TagSize;
        
        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    public async Task<MauiSherpaSettings> ImportSettingsAsync(byte[] encryptedData, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required", nameof(password));

        if (!await ValidateBackupAsync(encryptedData))
            throw new InvalidOperationException("Invalid backup file format");

        var minLength = MagicHeader.Length + SaltSize + NonceSize + TagSize;
        if (encryptedData.Length < minLength)
            throw new InvalidOperationException("Backup file is too small");

        var offset = MagicHeader.Length;
        
        var salt = new byte[SaltSize];
        Buffer.BlockCopy(encryptedData, offset, salt, 0, SaltSize);
        offset += SaltSize;
        
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(encryptedData, offset, nonce, 0, NonceSize);
        offset += NonceSize;
        
        var tag = new byte[TagSize];
        Buffer.BlockCopy(encryptedData, offset, tag, 0, TagSize);
        offset += TagSize;
        
        var ciphertext = new byte[encryptedData.Length - offset];
        Buffer.BlockCopy(encryptedData, offset, ciphertext, 0, ciphertext.Length);

        // Derive key and decrypt
        var key = DeriveKey(password, salt);
        var plaintext = new byte[ciphertext.Length];
        
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var json = Encoding.UTF8.GetString(plaintext);
        var settings = JsonSerializer.Deserialize<MauiSherpaSettings>(json) 
            ?? throw new InvalidOperationException("Failed to deserialize settings");

        return settings;
    }

    public Task<bool> ValidateBackupAsync(byte[] data)
    {
        if (data == null || data.Length < MagicHeader.Length)
            return Task.FromResult(false);

        for (int i = 0; i < MagicHeader.Length; i++)
        {
            if (data[i] != MagicHeader[i])
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, 
            salt, 
            Iterations, 
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
