﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using RoomPlayer = Photon.Realtime.Player;

namespace ShootZombies;

public partial class Plugin
{
	private bool TryLoadLightweightAkResources()
	{
		try
		{
			if (!AkLightweightAssetLoader.TryLoad(out var prefab, out var diagnostic))
			{
				LogDiagnosticOnce("ak-lightweight-unavailable", "Lightweight AK resources unavailable: " + diagnostic);
				return false;
			}
			ResetAkBundleSelectionState();
			_ak47Prefab = prefab;
			_ak47Bundle = null;
			_ak47VFX = null;
			_ak47ItemContent = null;
			_ak47IconTexture = null;
			if (AkLightweightAssetLoader.TryLoadIconTexture(out var texture, out var diagnostic2))
			{
				_ak47IconTexture = texture;
				LogDiagnosticOnce("ak-lightweight-icon-loaded", "Loaded lightweight AK icon: " + diagnostic2);
			}
			CaptureGenericResolvedWeaponAssets();
			ApplySelectedWeaponAssets();
			if (!HasAkVisualPrefab())
			{
				LogDiagnosticOnce("ak-lightweight-invalid", "Lightweight AK load produced no renderable prefab: " + DescribeAkVisualPrefabForDiagnostics());
				ResetAkBundleSelectionState();
				return false;
			}
			LogDiagnosticOnce("ak-lightweight-loaded", "Loaded lightweight AK resources: " + diagnostic + ", prefab=" + DescribeAkVisualPrefabForDiagnostics());
			ReplaceBlowgunSound();
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] Lightweight AK load failed, falling back to bundle: " + ex.Message));
			ResetAkBundleSelectionState();
			return false;
		}
	}

	private void LoadBundleInternal()
	{
		try
		{
			List<string> list = FindBundleCandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (list.Count == 0)
			{
				Log.LogWarning((object)"[ShootZombies] No bundle files found");
				return;
			}
			foreach (string item in list)
			{
				ResetAkBundleSelectionState();
				AssetBundle val = AssetBundle.LoadFromFile(item);
				if ((Object)val == (Object)null)
				{
					Log.LogWarning((object)("[ShootZombies] Failed to load bundle from path: " + item));
					continue;
				}
				OnBundleLoaded(val);
				if (HasAkVisualPrefab())
				{
					LogDiagnosticOnce("ak-bundle-accepted:" + item, "Accepted AK bundle path: " + item);
					return;
				}
				LogDiagnosticOnce("ak-bundle-rejected:" + item, "Rejected AK bundle path because no renderable AK prefab was resolved: " + item + ", prefab=" + DescribeAkVisualPrefabForDiagnostics());
				Log.LogWarning((object)("[ShootZombies] Bundle loaded but no model found, trying next candidate: " + item));
				val.Unload(false);
				if ((Object)_ak47Bundle == (Object)val)
				{
					_ak47Bundle = null;
				}
				ResetAkBundleSelectionState();
			}
			ResetAkBundleSelectionState();
			Log.LogWarning((object)"[ShootZombies] No usable AK bundle found after scanning all candidate paths");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] LoadBundleInternal error: " + ex));
		}
	}

	private static IEnumerable<string> FindBundleCandidatePaths()
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = new string[4] { "Weapons_shootzombies.bundle", "ak47_shootzombies.peakbundle", "shootzombies_weapons.peakbundle", "ak.peakbundle" };
		foreach (string item in GetBundleSearchRoots())
		{
			if (string.IsNullOrWhiteSpace(item) || !Directory.Exists(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string path in array2)
			{
				string text = Path.Combine(item, path);
				if (File.Exists(text) && seen.Add(text))
				{
					yield return text;
				}
			}
		}
		foreach (string item in GetBundleSearchRoots())
		{
			if (string.IsNullOrWhiteSpace(item) || !Directory.Exists(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string searchPattern in array2)
			{
				string[] array3 = Array.Empty<string>();
				try
				{
					array3 = Directory.GetFiles(item, searchPattern, SearchOption.AllDirectories);
				}
				catch
				{
				}
				string[] array4 = array3;
				foreach (string text2 in array4)
				{
					if (seen.Add(text2))
					{
						yield return text2;
					}
				}
			}
		}
	}

	private static IEnumerable<string> GetBundleSearchRoots()
	{
		string text = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
		string text2 = Paths.PluginPath ?? string.Empty;
		string text3 = ((string.IsNullOrWhiteSpace(text2) || !Directory.Exists(text2)) ? string.Empty : (Directory.GetParent(text2)?.FullName ?? string.Empty));
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
	}

	private void OnBundleLoaded(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			Log.LogError((object)"[ShootZombies] AssetBundle is null");
			return;
		}
		try
		{
			_ak47Bundle = bundle;
			_ak47VFX = null;
			TryResolvePreferredAkAssets(bundle);
			TryResolveKnownAkItemContent(bundle);
			Object[] array = bundle.LoadAllAssets();
			foreach (Object val in array)
			{
				Type type = ((object)val).GetType();
				if (type.Name.Contains("ItemContent") || type.Name.Contains("IC_AK"))
				{
					_ak47ItemContent = val;
					LogItemContentReflectionDiagnosis(_ak47ItemContent);
					FieldInfo field = type.GetField("ItemPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					PropertyInfo property = type.GetProperty("ItemPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null)
					{
						object value = property.GetValue(val);
						_ak47Prefab = (GameObject)((value is GameObject) ? value : null);
					}
					else if (field != null)
					{
						object value2 = field.GetValue(val);
						_ak47Prefab = (GameObject)((value2 is GameObject) ? value2 : null);
					}
					if ((Object)_ak47IconTexture == (Object)null)
					{
						Texture2D val2 = TryExtractIconFromItemContent(_ak47ItemContent);
						if ((Object)val2 != (Object)null)
						{
							_ak47IconTexture = val2;
						}
					}
				}
				GameObject val3 = (GameObject)((val is GameObject) ? val : null);
				if ((Object)(object)val3 != (Object)null && IsLikelyAkVisualPrefab(val3))
				{
					_ak47VFX = val3;
				}
				if ((Object)(object)val3 != (Object)null && (Object)_ak47Prefab == (Object)null)
				{
					MeshRenderer[] componentsInChildren = val3.GetComponentsInChildren<MeshRenderer>(true);
					SkinnedMeshRenderer[] componentsInChildren2 = val3.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					MeshFilter[] componentsInChildren3 = val3.GetComponentsInChildren<MeshFilter>(true);
					if (componentsInChildren.Length != 0 || componentsInChildren2.Length != 0 || componentsInChildren3.Length != 0)
					{
						_ak47Prefab = val3;
					}
				}
				Texture2D val5 = (Texture2D)((val is Texture2D) ? val : null);
				if ((Object)(object)val5 != (Object)null && (Object)_ak47IconTexture == (Object)null && IsLikelyIconTexture(val5))
				{
					_ak47IconTexture = val5;
				}
			}
			TryResolveItemContentFromNamedAssets(bundle);
			if ((Object)_ak47Prefab == (Object)null)
			{
				GameObject[] array2 = bundle.LoadAllAssets<GameObject>();
				GameObject[] array3 = array2;
				foreach (GameObject val6 in array3)
				{
					MeshRenderer[] componentsInChildren4 = val6.GetComponentsInChildren<MeshRenderer>(true);
					SkinnedMeshRenderer[] componentsInChildren5 = val6.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					MeshFilter[] componentsInChildren6 = val6.GetComponentsInChildren<MeshFilter>(true);
					if (componentsInChildren4.Length != 0 || componentsInChildren5.Length != 0 || componentsInChildren6.Length != 0)
					{
						_ak47Prefab = val6;
					}
				}
				if ((Object)_ak47Prefab == (Object)null && array2.Length != 0)
				{
					_ak47Prefab = array2[0];
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				GameObject val10 = TryLoadRenderablePrefabFromNamedAssets(bundle);
				if ((Object)val10 != (Object)null)
				{
					_ak47Prefab = val10;
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				Mesh[] array7 = bundle.LoadAllAssets<Mesh>();
				Material[] array8 = bundle.LoadAllAssets<Material>();
				Texture2D[] array9 = bundle.LoadAllAssets<Texture2D>();
				GameObject val11 = TryCreateSyntheticAkPrefabFromBundle(array7, array8, array9);
				if ((Object)val11 != (Object)null)
				{
					_ak47Prefab = val11;
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				GameObject val12 = TryCreateSyntheticAkPrefabFromNamedAssets(bundle);
				if ((Object)val12 != (Object)null)
				{
					_ak47Prefab = val12;
				}
			}
			if ((Object)_ak47Prefab == (Object)null)
			{
				Log.LogWarning((object)"[ShootZombies] No model found in bundle, creating fallback cube model");
				_ak47Prefab = CreateFallbackModel();
			}
			if ((Object)_ak47VFX == (Object)null)
			{
				GameObject val7 = bundle.LoadAsset<GameObject>("VFX_AK");
				if ((Object)val7 != (Object)null)
				{
					_ak47VFX = val7;
				}
			}
			Texture2D[] array5 = bundle.LoadAllAssets<Texture2D>();
			if (array5 != null && array5.Length != 0 && (Object)_ak47IconTexture == (Object)null)
			{
				Texture2D[] array6 = array5;
				foreach (Texture2D val8 in array6)
				{
					if (IsLikelyIconTexture(val8))
					{
						_ak47IconTexture = val8;
						break;
					}
				}
			}
			if ((Object)_ak47IconTexture == (Object)null && _ak47ItemContent != null)
			{
				Texture2D val9 = TryExtractIconFromItemContent(_ak47ItemContent);
				if ((Object)val9 != (Object)null)
				{
					_ak47IconTexture = val9;
				}
			}
			CaptureGenericResolvedWeaponAssets();
			TryResolveSelectableWeaponAssets(bundle);
			ApplySelectedWeaponAssets();
			LogBundleSelectionDiagnosis(bundle);
			ReplaceBlowgunSound();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] OnBundleLoaded error: " + ex));
		}
	}

	private static void ResetAkBundleSelectionState()
	{
		for (int num = _runtimeWeaponPrefabClones.Count - 1; num >= 0; num--)
		{
			GameObject val = _runtimeWeaponPrefabClones[num];
			if (!((Object)val == (Object)null))
			{
				try
				{
					Object.DestroyImmediate((Object)val);
				}
				catch
				{
				}
			}
		}
		_runtimeWeaponPrefabClones.Clear();
		_resolvedWeaponPrefabs.Clear();
		_resolvedWeaponIcons.Clear();
		_ak47Bundle = null;
		_ak47Prefab = null;
		_ak47VFX = null;
		_ak47ItemContent = null;
		_ak47IconTexture = null;
		_genericResolvedWeaponPrefab = null;
		_genericResolvedWeaponIcon = null;
		_useExplicitSelectedWeaponIcon = false;
	}

	private void TryResolveItemContentFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null || _ak47ItemContent != null)
		{
			return;
		}
		TryResolveKnownAkItemContent(bundle);
		if (_ak47ItemContent != null)
		{
			return;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			Object val = null;
			try
			{
				val = bundle.LoadAsset(text);
			}
			catch (Exception ex2)
			{
				LogDiagnosticOnce("ak-itemcontent-load-error:" + text, "Named asset load failed: asset=" + text + ", error=" + ex2.GetType().Name);
			}
			if ((Object)val == (Object)null)
			{
				if (text.IndexOf("ic_ak", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					LogDiagnosticOnce("ak-itemcontent-null:" + text, "Named asset load returned null: asset=" + text);
					Object[] array = null;
					try
					{
						array = bundle.LoadAssetWithSubAssets(text);
					}
					catch (Exception ex3)
					{
						LogDiagnosticOnce("ak-itemcontent-subasset-error:" + text, "LoadAssetWithSubAssets failed: asset=" + text + ", error=" + ex3.GetType().Name);
					}
					if (array == null || array.Length == 0)
					{
						LogDiagnosticOnce("ak-itemcontent-subasset-empty:" + text, "LoadAssetWithSubAssets returned no sub-assets: asset=" + text);
					}
					else
					{
						string value = string.Join(" || ", array.Where((Object item) => (Object)item != (Object)null).Select((Object item) => item.GetType().FullName + ":" + item.name).Take(24));
						LogDiagnosticOnce("ak-itemcontent-subasset-list:" + text, "LoadAssetWithSubAssets returned: asset=" + text + ", items=" + value);
						Object[] array2 = array;
						foreach (Object val2 in array2)
						{
							if ((Object)val2 == (Object)null)
							{
								continue;
							}
							Type type2 = ((object)val2).GetType();
							if (!type2.Name.Contains("ItemContent") && !type2.Name.Contains("IC_AK"))
							{
								continue;
							}
							_ak47ItemContent = val2;
							LogItemContentReflectionDiagnosis(_ak47ItemContent);
							TryAssignAkPrefabFromItemContent(_ak47ItemContent);
							if ((Object)_ak47IconTexture == (Object)null)
							{
								_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
							}
							LogDiagnosticOnce("ak-itemcontent-resolved-subasset", "Resolved IC_AK from sub-asset: " + text + ", type=" + type2.FullName + ", name=" + ((Object)val2).name);
							break;
						}
						if (_ak47ItemContent != null)
						{
							break;
						}
					}
				}
				continue;
			}
			Type type = ((object)val).GetType();
			LogDiagnosticOnce("ak-itemcontent-load:" + text, "Named asset load: asset=" + text + ", type=" + type.FullName + ", name=" + ((Object)val).name);
			if (!type.Name.Contains("ItemContent") && !type.Name.Contains("IC_AK"))
			{
				continue;
			}
			_ak47ItemContent = val;
			LogItemContentReflectionDiagnosis(_ak47ItemContent);
			TryAssignAkPrefabFromItemContent(_ak47ItemContent);
			if ((Object)_ak47IconTexture == (Object)null)
			{
				_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
			}
			LogDiagnosticOnce("ak-itemcontent-resolved", "Resolved IC_AK from named asset: " + text);
			break;
		}
	}

	private void TryAssignAkPrefabFromItemContent(object itemContent)
	{
		if (itemContent == null || (Object)_ak47Prefab != (Object)null)
		{
			return;
		}
		try
		{
			Type type = itemContent.GetType();
			foreach (PropertyInfo item in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (!item.CanRead || item.GetIndexParameters().Length != 0)
				{
					continue;
				}
				object value = null;
				try
				{
					value = item.GetValue(itemContent, null);
				}
				catch
				{
				}
				if (TryAssignAkPrefabFromValue(value, "prop " + item.Name))
				{
					return;
				}
			}
			foreach (FieldInfo item2 in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				object value2 = null;
				try
				{
					value2 = item2.GetValue(itemContent);
				}
				catch
				{
				}
				if (TryAssignAkPrefabFromValue(value2, "field " + item2.Name))
				{
					return;
				}
			}
		}
		catch (Exception ex)
		{
			LogDiagnosticOnce("ak-itemcontent-prefab-error", "TryAssignAkPrefabFromItemContent failed: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private bool TryAssignAkPrefabFromValue(object value, string source)
	{
		GameObject val = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val != (Object)null && HasRenderableGeometry(val))
		{
			_ak47Prefab = val;
			LogDiagnosticOnce("ak-itemcontent-prefab:" + source, "Resolved AK prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(val));
			return true;
		}
		Item val2 = (Item)((value is Item) ? value : null);
		if ((Object)val2 != (Object)null && HasRenderableGeometry(((Component)val2).gameObject))
		{
			_ak47Prefab = ((Component)val2).gameObject;
			LogDiagnosticOnce("ak-itemcontent-item:" + source, "Resolved AK item/prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(_ak47Prefab));
			return true;
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null && HasRenderableGeometry(((Component)val3).gameObject))
		{
			_ak47Prefab = ((Component)val3).gameObject;
			LogDiagnosticOnce("ak-itemcontent-component:" + source, "Resolved AK component/prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(_ak47Prefab));
			return true;
		}
		return false;
	}

	private void LogBundleSelectionDiagnosis(AssetBundle bundle)
	{
		string fullName = _ak47ItemContent?.GetType().FullName ?? "null";
		string text = (((Object)_ak47VFX != (Object)null) ? ((Object)_ak47VFX).name : "null");
		string text2 = DescribeBundleAssetSummary(bundle);
		LogDiagnosticOnce("ak-bundle-selection", $"Bundle selection complete: bundle={(((Object)bundle != (Object)null) ? ((Object)bundle).name : "null")}, itemContentType={fullName}, prefab={DescribeAkVisualPrefabForDiagnostics()}, vfx={text}, icon={DescribeTextureForDiagnostics(_ak47IconTexture)}, assets={text2}");
	}

	private static string DescribeBundleAssetSummary(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return "bundle=null";
		}
		try
		{
			GameObject[] array = bundle.LoadAllAssets<GameObject>();
			Mesh[] array2 = bundle.LoadAllAssets<Mesh>();
			Material[] array3 = bundle.LoadAllAssets<Material>();
			Texture2D[] array4 = bundle.LoadAllAssets<Texture2D>();
			string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
			string[] allScenePaths = bundle.GetAllScenePaths() ?? Array.Empty<string>();
			string text = string.Join(", ", array.Take(6).Select((GameObject go) => ((Object)go).name));
			string text2 = string.Join(", ", array2.OrderByDescending((Mesh mesh) => (mesh != null) ? mesh.vertexCount : 0).Take(6).Select((Mesh mesh) => $"{((Object)mesh).name}:{mesh.vertexCount}"));
			string text3 = string.Join(", ", array3.Take(6).Select((Material material) => ((Object)material).name));
			string text4 = string.Join(", ", array4.Take(6).Select((Texture2D texture) => $"{((Object)texture).name}:{((Texture)texture).width}x{((Texture)texture).height}"));
			string text5 = string.Join(", ", allAssetNames.Take(8));
			string text6 = string.Join(", ", allScenePaths.Take(4));
			return $"go[{array.Length}]={text}; mesh[{array2.Length}]={text2}; mat[{array3.Length}]={text3}; tex[{array4.Length}]={text4}; names[{allAssetNames.Length}]={text5}; scenes[{allScenePaths.Length}]={text6}";
		}
		catch (Exception ex)
		{
			return "asset-summary-error:" + ex.GetType().Name;
		}
	}

	private void LogItemContentReflectionDiagnosis(object itemContent)
	{
		if (itemContent == null)
		{
			return;
		}
		try
		{
			Type type = itemContent.GetType();
			List<string> list = new List<string>();
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (FieldInfo item in type.GetFields(bindingAttr).OrderBy((FieldInfo f) => f.Name))
			{
				object value = null;
				string text = "unreadable";
				try
				{
					value = item.GetValue(itemContent);
					text = DescribeReflectionValue(value);
				}
				catch (Exception ex2)
				{
					text = "error:" + ex2.GetType().Name;
				}
				list.Add("field " + item.Name + "=" + text);
			}
			foreach (PropertyInfo item2 in type.GetProperties(bindingAttr).OrderBy((PropertyInfo p) => p.Name))
			{
				if (!item2.CanRead || item2.GetIndexParameters().Length != 0)
				{
					continue;
				}
				object value2 = null;
				string text2 = "unreadable";
				try
				{
					value2 = item2.GetValue(itemContent, null);
					text2 = DescribeReflectionValue(value2);
				}
				catch (Exception ex3)
				{
					text2 = "error:" + ex3.GetType().Name;
				}
				list.Add("prop " + item2.Name + "=" + text2);
			}
			LogDiagnosticOnce("ak-ic-members:" + type.FullName, "IC_AK members: type=" + type.FullName + ", summary=" + string.Join(" || ", list.Take(24)));
			List<DiagnosticGraphHit> list2 = new List<DiagnosticGraphHit>();
			HashSet<object> visited = new HashSet<object>(ReferenceIdentityComparer.Instance);
			InspectItemContentGraph(itemContent, "IC_AK", 0, 3, visited, list2);
			if (list2.Count == 0)
			{
				LogDiagnosticOnce("ak-ic-candidates:" + type.FullName, "IC_AK graph candidates: none");
				return;
			}
			string value3 = string.Join(" || ", list2.OrderByDescending((DiagnosticGraphHit hit) => hit.Score).ThenBy((DiagnosticGraphHit hit) => hit.Path).Take(16).Select((DiagnosticGraphHit hit) => $"{hit.Path} => {hit.Summary}"));
			LogDiagnosticOnce("ak-ic-candidates:" + type.FullName, "IC_AK graph candidates: " + value3);
		}
		catch (Exception ex)
		{
			LogDiagnosticOnce("ak-ic-reflection-error", "IC_AK reflection diagnosis failed: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void InspectItemContentGraph(object value, string path, int depth, int maxDepth, HashSet<object> visited, List<DiagnosticGraphHit> hits)
	{
		if (value == null || depth > maxDepth)
		{
			return;
		}
		Type type = value.GetType();
		if (!(value is string) && !type.IsValueType && !visited.Add(value))
		{
			return;
		}
		if (TryCreateDiagnosticGraphHit(value, path, out var hit))
		{
			hits.Add(hit);
		}
		if (depth >= maxDepth || IsTerminalDiagnosticType(type))
		{
			return;
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			int num = 0;
			foreach (object item in enumerable)
			{
				InspectItemContentGraph(item, path + "[" + num + "]", depth + 1, maxDepth, visited, hits);
				num++;
				if (num >= 8)
				{
					break;
				}
			}
			return;
		}
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		foreach (FieldInfo item2 in type.GetFields(bindingAttr))
		{
			if (ShouldSkipDiagnosticMember(item2.FieldType))
			{
				continue;
			}
			object value2 = null;
			try
			{
				value2 = item2.GetValue(value);
			}
			catch
			{
			}
			if (value2 != null)
			{
				InspectItemContentGraph(value2, path + "." + item2.Name, depth + 1, maxDepth, visited, hits);
			}
		}
		foreach (PropertyInfo item3 in type.GetProperties(bindingAttr))
		{
			if (!item3.CanRead || item3.GetIndexParameters().Length != 0 || ShouldSkipDiagnosticMember(item3.PropertyType))
			{
				continue;
			}
			object value3 = null;
			try
			{
				value3 = item3.GetValue(value, null);
			}
			catch
			{
			}
			if (value3 != null)
			{
				InspectItemContentGraph(value3, path + "." + item3.Name, depth + 1, maxDepth, visited, hits);
			}
		}
	}

	private static bool TryCreateDiagnosticGraphHit(object value, string path, out DiagnosticGraphHit hit)
	{
		hit = null;
		Item val = (Item)((value is Item) ? value : null);
		if ((Object)val != (Object)null)
		{
			GameObject gameObject = ((Component)val).gameObject;
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Item(type={value.GetType().FullName}, go={((Object)gameObject).name}, itemID={val.itemID}, uiName={val.UIData?.itemName ?? "null"}, renderable={HasRenderableGeometry(gameObject)}, detail={DescribeGameObjectForDiagnostics(gameObject, 4)})",
				Score = ScoreDiagnosticGameObject(gameObject)
			};
			return true;
		}
		GameObject val2 = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val2 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"GameObject(type={value.GetType().FullName}, renderable={HasRenderableGeometry(val2)}, detail={DescribeGameObjectForDiagnostics(val2, 4)})",
				Score = ScoreDiagnosticGameObject(val2)
			};
			return true;
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null)
		{
			GameObject gameObject2 = ((Component)val3).gameObject;
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Component(type={value.GetType().FullName}, go={((Object)gameObject2).name}, renderable={HasRenderableGeometry(gameObject2)}, detail={DescribeGameObjectForDiagnostics(gameObject2, 4)})",
				Score = ScoreDiagnosticGameObject(gameObject2)
			};
			return true;
		}
		Texture2D val4 = (Texture2D)((value is Texture2D) ? value : null);
		if ((Object)val4 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = "Texture2D(" + DescribeTextureForDiagnostics(val4) + ")",
				Score = 200
			};
			return true;
		}
		Material val5 = (Material)((value is Material) ? value : null);
		if ((Object)val5 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Material(name={((Object)val5).name}, shader={val5.shader?.name ?? "null"}, mainTex={(((Object)val5.mainTexture != (Object)null) ? ((Object)val5.mainTexture).name : "null")})",
				Score = 150
			};
			return true;
		}
		Mesh val6 = (Mesh)((value is Mesh) ? value : null);
		if ((Object)val6 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Mesh(name={((Object)val6).name}, verts={val6.vertexCount}, subMeshes={val6.subMeshCount})",
				Score = 100 + val6.vertexCount
			};
			return true;
		}
		UnityEngine.Object val7 = (UnityEngine.Object)((value is UnityEngine.Object) ? value : null);
		if ((Object)val7 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"UnityObject(type={value.GetType().FullName}, name={((Object)val7).name})",
				Score = 50
			};
			return true;
		}
		return false;
	}

	private static int ScoreDiagnosticGameObject(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return int.MinValue;
		}
		int num = 0;
		string text = (((Object)candidate).name ?? string.Empty).ToLowerInvariant();
		if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
		{
			num += 12000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("flash") || text.Contains("particle") || text.Contains("smoke"))
		{
			num -= 20000;
		}
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				num += val.sharedMesh.vertexCount;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				num += val2.sharedMesh.vertexCount;
			}
		}
		return num;
	}

	private static bool IsTerminalDiagnosticType(Type type)
	{
		if (type == null)
		{
			return true;
		}
		if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
		{
			return true;
		}
		if (typeof(UnityEngine.Object).IsAssignableFrom(type))
		{
			return true;
		}
		string fullName = type.FullName ?? string.Empty;
		if (!fullName.StartsWith("System.", StringComparison.Ordinal) && !fullName.StartsWith("UnityEngine.", StringComparison.Ordinal))
		{
			return fullName.StartsWith("TMPro.", StringComparison.Ordinal);
		}
		return true;
	}

	private static bool ShouldSkipDiagnosticMember(Type memberType)
	{
		if (memberType == null)
		{
			return true;
		}
		if (memberType.IsPointer)
		{
			return true;
		}
		if (!typeof(Delegate).IsAssignableFrom(memberType))
		{
			return memberType == typeof(IntPtr) || memberType == typeof(UIntPtr);
		}
		return true;
	}

	private static string DescribeReflectionValue(object value)
	{
		if (value == null)
		{
			return "null";
		}
		Type type = value.GetType();
		GameObject val = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val != (Object)null)
		{
			return $"GameObject(name={((Object)val).name}, renderable={HasRenderableGeometry(val)})";
		}
		Item val2 = (Item)((value is Item) ? value : null);
		if ((Object)val2 != (Object)null)
		{
			return $"Item(go={((Object)((Component)val2).gameObject).name}, itemID={val2.itemID}, uiName={val2.UIData?.itemName ?? "null"})";
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null)
		{
			return $"Component(type={type.FullName}, go={((Object)((Component)val3).gameObject).name})";
		}
		Texture2D val4 = (Texture2D)((value is Texture2D) ? value : null);
		if ((Object)val4 != (Object)null)
		{
			return "Texture2D(" + DescribeTextureForDiagnostics(val4) + ")";
		}
		Material val5 = (Material)((value is Material) ? value : null);
		if ((Object)val5 != (Object)null)
		{
			return $"Material(name={((Object)val5).name}, shader={val5.shader?.name ?? "null"})";
		}
		Mesh val6 = (Mesh)((value is Mesh) ? value : null);
		if ((Object)val6 != (Object)null)
		{
			return $"Mesh(name={((Object)val6).name}, verts={val6.vertexCount})";
		}
		UnityEngine.Object val7 = (UnityEngine.Object)((value is UnityEngine.Object) ? value : null);
		if ((Object)val7 != (Object)null)
		{
			return $"UnityObject(type={type.FullName}, name={((Object)val7).name})";
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			int num = 0;
			foreach (object item in enumerable)
			{
				num++;
				if (num >= 8)
				{
					break;
				}
			}
			return $"Enumerable(type={type.FullName}, count>={num})";
		}
		return $"Object(type={type.FullName})";
	}

	private static bool IsLikelyIconTexture(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		int width = ((Texture)texture).width;
		int height = ((Texture)texture).height;
		if (width <= 0 || height <= 0)
		{
			return false;
		}
		string text = (((Object)texture).name ?? string.Empty).ToLowerInvariant();
		bool flag = text.Contains("icon") || text.Contains("ui") || text.Contains("thumb") || text.Contains("inventory");
		if (flag)
		{
			if (width <= 4096 && height <= 4096)
			{
				return Mathf.Abs(width - height) <= Mathf.Max(4, Mathf.RoundToInt((float)Mathf.Max(width, height) * 0.02f));
			}
			return false;
		}
		if (width >= 32 && height >= 32 && width <= 512 && height <= 512)
		{
			return Mathf.Abs(width - height) <= 2;
		}
		return false;
	}

	private static Texture2D TryExtractIconFromItemContent(object itemContent)
	{
		if (itemContent == null)
		{
			return null;
		}
		try
		{
			Type type = itemContent.GetType();
			object obj = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent) ?? type.GetField("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val = TryExtractIconFromItem((Item)((obj is Item) ? obj : null));
			if ((Object)val != (Object)null)
			{
				return val;
			}
			if (obj != null && !ReferenceEquals(obj, itemContent))
			{
				Texture2D val2 = TryExtractIconFromItemContent(obj);
				if ((Object)val2 != (Object)null)
				{
					return val2;
				}
			}
			object obj2 = type.GetProperty("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val3 = (Texture2D)((obj2 is Texture2D) ? obj2 : null);
			if ((Object)val3 != (Object)null)
			{
				return val3;
			}
			object obj3 = type.GetField("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val4 = (Texture2D)((obj3 is Texture2D) ? obj3 : null);
			if ((Object)val4 != (Object)null)
			{
				return val4;
			}
		}
		catch
		{
		}
		return null;
	}

	private static Texture2D TryExtractIconFromItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		try
		{
			Texture2D val = item.UIData?.icon;
			if ((Object)val != (Object)null)
			{
				return val;
			}
			Texture2D val2 = item.UIData?.altIcon;
			if ((Object)val2 != (Object)null)
			{
				return val2;
			}
			ItemUIData uIData = item.UIData;
			Texture2D val3 = ((uIData != null) ? uIData.GetIcon() : null);
			if ((Object)val3 != (Object)null)
			{
				return val3;
			}
		}
		catch
		{
		}
		return null;
	}

	private void TryResolveKnownAkItemContent(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null || _ak47ItemContent != null)
		{
			return;
		}
		Type type = ResolveLoadedType("PEAKLib.Items.UnityEditor.UnityItemContent", "com.github.PEAKModding.PEAKLib.Items") ?? ResolveLoadedType("PEAKLib.Items.ItemContent", "com.github.PEAKModding.PEAKLib.Items");
		if (type == null)
		{
			LogDiagnosticOnce("ak-itemcontent-type-missing", "PEAKLib.Items is not loaded, so IC_AK cannot be deserialized from the AK bundle.");
			return;
		}
		IEnumerable<string> source = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in PreferredAkItemContentAssetNames.Concat(source.Where((string name) => !string.IsNullOrWhiteSpace(name) && name.IndexOf("ic_ak", StringComparison.OrdinalIgnoreCase) >= 0)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			Object val = null;
			try
			{
				val = bundle.LoadAsset(text, type);
			}
			catch (Exception ex)
			{
				LogDiagnosticOnce("ak-itemcontent-typed-load-error:" + text, "Typed IC_AK load failed: asset=" + text + ", type=" + type.FullName + ", error=" + ex.GetType().Name);
			}
			if ((Object)val == (Object)null)
			{
				continue;
			}
			_ak47ItemContent = val;
			LogItemContentReflectionDiagnosis(_ak47ItemContent);
			TryAssignAkPrefabFromItemContent(_ak47ItemContent);
			if ((Object)_ak47IconTexture == (Object)null)
			{
				_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
			}
			LogDiagnosticOnce("ak-itemcontent-typed-load:" + text, "Resolved IC_AK using exact type load: asset=" + text + ", type=" + type.FullName + ", name=" + ((Object)val).name);
			break;
		}
	}

	private GameObject CreateFallbackModel()
	{
		GameObject obj = GameObject.CreatePrimitive((PrimitiveType)3);
		((Object)obj).name = "AK47_Fallback";
		obj.transform.localScale = new Vector3(0.1f, 0.1f, 0.5f);
		MeshRenderer component = obj.GetComponent<MeshRenderer>();
		if ((Object)component != (Object)null)
		{
			Material val = new Material(ResolveLocalVisualCompatibleShader());
			if (val.HasProperty("_BaseColor"))
			{
				val.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f, 1f));
			}
			if (val.HasProperty("_Color"))
			{
				val.SetColor("_Color", new Color(0.2f, 0.2f, 0.2f, 1f));
			}
			((Renderer)component).material = val;
		}
		return obj;
	}

	private static GameObject TryCreateSyntheticAkPrefabFromBundle(Mesh[] meshes, Material[] materials, Texture2D[] textures)
	{
		Mesh val = SelectBestBundleMesh(meshes);
		if ((Object)val == (Object)null)
		{
			return null;
		}
		int num = Mathf.Max(val.subMeshCount, 1);
		Material[] array = ChooseBundleMaterials(materials, textures, num);
		if (array == null || array.Length == 0)
		{
			array = (Material[])(object)new Material[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = CreateSyntheticFallbackMaterial(null);
			}
		}
		GameObject val2 = new GameObject("AK");
		val2.hideFlags = (HideFlags)61;
		val2.SetActive(false);
		GameObject val3 = new GameObject("Mesh");
		val3.hideFlags = (HideFlags)61;
		val3.transform.SetParent(val2.transform, false);
		MeshFilter val4 = val3.AddComponent<MeshFilter>();
		MeshRenderer obj = val3.AddComponent<MeshRenderer>();
		val4.sharedMesh = val;
		((Renderer)obj).sharedMaterials = NormalizeBundleMaterialArray(array, num);
		LogDiagnosticOnce("ak-synthetic-prefab:" + ((Object)val).name, $"Created synthetic AK prefab from raw bundle mesh: mesh={((Object)val).name}, verts={val.vertexCount}, subMeshes={num}, mats={((Renderer)obj).sharedMaterials.Length}");
		return val2;
	}

	private static GameObject TryLoadRenderablePrefabFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return null;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		GameObject result = null;
		int num = int.MinValue;
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			GameObject val = null;
			try
			{
				val = bundle.LoadAsset<GameObject>(text);
			}
			catch
			{
			}
			if ((Object)val == (Object)null || !HasRenderableGeometry(val))
			{
				continue;
			}
			int num2 = ScoreNamedRenderablePrefab(text, val);
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		if ((Object)result != (Object)null)
		{
			LogDiagnosticOnce("ak-named-prefab", $"Loaded renderable prefab from named asset: asset={FindAssetNameForObject(bundle, result)}, prefab={DescribeGameObjectForDiagnostics(result)}");
		}
		return result;
	}

	private static int ScoreNamedRenderablePrefab(string assetName, GameObject candidate)
	{
		int num = 0;
		string text = ((assetName ?? string.Empty) + "/" + ((((Object)candidate).name ?? string.Empty))).ToLowerInvariant();
		if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
		{
			num += 12000;
		}
		if (text.Contains("prefab") || text.Contains("model") || text.Contains("mesh"))
		{
			num += 4000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("flash") || text.Contains("smoke") || text.Contains("particle") || text.Contains("muzzle"))
		{
			num -= 20000;
		}
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				num += val.sharedMesh.vertexCount;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				num += val2.sharedMesh.vertexCount;
			}
		}
		return num;
	}

	private static GameObject TryCreateSyntheticAkPrefabFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return null;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		List<Mesh> list = new List<Mesh>();
		List<Material> list2 = new List<Material>();
		List<Texture2D> list3 = new List<Texture2D>();
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			TryAddNamedAsset(bundle, text, list, list2, list3);
		}
		if (list.Count == 0 && list2.Count == 0 && list3.Count == 0)
		{
			return null;
		}
		LogDiagnosticOnce("ak-named-assets-scan", $"Named asset scan found mesh={list.Count}, mat={list2.Count}, tex={list3.Count}");
		return TryCreateSyntheticAkPrefabFromBundle(list.Distinct().ToArray(), list2.Distinct().ToArray(), list3.Distinct().ToArray());
	}

	private static void TryAddNamedAsset(AssetBundle bundle, string assetName, List<Mesh> meshes, List<Material> materials, List<Texture2D> textures)
	{
		try
		{
			Mesh val = bundle.LoadAsset<Mesh>(assetName);
			if ((Object)val != (Object)null)
			{
				meshes.Add(val);
			}
		}
		catch
		{
		}
		try
		{
			Material val2 = bundle.LoadAsset<Material>(assetName);
			if ((Object)val2 != (Object)null)
			{
				materials.Add(val2);
			}
		}
		catch
		{
		}
		try
		{
			Texture2D val3 = bundle.LoadAsset<Texture2D>(assetName);
			if ((Object)val3 != (Object)null)
			{
				textures.Add(val3);
			}
		}
		catch
		{
		}
		try
		{
			Object[] array = bundle.LoadAssetWithSubAssets(assetName);
			if (array == null)
			{
				return;
			}
			foreach (Object val4 in array)
			{
				Mesh val5 = (Mesh)(object)((val4 is Mesh) ? val4 : null);
				if ((Object)val5 != (Object)null)
				{
					meshes.Add(val5);
					continue;
				}
				Material val6 = (Material)(object)((val4 is Material) ? val4 : null);
				if ((Object)val6 != (Object)null)
				{
					materials.Add(val6);
					continue;
				}
				Texture2D val7 = (Texture2D)(object)((val4 is Texture2D) ? val4 : null);
				if ((Object)val7 != (Object)null)
				{
					textures.Add(val7);
				}
			}
		}
		catch
		{
		}
	}

	private static string FindAssetNameForObject(AssetBundle bundle, Object target)
	{
		if ((Object)bundle == (Object)null || (Object)target == (Object)null)
		{
			return "unknown";
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in allAssetNames)
		{
			try
			{
				Object val = bundle.LoadAsset(text);
				if ((Object)val == (Object)target)
				{
					return text;
				}
			}
			catch
			{
			}
		}
		return "unknown";
	}

	private static void TryResolvePreferredAkAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return;
		}
		if (!HasRenderableGeometry(_ak47Prefab))
		{
			GameObject val = TryLoadNamedGameObject(bundle, PreferredAkPrefabAssetNames);
			if ((Object)val != (Object)null && HasRenderableGeometry(val))
			{
				_ak47Prefab = val;
				LogDiagnosticOnce("ak-preferred-prefab", "Resolved AK prefab from top-level bundle asset: " + DescribeGameObjectForDiagnostics(val));
			}
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			Texture2D val2 = TryLoadNamedTexture(bundle, PreferredAkIconAssetNames);
			if ((Object)val2 != (Object)null)
			{
				_ak47IconTexture = val2;
				LogDiagnosticOnce("ak-preferred-icon", "Resolved AK icon from top-level bundle asset: " + DescribeTextureForDiagnostics(val2));
			}
		}
		if ((Object)_ak47VFX == (Object)null)
		{
			GameObject val3 = TryLoadNamedGameObject(bundle, PreferredAkVfxAssetNames);
			if ((Object)val3 != (Object)null)
			{
				_ak47VFX = val3;
			}
		}
	}

	private static GameObject TryLoadNamedGameObject(AssetBundle bundle, IEnumerable<string> assetNames)
	{
		if ((Object)bundle == (Object)null || assetNames == null)
		{
			return null;
		}
		foreach (string assetName in assetNames)
		{
			if (string.IsNullOrWhiteSpace(assetName))
			{
				continue;
			}
			try
			{
				GameObject val = bundle.LoadAsset<GameObject>(assetName);
				if ((Object)val != (Object)null)
				{
					return val;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static Texture2D TryLoadNamedTexture(AssetBundle bundle, IEnumerable<string> assetNames)
	{
		if ((Object)bundle == (Object)null || assetNames == null)
		{
			return null;
		}
		foreach (string assetName in assetNames)
		{
			if (string.IsNullOrWhiteSpace(assetName))
			{
				continue;
			}
			try
			{
				Texture2D val = bundle.LoadAsset<Texture2D>(assetName);
				if ((Object)val != (Object)null)
				{
					return val;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static Mesh SelectBestBundleMesh(Mesh[] meshes)
	{
		if (meshes == null || meshes.Length == 0)
		{
			return null;
		}
		Mesh result = null;
		int num = int.MinValue;
		foreach (Mesh val in meshes)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			string text = (((Object)val).name ?? string.Empty).ToLowerInvariant();
			int vertexCount = val.vertexCount;
			if (vertexCount <= 24)
			{
				vertexCount -= 20000;
			}
			if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
			{
				vertexCount += 12000;
			}
			if (text.Contains("mesh") || text.Contains("body"))
			{
				vertexCount += 2000;
			}
			if (text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("muzzle") || text.Contains("flash") || text.Contains("smoke") || text.Contains("hand") || text.Contains("arm") || text.Contains("finger") || text.Contains("trigger") || text.Contains("collider") || text.Contains("cube") || text.Contains("plane") || text.Contains("quad") || text.Contains("sphere") || text.Contains("capsule"))
			{
				vertexCount -= 30000;
			}
			if (vertexCount > num)
			{
				num = vertexCount;
				result = val;
			}
		}
		if ((Object)result != (Object)null)
		{
			LogDiagnosticOnce("ak-synthetic-mesh-candidate", $"Selected raw bundle mesh candidate: name={((Object)result).name}, verts={result.vertexCount}, score={num}");
		}
		return result;
	}

	private static Material[] ChooseBundleMaterials(Material[] materials, Texture2D[] textures, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		List<Material> list = new List<Material>();
		if (materials != null)
		{
			foreach (Material val in materials.OrderByDescending(ScoreBundleMaterial))
			{
				if ((Object)val == (Object)null)
				{
					continue;
				}
				if (ScoreBundleMaterial(val) < -1000)
				{
					continue;
				}
				list.Add(val);
				if (list.Count >= subMeshCount)
				{
					break;
				}
			}
		}
		if (list.Count == 0)
		{
			Texture2D val2 = SelectBestBundleTexture(textures);
			for (int i = 0; i < subMeshCount; i++)
			{
				list.Add(CreateSyntheticFallbackMaterial(val2));
			}
		}
		return NormalizeBundleMaterialArray(list.ToArray(), subMeshCount);
	}

	private static int ScoreBundleMaterial(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return int.MinValue;
		}
		string text = (((Object)material).name ?? string.Empty).ToLowerInvariant();
		int num = 0;
		if (text.Contains("ak") || text.Contains("gun") || text.Contains("weapon") || text.Contains("body") || text.Contains("mat"))
		{
			num += 4000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("particle") || text.Contains("smoke") || text.Contains("flash"))
		{
			num -= 12000;
		}
		Texture mainTexture = material.mainTexture;
		if ((Object)mainTexture != (Object)null)
		{
			num += 2500;
		}
		string text2 = material.shader?.name?.ToLowerInvariant() ?? string.Empty;
		if (text2.Contains("particle") || text2.Contains("vfx"))
		{
			num -= 10000;
		}
		return num;
	}

	private static Texture2D SelectBestBundleTexture(Texture2D[] textures)
	{
		if (textures == null || textures.Length == 0)
		{
			return null;
		}
		Texture2D result = null;
		int num = int.MinValue;
		foreach (Texture2D val in textures)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			string text = (((Object)val).name ?? string.Empty).ToLowerInvariant();
			int num2 = ((Texture)val).width * ((Texture)val).height;
			if (IsLikelyIconTexture(val))
			{
				num2 -= 20000;
			}
			if (text.Contains("ak") || text.Contains("gun") || text.Contains("weapon") || text.Contains("body") || text.Contains("albedo") || text.Contains("base"))
			{
				num2 += 12000;
			}
			if (text.Contains("icon") || text.Contains("ui") || text.Contains("thumb") || text.Contains("inventory") || text.Contains("vfx") || text.Contains("effect") || text.Contains("particle"))
			{
				num2 -= 15000;
			}
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		return result;
	}

	private static Material CreateSyntheticFallbackMaterial(Texture2D mainTexture)
	{
		Material val = new Material(ResolveLocalVisualCompatibleShader());
		if (val.HasProperty("_BaseColor"))
		{
			val.SetColor("_BaseColor", Color.white);
		}
		if (val.HasProperty("_Color"))
		{
			val.SetColor("_Color", Color.white);
		}
		if ((Object)mainTexture != (Object)null)
		{
			if (val.HasProperty("_BaseMap"))
			{
				val.SetTexture("_BaseMap", mainTexture);
			}
			if (val.HasProperty("_MainTex"))
			{
				val.SetTexture("_MainTex", mainTexture);
			}
		}
		return val;
	}

	private static Material[] NormalizeBundleMaterialArray(Material[] source, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		Material[] array = (Material[])(object)new Material[subMeshCount];
		if (source == null || source.Length == 0)
		{
			for (int i = 0; i < subMeshCount; i++)
			{
				array[i] = CreateSyntheticFallbackMaterial(null);
			}
			return array;
		}
		for (int j = 0; j < subMeshCount; j++)
		{
			Material val = source[Mathf.Clamp(j, 0, source.Length - 1)];
			array[j] = (((Object)val != (Object)null) ? val : CreateSyntheticFallbackMaterial(null));
		}
		return array;
	}

	private bool IsChineseLanguage()
	{
		bool isChinese;
		string languageName;
		string source;
		return TryResolveGameLanguage(out isChinese, out languageName, out source) && isChinese;
	}

	private bool GetCachedChineseLanguageSetting(bool forceRefresh = false)
	{
		if (forceRefresh || Time.unscaledTime - _lastLanguagePollTime >= LanguagePollInterval)
		{
			_cachedIsChineseLanguage = IsChineseLanguage();
			_lastLanguagePollTime = Time.unscaledTime;
		}
		return _cachedIsChineseLanguage;
	}

	internal bool GetCachedChineseLanguageSettingRuntime(bool forceRefresh = false)
	{
		return GetCachedChineseLanguageSetting(forceRefresh);
	}

	private bool TryResolveGameLanguage(out bool isChinese, out string languageName, out string source)
	{
		if (TryGetConfiguredGameLanguage(out isChinese, out languageName))
		{
			source = "PlayerPrefs.LanguageSetting";
			return true;
		}
		if (TryGetLocalizedTextCurrentLanguage(out isChinese, out languageName))
		{
			source = "LocalizedText.CURRENT_LANGUAGE";
			return true;
		}
		isChinese = false;
		languageName = string.Empty;
		source = string.Empty;
		return false;
	}

	private static bool TryGetConfiguredGameLanguage(out bool isChinese, out string languageName)
	{
		isChinese = false;
		languageName = string.Empty;
		try
		{
			if (!PlayerPrefs.HasKey("LanguageSetting"))
			{
				return false;
			}
			if (TryGetPlayerPrefsInt("LanguageSetting", out var value))
			{
				languageName = GetConfiguredLanguageName(value);
				isChinese = value == 9;
				return true;
			}
			string text = PlayerPrefs.GetString("LanguageSetting", string.Empty);
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			{
				languageName = GetConfiguredLanguageName(value);
				isChinese = value == 9;
				return true;
			}
			languageName = text;
			isChinese = IsChineseLanguageName(text);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLikelyAkVisualPrefab(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return false;
		}
		string text = (((Object)candidate).name ?? string.Empty).ToLowerInvariant();
		if (!(text == "vfx_ak") && !text.Contains("vfx_ak"))
		{
			if (text.Contains("vfx"))
			{
				return text.Contains("ak");
			}
			return false;
		}
		return true;
	}

	private static string DescribeRenderableCounts(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return "no-renderers";
		}
		MeshRenderer[] componentsInChildren = root.GetComponentsInChildren<MeshRenderer>(true);
		SkinnedMeshRenderer[] componentsInChildren2 = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		MeshFilter[] componentsInChildren3 = root.GetComponentsInChildren<MeshFilter>(true);
		return $"{componentsInChildren.Length} MeshRenderers, {componentsInChildren2.Length} SkinnedMeshRenderers, {componentsInChildren3.Length} MeshFilters";
	}

	private static bool TryGetLocalizedTextCurrentLanguage(out bool isChinese, out string languageName)
	{
		isChinese = false;
		languageName = string.Empty;
		try
		{
			object obj = ((typeof(Item).Assembly.GetType("LocalizedText") ?? ResolveLoadedType("LocalizedText"))?.GetField("CURRENT_LANGUAGE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(null);
			if (obj == null)
			{
				return false;
			}
			languageName = obj.ToString() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(languageName))
			{
				languageName = Convert.ToInt32(obj, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
			}
			isChinese = IsChineseLanguageName(languageName);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetPlayerPrefsInt(string key, out int value)
	{
		value = int.MinValue;
		try
		{
			value = PlayerPrefs.GetInt(key, int.MinValue);
			return value != int.MinValue;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsChineseLanguageName(string languageName)
	{
		if (string.IsNullOrWhiteSpace(languageName))
		{
			return false;
		}
		if (languageName.IndexOf("SimplifiedChinese", StringComparison.OrdinalIgnoreCase) < 0 && languageName.IndexOf("TraditionalChinese", StringComparison.OrdinalIgnoreCase) < 0 && !languageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && languageName.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return languageName.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private static string GetConfiguredLanguageName(int languageValue)
	{
		return languageValue switch
		{
			0 => "English", 
			1 => "French", 
			2 => "Italian", 
			3 => "German", 
			4 => "SpanishSpain", 
			5 => "SpanishLatam", 
			6 => "BRPortuguese", 
			7 => "Russian", 
			8 => "Ukrainian", 
			9 => "SimplifiedChinese", 
			10 => "Japanese", 
			11 => "Korean", 
			12 => "Polish", 
			13 => "Turkish", 
			_ => languageValue.ToString(CultureInfo.InvariantCulture), 
		};
	}

	private ConfigEntry<float> BindRangedFloat(string section, string key, float defaultValue, float minValue, float maxValue)
	{
		return ((BaseUnityPlugin)this).Config.Bind<float>(section, key, defaultValue, new ConfigDescription(GetLocalizedDescription(key, isChinese: false), (AcceptableValueBase)(object)new AcceptableValueRange<float>(minValue, maxValue), Array.Empty<object>()));
	}

	private ConfigEntry<int> BindRangedInt(string section, string key, int defaultValue, int minValue, int maxValue)
	{
		return ((BaseUnityPlugin)this).Config.Bind<int>(section, key, defaultValue, new ConfigDescription(GetLocalizedDescription(key, isChinese: false), (AcceptableValueBase)(object)new AcceptableValueRange<int>(minValue, maxValue), Array.Empty<object>()));
	}

	private ConfigEntry<string> BindSelectableString(string section, string key, string defaultValue, string[] values)
	{
		return ((BaseUnityPlugin)this).Config.Bind<string>(section, key, defaultValue, new ConfigDescription(GetLocalizedDescription(key, isChinese: false), (AcceptableValueBase)(object)new AcceptableValueList<string>(values ?? Array.Empty<string>()), Array.Empty<object>()));
	}

	private static string GetConfigBindingSectionName(string canonicalSection, bool isChinese)
	{
		return NormalizeSectionAlias(canonicalSection);
	}

	private static string GetConfigBindingKeyName(string canonicalKey, bool isChinese)
	{
		return NormalizeConfigKeyAlias(canonicalKey);
	}

	private static void NormalizeConfigRanges()
	{
		if (ZombieBehaviorDifficulty != null)
		{
			ZombieBehaviorDifficulty.Value = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
		}
		FireInterval.Value = Mathf.Clamp(FireInterval.Value, 0.1f, 3f);
		FireVolume.Value = Mathf.Clamp(FireVolume.Value, 0f, 1.5f);
		WeaponModelPitch.Value = Mathf.Clamp(WeaponModelPitch.Value, -180f, 180f);
		WeaponModelYaw.Value = Mathf.Clamp(WeaponModelYaw.Value, -180f, 180f);
		WeaponModelRoll.Value = Mathf.Clamp(WeaponModelRoll.Value, -180f, 180f);
		WeaponModelScale.Value = Mathf.Clamp(WeaponModelScale.Value, 0.3f, 2f);
		WeaponModelOffsetX.Value = Mathf.Clamp(WeaponModelOffsetX.Value, -0.5f, 0.5f);
		WeaponModelOffsetY.Value = Mathf.Clamp(WeaponModelOffsetY.Value, -0.5f, 0.5f);
		WeaponModelOffsetZ.Value = Mathf.Clamp(WeaponModelOffsetZ.Value, -0.5f, 0.5f);
		ZombieTimeReduction.Value = Mathf.Clamp(ZombieTimeReduction.Value, 0f, 60f);
		MaxZombies.Value = Mathf.Clamp(MaxZombies.Value, 0, 30);
		ZombieSpawnCount.Value = Mathf.Clamp(ZombieSpawnCount.Value, 0, 30);
		ZombieSpawnInterval.Value = Mathf.Clamp(ZombieSpawnInterval.Value, 1f, 120f);
		ZombieMaxLifetime.Value = Mathf.Clamp(ZombieMaxLifetime.Value, 10f, 600f);
		if (ZombieKnockbackForce != null)
		{
			ZombieKnockbackForce.Value = Mathf.Clamp(ZombieKnockbackForce.Value, 0f, 2000f);
		}
	}

	private void InitConfig()
	{
		// é…ç½®é”®åä¿æŒç¨³å®šå¹¶é›†ä¸­åœ¨è¿™é‡Œå®šä¹‰ï¼Œä¾¿äºŽè¿ç§»æ—§é…ç½®æ–‡ä»¶ã€‚
		// Keep config keys centralized here so legacy config migration stays predictable.
		bool isChinese = IsChineseLanguage();
		string configBindingSectionName = GetConfigBindingSectionName(FeaturesConfigSectionName, isChinese);
		string configBindingSectionName2 = GetConfigBindingSectionName(WeaponConfigSectionName, isChinese);
		string configBindingSectionName3 = GetConfigBindingSectionName(ZombieConfigSectionName, isChinese);
		ModEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(configBindingSectionName, GetConfigBindingKeyName("Mod", isChinese), true, GetLocalizedDescription("Mod", isChinese: false));
		OpenConfigPanelKey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(configBindingSectionName, GetConfigBindingKeyName("Open Config Panel", isChinese), KeyCode.Backslash, GetLocalizedDescription("Open Config Panel", isChinese: false));
		ConfigPanelTheme = BindSelectableString(configBindingSectionName, GetConfigBindingKeyName("Config Panel Theme", isChinese), DefaultConfigPanelThemeOption, ConfigPanelThemeValues);
		string text0 = LobbyConfigPanel.NormalizeThemeSelectionValue(ConfigPanelTheme.Value);
		if (!string.Equals(ConfigPanelTheme.Value, text0, StringComparison.Ordinal))
		{
			ConfigPanelTheme.Value = text0;
			SavePluginConfigQuietly();
		}
		WeaponEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(configBindingSectionName2, GetConfigBindingKeyName("Weapon", isChinese), true, GetLocalizedDescription("Weapon", isChinese: false));
		WeaponSelection = BindSelectableString(configBindingSectionName2, GetConfigBindingKeyName("Weapon Selection", isChinese), DefaultWeaponSelection, WeaponSelectionValues);
		string text = NormalizeWeaponSelection(WeaponSelection.Value);
		if (!string.Equals(WeaponSelection.Value, text, StringComparison.Ordinal))
		{
			WeaponSelection.Value = text;
			SavePluginConfigQuietly();
		}
		SpawnWeaponKey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(configBindingSectionName2, GetConfigBindingKeyName("Spawn Weapon", isChinese), (KeyCode)116, GetLocalizedDescription("Spawn Weapon", isChinese: false));
		FireInterval = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Fire Interval", isChinese), 0.4f, 0.1f, 3f);
		FireVolume = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Fire Volume", isChinese), 0.8f, 0f, 1.5f);
		AkSoundSelection = BindSelectableString(configBindingSectionName2, GetConfigBindingKeyName("AK Sound", isChinese), DefaultAkSoundOption, AkSoundSelectionValues);
		string text2 = NormalizeAkSoundSelection(AkSoundSelection.Value);
		if (!string.Equals(AkSoundSelection.Value, text2, StringComparison.Ordinal))
		{
			AkSoundSelection.Value = text2;
			SavePluginConfigQuietly();
		}
		AkSoundSelection.SettingChanged += delegate
		{
			OnAkSoundSelectionChanged();
		};
		ZombieTimeReduction = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Zombie Time Reduction", isChinese), DefaultZombieTimeReductionSeconds, 0f, 60f);
		WeaponModelScale = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Weapon Model Scale", isChinese), _localWeaponModelScale.x, 0.3f, 2f);
		WeaponModelPitch = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model X Rotation", isChinese), _localWeaponModelEuler.x, -180f, 180f);
		WeaponModelYaw = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Y Rotation", isChinese), _localWeaponModelEuler.y, -180f, 180f);
		WeaponModelRoll = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Z Rotation", isChinese), _localWeaponModelEuler.z, -180f, 180f);
		WeaponModelOffsetX = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model X Position", isChinese), _localWeaponModelOffset.x, -0.5f, 0.5f);
		WeaponModelOffsetY = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Y Position", isChinese), _localWeaponModelOffset.y, -0.5f, 0.5f);
		WeaponModelOffsetZ = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Z Position", isChinese), _localWeaponModelOffset.z, -0.5f, 0.5f);
		if (Mathf.Abs(WeaponModelYaw.Value - 10f) < 0.001f || Mathf.Abs(WeaponModelYaw.Value - 11.5f) < 0.001f || Mathf.Abs(WeaponModelYaw.Value - -4f) < 0.001f)
		{
			WeaponModelYaw.Value = _localWeaponModelEuler.y;
			SavePluginConfigQuietly();
		}
		if (Mathf.Abs(WeaponModelScale.Value - 2.8f) < 0.001f || Mathf.Abs(WeaponModelScale.Value - 2.55f) < 0.001f || Mathf.Abs(WeaponModelScale.Value - 0.8f) < 0.001f)
		{
			WeaponModelScale.Value = _localWeaponModelScale.x;
			SavePluginConfigQuietly();
		}
		if (Mathf.Abs(WeaponModelOffsetX.Value - -0.01f) < 0.001f && Mathf.Abs(WeaponModelOffsetY.Value - 0.01f) < 0.001f && Mathf.Abs(WeaponModelOffsetZ.Value) < 0.001f)
		{
			WeaponModelOffsetX.Value = _localWeaponModelOffset.x;
			WeaponModelOffsetY.Value = _localWeaponModelOffset.y;
			WeaponModelOffsetZ.Value = _localWeaponModelOffset.z;
			SavePluginConfigQuietly();
		}
		MaxZombies = BindRangedInt(configBindingSectionName3, GetConfigBindingKeyName("Max Count", isChinese), DefaultMaxZombieCount, 0, 30);
		ZombieSpawnCount = BindRangedInt(configBindingSectionName3, GetConfigBindingKeyName("Spawn Count", isChinese), DefaultZombieSpawnCount, 0, 30);
		ZombieSpawnInterval = BindRangedFloat(configBindingSectionName3, GetConfigBindingKeyName("Spawn Interval", isChinese), 15f, 1f, 120f);
		ZombieMaxLifetime = BindRangedFloat(configBindingSectionName3, GetConfigBindingKeyName("Max Lifetime", isChinese), 120f, 10f, 600f);
		ZombieBehaviorDifficulty = BindSelectableString(configBindingSectionName3, GetConfigBindingKeyName("Behavior Difficulty", isChinese), DefaultZombieBehaviorDifficulty, ZombieBehaviorDifficultyValues);
		ZombieKnockbackForce = BindRangedFloat(configBindingSectionName3, GetConfigBindingKeyName("Knockback Force", isChinese), DefaultZombieKnockbackForce, 0f, 2000f);
		string text3 = NormalizeZombieBehaviorDifficultySelection(string.IsNullOrWhiteSpace(_loadedZombieBehaviorDifficultyMetadata) ? ZombieBehaviorDifficulty.Value : _loadedZombieBehaviorDifficultyMetadata);
		if (!string.Equals(ZombieBehaviorDifficulty.Value, text3, StringComparison.Ordinal))
		{
			ZombieBehaviorDifficulty.Value = text3;
		}
		ApplyZombieBehaviorDifficultyPreset(text3);
		ApplySimplifiedZombieDerivedValues();
		_lastZombieBehaviorDifficulty = text3;
		NormalizeConfigRanges();
		RemoveLegacyInventorySlotConfig();
		RemoveLegacyFogModeConfig();
		RemoveLegacyPlayerShotConfig();
		RemoveLegacyRecoilConfig();
		RefreshOwnedConfigEntryCache();
		SubscribeOwnedConfigEntryChanges();
		SavePluginConfigQuietly();
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		ReleaseRoomConfigCallbacks();
		UnsubscribeOwnedConfigEntryChanges();
		StopGameplayLoadoutBootstrap();
		StopOwnedModConfigStabilizer();
		CleanupLocalHeldDebugSphere();
		CleanupMuzzleFlashPool();
		CleanupRemoteGunshotAudioPool();
		CleanupDartImpactVfxPool();
		ClearRuntimePatchCaches();
		_lobbyConfigPanel?.Dispose();
		_lobbyConfigPanel = null;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		string sceneBucket = GetSceneBucket(scene);
		if (string.Equals(sceneBucket, "other", StringComparison.Ordinal))
		{
			return;
		}
		if (string.Equals(sceneBucket, _activeSceneBucket, StringComparison.Ordinal))
		{
			return;
		}
		_activeSceneBucket = sceneBucket;
		BeginSceneRuntimeWarmup(scene);
		ClearRuntimePatchCaches();
		if (_scanCoroutine != null)
		{
			((MonoBehaviour)this).StopCoroutine(_scanCoroutine);
			_scanCoroutine = null;
		}
		StopGameplayLoadoutBootstrap();
		ResetLoadoutGrantTracking(clearPersistentRecords: true);
		CleanupWeaponLobbyNotice();
		CleanupLocalWeaponVisual();
		CleanupLocalHeldDebugSphere();
		RequestAkVisualRefresh(includeUiRefresh: true);
		_lastLocalizedModConfigUiFrame = -1;
		_localCharacter = null;
		_hasWeapon = false;
		_cachedHeldBlowgunItem = null;
		_lastHeldBlowgunSearchTime = -10f;
		_pendingRemoteWeaponGrant = false;
		_pendingRemoteFirstAidGrant = false;
		_lastPendingRemoteGrantAttemptTime = -10f;
		ZombieSpawner.StopZombieSpawning();
		ZombieSpawner.ClearZombies();
		bool flag2 = scene.name != null && scene.name.Contains("Airport");
		if (scene.name != null && !scene.name.Contains("Pretitle") && !scene.name.Contains("Title") && !flag2)
		{
			_resourcesLoaded = false;
			_scanCoroutine = ((MonoBehaviour)this).StartCoroutine(PeriodicPlayerScan());
			if (IsZombieSpawnFeatureEnabled())
			{
				ZombieSpawner.StartZombieSpawning();
			}
		}
		if (IsModConfigUiRuntimeSafeScene(scene))
		{
			((MonoBehaviour)this).StartCoroutine(RefreshLocalizedUiAfterStartup());
		}
	}

	private static void ClearRuntimePatchCaches()
	{
		InventoryItemUiPatch.ClearCaches();
		AkUiPatchHelpers.ClearRuntimeCaches();
		ZombieDeathPatch.ClearCaches();
	}

	private static void ResetLoadoutGrantTracking(bool clearPersistentRecords)
	{
		_receivedItem.Clear();
		_receivedFirstAid.Clear();
		if (clearPersistentRecords)
		{
			_persistentReceivedItem.Clear();
			_persistentReceivedFirstAid.Clear();
		}
		_pendingRemoteWeaponGrantActors.Clear();
		_pendingRemoteFirstAidGrantActors.Clear();
		_lastWeaponGrantTimeByActor.Clear();
		_lastFirstAidGrantTimeByActor.Clear();
		_weaponMissingSinceByActor.Clear();
		_recentWeaponDropTimeByActor.Clear();
	}

	private void StopGameplayLoadoutBootstrap()
	{
		if (_gameplayLoadoutBootstrapCoroutine != null)
		{
			((MonoBehaviour)this).StopCoroutine(_gameplayLoadoutBootstrapCoroutine);
			_gameplayLoadoutBootstrapCoroutine = null;
		}
	}

	private void RestartGameplayLoadoutBootstrapIfNeeded()
	{
		StopGameplayLoadoutBootstrap();
		if (!IsGameplayScene(SceneManager.GetActiveScene()) || !IsWeaponFeatureEnabled())
		{
			return;
		}
		_gameplayLoadoutBootstrapCoroutine = ((MonoBehaviour)this).StartCoroutine(GameplayLoadoutBootstrapCoroutine());
	}

	private IEnumerator GameplayLoadoutBootstrapCoroutine()
	{
		float deadline = Time.unscaledTime + GameplayLoadoutBootstrapDuration;
		while (Time.unscaledTime < deadline && IsGameplayScene(SceneManager.GetActiveScene()))
		{
			ProcessPendingRemoteGrantRequests(force: true);
			Character localCharacter = Character.localCharacter ?? _localCharacter;
			if ((Object)localCharacter != (Object)null && IsLoadoutGrantEligible(localCharacter))
			{
				int characterGrantTrackingId = GetCharacterGrantTrackingId(localCharacter);
				if (characterGrantTrackingId != int.MinValue)
				{
					bool flag = CharacterAlreadyHasShootZombiesWeapon(localCharacter);
					if (flag)
					{
						MarkWeaponGrantedForCharacter(localCharacter);
						_gameplayLoadoutBootstrapCoroutine = null;
						yield break;
					}
					if (!HasWeaponGrantRecord(characterGrantTrackingId) && !IsPendingRemoteWeaponGrant(characterGrantTrackingId) && !HasRecentWeaponDrop(characterGrantTrackingId))
					{
						TryGiveItemTo(localCharacter);
					}
					if (CharacterAlreadyHasShootZombiesWeapon(localCharacter))
					{
						MarkWeaponGrantedForCharacter(localCharacter);
						_gameplayLoadoutBootstrapCoroutine = null;
						yield break;
					}
				}
			}
			yield return (object)new WaitForSecondsRealtime(GameplayLoadoutBootstrapRetryInterval);
		}
		_gameplayLoadoutBootstrapCoroutine = null;
	}

	private void LoadResources()
	{
		if (_resourcesLoaded)
		{
			return;
		}
		try
		{
			ReplaceBlowgunSound();
			_resourcesLoaded = true;
		}
		catch (Exception)
		{
		}
	}

	private void CreateIconFromModel()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			GameObject val = new GameObject("AK47IconCamera");
			Camera obj = val.AddComponent<Camera>();
			obj.orthographic = false;
			obj.fieldOfView = 18f;
			obj.clearFlags = CameraClearFlags.SolidColor;
			obj.backgroundColor = new Color(0f, 0f, 0f, 0f);
			obj.cullingMask = -1;
			obj.nearClipPlane = 0.01f;
			obj.farClipPlane = 100f;
			obj.allowHDR = false;
			obj.allowMSAA = true;
			obj.useOcclusionCulling = false;
			GameObject val2 = new GameObject("IconLight");
			val2.transform.SetParent(val.transform);
			val2.transform.localPosition = new Vector3(2.2f, 2.8f, -2.8f);
			Light obj2 = val2.AddComponent<Light>();
			obj2.type = (LightType)1;
			obj2.intensity = 2.1f;
			obj2.color = Color.white;
			GameObject val3 = new GameObject("FillLight");
			val3.transform.SetParent(val.transform);
			val3.transform.localPosition = new Vector3(-2.1f, 1.4f, -2.2f);
			Light obj3 = val3.AddComponent<Light>();
			obj3.type = (LightType)1;
			obj3.intensity = 0.95f;
			obj3.color = Color.white;
			int num = 256;
			RenderTexture val4 = new RenderTexture(num, num, 24, RenderTextureFormat.ARGB32);
			val4.antiAliasing = 2;
			val4.useMipMap = false;
			val4.autoGenerateMips = false;
			val4.filterMode = FilterMode.Bilinear;
			val4.Create();
			obj.targetTexture = val4;
			GameObject val5 = Object.Instantiate<GameObject>(_ak47Prefab);
			val5.transform.position = Vector3.zero;
			val5.transform.rotation = Quaternion.Euler(-5f, -90f, 0f);
			val5.transform.localScale = Vector3.one * 0.54f;
			ItemPatch.PrepareAkVisualRenderers(val5, null);
			if (TryGetAkIconBounds(val5, out var bounds))
			{
				Vector3 center = bounds.center + new Vector3(0.04f, -0.02f, 0f);
				float num2 = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
				float num3 = Mathf.Max(1.8f, num2 / Mathf.Tan(obj.fieldOfView * 0.5f * (Mathf.PI / 180f)) * 2.15f);
				val.transform.position = center - Vector3.forward * num3;
				val.transform.rotation = Quaternion.identity;
			}
			else
			{
				val.transform.position = new Vector3(0.03f, -0.02f, -2.4f);
				val.transform.rotation = Quaternion.identity;
			}
			RenderTexture.active = val4;
			GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
			RenderTexture.active = null;
			obj.Render();
			_ak47IconTexture = new Texture2D(num, num, TextureFormat.RGBA32, false);
			((Object)_ak47IconTexture).name = "AK47_GeneratedIcon";
			_ak47IconTexture.wrapMode = TextureWrapMode.Clamp;
			_ak47IconTexture.filterMode = FilterMode.Bilinear;
			RenderTexture.active = val4;
			_ak47IconTexture.ReadPixels(new Rect(0f, 0f, (float)num, (float)num), 0, 0);
			ForceTransparentBackground(_ak47IconTexture);
			_ak47IconTexture.Apply();
			RenderTexture.active = null;
			obj.targetTexture = null;
			val4.Release();
			Object.DestroyImmediate((Object)val4);
			Object.DestroyImmediate((Object)val5);
			Object.DestroyImmediate((Object)val);
		}
		catch (Exception)
		{
		}
	}

	public static Texture2D GetAkIconTexture()
	{
		if ((Object)_ak47IconTexture != (Object)null && !IsLikelyIconTexture(_ak47IconTexture))
		{
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null && _ak47ItemContent != null)
		{
			_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
		}
		if ((Object)_ak47IconTexture != (Object)null && !_useExplicitSelectedWeaponIcon && !IsGeneratedModelIcon(_ak47IconTexture) && IsIconLikelyTooDark(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-too-dark:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected as too dark: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			Instance?.CreateIconFromModel();
			if ((Object)_ak47IconTexture != (Object)null)
			{
				LogDiagnosticOnce("ak-icon-generated:" + DescribeTextureForDiagnostics(_ak47IconTexture), "CreateIconFromModel result: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			}
		}
		if ((Object)_ak47IconTexture != (Object)null && !_useExplicitSelectedWeaponIcon && !IsGeneratedModelIcon(_ak47IconTexture) && IsIconLikelyTooDark(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-post-generate-too-dark:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected after generation check: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture != (Object)null && !IsLikelyIconTexture(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-invalid:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected as invalid UI texture: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			LogDiagnosticOnce("ak-icon-fallback", "AK icon fell back to built-in placeholder icon");
			_ak47IconTexture = CreateFallbackIconTexture();
		}
		return _ak47IconTexture;
	}

	public static Texture2D GetAkIconTexture(string selection)
	{
		string text = NormalizeWeaponSelection(selection);
		if (TryGetResolvedWeaponIcon(text, out var icon) && (Object)icon != (Object)null)
		{
			return icon;
		}
		if (string.Equals(text, GetCurrentWeaponSelection(), StringComparison.OrdinalIgnoreCase))
		{
			return GetAkIconTexture();
		}
		if ((Object)_genericResolvedWeaponIcon != (Object)null)
		{
			return _genericResolvedWeaponIcon;
		}
		return GetAkIconTexture();
	}

	internal static void LogDiagnosticOnce(string key, string message)
	{
		try
		{
			if (!EnableDiagnosticLogs)
			{
				return;
			}
			if (string.IsNullOrWhiteSpace(key))
			{
				key = "diagnostic:" + message;
			}
			if (!_diagnosticLogKeys.Add(key))
			{
				return;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)("[ShootZombies][Diag] " + message));
			}
		}
		catch
		{
		}
	}

	internal static void LogDiagnostic(string message)
	{
		try
		{
			if (!EnableDiagnosticLogs)
			{
				return;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)("[ShootZombies][Diag] " + message));
			}
		}
		catch
		{
		}
	}

	private static bool TryGetAkIconBounds(GameObject root, out Bounds bounds)
	{
		bounds = default(Bounds);
		if ((Object)root == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if (!((Object)val == (Object)null) && val.enabled && !val.forceRenderingOff)
			{
				if (!flag)
				{
					bounds = val.bounds;
					flag = true;
				}
				else
				{
					bounds.Encapsulate(val.bounds);
				}
			}
		}
		return flag;
	}

	private static void ForceTransparentBackground(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return;
		}
		try
		{
			Color32[] pixels = texture.GetPixels32();
			if (pixels == null || pixels.Length == 0)
			{
				return;
			}
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 val = pixels[i];
				if ((int)val.a <= 10)
				{
					pixels[i] = new Color32(0, 0, 0, 0);
				}
			}
			texture.SetPixels32(pixels);
		}
		catch
		{
		}
	}

	internal static string DescribeAkVisualPrefabForDiagnostics()
	{
		return DescribeGameObjectForDiagnostics(_ak47Prefab);
	}

	internal static string DescribeAkVisualPrefabForDiagnostics(string selection)
	{
		return DescribeGameObjectForDiagnostics(GetAkVisualPrefab(selection));
	}

	private static bool IsGeneratedModelIcon(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		string text = ((Object)texture).name ?? string.Empty;
		if (!text.Equals("AK47_GeneratedIcon", StringComparison.Ordinal))
		{
			return text.Equals("AK47_FallbackIcon", StringComparison.Ordinal);
		}
		return true;
	}

	public static GameObject GetAkVisualPrefab()
	{
		return _ak47Prefab;
	}

	public static GameObject GetAkVisualPrefab(string selection)
	{
		if (TryGetResolvedWeaponPrefab(selection, out var prefab) && HasRenderableGeometry(prefab))
		{
			return prefab;
		}
		if (HasRenderableGeometry(_genericResolvedWeaponPrefab))
		{
			return _genericResolvedWeaponPrefab;
		}
		return _ak47Prefab;
	}

	public static bool HasAkVisualPrefab()
	{
		return HasRenderableGeometry(_ak47Prefab);
	}

	public static bool HasAkVisualPrefab(string selection)
	{
		return HasRenderableGeometry(GetAkVisualPrefab(selection));
	}

	internal static bool IsLocalWeaponVisualFollowerEnabled()
	{
		return EnableLocalWeaponVisualFollower;
	}

	internal static float GetWeaponModelYaw()
	{
		return Mathf.Clamp(WeaponModelYaw?.Value ?? _localWeaponModelEuler.y, -180f, 180f);
	}

	internal static float GetWeaponModelPitch()
	{
		return Mathf.Clamp(WeaponModelPitch?.Value ?? _localWeaponModelEuler.x, -180f, 180f);
	}

	internal static float GetWeaponModelRoll()
	{
		return Mathf.Clamp(WeaponModelRoll?.Value ?? _localWeaponModelEuler.z, -180f, 180f);
	}

	internal static float GetWeaponModelScale()
	{
		return Mathf.Clamp(WeaponModelScale?.Value ?? _localWeaponModelScale.x, 0.3f, 2f);
	}

	internal static float GetWeaponVariantScaleMultiplier(string selection)
	{
		return Mathf.Clamp(GetWeaponVariantDefinition(selection)?.ScaleMultiplier ?? 1f, 0.3f, 3f);
	}

	internal static Vector3 GetWeaponVariantPositionOffset(string selection)
	{
		WeaponVariantDefinition weaponVariantDefinition = GetWeaponVariantDefinition(selection);
		return (weaponVariantDefinition != null) ? weaponVariantDefinition.PositionOffset : Vector3.zero;
	}

	internal static float GetEffectiveWeaponModelScale(string selection)
	{
		return Mathf.Clamp(GetWeaponModelScale() * GetWeaponVariantScaleMultiplier(selection), 0.3f, 6f);
	}

	internal static float GetWeaponModelOffsetX()
	{
		return Mathf.Clamp(WeaponModelOffsetX?.Value ?? _localWeaponModelOffset.x, -0.5f, 0.5f);
	}

	internal static float GetWeaponModelOffsetY()
	{
		return Mathf.Clamp(WeaponModelOffsetY?.Value ?? _localWeaponModelOffset.y, -0.5f, 0.5f);
	}

	internal static float GetWeaponModelOffsetZ()
	{
		return Mathf.Clamp(WeaponModelOffsetZ?.Value ?? _localWeaponModelOffset.z, -0.5f, 0.5f);
	}

	internal static Quaternion GetDirectAkRotationOverride()
	{
		return Quaternion.Euler(_weaponBaseEuler.x + GetWeaponModelPitch(), _weaponBaseEuler.y + GetWeaponModelYaw(), _weaponBaseEuler.z + GetWeaponModelRoll());
	}

	internal static Vector3 GetDirectAkPositionOverride(string selection)
	{
		return new Vector3(GetWeaponModelOffsetX(), GetWeaponModelOffsetY(), GetWeaponModelOffsetZ()) + GetWeaponVariantPositionOffset(selection);
	}

	internal static Vector3 GetDirectAkPositionOverride()
	{
		return GetDirectAkPositionOverride(GetCurrentWeaponSelection());
	}

	private static Vector3 GetConfiguredLocalWeaponModelEuler()
	{
		return new Vector3(_weaponBaseEuler.x + GetWeaponModelPitch(), _weaponBaseEuler.y + GetWeaponModelYaw(), _weaponBaseEuler.z + GetWeaponModelRoll());
	}

	private static Vector3 GetConfiguredLocalWeaponModelOffset()
	{
		return GetDirectAkPositionOverride();
	}

	private static bool HasRenderableGeometry(GameObject prefab)
	{
		if ((Object)prefab == (Object)null)
		{
			return false;
		}
		MeshFilter[] componentsInChildren = prefab.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				return true;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				return true;
			}
		}
		return false;
	}

	private static string FormatTransformPath(Transform transform)
	{
		if ((Object)transform == (Object)null)
		{
			return "null";
		}
		List<string> list = new List<string>();
		Transform val = transform;
		while ((Object)val != (Object)null)
		{
			list.Add(((Object)val).name);
			val = val.parent;
		}
		list.Reverse();
		return string.Join("/", list);
	}

	private static string DescribeGameObjectForDiagnostics(GameObject root, int maxEntries = 6)
	{
		if ((Object)root == (Object)null)
		{
			return "prefab=null";
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("name=").Append(((Object)root).name);
		stringBuilder.Append(", counts=").Append(DescribeRenderableCounts(root));
		List<string> list = new List<string>();
		MeshFilter[] componentsInChildren = root.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			MeshRenderer component = ((Component)val).GetComponent<MeshRenderer>();
			list.Add($"{FormatTransformPath(((Component)val).transform)}|mesh={((Object)val.sharedMesh).name}|verts={val.sharedMesh.vertexCount}|renderer={((Object)component != (Object)null ? ((Object)component).name : "none")}");
			if (list.Count >= maxEntries)
			{
				break;
			}
		}
		if (list.Count == 0)
		{
			SkinnedMeshRenderer[] componentsInChildren2 = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
			{
				if ((Object)val2 == (Object)null || (Object)val2.sharedMesh == (Object)null)
				{
					continue;
				}
				list.Add($"{FormatTransformPath(((Component)val2).transform)}|skinnedMesh={((Object)val2.sharedMesh).name}|verts={val2.sharedMesh.vertexCount}");
				if (list.Count >= maxEntries)
				{
					break;
				}
			}
		}
		if (list.Count != 0)
		{
			stringBuilder.Append(", candidates=").Append(string.Join(" || ", list));
		}
		return stringBuilder.ToString();
	}

	private static string DescribeTextureForDiagnostics(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return "null";
		}
		return $"name={((Object)texture).name}, size={((Texture)texture).width}x{((Texture)texture).height}, format={texture.format}, instance={((Object)texture).GetInstanceID()}";
	}

	private static bool IsIconLikelyTooDark(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		try
		{
			Color32[] pixels = texture.GetPixels32();
			if (pixels == null || pixels.Length == 0)
			{
				return false;
			}
			float num = 0f;
			int num2 = 0;
			int num3 = Mathf.Max(1, pixels.Length / 1024);
			for (int i = 0; i < pixels.Length; i += num3)
			{
				Color32 val = pixels[i];
				if (!((float)(int)val.a / 255f < 0.08f))
				{
					float num4 = (0.2126f * (float)(int)val.r + 0.7152f * (float)(int)val.g + 0.0722f * (float)(int)val.b) / 255f;
					num += num4;
					num2++;
				}
			}
			if (num2 < 8)
			{
				return false;
			}
			return num / (float)num2 < 0.08f;
		}
		catch
		{
			return false;
		}
	}

	private static Texture2D CreateFallbackIconTexture()
	{
		try
		{
			Texture2D val = new Texture2D(128, 128, (TextureFormat)5, false);
			((Object)val).name = "AK47_FallbackIcon";
			Color val2 = new Color(0f, 0f, 0f, 0f);
			Color val3 = new Color(0.94f, 0.94f, 0.94f, 1f);
			Color val4 = new Color(0.62f, 0.62f, 0.62f, 1f);
			Color[] array = (Color[])(object)new Color[16384];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = val2;
			}
			val.SetPixels(array);
			for (int j = 18; j < 104; j++)
			{
				for (int k = 58; k < 68; k++)
				{
					val.SetPixel(j, k, val3);
				}
			}
			for (int l = 76; l < 112; l++)
			{
				for (int m = 68; m < 80; m++)
				{
					val.SetPixel(l, m, val4);
				}
			}
			for (int n = 22; n < 40; n++)
			{
				for (int num = 44; num < 58; num++)
				{
					val.SetPixel(n, num, val3);
				}
			}
			for (int num2 = 52; num2 < 72; num2++)
			{
				for (int num3 = 68; num3 < 92; num3++)
				{
					if (num3 - 68 >= (num2 - 52) / 2)
					{
						val.SetPixel(num2, num3, val3);
					}
				}
			}
			for (int num4 = 92; num4 < 112; num4++)
			{
				for (int num5 = 54; num5 < 62; num5++)
				{
					val.SetPixel(num4, num5, val4);
				}
			}
			val.Apply();
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)"[ShootZombies] Using fallback icon texture");
			}
			return val;
		}
		catch
		{
			return null;
		}
	}

	private void ReplaceBlowgunModel()
	{
		if (IsRuntimeVisualRefreshBlocked())
		{
			return;
		}
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			if (TryResolveBaseWeaponItem(out var item))
			{
				ItemPatch.EnsureAkVisual(item, forceRefreshMarker: true);
				ItemPatch.EnsureAkVisualOnAllItems(forceRefresh: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private void RefreshWeaponModelVisuals()
	{
		try
		{
			ApplyLocalWeaponModelPose();
			ReplaceBlowgunModel();
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] RefreshWeaponModelVisuals failed: " + ex.Message));
			}
		}
	}

	private void ReplaceBlowgunLocalization()
	{
		try
		{
			Type type = typeof(Item).Assembly.GetType("LocalizedText");
			if (type != null)
			{
				FieldInfo field = type.GetField("mainTable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
				{
					object value = field.GetValue(null);
					if (value != null)
					{
						PropertyInfo property = value.GetType().GetProperty("Item");
						if (property != null)
						{
							List<string> list = new List<string>(15);
							for (int i = 0; i < 15; i++)
							{
								list.Add("AK-47");
							}
							property.SetValue(value, list, new object[1] { "NAME_AK" });
						}
					}
				}
			}
			if (_ak47ItemContent == null || !((Object)_ak47Prefab != (Object)null))
			{
				return;
			}
		}
		catch (Exception)
		{
		}
	}

	private void ReplaceBlowgunInstances()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			Log.LogWarning((object)"[ShootZombies] AK47 prefab is null, cannot replace blowgun");
			return;
		}
		try
		{
			ItemPatch.EnsureAkVisualOnAllItems();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] ReplaceBlowgunInstances error: " + ex));
		}
	}

	private void ReplaceBlowgunInstances_Old()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			int num = 0;
			Item[] array2 = array;
			foreach (Item val in array2)
			{
				if (_replacedBlowguns.Contains(((Component)val).gameObject))
				{
					continue;
				}
				string name = val.GetName();
				if (!ItemPatch.IsBlowgunLike(val, name))
				{
					continue;
				}
				MeshRenderer[] componentsInChildren = ((Component)val).GetComponentsInChildren<MeshRenderer>();
				MeshRenderer[] array3 = componentsInChildren;
				foreach (MeshRenderer val2 in array3)
				{
					if (!((Object)val2).name.Contains("Hand"))
					{
						((Renderer)val2).enabled = false;
					}
				}
				SkinnedMeshRenderer[] componentsInChildren2 = ((Component)val).GetComponentsInChildren<SkinnedMeshRenderer>();
				SkinnedMeshRenderer[] array4 = componentsInChildren2;
				foreach (SkinnedMeshRenderer val3 in array4)
				{
					if (!((Object)val3).name.Contains("Hand"))
					{
						((Renderer)val3).enabled = false;
					}
				}
				GameObject val4 = Object.Instantiate<GameObject>(_ak47Prefab, ((Component)val).transform);
				val4.transform.localPosition = Vector3.zero;
				val4.transform.localRotation = Quaternion.identity;
				val4.transform.localScale = Vector3.one;
				componentsInChildren = val4.GetComponentsInChildren<MeshRenderer>();
				for (int k = 0; k < componentsInChildren.Length; k++)
				{
					((Renderer)componentsInChildren[k]).enabled = true;
				}
				componentsInChildren2 = val4.GetComponentsInChildren<SkinnedMeshRenderer>();
				for (int l = 0; l < componentsInChildren2.Length; l++)
				{
					((Renderer)componentsInChildren2[l]).enabled = true;
				}
				_replacedBlowguns.Add(((Component)val).gameObject);
				num++;
			}
		}
		catch (Exception)
		{
		}
	}

	private static IEnumerator DelayedReplaceBlowgunModel()
	{
		yield return (object)new WaitForSeconds(0.5f);
		if (!((Object)(object)Instance == (Object)null))
		{
			Instance.ReplaceBlowgunInstances();
		}
		yield return (object)new WaitForSeconds(0.75f);
		if (!((Object)(object)Instance == (Object)null))
		{
			Instance.ReplaceBlowgunInstances();
		}
	}

	private static string NormalizeAkSoundSelection(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return DefaultAkSoundOption;
		}
		string a = value.Trim();
		string[] akSoundSelectionValues = AkSoundSelectionValues;
		foreach (string text in akSoundSelectionValues)
		{
			if (string.Equals(a, text, StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		switch (NormalizeLookupToken(value))
		{
		case "1":
		case "aksound1":
		case "aksounds1":
		case "sound1":
		case "声音1":
		case "音效1":
			return "ak_sound1";
		case "2":
		case "aksound2":
		case "aksounds2":
		case "sound2":
		case "声音2":
		case "音效2":
			return "ak_sound2";
		case "3":
		case "aksound3":
		case "aksounds3":
		case "sound3":
		case "声音3":
		case "音效3":
			return "ak_sound3";
		}
		if (string.Equals(a, "默认", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Default", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "default", StringComparison.OrdinalIgnoreCase))
		{
			return DefaultAkSoundOption;
		}
		return DefaultAkSoundOption;
	}

	private static string NormalizeZombieBehaviorDifficultySelection(string value)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text))
		{
			return DefaultZombieBehaviorDifficulty;
		}
		switch (text)
		{
		case "1":
		case "easy":
		case "simple":
		case "casual":
		case "休闲":
		case "简单":
			return "Easy";
		case "2":
		case "standard":
		case "normal":
		case "默认":
		case "标准":
			return "Standard";
		case "3":
		case "hard":
		case "困难":
			return "Hard";
		case "4":
		case "insane":
		case "brutal":
		case "疯狂":
		case "残酷":
			return "Insane";
		case "5":
		case "nightmare":
		case "hell":
		case "噩梦":
		case "地狱":
			return "Nightmare";
		default:
		{
			string[] zombieBehaviorDifficultyValues = ZombieBehaviorDifficultyValues;
			foreach (string text2 in zombieBehaviorDifficultyValues)
			{
				if (string.Equals(text, NormalizeLookupToken(text2), StringComparison.Ordinal))
				{
					return text2;
				}
			}
			return DefaultZombieBehaviorDifficulty;
		}
		}
	}

	private static int GetZombieBehaviorDifficultyPresetIndex(string difficulty)
	{
		string text = NormalizeZombieBehaviorDifficultySelection(difficulty);
		int num = Array.IndexOf(ZombieBehaviorDifficultyValues, text);
		if (num >= 0)
		{
			return num;
		}
		return 1;
	}

	private static ZombieBehaviorDifficultyPreset GetZombieBehaviorDifficultyPreset(string difficulty)
	{
		int zombieBehaviorDifficultyPresetIndex = GetZombieBehaviorDifficultyPresetIndex(difficulty);
		return ZombieBehaviorDifficultyPresets[Mathf.Clamp(zombieBehaviorDifficultyPresetIndex, 0, ZombieBehaviorDifficultyPresets.Length - 1)];
	}

	private void ApplyZombieBehaviorDifficultyPreset(string difficulty = null)
	{
		if (ZombieBehaviorDifficulty == null)
		{
			return;
		}
		string text = NormalizeZombieBehaviorDifficultySelection(difficulty ?? ZombieBehaviorDifficulty.Value);
		if (!string.Equals(ZombieBehaviorDifficulty.Value, text, StringComparison.Ordinal))
		{
			ZombieBehaviorDifficulty.Value = text;
		}
		DisableZombieSleepPatch.ResetRuntimeState();
		_pendingZombieSpeedRefresh = true;
		UpdateZombieSpeed(forceRefresh: true);
		ZombieSpawner.RefreshLiveZombieProperties();
	}

	private void ApplySimplifiedZombieDerivedValues()
	{
		if (!Application.isPlaying)
		{
			return;
		}
		if (IsZombieSpawnFeatureEnabled())
		{
			ZombieSpawner.StartZombieSpawning();
		}
		else
		{
			ZombieSpawner.StopZombieSpawning();
		}
	}

	private static float GetDerivedZombieSpawnIntervalRandomRange()
	{
		float num = Mathf.Max((ZombieSpawnInterval != null) ? ZombieSpawnInterval.Value : 15f, 1f);
		return Mathf.Clamp(num * 0.2f, 2f, 6f);
	}

	internal static float GetDerivedZombieSpawnIntervalRandomRangeRuntime()
	{
		return GetDerivedZombieSpawnIntervalRandomRange();
	}

	private static int GetDerivedZombieWaveMaxCount()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		if (num <= 0)
		{
			return 0;
		}
		int num2 = Mathf.Max((ZombieSpawnCount != null) ? ZombieSpawnCount.Value : DefaultZombieSpawnCount, 0);
		return Mathf.Clamp(num2, 0, num);
	}

	private static int GetDerivedZombieWaveMinCount()
	{
		return 0;
	}

	internal static int GetDerivedZombieWaveSpawnCountRuntime()
	{
		int derivedZombieWaveMaxCount = GetDerivedZombieWaveMaxCount();
		if (derivedZombieWaveMaxCount <= 0)
		{
			return 0;
		}
		return derivedZombieWaveMaxCount;
	}

	private static float GetDerivedZombieSpawnRadius()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		float num2 = NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty) switch
		{
			"Easy" => 2.5f,
			"Hard" => 0f,
			"Insane" => -1.5f,
			"Nightmare" => -3f,
			_ => 1.5f,
		};
		return Mathf.Clamp(12f + (float)Mathf.Max(num - 5, 0) * 0.45f + num2, 8f, 20f);
	}

	internal static float GetDerivedZombieSpawnRadiusRuntime()
	{
		return GetDerivedZombieSpawnRadius();
	}

	private static float GetDerivedZombieDestroyDistance()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		float num2 = NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty) switch
		{
			"Easy" => 0f,
			"Hard" => 8f,
			"Insane" => 12f,
			"Nightmare" => 16f,
			_ => 4f,
		};
		return Mathf.Clamp(72f + (float)Mathf.Max(num - 5, 0) * 1.5f + num2, 72f, 130f);
	}

	internal static float GetDerivedZombieDestroyDistanceRuntime()
	{
		return GetDerivedZombieDestroyDistance();
	}

	private static ZombieBehaviorDifficultyPreset GetCurrentZombieBehaviorDifficultyPresetRuntime()
	{
		return GetZombieBehaviorDifficultyPreset((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty);
	}

	internal static float GetZombieMoveSpeedMultiplierRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().MoveSpeedMultiplier, 0.1f, 5f);
	}

	internal static bool IsVanillaZombieBehaviorDifficultyRuntime()
	{
		return string.Equals(NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty), "Easy", StringComparison.Ordinal);
	}

	internal static float GetZombieKnockbackForceRuntime()
	{
		return Mathf.Clamp((ZombieKnockbackForce != null) ? ZombieKnockbackForce.Value : DefaultZombieKnockbackForce, 0f, 2000f);
	}

	internal static float GetZombieTargetSearchIntervalRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().TargetSearchInterval, 0.1f, 10f);
	}

	internal static float GetZombieBiteRecoveryTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().BiteRecoveryTime, 0f, 8f);
	}

	internal static float GetZombieSamePlayerBiteCooldownRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().SamePlayerBiteCooldown, 0.1f, 10f);
	}

	internal static float GetZombieSprintDistanceRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().SprintDistance, 1f, 50f);
	}

	internal static float GetZombieChaseTimeBeforeSprintRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().ChaseTimeBeforeSprint, 0f, 20f);
	}

	internal static float GetZombieLungeDistanceRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeDistance, 4f, 20f);
	}

	internal static float GetZombieLungeTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeTime, 0.1f, 5f);
	}

	internal static float GetZombieLungeRecoveryTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeRecoveryTime, 0f, 10f);
	}

	internal static float GetZombieLookAngleBeforeWakeupRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LookAngleBeforeWakeup, 0f, 180f);
	}

	internal static float GetZombieDistanceBeforeWakeupRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().DistanceBeforeWakeup, 5f, 100f);
	}

	internal static string GetZombieBehaviorDifficultyDisplayNameRuntime(string difficulty)
	{
		bool flag = Instance?.GetCachedChineseLanguageSetting() ?? false;
		switch (NormalizeZombieBehaviorDifficultySelection(difficulty))
		{
		case "Easy":
			return flag ? "简单" : "Easy";
		case "Hard":
			return flag ? "困难" : "Hard";
		case "Insane":
			return flag ? "疯狂" : "Insane";
		case "Nightmare":
			return flag ? "噩梦" : "Nightmare";
		default:
			return flag ? "标准" : "Standard";
		}
	}

	internal static string GetZombieBehaviorDifficultyDetailsRuntime()
	{
		bool flag = Instance?.GetCachedChineseLanguageSetting() ?? false;
		return flag ? "难度细节：\n简单：保留原版扑击、咬后恢复和咬人节奏，但对刷出的僵尸保持及时唤醒与追击，避免长时间站立。\n标准：移速 1.05x；索敌 6s；咬后 2.5s；冲刺 22m/2.25s；猛扑 10m/1.65s/4s。\n困难：移速 1.15x；索敌 3s；咬后 1.5s；冲刺 26m/1.25s；猛扑 12m/1.85s/2.5s。\n疯狂：移速 1.25x；索敌 1.5s；咬后 0.9s；冲刺 30m/0.85s；猛扑 13m/2s/1.5s。\n噩梦：移速 1.35x；索敌 0.75s；咬后 0.55s；冲刺 34m/0.65s；猛扑 14m/2.15s/0.85s。" : "Difficulty details:\nEasy: keeps vanilla lunge, bite recovery, and bite cadence, but spawned zombies still wake and pursue promptly so they do not stand idle for long periods.\nStandard: Move 1.05x; Search 6s; Post-bite 2.5s; Sprint 22m/2.25s; Lunge 10m/1.65s/4s.\nHard: Move 1.15x; Search 3s; Post-bite 1.5s; Sprint 26m/1.25s; Lunge 12m/1.85s/2.5s.\nInsane: Move 1.25x; Search 1.5s; Post-bite 0.9s; Sprint 30m/0.85s; Lunge 13m/2s/1.5s.\nNightmare: Move 1.35x; Search 0.75s; Post-bite 0.55s; Sprint 34m/0.65s; Lunge 14m/2.15s/0.85s.";
	}

	private static WeaponVariantDefinition GetWeaponVariantDefinition(string selection)
	{
		string text = NormalizeWeaponSelection(selection);
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition != null && string.Equals(weaponVariantDefinition.SelectionKey, text, StringComparison.OrdinalIgnoreCase))
			{
				return weaponVariantDefinition;
			}
		}
		return WeaponVariantDefinitions[0];
	}

	public static string GetWeaponDisplayName(string selection)
	{
		return GetWeaponVariantDefinition(selection)?.DisplayName ?? DefaultWeaponSelection;
	}

	private static string NormalizeLookupToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	private static bool MatchesWeaponAliasToken(string value, IEnumerable<string> aliases)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text) || aliases == null)
		{
			return false;
		}
		foreach (string alias in aliases)
		{
			string text2 = NormalizeLookupToken(alias);
			if (!string.IsNullOrEmpty(text2) && (string.Equals(text, text2, StringComparison.Ordinal) || text.StartsWith(text2, StringComparison.Ordinal) || (text2.Length >= 4 && text.IndexOf(text2, StringComparison.Ordinal) >= 0)))
			{
				return true;
			}
		}
		return false;
	}

	internal static string NormalizeWeaponSelection(string value)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text))
		{
			return DefaultWeaponSelection;
		}
		switch (text)
		{
		case "ak":
		case "ak47":
			return "AK47";
		case "mpx":
			return "MPX";
		case "hk416":
		case "hk417":
			return "HK416";
		}
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition != null && (MatchesWeaponAliasToken(text, weaponVariantDefinition.PrefabAliases) || MatchesWeaponAliasToken(text, weaponVariantDefinition.IconAliases) || string.Equals(text, NormalizeLookupToken(weaponVariantDefinition.SelectionKey), StringComparison.Ordinal)))
			{
				return weaponVariantDefinition.SelectionKey;
			}
		}
		return DefaultWeaponSelection;
	}

	public static string GetCurrentWeaponSelection()
	{
		return NormalizeWeaponSelection(WeaponSelection?.Value);
	}

	public static string GetCurrentAkSoundSelection()
	{
		return NormalizeAkSoundSelection(AkSoundSelection?.Value);
	}

	public static string GetCurrentWeaponDisplayName()
	{
		return GetWeaponDisplayName(GetCurrentWeaponSelection());
	}

	private static bool TryGetLocalPlayerWeaponSelectionOverride(int actorNr, out string selection)
	{
		selection = null;
		RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
		if (actorNr <= 0 || localPlayer == null || localPlayer.ActorNumber != actorNr)
		{
			return false;
		}
		selection = GetCurrentWeaponSelection();
		_playerWeaponSelectionsByActor[actorNr] = selection;
		return true;
	}

	private static bool TryGetLocalPlayerAkSoundSelectionOverride(int actorNr, out string selection)
	{
		selection = null;
		RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
		if (actorNr <= 0 || localPlayer == null || localPlayer.ActorNumber != actorNr)
		{
			return false;
		}
		selection = GetCurrentAkSoundSelection();
		_playerAkSoundSelectionsByActor[actorNr] = selection;
		return true;
	}

	internal static string GetWeaponSelectionForActor(int actorNr)
	{
		if (TryGetLocalPlayerWeaponSelectionOverride(actorNr, out var selection))
		{
			return selection;
		}
		if (actorNr > 0 && _playerWeaponSelectionsByActor.TryGetValue(actorNr, out var value))
		{
			return NormalizeWeaponSelection(value);
		}
		if (PhotonNetwork.InRoom)
		{
			RoomPlayer player = PhotonNetwork.PlayerList?.FirstOrDefault((RoomPlayer candidate) => candidate != null && candidate.ActorNumber == actorNr);
			if (player != null)
			{
				return GetWeaponSelectionForPlayer(player);
			}
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetAkSoundSelectionForActor(int actorNr)
	{
		if (TryGetLocalPlayerAkSoundSelectionOverride(actorNr, out var selection))
		{
			return selection;
		}
		if (actorNr > 0 && _playerAkSoundSelectionsByActor.TryGetValue(actorNr, out var value))
		{
			return NormalizeAkSoundSelection(value);
		}
		if (PhotonNetwork.InRoom)
		{
			RoomPlayer player = PhotonNetwork.PlayerList?.FirstOrDefault((RoomPlayer candidate) => candidate != null && candidate.ActorNumber == actorNr);
			if (player != null)
			{
				return GetAkSoundSelectionForPlayer(player);
			}
		}
		return GetCurrentAkSoundSelection();
	}

	internal static string GetWeaponSelectionForPlayer(RoomPlayer player)
	{
		if (player == null)
		{
			return GetCurrentWeaponSelection();
		}
		if (TryGetLocalPlayerWeaponSelectionOverride(player.ActorNumber, out var selection))
		{
			return selection;
		}
		object obj = (player.CustomProperties != null) ? player.CustomProperties[(object)PlayerWeaponSelectionPropertyKey] : null;
		string text = NormalizeWeaponSelection(obj as string);
		if (player.ActorNumber > 0)
		{
			_playerWeaponSelectionsByActor[player.ActorNumber] = text;
		}
		return text;
	}

	internal static string GetAkSoundSelectionForPlayer(RoomPlayer player)
	{
		if (player == null)
		{
			return GetCurrentAkSoundSelection();
		}
		if (TryGetLocalPlayerAkSoundSelectionOverride(player.ActorNumber, out var selection))
		{
			return selection;
		}
		object obj = (player.CustomProperties != null) ? player.CustomProperties[(object)PlayerAkSoundSelectionPropertyKey] : null;
		string text = NormalizeAkSoundSelection(obj as string);
		if (player.ActorNumber > 0)
		{
			_playerAkSoundSelectionsByActor[player.ActorNumber] = text;
		}
		return text;
	}

	internal static string GetWeaponSelectionForCharacter(Character character)
	{
		if ((Object)character == (Object)null)
		{
			return GetCurrentWeaponSelection();
		}
		PhotonView val = character.refs?.view;
		if ((Object)val == (Object)null)
		{
			val = ((Component)character).GetComponent<PhotonView>() ?? ((Component)character).GetComponentInParent<PhotonView>();
		}
		if ((Object)val != (Object)null && val.OwnerActorNr > 0)
		{
			return GetWeaponSelectionForActor(val.OwnerActorNr);
		}
		if ((Object)character == (Object)Character.localCharacter)
		{
			return GetCurrentWeaponSelection();
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetWeaponSelectionForItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return GetCurrentWeaponSelection();
		}
		Character val = (((Object)item.trueHolderCharacter != (Object)null) ? item.trueHolderCharacter : item.holderCharacter);
		if ((Object)val != (Object)null)
		{
			return GetWeaponSelectionForCharacter(val);
		}
		PhotonView val2 = ((Component)item).GetComponent<PhotonView>() ?? ((Component)item).GetComponentInParent<PhotonView>();
		if ((Object)val2 != (Object)null)
		{
			int num = (val2.OwnerActorNr > 0) ? val2.OwnerActorNr : val2.CreatorActorNr;
			if (num > 0)
			{
				return GetWeaponSelectionForActor(num);
			}
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetItemWeaponDisplayName(Item item)
	{
		return GetWeaponDisplayName(GetWeaponSelectionForItem(item));
	}

	private static void ApplySelectedWeaponAssets()
	{
		string currentWeaponSelection = GetCurrentWeaponSelection();
		GameObject val = null;
		Texture2D val2 = null;
		bool flag = false;
		if (!TryGetResolvedWeaponPrefab(currentWeaponSelection, out val))
		{
			TryGetResolvedWeaponPrefab(DefaultWeaponSelection, out val);
		}
		if (!HasRenderableGeometry(val))
		{
			val = (HasRenderableGeometry(_genericResolvedWeaponPrefab) ? _genericResolvedWeaponPrefab : null);
		}
		if (TryGetResolvedWeaponIcon(currentWeaponSelection, out val2) || TryGetResolvedWeaponIcon(DefaultWeaponSelection, out val2))
		{
			flag = (Object)val2 != (Object)null;
		}
		if ((Object)val2 == (Object)null)
		{
			val2 = _genericResolvedWeaponIcon;
		}
		_ak47Prefab = val;
		_ak47IconTexture = val2;
		_useExplicitSelectedWeaponIcon = flag;
	}

	private static bool TryGetResolvedWeaponPrefab(string selection, out GameObject prefab)
	{
		prefab = null;
		string key = NormalizeWeaponSelection(selection);
		if (_resolvedWeaponPrefabs.TryGetValue(key, out prefab) && HasRenderableGeometry(prefab))
		{
			return true;
		}
		prefab = null;
		return false;
	}

	private static bool TryGetResolvedWeaponIcon(string selection, out Texture2D icon)
	{
		icon = null;
		string key = NormalizeWeaponSelection(selection);
		if (_resolvedWeaponIcons.TryGetValue(key, out icon) && (Object)icon != (Object)null)
		{
			return true;
		}
		icon = null;
		return false;
	}

	private static void CaptureGenericResolvedWeaponAssets()
	{
		_genericResolvedWeaponPrefab = (HasRenderableGeometry(_ak47Prefab) ? _ak47Prefab : null);
		_genericResolvedWeaponIcon = _ak47IconTexture;
	}

	private static void TryResolveSelectableWeaponAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return;
		}
		GameObject[] source = bundle.LoadAllAssets<GameObject>() ?? Array.Empty<GameObject>();
		Texture2D[] textures = bundle.LoadAllAssets<Texture2D>() ?? Array.Empty<Texture2D>();
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition == null || string.IsNullOrWhiteSpace(weaponVariantDefinition.SelectionKey))
			{
				continue;
			}
			GameObject matchingWeaponPrefabCandidate = FindMatchingWeaponPrefabCandidate(source, weaponVariantDefinition);
			if ((Object)matchingWeaponPrefabCandidate != (Object)null)
			{
				GameObject runtimeWeaponPrefabClone = CreateRuntimeWeaponPrefabClone(matchingWeaponPrefabCandidate, weaponVariantDefinition.SelectionKey);
				if ((Object)runtimeWeaponPrefabClone != (Object)null)
				{
					_resolvedWeaponPrefabs[weaponVariantDefinition.SelectionKey] = runtimeWeaponPrefabClone;
				}
			}
			Texture2D bestMatchingWeaponIcon = FindBestMatchingWeaponIcon(bundle, textures, weaponVariantDefinition);
			if ((Object)bestMatchingWeaponIcon != (Object)null)
			{
				_resolvedWeaponIcons[weaponVariantDefinition.SelectionKey] = bestMatchingWeaponIcon;
			}
		}
	}

	private static GameObject FindMatchingWeaponPrefabCandidate(IEnumerable<GameObject> prefabs, WeaponVariantDefinition definition)
	{
		if (prefabs == null || definition == null)
		{
			return null;
		}
		foreach (GameObject prefab in prefabs)
		{
			GameObject matchingWeaponChildPrefab = FindMatchingWeaponChildPrefab(prefab, definition.PrefabAliases, directChildrenOnly: true);
			if ((Object)matchingWeaponChildPrefab != (Object)null)
			{
				return matchingWeaponChildPrefab;
			}
		}
		foreach (GameObject prefab2 in prefabs)
		{
			GameObject matchingWeaponChildPrefab2 = FindMatchingWeaponChildPrefab(prefab2, definition.PrefabAliases, directChildrenOnly: false);
			if ((Object)matchingWeaponChildPrefab2 != (Object)null)
			{
				return matchingWeaponChildPrefab2;
			}
		}
		foreach (GameObject prefab3 in prefabs)
		{
			if ((Object)prefab3 != (Object)null && HasRenderableGeometry(prefab3) && MatchesWeaponAliasToken(((Object)prefab3).name, definition.PrefabAliases))
			{
				return prefab3;
			}
		}
		return null;
	}

	private static GameObject FindMatchingWeaponChildPrefab(GameObject root, IEnumerable<string> aliases, bool directChildrenOnly)
	{
		if ((Object)root == (Object)null)
		{
			return null;
		}
		GameObject result = null;
		int bestScore = int.MinValue;
		if (directChildrenOnly)
		{
			for (int i = 0; i < root.transform.childCount; i++)
			{
				Transform child = root.transform.GetChild(i);
				if ((Object)child != (Object)null)
				{
					EvaluateWeaponPrefabCandidate(child.gameObject, aliases, ref result, ref bestScore);
				}
			}
			return result;
		}
		Transform[] componentsInChildren = root.GetComponentsInChildren<Transform>(true);
		foreach (Transform val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && !((Object)val == (Object)root.transform))
			{
				EvaluateWeaponPrefabCandidate(val.gameObject, aliases, ref result, ref bestScore);
			}
		}
		return result;
	}

	private static void EvaluateWeaponPrefabCandidate(GameObject candidate, IEnumerable<string> aliases, ref GameObject result, ref int bestScore)
	{
		if ((Object)candidate == (Object)null || !HasRenderableGeometry(candidate) || !MatchesWeaponAliasToken(((Object)candidate).name, aliases))
		{
			return;
		}
		int weaponPrefabCandidateScore = GetWeaponPrefabCandidateScore(candidate);
		if (weaponPrefabCandidateScore > bestScore)
		{
			bestScore = weaponPrefabCandidateScore;
			result = candidate;
		}
	}

	private static int GetWeaponPrefabCandidateScore(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return int.MinValue;
		}
		int num = Mathf.Clamp(candidate.transform.childCount * 1200, 0, 12000);
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			num += 1200;
			num += Mathf.Clamp(val.sharedMesh.vertexCount / 2, 0, 24000);
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if (!((Object)val2 == (Object)null) && (Object)val2.sharedMesh != (Object)null)
			{
				num += 1400;
				num += Mathf.Clamp(val2.sharedMesh.vertexCount / 2, 0, 24000);
			}
		}
		return num;
	}

	private static GameObject CreateRuntimeWeaponPrefabClone(GameObject source, string selectionKey)
	{
		if ((Object)source == (Object)null || !HasRenderableGeometry(source))
		{
			return null;
		}
		GameObject val = null;
		Transform transform = source.transform;
		if ((Object)transform.parent != (Object)null)
		{
			Transform root = transform.root;
			if ((Object)root == (Object)null)
			{
				return null;
			}
			if (!TryBuildRelativeChildIndexPath(root, transform, out var siblingIndexPath))
			{
				return null;
			}
			GameObject val2 = Object.Instantiate<GameObject>(((Component)root).gameObject);
			if ((Object)val2 == (Object)null)
			{
				return null;
			}
			Transform val3 = ResolveChildByIndexPath(val2.transform, siblingIndexPath);
			if ((Object)val3 == (Object)null || !HasRenderableGeometry(((Component)val3).gameObject))
			{
				Object.Destroy((Object)(object)val2);
				return null;
			}
			GameObject val4 = new GameObject(selectionKey + "_RuntimeRoot");
			val3.SetParent(val4.transform, worldPositionStays: true);
			Object.Destroy((Object)(object)val2);
			val = val4;
		}
		else
		{
			val = Object.Instantiate<GameObject>(source);
		}
		if ((Object)val == (Object)null)
		{
			return null;
		}
		val.transform.SetParent(null, worldPositionStays: false);
		val.transform.localPosition = Vector3.zero;
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		NormalizeRuntimeWeaponPrefabClone(val);
		val.SetActive(false);
		val.hideFlags = (HideFlags)61;
		((Object)val).name = selectionKey + "_RuntimePrefab";
		Object.DontDestroyOnLoad((Object)val);
		_runtimeWeaponPrefabClones.Add(val);
		return val;
	}

	private static bool TryBuildRelativeChildIndexPath(Transform root, Transform descendant, out List<int> siblingIndexPath)
	{
		siblingIndexPath = null;
		if ((Object)root == (Object)null || (Object)descendant == (Object)null)
		{
			return false;
		}
		List<int> list = new List<int>();
		Transform val = descendant;
		while ((Object)val != (Object)null && (Object)val != (Object)root)
		{
			list.Add(val.GetSiblingIndex());
			val = val.parent;
		}
		if ((Object)val != (Object)root)
		{
			return false;
		}
		list.Reverse();
		siblingIndexPath = list;
		return true;
	}

	private static Transform ResolveChildByIndexPath(Transform root, IReadOnlyList<int> siblingIndexPath)
	{
		if ((Object)root == (Object)null || siblingIndexPath == null)
		{
			return null;
		}
		Transform val = root;
		for (int i = 0; i < siblingIndexPath.Count; i++)
		{
			int num = siblingIndexPath[i];
			if (num < 0 || num >= val.childCount)
			{
				return null;
			}
			val = val.GetChild(num);
			if ((Object)val == (Object)null)
			{
				return null;
			}
		}
		return val;
	}

	private static void NormalizeRuntimeWeaponPrefabClone(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null))
			{
				ItemPatch.NormalizeAkRenderer(val);
			}
		}
	}

	private static Texture2D FindBestMatchingWeaponIcon(AssetBundle bundle, IEnumerable<Texture2D> textures, WeaponVariantDefinition definition)
	{
		if (textures == null || definition == null)
		{
			return null;
		}
		Texture2D result = null;
		int num = int.MinValue;
		foreach (Texture2D texture in textures)
		{
			if ((Object)texture == (Object)null)
			{
				continue;
			}
			string name = ((Object)texture).name;
			string assetNameForObject = FindAssetNameForObject(bundle, (Object)(object)texture);
			int num2 = int.MinValue;
			if (MatchesWeaponAliasToken(name, definition.IconAliases))
			{
				num2 = 100000;
			}
			else if (MatchesWeaponAliasToken(assetNameForObject, definition.IconAliases))
			{
				num2 = 96000;
			}
			else if (NormalizeLookupToken(name).IndexOf("icon", StringComparison.Ordinal) >= 0 && MatchesWeaponAliasToken(name, definition.PrefabAliases))
			{
				num2 = 92000;
			}
			else if (NormalizeLookupToken(assetNameForObject).IndexOf("icon", StringComparison.Ordinal) >= 0 && MatchesWeaponAliasToken(assetNameForObject, definition.PrefabAliases))
			{
				num2 = 90000;
			}
			else if (IsLikelyIconTexture(texture) && MatchesWeaponAliasToken(name, definition.PrefabAliases))
			{
				num2 = 86000;
			}
			else if (IsLikelyIconTexture(texture) && MatchesWeaponAliasToken(assetNameForObject, definition.PrefabAliases))
			{
				num2 = 84000;
			}
			if (num2 == int.MinValue)
			{
				continue;
			}
			if (IsLikelyIconTexture(texture))
			{
				num2 += 2000;
			}
			num2 += Mathf.Clamp(((Texture)texture).width * ((Texture)texture).height / 256, 0, 12000);
			if (num2 > num)
			{
				num = num2;
				result = texture;
			}
		}
		return result;
	}

	private void OnAkSoundSelectionChanged()
	{
		if (AkSoundSelection == null)
		{
			return;
		}
		string text = NormalizeAkSoundSelection(AkSoundSelection.Value);
		if (!string.Equals(AkSoundSelection.Value, text, StringComparison.Ordinal))
		{
			AkSoundSelection.Value = text;
			SavePluginConfigQuietly();
			return;
		}
		SavePluginConfigQuietly();
		ApplySelectedGunshotSound();
		PublishLocalAkSoundSelectionToPlayerProperties(force: true);
	}

	private void SavePluginConfigQuietly()
	{
		try
		{
			((BaseUnityPlugin)this).Config.Save();
		}
		catch
		{
		}
	}

	internal void SaveOwnedConfigRuntime()
	{
		SavePluginConfigQuietly();
	}

	private void ApplySelectedGunshotSound()
	{
		try
		{
			string text = (_currentGunshotSoundSelection = NormalizeAkSoundSelection(AkSoundSelection?.Value));
			if (_gunshotSoundLoadCoroutine != null)
			{
				((MonoBehaviour)this).StopCoroutine(_gunshotSoundLoadCoroutine);
				_gunshotSoundLoadCoroutine = null;
			}
			if (_externalGunshotSounds.TryGetValue(text, out var value) && (Object)value != (Object)null)
			{
				SetGunshotSoundClip(value, text, isExternal: true);
				return;
			}
			string externalAkSoundPath = GetExternalAkSoundPath(text);
			if (string.IsNullOrWhiteSpace(externalAkSoundPath) || !File.Exists(externalAkSoundPath))
			{
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogWarning((object)("[ShootZombies] Missing external AK sound for selection '" + text + "'"));
				}
				string text2 = FindFallbackAkSoundSelection(text);
				if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(text2, text, StringComparison.Ordinal))
				{
					if (AkSoundSelection != null && !string.Equals(AkSoundSelection.Value, text2, StringComparison.Ordinal))
					{
						AkSoundSelection.Value = text2;
					}
					else
					{
						_currentGunshotSoundSelection = text2;
						ApplySelectedGunshotSound();
					}
				}
				else
				{
					_gunshotSound = null;
					if ((Object)_sharedAudioSource != (Object)null)
					{
						_sharedAudioSource.clip = null;
					}
				}
			}
			else
			{
				_gunshotSoundLoadCoroutine = ((MonoBehaviour)this).StartCoroutine(LoadAudioClipFromFile(text, externalAkSoundPath));
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogWarning((object)("[ShootZombies] ApplySelectedGunshotSound failed: " + ex.Message));
			}
		}
	}

	private void SetGunshotSoundClip(AudioClip clip, string selection, bool isExternal)
	{
		if (!((Object)clip == (Object)null))
		{
			_currentGunshotSoundSelection = NormalizeAkSoundSelection(selection);
			_gunshotSound = clip;
			if ((Object)_sharedAudioSource != (Object)null)
			{
				_sharedAudioSource.clip = clip;
			}
			ReplaceBlowgunSoundInternal();
		}
	}

	private static AudioType GetAudioTypeForPath(string filePath)
	{
		return (AudioType)((Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant() switch
		{
			".wav" => 20, 
			".ogg" => 14, 
			".mp3" => 13, 
			_ => 0, 
		});
	}

	private static IEnumerable<string> EnumerateAkSoundDirectories()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (!string.IsNullOrWhiteSpace(directoryName) && hashSet.Add(directoryName))
		{
			yield return Path.Combine(directoryName, "AK_Sounds");
		}
		if (!string.IsNullOrWhiteSpace(Paths.PluginPath) && hashSet.Add(Paths.PluginPath))
		{
			yield return Path.Combine(Paths.PluginPath, "AK_Sounds");
		}
		string currentDirectory = Directory.GetCurrentDirectory();
		if (!string.IsNullOrWhiteSpace(currentDirectory) && hashSet.Add(currentDirectory))
		{
			yield return Path.Combine(currentDirectory, "AK_Sounds");
		}
	}

	private static string GetExternalAkSoundPath(string selection)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(selection);
		string[] array = new string[3] { ".wav", ".mp3", ".ogg" };
		foreach (string item in EnumerateAkSoundDirectories())
		{
			if (string.IsNullOrWhiteSpace(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				string text2 = Path.Combine(item, fileNameWithoutExtension + text);
				if (File.Exists(text2))
				{
					return text2;
				}
			}
		}
		return null;
	}

	private IEnumerator LoadAudioClipFromFile(string selection, string filePath)
	{
		string text = "file://" + filePath;
		UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(text, GetAudioTypeForPath(filePath));
		try
		{
			yield return www.SendWebRequest();
			if ((int)www.result == 1)
			{
				AudioClip content = DownloadHandlerAudioClip.GetContent(www);
				if ((Object)content != (Object)null)
				{
					((Object)content).name = Path.GetFileNameWithoutExtension(filePath);
					_externalGunshotSounds[selection] = content;
					if (string.Equals(NormalizeAkSoundSelection(AkSoundSelection?.Value), NormalizeAkSoundSelection(selection), StringComparison.Ordinal))
					{
						SetGunshotSoundClip(content, selection, isExternal: true);
					}
				}
				yield break;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] Failed to load AK sound '" + selection + "' from '" + filePath + "': " + www.error));
			}
		}
		finally
		{
			_gunshotSoundLoadCoroutine = null;
			((IDisposable)www)?.Dispose();
		}
	}

	private void ReplaceBlowgunSound()
	{
		try
		{
			ApplySelectedGunshotSound();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] ReplaceBlowgunSound error: " + ex));
		}
	}

	private void ReplaceBlowgunSoundInternal()
	{
		try
		{
			if ((Object)_gunshotSound == (Object)null)
			{
				return;
			}
			if (TryResolveBaseWeaponItem(out var item))
			{
				Type type = ((object)item).GetType();
				FieldInfo field = type.GetField("prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
				{
					field = type.GetField("_prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				if (field != null && (Object)_gunshotSound != (Object)null)
				{
					object value = field.GetValue(item);
					GameObject val = (GameObject)((value is GameObject) ? value : null);
					if ((Object)val != (Object)null)
					{
						AudioSource[] componentsInChildren = val.GetComponentsInChildren<AudioSource>(true);
						foreach (AudioSource val2 in componentsInChildren)
						{
							if ((Object)val2.clip != (Object)null)
							{
								val2.clip = _gunshotSound;
							}
						}
					}
				}
			}
			Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			foreach (Item val3 in array)
			{
				string name = val3.GetName();
				if (!ItemPatch.IsBlowgunLike(val3, name))
				{
					continue;
				}
				AudioSource[] componentsInChildren = ((Component)val3).GetComponentsInChildren<AudioSource>(true);
				foreach (AudioSource val4 in componentsInChildren)
				{
					if ((Object)val4.clip != (Object)null && (Object)_gunshotSound != (Object)null)
					{
						val4.clip = _gunshotSound;
					}
				}
				FieldInfo[] fields = ((object)val3).GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (!(fieldInfo.FieldType == typeof(AudioClip)) && !fieldInfo.Name.ToLower().Contains("sound") && !fieldInfo.Name.ToLower().Contains("audio") && !fieldInfo.Name.ToLower().Contains("sfx"))
					{
						continue;
					}
					try
					{
						if (fieldInfo.GetValue(val3) is AudioClip && (Object)_gunshotSound != (Object)null)
						{
							fieldInfo.SetValue(val3, _gunshotSound);
						}
					}
					catch (Exception)
					{
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool TryResolveBaseWeaponItem(out Item item)
	{
		item = null;
		if (TryGetItemById(70, out item))
		{
			return true;
		}
		int num = 0;
		try
		{
			Object[] array = Resources.FindObjectsOfTypeAll(typeof(Object));
			foreach (Object val in array)
			{
				if (val == (Object)null || ((object)val).GetType().Name != "ItemDatabase" || !(((object)val).GetType().GetField("itemLookup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val) is IDictionary dictionary))
				{
					continue;
				}
				foreach (DictionaryEntry item2 in dictionary)
				{
					object value = item2.Value;
					Item val2 = (Item)((value is Item) ? value : null);
					int num2 = ScoreBaseWeaponItem(val2);
					if (num2 > num)
					{
						num = num2;
						item = val2;
					}
				}
				break;
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryResolveBaseWeaponItem database scan failed: " + ex.Message));
		}
		if ((Object)item == (Object)null)
		{
			try
			{
				Item[] array2 = Resources.FindObjectsOfTypeAll<Item>();
				foreach (Item val3 in array2)
				{
					int num3 = ScoreBaseWeaponItem(val3);
					if (num3 > num)
					{
						num = num3;
						item = val3;
					}
				}
			}
			catch (Exception ex2)
			{
				Log.LogWarning((object)("[ShootZombies] TryResolveBaseWeaponItem resource scan failed: " + ex2.Message));
			}
		}
		return (Object)item != (Object)null;
	}

	private static bool TryGetItemById(ushort id, out Item item)
	{
		item = null;
		try
		{
			MethodInfo method = typeof(ItemDatabase).GetMethod("TryGetItem", BindingFlags.Static | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			object[] array = new object[2] { id, null };
			object obj = method.Invoke(null, array);
			if (!(obj is bool) || !(bool)obj)
			{
				return false;
			}
			object obj2 = array[1];
			item = (Item)((obj2 is Item) ? obj2 : null);
			return (Object)item != (Object)null;
		}
		catch
		{
			return false;
		}
	}
}
