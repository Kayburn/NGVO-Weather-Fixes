# NGVO Weather Fixes

*[Read in English](README.md)*

Patcher de [Synthesis](https://github.com/Mutagen-Modding/Synthesis) (C# / Mutagen)
que corrige bugs de iluminación puntuales en ~15 climas de Skyrim SE, detectados
jugando a fondo mi propia lista de mods (basada en **NGVO**, un modlist de Wabbajack
del que no soy el autor).

## Qué hace

En vez de generar un único patch combinado, `Program.cs` genera **un `.esp`
independiente por cada clima corregido** (marcado ESL), cada uno sobreescribiendo
solo el registro `WTHR` (y en algún caso `ImageSpace`/`LensFlare`) de ese clima
concreto — resueltos contra el load order real vía `ILinkCache` de Mutagen. Cada
`.esp` se puede activar o desactivar por separado.

Los fixes tocan sobre todo `DirectionalAmbientLightingColors` (DALC),
`SunlightColor`, tintes de `ImageSpace` y, en un caso, un `LensFlare` duplicado
(para no afectar a los ~50 climas que comparten el original).

## Requisitos

- La mayoría de los climas corregidos son nativos de **`NAT-ENB.esp`** — sin ese
  plugin, los FormID de origen no existen.
- Los valores están calibrados a ojo para el preset de ENB que trae NGVO
  (**Cabbage ENB**, basado en "Silent Horizons 2 - Shader Core" de Kitsuune).
  Con otro ENB (o sin ENB) los resultados visuales pueden no parecerse a lo
  esperado, aunque los datos de WTHR en sí sigan siendo correcciones válidas.

## Aviso importante

Este proyecto **no es un mod plug-and-play**. Los valores de cada clima están
ajustados a mi gusto propio tras horas de pruebas en juego. Lo comparto como
referencia / código abierto por si le interesa a alguien el proceso o quiere
adaptarlo a su propio load order, no como algo listo para instalar
directamente.

## Configuración

Las rutas de tu propia instalación (carpeta de salida del mod, plugins de
origen) no van en el código — van en `WeatherAdjustmentProject/paths.local.json`,
que no se sube al repo. Para usarlo:

1. Copia `WeatherAdjustmentProject/paths.example.json` a
   `WeatherAdjustmentProject/paths.local.json`.
2. Rellena `OutputDirs` y `OrderedSourcePaths` con tus propias rutas.
3. `dotnet run` desde `WeatherAdjustmentProject/`.

## Stack

.NET 8, `Mutagen.Bethesda.Skyrim` 0.52, `Mutagen.Bethesda.Synthesis` 0.35.3.

## Créditos

- NGVO — modlist base de Wabbajack (no soy su autor).
- NAT.ENB - ESP Weather Plugin.
- Cabbage ENB / Silent Horizons 2 - Shader Core (Kitsuune).
