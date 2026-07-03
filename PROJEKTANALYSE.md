# Lips Song Creator - Projektanalyse

## Projektziel

Eigene Karaoke-Songs fuer das Xbox 360 Spiel **Lips** erstellen und auf einer gemoddeten Xbox (RGH/JTAG) abspielen. Idealerweise mit automatischem Import von UltraStar-Songs.

## Technologie-Stack

- **Sprache:** C# / .NET 9
- **UI:** Blazor Server (Dark Theme, Piano Roll Canvas)
- **Tests:** xUnit (171 Tests)
- **Zielplattform:** Xbox 360 (Big-Endian, PowerPC)

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
    Program.cs                 # CLI mit Kommandos (classes, dump, chart, stfs-list, etc.)
  LipsSongExtractor.Tests/     # 171 Unit Tests
  LipsSongCreator.Web/         # Blazor Web UI
    Components/Pages/Home.razor # Piano Roll Editor + Upload
    Services/SongService.cs    # Backend-Service fuer UI
    wwwroot/js/pianoRoll.js    # Canvas-basierter Noten-Renderer
    wwwroot/app.css            # Dark Theme (Xbox/Lips-Stil)
  Example/                     # Referenz-Dateien
    California Love/           # Extrahierter Song vom Spiel-Disc
    DLC/                       # Original-DLC-Pakete zum Vergleich
```

## Reverse Engineering Erkenntnisse

### IXB-Format (.X360 Dateien)

Das proprieatere iNiS XML Binary Format wurde vollstaendig reverse-engineered:

**Dateistruktur:**
```
[XML-Header mit Klassen-Definitionen]
<Objects>
  [Binary Blob]
</Objects>
</ixb>
```

**XML-Header** definiert ein Klassenschema mit:
- Klassen-Namen, Groessen, Vererbung (Base-Index)
- Member-Definitionen mit Offset und optionalem Typ
- Vererbungsketten (z.B. lpsChart -> ixChart -> ixAgentPrototype -> ixPrototype -> ixAsset)

**Binary Blob** enthaelt serialisierte Objekte als Eintraege:
```
[runtime_ptr:4][size:4][data:size bytes]
```

- Inline-Daten (Strings, Arrays) und Objekte liegen gemischt im Blob
- `runtime_ptr` ist die originale Speicheradresse aus der Serialisierung
- ixVector<char>-Felder in Objekten enthalten `_data`-Pointer die auf die Inline-String-Eintraege verweisen
- Pointer-Lookup-Tabelle: `runtime_ptr -> blob_offset` ermoeglicht die Aufloesung

**Bekannte Klassen und ihre Groessen:**

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
| ixAssetPackage | 92 | Asset-Paket-Container |

**Song-Sequenz-Struktur (California Love, 15 + 6 Sequenzen):**

| # | Name | Inhalt |
|---|------|--------|
| 0 | Time | Tempo-Map (ixSeqNameTag: "Beat 8", "PV Start", "Stop") |
| 1 | Conductor | Song-Sections |
| 2 | Audio | Audio-Marker |
| 3 | Lyric | Lyrics Solo (lpsLyricMarker mit Silben) |
| 4 | Melody | Noten Solo (lpsMelodyMarker mit Tone) |
| 5 | Lyric_Duet | Lyrics Duett |
| 6 | Melody_Duet | Noten Duett |
| 7 | Lyric_Duet_P2 | Lyrics Spieler 2 |
| 8 | Melody_Duet_P2 | Noten Spieler 2 |
| 9 | Section | Abschnitte |
| 10 | Group | Gruppen |
| 11 | CallAndResponse | Call & Response |
| 12 | Movie | Video-Sync |
| 13 | AudioEffect | Audio-Effekte |
| 14 | Led | LED-Sequenzen |
| Extra | TimedGesture, Noisemaker_Mic, etc. | 6 weitere |

### Song-Paket-Struktur (DLC)

Ein Lips-DLC besteht aus folgenden Dateien:

| Datei | Format | Beschreibung |
|-------|--------|-------------|
| `Song.X360` | IXB | Chart (Noten, Lyrics, Timing, Sequenzen) |
| `Song_Lyric.X360` | IXB | Liedtext als ixRawFileImage (UTF-8 BOM + Plaintext) |
| `Song.xWMA` | xWMA | Audio (Xbox WMA-Format) |
| `Song.jpg` | JPEG | Album-Cover |
| `Song_prv.xWMA` | xWMA | Audio-Vorschau |
| `Song_prv.wmv` | WMV | Video-Vorschau |
| `Song_prv.nft` | NUT | Icon-Textur |
| `DLC.xml` | XML | Song-Index fuer Discovery |

**DLC.xml** ist der Song-Discovery-Mechanismus:
```xml
<DLCContents>
  <MusicIndices>
    <MusicIndex>
      <Artist>...</Artist>
      <Title>...</Title>
      <ChartUri>Song.X360</ChartUri>
      <AudioUri>Song.xWMA</AudioUri>
      <LyricUri>Song_Lyric.X360</LyricUri>
      <AlbumJacketUri>Song.jpg</AlbumJacketUri>
      <offerID>CCF01B2</offerID>
      <UintID>0x0CCF01B2</UintID>
      <ChartContentID>4D5308880CCF01B2</ChartContentID>
      ...
    </MusicIndex>
  </MusicIndices>
  <LicenseBits ValidBits="3">0x7</LicenseBits>
