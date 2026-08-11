using Grafirio.Bridge;

var builder = Host.CreateApplicationBuilder(args);

// Windows servisi olarak kurulabilsin. Konsoldan calistirildiginda bu satir
// bir sey degistirmiyor, yani hata ayiklama icin de ayni ikili kullanilabilir.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Grafirio Bridge";
});

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));

// Bu ikilinin penceresi yok: konsoldan ya da servis olarak calisiyor.
// Pencereli surum (Grafirio.Bridge.Desktop) ayni cekirdegi kullaniyor ve
// kendi IBridgeDisplay'ini kaydediyor — iki kabuk arasindaki tek fark bu.
builder.Services.AddSingleton<IBridgeDisplay, ConsoleBridgeDisplay>();

builder.Services.AddBridgeCore();

var host = builder.Build();
host.Run();
