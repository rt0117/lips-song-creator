# Lips Song Creator - Projektanalyse

## Projektziel

Eigene Karaoke-Songs fuer das Xbox 360 Spiel **Lips** erstellen und auf einer gemoddeten Xbox (RGH/JTAG) abspielen. Idealerweise mit automatischem Import von UltraStar-Songs.

## Technologie-Stack

- **Sprache:** C# / .NET 9
- **UI:** Blazor Server (Dark Theme, Piano Roll Canvas)
- **Tests:** xUnit (172 Tests, alle gruen)
- **Zielplattform:** Xbox 360 (Big-Endian, PowerPC)
- **Xbox-Mod:** RGH/JTAG mit Aurora Dashboard
- **Spiel-Version:** "Lips: Party Classics" (Disc Index 13, TitleID 0x4D530888)

## Projekt-Struktur

```
lips-song-creator/
  LipsSongExtractor/           # Kernbibliothek + CLI-Tool
    Poco/                      # XML-Datenmodelle (Ixb, ClassDef, MemberDef)
    LipsObjects/               # Lips-spezifische Modelle
    X360Reader.cs              # .X360 Dateien lesen (XML-Header + Binary Blob)
    X360Writer.cs              # .X360 Dateien schreiben (Roundtrip-faehig)
    FieldSizes.cs              # Big-Endian Typkonvertierung
    BlobAnalyzer.cs            # Blob-Struktur analysieren
    IxbDeserializer.cs         # IXB Binary Blob -> typisierte Objekte
    IxbSerializer.cs           # Objekt-Felder in bestehendem Blob modifizieren
    IxbBlobBuilder.cs          # IXB Blob from scratch aufbauen
    UltraStarParser.cs         # UltraStar .txt Dateien parsen
    UltraStarToLipsConverter.cs # UltraStar -> Lips .X360 Chart konvertieren
    LipsSongPackageBuilder.cs  # Komplettes Song-Paket erzeugen (DLC.xml, Lyric.X360, Chart)
    StfsReader.cs              # Xbox STFS/LIVE Container lesen und extrahieren
    StfsWriter.cs              # STFS/LIVE Container erzeugen
    Program.cs                 # CLI mit Kommandos
  LipsSongExtractor.Tests/     # 172 Unit Tests
  LipsSongCreator.Web/         # Blazor Web UI
    Components/Pages/Home.razor # Piano Roll Editor + Upload
    Services/SongService.cs    # Backend-Service fuer UI
    wwwroot/js/pianoRoll.js    # Canvas-basierter Noten-Renderer
    wwwroot/app.css            # Dark Theme (Xbox/Lips-Stil)
  Example/                     # Referenz-Dateien
    California Love/           # Extrahierter Song vom Spiel-Disc (alle 8 Dateien)
    DLC/                       # Original-DLC-Pakete (STFS LIVE-Container, ~330 Stueck)
    Lips.zip                   # Komplettes Spiel (22.5 GB, nicht im Git)
```

## Was komplett funktioniert

### IXB-Format (.X360) - Vollstaendig reverse-engineered

Das proprietaere iNiS XML Binary Format wurde komplett entschluesselt:

**Dateistruktur:**
```
[XML-Header mit Klassen-Definitionen]
<Objects>[Binary Blob]</Objects></ixb>
```

**Binary Blob** enthaelt serialisierte Objekte als Eintraege:
```
[runtime_ptr:4][size:4][data:size bytes]
```

- Inline-Daten (Strings, Arrays) und Objekte liegen gemischt im Blob
- `runtime_ptr` ist die originale Speicheradresse; wird via Pointer-Lookup-Tabelle aufgeloest
- Vererbungshierarchien werden korrekt aufgeloest
- Inline-Structs (z.B. `Tone` mit `fIdx` + `octave`) werden erkannt
- Roundtrip-Test: Lesen -> Schreiben -> Byte-identisch (bewiesen)

**Bekannte Klassen:**

