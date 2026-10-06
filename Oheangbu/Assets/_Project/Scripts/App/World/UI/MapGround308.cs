using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 map 3c (SPEC-MAP-OVERHAUL-308, "표식이 뜨는 때"): does the #308 paper DRAW the ground round a point as walked
    /// land? Pure functions over the fog texels the paper shader samples (WorldMapPresenter.fogPixels: alpha 0 = walked, point
    /// sampled, clamped). No state, no Unity object, nothing static but the functions.
    ///
    /// The shader (MapFog308.cginc MapFog308_Edge) draws a pixel as walked land where  field = land - theta > 0:
    ///   land  = min(depth, KCOVER x cover)   - a function of the nine fog cells round the pixel and nothing else (below, line for line)
    ///   theta = how deep inside the walked cells the crisp edge lies there: two sine waves pushed by a hash noise. Its VALUE
    ///           differs per GPU (the hash), but it is never above FROM + SPAN cells (+ the bites' share when MAPFOG308_E_DEEP is 1).
    /// So  land >= needLand  with  needLand = that upper bound + a margin  means: walked land on every GPU, at every zoom whose
    /// cells are wider than FLOOR / needLand px. The bound and the margin are data (MapNotation308SO.GroundNeedLand, baked from
    /// the cginc's own #define lines); the editor check compares them with the live cginc.
    ///
    /// Coordinates are the SHADER's: c = worldUv x (fog width, fog height). On the compact map a fog row is therefore drawn
    /// 6000 / 188 = 31.915 m high although the discovery grid's cells are 32 m (known since #304): the caller passes cells per
    /// metre for each axis, it does not divide by the cell size.</summary>
    public static class MapGround308
    {
        /// <summary>Most lattice steps across the radius (the sample count stays under ~30,000 whatever the data says).</summary>
        public const int MaxSteps = 96;

        /// <summary>1 = walked. The fog texture is point sampled and clamped: a cell off the grid reads its nearest edge cell.</summary>
        static float Walked(Color32[] fog, int width, int height, int x, int y)
        {
            if (x < 0) x = 0; else if (x >= width) x = width - 1;
            if (y < 0) y = 0; else if (y >= height) y = height - 1;
            return fog[y * width + x].a == 0 ? 1f : 0f;
        }

        /// <summary>`land` of MapFog308_Edge at fog coordinate (cx, cy) - the shader's `c`. Statement by statement:
        ///   cell=floor(c); f=c-cell; the nine cells w00..w22; k00..k11 = min of four; cover=lerp(lerp(k00,k10,f.x),lerp(k01,k11,f.x),f.y);
        ///   a0=.5*(1-f)*(1-f); a2=.5*f*f; a1=1-a0-a2; bs=...; depth=1-sqrt(max(2*(1-bs),0)); land=min(depth,KCOVER*cover);</summary>
        public static float Land(Color32[] fog, int width, int height, float cx, float cy, float kCover)
        {
            int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy);
            float fx = cx - ix, fy = cy - iy;
            float w00 = Walked(fog, width, height, ix - 1, iy - 1), w10 = Walked(fog, width, height, ix, iy - 1), w20 = Walked(fog, width, height, ix + 1, iy - 1);
            float w01 = Walked(fog, width, height, ix - 1, iy), w11 = Walked(fog, width, height, ix, iy), w21 = Walked(fog, width, height, ix + 1, iy);
            float w02 = Walked(fog, width, height, ix - 1, iy + 1), w12 = Walked(fog, width, height, ix, iy + 1), w22 = Walked(fog, width, height, ix + 1, iy + 1);
            float k00 = Mathf.Min(Mathf.Min(w00, w10), Mathf.Min(w01, w11));
            float k10 = Mathf.Min(Mathf.Min(w10, w20), Mathf.Min(w11, w21));
            float k01 = Mathf.Min(Mathf.Min(w01, w11), Mathf.Min(w02, w12));
            float k11 = Mathf.Min(Mathf.Min(w11, w21), Mathf.Min(w12, w22));
            float south = k00 + (k10 - k00) * fx, north = k01 + (k11 - k01) * fx;
            float cover = south + (north - south) * fy;
            float a0x = .5f * (1f - fx) * (1f - fx), a2x = .5f * fx * fx, a1x = 1f - a0x - a2x;
            float a0y = .5f * (1f - fy) * (1f - fy), a2y = .5f * fy * fy, a1y = 1f - a0y - a2y;
            float bs = (w00 * a0x + w10 * a1x + w20 * a2x) * a0y + (w01 * a0x + w11 * a1x + w21 * a2x) * a1y + (w02 * a0x + w12 * a1x + w22 * a2x) * a2y;
            float depth = 1f - Mathf.Sqrt(Mathf.Max(2f * (1f - bs), 0f));
            return Mathf.Min(depth, kCover * cover);
        }

        /// <summary>The least `land` over the disc of radiusMetres round fog coordinate (cx, cy): the anchor, then points every
        /// stepMetres of arc on the rim (a ground that is not drawn yet fails there first), then a square lattice of stepMetres
        /// inside. Points off the fog (outside the world) are skipped: there is no land there to draw. Returns early with the
        /// first value under stopBelow (pass float.NegativeInfinity for the true minimum). NaN in, or no fog: -1.</summary>
        public static float MinLand(Color32[] fog, int width, int height, float cx, float cy, float cellsPerMetreX, float cellsPerMetreY,
            float radiusMetres, float stepMetres, float kCover, float stopBelow)
        {
            if (fog == null || width <= 0 || height <= 0 || fog.Length < width * height) return -1f;
            if (float.IsNaN(cx) || float.IsNaN(cy) || cx < 0f || cy < 0f || cx >= width || cy >= height) return -1f;   // the anchor itself is off the world
            float least = Land(fog, width, height, cx, cy, kCover);
            if (least < stopBelow || !(radiusMetres > 0f)) return least;
            float step = stepMetres > 0f ? stepMetres : radiusMetres;
            int n = Mathf.FloorToInt(radiusMetres / step);
            if (n > MaxSteps) { n = MaxSteps; step = radiusMetres / n; }
            int ring = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * radiusMetres / step));
            for (int i = 0; i < ring; i++)
            {
                float a = 2f * Mathf.PI * i / ring;
                float x = cx + radiusMetres * Mathf.Cos(a) * cellsPerMetreX, y = cy + radiusMetres * Mathf.Sin(a) * cellsPerMetreY;
                if (x < 0f || y < 0f || x >= width || y >= height) continue;
                float land = Land(fog, width, height, x, y, kCover);
                if (land < least) { least = land; if (least < stopBelow) return least; }
            }
            float r2 = radiusMetres * radiusMetres;
            for (int j = -n; j <= n; j++)
                for (int i = -n; i <= n; i++)
                {
                    if (i == 0 && j == 0) continue;
                    float dx = i * step, dy = j * step;
                    if (dx * dx + dy * dy > r2) continue;
                    float x = cx + dx * cellsPerMetreX, y = cy + dy * cellsPerMetreY;
                    if (x < 0f || y < 0f || x >= width || y >= height) continue;
                    float land = Land(fog, width, height, x, y, kCover);
                    if (land < least) { least = land; if (least < stopBelow) return least; }
                }
            return least;
        }

        /// <summary>The rule: every point of the disc reads land >= needLand. A threshold that is not a positive number never
        /// passes (a broken notation value hides the marker, it does not show it early).</summary>
        public static bool Drawn(Color32[] fog, int width, int height, float cx, float cy, float cellsPerMetreX, float cellsPerMetreY,
            float radiusMetres, float stepMetres, float needLand, float kCover)
        {
            if (!(needLand > 0f) || !(kCover > 0f)) return false;
            return MinLand(fog, width, height, cx, cy, cellsPerMetreX, cellsPerMetreY, radiusMetres, stepMetres, kCover, needLand) >= needLand;
        }
    }
}
