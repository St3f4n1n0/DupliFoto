using DupliFoto.Tests;

namespace DupliFoto.Gui.Tests;

/// <summary>Una cartella di foto vere (JPEG e PNG) con doppioni noti, creata per ogni test.</summary>
public sealed class SamplePhotos : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "duplifoto-gui-" + Guid.NewGuid().ToString("N"));
    public string Photos => Path.Combine(Root, "Foto");
    public string Quarantine => Path.Combine(Root, "Quarantena");

    public SamplePhotos()
    {
        Directory.CreateDirectory(Path.Combine(Photos, "WhatsApp"));
        var shot = new DateTime(2026, 8, 10, 18, 30, 0);
        var gps = new RealImages.Exif(shot, Latitude: 45.4642, Longitude: 9.19);

        RealImages.SaveJpeg(TestImages.Scene(10, w: 1200, h: 900), P("mare.jpg"), exif: gps);
        File.Copy(P("mare.jpg"), P("mare (1).jpg"));
        RealImages.SaveJpeg(TestImages.Scene(10, w: 600, h: 450), P("WhatsApp/IMG-20260810-WA0001.jpg"), quality: 60);

        RealImages.SavePng(TestImages.Scene(20, w: 800, h: 600), P("fiore.png"));
        RealImages.SavePng(TestImages.Scene(20, w: 800, h: 600), P("fiore-esportata.png"), comment: "esportata");

        RealImages.SaveJpeg(TestImages.Scene(30, w: 1200, h: 900), P("raffica_1.jpg"), exif: gps with { TakenAt = shot.AddMinutes(5) });
        RealImages.SaveJpeg(TestImages.Resize(TestImages.Resize(TestImages.Scene(30, w: 1200, h: 900, shiftX: 0.02), 300, 225), 1200, 900),
            P("raffica_2.jpg"), exif: gps with { TakenAt = shot.AddMinutes(5).AddSeconds(1.5) });

        RealImages.SaveJpeg(TestImages.Scene(40, w: 1200, h: 900), P("montagna.jpg"), exif: gps with { TakenAt = shot.AddHours(1) });
    }

    public string P(string relative) => Path.Combine(Photos, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* pulizia best effort */ }
    }
}
