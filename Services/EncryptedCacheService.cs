using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamStorm.Metrics.Options;

namespace TeamStorm.Metrics.Services;

public sealed class EncryptedCacheService : IEncryptedCacheService
{
    private readonly string _cacheDir;
    private readonly byte[] _key;

    public EncryptedCacheService(IWebHostEnvironment env, IConfiguration configuration)
    {
        _cacheDir = Path.Combine(env.ContentRootPath, "App_Data", "cache");
        Directory.CreateDirectory(_cacheDir);

        var configured = configuration["Storm:CacheEncryptionKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _key = Convert.FromBase64String(configured);
        }
        else
        {
            var seed = $"TeamStorm::{Environment.MachineName}::{configuration[$"{StormOptions.SectionName}:ApiToken"] ?? ""}::{configuration[$"{StormOptions.SectionName}:SessionToken"] ?? ""}";
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        }

        if (_key.Length != 32) _key = SHA256.HashData(_key);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        var now = DateTimeOffset.UtcNow;

        if (File.Exists(path))
        {
            try
            {
                var payload = await File.ReadAllBytesAsync(path, cancellationToken);
                var plain = Decrypt(payload);
                var wrapper = JsonSerializer.Deserialize<CacheEnvelope<T>>(plain);
                if (wrapper is not null && wrapper.ExpiresAt > now)
                {
                    return wrapper.Value;
                }
            }
            catch
            {
                // If cache corrupted — ignore and rebuild.
            }
        }

        var value = await factory();
        var envelope = new CacheEnvelope<T>(value, now.Add(ttl));
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var encrypted = Encrypt(json);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);
        return value;
    }

    private string GetPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_cacheDir, hash + ".bin");
    }

    private byte[] Encrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[12 + 16 + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, 12);
        Buffer.BlockCopy(tag, 0, output, 12, 16);
        Buffer.BlockCopy(cipher, 0, output, 28, cipher.Length);
        return output;
    }

    private byte[] Decrypt(byte[] payload)
    {
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset ExpiresAt);
}