</DLCContents>
```

### STFS-Container (LIVE-Pakete)

DLCs werden als STFS-Container ausgeliefert (Magic "LIVE"):

**Header-Layout (0xAD0E Bytes):**

| Offset | Feld | Wert (Lips-DLCs) |
|--------|------|-------------------|
| 0x0000 | Magic | "LIVE" |
| 0x0004 | Certificate Type | 0xC684 |
| 0x022C | Licensing[0] | `FFFFFFFFFFFFFFFF00000001` (unlocked for all) |
| 0x0340 | Header Size | 0xAD0E |
| 0x0344 | Content Type | 0x00000002 (Marketplace Content) |
| 0x0360 | Title ID | 0x4D530888 (Lips) |
| 0x037B | Block Separation | 1 (read-only layout) |
| 0x0411 | Display Name | Song-Titel (UTF-16 BE, 9 Sprachslots) |
| 0x0D11 | Description | Beschreibung (UTF-16 BE, 9 Slots) |
| 0x1691 | **Title Name** | **"Lips"** (kritisch fuer Spiel-Zuordnung) |
| 0x1711 | Transfer Flags | 0xC0 |
| 0x1712 | Thumbnail Size | PNG-Bildgroesse |
| 0x171A | Thumbnail Image | PNG-Daten (max 0x4000 Bytes) |
| 0x571A | Title Thumbnail | PNG-Daten (max 0x4000 Bytes) |

**Block-Layout:**
- Daten-Bloecke a 0x1000 Bytes
- Nach jeweils 170 Daten-Bloecken: 1 Hash-Table-Block (SHA1 pro Block)
- File-Table bei Block 0 (0x40 Bytes pro Datei-Eintrag)

**Pfad auf der Xbox:**
```
Content/0000000000000000/4D530888/00000002/{Paketname}
```

### UltraStar-Format

Vollstaendig implementierter Parser fuer UltraStar TXT:

- Header-Tags: `#TITLE`, `#ARTIST`, `#BPM` (4x real), `#GAP` (ms)
- Noten: `Type StartBeat Length Pitch Text`
  - `:` Normal, `*` Golden, `F` Freestyle, `R` Rap
- Pitch 0 = C4 = MIDI 60
- Beat zu Sekunden: `GAP/1000 + beat * 60 / BPM`
- Phrasen-Trenner: `- StartBeat`
- Duett: `P1`/`P2` Delimiter

**Mapping UltraStar -> Lips:**

| UltraStar | Lips |
|-----------|------|
| Beat-Position | `m_fTriggerTiming` (Sekunden) |
| Beat-Laenge | `m_fLength` (Sekunden) |
| Pitch (MIDI-relativ) | `Tone.fIdx` (0-11) + `Tone.octave` |
| Silbentext | `m_strFreeWord` |
| Note-Type `:` | `lpsMelodyMarker` |
| Note-Type `*` | Golden Marker |
| Note-Type `F` | Freestyle Marker |
| Phrase-Break `-` | `lpsPhraseMarker` |

## Implementierungsstand

### Fertig (171 Tests)

