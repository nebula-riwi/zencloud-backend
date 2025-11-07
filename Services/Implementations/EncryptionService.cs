using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations
{
    public class EncryptionService : IEncryptionService
    {
        private readonly string _encryptionKey;

        public EncryptionService(IConfiguration configuration)
        {
            // Usar JWT_KEY como clave de encriptación (ya que es lo suficientemente larga y segura)
            _encryptionKey = configuration["JWT_KEY"] ?? "default-encryption-key-32-chars-long!";
            
            // Asegurar que la clave tenga exactamente 32 caracteres para AES-256
            if (_encryptionKey.Length < 32)
                _encryptionKey = _encryptionKey.PadRight(32, '0');
            else if (_encryptionKey.Length > 32)
                _encryptionKey = _encryptionKey.Substring(0, 32);
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                aes.IV = new byte[16]; // IV simple para desarrollo

                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var memoryStream = new MemoryStream();
                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainText);
                    cryptoStream.Write(plainBytes, 0, plainBytes.Length);
                }
                
                return Convert.ToBase64String(memoryStream.ToArray());
            }
            catch
            {
                // Fallback simple para desarrollo - solo codifica en Base64
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            }
        }

        public string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                aes.IV = new byte[16]; // IV simple para desarrollo

                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var memoryStream = new MemoryStream(Convert.FromBase64String(encryptedText));
                using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
                using var streamReader = new StreamReader(cryptoStream);
                
                return streamReader.ReadToEnd();
            }
            catch
            {
                // Fallback simple para desarrollo - solo decodifica Base64
                try
                {
                    var bytes = Convert.FromBase64String(encryptedText);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    return encryptedText;
                }
            }
        }
    }
}