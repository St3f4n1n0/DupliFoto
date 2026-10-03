namespace DupliFoto.Cli;

/// <summary>L'aiuto, nelle due lingue: ognuna mostra i suoi nomi di comandi e opzioni, e ricorda che anche gli altri funzionano.</summary>
internal static class Help
{
    public const string Italian = """
        DupliFoto — trova foto doppie, copie ricompresse e scatti multipli

        USO
          duplifoto-cli [analizza] <cartella> [<cartella>...] [opzioni]
          duplifoto-cli annulla <registro.jsonl>    riporta i file dalla quarantena
          duplifoto-cli hardware                    mostra CPU, GPU e NPU che la rete neurale può usare
          duplifoto-cli --versione

        MODALITÀ  (--modo)
          sola-lettura   predefinita: solo report HTML/CSV, nessun file toccato
          assistita      chiede conferma per ogni gruppo
          semi-auto      sposta da sola SOLO i file identici al byte (verificati), chiede il resto
          auto           sposta da sola fino a --soglia (predefinita 99, minimo 90); il resto lo chiede

        OPZIONI
          --azione quarantena|cestino   dove spostare i doppioni (predefinita: quarantena). Chiavette, schede
                                        di memoria e dischi di rete non hanno il Cestino: lì si usa la quarantena
          --quarantena <cartella>       cartella di quarantena (predefinita: DupliFoto-Quarantena accanto all'exe)
          --verifica-completa           prima di spostare un file identico, riconfrontalo per intero con la
                                        copia da tenere (lento sui dischi esterni). Senza, si ricontrollano
                                        peso, data, inizio e fine del file: la ricerca li ha già confrontati
                                        per intero
          --preferisci <cartella>       tieni sempre le copie che stanno in questa cartella (ripetibile)
          --solo-tra-cartelle           confronta ogni cartella solo con le altre: i doppioni dentro
                                        la stessa cartella vengono ignorati (servono almeno due cartelle)
          --report <file.html>          dove salvare il report (accanto viene creato anche il .csv)
          --modello <file.onnx>         modello neurale per riconoscere gli scatti multipli (es. DINOv2)
          --acceleratore auto|npu|gpu|cpu   dove far lavorare il modello (predefinito: auto = NPU, poi GPU, poi CPU)
          --raffica <secondi>           distanza massima tra scatti multipli (predefinita: 10)
          --no-raffiche                 non cercare scatti multipli
          --no-sottocartelle  --nascosti  --no-cache  --thread <n>
          --non-interattivo             non fare domande: ciò che richiede conferma resta da rivedere
          --lingua it|en|auto           lingua di messaggi e report (predefinita: come Windows;
                                        anche con la variabile DUPLIFOTO_LANG)

          Funzionano anche i nomi inglesi (scan, undo, help, --mode read-only, --keep...):
          duplifoto-cli --language en help

        FILE DI LAVORO
          Cache, registro e report (aperto con un doppio clic) stanno in "DupliFoto-dati" accanto all'exe, e
          la quarantena in "DupliFoto-Quarantena", sempre accanto all'exe: sul PC non resta niente di sparso. Aperto con un doppio clic, alla chiusura toglie anche la copia
          di sé scompattata in %TEMP%\.net; da un terminale la lascia, per ripartire subito al comando dopo.
          «Pulisci DupliFoto.bat /si», in DupliFoto-dati, toglie tutto (report esclusi).

        ESEMPI
          duplifoto-cli "D:\Foto"
          duplifoto-cli "D:\Foto" "E:\Backup telefono" --modo semi-auto --preferisci "D:\Foto"
          duplifoto-cli "D:\Catalogate" "D:\Da sistemare" --solo-tra-cartelle --preferisci "D:\Catalogate"
          duplifoto-cli "D:\Foto" --modo auto --soglia 98 --modello dinov2-small.onnx --non-interattivo
        """;

    public const string English = """
        DupliFoto — finds duplicate photos, recompressed copies and burst shots

        USAGE
          duplifoto-cli [scan] <folder> [<folder>...] [options]
          duplifoto-cli undo <journal.jsonl>        puts the files back from quarantine
          duplifoto-cli hardware                    shows the CPU, GPU and NPU the neural network can use
          duplifoto-cli --version

        MODES  (--mode)
          read-only      default: HTML/CSV report only, no file touched
          assisted       asks for confirmation for every group
          semi-auto      moves on its own ONLY byte-identical files (checked again), asks for the rest
          auto           moves on its own down to --threshold (default 99, minimum 90); asks for the rest

        OPTIONS
          --action quarantine|recycle-bin   where duplicates go (default: quarantine). USB sticks, memory
                                        cards and network drives have no Recycle Bin: there the quarantine is used
          --quarantine <folder>         quarantine folder (default: DupliFoto-Quarantena next to the exe)
          --full-check                  before moving an identical file, compare it again in full with the
                                        copy to keep (slow on external drives). Without it, size, date,
                                        start and end of the file are checked again: the search has
                                        already compared them in full
          --keep <folder>               always keep the copies in this folder (repeatable)
          --across-folders              compare each folder only with the others: duplicates within
                                        the same folder are ignored (needs at least two folders)
          --report <file.html>          where to save the report (a .csv is written next to it)
          --model <file.onnx>           neural model to recognise burst shots (e.g. DINOv2)
          --accelerator auto|npu|gpu|cpu    where the model runs (default: auto = NPU, then GPU, then CPU)
          --burst <seconds>             maximum time between burst shots (default: 10)
          --no-bursts                   do not look for burst shots
          --no-subfolders  --hidden  --no-cache  --threads <n>
          --non-interactive             ask nothing: whatever needs a confirmation is left for review
          --language it|en|auto         language of messages and report (default: as Windows;
                                        also through the DUPLIFOTO_LANG variable)

          The Italian names work too (analizza, annulla, aiuto, --modo sola-lettura, --preferisci...).

        WORKING FILES
          Cache, log and reports (when started with a double-click) live in "DupliFoto-dati" next to the
          exe, and the quarantine in "DupliFoto-Quarantena", also next to the exe: nothing is left
          scattered around the PC. Started with a double-click, on closing it also
          removes the copy of itself unpacked into %TEMP%\.net; from a terminal it keeps it, so that the
          next command starts at once. "Pulisci DupliFoto.bat /yes", in DupliFoto-dati, removes everything
          (except the reports).

        EXAMPLES
          duplifoto-cli "D:\Photos"
          duplifoto-cli "D:\Photos" "E:\Phone backup" --mode semi-auto --keep "D:\Photos"
          duplifoto-cli "D:\Catalogued" "D:\To sort" --across-folders --keep "D:\Catalogued"
          duplifoto-cli "D:\Photos" --mode auto --threshold 98 --model dinov2-small.onnx --non-interactive
        """;
}