| Komponente | Dateien | Tests | Beschreibung |
|-----------|---------|-------|-------------|
| X360Reader | 2 | 17 | .X360 lesen, XML-Header parsen, Vererbung aufloesen |
| FieldSizes | 1 | 20 | Big-Endian Konvertierung, Typ-Erkennung |
| BlobAnalyzer | 1 | 6 | Blob-Struktur analysieren |
| IxbDeserializer | 1 | 44 | Pointer-Lookup, Objekt-Aufloesung, Inline-Structs |
| X360Writer | 1 | 7 | Roundtrip (Byte-identisch lesen/schreiben) |
| IxbSerializer | 1 | 12 | Felder/Strings in bestehendem Blob modifizieren |
| UltraStar Parser | 1 | 25 | Header, Noten, Timing, MIDI, Duett, Edge Cases |
| UltraStar->Lips | 2 | 7 | Chart-Generierung, Blob from scratch |
| Song Package | 1 | 11 | DLC.xml, Lyric.X360, ZIP-Export |
| STFS Reader | 1 | 9 | LIVE-Container lesen, Dateien extrahieren |
| STFS Writer | 1 | 6 | LIVE-Container erzeugen (Roundtrip-validiert) |
| Web UI | 6 | - | Piano Roll, Metadaten-Editor, Export |

### CLI-Kommandos

```
dotnet run -- classes    <Pfad.X360>              Klassen auflisten
dotnet run -- dump       <Pfad.X360> <Klasse>     Objekt ausgeben
dotnet run -- info       <Pfad.X360>              Song-Metadaten
dotnet run -- chart      <Pfad.X360>              Chart analysieren
dotnet run -- export-json <Pfad.X360> [out.json]  JSON-Export
dotnet run -- analyze    <Pfad.X360>              Blob-Struktur
dotnet run -- hexdump    <Pfad.X360> [off] [len]  Hex-Dump
dotnet run -- stfs-list  <Paket>                  STFS-Dateien listen
dotnet run -- stfs-extract <Paket> <Ordner>       STFS extrahieren
dotnet run -- create-test-dlc <Ordner> <Ausgabe>  DLC aus Song-Ordner
dotnet run -- convert-ultrastar <TXT> <Ordner>    UltraStar konvertieren
```

### Web UI

Dark Theme Piano Roll Editor (Blazor Server):

```
[Toolbar: Brand | Datei | Export DLC / ZIP | Play | Zoom]
[Piano | === Piano Roll Canvas (Noten, Grid, Playhead) === | Metadaten ]
[Strip |                                                    | Sequenzen]
[      |                                                    | Note-Info]
[Timeline ================================================= |          ]
[ Lyrics: syl-la-bles  high-ligh-ted  in  ne-on            |          ]
[Props: Sequenz | Marker | Zoom | Offset                               ]
```

Funktionen:
- .X360 und .txt (UltraStar) Upload
- Metadaten editierbar (Title, Artist, Album, Genre, Year, Language)
- Sequenzen durchschaltbar
- Noten-Selektion mit Detail-Anzeige
- Canvas: Zoom (Ctrl+Scroll), Scroll, Drag, Hover-Glow
- Export als STFS LIVE-Paket oder ZIP

## Offene Probleme

### STFS-Paket wird von Lips nicht erkannt (BLOCKER)

**Status:** Die generierten STFS-Pakete werden von Aurora (Xbox Dashboard Mod) korrekt erkannt und der Titel aus der DLC.xml wird gelesen, aber Lips selbst listet den Song nicht auf.

**Was bereits verglichen und gefixt wurde** (gegen 2 Original-DLCs):

| Feld | Status |
|------|--------|
| Magic "LIVE" | OK |
| Certificate Type 0xC684 | OK |
| Licensing FFFFFFFFFFFFFFFF00000001 | OK |
| Header Size 0xAD0E | OK |
| Content Type 0x00000002 | OK |
| Title ID 0x4D530888 | OK |
| Block Separation 1 | OK |
| Display Name (9 Slots) | OK |
| Description (9 Slots) | OK |
| Title Name "Lips" (0x1691) | OK |
| Transfer Flags 0xC0 | OK |
| PNG Thumbnails | OK (Platzhalter) |

**Analyse-Ergebnisse (3 Original-DLCs + Captain Jack CD-Version verglichen):**

