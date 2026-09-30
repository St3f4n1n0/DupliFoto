# Third-party notices

DupliFoto's own code is released under the MIT License (see `LICENSE`). The published executables also contain the components below, each under its own license. Every release ships their full license texts in `DupliFoto-<version>-licenses.zip`.

| Component | Used for | License | Full text |
|---|---|---|---|
| [.NET](https://github.com/dotnet/runtime) | runtime bundled in the executables | MIT | `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) and [Windows ML](https://learn.microsoft.com/windows/ai/new-windows-ml/overview), including ONNX Runtime and DirectML | neural model on NPU, GPU or CPU | Microsoft Software License Terms (redistributable components) | `WindowsAppSDK-license.txt`, `WindowsML-license.txt`, `WindowsML-ThirdPartyNotices.txt` |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) | image decoding | Apache-2.0 | `Magick.NET-Notice.txt` |
| ImageMagick and the native libraries bundled with Magick.NET (libjpeg-turbo, libpng, libwebp, libheif, libde265, LibRaw, libjxl, libaom, zlib and others) | JPEG, PNG, HEIC, AVIF, WebP, RAW and other formats | ImageMagick License, BSD, MIT, zlib, LGPL and others, as listed in the file | `Magick.NET-Notice.txt` |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | EXIF metadata | Apache-2.0 | [text](https://www.apache.org/licenses/LICENSE-2.0) |
| [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) | XMP metadata (used by MetadataExtractor) | BSD (Adobe XMP) | [text](https://www.adobe.com/devnet/xmp/library/eula-xmp-library-java.html) |
| [System.IO.Hashing, System.Numerics.Tensors](https://github.com/dotnet/runtime) | xxHash, tensors | MIT | `dotnet-LICENSE.txt` |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | user interface | MIT | [text](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) |
| [SkiaSharp and HarfBuzzSharp](https://github.com/mono/SkiaSharp), with Skia, HarfBuzz and ANGLE | drawing and text | MIT; Skia and ANGLE BSD-3-Clause; HarfBuzz "Old MIT" | [text](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) |
| [Inter](https://github.com/rsms/inter) | fallback font where Segoe UI is missing | SIL Open Font License 1.1 | [text](https://github.com/rsms/inter/blob/master/LICENSE.txt) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | user interface structure | MIT | [text](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [Tmds.DBus](https://github.com/tmds/Tmds.DBus), MicroCom | used by Avalonia | MIT | [text](https://github.com/tmds/Tmds.DBus/blob/main/LICENSE) |

The optional DINOv2 model (`tools/export_dinov2.py`) is not bundled. Whoever exports it downloads it from Meta under the Apache-2.0 license.
