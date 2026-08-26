using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.Drawing;
using System.Text.Json;

namespace WeatherAdjustmentProject
{
    // Standalone generator: produces one small .esp per fixed weather, each carrying only
    // that weather's override, so the user can bundle them into their own MO2 mod individually
    // instead of one combined Synthesis-pipeline patch.
    public class Program
    {
        // Local machine paths (output dirs + source plugin paths) live in paths.local.json,
        // gitignored so absolute paths never end up in the public repo. See paths.example.json
        // for the expected shape.
        private static readonly PathsConfig Paths = PathsConfig.Load();

        // Written to both: the MO2 mod folder (what the game actually loads) and a backup
        // copy (the user's reference/archive location).
        private static readonly string[] OutputDirs = Paths.OutputDirs;

        // Verified against the full active NGVO load order (542 plugins scanned): only these
        // five files ever touch the 14 target weather records, NAT-ENB.esp being the winner
        // for all of them. Listed lowest to highest priority.
        private static readonly string[] OrderedSourcePaths = Paths.OrderedSourcePaths;

        private sealed class PathsConfig
        {
            public string[] OutputDirs { get; set; } = Array.Empty<string>();
            public string[] OrderedSourcePaths { get; set; } = Array.Empty<string>();

            public static PathsConfig Load()
            {
                const string fileName = "paths.local.json";
                if (!File.Exists(fileName))
                {
                    throw new FileNotFoundException(
                        $"No se encontró '{fileName}'. Copia 'paths.example.json' a " +
                        $"'{fileName}' y rellena tus propias rutas antes de ejecutar el patcher.");
                }

                var json = File.ReadAllText(fileName);
                return JsonSerializer.Deserialize<PathsConfig>(json)
                    ?? throw new InvalidDataException($"'{fileName}' está vacío o mal formado.");
            }
        }

