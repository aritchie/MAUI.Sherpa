using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ManagedSecretsService : IManagedSecretsService
{
    readonly ICloudSecretsService _cloudService;
    readonly ILoggingService _logger;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ManagedSecretsService(ICloudSecretsService cloudService, ILoggingService logger)
    {
        _cloudService = cloudService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return Array.Empty<ManagedSecret>();

        // List metadata keys as the source of truth (value keys may be sanitized by provider)
        var metaKeys = await _cloudService.ListSecretsAsync(IManagedSecretsService.MetadataPrefix, cancellationToken);
        var secrets = new List<ManagedSecret>();

        foreach (var fullMetaKey in metaKeys)
        {
            try
            {
                var metaBytes = await _cloudService.GetSecretAsync(fullMetaKey, cancellationToken);
                if (metaBytes is null)
                    continue;

                var json = System.Text.Encoding.UTF8.GetString(metaBytes);
                var meta = JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
                if (meta is not null)
                    secrets.Add(meta);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load metadata for '{fullMetaKey}': {ex.Message}");
            }
        }

        return secrets;
    }

    public async Task<ManagedSecret?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return null;

        return await LoadMetadataAsync(key, cancellationToken);
    }

    public async Task<byte[]?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return null;

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        return await _cloudService.GetSecretAsync(fullKey, cancellationToken);
    }

    public async Task<bool> CreateAsync(string key, byte[] value, ManagedSecretType type,
        string? description = null, string? originalFileName = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        var now = DateTime.UtcNow;

        var stored = await _cloudService.StoreSecretAsync(fullKey, value, cancellationToken: cancellationToken);
        if (!stored)
            return false;

        var meta = new ManagedSecret(key, type, description, originalFileName, now, now);
        await SaveMetadataAsync(meta, cancellationToken);

        _logger.LogInformation($"Created managed secret: {key} (type: {type})");
        return true;
    }

    public async Task<bool> UpdateAsync(string key, byte[]? value = null, string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var existing = await LoadMetadataAsync(key, cancellationToken);
        if (existing is null)
            return false;

        if (value is not null)
        {
            var fullKey = IManagedSecretsService.SecretPrefix + key;
            var stored = await _cloudService.StoreSecretAsync(fullKey, value, cancellationToken: cancellationToken);
            if (!stored)
                return false;
        }

        var updated = existing with
        {
            Description = description ?? existing.Description,
            UpdatedAt = DateTime.UtcNow
        };
        await SaveMetadataAsync(updated, cancellationToken);

        _logger.LogInformation($"Updated managed secret: {key}");
        return true;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        var metaKey = IManagedSecretsService.MetadataPrefix + key;

        var deleted = await _cloudService.DeleteSecretAsync(fullKey, cancellationToken);

        // Always try to delete metadata even if value deletion failed
        try
        {
            await _cloudService.DeleteSecretAsync(metaKey, cancellationToken);
        }
        catch
        {
            // Metadata cleanup is best-effort
        }

        _logger.LogInformation($"Deleted managed secret: {key}");
        return deleted;
    }

    async Task<ManagedSecret?> LoadMetadataAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var metaKey = IManagedSecretsService.MetadataPrefix + key;
            var metaBytes = await _cloudService.GetSecretAsync(metaKey, cancellationToken);
            if (metaBytes is null)
                return null;

            var json = System.Text.Encoding.UTF8.GetString(metaBytes);
            return JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load metadata for secret '{key}': {ex.Message}");
            return null;
        }
    }

    async Task SaveMetadataAsync(ManagedSecret meta, CancellationToken cancellationToken)
    {
        var metaKey = IManagedSecretsService.MetadataPrefix + meta.Key;
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await _cloudService.StoreSecretAsync(metaKey, bytes, cancellationToken: cancellationToken);
    }
}
