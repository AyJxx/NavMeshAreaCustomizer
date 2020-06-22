using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SocialPlatforms;

namespace NavigationArea
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class NavigationAreaSegment : MonoBehaviour
    {
		[Header("Terrain")]
		[Tooltip("This is mesh on which NavMesh will be generated.")]
		[SerializeField] private MeshFilter terrainMesh;
		[Tooltip("This is collider of mesh on which NavMesh will be generated.")]
		[SerializeField] private Collider terrainCollider;

		private MeshCollider areaCollider;
		private MeshFilter projectedArea;
		private MeshRenderer areaRenderer;

		private Mesh areaMesh;
		private Mesh projectedAreaMesh;

		private readonly List<Vector3> areaPoints = new List<Vector3>(); // Points in NavigationArea which are used to create arbitrary shape
		private readonly List<Vector3> segmentedAreaPoints = new List<Vector3>(); // Same as areaPoints but line from one point to another is segmented to multiple points
		private int[] areaPointsTriangles;

		private readonly List<Vector3> projectedAreaVertices = new List<Vector3>(); // These are vertices of mesh created in NavigationArea

		private Vector3[] terrainVertices;
		private int[] terrainTriangles;

		public bool RenderArea { get; set; }
		public float AreaLineThickness { get; set; }
		public float SegmentedLineStep { get; set; }

		private void OnValidate()
		{
			if (terrainMesh)
			{
				terrainVertices = terrainMesh.sharedMesh.vertices;
				terrainTriangles = terrainMesh.sharedMesh.triangles;
			}
		}

		private void OnEnable()
		{
			areaCollider = GetComponent<MeshCollider>();
			projectedArea = GetComponent<MeshFilter>();
			areaRenderer = GetComponent<MeshRenderer>();

			AreaRendering();

#if UNITY_EDITOR
			areaMesh = new Mesh();
			projectedAreaMesh = new Mesh();
#endif
		}

		private void OnDrawGizmos()
		{
			AreaRendering();

			if (transform.childCount < 3) return;

			for (int i = 0; i < transform.childCount; i++)
			{
				var p1 = transform.GetChild(i).position;
				var p2 = i == transform.childCount - 1 ? transform.GetChild(0).position : transform.GetChild(i + 1).position;

				Handles.color = Color.red;
				Handles.DrawBezier(p1, p2, p1, p2, Color.red, null, AreaLineThickness);
			}
		}

		public void InitializePoints()
		{
			CreatePoint(new Vector3(0.0f, 0.0f, 5.0f));
			CreatePoint(new Vector3(-5.0f, 0.0f, -5.0f));
			CreatePoint(new Vector3(5.0f, 0.0f, -5.0f));

			AssignClosestTerrain(); // !!!
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
			for (int i = 0; i < largeIcons.Length; i++)
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
			areaRenderer.sharedMaterial = material;
		}

		public void CalculateArea(bool manualInvoke)
		{
			if (transform.childCount < 3)
			{
				if (manualInvoke)
					Debug.LogError("NavMesh generation unsuccessful, create at least 3 points in the area!");
				return;
			}
			if (!terrainMesh)
			{
				if (manualInvoke)
					Debug.LogError("Assign terrain mesh to generate NavMesh!");
				return;
			}
			if (!terrainCollider)
			{
				if (manualInvoke)
					Debug.LogError("Assign terrain collider to generate NavMesh!");
				return;
			}

			OnValidate();

			areaCollider.enabled = true;
			areaRenderer.enabled = true;

			UpdateArea();
			UpdateProjectedArea();
		}

		private void AssignClosestTerrain()
		{
			RaycastHit[] hits;
			hits = Physics.RaycastAll(transform.position + Vector3.up * 10, Vector3.down, Mathf.Infinity);
			if (hits.Length > 0)
			{
				var sorted = hits.OrderBy(h => (h.point - transform.position).sqrMagnitude);
				foreach (var hit in sorted)
				{
					if (!hit.transform.root.Equals(transform.root) && hit.transform.GetComponent<MeshFilter>())
					{
						var meshFilter = hit.transform.GetComponent<MeshFilter>();
						terrainMesh = meshFilter;
						terrainCollider = hit.collider;
						return;
					}
				}
			}
		}

		private void AreaRendering()
		{
			if (Application.isPlaying)
			{
				areaCollider.enabled = false;
				areaRenderer.enabled = false;
			}
			else
			{
				areaCollider.enabled = true;
				areaRenderer.enabled = RenderArea;
			}
		}

		// Creating collider specified by points in navigation area
		private void UpdateArea()
		{
			areaPoints.Clear();
			segmentedAreaPoints.Clear();

			for (int i = 0; i < transform.childCount; i++)
			{
				transform.GetChild(i).position = ProjectOnTerrain(transform.GetChild(i).position); // Aligning Y coordinate of NavigationArea Point to lay on terrain collider
				areaPoints.Add(transform.GetChild(i).localPosition);

				var p1 = transform.GetChild(i).position;
				var p2 = i == transform.childCount - 1 ? transform.GetChild(0).position : transform.GetChild(i + 1).position;

				var path = p2 - p1;
				var dist = path.magnitude;
				var dir = path.normalized;

				for (float step = 0.0f; step < dist; step += SegmentedLineStep)
				{
					var p = p1 + dir * step;
					var segmentedPoint = ProjectOnTerrain(p);
					segmentedAreaPoints.Add(segmentedPoint);
				}
			}

			if (areaPointsTriangles == null || areaPointsTriangles.Length != (3 * (areaPoints.Count - 2)))
			{
				areaPointsTriangles = GenerateTriangles(areaPoints);
				areaMesh.Clear();
			}

			areaMesh.SetVertices(areaPoints);
			areaMesh.SetTriangles(areaPointsTriangles, 0);
			areaMesh.RecalculateBounds();
			areaCollider.sharedMesh = areaMesh;
		}

		// Using collider calculated in UpdateArea function to recreate terrain mesh but only in NavigationArea,
		// then this mesh is used for baking of NavMesh
		private void UpdateProjectedArea()
		{
			projectedAreaVertices.Clear();

			for (int i = 0; i < terrainVertices.Length; i++)
			{
				var terrainVertex = terrainMesh.transform.TransformPoint(terrainVertices[i]);

				if (IsVertexInArea(terrainVertex))
				{
					projectedAreaVertices.Add(transform.InverseTransformPoint(terrainVertex));
				}
				else
				{
					var closestSqrDistance = float.MaxValue;
					var closestAreaPoint = segmentedAreaPoints[0];
					foreach (var point in segmentedAreaPoints)
					{
						var sqrDist = (terrainVertex - point).sqrMagnitude;
						if (closestSqrDistance > sqrDist)
						{
							closestSqrDistance = sqrDist;
							closestAreaPoint = point;
						}
					}
					projectedAreaVertices.Add(transform.InverseTransformPoint(closestAreaPoint));
				}
			}

			Discard();
			
			projectedAreaMesh.Clear(); // Prevents error in console when new terrain mesh is assigned 
			projectedAreaMesh.SetVertices(projectedAreaVertices);
			//projectedAreaMesh.SetTriangles(terrainTriangles, 0);
			projectedAreaMesh.SetTriangles(triangles, 0);

			projectedAreaMesh.RecalculateBounds();
			projectedArea.sharedMesh = projectedAreaMesh;
		}

		private readonly List<int> triangles = new List<int>();
		private void Discard()
		{
			triangles.Clear();

			for (int i = 0; i < terrainTriangles.Length; i += 3)
			{
				var index1 = terrainTriangles[i];
				var index2 = terrainTriangles[i + 1];
				var index3 = terrainTriangles[i + 2];

				if (IsVertexInArea(terrainMesh.transform.TransformPoint(terrainVertices[index1])) ||
					IsVertexInArea(terrainMesh.transform.TransformPoint(terrainVertices[index2])) ||
					IsVertexInArea(terrainMesh.transform.TransformPoint(terrainVertices[index3])))
				{
					triangles.Add(index1);
					triangles.Add(index2);
					triangles.Add(index3);
				}
			}
		}

		// Generating triangles for supplied vertices
		private int[] GenerateTriangles(List<Vector3> vertices)
		{
			var indices = new int[3 * (vertices.Count - 2)];

			// All triangles start from first vertex
			int v1 = 0;
			int v2 = 2;
			int v3 = 1;

			for (int i = 0; i < indices.Length; i += 3)
			{
				indices[i] = v1;
				indices[i + 1] = v2++;
				indices[i + 2] = v3++;
			}

			return indices;
		}

		// NavigationArea is operating on XZ plane, so this method
		// projects any point on terrain collider (on correct Y coordinate)
		private Vector3 ProjectOnTerrain(Vector3 pos)
		{
			var highestPoint = pos;
			highestPoint.y = terrainCollider.bounds.max.y;

			var ray = new Ray(highestPoint + Vector3.up, Vector3.down);
			if (terrainCollider.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity))
				return hitInfo.point;
			else
				return pos;
		}

		// Method which checks if vertex lays in navigation area collider (ignoring Y coordinate)
		private bool IsVertexInArea(Vector3 pos)
		{
			pos.y = areaCollider.bounds.max.y;
			var ray = new Ray(pos + Vector3.up, Vector3.down);
			return areaCollider.Raycast(ray, out _, Mathf.Infinity);
		}
	}
}