using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NavigationArea
{
    [CustomEditor(typeof(NavigationAreaCustomizer))]
    public class NavigationAreaCustomizerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button(Constants.AddSegmentText))
                ((NavigationAreaCustomizer)target).AddAreaSegment();

            if (GUILayout.Button(Constants.CalculateAreaText))
                ((NavigationAreaCustomizer)target).CalculateArea(true);

#if NAV_MESH_SURFACE
            if (GUILayout.Button(Constants.BuildText))
                ((NavigationAreaCustomizer)target).BuildNavMesh();
            
            if (GUILayout.Button(Constants.ClearText))
                ((NavigationAreaCustomizer)target).ClearNavMesh();
#endif
        }
    }
}