        public static void Main()
        {
            var mods = OrderedSourcePaths
                .Select(p => SkyrimMod.CreateFromBinaryOverlay(p, SkyrimRelease.SkyrimSE))
                .ToList();
            var linkCache = mods.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();

            // Weather #10: CloudyTU_A - night ground/environment orange color bug.
            // NAT-ENB retuned every night DALC direction to a uniform cool gray (45,53,62)
            // except Z- (ground-facing), left at a stray orange (255,128,0) - confirmed
            // in-game as a visible orange tint on the ground/environment at night.
            // 2026-08-22: the user reported the yellowish tint persisted after the DALC fix. Found
            // the real remaining culprit: SunlightColor.Night was never checked originally and
            // sat at a strongly orange 170,85,0 (vs vanilla's cool 44,81,107) - a clear outlier
            // versus the standard 48,65,80 this same "Cloudy" family uses everywhere else
            // (CloudyTU, CloudyPF, OvercastTU, StormPF, etc.) - matched to that shared standard.
            // 2026-08-25: on top of the DALC/SunlightColor fix, the persistent orange/yellow
            // ground+moon-halo tint that survived every WTHR/ImageSpace/ENB-ini fix turned out to
            // come from NEITHER of those systems. It's the "NAT_SunFlareClear" LensFlare record
            // (0905AF:NAT-ENB.esp) that this weather's SunGlareLensFlare field points to -
            // confirmed by fully disabling ENB (GLOBAL.UseEffect off) and still seeing a giant
            // orange halo around the moon, meaning it's a native game object, not an ENB
            // post-process. Of its 21 sprites, 6 (HexShape01 x3, HexShape02 x3) have
            // Tint=188,44,18 (strong orange-red), sticking out from the rest of the sprites' cool
            // blue-white palette (~152-225,180-235,220-255). NOT editing that record directly:
            // ~50 other weathers share it via the same SunGlareLensFlare field (confirmed via
            // scan), including several already confirmed fine (#2, #6, #8, #12, #14) - editing it
            // would risk their daytime sun-flare look, since LensFlareSpriteData has no day/night
            // split of its own. Instead: duplicate it into a new record scoped to this .esp only,
            // fix the tint there, and repoint only this weather's SunGlareLensFlare to the copy -
            // the shared original stays untouched.
            // 2026-08-25: chased the Aurora model (RY vs GB, see below) as a candidate - reverted.
            // The user's installed "Praedy's Sky AIO" is v2.0.0.0 (newest is 2.1.0.0), and that mod
            // had a known bug where skyrimauroragb.nif and skyrimaurorary.nif had their contents
            // SWAPPED, fixed in a later update. In the user's (older, still-buggy) version, RY.nif is
            // the one that actually renders green - matches the original "auroras boreales
            // verdes" description and confirmed: switching to GB.nif made the aurora itself show
            // MORE orange, not less, while the ground tint was unaffected either way - proving
            // the aurora mesh was never the ground-tint culprit at all. Reverted to the original
            // vanilla-assigned RY.nif.
            //
            // 2026-08-25 (still investigating): the orange survives ENB fully disabled
            // (GLOBAL.UseEffect off) across the WHOLE scene (moon, sky, ground) - rules out ENB's
            // own shaders and the lens flare (which only ever explained the moon halo, fixed
            // separately below). Also ruled out: AGIS (game-imagespace reintroduction into ENB -
            // disabling it entirely did not remove the orange) and Monochroma (confirmed OFF by
            // default in the saved enbeffectpostpass.fx.ini, so it was never the source; any
            // color shift the user saw from it was from enabling it live just to test, not from
            // baseline config). Landed on the one native data point that's present regardless of
            // ENB and hasn't been tried yet: this weather's own Night ImageSpace
            // (ISSkyrimCloudyNIGHT_TU_A, 10E1D3:Skyrim.esm) has Tint.Amount=0.775 (fairly strong)
            // and Cinematic.Contrast=1.5 (50% boost) - neither looks dramatically orange in
            // isolation (Tint.Color=150,166,169 is actually cool-leaning) but combined they could
            // amplify latent warmth from elsewhere in the render. Confirmed via LinkCache scan:
            // this ImageSpace is used by ONLY this one weather (no sharing risk, no need to
            // duplicate - editing the override directly is safe). Zeroed Tint.Amount and reset
            // Contrast to neutral 1.0 as a test.
            GenerateEsp(linkCache, "NGVO_Weather_CloudyTU_A.esp", "Skyrim.esm", 0x0010E1ED, weather =>
            {
                weather.DirectionalAmbientLightingColors.Night.DirectionalZMinus = Color.FromArgb(0, 45, 53, 62);
                weather.SunlightColor.Night = Color.FromArgb(0, 48, 65, 80);
                weather.Aurora.File = @"Meshes\Sky\SkyrimAuroraRY.nif";
            }, extraSetup: (patchMod, lc) =>
            {
                // ImageSpace fix - FOUND AND CONFIRMED IN-GAME (2026-08-26). The user live-tuned the
                // AGIS "Night - Tint" value in the ENB panel through iterative testing (this field
                // is literally this ImageSpace's own Tint.Color/Tint.Amount, confirmed reachable
                // via Mutagen) until the ground/scene orange cast was gone: Color=(0,150,200),
                // Alpha=150/255=0.588. Confirmed the separate "Night - Color Balance" ENB Extender
                // setting (a DIFFERENT, GLOBAL, non-per-weather file - enbeffect.fx.ini's CG.HDR
                // section) was NOT needed - left at neutral (255,255,255) and the result was
                // equally clean, so the fix stays fully scoped to this one weather via Mutagen,
                // no global ENB file edited. Only this one weather uses this ImageSpace (confirmed
                // via LinkCache scan), so editing the override in place is safe - no duplication
                // needed. Root cause note: still not fully understood WHY the native tint at its
                // original values (150,166,169 @ 0.775) produced a visible orange cast rather than
                // the expected cool/neutral look, nor why zeroing it first (2026-08-25) didn't fix
                // it either - what worked was replacing it with a different cool tint entirely, not
                // removing it. Treat this as a confirmed-working color correction, not a fully
                // explained root cause.
                var nightImgsKey = new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0x10E1D3);
                var nightImgs = patchMod.ImageSpaces.GetOrAddAsOverride(lc.Resolve<IImageSpaceGetter>(nightImgsKey));
                nightImgs.Tint.Color = Color.FromArgb(0, 0, 150, 200);
                nightImgs.Tint.Amount = 150f / 255f;
                nightImgs.Cinematic.Contrast = 1.0f; // kept from the earlier test fix - was already in place during the user's successful live tuning above

                // LensFlare fix (moon-halo bug, separate from the ground-tint investigation above):
                // duplicate the shared NAT_SunFlareClear record so the ~50 other weathers using it
                // stay untouched, and neutralize the 6 orange HexShape01/02 sprite tints on the copy.
                var originalFlareKey = new FormKey(ModKey.FromNameAndExtension("NAT-ENB.esp"), 0x0905AF);
                var originalFlare = lc.Resolve<ILensFlareGetter>(originalFlareKey);
                var newFlare = originalFlare.Duplicate(patchMod.GetNextFormKey(), new LensFlare.TranslationMask(true));
                newFlare.EditorID = "NGVO_SunFlareClear_CloudyTU_A_Fix";
                patchMod.LensFlares.Add(newFlare);
                foreach (var sprite in newFlare.Sprites)
                {
                    if (sprite.LensFlareSpriteId is "HexShape01" or "HexShape02")
                    {
                        sprite.Data.Tint = Color.FromArgb(0, 168, 187, 238);
                    }
                }

                return newFlare.FormKey;
            }, setSunGlareLensFlare: true);

