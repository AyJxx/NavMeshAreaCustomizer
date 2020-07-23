using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;

namespace NavMeshAreaCustomizer
{
	/// <summary>
	/// Simply put, this tool recreates specified terrain mesh in defined navigation area and then is possible to bake NavMesh in this area,
	/// so it means much more control for designer
	/// </summary>
	[ExecuteInEditMode]
	public class NavMeshAreaCustomizer : MonoBehaviour
	{
		public enum ShowGizmos
		{
			Always, WhenSelected
		}

		[Space(20)]
		[SerializeField] private ShowGizmos showInEditMode = ShowGizmos.Always;
		[SerializeField] private ShowGizmos showInPlayMode = ShowGizmos.WhenSelected;
		[SerializeField] private bool autoCalculation = true; // If true, navigation area is calculated automatically each update (not apply to play mode)

		[Space(20)]
		[SerializeField] [Range(0.1f, 10.0f)] private float segmentedLineStep = 0.25f;
		[SerializeField] [Range(1.0f, 10.0f)] private float areaLineThickness = 5.0f;

		private readonly Dictionary<Transform, NavMeshAreaSegment> segments = new Dictionary<Transform, NavMeshAreaSegment>();
		private readonly List<Transform> segmentsToRemove = new List<Transform>();

		private Shader navigationAreaSegmentShader;
		private Material segmentMaterial;

#if NAV_MESH_SURFACE
		private NavMeshSurface navMeshSurface;
		private NavMeshSurface NavMeshSurface
		{
			get
			{
				if (!navMeshSurface)
					navMeshSurface = GetComponentInParent<NavMeshSurface>();
				return navMeshSurface;
			}
		}
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

			if (autoCalculation)
				enabled = true;
		}

#if UNITY_EDITOR
		private void Awake()
		{
			navigationAreaSegmentShader = (Shader)AssetDatabase.LoadAssetAtPath(Constants.NavigationAreaSegmentShaderPath, typeof(Shader));
			Assert.IsNotNull(navigationAreaSegmentShader, $"Shader not found at path {Constants.NavigationAreaSegmentShaderPath}");
			segmentMaterial = new Material(navigationAreaSegmentShader);
		}
#endif

		private void OnEnable()
		{
#if UNITY_EDITOR
			CheckSegments();
			foreach (var s in segments.Values)
				s.SetAreaMaterial(segmentMaterial);

			Selection.selectionChanged += OnSelectionChanged;
			OnSelectionChanged();
#else
			gameObject.SetActive(false);
#endif
		}

		private void Update()
		{
			if (!canUpdate)
				return;

			OnValidate();

			if (!autoCalculation)
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

#if UNITY_EDITOR
		private void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
		}

		private void OnDestroy()
		{
			if (segmentMaterial)
				DestroyImmediate(segmentMaterial);
		}

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
				if (Application.isPlaying)
					RenderArea = showInPlayMode == ShowGizmos.Always;
				else
					RenderArea = showInEditMode == ShowGizmos.Always;
			}
		}
#endif

		private void CheckSegments()
		{
			foreach (Transform child in transform)
			{
				if (!segments.ContainsKey(child))
				{
					var segment = child.GetComponent<NavMeshAreaSegment>();
					if (segment)
					{
						segments.Add(child, segment);
					}
					else
					{
						Debug.LogWarning($"Child object {segment.name} doesn't contain {nameof(NavMeshAreaSegment)} component!");
					}
				}
			}
		}

		public async void AddAreaSegment()
		{
			var segmentObj = new GameObject("AreaSegment");
			segmentObj.transform.SetParent(transform);
			segmentObj.transform.localPosition = Vector3.zero;
			segmentObj.transform.localRotation = Quaternion.identity;

			var segmentComp = (NavMeshAreaSegment)segmentObj.AddComponent(typeof(NavMeshAreaSegment));
			for (int i = 0; i < 2; i++)
				UnityEditorInternal.ComponentUtility.MoveComponentUp(segmentComp);

			segmentComp.Initialize();
			segmentComp.SetAreaMaterial(segmentMaterial);
			segmentComp.OnValidate();
			GameObjectUtility.SetStaticEditorFlags(segmentObj, StaticEditorFlags.NavigationStatic);

			OnValidate();
			segmentComp.CalculateArea(false);

			// !!!
			Selection.activeObject = null;
			await Task.Delay(10);
			Selection.activeObject = gameObject;
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
			NavMeshSurface.BuildNavMesh();
		}

		public void ClearNavMesh()
		{
			NavMeshSurface.RemoveData();
		}
#endif
	}
}
