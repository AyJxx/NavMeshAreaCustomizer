// Copyright (c) Adam Jůva.
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;

namespace NavMeshAreaCustomizer
{
    [CustomEditor(typeof(NavMeshAreaSegment))]
    public class NavMeshAreaSegmentEditor : Editor
    {
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			if (GUILayout.Button(Constants.AddPointText))
				((NavMeshAreaSegment)target).CreatePoint();
		}
	}
}