using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.PlayerLoop;
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
		private List<Vector3>[] segmentedAreaPoints; // Same as areaPoints but line from one point to another is segmented to multiple points
		private int[] areaPointsTriangles;

		private readonly List<Vector3> projectedAreaVertices = new List<Vector3>(); // These are vertices of mesh created in NavigationArea
		private readonly List<int> projectedAreaTriangles = new List<int>();

		private Vector3[] terrainVertices;
		private int[] terrainTriangles;

		public bool RenderArea { get; set; }
		public float AreaLineThickness { get; set; }
		public float SegmentedLineStep { get; set; }

		private readonly List<Vector3> areaTriangles = new List<Vector3>(); // !!!
		private bool isUpdating = false; // !!!

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

			SegmentArea();
			GenerateProjectedArea();

			//if (!isUpdating)
			//{
			//	UpdateArea();
			//	UpdateProjectedArea();
			//}
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
		public void UpdateArea()
		{
			areaPoints.Clear();
			//segmentedAreaPoints.Clear();

			for (int i = 0; i < transform.childCount; i++)
			{
				transform.GetChild(i).position = ProjectOnTerrain(transform.GetChild(i).position); // Aligning Y coordinate of NavigationArea Point to lay on terrain collider
				areaPoints.Add(transform.GetChild(i).localPosition);
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

		//private void UpdateArea()
		//{
		//	areaPoints.Clear();
		//	segmentedAreaPoints.Clear();

		//	for (int i = 0; i < transform.childCount; i++)
		//	{
		//		transform.GetChild(i).position = ProjectOnTerrain(transform.GetChild(i).position); // Aligning Y coordinate of NavigationArea Point to lay on terrain collider
		//		areaPoints.Add(transform.GetChild(i).localPosition);

		//		var p1 = transform.GetChild(i).position;
		//		var p2 = i == transform.childCount - 1 ? transform.GetChild(0).position : transform.GetChild(i + 1).position;

		//		var path = p2 - p1;
		//		var dist = path.magnitude;
		//		var dir = path.normalized;

		//		for (float step = 0.0f; step < dist; step += SegmentedLineStep)
		//		{
		//			var p = p1 + dir * step;
		//			var segmentedPoint = ProjectOnTerrain(p);
		//			segmentedAreaPoints.Add(segmentedPoint);
		//		}
		//	}

		//	GenerateAreaTriangles(areaPoints);
		//}

		private void SegmentArea()
		{
			if (segmentedAreaPoints == null || segmentedAreaPoints.Length != areaPoints.Count)
				segmentedAreaPoints = new List<Vector3>[areaPoints.Count];

			for (int i = 0; i < areaPoints.Count; i++)
			{
				var p1 = areaPoints[i];
				var p2 = i == areaPoints.Count - 1 ? areaPoints[0] : areaPoints[i + 1];

				var path = p2 - p1;
				var dist = path.magnitude;
				var dir = path.normalized;

				if (segmentedAreaPoints[i] == null)
					segmentedAreaPoints[i] = new List<Vector3>();
				else
					segmentedAreaPoints[i].Clear();
				
				for (float step = 0.0f; step < dist; step += SegmentedLineStep)
				{
					var p = p1 + dir * step;
					var segmentedPoint = ProjectOnTerrain(p);
					segmentedAreaPoints[i].Add(segmentedPoint);
				}
			}
		}

		// Using collider calculated in UpdateArea function to recreate terrain mesh but only in NavigationArea,
		// then this mesh is used for baking of NavMesh
		private void GenerateProjectedArea()
		{
			projectedAreaVertices.Clear();
			projectedAreaTriangles.Clear();

			bool[] verticesTransformed = new bool[terrainVertices.Length];
			for (int i = 0; i < terrainTriangles.Length; i += 3)
			{
				var index1 = terrainTriangles[i];
				var index2 = terrainTriangles[i + 1];
				var index3 = terrainTriangles[i + 2];

				var terrainVertex1 = terrainMesh.transform.TransformPoint(terrainVertices[index1]);
				var terrainVertex2 = terrainMesh.transform.TransformPoint(terrainVertices[index2]);
				var terrainVertex3 = terrainMesh.transform.TransformPoint(terrainVertices[index3]);

				bool addTriangle = false;

				if (IsVertexInArea(terrainVertex1))
				{
					if (!verticesTransformed[index1])
					{
						projectedAreaVertices.Add(transform.InverseTransformPoint(terrainVertex1));
						verticesTransformed[index1] = true;
					}
					addTriangle = true;
				}
				else if (!verticesTransformed[index1])
				{
					var closestAreaPoint = GetClosestAreaPoint(terrainVertex1);
					projectedAreaVertices.Add(transform.InverseTransformPoint(closestAreaPoint));
					verticesTransformed[index1] = true;
				}

				if (IsVertexInArea(terrainVertex2))
				{
					if (!verticesTransformed[index2])
					{
						projectedAreaVertices.Add(transform.InverseTransformPoint(terrainVertex2));
						verticesTransformed[index2] = true;
					}
					addTriangle = true;
				}
				else if (!verticesTransformed[index2])
				{
					var closestAreaPoint = GetClosestAreaPoint(terrainVertex2);
					projectedAreaVertices.Add(transform.InverseTransformPoint(closestAreaPoint));
					verticesTransformed[index2] = true;
				}

				if (IsVertexInArea(terrainVertex3))
				{
					if (!verticesTransformed[index3])
					{
						projectedAreaVertices.Add(transform.InverseTransformPoint(terrainVertex3));
						verticesTransformed[index3] = true;
					}
					addTriangle = true;
				}
				else if (!verticesTransformed[index3])
				{
					var closestAreaPoint = GetClosestAreaPoint(terrainVertex3);
					projectedAreaVertices.Add(transform.InverseTransformPoint(closestAreaPoint));
					verticesTransformed[index3] = true;
				}

				if (addTriangle)
				{
					projectedAreaTriangles.Add(index1);
					projectedAreaTriangles.Add(index2);
					projectedAreaTriangles.Add(index3);
				}
			}

			projectedAreaMesh.Clear(); // Prevents error in console when new terrain mesh is assigned 
			projectedAreaMesh.SetVertices(projectedAreaVertices);
			projectedAreaMesh.SetTriangles(projectedAreaTriangles, 0);

			projectedAreaMesh.RecalculateBounds();
			projectedArea.sharedMesh = projectedAreaMesh;
		}

		//private async void UpdateProjectedArea()
		//{
		//	Matrix4x4 terrainLocalToWorld = terrainMesh.transform.localToWorldMatrix;
		//	Matrix4x4 transformWorldToLocal = transform.worldToLocalMatrix;

		//	isUpdating = true;

		//	await Task.Run(() =>
		//	{
		//		projectedAreaVertices.Clear();
		//		projectedAreaTriangles.Clear();

		//		bool[] verticesTransformed = new bool[terrainVertices.Length];
		//		for (int i = 0; i < terrainTriangles.Length; i += 3)
		//		{
		//			var index1 = terrainTriangles[i];
		//			var index2 = terrainTriangles[i + 1];
		//			var index3 = terrainTriangles[i + 2];

		//			var terrainVertex1 = terrainLocalToWorld.MultiplyPoint(terrainVertices[index1]);
		//			var terrainVertex2 = terrainLocalToWorld.MultiplyPoint(terrainVertices[index2]);
		//			var terrainVertex3 = terrainLocalToWorld.MultiplyPoint(terrainVertices[index3]);

		//			bool addTriangle = false;

		//			if (IsVertexInTriangle(terrainVertex1))
		//			{
		//				if (!verticesTransformed[index1])
		//				{
		//					projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(terrainVertex1));
		//					verticesTransformed[index1] = true;
		//				}
		//				addTriangle = true;

		//				count++;
		//			}
		//			else if (!verticesTransformed[index1])
		//			{
		//				var closestAreaPoint = GetClosestAreaPoint(terrainVertex1);
		//				projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(closestAreaPoint));
		//				verticesTransformed[index1] = true;
		//			}

		//			if (IsVertexInTriangle(terrainVertex2))
		//			{
		//				if (!verticesTransformed[index2])
		//				{
		//					projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(terrainVertex2));
		//					verticesTransformed[index2] = true;
		//				}
		//				addTriangle = true;

		//				count++;
		//			}
		//			else if (!verticesTransformed[index2])
		//			{
		//				var closestAreaPoint = GetClosestAreaPoint(terrainVertex2);
		//				projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(closestAreaPoint));
		//				verticesTransformed[index2] = true;
		//			}

		//			if (IsVertexInTriangle(terrainVertex3))
		//			{
		//				if (!verticesTransformed[index3])
		//				{
		//					projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(terrainVertex3));
		//					verticesTransformed[index3] = true;
		//				}
		//				addTriangle = true;

		//				count++;
		//			}
		//			else if (!verticesTransformed[index3])
		//			{
		//				var closestAreaPoint = GetClosestAreaPoint(terrainVertex3);
		//				projectedAreaVertices.Add(transformWorldToLocal.MultiplyPoint(closestAreaPoint));
		//				verticesTransformed[index3] = true;
		//			}

		//			if (addTriangle)
		//			{
		//				projectedAreaTriangles.Add(index1);
		//				projectedAreaTriangles.Add(index2);
		//				projectedAreaTriangles.Add(index3);
		//			}
		//		}
		//	});

		//	projectedAreaMesh.Clear(); // Prevents error in console when new terrain mesh is assigned 
		//	projectedAreaMesh.SetVertices(projectedAreaVertices);
		//	projectedAreaMesh.SetTriangles(projectedAreaTriangles, 0);

		//	projectedAreaMesh.RecalculateBounds();
		//	projectedArea.sharedMesh = projectedAreaMesh;

		//	isUpdating = false;
		//}

		private Vector3 GetClosestAreaPoint(Vector3 vertexPos)
		{
			var closestSqrDistance = float.MaxValue;
			var closestAreaPointIndex = 0;
			for (int i = 0; i < areaPoints.Count; i++)
			{
				var sqrDist = (areaPoints[i] - vertexPos).sqrMagnitude;
				if (sqrDist < closestSqrDistance)
				{
					closestSqrDistance = sqrDist;
					closestAreaPointIndex = i;
				}
			}

			closestSqrDistance = float.MaxValue;
			var closestAreaPoint = Vector3.zero;
			foreach (var point in segmentedAreaPoints[closestAreaPointIndex])
			{
				var sqrDist = (point - vertexPos).sqrMagnitude;
				if (sqrDist < closestSqrDistance)
				{
					closestSqrDistance = sqrDist;
					closestAreaPoint = point;
				}
			}

			var previousIndex = closestAreaPointIndex == 0 ? areaPoints.Count - 1 : closestAreaPointIndex - 1;
			foreach (var point in segmentedAreaPoints[previousIndex])
			{
				var sqrDist = (point - vertexPos).sqrMagnitude;
				if (sqrDist < closestSqrDistance)
				{
					closestSqrDistance = sqrDist;
					closestAreaPoint = point;
				}
			}

			//var closestSqrDistance = float.MaxValue;
			//var closestAreaPoint = Vector3.zero;
			//foreach (var point in segmentedAreaPoints)
			//{
			//	var sqrDist = (vertexPos - point).sqrMagnitude;
			//	if (closestSqrDistance > sqrDist)
			//	{
			//		closestSqrDistance = sqrDist;
			//		closestAreaPoint = point;
			//	}
			//}
			return closestAreaPoint;
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

		//private void GenerateAreaTriangles(List<Vector3> vertices)
		//{
		//	var indicesCount = 3 * (vertices.Count - 2);

		//	 All triangles start from first vertex
		//	int v1 = 0;
		//	int v2 = 2;
		//	int v3 = 1;

		//	areaTriangles.Clear();
		//	for (int i = 0; i < indicesCount; i += 3)
		//	{
		//		areaTriangles.Add(transform.TransformPoint(vertices[v1]));
		//		areaTriangles.Add(transform.TransformPoint(vertices[v2++]));
		//		areaTriangles.Add(transform.TransformPoint(vertices[v3++]));
		//	}
		//}

		//private bool IsVertexInTriangle(Vector3 point)
		//{
		//	for (int j = 0; j < areaTriangles.Count; j += 3)
		//	{
		//		if (IsVertexInTriangle(areaTriangles[j], areaTriangles[j + 1], areaTriangles[j + 2], point))
		//		{
		//			return true;
		//		}
		//	}
		//	return false;
		//}

		//private bool IsVertexInTriangle(Vector3 posA, Vector3 posB, Vector3 posC, Vector3 point)
		//{
		//	/*if (IsOnSameSide(posA, posB, posC, point) && IsOnSameSide(posB, posC, posA, point) && IsOnSameSide(posC, posA, posB, point))
		//	{
		//		return true;
		//	}
		//	return false;*/

		//	 Compute vectors
		//	var v0 = posC - posA;
		//	var v1 = posB - posA;
		//	var v2 = point - posA;

		//	 Compute dot products
		//	var dot00 = Vector3.Dot(v0, v0);
		//	var dot01 = Vector3.Dot(v0, v1);
		//	var dot02 = Vector3.Dot(v0, v2);
		//	var dot11 = Vector3.Dot(v1, v1);
		//	var dot12 = Vector3.Dot(v1, v2);

		//	 Compute barycentric coordinates
		//	var invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
		//	var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
		//	var v = (dot00 * dot12 - dot01 * dot02) * invDenom;

		//	 Check if point is in triangle
		//	return (u >= 0) && (v >= 0) && (u + v < 1);
		//}

		//private bool IsOnSameSide(Vector3 pos1, Vector3 pos2, Vector3 pos3, Vector3 point)
		//{
		//	var a = Vector3.Cross(pos2 - pos1, point - pos1);
		//	var b = Vector3.Cross(pos2 - pos1, pos3 - pos1);
		//	if (Vector3.Dot(a, b) >= 0)
		//		return true;
		//	else
		//		return false;
		//}

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
			var p1 = (areaPoints[1] - areaPoints[0]).normalized;
			var p2 = (areaPoints[areaPoints.Count - 1] - areaPoints[0]).normalized;
			var normal = Vector3.Cross(p2, p1).normalized;

			pos.y = areaCollider.bounds.max.y;
			var ray = new Ray(pos + Vector3.up, Vector3.down);
			return areaCollider.Raycast(ray, out _, Mathf.Infinity);
		}
	}
}