| Klasse | Size | Beschreibung |
|--------|------|-------------|
| lpsChart | 184 | Haupt-Container: Sequenzen, Metadaten, Offsets |
| ixSequence | 104 | Sequenz-Container mit SeqCode-Liste |
| lpsMelodyMarker | 40 | Tonhoehe (Tone: fIdx + octave) + Timing |
| lpsLyricMarker | 64 | Silbe (m_strFreeWord) + Wortende-Flag |
| lpsHitMarker | 48 | Hit-Marker mit Tonhoehe |
| ixSeqTempoCode | 44 | BPM, Taktart, berechnete Position |
| ixSeqNameTag | 36 | Benannter Marker (z.B. "PV Start", "Stop") |
| Tone | 8 | Inline-Struct: fIdx (float, 0-11) + octave (int) |
| ixRawFileImage | 84 | Datei-Container (z.B. Lyric-Text) |

**Song-Sequenz-Struktur (15 + 6 Sequenzen pro Song):**
Time, Conductor, Audio, Lyric, Melody, Lyric_Duet, Melody_Duet, Lyric_Duet_P2, Melody_Duet_P2, Section, Group, CallAndResponse, Movie, AudioEffect, Led + 6 Extra-Sequenzen (TimedGesture, Noisemaker etc.)

### UltraStar Parser + Konverter - Funktioniert

- Parst alle Header-Tags, Notentypen (Normal, Golden, Freestyle, Rap), Duett
- Konvertiert Timing (Beat -> Sekunden), Pitch (MIDI -> Lips Tone)
- Generiert komplette .X360 Chart-Dateien und Lyric.X360

### Song-Paket-Builder - Funktioniert

Erzeugt alle generierbaren Dateien fuer ein DLC:
- `Song.X360` (Chart mit Noten, Lyrics, Timing)
- `Song_Lyric.X360` (Liedtext als ixRawFileImage)
- `DLC.xml` (Song-Discovery-Mechanismus)

### STFS Reader - Funktioniert

Liest Xbox 360 STFS/LIVE Container korrekt:
- Header-Parsing (Magic, ContentType, TitleID, DisplayName, Licensing)
- File-Table mit Block-Chains
- Datei-Extraktion (verifiziert gegen Original-DLCs)

### Web UI - Funktioniert

Dark Theme Piano Roll Editor mit Upload, Metadaten-Editor, Sequenz-Browser, Noten-Visualisierung, Export.

## BLOCKER: STFS-Paket wird von Lips nicht als DLC erkannt

### Symptom

Unsere generierten STFS-Pakete werden von **Aurora** (Xbox Dashboard Mod) korrekt erkannt - der Titel wird aus der DLC.xml gelesen und angezeigt. Aber **Lips selbst** listet den Song nicht in der Song-Auswahl auf.

Bei einem fruehen Test stieg die Song-Anzahl von 550 auf 552 (also +2), aber California Love erschien nicht doppelt - vermutlich wurde es mit der Disc-Version gemerged.

### Umgebung

