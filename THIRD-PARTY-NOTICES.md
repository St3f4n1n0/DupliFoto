# Componenti di terze parti

Il codice di DupliFoto è distribuito con licenza MIT (vedi `LICENSE`). I programmi pubblicati nelle release contengono anche i componenti elencati qui sotto, ciascuno con la propria licenza. Nello zip della release, la cartella `licenze/` contiene i testi completi forniti dagli autori.

| Componente | Uso | Licenza | Testo completo |
|---|---|---|---|
| [.NET](https://github.com/dotnet/runtime) | runtime incluso nel programma | MIT | `licenze/dotnet-LICENSE.txt`, `licenze/dotnet-THIRD-PARTY-NOTICES.txt` |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) e [Windows ML](https://learn.microsoft.com/windows/ai/new-windows-ml/overview), con ONNX Runtime e DirectML | rete neurale su NPU/GPU/CPU | Microsoft Software License Terms (componenti ridistribuibili) | `licenze/WindowsAppSDK-license.txt`, `licenze/WindowsML-license.txt`, `licenze/WindowsML-ThirdPartyNotices.txt` |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) | decodifica delle immagini | Apache-2.0 | `licenze/Magick.NET-Notice.txt` |
| ImageMagick e le librerie native incluse in Magick.NET (libjpeg-turbo, libpng, libwebp, libheif, libde265, LibRaw, libjxl, libaom, zlib e altre) | formati JPEG, PNG, HEIC, AVIF, WebP, RAW... | ImageMagick License, BSD, MIT, zlib, LGPL e altre, come indicato nel file | `licenze/Magick.NET-Notice.txt` |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | lettura dei dati EXIF | Apache-2.0 | [testo](https://www.apache.org/licenses/LICENSE-2.0) |
| [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) | lettura dei dati XMP (usato da MetadataExtractor) | BSD (Adobe XMP) | [testo](https://www.adobe.com/devnet/xmp/library/eula-xmp-library-java.html) |
| [System.IO.Hashing, System.Numerics.Tensors](https://github.com/dotnet/runtime) | hash xxHash, tensori | MIT | `licenze/dotnet-LICENSE.txt` |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | interfaccia grafica | MIT | [testo](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) |
| [SkiaSharp e HarfBuzzSharp](https://github.com/mono/SkiaSharp), con Skia, HarfBuzz e ANGLE | disegno e testo | MIT; Skia e ANGLE BSD-3-Clause; HarfBuzz "Old MIT" | [testo](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) |
| [Inter](https://github.com/rsms/inter) | carattere di riserva (dove Segoe UI non c'è) | SIL Open Font License 1.1 | [testo](https://github.com/rsms/inter/blob/master/LICENSE.txt) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | struttura dell'interfaccia | MIT | [testo](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [Tmds.DBus](https://github.com/tmds/Tmds.DBus), MicroCom | usati da Avalonia | MIT | [testo](https://github.com/tmds/Tmds.DBus/blob/main/LICENSE) |

Il modello facoltativo DINOv2 (`tools/export_dinov2.py`) non è incluso: chi lo esporta lo scarica da Meta, con licenza Apache-2.0.
