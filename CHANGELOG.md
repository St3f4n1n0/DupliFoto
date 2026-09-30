# Novità

## [0.1.0] - 2026-09-30

Prima versione pubblica.

- **Interfaccia grafica** (`DupliFoto2026.exe`), in stile Windows 11 con tema chiaro e scuro:
  - cartelle da aggiungere o trascinare, e una cartella preferita;
  - confronto affiancato con l'affidabilità;
  - spostamento, scambio, "tieni entrambe", annulla, report.
- **Riga di comando** (`duplifoto.exe`): modalità sola lettura, assistita, semi-automatica e automatica, report HTML/CSV, annulla dal registro.
- **Ricerca a imbuto**: file identici al byte, stessi pixel, stessa immagine ricompressa, ridimensionata o ruotata, scatti multipli.
- **Formati**: JPEG, PNG, HEIC, AVIF, WebP, TIFF, JPEG XL e i principali RAW.
- **Rete neurale facoltativa** (DINOv2) con Windows ML su NPU, GPU o CPU.
- **Sicurezza**:
  - nessuna cancellazione: si sposta in quarantena o nel Cestino, e solo se Windows può davvero metterci il file;
  - verifica byte per byte prima di spostare i file identici;
  - registro per annullare.
- **Sistemi**: Windows 10 (1809 o successivo) e Windows 11, x64 e ARM64. Programmi autonomi, niente da installare.