- Xbox 360 mit RGH/JTAG Mod
- Aurora Dashboard
- Spiel: "Lips: Party Classics" (TitleID 0x4D530888)
- ~330 Original-DLCs sind installiert und funktionieren (wurden vom Xbox Live Marketplace heruntergeladen und per FTP auf die Xbox kopiert)
- DLC-Pfad auf der Xbox: `\xbox360\System\hdd1\content\00...\4D530888\00000002\`

### Was alles getestet wurde (chronologisch)

#### Versuch 1: STFS from scratch (CreatePackage)
- STFS-Paket komplett selbst generiert
- **Ergebnis:** Nicht erkannt
- **Fixes:** Licensing-Daten (FFFFFFFFFFFFFFFF00000001), CertType (0xC684), TitleName "Lips", TransferFlags 0xC0, PNG-Thumbnails, DisplayName in 9 Sprachslots
- **Ergebnis nach Fixes:** Immer noch nicht erkannt

#### Versuch 2: Template-basiert (CreateFromTemplate)
- Original Captain Jack DLC als Template: Header byte-fuer-byte kopiert, nur Dateien ausgetauscht
- Block-Layout-Bug gefixed (interleaved Hash-Tables statt sequentiell)
- Roundtrip-Test: Captain Jack extrahieren -> repacken -> extrahieren -> **alle 12 Dateien byte-identisch**
- **Ergebnis:** Nicht erkannt

#### Versuch 3: DLC.xml Fixes
- DLC.xml wird immer als erste Datei in der File-Table geschrieben (wie bei Original-DLCs)
- Eindeutige offerID mit Zeitstempel (keine Kollision mit Disc-Version)
- offerID-Format auf 8 Hex-Zeichen korrigiert (war 7, Original hat 8)
- ChartContentID auf 16 Hex-Zeichen korrigiert (TitleID 8 + offerID 8)
- **Ergebnis:** Nicht erkannt

#### Versuch 4: ContentID als Dateiname
- Entdeckt: Original-DLC Dateinamen sind `ContentID(40 hex) + "4D"`
- ContentID (20 Bytes bei STFS Offset 0x32C) wird als Dateiname verwendet
- STFS-Writer generiert jetzt zufaellige ContentID und benennt die Ausgabedatei korrekt
- **Ergebnis:** Nicht erkannt

#### Entscheidender Test: Original-DLC Kopie
- **Test A:** Exakte Kopie des funktionierenden From Yesterday DLC unter anderem Dateinamen -> **NICHT erkannt**
- **Test B:** From Yesterday DLC mit 1 Byte Aenderung (DisplayName "Frum" statt "From"), gleicher Dateiname -> **NICHT erkannt**
- **Test C:** Unser generiertes DLC -> **NICHT erkannt**

### Analyse: Was bedeuten die Test-Ergebnisse?

**Test A ist der Schluessel:** Wenn eine exakte, unmodifizierte Kopie eines funktionierenden DLCs unter einem anderen Dateinamen NICHT erkannt wird, dann:

1. **Der Dateiname IST relevant** - Lips identifiziert DLCs anhand des Dateinamens (ContentID + "4D")
2. **ODER: Lips cached bekannte DLCs** und laedt keine neuen nach dem ersten Scan
3. **ODER: Es gibt ein Registrierungssystem** jenseits des einfachen Ordner-Scannens

**Test B zeigt:** Selbst eine minimale Aenderung am STFS-Header macht ein DLC unbrauchbar. Das deutet auf **Hash-/Signatur-Validierung** hin - auch auf RGH/JTAG Konsolen.

### DURCHBRUCH: ContentID ist der SHA1-Hash des Headers

Analyse der Original-DLCs (verifiziert an 11 von 11 DLC-Paketen aus der 330er-Sammlung):

```
ContentID (Offset 0x32C, 20 Bytes) == SHA1(header[0x344 .. 0xB000])
Dateiname == ContentID als Hex + "4D"
```

Die ContentID ist also **kein zufaelliger Wert**, sondern der SHA1-Hash des
kompletten Metadata-Headers (ab ContentType 0x344 bis zum Block-alignten
Header-Ende 0xB000). Die XContent-API der Xbox validiert diesen Hash beim
Content-Scan - **auch auf RGH/JTAG-Konsolen**.

Das erklaert ALLE bisherigen Testergebnisse:

- **Test A** (Kopie unter anderem Namen): Dateiname passt nicht mehr zum
  Header-Hash -> nicht erkannt
- **Test B** (1 Byte im DisplayName geaendert): Header-Hash aendert sich,
  ContentID im Header und Dateiname passen nicht mehr -> nicht erkannt
- **Test C** (unser DLC): ContentID war zufaellig generiert statt
  SHA1(Header) -> nicht erkannt

**Fix implementiert:** `StfsWriter` berechnet die ContentID jetzt als
letzten Schritt nach dem finalen Header-Aufbau (`WriteContentIdHash`).
Der Dateiname wird daraus abgeleitet. Gilt fuer `CreatePackage` und
`CreateFromTemplate`. Regression-Tests pruefen die Formel gegen
Original-DLCs.

**Status:** Auf der Xbox noch nicht verifiziert (naechster Hardware-Test).

### Moegliche Ursachen (noch nicht geprueft) - VERALTET, siehe Durchbruch oben

1. **STFS-Signatur wird doch geprueft:** Obwohl RGH/JTAG die RSA-Signatur beim Booten nicht prueft, koennte Lips (das Spiel selbst) die Signatur validieren. Die Original-DLCs haben eine gueltige Microsoft-Signatur im Header (Offset 0x000C-0x0103). Unsere Pakete haben dort Nullen.

2. **DLC-Cache/Registry:** Lips koennte beim ersten Start eine Liste installierter DLCs in einem Savegame oder Cache speichern und diese nicht automatisch aktualisieren. Das `lpssavedata`-Datei im Content-Ordner koennte relevant sein.

3. **Content License Verification:** Die Xbox Kernel-API (`XContentCreateEnumerator`, `XContentCreate`) koennte die Lizenz-Signatur pruefen bevor das Spiel die Datei ueberhaupt sieht. Selbst auf RGH koennte das Dashboard (Aurora) die Content-Enumeration anders handhaben als die Standard-Xbox-API.

4. **Falscher Content-Ordner-Pfad:** Der User-Pfad ist `\xbox360\System\hdd1\content\00...\4D530888\00000002\`. Die `00...` (Profile-ID) muss moeglicherweise `0000000000000000` sein oder zum aktiven Profil passen.

5. **Title Update/Compatibility:** Lips koennte ein Title Update benoetigen das die DLC-Scanning-Logik aendert. Oder die "Party Classics"-Version hat eine andere DLC-Discovery als das Original-Lips.

### Empfohlene naechste Schritte

1. **Pruefen ob DLCs ueberhaupt nachtraeglich hinzugefuegt werden koennen:** Einen noch nie installierten Original-DLC (aus der 330er-Sammlung) zum ersten Mal manuell in den Content-Ordner kopieren. Wenn auch der nicht erkannt wird, liegt das Problem nicht an unserem Code sondern an der Xbox-Konfiguration (Cache, Profil, Pfad).

2. **Savegame/Cache loeschen:** Die `lpssavedata`-Datei im Content-Ordner koennte ein DLC-Cache sein. Loeschen und Spiel neu starten.

3. **Profil-Ordner pruefen:** Statt `0000000000000000` den tatsaechlichen Profil-Ordner verwenden (Aurora zeigt die Profile-ID an).

4. **STFS-Signatur "faken":** Tools wie `wxPirs` oder das `Le Fluffie` STFS Tool koennen Pakete mit gueltigen Hashes/Signaturen erstellen die auch ohne Marketplace-Signatur funktionieren.

5. **XContent API untersuchen:** Auf RGH-Konsolen kann man XEX-Patches anwenden die die Content-License-Pruefung deaktivieren.

## Technische Details fuer weiteres Debugging

### Vergleich: Funktionierendes DLC vs. Unser DLC

Byte-fuer-Byte Vergleich des STFS-Headers (alle Felder bei Offset < 0x1720):

| Offset | Feld | Funktionierendes DLC | Unser DLC | Status |
|--------|------|---------------------|-----------|--------|
| 0x0000 | Magic | `LIVE` | `LIVE` | OK |
| 0x0004 | CertType | variiert (0x0318, 0xC684, 0xA64B) | 0xC684 | Variabel |
| 0x000C | RSA-Signatur | 256 Bytes Signaturdaten | 256 Bytes Nullen | **VERDAECHTIG** |
| 0x022C | Licensing[0] | `FFFFFFFFFFFFFFFF00000001` | identisch | OK |
| 0x032C | ContentID | SHA1(header[0x344..0xB000]) | jetzt identisch berechnet | **GEFIXT** |
| 0x0340 | HeaderSize | `0x0000AD0E` | identisch | OK |
| 0x0344 | ContentType | `0x00000002` | identisch | OK |
| 0x0360 | TitleID | `0x4D530888` | identisch | OK |
| 0x037B | BlockSep | `0x01` | identisch | OK |
| 0x0381 | TopHash | SHA1 der Hash-Table | berechnet | OK |
| 0x1691 | TitleName | "Lips" | identisch | OK |
| 0x1711 | TransferFlags | `0xC0` | identisch | OK |
| 0x1712 | ThumbImgSize | variiert (3000-5000) | 210 (minimal PNG) | Kleiner |
| 0x171A | Thumbnail | echtes PNG Cover | 64x64 Platzhalter | Anders |

### DLC.xml Vergleich

**Funktionierendes Original (From Yesterday):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<DLCContents>
  <MusicIndices>
    <MusicIndex>
      <Artist>30 Seconds To Mars</Artist>
      <Title>From Yesterday</Title>
      <Genre>Rock</Genre>
      <Year>2005</Year>
      <Language>EN</Language>
      <Album>A Beautiful Lie</Album>
      <Length>251</Length>
      <Rating>0</Rating>
      <LeaderBoardID>459</LeaderBoardID>
      <ChartUri>From Yesterday.X360</ChartUri>
      <AudioUri>From Yesterday.xWMA</AudioUri>
      <LyricUri>From Yesterday_Lyric.X360</LyricUri>
      <AlbumJacketUri>From Yesterday.jpg</AlbumJacketUri>
      <PreviewAudioUri>From Yesterday_prv.xWMA</PreviewAudioUri>
      <offerID>CCF01B2</offerID>
      <UintID>0x0CCF01B2</UintID>
      <ChartContentID>4D5308880CCF01B2</ChartContentID>
      <VideoContentID>4D5308880CCF01B2</VideoContentID>
      <PreviewLyric>"From yesterday it's coming..."</PreviewLyric>
    </MusicIndex>
  </MusicIndices>
  <MusicVideos>
    <MusicVideo>
      <Artist>30 Seconds To Mars</Artist>
      <Title>From Yesterday</Title>
      <Genre>Rock</Genre>
      <Year>2005</Year>
      <Album>A Beautiful Lie</Album>
      <VideoUri>From Yesterday.wmv</VideoUri>
      <PreviewAudioUri>From Yesterday_prv.xWMA</PreviewAudioUri>
      <PreviewVideoUri>From Yesterday_prv.wmv</PreviewVideoUri>
      <PreviewIconUri>From Yesterday_prv.nft</PreviewIconUri>
      <VideoContentID>4D5308880CCF01B2</VideoContentID>
      <ChartID>4D5308880CCF01B2_00</ChartID>
    </MusicVideo>
  </MusicVideos>
  <LicenseBits ValidBits="3">0x7</LicenseBits>
</DLCContents>
```