- Song-Dateien zwischen CD und DLC-Version sind **byte-identisch** -> unser Song-Content ist nicht das Problem
- Captain Jack DLC hat extra Dateien: `GES10007.X360` und `GES10007.xml` (Gesture-Daten fuer Kinect/Mikrofon)
- CertType variiert zwischen DLCs (0xC684 vs 0xC9D5) -> nicht hart kodierbar
- Header-Felder (TitleName, Licensing, Thumbnails etc.) sind jetzt alle korrekt

**Identifiziertes Kernproblem: STFS Block-Layout**

Unser STFS-Writer hat einen Bug im Block-Chain-Mechanismus:
- Original-DLCs verwenden **non-consecutive blocks** (kein 0x40 Flag in der File-Table)
- Die Xbox liest Dateien ueber die **Block-Chain in den Hash-Tables** (nextBlock-Pointer in jedem Hash-Entry)
- Unser Writer schreibt die Bloecke zwar sequentiell, aber die Hash-Table Block-Chain-Eintraege stimmen nicht mit dem tatsaechlichen Block-Layout ueberein
- **Symptom:** Unser eigener Reader kann die Dateien korrekt extrahieren (weil er den gleichen Bug hat), aber die Xbox liest Muell

**Empfohlene Loesungswege:**

1. **Kurzfristig (pragmatisch):** Song-Dateien als lose Dateien exportieren (ZIP) und mit einem getesteten STFS-Packer (Horizon, wxPirs, Le Fluffie) verpacken lassen
2. **Mittelfristig:** STFS Block-Layout komplett neu implementieren anhand der Python-Referenzimplementierung (py360) oder des Free60-Wiki
3. **Alternativ:** Einen Original-DLC als Template nehmen und nur die Datei-Inhalte innerhalb des bestehenden Block-Layouts ersetzen (ohne die Block-Struktur zu aendern) - erfordert dass die neuen Dateien in die gleiche Anzahl Bloecke passen

### Fehlende Features fuer kompletten Workflow

| Feature | Prioritaet | Beschreibung |
|---------|-----------|-------------|
| Audio-Konvertierung | Hoch | WAV/MP3 -> xWMA (benoeigt xWMAEncode oder ffmpeg-Pipeline) |
| Noten editieren | Mittel | Drag & Drop im Piano Roll |
| Audio-Playback | Mittel | Web Audio API mit Playhead-Sync |
| Batch-Konverter | Niedrig | Ganzen UltraStar-Ordner konvertieren |
| Lips-Preview | Niedrig | Gameplay-Simulation |

## Test-Abdeckung

```
171 Tests (alle gruen)
  FieldSizesTests           20  (Typgroessen, Big-Endian, ReadCString, ConvertByType)
  X360ReaderTests           17  (Header, Klassen, Vererbung, Groessen)
  BlobAnalyzerTests          6  (RU32/RI32/RF32, String-Suche)
  IxbDeserializerTests      44  (Pointer-Lookup, Objekte, Vektoren, Tone, Disambiguierung)
  X360WriterTests            7  (Roundtrip byte-identisch, Modifikation)
  IxbSerializerTests        12  (SetField, SetString, Roundtrip)
  UltraStarParserTests      25  (Header, Noten, Timing, MIDI, Duett, Edge Cases)
  UltraStarToLipsTests       7  (Konverter Roundtrip, Chart, Sequenzen, Timing)
  StfsReaderTests            9  (LIVE-Container, Extraktion, Dateien)
  StfsWriterTests            6  (Header, Roundtrip Writer->Reader)
  PackageBuilderTests       11  (DLC.xml, Lyric.X360, Komplett-Paket)
  Sonstige                   7
```

## Commit-Historie

```
8ec7fda Fix STFS header: add TitleName 'Lips' and transfer flags
301565c Fix STFS header: add thumbnails and correct display name locale slots
c00214d Fix STFS header: add licensing data and cert type for Lips DLC recognition
7259edd Add create-test-dlc and convert-ultrastar CLI commands
de639c7 Add STFS writer, editable metadata, and DLC export button
1bde98f Add UltraStar TXT parser with models and comprehensive unit tests
0edff62 Add UltraStar TXT import support to SongService
1d0389f Add LipsSongCreator.Web Blazor project with initial boilerplate
af1ba35 Enhance IxbDeserializer with inline struct support
0fa85c9 Add unit tests for BlobAnalyzer, FieldSizes, and IxbDeserializer
ac54318 Add BlobAnalyzer and IxbDeserializer for IXB binary blob parsing
8bad878 Add Example
7113a90 init
```
