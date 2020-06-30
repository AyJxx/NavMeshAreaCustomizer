using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;

namespace NavigationArea
{
	/// <summary>
	/// Simply put, this tool recreates specified terrain mesh in defined navigation area and then is possible to bake NavMesh in this area,
	/// so it means much more control for designer
	/// </summary>
	[ExecuteInEditMode]
	public class NavigationAreaCustomizer : MonoBehaviour
	{
		public enum ShowGizmos
		{
			Always, WhenSelected
		}

		[Space(20)]
		[SerializeField] private ShowGizmos showGizmos = ShowGizmos.Always;
		[SerializeField] private bool autoCalculation = true; // If true, navigation area is calculated automatically each update (not apply to play mode)

		[Space(20)]
		[SerializeField] [Range(0.1f, 10.0f)] private float segmentedLineStep = 0.25f;
		[SerializeField] [Range(1.0f, 10.0f)] private float areaLineThickness = 5.0f;

		private readonly Dictionary<Transform, NavigationAreaSegment> segments = new Dictionary<Transform, NavigationAreaSegment>();
		private readonly List<Transform> segmentsToRemove = new List<Transform>();

		private Shader navigationAreaSegmentShader;
		private Material segmentMaterial;

#if NAV_MESH_SURFACE
		private NavMeshSurface navMeshSurface;
#endif

		private bool canUpdate = false;

		private bool RenderArea
		{
			set
			{
				CheckSegments();
				foreach (var s in segments.Values)
					s.RenderArea = value;
			}
		}

		private void OnValidate()
		{
			CheckSegments();
			foreach (var s in segments.Values)
			{
				s.SegmentedLineStep = segmentedLineStep;
				s.AreaLineThickness = areaLineThickness;
			}
		}

		private void OnEnable()
		{
#if UNITY_EDITOR

#if NAV_MESH_SURFACE
			if (!navMeshSurface)
				navMeshSurface = GetComponentInParent<NavMeshSurface>();
#endif

			navigationAreaSegmentShader = (Shader)AssetDatabase.LoadAssetAtPath(Constants.NavigationAreaSegmentShaderPath, typeof(Shader));
			Assert.IsNotNull(navigationAreaSegmentShader, $"Shader not found at path {Constants.NavigationAreaSegmentShaderPath}");
			if (!segmentMaterial)
				segmentMaterial = new Material(navigationAreaSegmentShader);

			CheckSegments();
			foreach (var s in segments.Values)
				s.SetAreaMaterial(segmentMaterial);

			Selection.selectionChanged += OnSelectionChanged;
#else
			gameObject.SetActive(false);
#endif
		}

		private void Update()
		{
			if (!canUpdate)
				return;

			CheckSegments();
			OnValidate();

			if (!autoCalculation || Application.isPlaying) 
				return;

			segmentsToRemove.Clear();
			foreach (var s in segments)
			{
				if (s.Value != null)
					s.Value.CalculateArea(false);
				else
					segmentsToRemove.Add(s.Key);
			}

			foreach (var s in segmentsToRemove)
			{
				segments.Remove(s);
			}
		}

		private void OnDisable()
		{
#if UNITY_EDITOR
			if (segmentMaterial)
				DestroyImmediate(segmentMaterial);

			Selection.selectionChanged -= OnSelectionChanged;
#endif
		}

#if UNITY_EDITOR
		private void OnSelectionChanged()
		{
			if (Selection.activeTransform != null && Selection.activeTransform.root == transform.root)
			{
				canUpdate = true;
				RenderArea = true;
			}
			else
			{
				canUpdate = false;
				RenderArea = showGizmos == ShowGizmos.Always;
			}
		}
#endif

		private void CheckSegments()
		{
			foreach (Transform child in transform)
			{
				if (!segments.ContainsKey(child))
				{
					var segment = child.GetComponent<NavigationAreaSegment>();
					if (segment)
					{
						segments.Add(child, segment);
					}
					else
					{
						Debug.LogWarning($"Child object {segment.name} doesn't contain {nameof(NavigationAreaSegment)} component!");
					}
				}
			}
		}

		public void AddAreaSegment()
		{
			var segmentObj = new GameObject("AreaSegment");
			segmentObj.transform.SetParent(transform);
			segmentObj.transform.localPosition = Vector3.zero;
			segmentObj.transform.localRotation = Quaternion.identity;

			var segmentComp = (NavigationAreaSegment)segmentObj.AddComponent(typeof(NavigationAreaSegment));
			for (int i = 0; i < 2; i++)
				UnityEditorInternal.ComponentUtility.MoveComponentUp(segmentComp);

			segmentComp.InitializePoints();
			segmentComp.SetAreaMaterial(segmentMaterial);
			segmentComp.OnValidate();
			GameObjectUtility.SetStaticEditorFlags(segmentObj, StaticEditorFlags.NavigationStatic);

			OnValidate();
		}

		public void CalculateArea(bool manualInvoke)
		{
			foreach (var s in segments.Values)
			{
				if (s)
					s.CalculateArea(manualInvoke);
			}
		}

#if NAV_MESH_SURFACE
		public void BuildNavMesh()
		{
			if (!navMeshSurface)
				navMeshSurface = GetComponentInParent<NavMeshSurface>();

			CalculateArea(true);
			navMeshSurface.BuildNavMesh();
		}
#endif

#if NAV_MESH_SURFACE
		public void ClearNavMesh()
		{
			if (!navMeshSurface)
				navMeshSurface = GetComponentInParent<NavMeshSurface>();

			navMeshSurface.RemoveData();
		}
#endif
	}
}
