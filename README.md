**English** | [中文](README.zh-CN.md)

# XnbConverter

A high-performance XNB conversion tool — a full unpack of the game's Content takes about 10 seconds (XACT-related files take a bit longer).
This project is a C# port and extension of the JavaScript project **xnbcli**, filling in the gaps it left behind. Most of the parsing logic is modeled on the MonoGame source.

## Performance comparison

| Tool   | XnbConverter | [StardewXnbHack](https://github.com/Pathoschild/StardewXnbHack) | [xnbcli](https://github.com/LeonBlade/xnbcli/) | [XNBExtract](https://community.playstarbound.com/threads/110976) |
|------|--------------|-----------------------------------------------------------------|------------------------------------------------|------------------------------------------------------------------|
| Unpack time | 0m 14s       | ≈0m 43s                                                         | ≈6m 5s                                         | ≈2m 20s                                                          |

## About

Author: oxiaozouo — [1714050472@qq.com](1714050472@qq.com)
Version: 1.1.0-alpha
Language: C# .NET 9

## Supported conversions

XNB files can be converted to editable source assets and back. Known to work with [Stardew Valley] and [Terraria (partial)].

#### Round-trippable:

| Exported file | Description |
|--------------|------------------------------------------------------------------------|
| ".json"      | JSON form of a class — usually a configuration file for some in-game behavior. Stardew Valley 1.5 structured data is supported; the fields can be looked up on the game wiki. |
| ".png"       | Game textures — character portraits, item sprites, map tilesheets, etc. |
| ".fx" | Shaders. HLSL effect source **reconstructed** from the MGFX binary. Any HLSL tool can open, edit and render it. The MGFX shell (container structure) is embedded in the same-named `.config`; on pack, the `.fx` is translated back to GLSL and written out. Note: unlike other types, effects are **not byte-exact** — the GLSL gets re-laid-out, though it is functionally equivalent. DX platform bytecode cannot be turned back into source, so it is exported as `.cso` instead (raw bytes). |
| ".tbin"      | Maps, editable with [Tiled](https://www.mapeditor.org/). |
| ".xml"       | XML form of a class — usually font files. |
| ".json .png" | Usually fonts: the JSON holds glyph cropping/metrics, the PNG is the atlas image. |
| ".json .wav" | Usually `SoundEffect` — music and sound effects. |
| ".xwb" | Wave banks — a collection of sound effects, part of XACT. Unpacks to individual wav files; on pack, rebuilt from the generated same-named `.xwb.config` manifest. |

## Command reference

#### Unpack:

```bat
unpack -c -i "xnb file/xnb folder" -o "output directory"
```

Matches every XNB file under "xnb file/xnb folder" and exports both the source assets and the XNB config files into "output directory".

#### Pack:

```bat
pack -c -i "xnb file/xnb folder" -o "output directory"
```

Matches every `.config` file under "xnb file/xnb folder" and compiles the XNB files into "output directory".

#### Auto (unpack + pack):

```bat
auto -c -i "file/folder" -o "output directory"
```

Matches both `.xnb` and `.config` files under "file/folder", unpacks and repacks automatically, and writes the results to "output directory".

You can type these commands into the program's console, or just double-click `pack.bat` / `unpack.bat` to run them quickly.

#### Options:

`-c` : enable parallel processing. This is the main feature that sets this project apart from xnbcli.
By processing files in parallel you can cut pack/unpack time by up to N times, at the cost of proportionally more memory.
Set the `"Concurrency"` value in `.config/config.json` to control how many files are processed at once.
`-i` : input file / folder
`-o` : output directory

#### Configuration file:

```json
{
  "LogTime": true,
  // true or false — whether to print a timestamp with each log line
  "TimeFormat": "MM-dd HH:mm:ss",
  // e.g. "yyyy-MM-dd HH:mm:ss", "MM-dd HH:mm:ss" — format of that timestamp; see standard date format strings
  "LogPrintingOptions": "Info, Warn, Error",
  // Info, Warn, Error, Debug (only these four) — which log levels are printed to the console, comma-separated
  "LogSaveOptions": "Error",
  // Info, Warn, Error, Debug (only these four) — which log levels are written to the log file, comma-separated
  "Concurrency": 15
  // Concurrency is >0 and < 16; 15 is recommended — number of files processed in parallel
}
```

## Quick tutorial: making an XNB mod

1. Unpack with this tool.
2. Edit the exported files — retouch images, swap fonts, change the JSON, edit maps, etc.
3. Adding a new XNB (e.g. a modified map in Stardew Valley):
   Prepare your edited `map.tbin` and the new `tilesheet.png`, then copy an existing `other_tilesheet.config`
   and rename it to `tilesheet.config`.
4. Pack it back. This project reports an error and aborts that file if the data format is wrong, so make sure your edits keep the data valid.

## Notes

This project is still in development (currently on hold), provided for learning and exchange only — please do not use it commercially. For any copyright issues arising from the XNB files you produce (font licenses, art asset licenses, original author rights, etc.), please judge for yourself; this project accepts no responsibility.

If you run into any problems while using it, contact [1714050472@qq.com](1714050472@qq.com) and I will try to help as soon as possible.

## Credits

### 1. [xnbcli](https://github.com/LeonBlade/xnbcli)

This project is a C# port of xnbcli with additional functionality.

### 2. [unxwb](https://github.com/mariodon/unxwb)

WaveBank export is based on unxwb, under its GPL license.

### 3. [MonoGame](https://github.com/MonoGame/MonoGame)

The AudioEngine, SoundBank and WaveBank readers are largely derived from the MonoGame project.

### 4. [TConvert](https://github.com/trigger-segfault/TConvert)

SoundEffect reading references the `WavConverter.cs` part of TConvert and ffmpeg's audio handling, under their GPL licenses. SoundEffect writing was implemented as well.

### 5. [FFmpeg](http://ffmpeg.org)

Audio transcoding is largely based on ffmpeg, under its LGPL license.

### 6. [TbinCSharp](https://github.com/spacechase0/TbinCSharp)

TBin reading comes from TbinCSharp, under its MIT license. Writing tbin back into XNB was implemented as well.

### 7. [LibSquishNet](https://github.com/MaxxWyndham/LibSquishNet)

DXT compression/decompression comes from LibSquishNet, under its MIT license. The code was heavily optimized for performance and integrated into this project.

### 8. [LzxDecoder.cs](https://github.com/MonoGame/MonoGame/blob/master/MonoGame.Framework/Content/LzxDecoder.cs)

LZX compression handling comes from LzxDecoder.cs, under its LGPL / MS-PL licenses.
Original project: [libmspack](https://www.cabextract.org.uk/libmspack/)

This project is released under the GPL.

### 9. [lz4net](https://github.com/MiloszKrajewski/lz4net) (via MonoGame)

LZ4 compression/decompression uses the copy of lz4net bundled inside the MonoGame content pipeline, located at `XnbConverter.Core/Utilities/LZ4`, under its BSD-3-Clause license.

## Libraries used

### 1. [Newtonsoft.Json](https://www.newtonsoft.com/json)

JSON reading and writing. Currently the only library still referenced from NuGet.

### 2. lz4net (vendored source, no package reference)

LZ4 compression/decompression uses the copy of lz4net from the MonoGame content pipeline, at `XnbConverter.Core/Utilities/LZ4` (BSD-3-Clause).
