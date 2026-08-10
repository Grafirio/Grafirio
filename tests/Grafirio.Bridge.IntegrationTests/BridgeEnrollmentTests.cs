using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Kayıt ve kimlik doğrulama, gerçek Mongo'ya karşı.
///
/// Parite testi bu yolu taklit edilmiş bir kimlik doğrulayıcıyla geçiyordu;
/// buradaki, bir bridge'i şirkete bağlayan gerçek zinciri ölçüyor. Zincirin
/// tek zayıf halkası bile, yanlış şirketin verisine erişim demek.
/// </summary>
public class BridgeEnrollmentTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private BridgeStore Store() => new(mongo.Database, NullLogger<BridgeStore>.Instance);

    [SkippableFact]
    public async Task Token_uretilip_bir_kez_harcaniyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var token = await store.CreateEnrollmentTokenAsync("firma-a", "kullanici-1");

        Assert.Equal("firma-a", await store.RedeemEnrollmentTokenAsync(token));

        // İkinci kullanım geçmemeli: token bir bridge'i şirkete bağlayan tek
        // şey ve dolaşımda kalırsa başkası kendi bridge'ini kaydettirebilir.
        Assert.Null(await store.RedeemEnrollmentTokenAsync(token));
    }

    [SkippableFact]
    public async Task Gecersiz_token_kabul_edilmiyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        Assert.Null(await Store().RedeemEnrollmentTokenAsync("uydurma-token"));
    }

    [SkippableFact]
    public async Task Kayitli_bridge_kendi_sirriyla_taniniyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var (bridgeId, secret) = await store.RegisterAsync(
            "firma-a", "Merkez", "SRV-01", "1.0.0");

        var bridge = await store.AuthenticateAsync(bridgeId, secret);

        Assert.NotNull(bridge);
        Assert.Equal("firma-a", bridge.CompanyId);
        Assert.Equal("SRV-01", bridge.MachineName);
    }

    [SkippableFact]
    public async Task Yanlis_sir_reddediliyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var (bridgeId, _) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        Assert.Null(await store.AuthenticateAsync(bridgeId, "yanlis-sir"));
    }

    [SkippableFact]
    public async Task Iptal_edilen_bridge_artik_taninmiyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var (bridgeId, secret) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        Assert.True(await store.RevokeAsync(bridgeId, "firma-a"));
        Assert.Null(await store.AuthenticateAsync(bridgeId, secret));
    }

    [SkippableFact]
    public async Task Baska_sirket_bridge_i_iptal_edemiyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var (bridgeId, secret) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        Assert.False(await store.RevokeAsync(bridgeId, "firma-b"));
        Assert.NotNull(await store.AuthenticateAsync(bridgeId, secret));
    }

    [SkippableFact]
    public async Task Listede_yalnizca_kendi_sirketinin_bridge_leri_gorunuyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        await store.RegisterAsync("firma-a", "A", "SRV-A", "1.0.0");
        await store.RegisterAsync("firma-b", "B", "SRV-B", "1.0.0");

        var list = await store.ListAsync("firma-a");

        Assert.All(list, b => Assert.Equal("firma-a", b.CompanyId));
        Assert.Contains(list, b => b.MachineName == "SRV-A");
        Assert.DoesNotContain(list, b => b.MachineName == "SRV-B");
    }

    [SkippableFact]
    public async Task Baglanti_bridge_e_baglanip_cozuluyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = Store();
        var connectionId = Guid.NewGuid();
        var (bridgeId, _) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        // Başlangıçta bağlı değil: yani doğrudan bağlantı.
        Assert.Null(await store.GetBoundBridgeAsync(connectionId));

        await store.BindConnectionAsync(connectionId, "firma-a", bridgeId);
        Assert.Equal(bridgeId, await store.GetBoundBridgeAsync(connectionId));

        // null'a çevirmek bağlantıyı doğrudan moda geri döndürmeli.
        await store.BindConnectionAsync(connectionId, "firma-a", null);
        Assert.Null(await store.GetBoundBridgeAsync(connectionId));
    }
}
