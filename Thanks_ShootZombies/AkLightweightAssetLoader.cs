using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShootZombies;

internal static class AkLightweightAssetLoader
{
	private const string ResourceFolderName = "AK_Resources";

	private const string CanonicalModelFileName = "ak_model.obj";

	private const string CanonicalTextureFileName = "ak_texture.png";

	private const string CanonicalIconFileName = "ak_icon.png";

	private static readonly string[] ModelFileNames = new string[3] { "ak_model.obj", "-3728671120793114700_Cube.obj", "Cube.obj" };

	private static readonly string[] TextureFileNames = new string[3] { "ak_texture.png", "AK-47_type_II.png", "ak47.png" };

	private static readonly string[] IconFileNames = new string[3] { "ak_icon.png", "AK-47_type_II_icon.png", "ak47_icon.png" };

	private static readonly Vector3 MeshLocalPosition = new Vector3(0f, 0f, 0.11863899f);

	private static readonly Quaternion MeshLocalRotation = new Quaternion(1.1009293E-08f, -0.70710677f, -0.7071068f, -2.2351747E-08f);

	private static readonly Vector3 MeshLocalScale = new Vector3(0.3060086f, 0.18934934f, 0.18934931f);

	private static readonly Vector3 SpawnLocalPosition = new Vector3(0.002f, 0.036f, 0.5118f);

	private static readonly Quaternion SpawnLocalRotation = Quaternion.identity;

	private static readonly Vector3 SpawnLocalScale = new Vector3(0.7339988f, 0.7339988f, 0.7339988f);

	private struct ObjVertexKey : IEquatable<ObjVertexKey>
	{
		public int PositionIndex;

		public int UvIndex;

		public int NormalIndex;

		public bool Equals(ObjVertexKey other)
		{
			if (PositionIndex == other.PositionIndex && UvIndex == other.UvIndex)
			{
				return NormalIndex == other.NormalIndex;
			}
			return false;
		}

		public override bool Equals(object obj)
		{
			if (!(obj is ObjVertexKey))
			{
				return false;
			}
			return Equals((ObjVertexKey)obj);
		}

		public override int GetHashCode()
		{
			int num = 17;
			num = num * 31 + PositionIndex;
			num = num * 31 + UvIndex;
			return num * 31 + NormalIndex;
		}
	}

	private struct ObjFaceVertex
	{
		public int PositionIndex;

		public int UvIndex;

		public int NormalIndex;
	}

	public static bool TryLoad(out GameObject prefab, out string diagnostic)
	{
		prefab = null;
		diagnostic = string.Empty;
		if (!TryResolveResourcePaths(out var modelPath, out var texturePath, out diagnostic))
		{
			return false;
		}
		if (!TryLoadObjMesh(modelPath, out var mesh, out var meshSummary))
		{
			diagnostic = "OBJ load failed: " + modelPath + " (" + meshSummary + ")";
			return false;
		}
		Texture2D texture = LoadTexture(texturePath, out var textureSummary);
		Material material = CreateMaterial(texture, texturePath);
		if ((UnityEngine.Object)material == (UnityEngine.Object)null)
		{
			diagnostic = "Material creation failed for texture: " + texturePath;
			return false;
		}
		prefab = BuildPrefab(mesh, material);
		if ((UnityEngine.Object)prefab == (UnityEngine.Object)null)
		{
			diagnostic = "Prefab build failed";
			return false;
		}
		diagnostic = "model=" + Path.GetFileName(modelPath) + ", texture=" + Path.GetFileName(texturePath) + ", " + meshSummary + ", " + textureSummary;
		return true;
	}

