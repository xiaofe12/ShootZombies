using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ShootZombies;

[HarmonyPatch]
public static partial class ItemPatch
{
	private struct SourceVisualCandidate
	{
		public Mesh Mesh;

		public Material[] Materials;

		public Transform Transform;

		public int VertexCount;

		public int Score;

		public string Path;

		public string MeshName;
	}

	internal static string DisplayName => Plugin.GetCurrentWeaponDisplayName();

	private const string LocalVisualName = "AK_VisualRoot";

	private const string VisualCloneName = "AK_LocalVisualClone";

	private const string SimpleVisualName = "AK47Model";

	private const string MuzzleMarkerName = "AK_MuzzleMarker";

	private const string WorldColliderName = "AK_WorldCollider";

	private const string HeldLegacyVisualName = "AK47_HeldLegacyModel";

	private const string InPlaceVisualRootName = "AK_InPlaceVisualRoot";

	private const string HeldDebugSphereName = "AK_HeldDebugSphere";

	private static readonly Quaternion HeldLegacyRotationOffset = Quaternion.identity;

	private static readonly Vector3 HeldLegacyPositionOffset = new Vector3(-0.01f, 0.01f, 0f);

	private static readonly Quaternion InPlaceYawOffset = Quaternion.Euler(0f, 0f, 0f);
	private static readonly string[] SelectableWeaponKeywords = new string[4] { "ak47", "mpx", "hk416", "hk417" };

	private const float RuntimeVisualBaseScaleMultiplier = 1.275f;

	private static readonly string[] ItemKeywords = new string[5] { "healingdart", "blowgun", "dart", "吹箭筒", "ak47" };

	private static readonly string[] ExcludedVisualKeywords = new string[9] { "hand", "arm", "finger", "collider", "trigger", "vfx", "effect", "spawn", "holiday" };

	private static readonly Dictionary<int, Renderer[]> HiddenRendererCache = new Dictionary<int, Renderer[]>();

	private static readonly HashSet<int> InPlaceSwappedItemIds = new HashSet<int>();

	private static readonly Dictionary<int, int> PendingSetStateValueByItemId = new Dictionary<int, int>();

	private static readonly Dictionary<int, Character> PendingSetStateHolderByItemId = new Dictionary<int, Character>();

	private const bool UseDebugAnchorSphereVisual = false;

	private static Mesh _debugCubeMesh;

	private static Material[] _debugCubeMaterials;

	private static Mesh _debugSphereMesh;

	private static Material[] _debugSphereMaterials;

	private static Material[] _meshOnlyTestMaterials;

	private static Material[] _cachedDirectAkSharedMaterials;

	private static int _cachedDirectAkSharedMaterialsHash;

	private static string _cachedCombinedWeaponSelection = string.Empty;

	private const bool PreferInPlaceMeshSwap = true;

	private const bool PreferLegacyHeldVisualForLocalPlayer = false;

	private const bool EnableHeldDebugSphereVisual = false;

	private static readonly string[] MainRendererFieldNames = new string[6] { "<mainRenderer>k__BackingField", "mainRenderer", "_mainRenderer", "<MainRenderer>k__BackingField", "MainRenderer", "_MainRenderer" };

	private static readonly string[] MainRendererPropertyNames = new string[2] { "mainRenderer", "MainRenderer" };

	private static readonly string[] AddtlRendererFieldNames = new string[6] { "<addtlRenderers>k__BackingField", "addtlRenderers", "_addtlRenderers", "<AddtlRenderers>k__BackingField", "AddtlRenderers", "_AddtlRenderers" };

	private static Quaternion GetDirectAkRotationOverride()
	{
		return Plugin.GetDirectAkRotationOverride();
	}

	private static float GetDirectAkScaleMultiplier(string selection = null)
	{
		return Plugin.GetEffectiveWeaponModelScale(string.IsNullOrWhiteSpace(selection) ? Plugin.GetCurrentWeaponSelection() : selection);
	}

	private static Vector3 GetDirectAkPositionOverride(string selection = null)
	{
		return Plugin.GetDirectAkPositionOverride(string.IsNullOrWhiteSpace(selection) ? Plugin.GetCurrentWeaponSelection() : selection);
	}

