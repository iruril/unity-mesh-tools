using System;
using UnityEditor;

namespace MeshTools.Dismemberment.Editor
{
    public static class SkinnedMeshCutterLauncher
    {
        public static Func<SkinnedMeshCutterWindowBase> WindowFactory { get; set; }

        /// <summary>기존 프로젝트 전용 진입점에서 등록된 확장 창 또는 공용 창을 엽니다.</summary>
        public static void Open()
        {
            var window = WindowFactory != null
                ? WindowFactory()
                : EditorWindow.GetWindow<SkinnedMeshModelCutterWindow>();
            window.UseSelectionIfEmpty();
            window.Show();
        }
    }
}
