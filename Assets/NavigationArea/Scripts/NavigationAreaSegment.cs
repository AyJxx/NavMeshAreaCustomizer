using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NavigationArea
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class NavigationAreaSegment : MonoBehaviour
    {
#if UNITY_EDITOR
		[Header("Terrain")]
		[Tooltip("This is mesh on which NavMesh will be generated.")]
		[SerializeField] private MeshFilter terrainMesh;
		[Tooltip("This is collider of mesh on which NavMesh will be generated.")]
		[SerializeField] private Collider terrainCollider;

		private Mesh projectedAreaMesh;

		private readonly List<Vector3> areaPoints = new List<Vector3>(); // Points in NavigationArea which are used to create arbitrary shape
		private List<Vector3>[] segmentedAreaPoints; // Same as areaPoints but line from one point to another is segmented to multiple points
		private readonly List<Vector3> areaTriangles = new List<Vector3>();

		private readonly List<Vector3> projectedAreaVertices = new List<Vector3>(); // These are vertices of mesh created in NavigationArea
		private readonly List<int> projectedAreaTriangles = new List<int>();
		private bool[] projectedVerticesTransformed;

		private Vector3[] terrainVertices;
		private int[] terrainTriangles;

		private bool isUpdating = false; // Flag is secondary thread is already calculating projected area mesh
		private bool pendingCalculation = false; // Flag if there is needed final calculation after user defined navigation area
		private bool isDestroyed = false;

		private MeshFilter projectedArea;
		private MeshFilter ProjectedArea
		{
			get
			{
				if (!projectedArea)
					projectedArea = GetComponent<MeshFilter>();
				return projectedArea;
			}
		}

		private MeshRenderer areaRenderer;
		private MeshRenderer AreaRenderer
		{
			get
			{
				if (!areaRenderer)
					areaRenderer = GetComponent<MeshRenderer>();
				return areaRenderer;
			}
		}

		public bool RenderArea { get; set; }
		public float AreaLineThickness { get; set; }
		public float SegmentedLineStep { get; set; }
		
		public void OnValidate()
		{
			if (terrainMesh)
			{
				terrainVertices = terrainMesh.sharedMesh.vertices;
				terrainTriangles = terrainMesh.sharedMesh.triangles;
				projectedVerticesTransformed = new bool[terrainVertices.Length];
			}
		}

		private void Awake()
		{
			projectedAreaMesh = new Mesh();
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

			for (int i = 0; i < transform.childCount; i++)
			{
				var p1 = transform.GetChild(i).position;
				var p2 = i == transform.childCount - 1 ? transform.GetChild(0).position : transform.GetChild(i + 1).position;

				Handles.color = Color.red;
				Handles.DrawBezier(p1, p2, p1, p2, Color.red, null, AreaLineThickness);
			}
		}

		public void Initialize()
		{
			CreatePoint(new Vector3(0.0f, 0.0f, 5.0f));
			CreatePoint(new Vector3(-5.0f, 0.0f, -5.0f));
			CreatePoint(new Vector3(5.0f, 0.0f, -5.0f));

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
			AreaRenderer.sharedMaterial = material;
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

			AlignAreaPoints();

			if (!isUpdating)
			{
				UpdateArea();
				GenerateProjectedArea();
			}
			else
			{
				pendingCalculation = true;
			}
		}

		private void AssignClosestTerrain()
		{
			var hits = Physics.RaycastAll(transform.position + transform.up * 100.0f, -transform.up, Mathf.Infinity);
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

		private void AlignAreaPoints()
		{
			for (int i = 0; i < transform.childCount; i++)
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

			for (int i = 0; i < transform.childCount; i++)
			{
				areaPoints.Add(transform.GetChild(i).localPosition);
			}

			GenerateAreaTriangles(areaPoints);
			SegmentArea(areaPoints);
		}

		private void SegmentArea(List<Vector3> areaPoints)
		{
			if (segmentedAreaPoints == null || segmentedAreaPoints.Length != areaPoints.Count)
				segmentedAreaPoints = new List<Vector3>[areaPoints.Count];

			for (int i = 0; i < areaPoints.Count; i++)
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
				
				for (float step = 0.0f; step < dist; step += SegmentedLineStep)
				{
					var p = p1 + dir * step;
					var segmentedPoint = ProjectOnTerrain(transform.TransformPoint(p)); // World space
					segmentedAreaPoints[i].Add(transform.InverseTransformPoint(segmentedPoint));
				}
			}
		}

		// Using collider calculated in UpdateArea function to recreate terrain mesh but only in NavigationArea,
		// then this mesh is used for NavMesh baking
		private async void GenerateProjectedArea()
		{
			isUpdating = true;

			var terrainLocalToWorld = terrainMesh.transform.localToWorldMatrix;
			var areaWorldToLocal = transform.worldToLocalMatrix;

			await Task.Run(() =>
			{
				projectedAreaVertices.Clear();
				projectedAreaTriangles.Clear();

				for (int i = 0; i < projectedVerticesTransformed.Length; i++)
				{
					projectedVerticesTransformed[i] = false;
				}

				for (int i = 0; i < terrainTriangles.Length; i += 3)
				{
					var index1 = terrainTriangles[i];
					var index2 = terrainTriangles[i + 1];
					var index3 = terrainTriangles[i + 2];

					var terrainVertex1 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index1])); // Area space
					var terrainVertex2 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index2])); // Area space
					var terrainVertex3 = areaWorldToLocal.MultiplyPoint(terrainLocalToWorld.MultiplyPoint(terrainVertices[index3])); // Area space

					bool addTriangle = false;

					CalculateProjectedAreaVertex(terrainVertex1, index1, ref addTriangle);
					CalculateProjectedAreaVertex(terrainVertex2, index2, ref addTriangle);
					CalculateProjectedAreaVertex(terrainVertex3, index3, ref addTriangle);

					if (addTriangle)
					{
						projectedAreaTriangles.Add(index1);
						projectedAreaTriangles.Add(index2);
						projectedAreaTriangles.Add(index3);
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

			if (pendingCalculation)
			{
				pendingCalculation = false;
				CalculateArea(false);
			}
		}

		private void CalculateProjectedAreaVertex(Vector3 terrainVertexAreaSpace, int index, ref bool addTriangle)
		{
			if (IsVertexInArea(terrainVertexAreaSpace))
			{
				if (!projectedVerticesTransformed[index])
				{
					projectedAreaVertices.Add(terrainVertexAreaSpace);
					projectedVerticesTransformed[index] = true;
				}
				addTriangle = true;
			}
			else if (!projectedVerticesTransformed[index])
			{
				var closestAreaPoint = GetClosestAreaPoint(terrainVertexAreaSpace);
				projectedAreaVertices.Add(closestAreaPoint);
				projectedVerticesTransformed[index] = true;
			}
		}

		private Vector3 GetClosestAreaPoint(Vector3 terrainVertexAreaSpace)
		{
			var closestAreaPointSqrDist = float.MaxValue;
			var closestAreaPointIndex = 0;
			for (int i = 0; i < areaPoints.Count; i++)
			{
				var sqrDist = (areaPoints[i] - terrainVertexAreaSpace).sqrMagnitude;
				if (sqrDist < closestAreaPointSqrDist)
				{
					closestAreaPointSqrDist = sqrDist;
					closestAreaPointIndex = i;
				}
			}

			var closestSegmentedPointSqrDist = float.MaxValue;
			var closestSegmentedPoint = Vector3.zero;
			foreach (var point in segmentedAreaPoints[closestAreaPointIndex])
			{
				var sqrDist = (point - terrainVertexAreaSpace).sqrMagnitude;
				if (sqrDist < closestSegmentedPointSqrDist)
				{
					closestSegmentedPointSqrDist = sqrDist;
					closestSegmentedPoint = point;
				}
			}

			var previousIndex = closestAreaPointIndex == 0 ? areaPoints.Count - 1 : closestAreaPointIndex - 1;
			foreach (var point in segmentedAreaPoints[previousIndex])
			{
				var sqrDist = (point - terrainVertexAreaSpace).sqrMagnitude;
				if (sqrDist < closestSegmentedPointSqrDist)
				{
					closestSegmentedPointSqrDist = sqrDist;
					closestSegmentedPoint = point;
				}
			}

			var nextIndex = ++closestAreaPointIndex % areaPoints.Count;
			foreach (var point in segmentedAreaPoints[nextIndex])
			{
				var sqrDist = (point - terrainVertexAreaSpace).sqrMagnitude;
				if (sqrDist < closestSegmentedPointSqrDist)
				{
					closestSegmentedPointSqrDist = sqrDist;
					closestSegmentedPoint = point;
				}
			}

			return closestSegmentedPoint;
		}

		/// <summary>
		/// Area is recreated to triangles so each terrain vertex can be tested against them to know if is in area or not.
		/// </summary>
		/// <param name="vertices">Vertices for which triangles are created.</param>
		private void GenerateAreaTriangles(List<Vector3> vertices)
		{
			var indicesCount = 3 * (vertices.Count - 2);

			// All triangles start from first vertex
			int v1 = 0;
			int v2 = 2;
			int v3 = 1;

			areaTriangles.Clear();
			for (int i = 0; i < indicesCount; i += 3)
			{
				areaTriangles.Add(vertices[v1]);
				areaTriangles.Add(vertices[v2++]);
				areaTriangles.Add(vertices[v3++]);
			}
		}

		private bool IsVertexInArea(Vector3 point)
		{
			for (int j = 0; j < areaTriangles.Count; j += 3)
			{
				if (IsVertexInArea(areaTriangles[j], areaTriangles[j + 1], areaTriangles[j + 2], point))
				{
					return true;
				}
			}
			return false;
		}

		private bool IsVertexInArea(Vector3 posA, Vector3 posB, Vector3 posC, Vector3 point)
		{
			posA.y = posB.y = posC.y = point.y = 0;
			/*if (IsOnSameSide(posA, posB, posC, point) && IsOnSameSide(posB, posC, posA, point) && IsOnSameSide(posC, posA, posB, point))
			{
				return true;
			}
			return false;*/

			// Compute vectors
			var v0 = posC - posA;
			var v1 = posB - posA;
			var v2 = point - posA;

			// Compute dot products
			var dot00 = Vector3.Dot(v0, v0);
			var dot01 = Vector3.Dot(v0, v1);
			var dot02 = Vector3.Dot(v0, v2);
			var dot11 = Vector3.Dot(v1, v1);
			var dot12 = Vector3.Dot(v1, v2);

			// Compute barycentric coordinates
			var invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);
			var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
			var v = (dot00 * dot12 - dot01 * dot02) * invDenom;

			// Check if point is in triangle
			return (u >= 0.0f) && (v >= 0.0f) && (u + v < 1.0f);
		}

		private bool IsOnSameSide(Vector3 pos1, Vector3 pos2, Vector3 pos3, Vector3 point)
		{
			var a = Vector3.Cross(pos2 - pos1, point - pos1);
			var b = Vector3.Cross(pos2 - pos1, pos3 - pos1);
			if (Vector3.Dot(a, b) >= 0)
				return true;
			else
				return false;
		}

		// NavigationArea is operating on XZ plane, so this method
		// projects any point on terrain collider (on correct Y coordinate)
		private Vector3 ProjectOnTerrain(Vector3 worldPos)
		{
			var highestPoint = worldPos;
			highestPoint.y = terrainCollider.bounds.max.y;
			var ray = new Ray(highestPoint + Vector3.up, Vector3.down);

			if (terrainCollider.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity))
				return hitInfo.point;
			else
				return worldPos;
		}

		//// Generating triangles for supplied vertices
		//private int[] GenerateTriangles(List<Vector3> vertices)
		//{
		//	var indices = new int[3 * (vertices.Count - 2)];

		//	// All triangles start from first vertex
		//	int v1 = 0;
		//	int v2 = 2;
		//	int v3 = 1;

		//	for (int i = 0; i < indices.Length; i += 3)
		//	{
		//		indices[i] = v1;
		//		indices[i + 1] = v2++;
		//		indices[i + 2] = v3++;
		//	}

		//	return indices;
		//}

		//// Method which checks if vertex lays in navigation area collider (ignoring Y coordinate)
		//private bool IsVertexInAreaTest(Vector3 pos)
		//{
		//	pos.y = areaCollider.bounds.max.y;
		//	var ray = new Ray(pos + Vector3.up, Vector3.down);
		//	return areaCollider.Raycast(ray, out _, Mathf.Infinity);
		//}
#endif
	}
}