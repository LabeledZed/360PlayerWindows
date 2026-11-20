using OculusWrap;
using SharpDX;
using SharpDX.Toolkit.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.Tools
{
	public class GraphicTools
	{
		private static readonly Dictionary<int, Vector4> FacebookCubeMap = new Dictionary<int, Vector4>
		{
			{ 2, new Vector4(3f, 0f / 3f, 2f, 0f) },
			{ 3, new Vector4(3f, 1f / 3f, 2f, 0f) },
			{ 4, new Vector4(3f, 2f / 3f, 2f, 0f) },
			{ 5, new Vector4(3f, 0f / 3f, 2f, 1f / 2f) },
			{ 1, new Vector4(3f, 1f / 3f, 2f, 1f / 2f) },
			{ 0, new Vector4(3f, 2f / 3f, 2f, 1f / 2f) }
		};

		public static Vector2 MapCubeFacebook(int index, Vector2 vector)
		{
			Vector2 vector2 = vector;
			if (index == 5)
				vector2 = new Vector2(1 - vector.X, 1 - vector.Y);

			Vector4 mapping = FacebookCubeMap[index];
			return new Vector2(
				vector2.X / mapping.X + mapping.Y,
				vector2.Y / mapping.Z + mapping.W
				);
		}

		public static GeometricPrimitive GenerateFacebookCube(GraphicsDevice graphicsDevice, bool toLeftHanded)
		{
			using (GeometricPrimitive primitive = SharpDX.Toolkit.Graphics.GeometricPrimitive.Cube.New(graphicsDevice, 6, toLeftHanded))
			{
				short[] indices = primitive.IndexBuffer.GetData<short>();
				var data = primitive.VertexBuffer.GetData();

				float offset = 0.99f;

				for (int it = 0; it < data.Length; it++)
				{
					//0,0
					//0,1
					//1,1
					//1,0
					int index = (int)Math.Floor(it / 4f);
					data[it].TextureCoordinate = MapCubeFacebook(index, data[it].TextureCoordinate);
					switch (index)
					{
						case 0:
							data[it].Position.Z *= offset;
							break;
						case 1:
							data[it].Position.Z *= offset;
							break;
						case 2:
							data[it].Position.X *= offset;
							break;
						case 3:
							data[it].Position.X *= offset;
							break;
						case 4:
							data[it].Position.Y *= offset;
							break;
						case 5:
							data[it].Position.Y *= offset;
							break;
					}
				}

				return new GeometricPrimitive(graphicsDevice, data, indices);
			}
		}


		/// <summary>
		/// Maps a 360° sphere UV coordinate (where front hemisphere has u in [0.25, 0.75], v in [0, 1])
		/// to a 180° Equirectangular (Dome / VR180 / _LR) UV coordinate in [0, 1] x [0, 1].
		/// </summary>
		public static Vector2 ComputeEquirectangular180UV(Vector2 sphereUV)
		{
			float u = MathUtil.Clamp(sphereUV.X * 2f - 0.5f, 0f, 1f);
			float v = MathUtil.Clamp(sphereUV.Y, 0f, 1f);
			return new Vector2(u, v);
		}

		/// <summary>
		/// Maps a 360° sphere UV coordinate (where front hemisphere has u in [0.25, 0.75], v in [0, 1])
		/// to a 180° equidistant circular Fisheye / Dual-Fisheye UV coordinate in [0, 1] x [0, 1].
		/// </summary>
		public static Vector2 ComputeFisheye180UV(Vector2 sphereUV, float fovDegrees = 180f)
		{
			if (fovDegrees <= 1f)
				fovDegrees = 180f;

			double yaw = (sphereUV.X - 0.5) * (2.0 * Math.PI);
			double pitch = (0.5 - sphereUV.Y) * Math.PI;

			double cosPitch = Math.Cos(pitch);
			double nx = cosPitch * Math.Sin(yaw);
			double ny = Math.Sin(pitch);
			double nz = cosPitch * Math.Cos(yaw);

			double rho = Math.Sqrt(nx * nx + ny * ny);
			if (rho < 1e-7)
			{
				return new Vector2(0.5f, 0.5f);
			}

			double theta = Math.Atan2(rho, nz); // Angle from forward optical axis [0, pi/2]
			double halfFovRad = (fovDegrees * Math.PI) / 360.0;
			double r = theta / halfFovRad;

			double u = 0.5 + 0.5 * r * (nx / rho);
			double v = 0.5 - 0.5 * r * (ny / rho);

			return new Vector2(
				MathUtil.Clamp((float)u, 0f, 1f),
				MathUtil.Clamp((float)v, 0f, 1f));
		}

		/// <summary>
		/// Maps a 360° sphere UV coordinate (where front hemisphere has u in [0.25, 0.75], v in [0, 1])
		/// to a VR180 Mesh-Projected (Equi-Angular / lens-distortion-compensated 180°) UV coordinate in [0, 1] x [0, 1].
		/// </summary>
		public static Vector2 ComputeMesh180UV(Vector2 sphereUV)
		{
			// Normalized front-hemisphere angular coordinates in [-1, 1]
			double ax = MathUtil.Clamp(sphereUV.X * 4f - 2f, -1f, 1f);
			double ay = MathUtil.Clamp(1f - sphereUV.Y * 2f, -1f, 1f);

			// Equi-Angular VR180 mesh curve: boosts center optical resolution and unwarps peripheral compression
			const double kappa = 1.15;
			double invAtanKappa = 1.0 / Math.Atan(kappa);

			double mappedX = Math.Atan(kappa * ax) * invAtanKappa;
			double mappedY = Math.Atan(kappa * ay) * invAtanKappa;

			float u = MathUtil.Clamp((float)(0.5 + 0.5 * mappedX), 0f, 1f);
			float v = MathUtil.Clamp((float)(0.5 - 0.5 * mappedY), 0f, 1f);
			return new Vector2(u, v);
		}

		private static GeometricPrimitive GenerateHemisphere(
			GraphicsDevice graphicsDevice,
			bool toLeftHanded,
			Func<Vector2, Vector2> uvMapper)
		{
			using (GeometricPrimitive primitive = GeometricPrimitive.Sphere.New(graphicsDevice, 50, 64, toLeftHanded))
			{
				short[] sourceIndices = primitive.IndexBuffer.GetData<short>();
				var data = primitive.VertexBuffer.GetData();

				Vector2[] originalUVs = new Vector2[data.Length];
				bool[] isPole = new bool[data.Length];

				for (int it = 0; it < data.Length; it++)
				{
					Vector2 uv = data[it].TextureCoordinate;
					originalUVs[it] = uv;
					isPole[it] = uv.Y <= 1e-4f || uv.Y >= 1f - 1e-4f || Math.Abs(data[it].Normal.Y) >= 0.9999f;
					if (isPole[it])
					{
						uv.X = 0.5f;
					}
					data[it].TextureCoordinate = uvMapper(uv);
				}

				// Keep only triangle triplets (i0, i1, i2) belonging to the front 180° hemisphere (u360 in [0.25, 0.75])
				// so the back 180° hemisphere renders black instead of tiling/stretching the front texture.
				List<short> frontIndices = new List<short>(sourceIndices.Length / 2 + 384);
				const float minFrontU = 0.25f - 1e-4f;
				const float maxFrontU = 0.75f + 1e-4f;

				for (int t = 0; t + 2 < sourceIndices.Length; t += 3)
				{
					short i0 = sourceIndices[t];
					short i1 = sourceIndices[t + 1];
					short i2 = sourceIndices[t + 2];

					bool v0Front = isPole[i0] || (originalUVs[i0].X >= minFrontU && originalUVs[i0].X <= maxFrontU);
					bool v1Front = isPole[i1] || (originalUVs[i1].X >= minFrontU && originalUVs[i1].X <= maxFrontU);
					bool v2Front = isPole[i2] || (originalUVs[i2].X >= minFrontU && originalUVs[i2].X <= maxFrontU);

					if (v0Front && v1Front && v2Front)
					{
						frontIndices.Add(i0);
						frontIndices.Add(i1);
						frontIndices.Add(i2);
					}
				}

				short[] finalIndices = frontIndices.Count >= 3 ? frontIndices.ToArray() : sourceIndices;
				return new GeometricPrimitive(graphicsDevice, data, finalIndices);
			}
		}

		public static GeometricPrimitive GenerateDome(GraphicsDevice graphicsDevice, bool toLeftHanded)
		{
			return GenerateHemisphere(graphicsDevice, toLeftHanded, ComputeEquirectangular180UV);
		}

		public static GeometricPrimitive GenerateFisheye180(GraphicsDevice graphicsDevice, bool toLeftHanded, float fovDegrees = 180f)
		{
			return GenerateHemisphere(graphicsDevice, toLeftHanded, uv => ComputeFisheye180UV(uv, fovDegrees));
		}

		public static GeometricPrimitive GenerateMeshProjected180(GraphicsDevice graphicsDevice, bool toLeftHanded)
		{
			var customMesh = MediaDecoder.Instance?.CurrentServiceResult?.CustomMesh;
			if (customMesh != null && customMesh.IsValid)
			{
				var srcVertices = customMesh.Vertices;
				var srcIndices = customMesh.Indices;
				var vertices = new VertexPositionNormalTexture[srcVertices.Length];
				var indices = new short[srcIndices.Length];
				Array.Copy(srcVertices, vertices, srcVertices.Length);
				Array.Copy(srcIndices, indices, srcIndices.Length);

				if (toLeftHanded)
				{
					for (int i = 0; i < vertices.Length; i++)
					{
						vertices[i].Position.Z = -vertices[i].Position.Z;
						vertices[i].Normal.Z = -vertices[i].Normal.Z;
					}
					for (int t = 0; t + 2 < indices.Length; t += 3)
					{
						short tmp = indices[t];
						indices[t] = indices[t + 2];
						indices[t + 2] = tmp;
					}
				}
				return new GeometricPrimitive(graphicsDevice, vertices, indices);
			}

			return GenerateHemisphere(graphicsDevice, toLeftHanded, ComputeMesh180UV);
		}


		public static GeometricPrimitive CreateGeometry(ProjectionMode projection, GraphicsDevice graphicsDevice, bool toLeftHanded=true)
		{
			switch (projection)
			{
				case ProjectionMode.Sphere:
					return GeometricPrimitive.Sphere.New(graphicsDevice, 6, 64, toLeftHanded);

				case ProjectionMode.CubeFacebook:
					return GenerateFacebookCube(graphicsDevice, toLeftHanded);

				case ProjectionMode.Autodetect:
				case ProjectionMode.Dome:
					return GenerateDome(graphicsDevice, toLeftHanded);

				case ProjectionMode.Fisheye180:
					return GenerateFisheye180(graphicsDevice, toLeftHanded);

				case ProjectionMode.Mesh180:
					return GenerateMeshProjected180(graphicsDevice, toLeftHanded);

				default:
					throw new ArgumentException("Unknown projection: " + projection);
			}
		}


		public static Vector2 QuaternionToYawPitch(OVRTypes.Quaternionf ovrQuatf)
		{
			return QuaternionToYawPitch(new Quaternion(-ovrQuatf.X, -ovrQuatf.Y, -ovrQuatf.Z, ovrQuatf.W));
		}

		public static Vector2 QuaternionToYawPitch(Quaternion qVeryWTF)
		{
            // visual studio compiler bug? 
            // without this the parameter quaternion is modified by line #208 and this change persists in the calling scope... sometimes
            Quaternion q = new Quaternion(qVeryWTF.X, qVeryWTF.Y, qVeryWTF.Z, qVeryWTF.W);

			q.Normalize();

			var x = 0f;
			var y = 0f;
			var z = 1f;

			var pole = q.X * q.W + q.Z * q.Y;
			q = q * Quaternion.RotationYawPitchRoll(MathUtil.DegreesToRadians(90) * x, MathUtil.DegreesToRadians(90) * y, MathUtil.DegreesToRadians(90) * z);

			q.Normalize();

			double yaw = 0, pitch = 0;

			if (pole > 0.49999f)
			{
				yaw = 2f * (float)Math.Atan2(q.Y, q.X) + Math.PI * 0.5f;
				pitch = -Math.PI * 0.5f;
			}
			else if (pole < -0.49999f)
			{
				yaw = 2f * (float)Math.Atan2(q.Y, q.Z) - Math.PI * 0.5f;
				pitch = Math.PI * 0.5f;
			}
			else
			{
				yaw = Math.Atan2(2 * (q.Y * q.Z + q.W * q.X), q.W * q.W - q.X * q.X - q.Y * q.Y + q.Z * q.Z);
				double sinPitch = Math.Max(-1.0, Math.Min(1.0, -2 * (q.X * q.Z - q.W * q.Y)));
				pitch = Math.Asin(sinPitch);
				//roll = Math.Atan2(2 * (q.X * q.Y + q.W * q.Z), q.W * q.W + q.X * q.X - q.Y * q.Y - q.Z * q.Z);
			}

			yaw = yaw > Math.PI ? yaw - 2 * Math.PI : (yaw < -Math.PI ? yaw + 2 * Math.PI : yaw);

			return new Vector2((float)yaw, (float)pitch);
		}

	}
}
