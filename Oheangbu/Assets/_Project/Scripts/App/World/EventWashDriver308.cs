using Oheangbu.BrushRender;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>#308 권역 담채 driver (SPEC-EVENT-WASH-308 §6, D308-6 / D308-6b, TEST). Scene place: WorldMacro_Playtest/EventWash308.
    /// Every PollSeconds (and on the session's InteractionResolved) it re-reads which campaign stages are available and fades their
    /// catalog places in/out; each frame with something moving it writes the shader globals InkWash297 (surface term + progress
    /// volume term) and RealmFog297 (far air term) read. It writes only Shader.SetGlobal* — never the sky, ambient, a material or an
    /// asset, never the map or minimap. All references are serialized (no singleton, no Find, no static access); shader ids are
    /// instance fields; no static mutable state. OnDisable and the SubsystemRegistration reset put _OhEventWashCount (and the
    /// volume split _OhEventWashVolA) back to 0 (domain reload is off). The volume's near-place attenuation is computed by the
    /// shader from the camera position every frame, so the write cadence is unchanged.</summary>
    [DisallowMultipleComponent]
    public sealed class EventWashDriver308 : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public EventWashSheetSO Sheet;
        [Tooltip("The scene's own catalog (WorldRealmAtmosphere.Catalog)")]
        public WorldLocationCatalog Catalog;
        [Tooltip("The scene's own palette (WorldLookDriver palette)")]
        public ElementPaletteSO Palette;

        readonly Vector4[] zone = new Vector4[EventWashPlanner308.ShaderZones];
        readonly Vector4[] colour = new Vector4[EventWashPlanner308.ShaderZones];
        readonly Vector4[] band = new Vector4[EventWashPlanner308.ShaderZones];
        readonly Vector4[] volume = new Vector4[EventWashPlanner308.ShaderZones];
        EventWashGlobals308 globals;
        EventWashPlanner308 planner;
        Oheangbu.Data.Demo.DemoCampaignProfile plannedCampaign;
        float nextPoll;
        bool pollNow, snapNext, hasEye, written;
        Vector3 lastEye;

        /// <summary>Probe access (AC-W10/W13): the live plan, the count and the volume split written last frame.</summary>
        public EventWashPlanner308 Planner => planner;
        public int ActiveZones { get; private set; }
        public int ActiveVolumes { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobals()
        {
            // domain reload is off: a previous Play session's globals must not leak into the next
            Shader.SetGlobalFloat("_OhEventWashCount", 0f);
            Shader.SetGlobalVector("_OhEventWashVolA", Vector4.zero);
            Shader.SetGlobalFloat("_OhInkWashDebug308", 0f);
        }

        void OnEnable()
        {
            globals = new EventWashGlobals308();
            planner = null; plannedCampaign = null; snapNext = true; pollNow = true; hasEye = false; written = false;
            if (Session != null) Session.InteractionResolved += OnInteractionResolved;
            globals.Off(false);
        }

        void OnDisable()
        {
            if (Session != null) Session.InteractionResolved -= OnInteractionResolved;
            // AC-W15: after Play the edit-mode frame shows the material _DebugView again (a capture's debug view never sticks)
            globals?.Off(true);
            ActiveZones = 0; ActiveVolumes = 0;
        }

        void OnInteractionResolved(PrologueInteractionKind kind, Vector3 position) { pollNow = true; }

        Camera View => Session != null && Session.Walker != null && Session.Walker.ViewCamera != null ? Session.Walker.ViewCamera : Camera.main;

        void LateUpdate()
        {
            if (Sheet == null || Catalog == null || Palette == null || Session == null) { Off(); return; }
            var campaign = Session.Content != null ? Session.Content.Campaign : null;
            if (campaign == null) { Off(); return; }
            if (planner == null || !ReferenceEquals(plannedCampaign, campaign))
            {
                planner = new EventWashPlanner308(Sheet, Catalog, Palette, campaign);   // once per campaign (load), not per frame
                plannedCampaign = campaign; snapNext = true; pollNow = true;
            }
            var view = View;
            Vector3 eye = view != null ? view.transform.position : transform.position;
            if (hasEye && Sheet.SnapDistance > 0f && (eye - lastEye).sqrMagnitude > Sheet.SnapDistance * Sheet.SnapDistance) { snapNext = true; pollNow = true; }
            lastEye = eye; hasEye = true;

            bool polled = false;
            if (pollNow || Time.unscaledTime >= nextPoll)
            {
                if (Session.DemoCampaignActive) planner.Poll(Session.Progress.campaign, Session.Progress.defeated);
                else planner.Clear();
                nextPoll = Time.unscaledTime + Mathf.Max(.05f, Sheet.PollSeconds);
                pollNow = false; polled = true;
            }
            bool moving = planner.Step(Time.deltaTime, snapNext);
            snapNext = false;
            // write while fading, after every poll (the nearest set follows the camera) and once after enable
            if (!moving && !polled && written) return;
            ActiveZones = planner.Write(eye, zone, colour, band, volume, out int split);
            ActiveVolumes = split;
            globals.Write(Sheet, ActiveZones, split, zone, colour, band, volume);
            written = true;
        }

        void Off()
        {
            if (ActiveZones == 0 && written) return;
            globals.Off(false);
            ActiveZones = 0; ActiveVolumes = 0; written = true;
        }
    }

    /// <summary>The #308 권역 담채 shader globals (SPEC-EVENT-WASH-308 §2–2b, §6): ids cached per instance (no static state) and one
    /// write routine shared by EventWashDriver308 and the editor preview (wash308-preview), so both put the same values.
    ///  _OhEventWashCount (all slots), _OhEventWashZone / Colour / Band / Volume [8], _OhEventWashParams (surface), _OhEventWashAir
    ///  (RealmFog297), _OhEventWashVolA (split, cap, nearClip m, nearKeep), _OhEventWashVolB (nearRamp m, surfaceKeep, surface ramp
    ///  start m, end m), _OhEventWashVolC (edgeNoise, mottle, noiseScale, valueKeep). Arrays are always ShaderZones long.</summary>
    public sealed class EventWashGlobals308
    {
        readonly int countId, zoneId, colourId, bandId, volumeId, paramsId, airId, volAId, volBId, volCId, debugId;

        public EventWashGlobals308()
        {
            countId = Shader.PropertyToID("_OhEventWashCount");
            zoneId = Shader.PropertyToID("_OhEventWashZone");
            colourId = Shader.PropertyToID("_OhEventWashColour");
            bandId = Shader.PropertyToID("_OhEventWashBand");
            volumeId = Shader.PropertyToID("_OhEventWashVolume");
            paramsId = Shader.PropertyToID("_OhEventWashParams");
            airId = Shader.PropertyToID("_OhEventWashAir");
            volAId = Shader.PropertyToID("_OhEventWashVolA");
            volBId = Shader.PropertyToID("_OhEventWashVolB");
            volCId = Shader.PropertyToID("_OhEventWashVolC");
            debugId = Shader.PropertyToID("_OhInkWashDebug308");
        }

        /// <summary>Writes one planner result (count last, so a half-written frame never shows).</summary>
        public void Write(EventWashSheetSO sheet, int count, int split, Vector4[] zone, Vector4[] colour, Vector4[] band, Vector4[] volume)
        {
            Shader.SetGlobalVectorArray(zoneId, zone);
            Shader.SetGlobalVectorArray(colourId, colour);
            Shader.SetGlobalVectorArray(bandId, band);
            Shader.SetGlobalVectorArray(volumeId, volume);
            Shader.SetGlobalVector(paramsId, new Vector4(sheet.Feather, sheet.EdgeNoiseScale, sheet.Chroma, sheet.Lift));
            Shader.SetGlobalVector(airId, new Vector4(sheet.AirRadiusScale, sheet.AirTint, 0f, 0f));
            Shader.SetGlobalVector(volAId, new Vector4(split, sheet.VolumeCap, sheet.VolumeNearClip, sheet.VolumeNearKeep));
            Shader.SetGlobalVector(volBId, new Vector4(sheet.VolumeNearRamp, sheet.VolumeSurfaceKeep, sheet.VolumeSurfaceRamp.x, sheet.VolumeSurfaceRamp.y));
            Shader.SetGlobalVector(volCId, new Vector4(sheet.VolumeEdgeNoise, sheet.VolumeMottle, sheet.VolumeNoiseScale, sheet.VolumeValueKeep));
            Shader.SetGlobalFloat(countId, count);
        }

        /// <summary>Count 0 and split 0 (both terms off, AC-W1); debugToo also resets _OhInkWashDebug308 (AC-W15).</summary>
        public void Off(bool debugToo)
        {
            Shader.SetGlobalFloat(countId, 0f);
            Shader.SetGlobalVector(volAId, Vector4.zero);
            if (debugToo) Shader.SetGlobalFloat(debugId, 0f);
        }
    }
}
