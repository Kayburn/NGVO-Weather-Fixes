# NGVO Weather Fixes

*[Leer en español](README_ES.md)*

[Synthesis](https://github.com/Mutagen-Modding/Synthesis) (C# / Mutagen) patcher
that fixes specific lighting bugs in ~15 Skyrim SE weathers, found by playing
through my own modlist (based on **NGVO**, a Wabbajack modlist I'm not the
author of) in depth.

## What it does

Instead of generating a single combined patch, `Program.cs` produces **one
standalone `.esp` per fixed weather** (flagged ESL), each overriding only the
`WTHR` record (and in some cases `ImageSpace`/`LensFlare`) of that specific
weather — resolved against the real load order via Mutagen's `ILinkCache`.
Each `.esp` can be enabled or disabled independently.

The fixes mostly touch `DirectionalAmbientLightingColors` (DALC),
`SunlightColor`, `ImageSpace` tints, and in one case a duplicated
`LensFlare` (to avoid affecting the ~50 other weathers that share the
original record).

## Requirements

- Most of the fixed weathers are native to **`NAT-ENB.esp`** — without that
  plugin, the source FormIDs don't exist.
- Values were tuned by eye for the ENB preset NGVO ships
  (**Cabbage ENB**, based on Kitsuune's "Silent Horizons 2 - Shader Core").
  With a different ENB (or none at all) the visual results may not match
  what's expected, even though the underlying WTHR data corrections remain
  valid.

## Important note

This project **is not a plug-and-play mod**. Each weather's values were
tuned to my own taste after hours of in-game testing. I'm sharing it as
reference / open source in case the process is useful to someone or they
want to adapt it to their own load order — not as something ready to
install directly.

## Setup

Paths specific to your own install (mod output folder, source plugins)
don't live in the code — they go in
`WeatherAdjustmentProject/paths.local.json`, which is not committed to the
repo. To use it:

1. Copy `WeatherAdjustmentProject/paths.example.json` to
   `WeatherAdjustmentProject/paths.local.json`.
2. Fill in `OutputDirs` and `OrderedSourcePaths` with your own paths.
3. `dotnet run` from `WeatherAdjustmentProject/`.

## Stack

.NET 8, `Mutagen.Bethesda.Skyrim` 0.52, `Mutagen.Bethesda.Synthesis` 0.35.3.

## Credits

- NGVO — base Wabbajack modlist (I'm not its author).
- NAT.ENB - ESP Weather Plugin.
- Cabbage ENB / Silent Horizons 2 - Shader Core (Kitsuune).
