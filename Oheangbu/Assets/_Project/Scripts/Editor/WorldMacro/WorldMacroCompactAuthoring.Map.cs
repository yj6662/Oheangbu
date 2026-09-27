using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        const int CompactMapVersion = 1;
        static string CompactMapStatePath => Path.Combine(Output, "map_progress.json");
        static string CompactMapReportPath => Path.Combine(Output, "map_alignment_report.json");

        [Serializable] sealed class CompactMapTextureJob
        {
            public string sourceData, targetData, kind, tileId, sourceImage, sourceImageHash, sourceMetaHash;
            public string destinationImage, destinationImageHash, destinationMetaHash, sourceGraphicsFormat;
            public int tileIndex = -1, width, height;
            public bool completed, srgb, mipmaps, alphaIsTransparency;
            public FilterMode filterMode;
            public Rect sourceWorldUv, destinationWorldUv;
            public Vector2 sourceMin, sourceMax, destinationMin, destinationMax;
            public double maximumInverseRoundtripErrorMetres;
        }

        [Serializable] sealed class CompactMapState
        {
            public int version = CompactMapVersion;
            public string phase = "QUEUED", compressionHash, sourceSceneHash;
            public string method = "Each destination pixel center is inverse-mapped to source world XZ, then source map UV and source texture crop UV. RGBA8 bilinear sampling retains alpha. Regional terrain tiles retain their original detail source and pixel dimensions.";
            public string discoveryPolicy = "Existing map/mini-paper shaders, marker discovery flags, vector line policy and explored-cell reveal behavior remain unchanged.";
            public CompactMapTextureJob[] jobs = Array.Empty<CompactMapTextureJob>();
        }

        [Serializable] sealed class CompactMapAlignment
        {
            public string sourceData, targetData, sourceHash, targetHash;
            public bool usable, hasIllustration, regionTileCountPreserved, miniDetailPrefixPreserved;
            public int outlinePoints, markers, lines, zones, regionTiles, comparedCoordinates;
            public float maximumCoordinateErrorMetres;
        }

        [Serializable] sealed class CompactMapReport
        {
            public string status, compressionHash, sourceSceneHash, method, discoveryPolicy;
            public bool originalsUnchanged;
            public CompactMapTextureJob[] textures;
            public CompactMapAlignment[] maps;
        }

        [Serializable] sealed class CompactMapStatus
        {
            public string phase, nextImage, manifest, alignmentReport;
            public int completedTextures, totalTextures;
        }

        // Bounded continuation: at most ONE texture readback/warp/import per call.
        // This function never changes source asset importers, source files or map shaders.
        static string MapStep()
        {
            RequireCompact();
            var progress = ReadProgress();
            var compression = Compression;
            if (compression == null || compression.Mapping == null)
                throw new InvalidOperationException("Compact compression mapping is missing.");
            string compressionPath = AssetDatabase.GetAssetPath(compression);
            if (string.IsNullOrEmpty(compressionPath) || !File.Exists(compressionPath))
                throw new InvalidOperationException("Save the compact compression mapping before preparing map textures.");
            CompactMapState state;
            if (File.Exists(CompactMapStatePath))
            {
                state = JsonUtility.FromJson<CompactMapState>(File.ReadAllText(CompactMapStatePath));
                if (state == null || state.version != CompactMapVersion || state.jobs == null)
                    throw new InvalidOperationException("Unsupported or incomplete compact map progress file.");
            }
            else
            {
                state = PrepareCompactMapState(progress, compression);
                WriteCompactMapState(state);
            }
            if (state.compressionHash != Hash(compressionPath) || state.sourceSceneHash != progress.sourceHash)
                throw new InvalidOperationException("Map source/compression revision changed; do not mix texture jobs from different mappings.");
            if (state.phase == "COMPLETE")
            {
                VerifyCompactMapInputsAndOutputs(state, progress);
                return CompactMapStatusJson(state);
            }

            var job = state.jobs.FirstOrDefault(j => !j.completed);
            if (job != null)
            {
                VerifyCompactMapSource(job);
                BuildCompactMapTexture(job, compression.Mapping);
                job.completed = true;
                state.phase = "TEXTURES";
                WriteCompactMapState(state);
            }
            if (state.jobs.All(j => j.completed))
            {
                FinishCompactMap(state, progress, compression);
                state.phase = "COMPLETE";
                WriteCompactMapState(state);
            }
            return CompactMapStatusJson(state);
        }

        static CompactMapState PrepareCompactMapState(Progress progress, WorldMacroCompressionMapSO compression)
        {
            var jobs = new List<CompactMapTextureJob>();
            var pairs = (progress.assets ?? Array.Empty<AssetPair>())
                .Where(p => AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(p.source) != null).ToArray();
            if (pairs.Length == 0) throw new InvalidOperationException("No cloned runtime map data was recorded by compact Prepare.");
            for (int index = 0; index < pairs.Length; index++)
            {
                var pair = pairs[index];
                RequireCompactMapDestination(pair.target);
                if (Hash(pair.source) != pair.hash) throw new InvalidOperationException("Original map data changed: " + pair.source);
                var source = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(pair.source);
                var target = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(pair.target);
                if (source == null || !source.IsUsable || target == null)
                    throw new InvalidOperationException("Source map or its compact clone is missing/unusable: " + pair.source);
                ValidateCompactMapCoordinates(source, target, compression);
                string imageFolder = Folder + "/Map/" + index.ToString("D2");
                Add(source.BaseMap, "BaseMap", -1, "", new Rect(0, 0, 1, 1));
                if (source.IllustratedMap != null) Add(source.IllustratedMap, "IllustratedMap", -1, "", new Rect(0, 0, 1, 1));
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var tiles = source.RegionTiles ?? Array.Empty<WorldMapRegionTile>();
                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    var tile = tiles[tileIndex];
                    if (tile == null || tile.Texture == null || string.IsNullOrWhiteSpace(tile.Id) || !ids.Add(tile.Id))
                        throw new InvalidOperationException("Original regional tiles are incomplete or have duplicate IDs: " + pair.source);
                    Add(tile.Texture, "Region", tileIndex, tile.Id, tile.WorldUv);
                }

                void Add(Texture2D image, string kind, int tileIndex, string tileId, Rect sourceUv)
                {
                    if (image == null) throw new InvalidOperationException("Map texture is missing: " + kind);
                    ValidateCompactMapUv(sourceUv, "Original " + kind + "/" + tileId);
                    string sourcePath = AssetDatabase.GetAssetPath(image);
                    if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                        throw new InvalidOperationException("Map image needs a persistent source asset: " + image.name);
                    var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                    if (importer == null) throw new InvalidOperationException("Expected imported map image: " + sourcePath);
                    // MiniPaper uses this exact prefix to enable explored terrain detail.
                    string name = kind == "Region" ? (image.name.StartsWith("MiniTerrain_", StringComparison.Ordinal) ? "MiniTerrain_" : "Region_") + SafeCompactMapName(tileId) : kind;
                    string destination = imageFolder + "/" + name + "_Compact.png";
                    RequireCompactMapDestination(destination);
                    if (File.Exists(destination) || AssetDatabase.LoadMainAssetAtPath(destination) != null)
                        throw new InvalidOperationException("Unclaimed map output already exists: " + destination);
                    Rect destinationUv = MapCompactMapCrop(sourceUv, source, target, compression);
                    // Keep source pixel dimensions, including high-resolution terrain tiles.
                    jobs.Add(new CompactMapTextureJob
                    {
                        sourceData = pair.source, targetData = pair.target, kind = kind, tileIndex = tileIndex, tileId = tileId,
                        sourceImage = sourcePath, sourceImageHash = Hash(sourcePath), sourceMetaHash = Hash(sourcePath + ".meta"),
                        destinationImage = destination, width = image.width, height = image.height,
                        sourceGraphicsFormat = image.graphicsFormat.ToString(), srgb = importer.sRGBTexture,
                        mipmaps = importer.mipmapEnabled, alphaIsTransparency = importer.alphaIsTransparency, filterMode = importer.filterMode,
                        sourceWorldUv = sourceUv, destinationWorldUv = destinationUv,
                        sourceMin = source.BoundsMin, sourceMax = source.BoundsMax,
                        destinationMin = target.BoundsMin, destinationMax = target.BoundsMax
                    });
                }
            }
            if (jobs.Select(j => j.destinationImage).Distinct(StringComparer.Ordinal).Count() != jobs.Count)
                throw new InvalidOperationException("Compact map output names collide after normalization.");
            return new CompactMapState
            {
                compressionHash = Hash(AssetDatabase.GetAssetPath(compression)), sourceSceneHash = progress.sourceHash, jobs = jobs.ToArray()
            };
        }

        static void BuildCompactMapTexture(CompactMapTextureJob job, WorldMacroCompressionMap mapping)
        {
            RequireCompactMapDestination(job.destinationImage);
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(job.sourceImage);
            if (source == null || source.width != job.width || source.height != job.height)
                throw new InvalidOperationException("Source image dimensions changed: " + job.sourceImage);
            Texture2D readable = null, result = null;
            try
            {
                // GPU copy makes an unreadable imported texture readable without changing its importer.
                readable = ReadCompactMapTexture(source);
                Color32[] sourcePixels = readable.GetPixels32();
                var destinationPixels = new Color32[checked(job.width * job.height)];
                double[] xs = CompactMapSampleAxis(job, mapping.XAxis, true);
                double[] ys = CompactMapSampleAxis(job, mapping.ZAxis, false);
                for (int y = 0; y < job.height; y++)
                    for (int x = 0; x < job.width; x++)
                        destinationPixels[y * job.width + x] = SampleCompactMapRgba(sourcePixels, source.width, source.height, xs[x], ys[y]);
                result = new Texture2D(job.width, job.height, TextureFormat.RGBA32, false, !job.srgb);
                result.SetPixels32(destinationPixels); result.Apply(false, false);
                EnsureCompactMapAssetFolder(Path.GetDirectoryName(job.destinationImage).Replace('\\', '/'));
                File.WriteAllBytes(job.destinationImage, result.EncodeToPNG());
                AssetDatabase.ImportAsset(job.destinationImage, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(job.destinationImage) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Compact map importer was not created: " + job.destinationImage);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = job.srgb;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = job.alphaIsTransparency;
                importer.mipmapEnabled = job.mipmaps; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = job.filterMode;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(job.width, job.height)));
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
                importer.SaveAndReimport();
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(job.destinationImage);
                if (imported == null || imported.width != job.width || imported.height != job.height)
                    throw new InvalidOperationException("Compact texture import reduced map resolution: " + job.destinationImage);
                job.destinationImageHash = Hash(job.destinationImage);
                job.destinationMetaHash = Hash(job.destinationImage + ".meta");
                VerifyCompactMapSource(job);
            }
            finally
            {
                if (readable != null) Object.DestroyImmediate(readable);
                if (result != null) Object.DestroyImmediate(result);
            }
        }

        static Texture2D ReadCompactMapTexture(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D copy = null;
            try
            {
                // Match the actual GPU source encoding, including Gamma projects.
                bool sourceGpuSrgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
                var descriptor = new RenderTextureDescriptor(source.width, source.height,
                    sourceGpuSrgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, 0)
                { msaaSamples = 1, useMipMap = false, autoGenerateMips = false };
                target = RenderTexture.GetTemporary(descriptor);
                target.filterMode = FilterMode.Point;
                GL.sRGBWrite = sourceGpuSrgb;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !sourceGpuSrgb);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); copy.Apply(false, false);
                return copy;
            }
            catch
            {
                if (copy != null) Object.DestroyImmediate(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgbWrite;
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        static double[] CompactMapSampleAxis(CompactMapTextureJob job, WorldMacroCompressionAxis axis, bool x)
        {
            int length = x ? job.width : job.height;
            var result = new double[length];
            double srcMin = x ? job.sourceMin.x : job.sourceMin.y, srcMax = x ? job.sourceMax.x : job.sourceMax.y;
            double dstMin = x ? job.destinationMin.x : job.destinationMin.y, dstMax = x ? job.destinationMax.x : job.destinationMax.y;
            double cropMin = x ? job.sourceWorldUv.x : job.sourceWorldUv.y, cropSize = x ? job.sourceWorldUv.width : job.sourceWorldUv.height;
            double dstCropMin = x ? job.destinationWorldUv.x : job.destinationWorldUv.y, dstCropSize = x ? job.destinationWorldUv.width : job.destinationWorldUv.height;
            for (int i = 0; i < length; i++)
            {
                double destinationUv = dstCropMin + (i + .5) / length * dstCropSize;
                double destinationWorld = dstMin + destinationUv * (dstMax - dstMin);
                double sourceWorld = axis.Inverse(destinationWorld);
                double oldFullUv = (sourceWorld - srcMin) / (srcMax - srcMin);
                double oldTextureUv = (oldFullUv - cropMin) / cropSize;
                if (double.IsNaN(oldTextureUv) || double.IsInfinity(oldTextureUv) || oldTextureUv < -.00001 || oldTextureUv > 1.00001)
                    throw new InvalidOperationException("Inverse map sample left its source texture crop: " + job.destinationImage);
                result[i] = oldTextureUv * length - .5;
                job.maximumInverseRoundtripErrorMetres = Math.Max(job.maximumInverseRoundtripErrorMetres, Math.Abs(axis.Forward(sourceWorld) - destinationWorld));
            }
            if (job.maximumInverseRoundtripErrorMetres > .001)
                throw new InvalidOperationException("Map inverse alignment exceeds one millimetre: " + job.destinationImage);
            return result;
        }

        static Color32 SampleCompactMapRgba(Color32[] pixels, int width, int height, double sourceX, double sourceY)
        {
            sourceX = Math.Max(0, Math.Min(width - 1, sourceX)); sourceY = Math.Max(0, Math.Min(height - 1, sourceY));
            int x = (int)sourceX, y = (int)sourceY, right = Math.Min(x + 1, width - 1), top = Math.Min(y + 1, height - 1);
            float tx = (float)(sourceX - x), ty = (float)(sourceY - y);
            Color32 a = pixels[y * width + x], b = pixels[y * width + right], c = pixels[top * width + x], d = pixels[top * width + right];
            return new Color32(Channel(a.r, b.r, c.r, d.r), Channel(a.g, b.g, c.g, d.g), Channel(a.b, b.b, c.b, d.b), Channel(a.a, b.a, c.a, d.a));
            byte Channel(byte av, byte bv, byte cv, byte dv) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(Mathf.Lerp(av, bv, tx), Mathf.Lerp(cv, dv, tx), ty)), 0, 255);
        }

        static Rect MapCompactMapCrop(Rect uv, WorldMapBakedDataSO source, WorldMapBakedDataSO target, WorldMacroCompressionMapSO compression)
        {
            Vector2 a = source.BoundsMin + Vector2.Scale(uv.min, source.BoundsMax - source.BoundsMin);
            Vector2 b = source.BoundsMin + Vector2.Scale(uv.max, source.BoundsMax - source.BoundsMin);
            Vector3 mappedA = compression.Map(new Vector3(a.x, 0, a.y)), mappedB = compression.Map(new Vector3(b.x, 0, b.y));
            Vector2 size = target.BoundsMax - target.BoundsMin;
            var mappedUv = Rect.MinMaxRect((mappedA.x - target.BoundsMin.x) / size.x, (mappedA.z - target.BoundsMin.y) / size.y,
                (mappedB.x - target.BoundsMin.x) / size.x, (mappedB.z - target.BoundsMin.y) / size.y);
            ValidateCompactMapUv(mappedUv, "Compact crop");
            // The compression is separable and monotonic, so mapped opposite corners
            // are the exact rectangular coverage of the original regional crop.
            return mappedUv;
        }

        static void FinishCompactMap(CompactMapState state, Progress progress, WorldMacroCompressionMapSO compression)
        {
            VerifyCompactMapInputsAndOutputs(state, progress);
            var alignments = new List<CompactMapAlignment>();
            var targets = state.jobs.Select(j => j.targetData).Distinct(StringComparer.Ordinal).ToArray();
            // Validate all vector geometry before binding any map family.
            foreach (string path in targets)
            {
                var job = state.jobs.First(j => j.targetData == path);
                ValidateCompactMapCoordinates(AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(job.sourceData),
                    AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(job.targetData), compression);
            }
            foreach (string path in targets)
            {
                var jobs = state.jobs.Where(j => j.targetData == path).ToArray();
                var source = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(jobs[0].sourceData);
                var target = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(path);
                var tiles = new WorldMapRegionTile[(source.RegionTiles ?? Array.Empty<WorldMapRegionTile>()).Length];
                foreach (var job in jobs)
                {
                    var image = AssetDatabase.LoadAssetAtPath<Texture2D>(job.destinationImage);
                    if (image == null) throw new InvalidOperationException("Completed compact map image is missing: " + job.destinationImage);
                    if (job.kind == "BaseMap") target.BaseMap = image;
                    else if (job.kind == "IllustratedMap") target.IllustratedMap = image;
                    else if (job.kind == "Region") tiles[job.tileIndex] = new WorldMapRegionTile { Id = job.tileId, Texture = image, WorldUv = job.destinationWorldUv };
                    else throw new InvalidOperationException("Unknown compact map job kind: " + job.kind);
                }
                if (source.IllustratedMap == null) target.IllustratedMap = null;
                if (tiles.Any(t => t == null || t.Texture == null)) throw new InvalidOperationException("Compact region tile coverage is incomplete.");
                target.RegionTiles = tiles;
                if (!target.IsUsable || target.HasIllustration != source.HasIllustration)
                    throw new InvalidOperationException("Compact map did not retain usable source presentation: " + path);
                for (int i = 0; i < tiles.Length; i++)
                    if (source.RegionTiles[i].Texture.name.StartsWith("MiniTerrain_", StringComparison.Ordinal) != tiles[i].Texture.name.StartsWith("MiniTerrain_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Regional detail shader prefix changed: " + tiles[i].Id);
                EditorUtility.SetDirty(target);
                var alignment = ValidateCompactMapCoordinates(source, target, compression);
                alignment.sourceData = jobs[0].sourceData; alignment.targetData = path;
                alignment.usable = target.IsUsable; alignment.hasIllustration = target.HasIllustration;
                alignment.regionTiles = tiles.Length; alignment.regionTileCountPreserved = tiles.Length == (source.RegionTiles?.Length ?? 0);
                alignment.miniDetailPrefixPreserved = true; alignments.Add(alignment);
            }
            // Persist only the owned compact map assets, not unrelated dirty assets.
            foreach (string path in targets) AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(path));
            foreach (var alignment in alignments) { alignment.sourceHash = Hash(alignment.sourceData); alignment.targetHash = Hash(alignment.targetData); }
            VerifyCompactMapInputsAndOutputs(state, progress);
            var report = new CompactMapReport
            {
                status = "COMPACT_MAP_COMPLETE", compressionHash = state.compressionHash, sourceSceneHash = state.sourceSceneHash,
                originalsUnchanged = true, method = state.method, discoveryPolicy = state.discoveryPolicy,
                textures = state.jobs, maps = alignments.ToArray()
            };
            WriteCompactMapJson(CompactMapReportPath, JsonUtility.ToJson(report, true));
            string finding = "COMPACT_MAP_COMPLETE: " + alignments.Count + " map data assets; " + state.jobs.Length + " independent textures; original maps and image hashes unchanged; map_alignment_report.json";
            if (progress.findings == null) progress.findings = new List<string>();
            if (!progress.findings.Contains(finding)) progress.findings.Add(finding);
            WriteProgress(progress);
        }

        static CompactMapAlignment ValidateCompactMapCoordinates(WorldMapBakedDataSO source, WorldMapBakedDataSO target, WorldMacroCompressionMapSO compression)
        {
            if (source == null || target == null) throw new InvalidOperationException("Missing source/target map data.");
            var report = new CompactMapAlignment();
            void Compare(Vector2 a, Vector2 b)
            {
                Vector3 mapped = compression.Map(new Vector3(a.x, 0, a.y));
                float error = Vector2.Distance(new Vector2(mapped.x, mapped.z), b);
                if (!float.IsFinite(error) || error > .003f) throw new InvalidOperationException("Compact map vector coordinates were not remapped consistently: " + target.name + " error=" + error);
                report.maximumCoordinateErrorMetres = Mathf.Max(report.maximumCoordinateErrorMetres, error); report.comparedCoordinates++;
            }
            void Points(Vector2[] a, Vector2[] b)
            {
                a = a ?? Array.Empty<Vector2>(); b = b ?? Array.Empty<Vector2>();
                if (a.Length != b.Length) throw new InvalidOperationException("Map point counts changed during coordinate conversion.");
                for (int i = 0; i < a.Length; i++) Compare(a[i], b[i]);
            }
            void Lines(WorldMapLineSpec[] a, WorldMapLineSpec[] b)
            {
                a = a ?? Array.Empty<WorldMapLineSpec>(); b = b ?? Array.Empty<WorldMapLineSpec>();
                if (a.Length != b.Length) throw new InvalidOperationException("Map line count changed.");
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] == null || b[i] == null) { if (a[i] != b[i]) throw new InvalidOperationException("Map line null state changed."); continue; }
                    if (a[i].Id != b[i].Id || a[i].Kind != b[i].Kind || a[i].PixelWidth != b[i].PixelWidth) throw new InvalidOperationException("Map line identity/style changed.");
                    Points(a[i].Points, b[i].Points);
                }
            }
            Compare(source.BoundsMin, target.BoundsMin); Compare(source.BoundsMax, target.BoundsMax);
            if (target.BoundsMax.x <= target.BoundsMin.x || target.BoundsMax.y <= target.BoundsMin.y) throw new InvalidOperationException("Compact map bounds are invalid.");
            Points(source.Outline, target.Outline); Lines(source.Lines, target.Lines);
            var aMarkers = source.Markers ?? Array.Empty<WorldMapMarkerSpec>(); var bMarkers = target.Markers ?? Array.Empty<WorldMapMarkerSpec>();
            if (aMarkers.Length != bMarkers.Length) throw new InvalidOperationException("Map marker count changed.");
            for (int i = 0; i < aMarkers.Length; i++)
            {
                var a = aMarkers[i]; var b = bMarkers[i];
                if (a == null || b == null) { if (a != b) throw new InvalidOperationException("Map marker null state changed."); continue; }
                if (a.Id != b.Id || a.Label != b.Label || a.Kind != b.Kind || a.CompletionId != b.CompletionId || a.ZoneId != b.ZoneId || a.InitiallyDiscovered != b.InitiallyDiscovered)
                    throw new InvalidOperationException("Map marker identity or discovery policy changed.");
                Compare(a.WorldXZ, b.WorldXZ);
            }
            var aZones = source.Zones ?? Array.Empty<WorldMapZoneSpec>(); var bZones = target.Zones ?? Array.Empty<WorldMapZoneSpec>();
            if (aZones.Length != bZones.Length) throw new InvalidOperationException("Map zone count changed.");
            for (int i = 0; i < aZones.Length; i++)
            {
                var a = aZones[i]; var b = bZones[i];
                if (a == null || b == null) { if (a != b) throw new InvalidOperationException("Map zone null state changed."); continue; }
                if (a.Id != b.Id || a.Label != b.Label || a.MinimumY != b.MinimumY || a.MaximumY != b.MaximumY) throw new InvalidOperationException("Map zone identity/height band changed under a Y-preserving map.");
                Points(a.Polygon, b.Polygon); Points(a.DetailPath, b.DetailPath); Lines(a.DetailLines, b.DetailLines);
            }
            report.outlinePoints = source.Outline?.Length ?? 0; report.markers = aMarkers.Length;
            report.lines = source.Lines?.Length ?? 0; report.zones = aZones.Length;
            return report;
        }

        static void VerifyCompactMapInputsAndOutputs(CompactMapState state, Progress progress)
        {
            if (!File.Exists(SourceScene) || Hash(SourceScene) != state.sourceSceneHash)
                throw new InvalidOperationException("Original source scene changed after compact preparation.");
            foreach (var job in state.jobs)
            {
                VerifyCompactMapSource(job);
                var pair = (progress.assets ?? Array.Empty<AssetPair>()).FirstOrDefault(p => p.source == job.sourceData && p.target == job.targetData);
                if (pair == null || Hash(pair.source) != pair.hash) throw new InvalidOperationException("Original map asset changed or disappeared from clone manifest: " + job.sourceData);
                if (job.completed && (!File.Exists(job.destinationImage) || Hash(job.destinationImage) != job.destinationImageHash ||
                    !File.Exists(job.destinationImage + ".meta") || Hash(job.destinationImage + ".meta") != job.destinationMetaHash))
                    throw new InvalidOperationException("Completed compact map image/import settings changed: " + job.destinationImage);
            }
        }

        static void VerifyCompactMapSource(CompactMapTextureJob job)
        {
            if (!File.Exists(job.sourceImage) || Hash(job.sourceImage) != job.sourceImageHash ||
                !File.Exists(job.sourceImage + ".meta") || Hash(job.sourceImage + ".meta") != job.sourceMetaHash)
                throw new InvalidOperationException("Original map image or importer changed: " + job.sourceImage);
        }

        static void ValidateCompactMapUv(Rect uv, string label)
        {
            if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || !float.IsFinite(uv.width) || !float.IsFinite(uv.height) ||
                uv.width <= 0 || uv.height <= 0 || uv.xMin < -.00001f || uv.yMin < -.00001f || uv.xMax > 1.00001f || uv.yMax > 1.00001f)
                throw new InvalidOperationException(label + " UV must be a finite positive rectangle within the full map.");
        }

        static string SafeCompactMapName(string value)
        {
            var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            return new string(chars);
        }

        static void RequireCompactMapDestination(string path)
        {
            string compactRoot = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string absolute = Path.GetFullPath(path);
            if (!absolute.StartsWith(compactRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Map writes must stay inside the compact asset folder: " + path);
        }

        static void EnsureCompactMapAssetFolder(string folder)
        {
            RequireCompactMapDestination(folder + "/placeholder");
            string current = "Assets";
            foreach (string bit in folder.Split('/').Skip(1))
            {
                string next = current + "/" + bit;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, bit);
                current = next;
            }
        }

        static void WriteCompactMapState(CompactMapState state) => WriteCompactMapJson(CompactMapStatePath, JsonUtility.ToJson(state, true));
        static void WriteCompactMapJson(string path, string json)
        {
            string allowed = Path.GetFullPath(Output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Map manifest path escaped the compact report folder.");
            Directory.CreateDirectory(Output);
            string temporary = path + ".tmp"; File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }

        static string CompactMapStatusJson(CompactMapState state) => JsonUtility.ToJson(new CompactMapStatus
        {
            phase = state.phase, completedTextures = state.jobs.Count(j => j.completed), totalTextures = state.jobs.Length,
            nextImage = state.jobs.FirstOrDefault(j => !j.completed)?.destinationImage,
            manifest = CompactMapStatePath, alignmentReport = state.phase == "COMPLETE" ? CompactMapReportPath : null
        }, true);
    }
}
