using System.Security.Cryptography;
using System.Text;

namespace Grafirio.DataAnalysis.Api.Data;

/// <summary>
/// Musteri veritabani sifreleri icin AES-256-CBC sifreleme.
///
/// Onceki surumde dort ayri sorun vardi, dordu de uretimde acikti:
///
///   1. Anahtar yoksa kaynak kodda yazili sabit bir metne dusuyordu. Azure'da
///      ENCRYPTION_KEY hic tanimli olmadigi icin butun musteri sifreleri,
///      repoya erisen herkesin cozebilecegi bir anahtarla sifreleniyordu.
///      Artik anahtar zorunlu; yoksa servis acilista duruyor.
///   2. IV sabitti. Ayni sifre hep ayni sifreli metni uretiyordu; bu, iki
///      musterinin ayni parolayi kullandigini disaridan gorunur kilar.
///      Artik her sifrelemede rastgele IV uretilip ciktinin basina yaziliyor.
///   3. Cozulen metnin ilk karakteri Console'a yaziliyordu. Sifre iceriginin
///      hicbir parcasi loglanmamali; o satirlar kaldirildi.
///   4. Cozme hatasi yutulup bos string donuyordu. Cagiran taraf bunu gecerli
///      bir sifre sanip baglaniyor, kullanici da sebebi yazmayan bir
///      "Login failed" goruyordu. Artik acik hata firlatiliyor.
///
/// Bicim: "v2:" + base64(IV(16 bayt) || sifreli metin)
/// Onekli olmayan kayitlar eski bicimdir; geriye donuk okunabilsinler diye
/// sabit IV ile cozulur. Anahtar degistirildiginde ENCRYPTION_KEY_PREVIOUS
/// tanimlanarak eski kayitlar da okunmaya devam eder (anahtar rotasyonu).
/// </summary>
public static class EncryptionHelper
{
    private const string VersionPrefix = "v2:";
    private const int IvLength = 16;

    /// <summary>Eski kayitlarin sabit IV'si. Yalnizca okuma icin duruyor.</summary>
    private static readonly byte[] LegacyIv =
        Encoding.UTF8.GetBytes("GrafiirioIV2026".PadRight(IvLength)[..IvLength]);

    private static readonly Lazy<byte[]> CurrentKey = new(() =>
        DeriveKey(Environment.GetEnvironmentVariable("ENCRYPTION_KEY"), required: true)!);

    private static readonly Lazy<byte[]?> PreviousKey = new(() =>
        DeriveKey(Environment.GetEnvironmentVariable("ENCRYPTION_KEY_PREVIOUS"), required: false));

    private static byte[]? DeriveKey(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!required) return null;
            throw new InvalidOperationException(
                "ENCRYPTION_KEY tanımlı değil. Müşteri veritabanı şifreleri bu anahtarla " +
                "korunuyor; varsayılan bir değere düşmek yerine servis başlatılmıyor.");
        }

        if (value.Length < 16)
            throw new InvalidOperationException("ENCRYPTION_KEY en az 16 karakter olmalı.");

        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Acilista cagrilir: anahtar eksikse hata, ilk sifreleme isleminde degil,
    /// servis ayaga kalkarken ve acik bir mesajla ortaya ciksin.
    /// </summary>
    public static void EnsureConfigured() => _ = CurrentKey.Value;

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = CurrentKey.Value;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(payload, 0);
        cipherBytes.CopyTo(payload, aes.IV.Length);

        return VersionPrefix + Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Sifreli metni cozer. Cozulemezse <see cref="CryptographicException"/> firlatir.
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        if (encryptedText.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            var payload = Convert.FromBase64String(encryptedText[VersionPrefix.Length..]);
            if (payload.Length <= IvLength)
                throw new CryptographicException("Şifreli veri geçersiz: IV eksik.");

            return Transform(payload[IvLength..], payload[..IvLength], CurrentKey.Value);
        }

        // Onekli olmayan kayit: eski bicim, sabit IV.
        var legacyBytes = Convert.FromBase64String(encryptedText);
        try
        {
            return Transform(legacyBytes, LegacyIv, CurrentKey.Value);
        }
        catch (CryptographicException)
        {
            if (PreviousKey.Value is null) throw;
            return Transform(legacyBytes, LegacyIv, PreviousKey.Value);
        }
    }

    /// <summary>
    /// Kayit eski bicimde mi. Cagiran taraf isterse cozup yeniden sifreleyerek
    /// kaydi guncel bicime tasiyabilir.
    /// </summary>
    public static bool NeedsReEncryption(string? encryptedText) =>
        !string.IsNullOrEmpty(encryptedText) &&
        !encryptedText.StartsWith(VersionPrefix, StringComparison.Ordinal);

    private static string Transform(byte[] cipherBytes, byte[] iv, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
