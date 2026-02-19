using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Stores Google identities with service account JSON in secure storage.
/// Only non-sensitive metadata is stored in the JSON file.
/// </summary>
public class GoogleIdentityService : IGoogleIdentityService
{
    private readonly ISecureStorageService _secureStorage;
    private readonly IFileSystemService _fileSystem;
    private readonly ILoggingService _logger;
    private readonly string _settingsPath;
    private List<GoogleIdentityMetadata> _identities = new();

    private record GoogleIdentityMetadata(string Id, string Name, string ProjectId, string ClientEmail, string? ServiceAccountJsonPath);

    private const string SecureKeyPrefix = "google_identity_sa_";

    public GoogleIdentityService(ISecureStorageService secureStorage, IFileSystemService fileSystem, ILoggingService logger)
    {
        _secureStorage = secureStorage;
        _fileSystem = fileSystem;
        _logger = logger;
        _settingsPath = Path.Combine(
            AppDataPath.GetAppDataDirectory(),
            "google-identities.json");
    }

    public async Task<IReadOnlyList<GoogleIdentity>> GetIdentitiesAsync()
    {
        await LoadIdentitiesAsync();
        var result = new List<GoogleIdentity>();

        foreach (var meta in _identities)
        {
            var saJson = await _secureStorage.GetAsync(SecureKeyPrefix + meta.Id);
            result.Add(new GoogleIdentity(
                meta.Id, meta.Name, meta.ProjectId, meta.ClientEmail, meta.ServiceAccountJsonPath, saJson));
        }

        return result.AsReadOnly();
    }

    public async Task<GoogleIdentity?> GetIdentityAsync(string id)
    {
        await LoadIdentitiesAsync();
        var meta = _identities.FirstOrDefault(i => i.Id == id);
        if (meta == null) return null;

        var saJson = await _secureStorage.GetAsync(SecureKeyPrefix + id);
        return new GoogleIdentity(meta.Id, meta.Name, meta.ProjectId, meta.ClientEmail, meta.ServiceAccountJsonPath, saJson);
    }

    public async Task SaveIdentityAsync(GoogleIdentity identity)
    {
        await LoadIdentitiesAsync();

        if (!string.IsNullOrEmpty(identity.ServiceAccountJson))
        {
            await _secureStorage.SetAsync(SecureKeyPrefix + identity.Id, identity.ServiceAccountJson);
        }

        var meta = new GoogleIdentityMetadata(
            identity.Id, identity.Name, identity.ProjectId, identity.ClientEmail, identity.ServiceAccountJsonPath);

        var existing = _identities.FindIndex(i => i.Id == identity.Id);
        if (existing >= 0)
            _identities[existing] = meta;
        else
            _identities.Add(meta);

        await PersistIdentitiesAsync();
        _logger.LogInformation($"Saved Google identity: {identity.Name} (service account JSON stored securely)");
    }

    public async Task DeleteIdentityAsync(string id)
    {
        await LoadIdentitiesAsync();

        await _secureStorage.RemoveAsync(SecureKeyPrefix + id);

        var removed = _identities.RemoveAll(i => i.Id == id);
        if (removed > 0)
        {
            await PersistIdentitiesAsync();
            _logger.LogInformation($"Deleted Google identity: {id}");
        }
    }

    public async Task<bool> TestConnectionAsync(GoogleIdentity identity)
    {
        try
        {
            var saJson = identity.ServiceAccountJson;
            if (string.IsNullOrEmpty(saJson) && !string.IsNullOrEmpty(identity.ServiceAccountJsonPath))
            {
                saJson = await _fileSystem.ReadFileAsync(identity.ServiceAccountJsonPath);
            }

            if (string.IsNullOrEmpty(saJson))
            {
                _logger.LogError("No service account JSON available");
                return false;
            }

            // Validate JSON structure
            var doc = JsonSerializer.Deserialize<JsonElement>(saJson);
            if (doc.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Service account JSON is not a valid object");
                return false;
            }

            var hasType = doc.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "service_account";
            var hasProjectId = doc.TryGetProperty("project_id", out _);
            var hasPrivateKey = doc.TryGetProperty("private_key", out _);
            var hasClientEmail = doc.TryGetProperty("client_email", out _);

            if (hasType && hasProjectId && hasPrivateKey && hasClientEmail)
            {
                _logger.LogInformation($"Connection test passed (validation only) for: {identity.Name}");
                return true;
            }

            _logger.LogError("Service account JSON missing required fields (type, project_id, private_key, client_email)");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Invalid JSON format: {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Parse project_id and client_email from service account JSON.
    /// </summary>
    public static (string? ProjectId, string? ClientEmail) ParseServiceAccountJson(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            string? projectId = null;
            string? clientEmail = null;

            if (doc.TryGetProperty("project_id", out var pid))
                projectId = pid.GetString();
            if (doc.TryGetProperty("client_email", out var ce))
                clientEmail = ce.GetString();

            return (projectId, clientEmail);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task LoadIdentitiesAsync()
    {
        try
        {
            if (await _fileSystem.FileExistsAsync(_settingsPath))
            {
                var json = await _fileSystem.ReadFileAsync(_settingsPath);
                if (!string.IsNullOrEmpty(json))
                {
                    _identities = JsonSerializer.Deserialize<List<GoogleIdentityMetadata>>(json) ?? new();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load Google identities: {ex.Message}");
        }
        _identities = new();
    }

    private async Task PersistIdentitiesAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                await _fileSystem.CreateDirectoryAsync(directory);

            var json = JsonSerializer.Serialize(_identities, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await _fileSystem.WriteFileAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to persist Google identities: {ex.Message}", ex);
        }
    }
}
