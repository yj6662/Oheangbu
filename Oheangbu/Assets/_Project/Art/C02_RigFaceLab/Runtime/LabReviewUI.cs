using UnityEngine;

namespace Oheangbu.C02RigFaceLab
{
    public sealed class LabReviewUI : MonoBehaviour
    {
        public LabFaceController face;
        public LabPlaybackController playback;
        public Camera[] cameras;
        public int cameraIndex;
        public bool visible = true;
        Vector2 scroll;
        public void SelectCamera(int index)
        {
            cameraIndex = Mathf.Clamp(index, 0, cameras.Length - 1);
            for (int i = 0; i < cameras.Length; ++i) cameras[i].enabled = i == cameraIndex;
        }
        void Start() { if (cameras.Length > 0) SelectCamera(cameraIndex); }
        void OnGUI()
        {
            if (!visible) return;
            GUILayout.BeginArea(new Rect(12, 12, 310, Screen.height - 24), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("C02 Rig / Face — Independent experiment");
            GUILayout.Label("Quality: UNVERIFIED · Canonical unchanged");
            if (face != null && face.profile != null)
            {
                if (GUILayout.Button("Neutral / 0")) face.Neutral();
                for (int i = 0; i < face.ChannelCount; ++i)
                {
                    GUILayout.Label(face.profile.faceChannels[i] + "  " + face.GetValue(i).ToString("0.00"));
                    face.SetValue(i, GUILayout.HorizontalSlider(face.GetValue(i), 0f, 1f));
                }
                for (int i = 0; i < face.profile.presets.Length; ++i)
                    if (GUILayout.Button(face.profile.presets[i].name)) face.ApplyPreset(i);
            }
            if (playback != null)
            {
                foreach (string state in playback.states) if (GUILayout.Button(state)) { playback.StopSequence(); playback.Play(state); }
                if (playback.states.Length >= 4 && GUILayout.Button("Idle → Run ×6 → Idle → Attack")) playback.StartSequence();
                GUILayout.Label(playback.sequenceStatus);
            }
            for (int i = 0; i < cameras.Length; ++i) if (GUILayout.Button(cameras[i].name)) SelectCamera(i);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