	public static bool TryLoadIconTexture(out Texture2D texture, out string diagnostic)
	{
		texture = null;
		diagnostic = string.Empty;
		List<string> list = GetSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string item in list)
		{
			string text = ResolveFirstExistingPath(item, IconFileNames);
			if (!string.IsNullOrWhiteSpace(text))
			{
				texture = LoadTexture(text, out var summary);
				if ((UnityEngine.Object)texture != (UnityEngine.Object)null)
				{
					diagnostic = "icon=" + Path.GetFileName(text) + ", " + summary;
					return true;
				}
				diagnostic = "icon decode failed: " + text + ", " + summary;
				return false;
			}
		}
		diagnostic = "searched=" + string.Join(" | ", list);
		return false;
	}

	private static bool TryResolveResourcePaths(out string modelPath, out string texturePath, out string diagnostic)
	{
		modelPath = string.Empty;
		texturePath = string.Empty;
		List<string> list = GetSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string item in list)
		{
			string text = ResolveFirstExistingPath(item, ModelFileNames);
			string text2 = ResolveFirstExistingPath(item, TextureFileNames);
			if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
			{
				modelPath = text;
				texturePath = text2;
				diagnostic = "resolved at " + item;
				return true;
			}
		}
		diagnostic = "searched=" + string.Join(" | ", list);
		return false;
	}

	private static IEnumerable<string> GetSearchDirectories()
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in GetSearchRoots())
		{
			if (string.IsNullOrWhiteSpace(item))
			{
				continue;
			}
			string fullPath = GetFullPath(item);
			if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath) || !seen.Add(fullPath))
			{
				continue;
			}
			yield return fullPath;
			string text = Path.Combine(fullPath, ResourceFolderName);
			if (Directory.Exists(text))
			{
				string fullPath2 = GetFullPath(text);
				if (!string.IsNullOrWhiteSpace(fullPath2) && seen.Add(fullPath2))
				{
					yield return fullPath2;
				}
			}
		}
	}

	private static IEnumerable<string> GetSearchRoots()
	{
		string location = Assembly.GetExecutingAssembly().Location;
		string text = (string.IsNullOrWhiteSpace(location) ? string.Empty : (Path.GetDirectoryName(location) ?? string.Empty));
		string text2 = Paths.PluginPath ?? string.Empty;
		string text3 = (string.IsNullOrWhiteSpace(text2) ? string.Empty : (Directory.GetParent(text2)?.FullName ?? string.Empty));
		if (!string.IsNullOrWhiteSpace(text))
		{
			yield return text;
		}
		if (!string.IsNullOrWhiteSpace(text2))
		{
			yield return text2;
		}
		if (!string.IsNullOrWhiteSpace(text3))
		{
			yield return text3;
		}
		yield return Path.Combine(Environment.CurrentDirectory ?? string.Empty, ResourceFolderName);
	}

	private static string GetFullPath(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string ResolveFirstExistingPath(string directory, IEnumerable<string> fileNames)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return string.Empty;
		}
		foreach (string fileName in fileNames)
		{
			string text = Path.Combine(directory, fileName);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return string.Empty;
	}

	private static bool TryLoadObjMesh(string modelPath, out Mesh mesh, out string summary)
	{
		mesh = null;
		summary = string.Empty;
		List<Vector3> list = new List<Vector3>(4096);
		List<Vector2> list2 = new List<Vector2>(4096);
		List<Vector3> list3 = new List<Vector3>(4096);
		List<Vector3> list4 = new List<Vector3>(4096);
		List<Vector2> list5 = new List<Vector2>(4096);
		List<Vector3> list6 = new List<Vector3>(4096);
		List<int> list7 = new List<int>(12288);
		Dictionary<ObjVertexKey, int> dictionary = new Dictionary<ObjVertexKey, int>();
		bool flag = true;
		try
		{
			foreach (string item in File.ReadLines(modelPath))
			{
				if (string.IsNullOrWhiteSpace(item) || item[0] == '#')
				{
					continue;
				}
				if (item.StartsWith("v ", StringComparison.Ordinal))
				{
					if (TryParseVector3(item, out var vector))
					{
						list.Add(vector);
					}
					continue;
				}
				if (item.StartsWith("vt ", StringComparison.Ordinal))
				{
					if (TryParseVector2(item, out var vector2))
					{
						list2.Add(vector2);
					}
					continue;
				}
				if (item.StartsWith("vn ", StringComparison.Ordinal))
				{
					if (TryParseVector3(item, out var vector3))
					{
						list3.Add(vector3);
					}
					continue;
				}
				if (!item.StartsWith("f ", StringComparison.Ordinal))
				{
					continue;
				}
				string[] array = item.Substring(2).Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length < 3)
				{
					continue;
				}
				List<ObjFaceVertex> list8 = new List<ObjFaceVertex>(array.Length);
				string[] array2 = array;
				foreach (string token in array2)
				{
					if (TryParseFaceVertex(token, list.Count, list2.Count, list3.Count, out var vertex))
					{
						list8.Add(vertex);
						if (vertex.NormalIndex < 0)
						{
							flag = false;
						}
					}
				}
				if (list8.Count < 3)
				{
					continue;
				}
				for (int i = 1; i < list8.Count - 1; i++)
				{
					AddTriangleVertex(list8[0], list, list2, list3, list4, list5, list6, list7, dictionary);
					AddTriangleVertex(list8[i], list, list2, list3, list4, list5, list6, list7, dictionary);
					AddTriangleVertex(list8[i + 1], list, list2, list3, list4, list5, list6, list7, dictionary);
				}
			}
			if (list4.Count == 0 || list7.Count == 0)
			{
				summary = "no mesh data";
				return false;
			}
			mesh = new Mesh();
			mesh.name = "AK_RuntimeMesh";
			if (list4.Count > 65535)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(list4);
			if (list5.Count == list4.Count)
			{
				mesh.SetUVs(0, list5);
			}
			mesh.subMeshCount = 1;
			mesh.SetTriangles(list7, 0, true);
			if (flag && list6.Count == list4.Count)
			{
				mesh.SetNormals(list6);
			}
			else
			{
				mesh.RecalculateNormals();
			}
			mesh.RecalculateBounds();
			TryRecalculateTangents(mesh);
			summary = "verts=" + list4.Count + ", tris=" + list7.Count / 3;
			return true;
		}
		catch (Exception ex)
		{
			mesh = null;
			summary = ex.GetType().Name + ": " + ex.Message;
			return false;
		}
	}

	private static bool TryParseVector3(string line, out Vector3 vector)
	{
		vector = Vector3.zero;
		string[] array = line.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 4)
		{
			return false;
		}
		if (float.TryParse(array[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && float.TryParse(array[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) && float.TryParse(array[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var result3))
		{
			vector = new Vector3(result, result2, result3);
			return true;
		}
		return false;
	}

	private static bool TryParseVector2(string line, out Vector2 vector)
	{
		vector = Vector2.zero;
		string[] array = line.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 3)
		{
			return false;
		}
		if (float.TryParse(array[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && float.TryParse(array[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			vector = new Vector2(result, result2);
			return true;
		}
		return false;
	}

	private static bool TryParseFaceVertex(string token, int positionCount, int uvCount, int normalCount, out ObjFaceVertex vertex)
	{
		vertex = default(ObjFaceVertex);
		if (string.IsNullOrWhiteSpace(token))
		{
			return false;
		}
		string[] array = token.Split('/');
		if (array.Length == 0)
		{
			return false;
		}
		vertex.PositionIndex = ResolveObjIndex(ParseObjIndex(array[0]), positionCount);
		vertex.UvIndex = ((array.Length >= 2 && !string.IsNullOrWhiteSpace(array[1])) ? ResolveObjIndex(ParseObjIndex(array[1]), uvCount) : (-1));
		vertex.NormalIndex = ((array.Length >= 3 && !string.IsNullOrWhiteSpace(array[2])) ? ResolveObjIndex(ParseObjIndex(array[2]), normalCount) : (-1));
		return vertex.PositionIndex >= 0;
	}

	private static int ParseObjIndex(string value)
	{
		int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result);
		return result;
	}

	private static int ResolveObjIndex(int rawIndex, int count)
	{
		if (rawIndex > 0)
		{
			return rawIndex - 1;
		}
		if (rawIndex < 0)
		{
			return count + rawIndex;
		}
		return -1;
	}

	private static void AddTriangleVertex(ObjFaceVertex faceVertex, List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals, List<Vector3> outVertices, List<Vector2> outUvs, List<Vector3> outNormals, List<int> outTriangles, Dictionary<ObjVertexKey, int> cache)
	{
		ObjVertexKey key = default(ObjVertexKey);
		key.PositionIndex = faceVertex.PositionIndex;
		key.UvIndex = faceVertex.UvIndex;
		key.NormalIndex = faceVertex.NormalIndex;
		if (!cache.TryGetValue(key, out var value))
		{
			value = outVertices.Count;
			cache[key] = value;
			outVertices.Add(positions[faceVertex.PositionIndex]);
			outUvs.Add((faceVertex.UvIndex >= 0 && faceVertex.UvIndex < uvs.Count) ? uvs[faceVertex.UvIndex] : Vector2.zero);
			outNormals.Add((faceVertex.NormalIndex >= 0 && faceVertex.NormalIndex < normals.Count) ? normals[faceVertex.NormalIndex] : Vector3.zero);
		}
		outTriangles.Add(value);
	}

	private static void TryRecalculateTangents(Mesh mesh)
	{
		if ((UnityEngine.Object)mesh == (UnityEngine.Object)null)
		{
			return;
		}
		try
		{
			MethodInfo method = typeof(Mesh).GetMethod("RecalculateTangents", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			method?.Invoke(mesh, null);
		}
		catch
		{
		}
	}

	private static Texture2D LoadTexture(string texturePath, out string summary)
	{
		summary = "texture=missing";
		if (string.IsNullOrWhiteSpace(texturePath) || !File.Exists(texturePath))
		{
			return null;
		}
		try
		{
			byte[] array = File.ReadAllBytes(texturePath);
			Texture2D val = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			((UnityEngine.Object)val).name = Path.GetFileNameWithoutExtension(texturePath);
			if (!TryLoadPngIntoTexture(val, array))
			{
				UnityEngine.Object.DestroyImmediate(val);
				summary = "texture=decode-failed";
				return null;
			}
			val.wrapMode = TextureWrapMode.Clamp;
			val.filterMode = FilterMode.Bilinear;
			val.anisoLevel = 2;
			summary = "texture=" + ((Texture)val).width + "x" + ((Texture)val).height;
			return val;
		}
		catch (Exception ex)
		{
			summary = "texture-error=" + ex.GetType().Name;
			return null;
		}
	}

	private static bool TryLoadPngIntoTexture(Texture2D texture, byte[] data)
	{
		if ((UnityEngine.Object)texture == (UnityEngine.Object)null || data == null || data.Length == 0)
		{
			return false;
		}
		try
		{
			MethodInfo method = typeof(Texture2D).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(byte[]) }, null);
			if (method != null)
			{
				object obj = method.Invoke(texture, new object[1] { data });
				if (obj is bool)
				{
					return (bool)obj;
				}
				return true;
			}
		}
		catch
		{
		}
		try
		{
			Type type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", throwOnError: false);
			MethodInfo method2 = type?.GetMethod("LoadImage", BindingFlags.Static | BindingFlags.Public, null, new Type[3]
			{
				typeof(Texture2D),
				typeof(byte[]),
				typeof(bool)
			}, null);
			if (method2 != null)
			{
				object obj2 = method2.Invoke(null, new object[3] { texture, data, false });
				if (obj2 is bool)
				{
					return (bool)obj2;
				}
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private static Material CreateMaterial(Texture2D texture, string texturePath)
	{
		Shader val = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
		if ((UnityEngine.Object)val == (UnityEngine.Object)null)
		{
			return null;
		}
		Material val2 = new Material(val);
		((UnityEngine.Object)val2).name = "M_AK_Runtime";
		if (val2.HasProperty("_BaseMap"))
		{
			val2.SetTexture("_BaseMap", texture);
		}
		if (val2.HasProperty("_MainTex"))
		{
			val2.SetTexture("_MainTex", texture);
		}
		if (val2.HasProperty("_BaseColor"))
		{
			val2.SetColor("_BaseColor", Color.white);
		}
		if (val2.HasProperty("_Color"))
		{
			val2.SetColor("_Color", Color.white);
		}
		if (val2.HasProperty("_Smoothness"))
		{
			val2.SetFloat("_Smoothness", 0.12f);
		}
		if (val2.HasProperty("_Glossiness"))
		{
			val2.SetFloat("_Glossiness", 0.12f);
		}
		if (val2.HasProperty("_Cull"))
		{
			val2.SetInt("_Cull", 2);
		}
		val2.enableInstancing = true;
		Plugin.LogDiagnosticOnce("ak-light-mat:" + Path.GetFileName(texturePath ?? string.Empty), "Created lightweight AK material with shader=" + (((UnityEngine.Object)val != (UnityEngine.Object)null) ? ((UnityEngine.Object)val).name : "null") + ", texture=" + (((UnityEngine.Object)texture != (UnityEngine.Object)null) ? ((Texture)texture).width + "x" + ((Texture)texture).height : "none"));
		return val2;
	}

	private static GameObject BuildPrefab(Mesh mesh, Material material)
	{
		if ((UnityEngine.Object)mesh == (UnityEngine.Object)null || (UnityEngine.Object)material == (UnityEngine.Object)null)
		{
			return null;
		}
		GameObject val = new GameObject("AK");
		GameObject val2 = new GameObject("Mesh");
		val2.transform.SetParent(val.transform, false);
		val2.transform.localPosition = MeshLocalPosition;
		val2.transform.localRotation = MeshLocalRotation;
		val2.transform.localScale = MeshLocalScale;
		MeshFilter val3 = val2.AddComponent<MeshFilter>();
		MeshRenderer val4 = val2.AddComponent<MeshRenderer>();
		val3.sharedMesh = mesh;
		((Renderer)val4).sharedMaterials = (Material[])(object)new Material[1] { material };
		((Renderer)val4).enabled = false;
		((Renderer)val4).forceRenderingOff = true;
		((Renderer)val4).shadowCastingMode = ShadowCastingMode.On;
		((Renderer)val4).receiveShadows = true;
		GameObject val5 = new GameObject("SpawnPos");
		val5.transform.SetParent(val.transform, false);
		val5.transform.localPosition = SpawnLocalPosition;
		val5.transform.localRotation = SpawnLocalRotation;
		val5.transform.localScale = SpawnLocalScale;
		UnityEngine.Object.DontDestroyOnLoad(val);
		return val;
	}
}
