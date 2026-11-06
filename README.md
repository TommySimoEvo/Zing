# Zing

Visualizzatore leggero di file Markdown per Windows, scritto in .NET 10 WinForms con WebView2 e Markdig. Si installa per singolo utente con un MSI di circa 40 MB, che include il runtime .NET.

## Funzioni

- **Markdown in stile GitHub**: tabelle, task list, note a piè di pagina, emoji. Il front matter YAML compare in un blocco chiuso, "Metadati". I file senza BOM vengono letti come UTF-8; se non sono UTF-8 valido, come Windows-1252.
- **`[[wikilink]]`** in stile Obsidian/workbrain, con le forme `[[x|alias]]` e `[[x#Titolo sezione]]`:
  - il target si cerca relativo alla cartella del file e poi a ogni cartella antenata, fino alla radice del progetto (`.git` o `.obsidian`); se non c'è, si cerca per nome dentro la radice;
  - l'ancora si confronta con il testo dei titoli, quindi funziona anche con accenti e numerazione;
  - `![[immagine.png]]` mostra l'immagine; `![[nota]]` diventa un link (l'inclusione del contenuto non è supportata);
  - un link non risolto appare in rosso tratteggiato;
  - per sicurezza non sono accettati percorsi assoluti, unità, UNC né `..`.
- **Link**:
  - i link relativi tra file `.md` e le immagini locali funzionano;
  - i link web si aprono nel browser predefinito, ma solo con un clic;
  - gli altri file locali si aprono con l'app associata solo con un clic, solo per i tipi ammessi (documenti Office, PDF, immagini, testo) e sempre dopo una conferma;
  - gli eseguibili e gli altri tipi non vengono mai aperti: Zing offre di mostrarli in Esplora file.
- **Immagini**: solo locali, `data:` e PlantUML. Le immagini remote (`https://…`) sono bloccate, perché un .md non deve poter far partire richieste verso server scelti da lui.
- **Mermaid** con libreria locale (`assets/mermaid.min.js` 11.17.2), quindi offline.
- **PlantUML** tramite il server pubblico `plantuml.com`, **su richiesta**: sotto ogni diagramma c'è il pulsante "Mostra diagramma", che invia il testo del diagramma al server. Con "usa sempre plantuml.com" la scelta viene ricordata (`"PlantUml": "always"` in `settings.json`; si torna indietro con `"ask"`).
- **Pannello ad albero nativo** a sinistra (`Ctrl+B`):
  - mostra cartelle e file Markdown e carica una cartella solo quando la apri;
  - la radice è il progetto del file aperto e si cambia con `…` o dal menu del tasto destro;
  - si aggiorna da solo quando i file cambiano.
- **Indice dei titoli** a destra (tasto `T`), tema automatico/chiaro/scuro (tasto `D`) e ricarica automatica quando il file cambia, mantenendo la posizione di scorrimento.
- **Scorciatoie**: `Ctrl+O` apre un file, `Ctrl+F` cerca, `Alt+←` torna indietro, `Ctrl+rotella` regola lo zoom, `Ctrl+P` stampa.
- **Cartelle di rete**: Zing legge solo dalla condivisione (`\\server\share`) del file aperto dall'utente, non da altre.

## Installazione su altri PC

Il file da distribuire è `artifacts\Zing-<versione>-x64.msi`, che si genera con `.\build-installer.ps1`. Usa WiX 5 come pacchetto NuGet, quindi non serve installare niente.

- **Installazione**: per singolo utente, senza permessi di amministratore. I file vanno in `%LOCALAPPDATA%\Programs\Zing`; viene creato il collegamento nel menu Start e Zing viene registrato per `.md` e `.markdown` in HKCU.
- **Prerequisiti**: solo il runtime WebView2, già presente su Windows 11. Il runtime .NET è incluso (publish self-contained, circa 115 MB su disco).
- **Programma predefinito**: Windows non permette a un programma di impostarsi da solo. L'utente sceglie *Apri con → Scegli un'altra app → Zing → Sempre*.
- **Aggiornamento**: una nuova versione sostituisce quella installata, anche se ha lo stesso numero di versione.
- **Disinstallazione**: da *Impostazioni → App*. Restano preferenze e cache in `%LOCALAPPDATA%\Zing`, da cancellare a mano se servisse.
- **Firma**: l'MSI non è firmato, quindi SmartScreen può mostrare un avviso.

In alternativa, senza MSI: `Zing.exe --register` / `--unregister` scrive o rimuove le stesse chiavi HKCU. Con l'MSI installato, `--unregister` rimanda a *Impostazioni → App*.

> **Nota per chi sviluppa da Claude desktop.** Claude desktop è un'app MSIX: i processi che lancia vedono una copia virtualizzata di HKCU, e in parte di AppData. Una registrazione fatta dalla sua shell non arriva al registro reale. Per verificare il registro reale si legge via WMI (`StdRegProv` su `HKEY_USERS\<SID>`); per installare davvero si usa l'MSI.

## Test end-to-end

```powershell
Zing.exe --selftest samples\test.md $env:TEMP\zt
Zing.exe --selftest "samples\security\ancore.md#Perché funziona" $env:TEMP\zt-ancora
```

Il comando produce tre file:
- `zt.json`: report con titolo, numero di blocchi Mermaid resi, PlantUML in attesa o caricati, wikilink risolti e mancanti, posizione di scorrimento, errori JS e stato dell'albero;
- `zt.png`: screenshot della pagina;
- `zt-window.png`: screenshot della finestra intera, oppure `zt-screenshot-error.txt` se lo schermo non è catturabile.

Con la variabile `ZING_PLANTUML=always`, valida solo in self-test, i diagrammi PlantUML vengono resi senza chiedere.

`samples/security/` contiene i casi di sicurezza:
- `meta.md` fa un meta refresh verso `marker.bat`, che non deve creare `%TEMP%\zing-pwned.txt`, e contiene anche un'immagine UNC, un'immagine remota e wikilink assoluti e con `..`;
- `ansi.md` è un file in Windows-1252;
- `ancore.md` serve a provare le ancore con accenti e numerazione.

## Decisioni

1. **WinForms + WebView2**, con il runtime .NET incluso nell'MSI (scelta di Tommaso, 06/11/2026): niente prerequisiti in cambio di un MSI da 40 MB invece di 1,5 MB.
2. **Finto host `https://zing.local/f/<percorso>`.** I file sono serviti da `WebResourceRequested`, con lettura e rendering in background. Così i link relativi e il tasto indietro funzionano senza codice aggiuntivo.
3. **Il .md non è fidato.**
   - Una CSP con nonce impedisce che gli script presenti nel .md vengano eseguiti.
   - Le immagini sono limitate a file locali e PlantUML.
   - Le uscite verso il sistema (shell, altri processi, rete) richiedono un clic, passano da un elenco di tipi ammessi e da una conferma: vedi `LinkPolicy.cs` e la review del 06/11/2026.
4. **Wikilink convertiti prima di Markdig.** La conversione salta i blocchi recintati e gli span di codice su una riga, non i blocchi indentati di 4 spazi: saltarli romperebbe i wikilink nelle liste annidate.
5. **Albero come controllo nativo** (`TreeView` in uno `SplitContainer`): lo stato resta tra un file e l'altro. Il tema dell'albero segue quello della pagina.
6. **Nome Zing** (decisione di Tommaso, 06/11/2026). L'icona è `zing.ico`. La cartella del repository resta `md-viewer`.
7. **PlantUML su richiesta per tutti** (decisione di Tommaso, 06/11/2026): il testo dei diagrammi esce dal PC solo dopo un clic o una scelta esplicita.
