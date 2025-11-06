using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class CredentialsGeneratorService : ICredentialsGeneratorService
{
    public string GenerateDatabaseName(string engineName, Guid userId)
    {
        var userPrefix = userId.ToString().Substring(0, 8);
        var timeStamp = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        return $"{engineName.ToLower()}_{userPrefix}_{timeStamp}";
    }

    public string GenerateUsername(string databaseName)
    {
        return $"_user{databaseName}";
    }

    public string GeneratePassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";
        var random = new char[16];
        
        for (int i = 0; i < random.Length; i++)
        {
            random[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        }
        
        return new string(random);
    }

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = 8;
        argon2.Iterations = 4;
        argon2.MemorySize = 1024 * 128;
        
        var hash = argon2.GetBytes(16);
        
        var hashBytes = new byte[salt.Length + hash.Length];
        Array.Copy(salt, 0, hashBytes, 0, salt.Length);
        Array.Copy(hash, 0, hashBytes, salt.Length, hash.Length);
        
        return Convert.ToBase64String(hashBytes);
    }
}