            // Weather #1: FogPF - de dia bien, de noche ultra oscuro.
            // ORIGINAL FIX (kept, harmless): normalized Sky/Fog/MoonGlare to NAT-ENB's ~0.55x
            // night-dimming factor. CONFIRMED INEFFECTIVE IN-GAME (2026-08-22, the user: "sigue
            // viendose muy oscuro") - this ENB preset uses its own procedural SKY/SKYSCATTERING/
            // VOLUMETRICFOG shaders (visible in the ENB panel's category list), which appear to
            // replace vanilla Sky/Fog/Horizon rendering entirely, making those WTHR fields
            // cosmetically inert. Confirmed by contrast: weather #13 (FogRE), where only
            // DALC/AmbientColor/SunlightColor were touched (no Sky/Fog), DID show a visible
            // change in-game. Added DALC/SunlightColor.Night raised to ~90% of this weather's
            // vanilla baseline (AmbientColor.Night was already near-vanilla, left alone).
            GenerateEsp(linkCache, "NGVO_Weather_FogPF.esp", "Skyrim.esm", 0x000C821E, weather =>
            {
                weather.SkyUpperColor.Night = Color.FromArgb(0, 84, 95, 102);
                weather.SkyLowerColor.Night = Color.FromArgb(0, 40, 52, 59);
                weather.HorizonColor.Night = Color.FromArgb(0, 31, 42, 47);
                weather.FogNearColor.Night = Color.FromArgb(0, 39, 45, 48);
                weather.FogFarColor.Night = Color.FromArgb(0, 32, 41, 46);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 140, 125, 105);

                weather.SunlightColor.Night = Color.FromArgb(0, 64, 104, 127);
                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 62, 62, 72);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 62, 72, 72);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 63, 70, 71);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 63, 65, 71);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 50, 63, 64);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 69, 95, 104);
            });

            // Weather #2: CloudyTU - stars a bit too bright, ground/environment a bit too dark
            // at night (moon phase behavior and dawn left untouched, per the user - expected/working
            // as intended). Day left untouched: no data anomaly found, and the complaint itself
            // was too vague ("sensacion extrana") to translate into a confident numeric change.
            GenerateEsp(linkCache, "NGVO_Weather_CloudyTU.esp", "Skyrim.esm", 0x0010A243, weather =>
            {
                weather.StarsColor.Night = Color.FromArgb(0, 155, 155, 155);
                weather.AmbientColor.Night = Color.FromArgb(0, 31, 37, 43);

                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 52, 61, 71);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 52, 61, 71);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 52, 61, 71);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 52, 61, 71);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 36, 43, 51);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 76, 92, 108);
            });

            // Weather #3: StormFF - day too bright for a storm, night too dark. Sunrise/sunset
            // left untouched, per the user - already correct.
            GenerateEsp(linkCache, "NGVO_Weather_StormFF.esp", "Skyrim.esm", 0x0010A23C, weather =>
            {
                // Day: these 3 channels came out brighter than vanilla's storm equivalent while
                // everything else stayed equal or darker - scaled down 25%, hue preserved.
                weather.AmbientColor.Day = Color.FromArgb(0, 48, 52, 56);
                weather.HorizonColor.Day = Color.FromArgb(0, 27, 34, 37);
                weather.SkyUpperColor.Day = Color.FromArgb(0, 77, 80, 82);

                // Night: DALC follows this weather's normal ~0.75x night-dimming factor, but
                // Sky/Horizon/Fog were crushed far below that (FogNear near-black) - normalized
                // to the same ~0.75x factor.
                weather.SkyUpperColor.Night = Color.FromArgb(0, 22, 45, 59);
                weather.SkyLowerColor.Night = Color.FromArgb(0, 11, 22, 28);
                weather.HorizonColor.Night = Color.FromArgb(0, 0, 16, 18);
                weather.FogNearColor.Night = Color.FromArgb(0, 8, 20, 35);
                weather.FogFarColor.Night = Color.FromArgb(0, 26, 35, 38);

                // 2026-08-22: the user confirmed this weather now looks fixed in-game, but that may
                // reflect the day fix (AmbientColor.Day, a confirmed-effective field) rather than
                // the night one (Sky/Fog only, same as #1's initially-ineffective fix). Added
                // DALC/SunlightColor.Night at ~90% of vanilla for consistency/safety - if night
                // was already fine this only nudges it slightly brighter within the same range.
                weather.SunlightColor.Night = Color.FromArgb(0, 72, 134, 153);
                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 54, 64, 80);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 54, 69, 76);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 54, 62, 75);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 54, 65, 75);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 44, 54, 61);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 80, 101, 104);
            });

            // Weather #4: FogRain (native to NAT-ENB.esp, no vanilla equivalent). Night
            // Sky/Horizon/Fog/MoonGlare/DALC are byte-for-byte identical to weather #1's
            // (FogPF) broken values - same shared template bug. Reusing #1's fix, including the
            // 2026-08-22 DALC/SunlightColor addition (see #1's comment: Sky/Fog fields are
            // cosmetically inert under this ENB preset's procedural sky/fog shaders - DALC is
            // the field that actually renders).
            GenerateEsp(linkCache, "NGVO_Weather_FogRain.esp", "NAT-ENB.esp", 0x0053C7, weather =>
            {
                weather.SkyUpperColor.Night = Color.FromArgb(0, 84, 95, 102);
                weather.SkyLowerColor.Night = Color.FromArgb(0, 40, 52, 59);
                weather.HorizonColor.Night = Color.FromArgb(0, 31, 42, 47);
                weather.FogNearColor.Night = Color.FromArgb(0, 39, 45, 48);
                weather.FogFarColor.Night = Color.FromArgb(0, 32, 41, 46);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 140, 125, 105);

                weather.SunlightColor.Night = Color.FromArgb(0, 64, 104, 127);
                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 62, 62, 72);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 62, 72, 72);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 63, 70, 71);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 63, 65, 71);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 50, 63, 64);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 69, 95, 104);
            });

            // Weather #5: FogVT - "se ve algo menos de lo debido pese a la niebla". Same shared
            // broken night template as #1/#4, but computed from THIS weather's own vanilla
            // baseline (not reused blindly) - MoonGlareColor vanilla is a distinctly warmer/
            // orange tone here than FogPF's, so the 0.55x-corrected value differs accordingly.
            // Stars (0,0,0) and reduced moon brightness left untouched - confirmed normal/fog-
            // appropriate by the user. Day left untouched - confirmed fine.
            // 2026-08-22: added DALC/SunlightColor.Night (same "Sky/Fog is cosmetically inert"
            // finding as #1 - this weather's vanilla DALC Night is identical to FogPF's, so the
            // 90%-of-vanilla DALC target is the same; SunlightColor computed from FogVT's own
            // vanilla, which differs slightly from FogPF's).
            GenerateEsp(linkCache, "NGVO_Weather_FogVT.esp", "Skyrim.esm", 0x0010A7A7, weather =>
            {
                weather.SkyUpperColor.Night = Color.FromArgb(0, 84, 95, 102);
                weather.SkyLowerColor.Night = Color.FromArgb(0, 40, 52, 59);
                weather.HorizonColor.Night = Color.FromArgb(0, 31, 42, 48);
                weather.FogNearColor.Night = Color.FromArgb(0, 35, 45, 47);
                weather.FogFarColor.Night = Color.FromArgb(0, 37, 47, 52);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 96, 49, 20);

                weather.SunlightColor.Night = Color.FromArgb(0, 73, 113, 138);
                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 62, 62, 72);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 62, 72, 72);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 63, 70, 71);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 63, 65, 71);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 50, 63, 64);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 69, 95, 104);
            });

            // Weather #6: CloudyPF - "dia algo menos luminoso de lo debido, nada exagerado".
            // DALC Day was 78-93% of vanilla in 4/6 directions (Z+ already above vanilla, left
            // untouched) - raised the below-vanilla directions 10%. AmbientColor.Day left alone:
            // same shared template value seen across other weathers with no day complaint, so
            // it's unlikely to be the actual driver of the "less lit" impression.
            GenerateEsp(linkCache, "NGVO_Weather_CloudyPF.esp", "Skyrim.esm", 0x00012F89, weather =>
            {
                var dalcDay = weather.DirectionalAmbientLightingColors.Day;
                dalcDay.DirectionalXPlus = Color.FromArgb(0, 97, 136, 128);
                dalcDay.DirectionalXMinus = Color.FromArgb(0, 118, 129, 139);
                dalcDay.DirectionalYPlus = Color.FromArgb(0, 118, 129, 139);
                dalcDay.DirectionalYMinus = Color.FromArgb(0, 124, 146, 168);
                dalcDay.DirectionalZMinus = Color.FromArgb(0, 167, 175, 184);
            });

            // Weather #7: OvercastTU (native to NAT-ENB.esp) - "sin estrellas ni luna pero
            // demasiado iluminado para no tenerlas". StarsColor/MoonGlareColor are already
            // correctly 0,0,0 (fully overcast), so the unexplained brightness comes from
            // DALC/AmbientColor/SunlightColor Night - the same shared "Cloudy" template value
            // as weather #2, but here the user wants it LOWERED instead of raised (confirmed no
            // conflict since each weather patches independently). Lowered 25%.
            GenerateEsp(linkCache, "NGVO_Weather_OvercastTU.esp", "NAT-ENB.esp", 0x0053BD, weather =>
            {
                weather.AmbientColor.Night = Color.FromArgb(0, 20, 24, 28);
                weather.SunlightColor.Night = Color.FromArgb(0, 36, 49, 60);

                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 23, 28, 33);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 36, 44, 52);
            });

            // Weather #9: Snow_Alt (native to NAT-ENB.esp) - "de noche no se distingue
            // absolutamente nada, extremadamente oscuro" (strongest complaint of the 14).
            // Vanilla SkyrimOvercastSnow deliberately keeps night notably bright (snow
            // reflectivity - FogNearColor.Night is even brighter than Day there), but NAT-ENB
            // crushed it with the same shared broken template as #1/#4/#7. MoonGlareColor/
            // StarsColor already correctly 0,0,0 (matches vanilla, fully overcast) - untouched.
            // 2026-08-22: given the Sky/Fog fields are now confirmed cosmetically inert under
            // this ENB preset, AmbientColor.Night raised further (41,45,51 -> 90% of vanilla
            // 75,82,93) and DALC/Night added (previously untouched) - this was the strongest
            // complaint of the 14, so DALC needs to carry the fix on its own now.
            // 2026-08-22 (round 2): the user confirmed still too dark after the 90%-of-vanilla pass -
            // asked for +20% more on top of that. Applied directly.
            GenerateEsp(linkCache, "NGVO_Weather_SnowAlt.esp", "NAT-ENB.esp", 0x0D64D3, weather =>
            {
                weather.AmbientColor.Night = Color.FromArgb(0, 82, 89, 101);
                weather.SkyUpperColor.Night = Color.FromArgb(0, 51, 59, 61);
                weather.SkyLowerColor.Night = Color.FromArgb(0, 34, 41, 42);
                weather.HorizonColor.Night = Color.FromArgb(0, 35, 36, 36);
                weather.FogNearColor.Night = Color.FromArgb(0, 53, 61, 64);
                weather.FogFarColor.Night = Color.FromArgb(0, 33, 36, 36);

                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 77, 78, 84);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 76, 85, 84);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 77, 84, 82);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 76, 79, 85);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 65, 73, 72);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 143, 154, 157);
            });

            // Weather #11: StormPF - day too bright for a heavy storm, night too bright
            // ("similar al #7, bajar" - the user's own words). Sunrise left pending: the signal is
            // genuinely mixed against vanilla (some channels over, some under), no clear single
            // broken outlier - not applying a guessed fix there.
            GenerateEsp(linkCache, "NGVO_Weather_StormPF.esp", "Skyrim.esm", 0x000C8220, weather =>
            {
                // Day: only HorizonColor.Day stood out above vanilla (~3x); everything else
                // was at or below vanilla already.
                weather.HorizonColor.Day = Color.FromArgb(0, 32, 35, 37);

                // Night: identical broken template to weather #7 - same -25% correction, per
                // the user's own "similar al #7" framing.
                weather.AmbientColor.Night = Color.FromArgb(0, 20, 24, 28);
                weather.SunlightColor.Night = Color.FromArgb(0, 36, 49, 60);
                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 34, 40, 47);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 23, 28, 33);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 36, 44, 52);
            });

            // Weather #12: SClearSkyTU (native to NAT-ENB.esp) - "luna llena brilla muchisimo,
            // alumbra el entorno como si fuera de dia; estrellas muy iluminadas (nebulosas
            // visibles)". StarsColor.Night (255,255,255) and MoonGlareColor.Night (181,191,202)
            // both matched or exceeded vanilla already - not bugs, just too strong for the user's
            // taste combined with the enhanced star/moon SKSE visuals. DALC Night intentionally
            // NOT touched: already below the vanilla clear-weather baseline, no data-backed case
            // for it being the culprit - the "entorno" brightness is more likely ENB bloom
            // triggered by the strong MoonGlareColor bleeding into the scene. Revisit DALC only
            // if the environment still looks too bright in-game after this.
            GenerateEsp(linkCache, "NGVO_Weather_SClearSkyTU.esp", "NAT-ENB.esp", 0x00003DF4, weather =>
            {
                weather.StarsColor.Night = Color.FromArgb(0, 166, 166, 166);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 109, 115, 121);
            });

            // Weather #13: FogRE - two independent issues.
            // (1) Type-classification bug: Flags=Cloudy while the genuinely clear weathers in
            // the user's list (#12, #14) use Pleasant - causes the wrong "cloudy" icon on the wait
            // menu despite the full moon being visible through the fog. Flag-only change, does
            // not affect any lighting/color field.
            // (2) Night "demasiado iluminado, bajar un pelin" - no single outlier field found
            // (Sky/Fog already crushed near-black same as #1/#4/#5/#9; DALC/Ambient/Sunlight at
            // or below vanilla; MoonGlareColor modest). The user confirmed to lower it anyway based
            // on their own in-game read - applied a moderate -15% to AmbientColor/SunlightColor/
            // DALC/MoonGlareColor Night (left Sky/Fog/FogNear alone, already near the floor).
            GenerateEsp(linkCache, "NGVO_Weather_FogRE.esp", "Skyrim.esm", 0x0010A239, weather =>
            {
                weather.Flags = (weather.Flags & ~Weather.Flag.Cloudy) | Weather.Flag.Pleasant;

                weather.AmbientColor.Night = Color.FromArgb(0, 23, 27, 31);
                weather.SunlightColor.Night = Color.FromArgb(0, 41, 55, 68);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 32, 37, 40);

                var dalcNight = weather.DirectionalAmbientLightingColors.Night;
                dalcNight.DirectionalXPlus = Color.FromArgb(0, 30, 37, 43);
                dalcNight.DirectionalXMinus = Color.FromArgb(0, 30, 37, 43);
                dalcNight.DirectionalYPlus = Color.FromArgb(0, 30, 37, 43);
                dalcNight.DirectionalYMinus = Color.FromArgb(0, 41, 54, 68);
                dalcNight.DirectionalZPlus = Color.FromArgb(0, 41, 63, 86);
                dalcNight.DirectionalZMinus = Color.FromArgb(0, 41, 63, 86);
            });

            // Weather #14: SunnyTU - "de dia demasiado luminoso". Only SunlightColor.Day stood
            // out (245,233,211, near the 255 ceiling, ~55-65% above vanilla's 157,147,140);
            // DALC/Sky/Horizon/FogFar were already at or below vanilla. Lowered 20%. Sunset/
            // night left untouched - confirmed correct.
            GenerateEsp(linkCache, "NGVO_Weather_SunnyTU.esp", "Skyrim.esm", 0x0010A240, weather =>
            {
                weather.SunlightColor.Day = Color.FromArgb(0, 196, 186, 169);
            });

            // New batch (2026-08-22): SClearSkyPF (native to NAT-ENB.esp) - "despejado,
            // demasiada iluminacion de noche, parece de dia, la iluminacion de las lunas parece
            // irreal, muy exagerada". Confirmed a genuinely distinct weather from #12
            // (SClearSkyTU) - different EditorID/FormID, not a re-rolled duplicate. Same complaint
            // pattern and same fix approach as #12: StarsColor.Night was already at the ceiling
            // (255,255,255, matches vanilla-equivalent - not a bug, just too strong combined with
            // the enhanced star/moon SKSE visuals) and MoonGlareColor.Night (143,159,175) lowered
            // by the same -35% the user approved for #12.
            GenerateEsp(linkCache, "NGVO_Weather_SClearSkyPF.esp", "NAT-ENB.esp", 0x007F00, weather =>
            {
                weather.StarsColor.Night = Color.FromArgb(0, 166, 166, 166);
                weather.MoonGlareColor.Night = Color.FromArgb(0, 93, 103, 114);
            });
        }

        private static void GenerateEsp(
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
            string fileName,
            string originMasterName,
            uint weatherId,
            Action<Weather> edit,
            Func<SkyrimMod, ILinkCache<ISkyrimMod, ISkyrimModGetter>, FormKey>? extraSetup = null,
            bool setSunGlareLensFlare = false)
        {
            var patchMod = new SkyrimMod(ModKey.FromNameAndExtension(fileName), SkyrimRelease.SkyrimSE);
            patchMod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small; // ESL/ESP-FE flag - keeps these micro-patches out of the regular plugin-slot budget

            FormKey? extraRecordKey = extraSetup?.Invoke(patchMod, linkCache);

            var originFormKey = new FormKey(ModKey.FromNameAndExtension(originMasterName), weatherId);
            var weather = patchMod.Weathers.GetOrAddAsOverride(linkCache.Resolve<IWeatherGetter>(originFormKey));
            edit(weather);

            if (setSunGlareLensFlare && extraRecordKey.HasValue)
            {
                weather.SunGlareLensFlare.SetTo(extraRecordKey.Value);
            }

            foreach (var dir in OutputDirs)
            {
                var outPath = Path.Combine(dir, fileName);
                patchMod.WriteToBinary(outPath);
                Console.WriteLine($"Wrote {outPath}");
            }
        }
    }
}
