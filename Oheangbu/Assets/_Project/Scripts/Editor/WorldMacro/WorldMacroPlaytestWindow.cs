using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public sealed class WorldMacroPlaytestWindow:EditorWindow
    {
        [MenuItem("Tools/오행부/첫 플레이 구간")]
        static void Open()=>GetWindow<WorldMacroPlaytestWindow>("첫 플레이 구간");
        Vector2 scroll;
        void OnGUI()
        {
            EditorGUILayout.HelpBox("TEST · 폐광 조사 → 첫 교전 → 선택 지선 → 북쪽 사면 → 기존 다리 → 금표 주막. 보행 완주·비주얼·실제 입력 검수는 사용자 확인 전까지 미검증입니다. 아래 이동 버튼은 선택 지점 확인용이며 자동 보행이 아닙니다.",MessageType.Info);
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath){if(GUILayout.Button("플레이테스트 씬 열기")&&EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(WorldMacroPlaytestAuthoring.ScenePath);return;}
            var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s==null)return;
            if(GUILayout.Button("Scene 뷰에서 전체 보행 경로 선택"))Selection.activeGameObject=s.gameObject;
            scroll=EditorGUILayout.BeginScrollView(scroll);
            Stop("안전 시작점",s.Content.StartFeet,s.Content.StartYaw);
            foreach(var p in s.Content.Points)Stop(p.Prompt,p.Position-Vector3.forward*1.8f,0);
            var path=s.Content.MainPath;float walked=0;for(int i=1;i<path.Length;i++){walked+=Vector3.Distance(path[i-1],path[i]);if(walked<500)continue;walked=0;Stop("사면 중간점 "+i,path[i],i+1<path.Length?Quaternion.LookRotation(path[i+1]-path[i]).eulerAngles.y:0);}
            EditorGUILayout.EndScrollView();
            void Stop(string label,Vector3 p,float yaw){EditorGUILayout.LabelField(label+"  "+p.ToString("F1"));if(GUILayout.Button("이 지점 보기 / Play 중 보행 위치 이동")){if(Application.isPlaying){if(s.Walker.Seated){Debug.LogWarning("먼저 차량에서 하차하세요.");return;}if(s.TrySafeFeet(p,out var feet))s.Teleport(feet,yaw);}else if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(p+Vector3.up,Quaternion.Euler(20,yaw,0),12);}}
        }
        [DrawGizmo(GizmoType.Selected)]
        static void Draw(WorldMacroPlaytestSession s,GizmoType type){if(s.Content==null)return;Gizmos.color=new Color(.8f,.65f,.3f);Line(s.Content.MainPath);Gizmos.color=Color.cyan;Line(s.Content.BranchPath);void Line(Vector3[] p){for(int i=1;i<p.Length;i++)Gizmos.DrawLine(p[i-1]+Vector3.up*.2f,p[i]+Vector3.up*.2f);}}
    }
}
