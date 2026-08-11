// Proje hem WPF hem Windows Forms kullaniyor (tepsi simgesi icin) ve iki
// cerceve ayni adlari tasiyor. Belirsizligi her dosyada tek tek cozmek yerine
// tercih burada bir kez soyleniyor: arayuz WPF'in, Windows Forms yalnizca
// tepsi simgesi icin var.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;

// Cekirdek projede bunlar Worker SDK'siyla ortuluydu; burada degil.
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
