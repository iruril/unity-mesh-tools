using System;
using UnityEditor;

namespace MeshTools.Dismemberment.Editor
{
    public static class SkinnedMeshCutterLauncher
    {
        public static Func<SkinnedMeshCutterWindowBase> WindowFactory { get; set; }

        /// <summary>선택한 스킨을 사용할 절단 도구 창을 엽니다.</summary>
        [MenuItem("MeshTools/Skinned Mesh Cutter")]
        public static void Open()
        {
            var window = WindowFactory != null
                ? WindowFactory()
                : EditorWindow.GetWindow<SkinnedMeshCutterWindowBase>();
            window.UseSelectionIfEmpty();
            window.Show();
        }
    }
}
