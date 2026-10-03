using DupliFoto.Core;

namespace DupliFoto.Gui.Views;

/// <summary>
/// I testi fissi della finestra, nelle due lingue. Nel XAML si usano con <c>{l:T Chiave}</c> (vedi <see cref="TExtension"/>),
/// che si aggiorna da solo quando si cambia lingua. I testi che dipendono dallo stato stanno nel view model.
/// </summary>
public static class Strings
{
    public static string OpenInViewer => Lang.T("Apri con il visualizzatore di foto", "Open in the photo viewer");
    public static string OpenPhoto => Lang.T("Apri la foto", "Open the photo");
    public static string ShowInFolder => Lang.T("Mostra nella cartella", "Show in folder");
    public static string FoldersToScan => Lang.T("Cartelle da analizzare", "Folders to scan");
    public static string DragHint => Lang.T("puoi anche trascinarle qui da Esplora risorse", "you can also drag them here from File Explorer");
    public static string AddFolder => Lang.T("Aggiungi cartella", "Add folder");
    public static string NoFolders => Lang.T("Nessuna cartella: aggiungine una o trascinala qui.", "No folders yet: add one or drag it here.");
    public static string RemoveFromList => Lang.T("Togli dall'elenco", "Remove from the list");
    public static string RemoveFolderFromList => Lang.T("Togli la cartella dall'elenco", "Remove the folder from the list");
    public static string CopyToKeep => Lang.T("Copia da tenere", "Copy to keep");
    public static string AutomaticChoice => Lang.T("Scelta automatica", "Automatic choice");
    public static string AutomaticChoiceTip => Lang.T("Decide DupliFoto: risoluzione più alta, poi metadati più completi, nome senza «(1)»...", "DupliFoto decides: higher resolution, then richer metadata, a name without “(1)”...");
    public static string LookFor => Lang.T("Cerca i doppioni", "Look for duplicates");
    public static string InAllPhotos => Lang.T("in tutte le foto", "in all photos");
    public static string InAllPhotosTip => Lang.T("Anche le copie che stanno nella stessa cartella", "Copies in the same folder too");
    public static string AcrossFolders => Lang.T("solo tra cartelle diverse", "only across different folders");
    public static string AcrossFoldersTip => Lang.T("Ogni cartella viene confrontata solo con le altre: i doppioni dentro la stessa cartella restano dove sono", "Each folder is compared only with the others: duplicates within the same folder stay where they are");
    public static string Mode => Lang.T("Modalità", "Mode");
    public static string DuplicatesGoTo => Lang.T("I doppioni vanno in", "Duplicates go to");
    public static string AutoFrom => Lang.T("Sposta da sola da", "Moves on its own from");
    public static string PercentUp => Lang.T("% in su", "% up");
    public static string Subfolders => Lang.T("Sottocartelle", "Subfolders");
    public static string LookForBursts => Lang.T("Cerca scatti multipli", "Look for burst shots");
    public static string MoreOptions => Lang.T("Altre opzioni", "More options");
    public static string QuarantineFolder => Lang.T("Cartella di quarantena", "Quarantine folder");
    public static string Browse => Lang.T("Sfoglia…", "Browse…");
    public static string QuarantineHint => Lang.T("Da qui «Annulla spostamenti» rimette tutto a posto.", "From here, “Undo moves” puts everything back.");
    public static string BurstShots => Lang.T("Scatti multipli", "Burst shots");
    public static string AtMost => Lang.T("Al massimo", "At most");
    public static string SecondsBetween => Lang.T("secondi tra uno scatto e l'altro", "seconds between shots");
    public static string NeuralOptional => Lang.T("Rete neurale (facoltativa)", "Neural network (optional)");
    public static string NeuralHint => Lang.T("Un modello ONNX come DINOv2 riconosce meglio gli scatti multipli. Usa la NPU o la GPU, se ci sono.", "An ONNX model such as DINOv2 recognises burst shots better. It uses the NPU or the GPU when there is one.");
    public static string NoModel => Lang.T("nessun modello: solo algoritmi classici", "no model: classic algorithms only");
    public static string RunOn => Lang.T("Esegui su", "Run on");
    public static string WindowsOnly => Lang.T("Disponibile solo nella versione per Windows.", "Only available in the Windows version.");
    public static string DupliFotoFiles => Lang.T("File di DupliFoto", "DupliFoto's files");
    public static string DataFolderHint => Lang.T("Impostazioni, cache delle analisi, registro errori e report stanno nella cartella «DupliFoto-dati» accanto all'exe: sul PC non resta niente di sparso. «Pulisci DupliFoto.bat», lì dentro, toglie anche i file delle versioni precedenti.", "Settings, analysis cache, error log and reports live in the “DupliFoto-dati” folder next to the exe: nothing is left scattered around the PC. “Pulisci DupliFoto.bat” in there also removes the files of earlier versions.");
    public static string Open => Lang.T("Apri", "Open");
    public static string DataFolderFallback => Lang.T("Accanto all'exe non si può scrivere (disco protetto o cartella di sistema): per questo i file stanno qui, e i report in Documenti\\DupliFoto.", "The folder next to the exe cannot be written (protected disk or system folder): that is why the files are here, and the reports in Documents\\DupliFoto.");
    public static string RemoveTemp => Lang.T("Alla chiusura togli anche i file temporanei del programma", "On closing, also remove the program's temporary files");
    public static string RemoveTempHint => Lang.T("L'exe, a ogni avvio, si scompatta in %TEMP%\\.net. Togliendo quella copia alla chiusura sul PC non resta niente; il prossimo avvio però richiede qualche secondo in più.", "At every start the exe unpacks itself into %TEMP%\\.net. Removing that copy on closing leaves nothing on the PC; the next start takes a few seconds longer, though.");
    public static string StartSearch => Lang.T("Avvia ricerca", "Start search");
    public static string Stop => Lang.T("Interrompi", "Stop");
    public static string WelcomeTitle => Lang.T("Trova le foto doppie", "Find duplicate photos");
    public static string WelcomeText => Lang.T("Aggiungi le cartelle da controllare, scegli la modalità e premi «Avvia ricerca». In sola lettura nessun file viene toccato; negli altri casi i doppioni vanno in quarantena, da dove si possono sempre ripristinare.", "Add the folders to check, choose the mode and press “Start search”. In read-only mode no file is touched; otherwise duplicates go to quarantine, from where they can always be restored.");
    public static string Searching => Lang.T("Ricerca in corso", "Searching");
    public static string NoDuplicates => Lang.T("Nessun doppione", "No duplicates");
    public static string PickPair => Lang.T("Scegli una coppia dall'elenco qui sotto.", "Choose a pair from the list below.");
    public static string Confidence => Lang.T("Affidabilità", "Confidence");
    public static string SwapTip => Lang.T("Tieni la foto a destra invece di quella a sinistra", "Keep the right-hand photo instead of the left-hand one");
    public static string Swap => Lang.T("Scambia", "Swap");
    public static string PreviousPair => Lang.T("Coppia precedente", "Previous pair");
    public static string NextPair => Lang.T("Coppia successiva", "Next pair");
    public static string MoveRight => Lang.T("Sposta quella a destra", "Move the right-hand one");
    public static string KeepBoth => Lang.T("Tieni entrambe", "Keep both");
    public static string PhotosAnalysed => Lang.T("foto analizzate", "photos analysed");
    public static string Groups => Lang.T("gruppi", "groups");
    public static string Duplicates => Lang.T("doppioni", "duplicates");
    public static string Reclaimable => Lang.T("recuperabili", "to reclaim");
    public static string ToDecide => Lang.T("da decidere", "to decide");
    public static string Moved => Lang.T("spostati", "moved");
    public static string PairsFound => Lang.T("Coppie trovate", "Pairs found");
    public static string UndoTip => Lang.T("Riporta al loro posto i file spostati in questa sessione", "Puts back the files moved in this session");
    public static string UndoMoves => Lang.T("Annulla spostamenti", "Undo moves");
    public static string ReportTip => Lang.T("Report HTML con anteprime e CSV per Excel", "HTML report with thumbnails, and CSV for Excel");
    public static string Report => Lang.T("Report", "Report");
    public static string Show => Lang.T("Mostra", "Show");
    public static string Status => Lang.T("Stato", "Status");
    public static string ToKeep => Lang.T("Da tenere", "To keep");
    public static string ToMove => Lang.T("Da spostare", "To move");
    public static string Kind => Lang.T("Tipo", "Kind");
    public static string Cancel => Lang.T("Annulla", "Cancel");
    public static string RootTip => Lang.T("La cartella aggiunta da cui viene questa foto", "The added folder this photo comes from");
    public static string Language => Lang.T("Lingua · Language", "Language · Lingua");
    public static string Engines => Lang.T("Motori di calcolo", "Compute engines");
}
