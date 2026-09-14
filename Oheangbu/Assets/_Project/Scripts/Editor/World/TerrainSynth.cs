using System;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §4 층 3 · §7 ④] 지형 합성 — 순수 C#. System.Random(seed)만 쓴다(UnityEngine.Random/Time 금지 · §6 A11 결정론).
    // 순서 = RidgedFbm + 도메인 워프 + valleyProfile → 열 침식(마스크 제외) → 림·경계 띠(≥boundarySlopeDeg) → P2 시드 스탬프(yaw 0).
    // 수치는 전부 AreaSheetSO·TerrainSynthSO·WorldScaleSO에서 온다 — 이 파일의 숫자 리터럴은 인덱스·0·0.5·1·2뿐(§6 A9).
    // 결과 = Terrain 정규화 높이 [0,1] · 인덱스 [z, x](TerrainData.SetHeights 규약) · 크기 = heightmapRes². 절대 m = 값 × tile.heightM.
    // 브러시 손질 없음(D8): 같은 시트·시드 → 같은 float[,] → 같은 HeightsHash.
    public static class TerrainSynth
    {
        // 스파이크 P2 골짜기 heightfield 스탬프(D1) — MineExit 기준 로컬 XZ 사각형, 값 = MineExit 접지 대비 상대 m. 행 = z · 열 = x.
        public sealed class SpikeStamp
        {
            public float[,] RelativeHeightsM;
            public Vector2 LocalMinXZ;
            public Vector2 SizeXZ;
        }

        // 감사용 상세 결과 — 마스크는 §6 A2(경계 띠)·C3(절벽 셀) 대조 입력.
        public sealed class SynthResult
        {
            public float[,] Heights;          // 정규화 [0,1]
            public bool[,] ErosionMask;       // true = 침식 제외 셀(경계 띠·림·절벽 예정·랜드마크 발자국)
            public bool[,] CliffMask;         // true = 골짜기 벽 절벽 예정 셀(경사 ≥ cliffSlopeDeg)
            public float MinHeightM;
            public float MaxHeightM;
            public float CellSizeM;
        }

        private const string MineExitPoiId = "MineExit";
        private const int PermSize = 256;

        // 값 노이즈 격자 — 순열표는 System.Random(seed)로 1회 셔플(결정론). 옥타브별 오프셋도 같은 난수열에서 뽑는다.
        private sealed class NoiseField
        {
            private readonly int[] _perm;
            private readonly float[] _offsetX;
            private readonly float[] _offsetZ;

            public NoiseField(System.Random rng, int octaves, float offsetRange)
            {
                _perm = new int[PermSize * 2];
                var basePerm = new int[PermSize];
                for (int i = 0; i < PermSize; i++)
                {
                    basePerm[i] = i;
                }
                for (int i = PermSize - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int tmp = basePerm[i];
                    basePerm[i] = basePerm[j];
                    basePerm[j] = tmp;
                }
                for (int i = 0; i < PermSize * 2; i++)
                {
                    _perm[i] = basePerm[i % PermSize];
                }
                _offsetX = new float[Math.Max(1, octaves)];
                _offsetZ = new float[Math.Max(1, octaves)];
                for (int o = 0; o < _offsetX.Length; o++)
                {
                    _offsetX[o] = (float)rng.NextDouble() * offsetRange;
                    _offsetZ[o] = (float)rng.NextDouble() * offsetRange;
                }
            }

            public float OffsetX(int octave) => _offsetX[octave % _offsetX.Length];
            public float OffsetZ(int octave) => _offsetZ[octave % _offsetZ.Length];

            // 격자점 해시 → [0,1)
            private float Lattice(int ix, int iz)
            {
                int h = _perm[(_perm[ix & (PermSize - 1)] + iz) & (PermSize - 1)];
                return (float)h / PermSize;
            }

            // 2D 값 노이즈 [0,1] — smoothstep 보간.
            public float Value(float x, float z)
            {
                int ix = FloorToInt(x);
                int iz = FloorToInt(z);
                float fx = x - ix;
                float fz = z - iz;
                float sx = SmoothStep01(fx);
                float sz = SmoothStep01(fz);
                float a = Lattice(ix, iz);
                float b = Lattice(ix + 1, iz);
                float c = Lattice(ix, iz + 1);
                float d = Lattice(ix + 1, iz + 1);
                return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sz);
            }
        }

        // ---- 진입점 ----

        // 스파이크 스탬프 없음 · 림 표본 = 시트 길 knots(+눈높이).
        public static float[,] Generate(AreaSheetSO sheet, TerrainSynthSO synth, WorldScaleSO scale)
        {
            return GenerateDetailed(sheet, synth, scale, null, null).Heights;
        }

        public static float[,] Generate(AreaSheetSO sheet, TerrainSynthSO synth, WorldScaleSO scale, SpikeStamp stamp)
        {
            return GenerateDetailed(sheet, synth, scale, stamp, null).Heights;
        }

        // rimSamples = 림 앙각(rimElevationMaxDeg) 평가 시점(절대 좌표, y = 지면) — null이면 시트 길 knots 전부.
        public static SynthResult GenerateDetailed(AreaSheetSO sheet, TerrainSynthSO synth, WorldScaleSO scale,
            SpikeStamp stamp, Vector3[] rimSamples)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (synth == null) throw new ArgumentNullException(nameof(synth));
            if (scale == null) throw new ArgumentNullException(nameof(scale));
            if (sheet.Tiles == null || sheet.Tiles.Length == 0) throw new InvalidOperationException("AreaSheetSO.tiles 비어 있음");

            var tile = sheet.Tiles[0];
            int res = tile.heightmapRes;
            if (res < 2) throw new InvalidOperationException("heightmapRes < 2");
            float size = tile.sizeM;
            float cell = size / (res - 1);
            float originX = tile.originXZ.x;
            float originZ = tile.originXZ.y;

            var rng = new System.Random(synth.Seed);
            int octaves = Math.Max(1, synth.RidgedOctaves);
            var macroNoise = new NoiseField(rng, octaves, size);
            var warpNoise = new NoiseField(rng, 2, size);

            var heights = new float[res, res];
            var cliffMask = new bool[res, res];
            var erosionMask = new bool[res, res];

            // ① 매크로(Ridged Fbm + 도메인 워프) + 골짜기 프로필
            Vector3[] axis = synth.ValleyAxisKnots ?? new Vector3[0];
            float halfW = Math.Max(cell, synth.ValleyHalfWidthM);
            for (int iz = 0; iz < res; iz++)
            {
                float wz = originZ + iz * cell;
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = originX + ix * cell;
                    float macro = synth.ValleyFloorY + (RidgedFbm(macroNoise, warpNoise, wx, wz, synth, octaves) * 2f - 1f) * synth.MacroAmplitude;
                    heights[iz, ix] = ValleyProfile(axis, halfW, wx, wz, macro, synth.ValleyFloorY);
                }
            }

            // ② 절벽 예정 셀(골짜기 벽 대역 안 경사 ≥ cliffSlopeDeg) — 침식 제외 · 절벽 키트 매입 후보(§7 ⑦)
            float cliffTan = (float)Math.Tan(scale.CliffSlopeDeg * Mathf.Deg2Rad);
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    if (SlopeTan(heights, ix, iz, res, cell) >= cliffTan)
                    {
                        cliffMask[iz, ix] = true;
                        erosionMask[iz, ix] = true;
                    }
                }
            }

            // ③ 경계 띠·림 + 랜드마크 발자국 → 침식 마스크
            float band = scale.BoundaryBandM;
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    if (EdgeDistance(ix, iz, res) * cell < band) erosionMask[iz, ix] = true;
                }
            }
            MarkLandmarkFootprints(sheet, erosionMask, res, cell, originX, originZ);

            // ④ 열 침식(마스크 제외) — 마스크 셀은 침식 전 값을 그대로 보존한다
            var preErosion = (float[,])heights.Clone();
            if (synth.ThermalIterations > 0)
            {
                ThermalErosion(heights, synth.ErosionMaskEnabled ? erosionMask : null, synth.ThermalIterations, synth.TalusDeg, cell);
                if (synth.ErosionMaskEnabled)
                {
                    for (int iz = 0; iz < res; iz++)
                    {
                        for (int ix = 0; ix < res; ix++)
                        {
                            if (erosionMask[iz, ix]) heights[iz, ix] = preErosion[iz, ix];
                        }
                    }
                }
            }

            // ⑤ 림(앙각 ≤ rimElevationMaxDeg 파생) + 경계 띠(림 안쪽 사면 ≥ boundarySlopeDeg)
            Vector3[] samples = rimSamples ?? CollectPathKnots(sheet);
            ApplyRimAndBoundary(heights, res, cell, originX, originZ, samples, scale);

            // ⑥ 스파이크 P2 시드 스탬프(yaw = synth.SpikeSeedYawDeg — 0 고정, D1)
            if (synth.SpikeSeedImport && stamp != null && stamp.RelativeHeightsM != null)
            {
                var poi = sheet.FindPoi(MineExitPoiId);
                if (poi != null) ApplySpikeStamp(heights, res, cell, originX, originZ, stamp, poi.position, synth.SpikeSeedYawDeg, synth.SpikeSeedBlendM);
            }

            // ⑦ 정규화 [0,1]
            float minH = float.MaxValue;
            float maxH = float.MinValue;
            float heightRange = Math.Max(cell, tile.heightM);
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float h = heights[iz, ix];
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    heights[iz, ix] = Clamp01(h / heightRange);
                }
            }

            return new SynthResult
            {
                Heights = heights,
                ErosionMask = erosionMask,
                CliffMask = cliffMask,
                MinHeightM = minH,
                MaxHeightM = maxH,
                CellSizeM = cell,
            };
        }

        // SHA-256 hex — float[,] 원시 바이트(행 우선). §6 A11 heightsHash · WorldBuildStamp 입력.
        public static string HeightsHash(float[,] heights)
        {
            if (heights == null) return "";
            int rows = heights.GetLength(0);
            int cols = heights.GetLength(1);
            var bytes = new byte[rows * cols * sizeof(float)];
            Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        public static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        // ---- 합성 요소 ----

        // Ridged Fbm [0,1] — 파장 macroWavelengthM 기준, 옥타브마다 lacunarity·gain. 도메인 워프 = 파장 × domainWarp.
        private static float RidgedFbm(NoiseField noise, NoiseField warp, float x, float z, TerrainSynthSO synth, int octaves)
        {
            float wavelength = Math.Max(Mathf.Epsilon, synth.MacroWavelengthM);
            float baseFreq = 1f / wavelength;
            float warpAmp = synth.DomainWarp * wavelength;
            float wx = x + (warp.Value(x * baseFreq + warp.OffsetX(0), z * baseFreq + warp.OffsetZ(0)) * 2f - 1f) * warpAmp;
            float wz = z + (warp.Value(x * baseFreq + warp.OffsetX(1), z * baseFreq + warp.OffsetZ(1)) * 2f - 1f) * warpAmp;

            float sum = 0f;
            float norm = 0f;
            float amp = 1f;
            float freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                float n = noise.Value(wx * freq + noise.OffsetX(o), wz * freq + noise.OffsetZ(o));
                float ridge = 1f - Math.Abs(n * 2f - 1f);
                ridge *= ridge;
                sum += ridge * amp;
                norm += amp;
                amp *= synth.Gain;
                freq *= synth.Lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        // 골짜기 프로필 — 축까지 거리 d: d ≤ halfW = 바닥(축 보간 y) · halfW~2·halfW = smoothstep 벽 · 밖 = 매크로.
        private static float ValleyProfile(Vector3[] axis, float halfW, float x, float z, float macro, float floorY)
        {
            if (axis == null || axis.Length < 2) return macro;
            float axisY;
            float d = DistanceToPolylineXZ(axis, x, z, out axisY);
            float wallTop = Math.Max(macro, axisY);
            if (d <= halfW) return axisY;
            float wallBand = halfW * 2f;
            if (d >= wallBand) return macro;
            float t = SmoothStep01((d - halfW) / halfW);
            return Lerp(axisY, wallTop, t);
        }

        // 열 침식 — 4이웃, 안식각(talusDeg) 초과분의 절반을 가장 낮은 이웃으로. 델타는 전수 계산 후 일괄 적용(순서 무관 = 결정론).
        private static void ThermalErosion(float[,] h, bool[,] mask, int iterations, float talusDeg, float cell)
        {
            int res = h.GetLength(0);
            float talus = (float)Math.Tan(talusDeg * Mathf.Deg2Rad) * cell;
            var delta = new float[res, res];
            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            for (int it = 0; it < iterations; it++)
            {
                Array.Clear(delta, 0, delta.Length);
                for (int iz = 0; iz < res; iz++)
                {
                    for (int ix = 0; ix < res; ix++)
                    {
                        if (mask != null && mask[iz, ix]) continue;
                        float center = h[iz, ix];
                        float bestDiff = 0f;
                        int bestX = -1;
                        int bestZ = -1;
                        for (int k = 0; k < dx.Length; k++)
                        {
                            int nx = ix + dx[k];
                            int nz = iz + dz[k];
                            if (nx < 0 || nz < 0 || nx >= res || nz >= res) continue;
                            if (mask != null && mask[nz, nx]) continue;
                            float diff = center - h[nz, nx];
                            if (diff > bestDiff)
                            {
                                bestDiff = diff;
                                bestX = nx;
                                bestZ = nz;
                            }
                        }
                        if (bestX < 0 || bestDiff <= talus) continue;
                        float move = (bestDiff - talus) * 0.5f;
                        delta[iz, ix] -= move;
                        delta[bestZ, bestX] += move;
                    }
                }
                for (int iz = 0; iz < res; iz++)
                {
                    for (int ix = 0; ix < res; ix++)
                    {
                        h[iz, ix] += delta[iz, ix];
                    }
                }
            }
        }

        // 림 + 경계 띠: 4변 각 가장자리 셀의 림 높이 = min_샘플(샘플 y + 눈높이 + 거리·tan(rimElevationMaxDeg)) — 「림 높이는 값이 아니라 파생」.
        // 띠 안(가장자리에서 d < boundaryBandM): h = max(h, rimY − d·tan(boundarySlopeDeg)) — 보이지 않는 벽 대신 경사 띠(림 안쪽 사면).
        private static void ApplyRimAndBoundary(float[,] h, int res, float cell, float originX, float originZ, Vector3[] samples, WorldScaleSO scale)
        {
            float rimTan = (float)Math.Tan(scale.RimElevationMaxDeg * Mathf.Deg2Rad);
            float bandTan = (float)Math.Tan(scale.BoundarySlopeDeg * Mathf.Deg2Rad);
            float band = scale.BoundaryBandM;
            float eye = scale.EyeHeight;
            int last = res - 1;

            // 4변 림 높이 — [0]=남(z=0) [1]=북(z=last) [2]=서(x=0) [3]=동(x=last)
            var rimY = new float[4, res];
            for (int i = 0; i < res; i++)
            {
                float t = i * cell;
                rimY[0, i] = RimHeightAt(originX + t, originZ, samples, rimTan, eye);
                rimY[1, i] = RimHeightAt(originX + t, originZ + last * cell, samples, rimTan, eye);
                rimY[2, i] = RimHeightAt(originX, originZ + t, samples, rimTan, eye);
                rimY[3, i] = RimHeightAt(originX + last * cell, originZ + t, samples, rimTan, eye);
            }

            int bandCells = (int)Math.Ceiling(band / cell);
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    int dEdge = EdgeDistance(ix, iz, res);
                    if (dEdge > bandCells) continue;
                    float dM = dEdge * cell;
                    // 가장 가까운 변의 림 높이(모서리는 둘 중 높은 쪽)
                    float rim = float.MinValue;
                    if (iz == dEdge) rim = Math.Max(rim, rimY[0, ix]);
                    if (last - iz == dEdge) rim = Math.Max(rim, rimY[1, ix]);
                    if (ix == dEdge) rim = Math.Max(rim, rimY[2, iz]);
                    if (last - ix == dEdge) rim = Math.Max(rim, rimY[3, iz]);
                    if (rim == float.MinValue) continue;
                    float target = rim - dM * bandTan;
                    if (target > h[iz, ix]) h[iz, ix] = target;
                }
            }
        }

        private static float RimHeightAt(float x, float z, Vector3[] samples, float rimTan, float eye)
        {
            if (samples == null || samples.Length == 0) return float.MinValue;
            float best = float.MaxValue;
            for (int i = 0; i < samples.Length; i++)
            {
                float dx = x - samples[i].x;
                float dz = z - samples[i].z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                float allowed = samples[i].y + eye + dist * rimTan;
                if (allowed < best) best = allowed;
            }
            return best;
        }

        // 스탬프 — 로컬 사각형을 MineExit 기준으로(yaw 회전) 놓고, 가장자리 blendM 안에서 smoothstep 혼합.
        private static void ApplySpikeStamp(float[,] h, int res, float cell, float originX, float originZ,
            SpikeStamp stamp, Vector3 anchor, float yawDeg, float blendM)
        {
            int sRows = stamp.RelativeHeightsM.GetLength(0);
            int sCols = stamp.RelativeHeightsM.GetLength(1);
            if (sRows < 2 || sCols < 2 || stamp.SizeXZ.x <= 0f || stamp.SizeXZ.y <= 0f) return;
            float cosY = (float)Math.Cos(yawDeg * Mathf.Deg2Rad);
            float sinY = (float)Math.Sin(yawDeg * Mathf.Deg2Rad);
            float blend = Math.Max(cell, blendM);
            for (int iz = 0; iz < res; iz++)
            {
                float wz = originZ + iz * cell - anchor.z;
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = originX + ix * cell - anchor.x;
                    // 월드 → 스탬프 로컬(yaw 역회전)
                    float lx = wx * cosY - wz * sinY;
                    float lz = wx * sinY + wz * cosY;
                    float u = (lx - stamp.LocalMinXZ.x) / stamp.SizeXZ.x;
                    float v = (lz - stamp.LocalMinXZ.y) / stamp.SizeXZ.y;
                    if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                    float insideX = Math.Min(lx - stamp.LocalMinXZ.x, stamp.LocalMinXZ.x + stamp.SizeXZ.x - lx);
                    float insideZ = Math.Min(lz - stamp.LocalMinXZ.y, stamp.LocalMinXZ.y + stamp.SizeXZ.y - lz);
                    float w = SmoothStep01(Clamp01(Math.Min(insideX, insideZ) / blend));
                    float stampY = anchor.y + Bilinear(stamp.RelativeHeightsM, u, v);
                    h[iz, ix] = Lerp(h[iz, ix], stampY, w);
                }
            }
        }

        private static void MarkLandmarkFootprints(AreaSheetSO sheet, bool[,] mask, int res, float cell, float originX, float originZ)
        {
            var landmarks = sheet.Landmarks;
            if (landmarks == null) return;
            foreach (var placement in landmarks)
            {
                if (placement == null || placement.landmark == null) continue;
                var fp = placement.landmark.Footprint;
                if (fp == null || fp.Length < 3) continue;
                // 발자국을 yaw 회전 + 위치 이동한 월드 다각형으로
                float cosY = (float)Math.Cos(placement.yawDeg * Mathf.Deg2Rad);
                float sinY = (float)Math.Sin(placement.yawDeg * Mathf.Deg2Rad);
                var poly = new Vector2[fp.Length];
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i < fp.Length; i++)
                {
                    float px = fp[i].x * cosY + fp[i].y * sinY + placement.position.x;
                    float pz = -fp[i].x * sinY + fp[i].y * cosY + placement.position.z;
                    poly[i] = new Vector2(px, pz);
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (pz < minZ) minZ = pz;
                    if (pz > maxZ) maxZ = pz;
                }
                int ix0 = Math.Max(0, FloorToInt((minX - originX) / cell));
                int ix1 = Math.Min(res - 1, (int)Math.Ceiling((maxX - originX) / cell));
                int iz0 = Math.Max(0, FloorToInt((minZ - originZ) / cell));
                int iz1 = Math.Min(res - 1, (int)Math.Ceiling((maxZ - originZ) / cell));
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        if (PointInPolygon(poly, originX + ix * cell, originZ + iz * cell)) mask[iz, ix] = true;
                    }
                }
            }
        }

        private static Vector3[] CollectPathKnots(AreaSheetSO sheet)
        {
            int count = 0;
            var paths = sheet.Paths;
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    if (p != null && p.knots != null) count += p.knots.Length;
                }
            }
            var result = new Vector3[count];
            int n = 0;
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    if (p == null || p.knots == null) continue;
                    Array.Copy(p.knots, 0, result, n, p.knots.Length);
                    n += p.knots.Length;
                }
            }
            return result;
        }

        // ---- 기하 보조 ----

        // 경사 tan(중앙 차분 · 가장자리는 전진/후진 차분)
        private static float SlopeTan(float[,] h, int ix, int iz, int res, float cell)
        {
            int xa = Math.Max(0, ix - 1);
            int xb = Math.Min(res - 1, ix + 1);
            int za = Math.Max(0, iz - 1);
            int zb = Math.Min(res - 1, iz + 1);
            float gx = (h[iz, xb] - h[iz, xa]) / ((xb - xa) * cell);
            float gz = (h[zb, ix] - h[za, ix]) / ((zb - za) * cell);
            return (float)Math.Sqrt(gx * gx + gz * gz);
        }

        private static int EdgeDistance(int ix, int iz, int res)
        {
            int last = res - 1;
            return Math.Min(Math.Min(ix, last - ix), Math.Min(iz, last - iz));
        }

        // XZ 폴리라인까지 최단 거리 + 그 지점의 보간 y.
        public static float DistanceToPolylineXZ(Vector3[] knots, float x, float z, out float yAtNearest)
        {
            float best = float.MaxValue;
            yAtNearest = knots.Length > 0 ? knots[0].y : 0f;
            for (int i = 0; i + 1 < knots.Length; i++)
            {
                Vector3 a = knots[i];
                Vector3 b = knots[i + 1];
                float abx = b.x - a.x;
                float abz = b.z - a.z;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 0f ? ((x - a.x) * abx + (z - a.z) * abz) / len2 : 0f;
                t = Clamp01(t);
                float px = a.x + abx * t;
                float pz = a.z + abz * t;
                float dx = x - px;
                float dz = z - pz;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d < best)
                {
                    best = d;
                    yAtNearest = Lerp(a.y, b.y, t);
                }
            }
            return best;
        }

        public static bool PointInPolygon(Vector2[] poly, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                bool cross = (poly[i].y > z) != (poly[j].y > z);
                if (!cross) continue;
                float xAt = (poly[j].x - poly[i].x) * (z - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x;
                if (x < xAt) inside = !inside;
            }
            return inside;
        }

        private static float Bilinear(float[,] grid, float u, float v)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            float fx = u * (cols - 1);
            float fz = v * (rows - 1);
            int x0 = Math.Min(cols - 2, Math.Max(0, FloorToInt(fx)));
            int z0 = Math.Min(rows - 2, Math.Max(0, FloorToInt(fz)));
            float tx = Clamp01(fx - x0);
            float tz = Clamp01(fz - z0);
            float a = Lerp(grid[z0, x0], grid[z0, x0 + 1], tx);
            float b = Lerp(grid[z0 + 1, x0], grid[z0 + 1, x0 + 1], tx);
            return Lerp(a, b, tz);
        }

        private static int FloorToInt(float v)
        {
            return (int)Math.Floor(v);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        // Hermite smoothstep(0,1) = t²·(3 − 2t) — 3은 다항식 계수(미감 아님).
        private static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            return t * t * (3 - 2f * t);
        }
    }
}
