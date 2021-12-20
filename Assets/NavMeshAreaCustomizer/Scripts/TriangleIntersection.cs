﻿// Copyright (c) Adam Jůva.
// Licensed under the MIT License.

using UnityEngine;

namespace NavMeshAreaCustomizer
{
	/// <summary>
	/// Class for calculating point-triangle and triangle-triangle intersections.
	/// Sources:
	/// https://blackpawn.com/texts/pointinpoly/default.html
	/// http://answers.unity.com/answers/861741/view.html
	/// </summary>
	public static class TriangleIntersection
    {
		public static bool PointAndTriangle(Vector3 posA, Vector3 posB, Vector3 posC, Vector3 point)
		{
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

		/// <summary>
		/// Checks if the triangle V(v0, v1, v2) intersects the triangle U(u0, u1, u2).
		/// Returns true if V intersects U, otherwise false.
		/// </summary>
		/// <param name="v0">Vertex 0 of V</param>
		/// <param name="v1">Vertex 1 of V</param>
		/// <param name="v2">Vertex 2 of V</param>
		/// <param name="u0">Vertex 0 of U</param>
		/// <param name="u1">Vertex 1 of U</param>
		/// <param name="u2">Vertex 2 of U</param>
		public static bool TriangleAndTriangle(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 u0, Vector3 u1, Vector3 u2)
		{
			Vector3 e1, e2;
			Vector3 n1, n2;
			Vector3 dd;
			Vector2 isect1 = Vector2.zero, isect2 = Vector2.zero;

			float du0, du1, du2, dv0, dv1, dv2, d1, d2;
			float du0du1, du0du2, dv0dv1, dv0dv2;
			float vp0, vp1, vp2;
			float up0, up1, up2;
			float bb, cc, max;

			short index;

			// compute plane equation of triangle(v0,v1,v2) 
			e1 = v1 - v0;
			e2 = v2 - v0;
			n1 = Vector3.Cross(e1, e2);
			d1 = -Vector3.Dot(n1, v0);
			// plane equation 1: N1.X+d1=0 */

			// put u0,u1,u2 into plane equation 1 to compute signed distances to the plane
			du0 = Vector3.Dot(n1, u0) + d1;
			du1 = Vector3.Dot(n1, u1) + d1;
			du2 = Vector3.Dot(n1, u2) + d1;

			// coplanarity robustness check 
			if (Mathf.Abs(du0) < Mathf.Epsilon) { du0 = 0.0f; }
			if (Mathf.Abs(du1) < Mathf.Epsilon) { du1 = 0.0f; }
			if (Mathf.Abs(du2) < Mathf.Epsilon) { du2 = 0.0f; }

			du0du1 = du0 * du1;
			du0du2 = du0 * du2;

			// same sign on all of them + not equal 0 ? 
			if (du0du1 > 0.0f && du0du2 > 0.0f)
			{
				// no intersection occurs
				return false;
			}

			// compute plane of triangle (u0,u1,u2)
			e1 = u1 - u0;
			e2 = u2 - u0;
			n2 = Vector3.Cross(e1, e2);
			d2 = -Vector3.Dot(n2, u0);

			// plane equation 2: N2.X+d2=0 
			// put v0,v1,v2 into plane equation 2
			dv0 = Vector3.Dot(n2, v0) + d2;
			dv1 = Vector3.Dot(n2, v1) + d2;
			dv2 = Vector3.Dot(n2, v2) + d2;

			if (Mathf.Abs(dv0) < Mathf.Epsilon) { dv0 = 0.0f; }
			if (Mathf.Abs(dv1) < Mathf.Epsilon) { dv1 = 0.0f; }
			if (Mathf.Abs(dv2) < Mathf.Epsilon) { dv2 = 0.0f; }


			dv0dv1 = dv0 * dv1;
			dv0dv2 = dv0 * dv2;

			// same sign on all of them + not equal 0 ? 
			if (dv0dv1 > 0.0f && dv0dv2 > 0.0f)
			{
				// no intersection occurs
				return false;
			}

			// compute direction of intersection line 
			dd = Vector3.Cross(n1, n2);

			// compute and index to the largest component of D 
			max = (float)Mathf.Abs(dd[0]);
			index = 0;
			bb = (float)Mathf.Abs(dd[1]);
			cc = (float)Mathf.Abs(dd[2]);
			if (bb > max) { max = bb; index = 1; }
			if (cc > max) { index = 2; }

			// this is the simplified projection onto L
			vp0 = v0[index];
			vp1 = v1[index];
			vp2 = v2[index];

			up0 = u0[index];
			up1 = u1[index];
			up2 = u2[index];

			// compute interval for triangle 1 
			float a = 0, b = 0, c = 0, x0 = 0, x1 = 0;
			if (ComputeIntervals(vp0, vp1, vp2, dv0, dv1, dv2, dv0dv1, dv0dv2, ref a, ref b, ref c, ref x0, ref x1))
			{
				return TriTriCoplanar(n1, v0, v1, v2, u0, u1, u2);
			}

			// compute interval for triangle 2 
			float d = 0, e = 0, f = 0, y0 = 0, y1 = 0;
			if (ComputeIntervals(up0, up1, up2, du0, du1, du2, du0du1, du0du2, ref d, ref e, ref f, ref y0, ref y1))
			{
				return TriTriCoplanar(n1, v0, v1, v2, u0, u1, u2);
			}

			float xx, yy, xxyy, tmp;
			xx = x0 * x1;
			yy = y0 * y1;
			xxyy = xx * yy;

			tmp = a * xxyy;
			isect1[0] = tmp + b * x1 * yy;
			isect1[1] = tmp + c * x0 * yy;

			tmp = d * xxyy;
			isect2[0] = tmp + e * xx * y1;
			isect2[1] = tmp + f * xx * y0;

			Sort(ref isect1);
			Sort(ref isect2);

			return !(isect1[1] < isect2[0] || isect2[1] < isect1[0]);
		}

		private static void Sort(ref Vector2 v)
		{
			if (v.x > v.y)
			{
				float c;
				c = v.x;
				v.x = v.y;
				v.y = c;
			}
		}

		/// <summary>
		/// This edge to edge test is based on Franlin Antonio's gem: "Faster Line Segment Intersection", in Graphics Gems III, pp. 199-202.
		/// </summary>
		private static bool EdgeEdgeTest(Vector3 v0, Vector3 v1, Vector3 u0, Vector3 u1, int i0, int i1)
		{
			float Ax, Ay, Bx, By, Cx, Cy, e, d, f;
			Ax = v1[i0] - v0[i0];
			Ay = v1[i1] - v0[i1];

			Bx = u0[i0] - u1[i0];
			By = u0[i1] - u1[i1];
			Cx = v0[i0] - u0[i0];
			Cy = v0[i1] - u0[i1];
			f = Ay * Bx - Ax * By;
			d = By * Cx - Bx * Cy;
			if ((f > 0 && d >= 0 && d <= f) || (f < 0 && d <= 0 && d >= f))
			{
				e = Ax * Cy - Ay * Cx;
				if (f > 0)
				{
					if (e >= 0 && e <= f) { return true; }
				}
				else
				{
					if (e <= 0 && e >= f) { return true; }
				}
			}

			return false;
		}

		private static bool EdgeAgainstTriEdges(Vector3 v0, Vector3 v1, Vector3 u0, Vector3 u1, Vector3 u2, int i0, int i1)
		{
			// Test edge u0, u1 against v0, v1
			if (EdgeEdgeTest(v0, v1, u0, u1, i0, i1))
				return true;

			// Test edge u1, u2 against v0, v1 
			if (EdgeEdgeTest(v0, v1, u1, u2, i0, i1))
				return true;

			// test edge u2,u1 against v0,v1 
			if (EdgeEdgeTest(v0, v1, u2, u0, i0, i1))
				return true;

			return false;
		}

		private static bool PointInTri(Vector3 v0, Vector3 u0, Vector3 u1, Vector3 u2, short i0, short i1)
		{
			float a, b, c, d0, d1, d2;

			// is T1 completly inside T2?
			// check if v0 is inside tri(u0,u1,u2)
			a = u1[i1] - u0[i1];
			b = -(u1[i0] - u0[i0]);
			c = -a * u0[i0] - b * u0[i1];
			d0 = a * v0[i0] + b * v0[i1] + c;

			a = u2[i1] - u1[i1];
			b = -(u2[i0] - u1[i0]);
			c = -a * u1[i0] - b * u1[i1];
			d1 = a * v0[i0] + b * v0[i1] + c;

			a = u0[i1] - u2[i1];
			b = -(u0[i0] - u2[i0]);
			c = -a * u2[i0] - b * u2[i1];
			d2 = a * v0[i0] + b * v0[i1] + c;

			if (d0 * d1 > 0.0f)
			{
				if (d0 * d2 > 0.0f)
					return true;
			}

			return false;
		}

		private static bool TriTriCoplanar(Vector3 N, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 u0, Vector3 u1, Vector3 u2)
		{
			float[] A = new float[3];
			short i0, i1;

			// first project onto an axis-aligned plane, that maximizes the area
			// of the triangles, compute indices: i0,i1. 
			A[0] = Mathf.Abs(N[0]);
			A[1] = Mathf.Abs(N[1]);
			A[2] = Mathf.Abs(N[2]);
			if (A[0] > A[1])
			{
				if (A[0] > A[2])
				{
					// A[0] is greatest
					i0 = 1;
					i1 = 2;
				}
				else
				{
					// A[2] is greatest
					i0 = 0;
					i1 = 1;
				}
			}
			else
			{
				if (A[2] > A[1])
				{
					// A[2] is greatest 
					i0 = 0;
					i1 = 1;
				}
				else
				{
					// A[1] is greatest 
					i0 = 0;
					i1 = 2;
				}
			}

			// test all edges of triangle 1 against the edges of triangle 2 
			if (EdgeAgainstTriEdges(v0, v1, u0, u1, u2, i0, i1)) 
				return true;
			if (EdgeAgainstTriEdges(v1, v2, u0, u1, u2, i0, i1)) 
				return true;
			if (EdgeAgainstTriEdges(v2, v0, u0, u1, u2, i0, i1)) 
				return true;

			// finally, test if tri1 is totally contained in tri2 or vice versa 
			if (PointInTri(v0, u0, u1, u2, i0, i1)) 
				return true;
			if (PointInTri(u0, v0, v1, v2, i0, i1)) 
				return true;

			return false;
		}

		private static bool ComputeIntervals(float VV0, float VV1, float VV2,
								  float D0, float D1, float D2, float D0D1, float D0D2,
								  ref float A, ref float B, ref float C, ref float X0, ref float X1)
		{
			if (D0D1 > 0.0f)
			{
				// here we know that D0D2<=0.0 
				// that is D0, D1 are on the same side, D2 on the other or on the plane 
				A = VV2; B = (VV0 - VV2) * D2; C = (VV1 - VV2) * D2; X0 = D2 - D0; X1 = D2 - D1;
			}
			else if (D0D2 > 0.0f)
			{
				// here we know that d0d1<=0.0 
				A = VV1; B = (VV0 - VV1) * D1; C = (VV2 - VV1) * D1; X0 = D1 - D0; X1 = D1 - D2;
			}
			else if (D1 * D2 > 0.0f || D0 != 0.0f)
			{
				// here we know that d0d1<=0.0 or that D0!=0.0 
				A = VV0; B = (VV1 - VV0) * D0; C = (VV2 - VV0) * D0; X0 = D0 - D1; X1 = D0 - D2;
			}
			else if (D1 != 0.0f)
			{
				A = VV1; B = (VV0 - VV1) * D1; C = (VV2 - VV1) * D1; X0 = D1 - D0; X1 = D1 - D2;
			}
			else if (D2 != 0.0f)
			{
				A = VV2; B = (VV0 - VV2) * D2; C = (VV1 - VV2) * D2; X0 = D2 - D0; X1 = D2 - D1;
			}
			else
			{
				return true;
			}

			return false;
		}
	}
}