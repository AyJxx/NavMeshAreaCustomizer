// Copyright (c) Adam Jůva.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NavMeshAreaCustomizer
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class NavMeshAreaSegment : MonoBehaviour
    {
#if UNITY_EDITOR
		[Header("Terrain")]
		[Tooltip("This is mesh on which NavMesh will be generated.")]
		[SerializeField] private MeshFilter terrainMesh;

		[Tooltip("This is collider of mesh on which NavMesh will be generated.")]
		[SerializeField] private Collider terrainCollider;

		private Mesh projectedAreaMesh;
		private MeshCollider terrainMeshCollider;

		private MeshFilter projectedArea;
		private MeshRenderer areaRenderer;

		private readonly List<Vector3> areaPoints = new List<Vector3>(); // Points in NavigationArea which are used to create arbitrary shape
		private List<Vector3>[] segmentedAreaPoints; // Same as areaPoints but line from one point to another is segmented to multiple points
		private readonly List<Vector3> areaTriangles = new List<Vector3>();

		private readonly List<int> projectedAreaTriangles = new List<int>();
		private Vector3[] projectedAreaVertices; // These are vertices of mesh created in NavigationArea
		private bool[] projectedVerticesTransformed;

		private Vector3[] terrainVertices;
		private int[] terrainTriangles;

		private bool isUpdating = false; // Flag is secondary thread is already calculating projected area mesh
		private bool hasPendingCalculation = false; // Flag if there is needed final calculation after user defined navigation area
		private bool isDestroyed = false;


		private MeshFilter ProjectedArea => projectedArea ?? (projectedArea = GetComponent<MeshFilter>());
		private MeshRenderer AreaRenderer => areaRenderer ?? (areaRenderer = GetComponent<MeshRenderer>());

		public bool RenderArea { get; set; }
		public float AreaLineThickness { get; set; }
		public float SegmentedLineStep { get; set; }


		private event Action AreaCalculated;
		

		public void OnValidate()
		{
			if (!isUpdating)
			{
				OnTerrainChange();
				return;
			}

			if (AreaCalculated == null)
				AreaCalculated += OnTerrainChange;
		}

		private void Awake()
		{
			projectedAreaMesh = new Mesh() { name = "ProjectedArea" };
		}

		private void OnDestroy()
		{
			isDestroyed = true;
		}

		private void OnDrawGizmos()
		{
			AreaRenderer.enabled = RenderArea;
			if (!RenderArea)
				return;

			if (transform.childCount < 3) 
				return;

			for (var i = 0; i < transform.childCount; i++)
			{
				var p1 = transform.GetChild(i).position;
				var p2 = i == transform.childCount - 1 ? transform.GetChild(0).position : transform.GetChild(i + 1).position;

				Handles.color = Color.red;
				Handles.DrawBezier(p1, p2, p1, p2, Color.red, null, AreaLineThickness);
			}
		}

		private void OnTerrainChange()
		{
			if (terrainMesh != null)
			{
				terrainVertices = terrainMesh.sharedMesh.vertices;
				terrainTriangles = terrainMesh.sharedMesh.triangles;
				projectedAreaVertices = new Vector3[terrainVertices.Length];
				projectedVerticesTransformed = new bool[terrainVertices.Length];

				// Prevention for enabled Navigation Static flag on terrain game object
				var flags = GameObjectUtility.GetStaticEditorFlags(terrainMesh.gameObject);
				if ((flags & StaticEditorFlags.NavigationStatic) > 0)
				{
					flags ^= StaticEditorFlags.NavigationStatic;
					GameObjectUtility.SetStaticEditorFlags(terrainMesh.gameObject, flags);
				}
			}

			if (terrainCollider != null)
				terrainMeshCollider = terrainCollider.GetComponent<MeshCollider>();

			AreaCalculated -= OnTerrainChange;
		}

		public void Initialize()
		{
			CreatePoint(new Vector3(0.0f, 0.0f, 5.0f));
			CreatePoint(new Vector3(-5.0f, 0.0f, -5.0f));
			CreatePoint(new Vector3(5.0f, 0.0f, -5.0f));

			var meshRenderer = GetComponent<MeshRenderer>();
			meshRenderer.lightProbeUsage = LightProbeUsage.Off;
			meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			meshRenderer.allowOcclusionWhenDynamic = false;

			AssignClosestTerrain();
		}

		public void CreatePoint()
		{
			CreatePoint(transform.GetChild(transform.childCount - 1).localPosition);
		}

		private void CreatePoint(Vector3 localPos)
		{
			var point = new GameObject("Point");
			point.transform.SetParent(transform);
			point.transform.localPosition = localPos;
			point.transform.localRotation = Quaternion.identity;
			DrawPointIcon(point, 6);
		}

		private void DrawPointIcon(GameObject obj, int index)
		{
			var largeIcons = new GUIContent[8];
			for (var i = 0; i < largeIcons.Length; i++)
				largeIcons[i] = EditorGUIUtility.IconContent("sv_label_" + (i));

			var icon = largeIcons[index];
			var egu = typeof(EditorGUIUtility);
			var flags = BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.NonPublic;
			var args = new object[] { obj, icon.image };
			var setIcon = egu.GetMethod("SetIconForObject", flags, null, new Type[] { typeof(UnityEngine.Object), typeof(Texture2D) }, null);
			setIcon.Invoke(null, args);
		}

		public void SetAreaMaterial(Material material)
		{
			AreaRenderer.sharedMaterial = material;
		}

		private bool CanBeUpdated()
		{
			if (Selection.activeTransform == null)
				return false;

			return transform.IsChildOf(Selection.activeTransform) || Selection.activeTransform.IsChildOf(transform);
		}

		public void CalculateArea(bool manualInvoke)
		{
			if (!manualInvoke && !hasPendingCalculation && !CanBeUpdated())
				return;

			if (transform.childCount < 3)
			{
				if (manualInvoke)
					Debug.LogError("NavMesh generation unsuccessful, create at least 3 points in the area!");
				return;
			}

			if (terrainMesh == null)
			{
				if (manualInvoke)
					Debug.LogError("Assign terrain mesh to generate NavMesh!");
				return;
			}

			if (terrainCollider == null)
			{
				if (manualInvoke)
					Debug.LogError("Assign terrain collider to generate NavMesh!");
				return;
			}

			var isConvex = false;
			if (terrainMeshCollider)
				isConvex = terrainMeshCollider.convex;

			AlignAreaPoints();

			if (!isUpdating)
			{
				UpdateArea();
				GenerateProjectedArea();
			}
			else
			{
				hasPendingCalculation = true;
			}

			if (terrainMeshCollider != null)
				terrainMeshCollider.convex = isConvex;
		}

		/// <summary>
		/// This method tries to find nearest terrain mesh (collider) and assign it to the current segment.
		/// </summary>
		private void AssignClosestTerrain()
		{
			var hits = Physics.RaycastAll(transform.position + transform.up * 100.0f, -transform.up);
			if (hits.Length > 0)
			{
				var sorted = hits.OrderBy(h => (h.point - transform.position).sqrMagnitude);
				foreach (var hit in sorted)
				{
					var meshFilter = hit.transform.GetComponent<MeshFilter>();
					if (!meshFilter)
						continue;

					terrainMesh = meshFilter;
					terrainCollider = hit.collider;
					return;
				}
			}
		}

		/// <summary>
		/// This method keeps area points sticked to a terrain.
		/// </summary>
		private void AlignAreaPoints()
		{
			for (var i = 0; i < transform.childCount; i++)
			{
				transform.GetChild(i).position = ProjectOnTerrain(transform.GetChild(i).position);  // Aligning Y coordinate of NavigationArea Point to lay on terrain collider
			}
		}

		/// <summary>
		/// Updating navigation area - lines segmentation and generating area triangles.
		/// </summary>
		private void UpdateArea()
		{
			areaPoints.Clear();

			for (var i = 0; i < transform.childCount; i++)
			{
				areaPoints.Add(transform.GetChild(i).localPosition);
			}

			GenerateAreaTriangles(areaPoints);
			SegmentArea(areaPoints);
		}

		/// <summary>
		/// This method divides area lines to small segments so terrain mesh vertices outside of area shape can be aligned with them.
		/// </summary>
		/// <param name="areaPoints">Area points of current segment.</param>
		private void SegmentArea(IReadOnlyList<Vector3> areaPoints)
		{
			if (segmentedAreaPoints == null || segmentedAreaPoints.Length != areaPoints.Count)
				segmentedAreaPoints = new List<Vector3>[areaPoints.Count];

			for (var i = 0; i < areaPoints.Count; i++)
			{
				var p1 = areaPoints[i]; // Area space
				var p2 = i == areaPoints.Count - 1 ? areaPoints[0] : areaPoints[i + 1]; // Area space

				var path = p2 - p1;
				var dist = path.magnitude;
				var dir = path.normalized;

				if (segmentedAreaPoints[i] == null)
					segmentedAreaPoints[i] = new List<Vector3>();
				else
					segmentedAreaPoints[i].Clear();
				
				for (var step = 0.0f; step < dist; step += SegmentedLineStep)
				{
					var p = p1 + dir * step;
					var segmentedPoint = ProjectOnTerrain(transform.TransformPoint(p)); // World space
					segmentedAreaPoints[i].Add(transform.InverseTransformPoint(segmentedPoint));
				}
			}
		}

		/// <summary>
		/// This methods basically recreates terrain mesh but only in projected area, 
		/// so NavMesh then can be baked on it.
		/// </summary>
		private async void GenerateProjectedArea()
		{
			isUpdating = true;

			var terrainLocalToWorld = terrainMesh.transform.localToWorldMatrix;
			var areaWorldToLocal = transform.worldToLocalMatrix;

			await Task.Run(() =>
			{
				projectedAreaTriangles.Clear();

				for (var i = 0; i < projectedVerticesTransformed.Length; i++)
				{
					projectedVerticesTransformed[i] = false;
				}

				for (var i = 0; i < terrainTriangles.Length; i += 3)
				{
					var index1 = terrainTriangles[i];
					var index2 = terrainTriangles[i + 1];
					var index3 = terrainTriangles[i + 2];

					var terrainVertex1 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index1])); // Area space
					var terrainVertex2 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index2])); // Area space
					var terrainVertex3 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index3])); // Area space

					// Copying terrainVertex variables and zeroing Y coordinate (in area space) so triangle intersection is performed in 2D plane
					var tv1 = terrainVertex1;
					var tv2 = terrainVertex2;
					var tv3 = terrainVertex3;
					tv1.y = tv2.y = tv3.y = 0;

					for (var j = 0; j < areaTriangles.Count; j += 3)
					{
						var at1 = areaTriangles[j];
						var at2 = areaTriangles[j + 1];
						var at3 = areaTriangles[j + 2];
						at1.y = at2.y = at3.y = 0; // Zeroing Y coordinate (in area space) so triangle intersection is performed in 2D plane, same as with terrainVertex

						// Testing if current terrain triangle intersects some of area triangles
						if (TriangleIntersection.TriangleAndTriangle(at1, at2, at3, tv1, tv2, tv3))
						{
							CalculateProjectedAreaVertex(terrainVertex1, index1);
							CalculateProjectedAreaVertex(terrainVertex2, index2);
							CalculateProjectedAreaVertex(terrainVertex3, index3);

							projectedAreaTriangles.Add(index1);
							projectedAreaTriangles.Add(index2);
							projectedAreaTriangles.Add(index3);
							break;
						}
					}
				}
			});

			if (isDestroyed)
				return;

			projectedAreaMesh.Clear(); // Prevents error in console when new terrain mesh is assigned 
			projectedAreaMesh.SetVertices(projectedAreaVertices);
			projectedAreaMesh.SetTriangles(projectedAreaTriangles, 0);
			projectedAreaMesh.RecalculateBounds();
			ProjectedArea.sharedMesh = projectedAreaMesh;

			isUpdating = false;
			AreaCalculated?.Invoke();

			if (hasPendingCalculation)
			{
				hasPendingCalculation = false;
				CalculateArea(false);
			}
		}

		private void CalculateProjectedAreaVertex(Vector3 terrainVertexAreaSpace, int index)
		{
			if (projectedVerticesTransformed[index])
				return;

			projectedAreaVertices[index] = IsVertexInArea(terrainVertexAreaSpace) ? terrainVertexAreaSpace : GetClosestAreaPoint(terrainVertexAreaSpace);
			projectedVerticesTransformed[index] = true;
		}

		private Vector3 GetClosestAreaPoint(Vector3 terrainVertexAreaSpace)
		{
			var closestSegmentedPointSqrDist = float.MaxValue;
			var closestSegmentedPoint = Vector3.zero;

			for (var i = 0; i < areaPoints.Count; i++)
			{
				closestSegmentedPoint = GetClosestAreaPoint(terrainVertexAreaSpace, i, closestSegmentedPoint, ref closestSegmentedPointSqrDist);
			}

			return closestSegmentedPoint;
		}

		private Vector3 GetClosestAreaPoint(Vector3 terrainVertexAreaSpace, int index, Vector3 closestPoint, ref float closestPointSqrDist)
		{
			foreach (var point in segmentedAreaPoints[index])
			{
				var sqrDist = (point - terrainVertexAreaSpace).sqrMagnitude;

				if (sqrDist < closestPointSqrDist)
				{
					closestPointSqrDist = sqrDist;
					closestPoint = point;
				}
			}
			return closestPoint;
		}

		/// <summary>
		/// Area is recreated to triangles so each terrain vertex can be tested against them to know if is in area or not.
		/// </summary>
		/// <param name="vertices">Vertices for which triangles are created.</param>
		private void GenerateAreaTriangles(List<Vector3> vertices)
		{
			var indicesCount = 3 * (vertices.Count - 2);

			// These are indices, all triangles start from first vertex
			var v1 = 0;
			var v2 = 2;
			var v3 = 1;

			areaTriangles.Clear();

			for (var i = 0; i < indicesCount; i += 3)
			{
				areaTriangles.Add(vertices[v1]);
				areaTriangles.Add(vertices[v2++]);
				areaTriangles.Add(vertices[v3++]);
			}
		}

		private bool IsVertexInArea(Vector3 pointAreaSpace)
		{
			pointAreaSpace.y = 0;

			for (var j = 0; j < areaTriangles.Count; j += 3)
			{
				var at1 = areaTriangles[j];
				var at2 = areaTriangles[j + 1];
				var at3 = areaTriangles[j + 2];
				at1.y = at2.y = at3.y = 0;

				if (TriangleIntersection.PointAndTriangle(at1, at2, at3, pointAreaSpace))
					return true;
			}
			return false;
		}

		// NavigationArea is operating on XZ plane, so this method
		// projects any point on terrain collider (on correct Y coordinate)
		private Vector3 ProjectOnTerrain(Vector3 worldPos)
		{
			var highestPoint = worldPos;
			highestPoint.y = terrainCollider.bounds.max.y;
			var ray = new Ray(highestPoint + Vector3.up, Vector3.down);

			return terrainCollider.Raycast(ray, out var hitInfo, Mathf.Infinity) ? hitInfo.point : worldPos;
		}
#endif
	}
}