	private static string DescribeItemForDiagnostics(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return "item=null";
		}
		string text = ((((Object)((Component)item).gameObject).name ?? string.Empty) + "/" + (((Object)((Component)item).transform).name ?? string.Empty)).Trim('/');
		string text2 = ((((Object)item.holderCharacter != (Object)null) ? ((Object)item.holderCharacter).name : null) ?? "null");
		string text3 = item.UIData?.itemName ?? "null";
		return $"name={text}, itemID={item.itemID}, state={(int)item.itemState}, holder={text2}, uiName={text3}";
	}

	private static string DescribeRendererCandidatesForDiagnostics(Item item, int maxEntries = 6)
	{
		if ((Object)item == (Object)null)
		{
			return "no-item";
		}
		List<string> list = new List<string>();
		Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			Mesh rendererMesh = GetRendererMesh(val);
			string item2 = $"{GetTransformPath(((Component)val).transform)}|renderer={((Object)val).name}|mesh={(((Object)(object)rendererMesh != (Object)null) ? ((Object)rendererMesh).name : "null")}|verts={(((Object)(object)rendererMesh != (Object)null) ? rendererMesh.vertexCount : 0)}";
			list.Add(item2);
			if (list.Count >= maxEntries)
			{
				break;
			}
		}
		if (list.Count == 0)
		{
			return "no-renderers";
		}
		return string.Join(" || ", list);
	}

	[HarmonyPatch(typeof(Item), "OnDestroy")]
	[HarmonyPrefix]
	public static void ItemOnDestroyPrefix(Item __instance)
	{
		CleanupItem(__instance);
	}

	[HarmonyPatch(typeof(Item), "OnEnable")]
	[HarmonyPostfix]
	public static void ItemOnEnablePostfix(Item __instance)
	{
		TryRefreshItem(__instance, forceRefresh: false);
	}

	[HarmonyPatch(typeof(Item), "Start")]
	[HarmonyPostfix]
	public static void ItemStartPostfix(Item __instance)
	{
		TryRefreshItem(__instance, forceRefresh: false);
	}

	[HarmonyPatch(typeof(Item), "SetState")]
	[HarmonyPrefix]
	public static void ItemSetStatePrefix(Item __instance)
	{
		CachePendingSetStateContext(__instance);
	}

	[HarmonyPatch(typeof(Item), "SetState")]
	[HarmonyPostfix]
	public static void ItemSetStatePostfix(Item __instance)
	{
		NotifyDroppedWeaponIfNeeded(__instance);
		TryRefreshItem(__instance, forceRefresh: false);
		try
		{
			ItemUIDataPatch.ForceRefreshVisibleUi();
		}
		catch
		{
		}
	}

	private static void CachePendingSetStateContext(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		int instanceID = ((Object)item).GetInstanceID();
		PendingSetStateValueByItemId[instanceID] = (int)item.itemState;
		Character itemHolderCharacter = GetItemHolderCharacter(item);
		if ((Object)itemHolderCharacter == (Object)null)
		{
			PendingSetStateHolderByItemId.Remove(instanceID);
		}
		else
		{
			PendingSetStateHolderByItemId[instanceID] = itemHolderCharacter;
		}
	}

	private static void NotifyDroppedWeaponIfNeeded(Item item)
	{
		if ((Object)item == (Object)null || !IsBlowgunLike(item))
		{
			return;
		}
		int instanceID = ((Object)item).GetInstanceID();
		Character value = null;
		PendingSetStateValueByItemId.TryGetValue(instanceID, out var value2);
		PendingSetStateHolderByItemId.TryGetValue(instanceID, out value);
		PendingSetStateValueByItemId.Remove(instanceID);
		PendingSetStateHolderByItemId.Remove(instanceID);
		if (value2 != 1 || (Object)value == (Object)null || value.isBot || value.isZombie)
		{
			return;
		}
		Character itemHolderCharacter = GetItemHolderCharacter(item);
		if ((int)item.itemState == 1)
		{
			return;
		}
		if ((Object)itemHolderCharacter == (Object)null || !((Object)itemHolderCharacter == (Object)value) || IsDroppedWorldItem(item))
		{
			Plugin.NotifyWeaponDropped(value);
		}
	}

	private static Character GetItemHolderCharacter(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		return ((Object)item.trueHolderCharacter != (Object)null) ? item.trueHolderCharacter : item.holderCharacter;
	}

	[HarmonyPatch(typeof(Item), "GetName")]
	[HarmonyPostfix]
	public static void GetNamePostfix(Item __instance, ref string __result)
	{
		if (Plugin.IsWeaponFeatureEnabled() && IsBlowgunLike(__instance, __result))
		{
			__result = Plugin.GetItemWeaponDisplayName(__instance);
		}
	}

	[HarmonyPatch(typeof(Item), "GetItemName")]
	[HarmonyPostfix]
	public static void GetItemNamePostfix(Item __instance, ref string __result)
	{
		if (Plugin.IsWeaponFeatureEnabled() && IsBlowgunLike(__instance, __result))
		{
			__result = Plugin.GetItemWeaponDisplayName(__instance);
		}
	}
}
