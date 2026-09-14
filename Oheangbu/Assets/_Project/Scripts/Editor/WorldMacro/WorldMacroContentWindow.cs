using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public sealed class WorldMacroContentWindow : EditorWindow
    {
        string search="";
        Vector2 scroll;
        [MenuItem("Tools/오행부/전체맵 콘텐츠 위치")]
        static void Open()=>GetWindow<WorldMacroContentWindow>("콘텐츠 위치");
        void OnGUI()
        {
            EditorGUILayout.HelpBox("TEST 위치 초안. 적 전투·보상·본편 저장은 미연결입니다. '보행 위치'는 선택한 지점 앞으로만 이동하며 자동 보행을 실행하지 않습니다.",MessageType.Info);
            search=EditorGUILayout.TextField("검색",search);
            var root=GameObject.Find(WorldMacroContentAuthoring.RootName);
            var preview=root==null?null:root.GetComponent<WorldMacroContentPreview>();
            if(preview==null||preview.Sheet==null){EditorGUILayout.LabelField("W_WorldMacro_Blockout 씬을 여세요.");return;}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var e in preview.Sheet.Entries.Where(e=>(e.Id+e.Label+e.Realm).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(e.Label+" · "+e.Kind,EditorStyles.boldLabel);
                EditorGUILayout.LabelField(e.Id+" / "+e.Realm+" / "+e.Stage);
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Button("씬에서 선택"))
                {
                    var p=Array.Find(preview.Points,t=>t!=null&&t.Id==e.Id);
                    if(p!=null){Selection.activeGameObject=p.gameObject;SceneView.lastActiveSceneView?.FrameSelected();}
                }
                if(GUILayout.Button("보행 위치"))WorldMacroContentAuthoring.Visit(e.Id);
                EditorGUILayout.EndHorizontal();EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
