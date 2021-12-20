// Copyright (c) Adam Jůva.
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;

namespace NavMeshAreaCustomizer
{
    [CustomEditor(typeof(NavMeshAreaCustomizer))]
    public class NavMeshAreaCustomizerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button(Constants.AddSegmentText))
                ((NavMeshAreaCustomizer)target).AddAreaSegment();

            if (GUILayout.Button(Constants.CalculateAreaText))
                ((NavMeshAreaCustomizer)target).CalculateArea(true);

#if NAV_MESH_SURFACE
            if (GUILayout.Button(Constants.BuildText))
                ((NavMeshAreaCustomizer)target).BuildNavMesh();
            
            if (GUILayout.Button(Constants.ClearText))
                ((NavMeshAreaCustomizer)target).ClearNavMesh();
#endif
        }
    }
}