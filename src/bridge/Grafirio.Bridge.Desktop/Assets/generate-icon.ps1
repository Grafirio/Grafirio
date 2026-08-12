# Uygulama ikonunu (app.ico) marka isaretinden uretir.
#
# Kaynak BrandMark.xaml — yani src/front/grifirio.front/public/favicon.svg'nin
# vektor karsiligi. Ikon elle cizilmiyor: logo degistiginde tek yapilacak sey
# bu script'i yeniden calistirmak.
#
# Neden ayri bir dosya olarak commit ediliyor: exe'nin Explorer'da ve gorev
# cubugunda gordugu ikon, derleme aninda Win32 kaynagi olarak gomuluyor
# (<ApplicationIcon>). Calisma aninda ayarlanan pencere/tepsi ikonu bunun
# yerine gecmiyor — dosyanin kendisi yine varsayilan Windows ikonuyla iniyor.
#
#   pwsh -File generate-icon.ps1   (ya da Windows PowerShell)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$markPath = Join-Path $here '..\BrandMark.xaml'
$outPath = Join-Path $here 'app.ico'

# Kucukten buyuge: Explorer kucuk gorunumde 16'yi, gorev cubugu 32'yi, buyuk
# simge gorunumu 256'yi kullaniyor. Hepsi ayni dosyada olmazsa Windows en
# yakinini olcekliyor ve sonuc bulaniklasiyor.
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$xaml = Get-Content $markPath -Raw
$dictionary = [System.Windows.Markup.XamlReader]::Parse($xaml)
$mark = $dictionary['BrandMark']

if ($null -eq $mark) { throw "BrandMark kaynagi bulunamadi: $markPath" }

$frames = @()

foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $rect = New-Object System.Windows.Rect 0, 0, $size, $size
    $context.DrawImage($mark, $rect)
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
        $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    if ($size -ge 256) {
        # 256 PNG olarak: bu boyutta ham BMP ~256 KB tutardi ve PNG, bicimin
        # bu boyut icin ongordugu saklama sekli.
        $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

        $stream = New-Object System.IO.MemoryStream
        $encoder.Save($stream)
        $data = $stream.ToArray()
        $stream.Dispose()
    }
    else {
        # Kucuk boyutlar sikistirilmamis DIB olarak. PNG girdileri Windows
        # Explorer'da calisiyor ama eski GDI+ tabanli okuyucular (ornegin
        # System.Drawing.Icon) onlari acamiyor; gercek ikon araclarinin
        # yaptigi da bu ayrim.
        #
        # Pbgra32 -> Bgra32: ICO onceden carpilmamis alfa bekliyor, aksi
        # halde yari saydam kenarlar koyulasiyor.
        $converted = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
        $converted.BeginInit()
        $converted.Source = $bitmap
        $converted.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
        $converted.EndInit()

        $stride = $size * 4
        $pixels = New-Object byte[] ($stride * $size)
        $converted.CopyPixels($pixels, $stride, 0)

        $maskStride = [int]([math]::Floor(($size + 31) / 32) * 4)

        $dib = New-Object System.IO.MemoryStream
        $dibWriter = New-Object System.IO.BinaryWriter $dib

        # BITMAPINFOHEADER. Yukseklik iki katı: bicim, renk verisinin
        # ardindan bir de maske katmani bekliyor.
        $dibWriter.Write([uint32]40)
        $dibWriter.Write([int32]$size)
        $dibWriter.Write([int32]($size * 2))
        $dibWriter.Write([uint16]1)
        $dibWriter.Write([uint16]32)
        $dibWriter.Write([uint32]0)                       # sikistirma yok
        $dibWriter.Write([uint32]($stride * $size + $maskStride * $size))
        $dibWriter.Write([int32]0); $dibWriter.Write([int32]0)
        $dibWriter.Write([uint32]0); $dibWriter.Write([uint32]0)

        # DIB satirlari alttan yukari saklaniyor.
        for ($y = $size - 1; $y -ge 0; $y--) {
            $dibWriter.Write($pixels, $y * $stride, $stride)
        }

        # Maske: 32 bit alfa zaten saydamligi tasidigi icin tamamen sifir.
        $emptyMask = New-Object byte[] ($maskStride * $size)
        $dibWriter.Write($emptyMask)

        $dibWriter.Flush()
        $data = $dib.ToArray()
        $dibWriter.Dispose()
        $dib.Dispose()
    }

    $frames += [pscustomobject]@{ Size = $size; Data = $data }
}

# ICO bicimi: 6 baytlik baslik, her goruntu icin 16 baytlik dizin girdisi,
# ardindan goruntu verileri. Vista'dan beri girdiler PNG olarak saklanabiliyor.
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output

$writer.Write([uint16]0)               # ayrilmis
$writer.Write([uint16]1)               # tur: 1 = ikon
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)

foreach ($frame in $frames) {
    # 256 piksel, bir bayta sigmadigi icin 0 olarak yaziliyor — bicimin kurali.
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }

    $writer.Write([byte]$dimension)     # genislik
    $writer.Write([byte]$dimension)     # yukseklik
    $writer.Write([byte]0)              # palet rengi yok
    $writer.Write([byte]0)              # ayrilmis
    $writer.Write([uint16]1)            # duzlem
    $writer.Write([uint16]32)           # bit derinligi
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)

    $offset += $frame.Data.Length
}

foreach ($frame in $frames) { $writer.Write($frame.Data) }

$writer.Flush()
[System.IO.File]::WriteAllBytes($outPath, $output.ToArray())
$writer.Dispose()
$output.Dispose()

$sizeLabel = '{0:N0}' -f (Get-Item $outPath).Length
Write-Host "Yazildi: $outPath ($sizeLabel bayt, $($frames.Count) boyut)"
