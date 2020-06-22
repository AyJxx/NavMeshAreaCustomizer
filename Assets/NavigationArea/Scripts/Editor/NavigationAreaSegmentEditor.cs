using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NavigationArea
{
    [CustomEditor(typeof(NavigationAreaSegment))]
    public class NavigationAreaSegmentEditor : Editor
    {
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			if (GUILayout.Button(Constants.AddPointText))
				((NavigationAreaSegment)target).CreatePoint();
		}
	}
}