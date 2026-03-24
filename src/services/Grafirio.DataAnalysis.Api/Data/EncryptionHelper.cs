using System.Security.Cryptography;
using System.Text;

namespace Grafirio.DataAnalysis.Api.Data;

/// <summary>
/// AES256 encryption/decryption helper for sensitive data
/// </summary>
public static class EncryptionHelper
{
    private static readonly byte[] Key;
    private static readonly byte[] IV;
    
    static EncryptionHelper()
    {
        // Production'da bu değerler environment variable'dan alınmalı
        var encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") 
            ?? "GrafiirioDataAnalysisEncryptionKey2026!SecurePassword";
        
        using var sha256 = SHA256.Create();
        Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey));
        
        // IV için sabit değer (Production'da farklı olmalı)
        var ivSource = "GrafiirioIV2026";
        IV = Encoding.UTF8.GetBytes(ivSource.PadRight(16).Substring(0, 16));
    }
    
    /// <summary>
    /// Şifre AES256 ile şifreler
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;
        
        Console.WriteLine($"[ENCRYPT] Input length: {plainText.Length}");
        
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = IV;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        
        var result = Convert.ToBase64String(encryptedBytes);
        Console.WriteLine($"[ENCRYPT] Output length: {result.Length}");
        return result;
    }
    
    /// <summary>
    /// Şifrelenmiş veriyi çözer
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
        {
            Console.WriteLine("[DECRYPT] Input is null or empty");
            return string.Empty;
        }
        
        Console.WriteLine($"[DECRYPT] Input length: {encryptedText.Length}");
        
        try
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = IV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using var decryptor = aes.CreateDecryptor();
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
            
            var result = Encoding.UTF8.GetString(decryptedBytes);
            Console.WriteLine($"[DECRYPT] Output length: {result.Length} (First char: {result[0]})");
            return result;
        }
        catch (Exception ex)
        {
            // Decryption failed
            Console.WriteLine($"[DECRYPT] ERROR: {ex.Message}");
            return string.Empty;
        }
    }
}
