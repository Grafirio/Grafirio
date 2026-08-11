// Proje hem WPF hem Windows Forms kullaniyor (tepsi simgesi icin) ve iki
// cerceve ayni adlari tasiyor. Belirsizligi her dosyada tek tek cozmek yerine
// tercih burada bir kez soyleniyor: arayuz WPF'in, Windows Forms yalnizca
// tepsi simgesi icin var.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