**Unsere generierte DLC.xml:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<DLCContents>
  <MusicIndices>
    <MusicIndex>
      <Artist>Unknown Artist</Artist>
      <Title>California Love</Title>
      <Genre>Pop</Genre>
      <Year>2024</Year>
      <Language>EN</Language>
      <Album></Album>
      <Length>200</Length>
      <Rating>0</Rating>
      <LeaderBoardID>9999</LeaderBoardID>
      <ChartUri>California Love.X360</ChartUri>
      <AudioUri>California Love.xWMA</AudioUri>
      <LyricUri>California Love_Lyric.X360</LyricUri>
      <AlbumJacketUri>California Love.jpg</AlbumJacketUri>
      <PreviewAudioUri>California Love_prv.xWMA</PreviewAudioUri>
      <offerID>A1B2C3D4</offerID>
      <UintID>0xA1B2C3D4</UintID>
      <ChartContentID>4D530888A1B2C3D4</ChartContentID>
      <VideoContentID>4D530888A1B2C3D4</VideoContentID>
      <PreviewLyric>""</PreviewLyric>
    </MusicIndex>
  </MusicIndices>
  <MusicVideos />
  <LicenseBits ValidBits="3">0x7</LicenseBits>
</DLCContents>
```

### STFS Block-Layout

Unser Writer verwendet jetzt das korrekte interleaved Hash-Table Layout:
```
[L0[0]] [Data 0-169] [L1] [L0[1]] [Data 170-339] [L0[2]] ...
```
Dies wurde verifiziert: Captain Jack DLC (110 MB, ~27000 Bloecke, 12 Dateien) wird nach Extract->Repack->Extract byte-identisch zurueckgelesen.

### Spiel-interne Datenbanken

Das Spiel verwendet SQLite-Datenbanken:
- `GameContentDB` (24 KB) - System-Registry (AudioEffects, Noisemakers, UIThemes etc.)
- `MusicDb2.db3` (233 KB) - Song-Datenbank mit 235 Disc-Songs
- `MusicDb2_4D530888_1..8.db3` - Disc-spezifische Song-Listen
- Die `DLCData`-Tabelle in MusicDb2 ist **leer** - DLCs werden NICHT in die DB eingetragen
- Disc-Songs verwenden `.ixb`/`.WMA` Extensionen in StageData-URIs (nicht `.X360`/`.xWMA`)

### Verfuegbare Referenz-Dateien

| Datei | Beschreibung |
|-------|-------------|
| `Example/California Love/` | Komplett extrahierter Disc-Song (8 Dateien) |
| `Example/DLC/4D530888.zip` | 330 Original-DLCs als ZIP (26 GB) |
| `Example/DLC/*.tmp` | Einzelne extrahierte DLCs zum Vergleich |
| `Example/Lips.zip` | Komplettes Spiel (22.5 GB) |

## Implementierungsstand

### 172 Tests (alle gruen)

| Komponente | Tests | Beschreibung |
|-----------|-------|-------------|
| FieldSizes | 20 | Typgroessen, Big-Endian, ReadCString, ConvertByType |
| X360Reader | 17 | Header, Klassen, Vererbung, Groessen |
| BlobAnalyzer | 6 | RU32/RI32/RF32, String-Suche |
| IxbDeserializer | 44 | Pointer-Lookup, Objekte, Vektoren, Tone, Disambiguierung |
| X360Writer | 7 | Roundtrip byte-identisch, Modifikation |
| IxbSerializer | 12 | SetField, SetString, Roundtrip |
| UltraStarParser | 25 | Header, Noten, Timing, MIDI, Duett, Edge Cases |
| UltraStarToLips | 7 | Konverter Roundtrip, Chart, Sequenzen, Timing |
| StfsReader | 9 | LIVE-Container, Extraktion, Dateien |
| StfsWriter | 7 | Header, Block-Layout, Roundtrip Writer->Reader |
| PackageBuilder | 11 | DLC.xml, Lyric.X360, Komplett-Paket |
| Sonstige | 7 | - |

### CLI-Kommandos

```
dotnet run -- classes      <Pfad.X360>              Klassen auflisten
dotnet run -- dump         <Pfad.X360> <Klasse>     Objekt ausgeben
dotnet run -- info         <Pfad.X360>              Song-Metadaten
dotnet run -- chart        <Pfad.X360>              Chart analysieren
dotnet run -- export-json  <Pfad.X360> [out.json]   JSON-Export
dotnet run -- analyze      <Pfad.X360>              Blob-Struktur
dotnet run -- hexdump      <Pfad.X360> [off] [len]  Hex-Dump
dotnet run -- stfs-list    <Paket>                   STFS-Dateien listen
dotnet run -- stfs-extract <Paket> <Ordner>          STFS extrahieren
dotnet run -- stfs-repack  <Template> <Ordner> <Out> STFS mit Template-Header repacken
dotnet run -- create-test-dlc <Song-Ordner> <Ausgabe> DLC erzeugen (korrekter Dateiname)
dotnet run -- convert-ultrastar <TXT> <Ordner>       UltraStar -> Lips konvertieren
dotnet run -- dump-db      <SQLite-DB>               SQLite-Datenbank auslesen
```

## Commit-Historie

```
17d1f07 Fix STFS ContentID and filename: Lips requires filename = ContentID + 4D
90a2c85 Fix offerID format: must be 8 hex chars, not 7
979dd8e Simplify create-test-dlc: remove template parameter
9905d08 Fix STFS file ordering: DLC.xml must be first entry in file table
e39297d Add dump-db command and ignore large game files
4c0ee95 Fix DLC identity collision: always generate unique offerID
bb9689c Fix create-test-dlc: use template-based STFS packaging
ce305e5 Fix STFS writer: correct interleaved hash table block layout
(fruehere Commits: IXB reader/writer, UltraStar parser, Web UI, etc.)
```
