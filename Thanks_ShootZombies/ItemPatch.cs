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
public static class ItemPatch
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
		if (IsBlowgunLike(__instance, __result))
		{
			__result = Plugin.GetItemWeaponDisplayName(__instance);
		}
	}

	[HarmonyPatch(typeof(Item), "GetItemName")]
	[HarmonyPostfix]
	public static void GetItemNamePostfix(Item __instance, ref string __result)
	{
		if (IsBlowgunLike(__instance, __result))
		{
			__result = Plugin.GetItemWeaponDisplayName(__instance);
		}
	}

	public static int EnsureAkVisualOnAllItems(bool forceRefresh = false)
	{
		int num = 0;
		Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
		for (int i = 0; i < array.Length; i++)
		{
			if (EnsureAkVisual(array[i], forceRefresh))
			{
				num++;
			}
		}
		return num;
	}

	public static bool EnsureAkMeshSwap(Item item, bool forceRefreshMarker = false)
	{
		return EnsureAkVisual(item, forceRefreshMarker);
	}

	public static bool EnsureAkVisual(Item item, bool forceRefreshMarker = false)
	{
		if (!ShouldProcessRuntimeItem(item))
		{
			return false;
		}
		if (!IsBlowgunLike(item))
		{
			RestoreOriginalVisual(item);
			CleanupSimpleAkVisual(item);
			DestroyLegacyHeldVisual(item, restoreRenderers: true);
			return false;
		}
		ApplyAkDisplay(item);
		Transform val = ResolveTargetVisualRoot(item);
		bool flag = HasReplacementVisualSource(item);
		if ((Object)val == (Object)null || !flag)
		{
			Plugin.LogDiagnosticOnce("ak-ensure-abort:" + ((Object)item).GetInstanceID(), $"EnsureAkVisual aborted: {DescribeItemForDiagnostics(item)}, targetRoot={GetTransformPath(val)}, hasReplacementSource={flag}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics(Plugin.GetWeaponSelectionForItem(item))}");
			RestoreOriginalVisual(item);
			CleanupSimpleAkVisual(item);
			DestroyLegacyHeldVisual(item, restoreRenderers: true);
			return false;
		}
		if (forceRefreshMarker)
		{
			RestoreOriginalVisual(item);
			CleanupSimpleAkVisual(item);
		}
		DestroyLegacyHeldVisual(item, restoreRenderers: false);
		RestoreInPlaceVisual(item);
		InPlaceSwappedItemIds.Remove(((Object)item).GetInstanceID());
		if (ShouldUseSeparateLocalFirstPersonVisual(item))
		{
			AkLocalVisualMarker componentInChildren = ((Component)item).GetComponentInChildren<AkLocalVisualMarker>(true);
			if ((Object)componentInChildren != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)componentInChildren).gameObject);
			}
			CleanupSimpleAkVisual(item);
			HideOriginalRenderers(item, val);
			SyncWeaponColliderState(item, null);
			return true;
		}
		Transform orCreateSimpleAkVisual = GetOrCreateSimpleAkVisual(item, val, forceRefreshMarker);
		if ((Object)orCreateSimpleAkVisual == (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-ensure-create-fail:" + ((Object)item).GetInstanceID(), $"GetOrCreateSimpleAkVisual failed: {DescribeItemForDiagnostics(item)}, targetRoot={GetTransformPath(val)}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics(Plugin.GetWeaponSelectionForItem(item))}");
			RestoreOriginalVisual(item);
			return false;
		}
		if (ShouldShowSimpleAkVisual(item))
		{
			HideOriginalRenderers(item, val);
			SetCustomVisualRenderersVisible(orCreateSimpleAkVisual, visible: true);
			SyncWeaponColliderState(item, null);
		}
		else
		{
			SetCustomVisualRenderersVisible(orCreateSimpleAkVisual, visible: false);
			RestoreOriginalVisual(item);
		}
		InPlaceSwappedItemIds.Remove(((Object)item).GetInstanceID());
		return true;
	}

	public static void ApplyAkDisplay(Item item)
	{
		if (!((Object)item == (Object)null))
		{
			ApplyUiData(item, item.UIData);
			if ((Object)item.isSecretlyOtherItemPrefab != (Object)null)
			{
				ApplyUiData(item, item.isSecretlyOtherItemPrefab.UIData);
			}
		}
	}

	internal static void ApplyAkDisplayIfNeeded(Item item)
	{
		if (!((Object)item == (Object)null) && IsBlowgunLike(item))
		{
			ApplyAkDisplay(item);
		}
	}

	public static bool IsBlowgunLike(Item item, string knownDisplayName = null)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		if ((Object)((Component)item).GetComponentInChildren<AkLocalVisualMarker>(true) != (Object)null)
		{
			return true;
		}
		if ((Object)((Component)item).GetComponentInChildren<AkInPlaceMarker>(true) != (Object)null)
		{
			return true;
		}
		if ((Object)((Component)item).GetComponentInChildren<AkHeldLegacyVisualMarker>(true) != (Object)null)
		{
			return true;
		}
		if (item.itemID == 70)
		{
			return true;
		}
		if ((Object)item.isSecretlyOtherItemPrefab != (Object)null && item.isSecretlyOtherItemPrefab.itemID == 70)
		{
			return true;
		}
		if (!ContainsKeyword(knownDisplayName) && !ContainsKeyword(((Object)((Component)item).gameObject).name) && !ContainsKeyword(item.UIData?.itemName))
		{
			return ContainsKeyword(item.isSecretlyOtherItemPrefab?.UIData?.itemName);
		}
		return true;
	}

	internal static bool ContainsWeaponKeyword(string value)
	{
		return ContainsKeyword(value);
	}

	public static Transform TryGetMuzzleMarker(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		AkMuzzleMarker componentInChildren = ((Component)item).GetComponentInChildren<AkMuzzleMarker>(true);
		if (!((Object)componentInChildren != (Object)null))
		{
			return null;
		}
		return ((Component)componentInChildren).transform;
	}

	internal static Transform ResolveTargetVisualRootForItem(Item item)
	{
		return ResolveTargetVisualRoot(item);
	}

	internal static bool IsLocallyHeldByPlayer(Item item, Character character = null)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		if ((int)item.itemState != 1)
		{
			return false;
		}
		character = (((Object)character != (Object)null) ? character : Character.localCharacter);
		if ((Object)character == (Object)null)
		{
			return false;
		}
		if (!((Object)item.holderCharacter == (Object)character))
		{
			return (Object)item.trueHolderCharacter == (Object)character;
		}
		return true;
	}

	private static bool ShouldUseSeparateLocalFirstPersonVisual(Item item)
	{
		if (!Plugin.IsLocalWeaponVisualFollowerEnabled())
		{
			return false;
		}
		if (IsLocallyHeldByPlayer(item))
		{
			return true;
		}
		Character localCharacter = Character.localCharacter;
		if ((Object)localCharacter == (Object)null || !IsBlowgunLike(item))
		{
			return false;
		}
		if ((Object)item.holderCharacter == (Object)localCharacter || (Object)item.trueHolderCharacter == (Object)localCharacter)
		{
			return (int)item.itemState == 1;
		}
		Item val = localCharacter.data?.currentItem;
		if ((Object)val == (Object)item && (int)item.itemState == 1)
		{
			return true;
		}
		return false;
	}

	internal static bool HasLegacyHeldVisual(Item item)
	{
		if ((Object)item != (Object)null)
		{
			return (Object)((Component)item).GetComponentInChildren<AkHeldLegacyVisualMarker>(true) != (Object)null;
		}
		return false;
	}

	private static bool ShouldProcessRuntimeItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		GameObject gameObject = ((Component)item).gameObject;
		if ((Object)gameObject == (Object)null)
		{
			return false;
		}
		if ((((Object)gameObject).hideFlags & (HideFlags)52) != 0)
		{
			return false;
		}
		Scene scene = gameObject.scene;
		if (scene.IsValid())
		{
			return scene.isLoaded;
		}
		return false;
	}

	private static bool ShouldShowSimpleAkVisual(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		if (ShouldUseSeparateLocalFirstPersonVisual(item))
		{
			return false;
		}
		if ((int)item.itemState == 1)
		{
			return true;
		}
		return (Object)(((Object)item.trueHolderCharacter != (Object)null) ? item.trueHolderCharacter : item.holderCharacter) == (Object)null;
	}

	private static bool ShouldUseInPlaceWeaponVisual(Item item)
	{
		if (!PreferInPlaceMeshSwap || (Object)item == (Object)null)
		{
			return false;
		}
		return !IsDroppedWorldItem(item);
	}

	private static bool IsDroppedWorldItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		if (ShouldUseSeparateLocalFirstPersonVisual(item) || (int)item.itemState == 1)
		{
			return false;
		}
		return (Object)(((Object)item.trueHolderCharacter != (Object)null) ? item.trueHolderCharacter : item.holderCharacter) == (Object)null;
	}

	private static void SetCustomVisualRenderersVisible(Transform root, bool visible)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null))
			{
				val.enabled = visible;
				val.forceRenderingOff = !visible;
				val.allowOcclusionWhenDynamic = false;
			}
		}
	}

	internal static void NormalizeVisualScaleAgainstReference(Transform visualRoot, Transform referenceRoot, string context)
	{
		if ((Object)visualRoot == (Object)null || !TryGetCombinedRenderableBounds(visualRoot, out var bounds))
		{
			return;
		}
		float boundsLongestSize = GetBoundsLongestSize(bounds);
		if (!IsFiniteFloat(boundsLongestSize) || boundsLongestSize < 0.0005f)
		{
			return;
		}
		bool flag = TryGetReferenceRenderableBounds(referenceRoot, visualRoot, out var bounds2);
		float boundsLongestSize2 = flag ? GetBoundsLongestSize(bounds2) : 0f;
		float num = 0f;
		string text = "none";
		if (flag && IsFiniteFloat(boundsLongestSize2) && boundsLongestSize2 > 0.02f && boundsLongestSize < boundsLongestSize2 * 0.22f)
		{
			num = boundsLongestSize2 * 0.92f;
			text = "reference";
		}
		else if (boundsLongestSize < 0.04f)
		{
			num = 0.42f;
			text = "fallback";
		}
		if (!IsFiniteFloat(num) || num <= boundsLongestSize)
		{
			return;
		}
		float num2 = Mathf.Clamp(num / boundsLongestSize, 1f, 256f);
		if (!IsFiniteFloat(num2) || num2 <= 1.05f)
		{
			return;
		}
		Vector3 localScale = visualRoot.localScale;
		Vector3 vector = new Vector3(Mathf.Abs(localScale.x * num2), Mathf.Abs(localScale.y * num2), Mathf.Abs(localScale.z * num2));
		if (!IsValidNormalizedScale(vector))
		{
			return;
		}
		visualRoot.localScale = vector;
		float num3 = num;
		if (TryGetCombinedRenderableBounds(visualRoot, out var bounds3))
		{
			num3 = GetBoundsLongestSize(bounds3);
		}
		Plugin.LogDiagnosticOnce("ak-visual-scale:" + context + ":" + ((Object)visualRoot).GetInstanceID(), $"Normalized weapon visual scale: context={context}, visual={GetTransformPath(visualRoot)}, reference={GetTransformPath(referenceRoot)}, oldSize={boundsLongestSize:0.####}, referenceSize={boundsLongestSize2:0.####}, targetSize={num:0.####}, newSize={num3:0.####}, scaleRatio={num2:0.###}, reason={text}, newLocalScale={visualRoot.localScale}");
	}

	private static bool TryGetReferenceRenderableBounds(Transform referenceRoot, Transform visualRoot, out Bounds bounds)
	{
		bounds = default(Bounds);
		if ((Object)referenceRoot == (Object)null)
		{
			return false;
		}
		if (TryGetOriginalWeaponRenderableBounds(referenceRoot, out bounds))
		{
			return true;
		}
		return TryGetCombinedRenderableBounds(referenceRoot, out bounds, visualRoot);
	}

	private static bool TryGetOriginalWeaponRenderableBounds(Transform targetVisualRoot, out Bounds bounds)
	{
		bounds = default(Bounds);
		if ((Object)targetVisualRoot == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = ((Component)targetVisualRoot).GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if (!IsOriginalWeaponRenderer(val, targetVisualRoot) || !TryGetRendererWorldBounds(val, out var bounds2))
			{
				continue;
			}
			if (!flag)
			{
				bounds = bounds2;
				flag = true;
			}
			else
			{
				bounds.Encapsulate(bounds2);
			}
		}
		return flag;
	}

	private static bool TryGetCombinedRenderableBounds(Transform root, out Bounds bounds, Transform excludedSubtree = null)
	{
		bounds = default(Bounds);
		if ((Object)root == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if ((Object)val == (Object)null || !IsMeshBackedRenderer(val))
			{
				continue;
			}
			Transform transform = ((Component)val).transform;
			if ((Object)excludedSubtree != (Object)null && ((Object)transform == (Object)excludedSubtree || transform.IsChildOf(excludedSubtree)))
			{
				continue;
			}
			if (!TryGetRendererWorldBounds(val, out var bounds2))
			{
				continue;
			}
			if (!flag)
			{
				bounds = bounds2;
				flag = true;
			}
			else
			{
				bounds.Encapsulate(bounds2);
			}
		}
		return flag;
	}

	private static bool TryGetRendererWorldBounds(Renderer renderer, out Bounds bounds)
	{
		bounds = default(Bounds);
		Mesh rendererMesh = GetRendererMesh(renderer);
		if ((Object)(object)rendererMesh == (Object)null)
		{
			return false;
		}
		return TryTransformBounds(rendererMesh.bounds, ((Component)renderer).transform.localToWorldMatrix, out bounds);
	}

	private static bool TryTransformBounds(Bounds sourceBounds, Matrix4x4 matrix, out Bounds bounds)
	{
		bounds = default(Bounds);
		Vector3 center = sourceBounds.center;
		Vector3 extents = sourceBounds.extents;
		Vector3 vector = matrix.MultiplyPoint3x4(center + new Vector3(0f - extents.x, 0f - extents.y, 0f - extents.z));
		if (!IsFiniteVector3(vector))
		{
			return false;
		}
		bounds = new Bounds(vector, Vector3.zero);
		Vector3[] array = new Vector3[7]
		{
			center + new Vector3(0f - extents.x, 0f - extents.y, extents.z),
			center + new Vector3(0f - extents.x, extents.y, 0f - extents.z),
			center + new Vector3(0f - extents.x, extents.y, extents.z),
			center + new Vector3(extents.x, 0f - extents.y, 0f - extents.z),
			center + new Vector3(extents.x, 0f - extents.y, extents.z),
			center + new Vector3(extents.x, extents.y, 0f - extents.z),
			center + new Vector3(extents.x, extents.y, extents.z)
		};
		for (int i = 0; i < array.Length; i++)
		{
			Vector3 vector2 = matrix.MultiplyPoint3x4(array[i]);
			if (!IsFiniteVector3(vector2))
			{
				return false;
			}
			bounds.Encapsulate(vector2);
		}
		return true;
	}

	private static float GetBoundsLongestSize(Bounds bounds)
	{
		Vector3 size = bounds.size;
		return Mathf.Max(size.x, size.y, size.z);
	}

	private static bool IsFiniteVector3(Vector3 value)
	{
		if (IsFiniteFloat(value.x) && IsFiniteFloat(value.y))
		{
			return IsFiniteFloat(value.z);
		}
		return false;
	}

	private static bool IsValidNormalizedScale(Vector3 value)
	{
		if (!IsFiniteVector3(value))
		{
			return false;
		}
		if (value.x < 0.0001f || value.y < 0.0001f || value.z < 0.0001f)
		{
			return false;
		}
		if (value.x > 256f || value.y > 256f || value.z > 256f)
		{
			return false;
		}
		return true;
	}

	internal static bool TryResolveBaseAkVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		mesh = null;
		materials = null;
		localScale = Vector3.one;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		debugInfo = "unresolved";
		if (!TryGetBestSourceVisual(out var best) || (Object)best.Mesh == (Object)null)
		{
			return false;
		}
		mesh = best.Mesh;
		materials = best.Materials;
		if ((Object)best.Transform != (Object)null)
		{
			TryGetTransformPoseRelativeToAkRoot(best.Transform, out var localPosition2, out var localRotation2, out var localScale2);
			localScale = SanitizeLocalScale(localScale2, Vector3.one);
			localRotation = SanitizeLocalRotation(localRotation2);
			localPosition = SanitizeLocalPosition(localPosition2, Vector3.zero);
		}
		debugInfo = $"path={best.Path}, mesh={best.MeshName}, verts={best.VertexCount}, score={best.Score}, subMeshes={Mathf.Max(best.Mesh.subMeshCount, 1)}, mats={((best.Materials != null) ? best.Materials.Length : 0)}, pos={localPosition}, rot={localRotation.eulerAngles}, scale={localScale}";
		return true;
	}

	private static bool HasReplacementVisualSource(Item item)
	{
		return Plugin.HasAkVisualPrefab(Plugin.GetWeaponSelectionForItem(item));
	}

	private static bool TryResolveBundleMeshOnlyVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		mesh = null;
		materials = null;
		localScale = Vector3.one;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		debugInfo = "bundle-mesh-only-unresolved";
		GameObject akVisualPrefab = Plugin.GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			return false;
		}
		Transform val = akVisualPrefab.transform.Find("AK/Mesh") ?? akVisualPrefab.transform.Find("Mesh");
		if ((Object)val == (Object)null && TryGetBestSourceVisual(out var best))
		{
			val = best.Transform;
			mesh = best.Mesh;
			debugInfo = "bundle-mesh-only fallback path=" + best.Path;
		}
		if ((Object)val == (Object)null)
		{
			return false;
		}
		if ((Object)mesh == (Object)null)
		{
			MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
			mesh = (((Object)component != (Object)null) ? component.sharedMesh : null);
		}
		if ((Object)mesh == (Object)null)
		{
			return false;
		}
		MeshRenderer component2 = ((Component)val).GetComponent<MeshRenderer>();
		TryGetTransformPoseRelativeToAkRoot(val, out localPosition, out localRotation, out localScale);
		localScale = SanitizeLocalScale(localScale, Vector3.one);
		localRotation = SanitizeLocalRotation(localRotation);
		localPosition = SanitizeLocalPosition(localPosition, Vector3.zero);
		Material[] array = ((component2 != null) ? ((Renderer)component2).sharedMaterials : null);
		bool flag = array != null && array.Length != 0 && array.Any((Material material) => (Object)material != (Object)null);
		materials = GetMeshOnlyTestMaterials(Mathf.Max(mesh.subMeshCount, 1));
		debugInfo = $"bundle-mesh-only path={GetTransformPath(val)}, mesh={((Object)mesh).name}, verts={mesh.vertexCount}, subMeshes={Mathf.Max(mesh.subMeshCount, 1)}, sourceMats={(flag ? array.Length : 0)}, runtimeMats=test/{materials.Length}, pos={localPosition}, rot={localRotation.eulerAngles}, scale={localScale}";
		return true;
	}

	private static Material[] GetMeshOnlyTestMaterials(int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		if (_meshOnlyTestMaterials == null || _meshOnlyTestMaterials.Length != subMeshCount || _meshOnlyTestMaterials.Any((Material material) => (Object)material == (Object)null))
		{
			Material[] array = (Material[])(object)new Material[subMeshCount];
			for (int num = 0; num < subMeshCount; num++)
			{
				Material val = CreateRuntimeMaterial(null, null);
				ApplyColor(val, new Color(0.82f, 0.82f, 0.82f, 1f));
				NormalizeMaterialForVisibility(val);
				array[num] = val;
			}
			_meshOnlyTestMaterials = array;
		}
		return _meshOnlyTestMaterials;
	}

	private static bool TryResolveDebugCubeVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		mesh = GetDebugCubeMesh();
		materials = GetDebugCubeMaterials();
		localScale = Vector3.one * 0.25f;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		debugInfo = "debug-cube";
		if ((Object)mesh != (Object)null && materials != null)
		{
			return materials.Length != 0;
		}
		return false;
	}

	private static Mesh GetDebugCubeMesh()
	{
		if ((Object)_debugCubeMesh != (Object)null)
		{
			return _debugCubeMesh;
		}
		GameObject val = GameObject.CreatePrimitive((PrimitiveType)3);
		try
		{
			MeshFilter component = val.GetComponent<MeshFilter>();
			if ((Object)component != (Object)null)
			{
				_debugCubeMesh = component.sharedMesh;
			}
		}
		finally
		{
			Object.Destroy((Object)(object)val);
		}
		return _debugCubeMesh;
	}

	private static Material[] GetDebugCubeMaterials()
	{
		if (_debugCubeMaterials != null && _debugCubeMaterials.Length != 0 && _debugCubeMaterials.All((Material material) => (Object)material != (Object)null))
		{
			return _debugCubeMaterials;
		}
		Material val = CreateRuntimeMaterial(null, null);
		ApplyColor(val, new Color(1f, 0.25f, 0.05f, 1f));
		if (val.HasProperty("_EmissionColor"))
		{
			val.EnableKeyword("_EMISSION");
			val.SetColor("_EmissionColor", new Color(0.35f, 0.08f, 0.02f, 1f));
		}
		NormalizeMaterialForVisibility(val);
		_debugCubeMaterials = (Material[])(object)new Material[1] { val };
		return _debugCubeMaterials;
	}

	private static bool TryResolveDebugSphereVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		mesh = GetDebugSphereMesh();
		materials = GetDebugSphereMaterials();
		localScale = Vector3.one * 0.08f;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		debugInfo = "debug-anchor-sphere";
		if ((Object)mesh != (Object)null && materials != null)
		{
			return materials.Length != 0;
		}
		return false;
	}

	private static Mesh GetDebugSphereMesh()
	{
		if ((Object)_debugSphereMesh != (Object)null)
		{
			return _debugSphereMesh;
		}
		GameObject val = GameObject.CreatePrimitive((PrimitiveType)0);
		try
		{
			MeshFilter component = val.GetComponent<MeshFilter>();
			if ((Object)component != (Object)null)
			{
				_debugSphereMesh = component.sharedMesh;
			}
		}
		finally
		{
			Object.Destroy((Object)(object)val);
		}
		return _debugSphereMesh;
	}

	private static Material[] GetDebugSphereMaterials()
	{
		if (_debugSphereMaterials != null && _debugSphereMaterials.Length != 0 && _debugSphereMaterials.All((Material material) => (Object)material != (Object)null))
		{
			return _debugSphereMaterials;
		}
		Material val = CreateRuntimeMaterial(null, null);
		ApplyColor(val, new Color(1f, 0.08f, 0.08f, 1f));
		if (val.HasProperty("_EmissionColor"))
		{
			val.EnableKeyword("_EMISSION");
			val.SetColor("_EmissionColor", new Color(0.55f, 0.02f, 0.02f, 1f));
		}
		NormalizeMaterialForVisibility(val);
		_debugSphereMaterials = (Material[])(object)new Material[1] { val };
		return _debugSphereMaterials;
	}

	private static void EnsureHeldDebugSphere(Transform visualRoot)
	{
		if (!EnableHeldDebugSphereVisual || (Object)visualRoot == (Object)null)
		{
			return;
		}
		Mesh debugSphereMesh = GetDebugSphereMesh();
		Material[] debugSphereMaterials = GetDebugSphereMaterials();
		if ((Object)debugSphereMesh == (Object)null || debugSphereMaterials == null || debugSphereMaterials.Length == 0)
		{
			return;
		}
		Transform val = visualRoot.Find(HeldDebugSphereName);
		if ((Object)val == (Object)null)
		{
			GameObject val2 = new GameObject(HeldDebugSphereName);
			val2.transform.SetParent(visualRoot, false);
			val = val2.transform;
		}
		MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
		if ((Object)component == (Object)null)
		{
			component = ((Component)val).gameObject.AddComponent<MeshFilter>();
		}
		MeshRenderer val3 = ((Component)val).GetComponent<MeshRenderer>();
		if ((Object)val3 == (Object)null)
		{
			val3 = ((Component)val).gameObject.AddComponent<MeshRenderer>();
		}
		component.sharedMesh = debugSphereMesh;
		((Renderer)val3).sharedMaterials = debugSphereMaterials;
		((Renderer)val3).enabled = true;
		((Renderer)val3).forceRenderingOff = false;
		((Renderer)val3).allowOcclusionWhenDynamic = false;
		ConfigureRuntimeWeaponRenderer((Renderer)(object)val3);
		val.localPosition = ResolveHeldDebugSphereLocalPosition(visualRoot);
		val.localRotation = Quaternion.identity;
		val.localScale = ResolveHeldDebugSphereLocalScale(visualRoot, 0.1f);
		SetLayerRecursively(val, ((Component)visualRoot).gameObject.layer);
		Plugin.LogDiagnosticOnce("ak-held-debug-sphere:" + GetTransformPath(visualRoot), $"Ensured held debug sphere: root={GetTransformPath(visualRoot)}, sphere={GetTransformPath(val)}, localPos={val.localPosition}, localScale={val.localScale}, worldPos={val.position}, rootBounds={DescribeBoundsForDiagnostics(visualRoot)}");
	}

	private static void CleanupHeldDebugSphere(Transform visualRoot)
	{
		if ((Object)visualRoot == (Object)null)
		{
			return;
		}
		Transform val = visualRoot.Find(HeldDebugSphereName);
		if ((Object)val != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)val).gameObject);
		}
	}

	private static Vector3 ResolveHeldDebugSphereLocalPosition(Transform visualRoot)
	{
		if ((Object)visualRoot == (Object)null || !TryGetCombinedRenderableBounds(visualRoot, out var bounds))
		{
			return Vector3.zero;
		}
		return SanitizeLocalPosition(visualRoot.InverseTransformPoint(bounds.center), Vector3.zero);
	}

	private static Vector3 ResolveHeldDebugSphereLocalScale(Transform visualRoot, float desiredWorldDiameter)
	{
		if ((Object)visualRoot == (Object)null)
		{
			return Vector3.one * desiredWorldDiameter;
		}
		Vector3 lossyScale = visualRoot.lossyScale;
		Vector3 value = new Vector3(desiredWorldDiameter / Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f), desiredWorldDiameter / Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f), desiredWorldDiameter / Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f));
		return SanitizeLocalScale(value, Vector3.one * desiredWorldDiameter);
	}

	internal static bool TryResolvePreferredAkVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		Vector3 directAkPositionOverride = GetDirectAkPositionOverride();
		if (!TryResolveBaseAkVisual(out mesh, out materials, out var localScale2, out var localRotation2, out var localPosition2, out debugInfo))
		{
			localScale = Vector3.one * 1.6f;
			localRotation = HeldLegacyRotationOffset;
			localPosition = directAkPositionOverride;
			return false;
		}
		localScale = SanitizeLocalScale(Vector3.Scale(localScale2, Vector3.one * 1.6f), Vector3.one * 1.6f);
		localRotation = SanitizeLocalRotation(localRotation2 * HeldLegacyRotationOffset);
		localPosition = SanitizeLocalPosition(localPosition2 + directAkPositionOverride, directAkPositionOverride);
		debugInfo += $", heldPos={localPosition}, heldRot={localRotation.eulerAngles}, heldScale={localScale}";
		return true;
	}

	private static bool TryResolveDirectAkVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		Vector3 directAkPositionOverride = GetDirectAkPositionOverride();
		mesh = null;
		materials = null;
		localScale = Vector3.one;
		localRotation = HeldLegacyRotationOffset;
		localPosition = directAkPositionOverride;
		debugInfo = "direct-ak-unresolved";
		GameObject akVisualPrefab = Plugin.GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-direct-prefab-null", "TryResolveDirectAkVisual aborted because AK visual prefab is null");
			return false;
		}
		Transform val = akVisualPrefab.transform.Find("AK/Mesh") ?? akVisualPrefab.transform.Find("Mesh");
		bool flag = (Object)val != (Object)null && ShouldRejectDirectAkSource(val);
		MeshFilter val2 = (((Object)val != (Object)null) ? ((Component)val).GetComponent<MeshFilter>() : null);
		MeshRenderer val3 = (((Object)val != (Object)null) ? ((Component)val).GetComponent<MeshRenderer>() : null);
		if ((Object)val2 == (Object)null || (Object)val3 == (Object)null || (Object)val2.sharedMesh == (Object)null || ShouldRejectDirectAkSource(val))
		{
			val2 = null;
			val3 = null;
			MeshFilter[] componentsInChildren = akVisualPrefab.GetComponentsInChildren<MeshFilter>(true);
			int num = int.MinValue;
			MeshFilter[] array = componentsInChildren;
			foreach (MeshFilter val4 in array)
			{
				if ((Object)val4 == (Object)null || (Object)val4.sharedMesh == (Object)null)
				{
					continue;
				}
				MeshRenderer component = ((Component)val4).GetComponent<MeshRenderer>();
				if (!((Object)component == (Object)null) && !ShouldRejectDirectAkSource(((Component)val4).transform))
				{
					int num2 = val4.sharedMesh.vertexCount;
					string text = GetTransformPath(((Component)val4).transform).ToLowerInvariant();
					if (text.Contains("/ak/"))
					{
						num2 += 4000;
					}
					if (text.EndsWith("/mesh"))
					{
						num2 += 6000;
					}
					if (text.Contains("cube"))
					{
						num2 += 2500;
					}
					if (num2 > num)
					{
						num = num2;
						val2 = val4;
						val3 = component;
					}
				}
			}
		}
		if ((Object)val2 == (Object)null || (Object)val3 == (Object)null || (Object)val2.sharedMesh == (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-direct-fail:" + ((Object)akVisualPrefab).GetInstanceID(), $"TryResolveDirectAkVisual failed: preferredNode={GetTransformPath(val)}, preferredRejected={flag}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics()}");
			return false;
		}
		mesh = val2.sharedMesh;
		materials = NormalizeMaterialArray(((Renderer)val3).sharedMaterials, Mathf.Max(mesh.subMeshCount, 1));
		localScale = Vector3.one * GetDirectAkScaleMultiplier();
		localRotation = GetDirectAkRotationOverride();
		localPosition = directAkPositionOverride;
		object[] obj = new object[7]
		{
			GetTransformPath(((Component)val2).transform),
			((Object)mesh).name,
			mesh.vertexCount,
			null,
			null,
			null,
			null
		};
		Material[] obj2 = materials;
		obj[3] = ((obj2 != null) ? obj2.Length : 0);
		obj[4] = localPosition;
		obj[5] = localRotation.eulerAngles;
		obj[6] = localScale;
		debugInfo = string.Format("direct path={0}, mesh={1}, verts={2}, mats={3}, pos={4}, rot={5}, scale={6}", obj);
		Plugin.LogDiagnosticOnce("ak-direct-success:" + GetTransformPath(((Component)val2).transform), $"TryResolveDirectAkVisual selected source={GetTransformPath(((Component)val2).transform)}, mesh={((Object)mesh).name}, verts={mesh.vertexCount}, mats={((materials != null) ? materials.Length : 0)}, heldPos={localPosition}, heldRot={localRotation.eulerAngles}, heldScale={localScale}");
		return true;
	}

	private static bool TryResolveSimpleVisualSource(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		if (TryResolveBaseAkVisual(out mesh, out materials, out var localScale2, out var localRotation2, out var localPosition2, out var debugInfo2) && (Object)mesh != (Object)null)
		{
			localScale = SanitizeLocalScale(localScale2, Vector3.one);
			localRotation = SanitizeLocalRotation(localRotation2);
			localPosition = SanitizeLocalPosition(localPosition2, Vector3.zero);
			debugInfo = "simple-base->" + debugInfo2;
			return true;
		}
		if (TryResolveDirectAkVisual(out mesh, out materials, out var localScale3, out var _, out var _, out var debugInfo3) && (Object)mesh != (Object)null)
		{
			localScale = SanitizeLocalScale(localScale3, Vector3.one);
			localRotation = Quaternion.identity;
			localPosition = Vector3.zero;
			debugInfo = "simple-direct-fallback->" + debugInfo3 + ", forcedPos=(0.00, 0.00, 0.00), forcedRot=(0.00, 0.00, 0.00)";
			return true;
		}
		localScale = Vector3.one;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		debugInfo = "simple-unresolved";
		return false;
	}

	private static bool ShouldUseHeldSimpleVisualPose(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		if ((int)item.itemState == 1)
		{
			return true;
		}
		if ((Object)item.holderCharacter == (Object)null)
		{
			return (Object)item.trueHolderCharacter != (Object)null;
		}
		return true;
	}

	private static bool TryResolveRuntimeSimpleVisualSource(Item item, out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		if (ShouldUseHeldSimpleVisualPose(item) && TryResolvePreferredAkVisual(out mesh, out materials, out localScale, out localRotation, out localPosition, out var debugInfo2) && (Object)mesh != (Object)null)
		{
			debugInfo = "simple-held->" + debugInfo2;
			return true;
		}
		return TryResolveSimpleVisualSource(out mesh, out materials, out localScale, out localRotation, out localPosition, out debugInfo);
	}

	private static bool ShouldRejectDirectAkSource(Transform transform)
	{
		if ((Object)transform == (Object)null)
		{
			return true;
		}
		string text = GetTransformPath(transform).ToLowerInvariant();
		if (!text.Contains("/hand") && !text.Contains("/arm") && !text.Contains("finger") && !text.Contains("holiday") && !text.Contains("vfx") && !text.Contains("effect") && !text.Contains("spawn"))
		{
			return text.Contains("muzzle");
		}
		return true;
	}

	private static Material[] BuildDirectAkMaterials(Material[] sourceMaterials, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		Material[] array = NormalizeMaterialArray(sourceMaterials, subMeshCount);
		Material[] array2 = (Material[])(object)new Material[subMeshCount];
		Material val = array.FirstOrDefault((Material material) => (Object)material != (Object)null);
		for (int i = 0; i < subMeshCount; i++)
		{
			Material source = ((i < array.Length) ? array[i] : null);
			if ((Object)source == (Object)null)
			{
				source = val;
			}
			array2[i] = CreateDirectVisibleMaterial(source);
			NormalizeMaterialForVisibility(array2[i]);
		}
		return array2;
	}

	private static Material CreateDirectVisibleMaterial(Material source)
	{
		Shader val = ResolvePreferredVisibleShader();
		Material val2 = new Material(((Object)val != (Object)null) ? val : ResolveFallbackShader());
		if ((Object)source != (Object)null)
		{
			CopySourceMaterialProperties(source, val2, preserveDestinationColor: false);
			NormalizeMaterial(val2, source);
		}
		else
		{
			NormalizeMaterial(val2, null);
		}
		return val2;
	}

	internal static bool TryResolveHeldPoseRelativeToBase(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
	{
		Vector3 directAkPositionOverride = GetDirectAkPositionOverride();
		localPosition = Vector3.zero;
		localRotation = Quaternion.identity;
		localScale = Vector3.one;
		if (!TryResolveBaseAkVisual(out var mesh, out var materials, out var localScale2, out var localRotation2, out var localPosition2, out var debugInfo))
		{
			return false;
		}
		if (!TryResolvePreferredAkVisual(out mesh, out materials, out var localScale3, out var localRotation3, out var localPosition3, out debugInfo))
		{
			return false;
		}
		Matrix4x4 val = Matrix4x4.TRS(localPosition2, localRotation2, localScale2);
		DecomposeMatrix(val.inverse * Matrix4x4.TRS(localPosition3, localRotation3, localScale3), out localPosition, out localRotation, out localScale);
		localPosition = SanitizeLocalPosition(localPosition, directAkPositionOverride);
		localRotation = SanitizeLocalRotation(localRotation);
		localScale = SanitizeLocalScale(localScale, Vector3.one * 1.6f);
		return true;
	}

	internal static Material[] BuildAkVisibleMaterials(Material[] preferredMaterials, int subMeshCount)
	{
		return BuildVisibleMaterials(preferredMaterials, null, subMeshCount);
	}

	private static int ComputeMaterialArrayHash(Material[] materials, int subMeshCount)
	{
		int num = subMeshCount;
		Material[] array = NormalizeMaterialArray(materials, subMeshCount);
		for (int i = 0; i < array.Length; i++)
		{
			num = num * 31 + (((Object)array[i] != (Object)null) ? ((Object)array[i]).GetInstanceID() : 0);
		}
		return num;
	}

	private static Material[] GetSharedDirectAkMaterials(Material[] sourceMaterials, int subMeshCount)
	{
		int num = ComputeMaterialArrayHash(sourceMaterials, subMeshCount);
		if (_cachedDirectAkSharedMaterials == null || _cachedDirectAkSharedMaterials.Length != subMeshCount || _cachedDirectAkSharedMaterialsHash != num || _cachedDirectAkSharedMaterials.Any((Material material) => (Object)material == (Object)null))
		{
			_cachedDirectAkSharedMaterials = BuildDirectAkMaterials(sourceMaterials, subMeshCount);
			_cachedDirectAkSharedMaterialsHash = num;
		}
		return (_cachedDirectAkSharedMaterials != null) ? _cachedDirectAkSharedMaterials.ToArray() : Array.Empty<Material>();
	}

	internal static void NormalizeAkRenderer(Renderer renderer)
	{
		NormalizeRendererMaterials(renderer);
	}

	private static void ConfigureRuntimeWeaponRenderer(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return;
		}
		renderer.allowOcclusionWhenDynamic = false;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		renderer.lightProbeUsage = LightProbeUsage.Off;
		renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
	}

	internal static Transform CreatePreparedAkClone(Item item, Transform parent, Transform targetVisualRoot, string cloneName)
	{
		if ((Object)parent == (Object)null)
		{
			return null;
		}
		GameObject akVisualPrefab = (((Object)item != (Object)null) ? Plugin.GetAkVisualPrefab(Plugin.GetWeaponSelectionForItem(item)) : Plugin.GetAkVisualPrefab());
		if (!((Object)akVisualPrefab == (Object)null))
		{
			GameObject val = Object.Instantiate<GameObject>(akVisualPrefab, parent, false);
			((Object)val).name = cloneName;
			StripNonVisualComponents(val);
			PrepareRenderers(val, targetVisualRoot);
			ApplyConfiguredPrefabRootPose(val.transform);
			NormalizeVisualScaleAgainstReference(val.transform, targetVisualRoot, cloneName);
			ApplyConfiguredWorldVisualScale(val.transform, ((Object)item != (Object)null) ? Plugin.GetWeaponSelectionForItem(item) : null);
			SetLayerRecursively(val.transform, ((Component)parent).gameObject.layer);
			SetHierarchyActive(val.transform, active: true);
			return val.transform;
		}
		if (TryResolvePreferredAkVisual(out var mesh, out var materials, out var localScale, out var localRotation, out var localPosition, out var _) && (Object)mesh != (Object)null)
		{
			GameObject val3 = new GameObject(cloneName);
			val3.transform.SetParent(parent, false);
			val3.transform.localPosition = localPosition;
			val3.transform.localRotation = localRotation;
			val3.transform.localScale = localScale;
			MeshFilter val4 = val3.AddComponent<MeshFilter>();
			MeshRenderer obj = val3.AddComponent<MeshRenderer>();
			val4.sharedMesh = mesh;
			((Renderer)obj).sharedMaterials = GetSharedDirectAkMaterials(materials, Mathf.Max(mesh.subMeshCount, 1));
			NormalizeAkRenderer((Renderer)(object)obj);
			NormalizeVisualScaleAgainstReference(val3.transform, targetVisualRoot, cloneName + "-mesh");
			ApplyConfiguredWorldVisualScale(val3.transform, ((Object)item != (Object)null) ? Plugin.GetWeaponSelectionForItem(item) : null);
			SetLayerRecursively(val3.transform, ((Component)parent).gameObject.layer);
			return val3.transform;
		}
		return null;
	}

	internal static Transform CreatePreparedAkClone(Transform parent, Transform targetVisualRoot, string cloneName)
	{
		return CreatePreparedAkClone(null, parent, targetVisualRoot, cloneName);
	}

	private static void ApplyConfiguredPrefabRootPose(Transform cloneRoot)
	{
		if (!((Object)cloneRoot == (Object)null))
		{
			cloneRoot.localPosition = GetDirectAkPositionOverride();
			cloneRoot.localRotation = GetDirectAkRotationOverride();
			cloneRoot.localScale = Vector3.one;
		}
	}

	private static void ApplyConfiguredWorldVisualScale(Transform cloneRoot, string selection = null)
	{
		if (!((Object)cloneRoot == (Object)null))
		{
			float num = Mathf.Max(GetDirectAkScaleMultiplier(selection) * RuntimeVisualBaseScaleMultiplier, 0.001f);
			cloneRoot.localScale = Vector3.Scale(cloneRoot.localScale, Vector3.one * num);
		}
	}

	private static void SetHierarchyActive(Transform root, bool active)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		((Component)root).gameObject.SetActive(active);
		Transform[] componentsInChildren = ((Component)root).GetComponentsInChildren<Transform>(true);
		foreach (Transform val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && !((Object)(object)val == (Object)(object)root))
			{
				((Component)val).gameObject.SetActive(active);
			}
		}
	}

	internal static void PrepareAkVisualRenderers(GameObject root, Transform targetVisualRoot)
	{
		PrepareRenderers(root, targetVisualRoot);
	}

	private static void TryRefreshItem(Item item, bool forceRefresh)
	{
		if (!ShouldProcessRuntimeItem(item) || !IsBlowgunLike(item))
		{
			return;
		}
		try
		{
			EnsureAkVisual(item, forceRefresh);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] ItemPatch refresh failed: " + ex));
		}
	}

	private static void ApplyUiData(Item item, ItemUIData uiData)
	{
		if (uiData == null)
		{
			return;
		}
		string weaponSelectionForItem = Plugin.GetWeaponSelectionForItem(item);
		uiData.itemName = Plugin.GetWeaponDisplayName(weaponSelectionForItem);
		Texture2D akIconTexture = Plugin.GetAkIconTexture(weaponSelectionForItem);
		if ((Object)akIconTexture != (Object)null)
		{
			uiData.icon = akIconTexture;
			if (uiData.hasAltIcon)
			{
				uiData.altIcon = akIconTexture;
			}
		}
	}

	private static Transform ResolveTargetVisualRoot(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		if ((int)item.itemState == 1)
		{
			Transform transform = ((Component)item).transform;
			Plugin.LogDiagnosticOnce("ak-target-root:" + ((Object)item).GetInstanceID(), $"ResolveTargetVisualRoot using held item root: {DescribeItemForDiagnostics(item)}, root={GetTransformPath(transform)}");
			return transform;
		}
		if (IsDroppedWorldItem(item))
		{
			Transform transform = ((Component)item).transform;
			Plugin.LogDiagnosticOnce("ak-target-root:" + ((Object)item).GetInstanceID(), $"ResolveTargetVisualRoot using dropped item root: {DescribeItemForDiagnostics(item)}, root={GetTransformPath(transform)}");
			return transform;
		}
		Transform val = ((Component)item).transform.Find("Blowgun");
		if ((Object)val != (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-target-root:" + ((Object)item).GetInstanceID(), $"ResolveTargetVisualRoot via Find(\"Blowgun\"): {DescribeItemForDiagnostics(item)}, root={GetTransformPath(val)}");
			return val;
		}
		Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val2 in componentsInChildren)
		{
			if (IsRenderableWeaponPiece(val2))
			{
				Plugin.LogDiagnosticOnce("ak-target-root:" + ((Object)item).GetInstanceID(), $"ResolveTargetVisualRoot via renderer scan: {DescribeItemForDiagnostics(item)}, root={GetTransformPath(((Component)val2).transform)}, rendererCandidates={DescribeRendererCandidatesForDiagnostics(item)}");
				return ((Component)val2).transform;
			}
		}
		Plugin.LogDiagnosticOnce("ak-target-root:" + ((Object)item).GetInstanceID(), $"ResolveTargetVisualRoot fell back to item root: {DescribeItemForDiagnostics(item)}, root={GetTransformPath(((Component)item).transform)}, rendererCandidates={DescribeRendererCandidatesForDiagnostics(item)}");
		return ((Component)item).transform;
	}

	private static Transform ResolveHeldLegacyParent(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		Transform transform = ((Component)item).transform.Find("Blowgun");
		if ((Object)transform != (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-held-parent:" + ((Object)item).GetInstanceID(), $"ResolveHeldLegacyParent via Find(\"Blowgun\"): {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(transform)}, itemRoot={GetTransformPath(((Component)item).transform)}");
			return transform;
		}
		Transform transform2 = ResolveTargetVisualRoot(item);
		if ((Object)transform2 != (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-held-parent:" + ((Object)item).GetInstanceID(), $"ResolveHeldLegacyParent fallback to target root: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(transform2)}, itemRoot={GetTransformPath(((Component)item).transform)}");
			return transform2;
		}
		Plugin.LogDiagnosticOnce("ak-held-parent:" + ((Object)item).GetInstanceID(), $"ResolveHeldLegacyParent fell back to item root: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(((Component)item).transform)}");
		return ((Component)item).transform;
	}

	private static bool IsRenderableWeaponPiece(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return false;
		}
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + (((Object)((Component)renderer).transform).name ?? string.Empty)).ToLowerInvariant();
		string[] excludedVisualKeywords = ExcludedVisualKeywords;
		foreach (string value in excludedVisualKeywords)
		{
			if (text.Contains(value))
			{
				return false;
			}
		}
		Mesh rendererMesh = GetRendererMesh(renderer);
		if ((Object)(object)rendererMesh != (Object)null)
		{
			return rendererMesh.vertexCount > 24;
		}
		return false;
	}

	private static bool IsMeshBackedRenderer(Renderer renderer)
	{
		return (Object)(object)GetRendererMesh(renderer) != (Object)null;
	}

	private static bool HasAnyPreferredRenderer(Renderer[] renderers)
	{
		if (renderers == null)
		{
			return false;
		}
		for (int i = 0; i < renderers.Length; i++)
		{
			if (IsRenderableWeaponPiece(renderers[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static Transform GetOrCreateLocalVisualRoot(Transform itemRoot, Transform targetVisualRoot)
	{
		AkLocalVisualMarker componentInChildren = ((Component)itemRoot).GetComponentInChildren<AkLocalVisualMarker>(true);
		if ((Object)componentInChildren != (Object)null)
		{
			Transform transform = ((Component)componentInChildren).transform;
			SyncVisualRootToTarget(transform, targetVisualRoot);
			return transform;
		}
		GameObject val = new GameObject("AK_VisualRoot");
		val.transform.SetParent(itemRoot, false);
		val.AddComponent<AkLocalVisualMarker>();
		SyncVisualRootToTarget(val.transform, targetVisualRoot);
		SetLayerRecursively(val.transform, ((Component)itemRoot).gameObject.layer);
		return val.transform;
	}

	private static void RefreshLocalVisual(Transform localVisualRoot, Transform targetVisualRoot, Item item, bool forceRefresh)
	{
		if (!((Object)localVisualRoot == (Object)null))
		{
			SyncVisualRootToTarget(localVisualRoot, targetVisualRoot);
			Transform val = localVisualRoot.Find("AK_LocalVisualClone");
			if (forceRefresh && (Object)val != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)val).gameObject);
				val = null;
			}
			if ((Object)val == (Object)null)
			{
				val = BuildVisualClone(item, localVisualRoot, targetVisualRoot);
			}
			EnsureVisualRenderersActive(val, visible: true);
			Transform originalAnchor = (((Object)targetVisualRoot != (Object)null) ? targetVisualRoot : (((Object)(object)localVisualRoot.parent != (Object)null) ? localVisualRoot.parent : localVisualRoot));
			int layer = (IsLocallyHeldByPlayer(item) ? ResolveBestVisibleLayerForLocalHeldItem(item, originalAnchor) : ResolveReferenceLayer(item, originalAnchor));
			SetLayerRecursively(localVisualRoot, layer);
			Renderer val2 = SelectPreferredRenderer(((Component)val).GetComponentsInChildren<Renderer>(true), val);
			if ((Object)val2 != (Object)null)
			{
				EnsureMuzzleMarkerForItem(item, val2);
			}
			EnsureMuzzleMarker(localVisualRoot, val);
		}
	}

	private static void BindVisualCloneToItem(Item item, Transform visualRoot)
	{
		if ((Object)item == (Object)null || (Object)visualRoot == (Object)null)
		{
			return;
		}
		MeshRenderer[] array = (from r in ((Component)visualRoot).GetComponentsInChildren<MeshRenderer>(true)
			where (Object)r != (Object)null && (Object)GetRendererMesh((Renderer)(object)r) != (Object)null
			select r).ToArray();
		SkinnedMeshRenderer[] array2 = (from r in ((Component)visualRoot).GetComponentsInChildren<SkinnedMeshRenderer>(true)
			where (Object)r != (Object)null && (Object)r.sharedMesh != (Object)null
			select r).ToArray();
		if ((array == null || array.Length == 0) && (array2 == null || array2.Length == 0))
		{
			return;
		}
		foreach (Renderer item2 in array.Cast<Renderer>().Concat((IEnumerable<Renderer>)(object)array2))
		{
			item2.enabled = true;
			item2.forceRenderingOff = false;
			item2.allowOcclusionWhenDynamic = false;
			SkinnedMeshRenderer val = (SkinnedMeshRenderer)(object)((item2 is SkinnedMeshRenderer) ? item2 : null);
			if (val != null)
			{
				val.updateWhenOffscreen = true;
			}
		}
		BindItemRendererFields(item, array, array2);
		Renderer val2 = SelectPreferredRenderer(array.Cast<Renderer>().Concat((IEnumerable<Renderer>)(object)array2), visualRoot);
		if ((Object)val2 != (Object)null)
		{
			EnsureMuzzleMarkerForItem(item, val2);
		}
	}

	private static Transform BuildVisualClone(Item item, Transform localVisualRoot, Transform targetVisualRoot)
	{
		return CreatePreparedAkClone(item, localVisualRoot, targetVisualRoot, "AK_LocalVisualClone");
	}

	internal static void StripNonVisualComponents(GameObject root)
	{
		foreach (Component item in root.GetComponentsInChildren<Component>(true).Reverse())
		{
			if (!((Object)(object)item == (Object)null) && !(item is Transform) && !(item is MeshFilter) && !(item is MeshRenderer) && !(item is SkinnedMeshRenderer))
			{
				Object.Destroy((Object)(object)item);
			}
		}
	}

	private static void PrepareRenderers(GameObject root, Transform targetVisualRoot)
	{
		List<Material> list = ResolveTemplateMaterials(targetVisualRoot);
		Material[] array = list.ToArray();
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		bool flag = HasAnyPreferredRenderer(componentsInChildren);
		Renderer[] array2 = componentsInChildren;
		foreach (Renderer val in array2)
		{
			if (!IsRenderableWeaponPiece(val) && (flag || !IsMeshBackedRenderer(val)))
			{
				val.enabled = false;
				continue;
			}
			val.enabled = true;
			val.forceRenderingOff = false;
			ConfigureRuntimeWeaponRenderer(val);
			Material[] sharedMaterials = val.sharedMaterials;
			Mesh rendererMesh = GetRendererMesh(val);
			int subMeshCount = Math.Max((rendererMesh != null) ? rendererMesh.subMeshCount : 0, 1);
			val.sharedMaterials = BuildVisibleMaterials(sharedMaterials, array, subMeshCount);
			NormalizeRendererMaterials(val);
		}
	}

	private static void EnsureVisualRenderersActive(Transform visualClone, bool visible)
	{
		if ((Object)visualClone == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)visualClone).GetComponentsInChildren<Renderer>(true);
		bool flag = HasAnyPreferredRenderer(componentsInChildren);
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if (!((Object)val == (Object)null))
			{
				if (!IsRenderableWeaponPiece(val) && (flag || !IsMeshBackedRenderer(val)))
				{
					val.enabled = false;
					continue;
				}
				val.enabled = visible;
				val.forceRenderingOff = !visible;
				val.allowOcclusionWhenDynamic = false;
			}
		}
	}

	private static Transform FindBestAnchor(Transform root)
	{
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		bool flag = HasAnyPreferredRenderer(componentsInChildren);
		Renderer val = null;
		int num = int.MinValue;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val2 in array)
		{
			if (!IsRenderableWeaponPiece(val2) && (flag || !IsMeshBackedRenderer(val2)))
			{
				continue;
			}
			Mesh rendererMesh = GetRendererMesh(val2);
			if (!((Object)(object)rendererMesh == (Object)null))
			{
				int num2 = ScoreRenderer(val2, rendererMesh);
				if (num2 > num)
				{
					num = num2;
					val = val2;
				}
			}
		}
		if (!((Object)val != (Object)null))
		{
			return null;
		}
		return ((Component)val).transform;
	}

	private static int ScoreRenderer(Renderer renderer, Mesh mesh)
	{
		int num = mesh.vertexCount;
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + (((Object)((Component)renderer).transform).name ?? string.Empty)).ToLowerInvariant();
		if (text.Contains("ak") || text.Contains("gun") || text.Contains("rifle") || text.Contains("weapon"))
		{
			num += 2000;
		}
		if (text.Contains("body") || text.Contains("mesh"))
		{
			num += 1000;
		}
		if (renderer is SkinnedMeshRenderer)
		{
			num -= 500;
		}
		return num;
	}

	private static void AlignCloneToAnchor(Transform cloneRoot, Transform anchor)
	{
		Matrix4x4 val = cloneRoot.parent.worldToLocalMatrix * anchor.localToWorldMatrix;
		DecomposeMatrix(Matrix4x4.TRS(new Vector3(-0.01f, -0.01f, 0f), Quaternion.Euler(0f, 0f, -12f), Vector3.one * 1.6f) * val.inverse, out var position, out var rotation, out var scale);
		cloneRoot.localPosition = position;
		cloneRoot.localRotation = rotation;
		cloneRoot.localScale = scale;
	}

	private static void EnsureMuzzleMarker(Transform localVisualRoot, Transform visualClone)
	{
		if (!((Object)localVisualRoot == (Object)null))
		{
			Transform val = localVisualRoot.Find("AK_MuzzleMarker");
			if ((Object)val == (Object)null)
			{
				GameObject val2 = new GameObject("AK_MuzzleMarker");
				val2.transform.SetParent(localVisualRoot, false);
				val2.AddComponent<AkMuzzleMarker>();
				val = val2.transform;
			}
			Renderer val3 = FindBestMuzzleRenderer(visualClone);
			if ((Object)val3 == (Object)null)
			{
				val.localPosition = Vector3.forward * 0.6f;
				val.localRotation = Quaternion.identity;
				return;
			}
			Bounds bounds = val3.bounds;
			Vector3 position = bounds.center + ((Component)val3).transform.forward * Mathf.Max(bounds.extents.z, 0.06f);
			val.position = position;
			val.rotation = ((Component)val3).transform.rotation;
		}
	}

	private static Renderer FindBestMuzzleRenderer(Transform visualClone)
	{
		if ((Object)visualClone == (Object)null)
		{
			return null;
		}
		Renderer[] componentsInChildren = ((Component)visualClone).GetComponentsInChildren<Renderer>(true);
		bool flag = HasAnyPreferredRenderer(componentsInChildren);
		Renderer result = null;
		float num = float.MinValue;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			bool flag2 = IsRenderableWeaponPiece(val) || (!flag && IsMeshBackedRenderer(val));
			if (val.enabled && flag2)
			{
				Bounds bounds = val.bounds;
				Vector3 size = bounds.size;
				float num2 = size.magnitude + bounds.center.z;
				if (num2 > num)
				{
					num = num2;
					result = val;
				}
			}
		}
		return result;
	}

	private static Material CreateRuntimeMaterial(Material source, Material template)
	{
		Material val = null;
		bool flag = (Object)template != (Object)null && (Object)template.shader != (Object)null && template.shader.isSupported && !string.IsNullOrWhiteSpace(((Object)template.shader).name) && ((Object)template.shader).name.StartsWith("W/", StringComparison.OrdinalIgnoreCase);
		bool flag2 = (Object)source == (Object)null || (Object)source.shader == (Object)null || !source.shader.isSupported || string.IsNullOrWhiteSpace(((Object)source.shader).name) || string.Equals(((Object)source.shader).name, "Standard", StringComparison.OrdinalIgnoreCase) || string.Equals(((Object)source.shader).name, "Standard (Specular setup)", StringComparison.OrdinalIgnoreCase) || string.Equals(((Object)source.shader).name, "Standard (Roughness setup)", StringComparison.OrdinalIgnoreCase);
		if (flag && flag2)
		{
			val = new Material(template);
			CopySourceMaterialProperties(((Object)source != (Object)null) ? source : template, val, preserveDestinationColor: false);
			NormalizeMaterial(val, template);
			return val;
		}
		if ((Object)source != (Object)null)
		{
			val = new Material(source);
			CopySourceMaterialProperties(source, val, preserveDestinationColor: false);
			NormalizeMaterial(val, source);
			return val;
		}
		if ((Object)template != (Object)null && !HasUsableTexture(source))
		{
			val = new Material(template);
			CopySourceMaterialProperties(((Object)source != (Object)null) ? source : template, val, preserveDestinationColor: false);
		}
		else if ((Object)template != (Object)null)
		{
			val = new Material(template);
			CopySourceMaterialProperties(template, val, preserveDestinationColor: false);
		}
		else
		{
			val = new Material(ResolveFallbackShader());
		}
		NormalizeMaterial(val, ((Object)template != (Object)null) ? template : source);
		return val;
	}

	private static Shader ResolveFallbackShader()
	{
		return Shader.Find("Unlit/Texture") ?? Shader.Find("W/Peak_Standard") ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
	}

	private static void NormalizeMaterial(Material material, Material referenceMaterial)
	{
		if (!((Object)material == (Object)null))
		{
			Shader shader = ResolveFallbackShader();
			string text = (((Object)material.shader != (Object)null) ? ((Object)material.shader).name : string.Empty);
			bool flag = (Object)material.shader == (Object)null || !material.shader.isSupported || string.IsNullOrWhiteSpace(text);
			if (flag && (Object)referenceMaterial != (Object)null && (Object)referenceMaterial.shader != (Object)null && referenceMaterial.shader.isSupported && !string.IsNullOrWhiteSpace(((Object)referenceMaterial.shader).name))
			{
				material.shader = referenceMaterial.shader;
			}
			if ((Object)material.shader == (Object)null || !material.shader.isSupported)
			{
				material.shader = shader;
			}
			Color color = ResolveMaterialColor(material);
			color.a = 1f;
			ApplyColor(material, color);
			if (material.HasProperty("_BaseColor"))
			{
				Color color2 = material.GetColor("_BaseColor");
				color2.a = 1f;
				color2.r = Mathf.Max(color2.r, 0.82f);
				color2.g = Mathf.Max(color2.g, 0.82f);
				color2.b = Mathf.Max(color2.b, 0.82f);
				material.SetColor("_BaseColor", color2);
			}
			if (material.HasProperty("_Color"))
			{
				Color color3 = material.color;
				color3.a = 1f;
				color3.r = Mathf.Max(color3.r, 0.82f);
				color3.g = Mathf.Max(color3.g, 0.82f);
				color3.b = Mathf.Max(color3.b, 0.82f);
				material.color = color3;
			}
			if (material.HasProperty("_EmissionColor"))
			{
				material.EnableKeyword("_EMISSION");
				material.SetColor("_EmissionColor", new Color(0.08f, 0.08f, 0.08f, 1f));
			}
			if (material.HasProperty("_Surface"))
			{
				material.SetFloat("_Surface", 0f);
			}
			if (material.HasProperty("_AlphaClip"))
			{
				material.SetFloat("_AlphaClip", 0f);
			}
			if (material.HasProperty("_Cutoff"))
			{
				material.SetFloat("_Cutoff", 0f);
			}
			if (material.HasProperty("_Cull"))
			{
				material.SetInt("_Cull", 0);
			}
			material.renderQueue = (((Object)referenceMaterial != (Object)null) ? referenceMaterial.renderQueue : (-1));
		}
	}

	private static List<Material> ResolveTemplateMaterials(Transform targetVisualRoot)
	{
		List<Material> list = new List<Material>();
		if ((Object)targetVisualRoot == (Object)null)
		{
			return list;
		}
		Renderer[] componentsInChildren = ((Component)targetVisualRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!IsOriginalWeaponRenderer(val, targetVisualRoot))
			{
				continue;
			}
			Material[] sharedMaterials = val.sharedMaterials;
			if (sharedMaterials == null)
			{
				continue;
			}
			Material[] array = sharedMaterials;
			foreach (Material val2 in array)
			{
				if ((Object)val2 != (Object)null)
				{
					list.Add(val2);
				}
			}
		}
		if (list.Count == 0)
		{
			Item componentInParent = ((Component)targetVisualRoot).GetComponentInParent<Item>();
			if ((Object)componentInParent != (Object)null)
			{
				AddTemplateMaterials(componentInParent.mainRenderer, list);
				Renderer[] addtlRenderers = componentInParent.addtlRenderers;
				if (addtlRenderers == null)
				{
					return list;
				}
				componentsInChildren = addtlRenderers;
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					AddTemplateMaterials(componentsInChildren[i], list);
				}
			}
		}
		return list;
	}

	private static void AddTemplateMaterials(Renderer renderer, List<Material> output)
	{
		if ((Object)renderer == (Object)null)
		{
			return;
		}
		Material[] sharedMaterials = renderer.sharedMaterials;
		if (sharedMaterials == null)
		{
			return;
		}
		Material[] array = sharedMaterials;
		foreach (Material val in array)
		{
			if ((Object)val != (Object)null)
			{
				output.Add(val);
			}
		}
	}

	private static void CopySourceMaterialProperties(Material source, Material destination, bool preserveDestinationColor)
	{
		if ((Object)source == (Object)null || (Object)destination == (Object)null)
		{
			return;
		}
		Texture val = ResolvePrimaryTexture(source);
		if ((Object)val != (Object)null)
		{
			bool flag = false;
			if (destination.HasProperty("_MainTex"))
			{
				destination.SetTexture("_MainTex", val);
				CopyTextureScaleAndOffset(source, destination, "_MainTex", "_MainTex");
				flag = true;
			}
			if (destination.HasProperty("_BaseMap"))
			{
				destination.SetTexture("_BaseMap", val);
				CopyTextureScaleAndOffset(source, destination, "_BaseMap", "_BaseMap");
				flag = true;
			}
			if (!flag)
			{
				AssignPrimaryTextureToDestination(source, destination, val);
			}
		}
		CopyTextureByAlias(source, destination, "_BaseMap", "_MainTex");
		CopyTextureByAlias(source, destination, "_MainTex", "_BaseMap");
		CopyTextureByAlias(source, destination, "_BumpMap", "_NormalMap");
		CopyTextureByAlias(source, destination, "_NormalMap", "_BumpMap");
		CopyTextureByAlias(source, destination, "_MetallicGlossMap", "_MetallicTex");
		CopyTextureByAlias(source, destination, "_MetallicTex", "_MetallicGlossMap");
		CopyTextureIfPresent(source, destination, "_BumpMap");
		CopyTextureIfPresent(source, destination, "_NormalMap");
		CopyTextureIfPresent(source, destination, "_EmissionMap");
		CopyTextureIfPresent(source, destination, "_MetallicGlossMap");
		CopyTextureIfPresent(source, destination, "_OcclusionMap");
		string[] texturePropertyNames = source.GetTexturePropertyNames();
		if (texturePropertyNames != null)
		{
			string[] array = texturePropertyNames;
			foreach (string propertyName in array)
			{
				CopyTextureIfPresent(source, destination, propertyName);
			}
		}
		if (!preserveDestinationColor)
		{
			Color color = ResolveMaterialColor(source);
			color.a = 1f;
			ApplyColor(destination, color);
		}
		else
		{
			Color color2 = ResolveMaterialColor(destination);
			color2.a = 1f;
			ApplyColor(destination, color2);
		}
		CopyColorIfPresent(source, destination, "_EmissionColor");
		CopyFloatIfPresent(source, destination, "_Metallic");
		CopyFloatIfPresent(source, destination, "_Smoothness");
		CopyFloatIfPresent(source, destination, "_Glossiness");
		CopyFloatIfPresent(source, destination, "_BumpScale");
		CopyFloatIfPresent(source, destination, "_Cutoff");
		if (source.IsKeywordEnabled("_EMISSION"))
		{
			destination.EnableKeyword("_EMISSION");
		}
	}

	private static void CopyTextureIfPresent(Material source, Material destination, string propertyName)
	{
		if (source.HasProperty(propertyName) && destination.HasProperty(propertyName))
		{
			Texture texture = source.GetTexture(propertyName);
			if ((Object)texture != (Object)null)
			{
				destination.SetTexture(propertyName, texture);
				CopyTextureScaleAndOffset(source, destination, propertyName, propertyName);
			}
		}
	}

	private static void CopyTextureByAlias(Material source, Material destination, string sourceProperty, string destinationProperty)
	{
		if (source.HasProperty(sourceProperty) && destination.HasProperty(destinationProperty))
		{
			Texture texture = source.GetTexture(sourceProperty);
			if (!((Object)texture == (Object)null))
			{
				destination.SetTexture(destinationProperty, texture);
				CopyTextureScaleAndOffset(source, destination, sourceProperty, destinationProperty);
			}
		}
	}

	private static void AssignPrimaryTextureToDestination(Material source, Material destination, Texture texture)
	{
		if ((Object)texture == (Object)null || (Object)destination == (Object)null)
		{
			return;
		}
		string[] texturePropertyNames = destination.GetTexturePropertyNames();
		if (texturePropertyNames == null || texturePropertyNames.Length == 0)
		{
			return;
		}
		string text = texturePropertyNames.FirstOrDefault((string name) => !string.IsNullOrEmpty(name) && (name.IndexOf("base", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("albedo", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("diff", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("main", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0));
		if (string.IsNullOrEmpty(text))
		{
			text = texturePropertyNames.FirstOrDefault((string name) => !string.IsNullOrEmpty(name) && name.IndexOf("normal", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("bump", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("metal", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("occlusion", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("emission", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("detail", StringComparison.OrdinalIgnoreCase) < 0) ?? texturePropertyNames[0];
		}
		if (string.IsNullOrEmpty(text) || !destination.HasProperty(text))
		{
			return;
		}
		destination.SetTexture(text, texture);
		if ((Object)source != (Object)null)
		{
			string text2 = ((source.HasProperty(text) && (Object)source.GetTexture(text) != (Object)null) ? text : ResolveSourceScaleProperty(source));
			if (!string.IsNullOrEmpty(text2))
			{
				CopyTextureScaleAndOffset(source, destination, text2, text);
			}
		}
	}

	private static string ResolveSourceScaleProperty(Material source)
	{
		if ((Object)source == (Object)null)
		{
			return null;
		}
		string[] array = new string[3] { "_BaseMap", "_MainTex", "_AlbedoMap" };
		foreach (string text in array)
		{
			if (source.HasProperty(text) && (Object)source.GetTexture(text) != (Object)null)
			{
				return text;
			}
		}
		return null;
	}

	private static void CopyTextureScaleAndOffset(Material source, Material destination, string sourceProperty, string destinationProperty)
	{
		if (source.HasProperty(sourceProperty) && destination.HasProperty(destinationProperty))
		{
			destination.SetTextureScale(destinationProperty, source.GetTextureScale(sourceProperty));
			destination.SetTextureOffset(destinationProperty, source.GetTextureOffset(sourceProperty));
		}
	}

	private static Texture ResolvePrimaryTexture(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return null;
		}
		string[] array = new string[6] { "_BaseMap", "_MainTex", "_BaseColorMap", "_AlbedoMap", "_Albedo", "_DiffuseMap" };
		foreach (string text in array)
		{
			if (material.HasProperty(text))
			{
				Texture texture = material.GetTexture(text);
				if ((Object)texture != (Object)null)
				{
					return texture;
				}
			}
		}
		string[] texturePropertyNames = material.GetTexturePropertyNames();
		if (texturePropertyNames == null)
		{
			return null;
		}
		string[] array2 = texturePropertyNames;
		foreach (string text2 in array2)
		{
			if (material.HasProperty(text2))
			{
				Texture texture2 = material.GetTexture(text2);
				if ((Object)texture2 != (Object)null)
				{
					return texture2;
				}
			}
		}
		return null;
	}

	private static bool HasUsableTexture(Material material)
	{
		if ((Object)material != (Object)null)
		{
			return (Object)ResolvePrimaryTexture(material) != (Object)null;
		}
		return false;
	}

	private static bool HasUsableTexture(Material[] materials)
	{
		if (materials == null || materials.Length == 0)
		{
			return false;
		}
		foreach (Material val in materials)
		{
			if ((Object)val != (Object)null && (Object)ResolvePrimaryTexture(val) != (Object)null)
			{
				return true;
			}
		}
		return false;
	}

	private static void CopyFloatIfPresent(Material source, Material destination, string propertyName)
	{
		if (source.HasProperty(propertyName) && destination.HasProperty(propertyName))
		{
			destination.SetFloat(propertyName, source.GetFloat(propertyName));
		}
	}

	private static void CopyColorIfPresent(Material source, Material destination, string propertyName)
	{
		if (source.HasProperty(propertyName) && destination.HasProperty(propertyName))
		{
			destination.SetColor(propertyName, source.GetColor(propertyName));
		}
	}

	private static Color ResolveMaterialColor(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return Color.white;
		}
		if (material.HasProperty("_BaseColor"))
		{
			return material.GetColor("_BaseColor");
		}
		if (material.HasProperty("_Color"))
		{
			return material.GetColor("_Color");
		}
		return Color.white;
	}

	private static void ApplyColor(Material material, Color color)
	{
		if (!((Object)material == (Object)null))
		{
			if (material.HasProperty("_BaseColor"))
			{
				material.SetColor("_BaseColor", color);
			}
			if (material.HasProperty("_Color"))
			{
				material.SetColor("_Color", color);
			}
		}
	}

	private static void HideOriginalRenderers(Item item, Transform targetVisualRoot)
	{
		if ((Object)item == (Object)null || (Object)targetVisualRoot == (Object)null)
		{
			return;
		}
		List<Renderer> list = new List<Renderer>();
		Renderer[] componentsInChildren = ((Component)targetVisualRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && IsOriginalWeaponRenderer(val, targetVisualRoot))
			{
				val.enabled = false;
				val.forceRenderingOff = true;
				list.Add(val);
			}
		}
		HiddenRendererCache[((Object)item).GetInstanceID()] = list.ToArray();
		Plugin.LogDiagnosticOnce("ak-hide-original:" + ((Object)item).GetInstanceID(), $"HideOriginalRenderers processed {DescribeItemForDiagnostics(item)}, targetRoot={GetTransformPath(targetVisualRoot)}, hiddenCount={list.Count}, hidden={DescribeRendererCollectionForDiagnostics(list)}");
	}

	private static bool IsOriginalWeaponRenderer(Renderer renderer, Transform targetVisualRoot)
	{
		if ((Object)renderer == (Object)null)
		{
			return false;
		}
		if ((Object)((Component)renderer).GetComponentInParent<AkLocalVisualMarker>() != (Object)null)
		{
			return false;
		}
		if ((Object)((Component)renderer).GetComponentInParent<AkSimpleVisualMarker>() != (Object)null)
		{
			return false;
		}
		if ((Object)((Component)renderer).GetComponentInParent<AkHeldLegacyVisualMarker>() != (Object)null)
		{
			return false;
		}
		if (HasNamedAncestor(((Component)renderer).transform, InPlaceVisualRootName))
		{
			return false;
		}
		if (!((Component)renderer).transform.IsChildOf(targetVisualRoot))
		{
			return false;
		}
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + (((Object)((Component)renderer).transform).name ?? string.Empty)).ToLowerInvariant();
		if (!text.Contains("hand") && !text.Contains("arm"))
		{
			return !text.Contains("finger");
		}
		return false;
	}

	private static void RestoreOriginalVisual(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		RestoreInPlaceVisual(item);
		AkLocalVisualMarker componentInChildren = ((Component)item).GetComponentInChildren<AkLocalVisualMarker>(true);
		if ((Object)componentInChildren != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)componentInChildren).gameObject);
		}
		if (HiddenRendererCache.TryGetValue(((Object)item).GetInstanceID(), out var value))
		{
			Renderer[] array = value;
			foreach (Renderer val in array)
			{
				if (!((Object)val == (Object)null))
				{
					val.enabled = true;
					val.forceRenderingOff = false;
					val.allowOcclusionWhenDynamic = false;
				}
			}
		}
		HiddenRendererCache.Remove(((Object)item).GetInstanceID());
		CleanupAllWeaponWorldColliders(item);
		SetOriginalItemCollidersEnabled(item, enabled: true);
	}

	private static void CleanupItem(Item item)
	{
		if (!((Object)item == (Object)null))
		{
			RestoreInPlaceVisual(item);
			CleanupSimpleAkVisual(item);
			DestroyLegacyHeldVisual(item, restoreRenderers: false);
			CleanupAllWeaponWorldColliders(item);
			SetOriginalItemCollidersEnabled(item, enabled: true);
			InPlaceSwappedItemIds.Remove(((Object)item).GetInstanceID());
			HiddenRendererCache.Remove(((Object)item).GetInstanceID());
			PendingSetStateValueByItemId.Remove(((Object)item).GetInstanceID());
			PendingSetStateHolderByItemId.Remove(((Object)item).GetInstanceID());
		}
	}

	private static void RestoreInPlaceVisual(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		Transform[] array = ((Component)item).GetComponentsInChildren<Transform>(true);
		foreach (Transform val in array)
		{
			if (!((Object)val == (Object)null) && string.Equals(((Object)val).name, InPlaceVisualRootName, StringComparison.Ordinal))
			{
				Object.Destroy((Object)(object)((Component)val).gameObject);
			}
		}
		AkInPlaceMarker[] componentsInChildren = ((Component)item).GetComponentsInChildren<AkInPlaceMarker>(true);
		foreach (AkInPlaceMarker val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			Transform transform = ((Component)val).transform;
			if (val.hasBaseTransform)
			{
				transform.localPosition = val.baseLocalPosition;
				transform.localRotation = val.baseLocalRotation;
				transform.localScale = val.baseLocalScale;
			}
			MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
			if ((Object)component != (Object)null && val.hasOriginalVisual)
			{
				component.sharedMesh = val.originalMesh;
			}
			MeshRenderer component2 = ((Component)val).GetComponent<MeshRenderer>();
			if ((Object)component2 != (Object)null)
			{
				if (val.hasOriginalVisual)
				{
					((Renderer)component2).sharedMaterials = (val.originalMaterials ?? Array.Empty<Material>());
				}
				((Renderer)component2).enabled = true;
				((Renderer)component2).forceRenderingOff = false;
				((Renderer)component2).allowOcclusionWhenDynamic = false;
			}
			Object.Destroy((Object)(object)val);
		}
		InPlaceSwappedItemIds.Remove(((Object)item).GetInstanceID());
	}

	private static void CleanupSimpleAkVisual(Item item)
	{
		if (!((Object)item == (Object)null))
		{
			Transform val = FindSimpleAkVisualRoot(item);
			if ((Object)val != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)val).gameObject);
			}
		}
	}

	private static Transform FindSimpleAkVisualRoot(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		AkSimpleVisualMarker componentInChildren = ((Component)item).GetComponentInChildren<AkSimpleVisualMarker>(true);
		if ((Object)componentInChildren != (Object)null)
		{
			return ((Component)componentInChildren).transform;
		}
		return ((Component)item).GetComponentsInChildren<Transform>(true).FirstOrDefault((Transform t) => (Object)t != (Object)null && string.Equals(((Object)t).name, "AK47Model", StringComparison.Ordinal));
	}

	private static Transform TryCreatePreparedSimpleAkVisual(Item item, Transform targetVisualRoot)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		Transform val = targetVisualRoot;
		if ((Object)val == (Object)null)
		{
			val = ((Component)item).transform;
		}
		Transform val2 = CreatePreparedAkClone(item, val, targetVisualRoot, "AK47Model");
		if ((Object)val2 == (Object)null)
		{
			return null;
		}
		if ((Object)((Component)val2).GetComponent<AkSimpleVisualMarker>() == (Object)null)
		{
			((Component)val2).gameObject.AddComponent<AkSimpleVisualMarker>();
		}
		EnsureVisualRenderersActive(val2, visible: true);
		return val2;
	}

	private static bool ShouldPreferBakedSimpleVisual(Item item)
	{
		return (Object)item != (Object)null;
	}

	private static bool ShouldIncludeBakedSimpleSourceRenderer(Renderer sourceRenderer)
	{
		if ((Object)sourceRenderer == (Object)null || !IsMeshBackedRenderer(sourceRenderer))
		{
			return false;
		}
		if ((Object)GetRendererMesh(sourceRenderer) == (Object)null)
		{
			return false;
		}
		if (ShouldRejectDirectAkSource(((Component)sourceRenderer).transform))
		{
			return false;
		}
		string text = ((((Object)sourceRenderer).name ?? string.Empty) + "/" + GetTransformPath(((Component)sourceRenderer).transform)).ToLowerInvariant();
		if (ExcludedVisualKeywords.Any((string keyword) => !string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword)))
		{
			return false;
		}
		return true;
	}

	private static Transform TryCreateBakedSimpleAkVisual(Item item, Transform targetVisualRoot)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		string weaponSelectionForItem = Plugin.GetWeaponSelectionForItem(item);
		GameObject akVisualPrefab = Plugin.GetAkVisualPrefab(weaponSelectionForItem);
		if ((Object)akVisualPrefab == (Object)null)
		{
			return null;
		}
		Transform val = targetVisualRoot;
		if ((Object)val == (Object)null)
		{
			val = ((Component)item).transform;
		}
		GameObject val2 = new GameObject("AK47Model");
		val2.transform.SetParent(val, false);
		val2.transform.localPosition = GetDirectAkPositionOverride(weaponSelectionForItem);
		val2.transform.localRotation = GetDirectAkRotationOverride();
		val2.transform.localScale = Vector3.one;
		val2.AddComponent<AkSimpleVisualMarker>();
		Material[] array = ResolveTemplateMaterials(val).ToArray();
		int num = 0;
		Renderer[] componentsInChildren = akVisualPrefab.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val3 in componentsInChildren)
		{
			if (!ShouldIncludeBakedSimpleSourceRenderer(val3))
			{
				continue;
			}
			Mesh rendererMesh = GetRendererMesh(val3);
			if ((Object)rendererMesh == (Object)null || rendererMesh.vertexCount <= 0)
			{
				continue;
			}
			GameObject val4 = new GameObject(string.IsNullOrWhiteSpace(((Object)val3).name) ? ("AK47MeshPart_" + num) : ((Object)val3).name);
			val4.transform.SetParent(val2.transform, false);
			if (!TryGetTransformPoseRelativeToAkRoot(((Component)val3).transform, out var localPosition, out var localRotation, out var localScale))
			{
				localPosition = ((Component)val3).transform.localPosition;
				localRotation = ((Component)val3).transform.localRotation;
				localScale = ((Component)val3).transform.localScale;
			}
			val4.transform.localPosition = SanitizeLocalPosition(localPosition, Vector3.zero);
			val4.transform.localRotation = SanitizeLocalRotation(localRotation);
			val4.transform.localScale = SanitizeLocalScale(localScale, Vector3.one);
			MeshFilter val5 = val4.AddComponent<MeshFilter>();
			MeshRenderer obj = val4.AddComponent<MeshRenderer>();
			val5.sharedMesh = rendererMesh;
			((Renderer)obj).sharedMaterials = BuildVisibleMaterials(val3.sharedMaterials, array, Math.Max(rendererMesh.subMeshCount, 1));
			ConfigureRuntimeWeaponRenderer((Renderer)(object)obj);
			NormalizeRendererMaterials((Renderer)(object)obj);
			((Renderer)obj).enabled = true;
			((Renderer)obj).forceRenderingOff = false;
			num++;
		}
		if (num == 0)
		{
			Object.Destroy((Object)(object)val2);
			return null;
		}
		NormalizeVisualScaleAgainstReference(val2.transform, val, "AK47Model-baked");
		ApplyConfiguredWorldVisualScale(val2.transform, weaponSelectionForItem);
		SetLayerRecursively(val2.transform, ((Component)val).gameObject.layer);
		SetHierarchyActive(val2.transform, active: true);
		EnsureVisualRenderersActive(val2.transform, visible: true);
		Plugin.LogDiagnosticOnce("ak-simple-baked-create-success:" + ((Object)item).GetInstanceID(), $"Created baked simple replacement visual: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(val)}, visual={GetTransformPath(val2.transform)}, renderers={DescribeRendererCollectionForDiagnostics(((Component)val2.transform).GetComponentsInChildren<Renderer>(true))}, bounds={DescribeBoundsForDiagnostics(val2.transform)}, selection={weaponSelectionForItem}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics(weaponSelectionForItem)}");
		return val2.transform;
	}

	private static Transform GetOrCreateSimpleAkVisual(Item item, Transform targetVisualRoot, bool forceRefreshMarker)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		Transform val = FindSimpleAkVisualRoot(item);
		if ((Object)val != (Object)null && (Object)targetVisualRoot != (Object)null && (Object)(object)val.parent != (Object)(object)targetVisualRoot)
		{
			Plugin.LogDiagnosticOnce("ak-simple-recreate-parent:" + ((Object)item).GetInstanceID() + ":" + ((Object)val).GetInstanceID(), $"Recreating simple visual because parent changed: {DescribeItemForDiagnostics(item)}, existingParent={GetTransformPath(val.parent)}, targetParent={GetTransformPath(targetVisualRoot)}, existingRenderers={DescribeRendererCollectionForDiagnostics(((Component)val).GetComponentsInChildren<Renderer>(true))}");
			Object.Destroy((Object)(object)((Component)val).gameObject);
			val = null;
			forceRefreshMarker = true;
		}
		if ((Object)val != (Object)null && !forceRefreshMarker && HasReplacementVisualSource(item) && CountMeshBackedRenderers(val) == 0)
		{
			Plugin.LogDiagnosticOnce("ak-simple-recreate-empty:" + ((Object)item).GetInstanceID() + ":" + ((Object)val).GetInstanceID(), $"Recreating simple visual because it has no mesh-backed renderers: {DescribeItemForDiagnostics(item)}, visual={GetTransformPath(val)}, renderers={DescribeRendererCollectionForDiagnostics(((Component)val).GetComponentsInChildren<Renderer>(true))}");
			Object.Destroy((Object)(object)((Component)val).gameObject);
			val = null;
			forceRefreshMarker = true;
		}
		if (forceRefreshMarker && (Object)val != (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-simple-force-refresh:" + ((Object)item).GetInstanceID() + ":" + ((Object)val).GetInstanceID(), $"Destroying simple visual for force refresh: {DescribeItemForDiagnostics(item)}, visual={GetTransformPath(val)}, renderers={DescribeRendererCollectionForDiagnostics(((Component)val).GetComponentsInChildren<Renderer>(true))}");
			Object.Destroy((Object)(object)((Component)val).gameObject);
			val = null;
		}
		if ((Object)val != (Object)null && !forceRefreshMarker)
		{
			Plugin.LogDiagnosticOnce("ak-simple-reuse:" + ((Object)item).GetInstanceID() + ":" + ((Object)val).GetInstanceID(), $"Reusing existing simple visual: {DescribeItemForDiagnostics(item)}, visual={GetTransformPath(val)}, parent={GetTransformPath(val.parent)}, bounds={DescribeBoundsForDiagnostics(val)}, renderers={DescribeRendererCollectionForDiagnostics(((Component)val).GetComponentsInChildren<Renderer>(true))}");
		}
		if ((Object)val == (Object)null)
		{
			bool useDebugBall = UseDebugAnchorSphereVisual;
			Transform val2 = targetVisualRoot;
			if ((Object)val2 == (Object)null)
			{
				val2 = ((Component)item).transform;
			}
			if (!useDebugBall)
			{
				val = TryCreatePreparedSimpleAkVisual(item, val2);
			}
			if ((Object)val == (Object)null)
			{
				string text = string.Empty;
				Mesh mesh = null;
				Material[] materials = null;
				Vector3 localScale = Vector3.one;
				Quaternion localRotation = Quaternion.identity;
				Vector3 localPosition = Vector3.zero;
				bool resolvedVisual = useDebugBall && TryResolveDebugSphereVisual(out mesh, out materials, out localScale, out localRotation, out localPosition, out text);
				if (!resolvedVisual || (Object)mesh == (Object)null)
				{
					Plugin.LogDiagnosticOnce("ak-simple-create-fail:" + ((Object)item).GetInstanceID(), $"Simple visual creation failed: {DescribeItemForDiagnostics(item)}, targetRoot={GetTransformPath(targetVisualRoot)}, debugMode={useDebugBall}, debugInfo={text}, selection={Plugin.GetWeaponSelectionForItem(item)}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics(Plugin.GetWeaponSelectionForItem(item))}");
					return null;
				}
				GameObject val3 = new GameObject("AK47Model");
				val3.transform.SetParent(val2, false);
				val3.transform.localPosition = localPosition;
				val3.transform.localRotation = localRotation;
				val3.transform.localScale = localScale;
				val3.AddComponent<AkSimpleVisualMarker>();
				MeshFilter val4 = val3.AddComponent<MeshFilter>();
				MeshRenderer obj = val3.AddComponent<MeshRenderer>();
				val4.sharedMesh = mesh;
				int num = Mathf.Max(mesh.subMeshCount, 1);
				((Renderer)obj).sharedMaterials = BuildVisibleMaterials(materials, ResolveTemplateMaterials(val2).ToArray(), num);
				((Renderer)obj).enabled = true;
				((Renderer)obj).forceRenderingOff = false;
				ConfigureRuntimeWeaponRenderer((Renderer)(object)obj);
				NormalizeVisualScaleAgainstReference(val3.transform, val2, "AK47Model-direct");
				ApplyConfiguredWorldVisualScale(val3.transform, Plugin.GetWeaponSelectionForItem(item));
				SetLayerRecursively(layer: IsLocallyHeldByPlayer(item) ? ResolveBestVisibleLayerForLocalHeldItem(item, val2) : ResolveReferenceLayer(item, val2), root: val3.transform);
				val = val3.transform;
				Plugin.LogDiagnosticOnce("ak-simple-create-success:" + ((Object)item).GetInstanceID(), $"Created simple debug visual: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(val2)}, visual={GetTransformPath(val)}, debugInfo={text}, mesh={((Object)mesh).name}, pos={localPosition}, rot={localRotation.eulerAngles}, scale={localScale}");
			}
			else if (!useDebugBall)
			{
				Plugin.LogDiagnosticOnce("ak-simple-create-clone-success:" + ((Object)item).GetInstanceID(), $"Created simple replacement visual from selected prefab clone: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(val2)}, visual={GetTransformPath(val)}, selection={Plugin.GetWeaponSelectionForItem(item)}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics(Plugin.GetWeaponSelectionForItem(item))}");
			}
		}
		Renderer val5 = SelectPreferredRenderer(((Component)val).GetComponentsInChildren<Renderer>(true), val);
		if ((Object)val5 == (Object)null)
		{
			bool showFallbackVisual = ShouldShowSimpleAkVisual(item);
			SetCustomVisualRenderersVisible(val, showFallbackVisual);
			SyncWeaponColliderState(item, null);
			LogWorldVisualState(item, val, showFallbackVisual ? "simple-no-preferred-renderer-visible" : "simple-no-preferred-renderer-hidden", targetVisualRoot);
			return val;
		}
		bool flag = ShouldShowSimpleAkVisual(item);
		SetCustomVisualRenderersVisible(val, flag);
		if (!flag)
		{
			SyncWeaponColliderState(item, null);
			LogWorldVisualState(item, val, "simple-hidden", targetVisualRoot);
			return val;
		}
		val5.enabled = true;
		val5.forceRenderingOff = false;
		val5.allowOcclusionWhenDynamic = false;
		int layer = (IsLocallyHeldByPlayer(item) ? ResolveBestVisibleLayerForLocalHeldItem(item, targetVisualRoot) : ResolveReferenceLayer(item, targetVisualRoot));
		SetLayerRecursively(val, layer);
		EnsureMuzzleMarkerForItem(item, val5);
		SyncWeaponColliderState(item, null);
		LogWorldVisualState(item, val, "simple-final", targetVisualRoot);
		return val;
	}

	private static bool ShouldUseWorldWeaponCollider(Item item)
	{
		return IsDroppedWorldItem(item);
	}

	private static void SyncWeaponColliderState(Item item, Transform weaponVisualRoot)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		CleanupAllWeaponWorldColliders(item);
		SetOriginalItemCollidersEnabled(item, enabled: true);
		Plugin.LogDiagnosticOnce("ak-collider-state:" + ((Object)item).GetInstanceID() + ":" + (int)item.itemState + ":original-only", $"Synced weapon collider state: {DescribeItemForDiagnostics(item)}, requestedCustom=False, usingCustom=False, mode=original-item, visualRoot={GetTransformPath(weaponVisualRoot)}");
	}

	private static bool EnsureWorldWeaponCollider(Item item, Transform visualRoot)
	{
		if ((Object)item == (Object)null || (Object)visualRoot == (Object)null)
		{
			return false;
		}
		if (!ShouldUseWorldWeaponCollider(item))
		{
			CleanupAllWeaponWorldColliders(item);
			return false;
		}
		Transform transform = ((Component)item).transform;
		if ((Object)transform == (Object)null)
		{
			return false;
		}
		if (!TryGetCombinedRenderableBounds(visualRoot, out var bounds) || !TryTransformWorldBoundsToLocalBounds(transform, bounds, out var localBounds))
		{
			Plugin.LogDiagnosticOnce("ak-world-collider-fail:" + ((Object)item).GetInstanceID(), $"Failed to build world collider from render bounds, keeping original colliders: {DescribeItemForDiagnostics(item)}, visualRoot={GetTransformPath(visualRoot)}, boundsValid={TryGetCombinedRenderableBounds(visualRoot, out var _)}");
			return false;
		}
		Transform val = ((Component)item).GetComponentsInChildren<Transform>(true).FirstOrDefault((Transform t) => (Object)t != (Object)null && string.Equals(((Object)t).name, WorldColliderName, StringComparison.Ordinal));
		if ((Object)val == (Object)null)
		{
			GameObject val2 = new GameObject(WorldColliderName);
			val2.transform.SetParent(transform, false);
			val = val2.transform;
		}
		else if ((Object)(object)val.parent != (Object)(object)transform)
		{
			val.SetParent(transform, false);
		}
		val.localRotation = Quaternion.identity;
		val.localScale = Vector3.one;
		val.localPosition = localBounds.center;
		BoxCollider val3 = ((Component)val).GetComponent<BoxCollider>();
		if ((Object)val3 == (Object)null)
		{
			val3 = ((Component)val).gameObject.AddComponent<BoxCollider>();
		}
		val3.isTrigger = false;
		val3.enabled = true;
		val3.center = Vector3.zero;
		val3.size = new Vector3(Mathf.Max(localBounds.size.x, 0.18f), Mathf.Max(localBounds.size.y, 0.18f), Mathf.Max(localBounds.size.z, 0.6f));
		Plugin.LogDiagnosticOnce("ak-world-collider:" + ((Object)item).GetInstanceID() + ":" + ((Object)visualRoot).GetInstanceID(), $"Configured world collider: {DescribeItemForDiagnostics(item)}, visualRoot={GetTransformPath(visualRoot)}, collider={GetTransformPath(val)}, parentScale={transform.lossyScale}, localCenter={val.localPosition}, localSize={val3.size}, trigger={val3.isTrigger}, bounds={DescribeBoundsForDiagnostics(visualRoot)}");
		return true;
	}

	private static void CleanupWorldWeaponCollider(Transform visualRoot)
	{
		if ((Object)visualRoot == (Object)null)
		{
			return;
		}
		Transform val = visualRoot.Find(WorldColliderName);
		if ((Object)val != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)val).gameObject);
		}
	}

	private static void CleanupAllWeaponWorldColliders(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		Transform[] componentsInChildren = ((Component)item).GetComponentsInChildren<Transform>(true);
		foreach (Transform val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && string.Equals(((Object)val).name, WorldColliderName, StringComparison.Ordinal))
			{
				Object.Destroy((Object)(object)((Component)val).gameObject);
			}
		}
	}

	private static void SetOriginalItemCollidersEnabled(Item item, bool enabled)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		foreach (Collider originalItemCollider in EnumerateOriginalItemColliders(item))
		{
			if (!((Object)originalItemCollider == (Object)null))
			{
				originalItemCollider.enabled = enabled;
			}
		}
	}

	private static IEnumerable<Collider> EnumerateOriginalItemColliders(Item item)
	{
		if ((Object)item == (Object)null)
		{
			yield break;
		}
		Collider[] componentsInChildren = ((Component)item).GetComponentsInChildren<Collider>(true);
		foreach (Collider collider in componentsInChildren)
		{
			if (!((Object)collider == (Object)null) && !IsCustomWeaponCollider(collider))
			{
				yield return collider;
			}
		}
	}

	private static bool IsCustomWeaponCollider(Collider collider)
	{
		if ((Object)collider == (Object)null)
		{
			return false;
		}
		if (string.Equals(((Object)collider).name, WorldColliderName, StringComparison.Ordinal))
		{
			return true;
		}
		Transform transform = ((Component)collider).transform;
		if ((Object)((Component)collider).GetComponentInParent<AkSimpleVisualMarker>(true) != (Object)null)
		{
			return true;
		}
		if ((Object)((Component)collider).GetComponentInParent<AkLocalVisualMarker>(true) != (Object)null)
		{
			return true;
		}
		if ((Object)((Component)collider).GetComponentInParent<AkHeldLegacyVisualMarker>(true) != (Object)null)
		{
			return true;
		}
		if (HasNamedAncestor(transform, InPlaceVisualRootName))
		{
			return true;
		}
		return string.Equals(((Object)transform).name, WorldColliderName, StringComparison.Ordinal);
	}

	private static bool TryTransformWorldBoundsToLocalBounds(Transform localRoot, Bounds worldBounds, out Bounds localBounds)
	{
		localBounds = default(Bounds);
		if ((Object)localRoot == (Object)null)
		{
			return false;
		}
		Matrix4x4 worldToLocalMatrix = localRoot.worldToLocalMatrix;
		Vector3 center = worldBounds.center;
		Vector3 extents = worldBounds.extents;
		Vector3[] array = new Vector3[8]
		{
			center + new Vector3(0f - extents.x, 0f - extents.y, 0f - extents.z),
			center + new Vector3(0f - extents.x, 0f - extents.y, extents.z),
			center + new Vector3(0f - extents.x, extents.y, 0f - extents.z),
			center + new Vector3(0f - extents.x, extents.y, extents.z),
			center + new Vector3(extents.x, 0f - extents.y, 0f - extents.z),
			center + new Vector3(extents.x, 0f - extents.y, extents.z),
			center + new Vector3(extents.x, extents.y, 0f - extents.z),
			center + new Vector3(extents.x, extents.y, extents.z)
		};
		bool flag = false;
		Vector3[] array2 = array;
		foreach (Vector3 point in array2)
		{
			Vector3 vector = worldToLocalMatrix.MultiplyPoint3x4(point);
			if (!IsFiniteVector3(vector))
			{
				continue;
			}
			if (!flag)
			{
				localBounds = new Bounds(vector, Vector3.zero);
				flag = true;
			}
			else
			{
				localBounds.Encapsulate(vector);
			}
		}
		return flag;
	}

	private static int CountMeshBackedRenderers(Transform root)
	{
		if ((Object)root == (Object)null)
		{
			return 0;
		}
		int num = 0;
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		Renderer[] array = componentsInChildren;
		foreach (Renderer renderer in array)
		{
			if (!((Object)renderer == (Object)null) && (Object)(object)GetRendererMesh(renderer) != (Object)null)
			{
				num++;
			}
		}
		return num;
	}

	private static void DisableNonVisualComponents(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		Component[] componentsInChildren = root.GetComponentsInChildren<Component>(true);
		foreach (Component val in componentsInChildren)
		{
			if ((Object)(object)val == (Object)null || val is Transform || val is MeshFilter || val is Renderer)
			{
				continue;
			}
			Behaviour val2 = (Behaviour)(object)((val is Behaviour) ? val : null);
			if (val2 != null)
			{
				val2.enabled = false;
				continue;
			}
			Collider val3 = (Collider)(object)((val is Collider) ? val : null);
			if (val3 != null)
			{
				val3.enabled = false;
				continue;
			}
			Rigidbody val4 = (Rigidbody)(object)((val is Rigidbody) ? val : null);
			if (val4 != null)
			{
				val4.isKinematic = true;
				val4.detectCollisions = false;
			}
		}
	}

	private static void PreparePrefabCloneRenderers(GameObject root, Transform targetVisualRoot)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			bool flag = (val.enabled = IsCloneWeaponRenderer(val));
			val.forceRenderingOff = !flag;
			ConfigureRuntimeWeaponRenderer(val);
			if (flag)
			{
				SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val2 != null)
				{
					val2.updateWhenOffscreen = true;
				}
				Material[] sharedMaterials = val.sharedMaterials;
				Mesh rendererMesh = GetRendererMesh(val);
				int subMeshCount = Math.Max((rendererMesh != null) ? rendererMesh.subMeshCount : 0, 1);
				val.sharedMaterials = BuildVisibleMaterials(sharedMaterials, ResolveTemplateMaterials(targetVisualRoot).ToArray(), subMeshCount);
			}
		}
	}

	private static bool IsCloneWeaponRenderer(Renderer renderer)
	{
		if ((Object)renderer == (Object)null || !IsMeshBackedRenderer(renderer))
		{
			return false;
		}
		string text = GetTransformPath(((Component)renderer).transform).ToLowerInvariant();
		if (text.Contains("/hand") || text.Contains("/arm") || text.Contains("finger") || text.Contains("holiday") || text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("muzzle"))
		{
			return false;
		}
		return true;
	}

	private static bool TryApplyInPlaceWeaponVisual(Item item, Transform targetVisualRoot, bool forceRefreshMarker)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		DestroyLegacyHeldVisual(item, restoreRenderers: true);
		CleanupSimpleAkVisual(item);
		if (forceRefreshMarker || (Object)FindInPlaceVisualRoot(item, targetVisualRoot) == (Object)null)
		{
			RestoreOriginalVisual(item);
		}
		if (!TryReplaceMeshOnExistingAnchor(item, targetVisualRoot, forceRefreshMarker, out var modelRoot, out var preferredRenderer))
		{
			return false;
		}
		if ((Object)modelRoot == (Object)null || (Object)preferredRenderer == (Object)null)
		{
			return false;
		}
		DisableSiblingWeaponRenderers(((Component)item).transform, modelRoot);
		if ((int)item.itemState == 1)
		{
			EnsureHeldVisibility(item, modelRoot, "in-place");
			EnsureHeldDebugSphere(modelRoot);
		}
		else
		{
			CleanupHeldDebugSphere(modelRoot);
			int layer = ResolveReferenceLayer(item, modelRoot);
			SetLayerRecursively(modelRoot, layer);
			SyncWeaponColliderState(item, modelRoot);
			LogWorldVisualState(item, modelRoot, "in-place-final", ((Component)item).transform);
		}
		InPlaceSwappedItemIds.Add(((Object)item).GetInstanceID());
		Plugin.LogDiagnosticOnce("ak-inplace-route:" + ((Object)item).GetInstanceID() + ":" + (int)item.itemState, $"Applied in-place weapon visual: {DescribeItemForDiagnostics(item)}, modelRoot={GetTransformPath(modelRoot)}, renderer={GetTransformPath(((Component)preferredRenderer).transform)}, state={(int)item.itemState}");
		return true;
	}

	private static Transform ResolveInPlaceVisualParent(Item item, Transform anchor)
	{
		if ((Object)item != (Object)null)
		{
			return ((Component)item).transform;
		}
		if ((Object)anchor != (Object)null)
		{
			return (((Object)(object)anchor.parent != (Object)null) ? anchor.parent : anchor);
		}
		return null;
	}

	private static Transform FindInPlaceVisualRoot(Item item, Transform anchor)
	{
		if ((Object)item != (Object)null)
		{
			Transform[] componentsInChildren = ((Component)item).GetComponentsInChildren<Transform>(true);
			Transform[] array = componentsInChildren;
			foreach (Transform val in array)
			{
				if (!((Object)val == (Object)null) && string.Equals(((Object)val).name, InPlaceVisualRootName, StringComparison.Ordinal))
				{
					return val;
				}
			}
		}
		if ((Object)anchor == (Object)null)
		{
			return null;
		}
		return anchor.Find(InPlaceVisualRootName);
	}

	private static void ApplyConfiguredInPlaceRootPose(Transform visualRoot)
	{
		if ((Object)visualRoot == (Object)null)
		{
			return;
		}
		visualRoot.localPosition = GetDirectAkPositionOverride();
		visualRoot.localRotation = GetDirectAkRotationOverride();
		visualRoot.localScale = Vector3.one;
	}

	private static void SyncInPlaceVisualRootToAnchor(Transform visualRoot, Transform stableParent, Transform anchor)
	{
		if ((Object)visualRoot == (Object)null)
		{
			return;
		}
		if ((Object)stableParent != (Object)null && (Object)(object)visualRoot.parent != (Object)(object)stableParent)
		{
			visualRoot.SetParent(stableParent, false);
		}
		if ((Object)stableParent == (Object)null || (Object)anchor == (Object)null)
		{
			ApplyConfiguredInPlaceRootPose(visualRoot);
			return;
		}
		Matrix4x4 matrix = stableParent.worldToLocalMatrix * anchor.localToWorldMatrix * Matrix4x4.TRS(GetDirectAkPositionOverride(), GetDirectAkRotationOverride(), Vector3.one);
		DecomposeMatrix(matrix, out var position, out var rotation, out var scale);
		visualRoot.localPosition = SanitizeLocalPosition(position, Vector3.zero);
		visualRoot.localRotation = SanitizeLocalRotation(rotation);
		visualRoot.localScale = SanitizeLocalScale(scale, Vector3.one);
	}

	private static bool TryCreateOrRefreshInPlaceVisualRoot(Item item, Transform anchor, bool forceRefreshMarker, out Transform modelRoot, out Renderer preferredRenderer)
	{
		modelRoot = null;
		preferredRenderer = null;
		if ((Object)item == (Object)null || (Object)anchor == (Object)null || !HasReplacementVisualSource(item))
		{
			return false;
		}
		GameObject akVisualPrefab = Plugin.GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			return false;
		}
		Transform stableParent = ResolveInPlaceVisualParent(item, anchor);
		if ((Object)stableParent == (Object)null)
		{
			return false;
		}
		Transform val = FindInPlaceVisualRoot(item, anchor);
		bool forceRefreshMarker2 = forceRefreshMarker;
		if ((Object)val != (Object)null && CountMeshBackedRenderers(val) == 0)
		{
			Object.Destroy((Object)(object)((Component)val).gameObject);
			val = null;
			forceRefreshMarker2 = true;
		}
		if (forceRefreshMarker2 && (Object)val != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)val).gameObject);
			val = null;
		}
		if ((Object)val == (Object)null)
		{
			GameObject val2 = Object.Instantiate<GameObject>(akVisualPrefab);
			if ((Object)val2 == (Object)null)
			{
				return false;
			}
			val2.transform.SetParent(stableParent, false);
			val2.SetActive(true);
			((Object)val2).name = InPlaceVisualRootName;
			StripNonVisualComponents(val2);
			PrepareRenderers(val2, anchor);
			val = val2.transform;
			Plugin.LogDiagnosticOnce("ak-inplace-create:" + ((Object)item).GetInstanceID() + ":" + (int)item.itemState, $"Created in-place visual root: {DescribeItemForDiagnostics(item)}, anchor={GetTransformPath(anchor)}, parent={GetTransformPath(stableParent)}, root={GetTransformPath(val)}, prefab={Plugin.DescribeAkVisualPrefabForDiagnostics()}");
		}
		else if ((Object)(object)val.parent != (Object)(object)stableParent)
		{
			val.SetParent(stableParent, false);
		}
		SetHierarchyActive(val, active: true);
		SyncInPlaceVisualRootToAnchor(val, stableParent, anchor);
		NormalizeVisualScaleAgainstReference(val, anchor, "in-place-root");
		ApplyConfiguredWorldVisualScale(val, Plugin.GetWeaponSelectionForItem(item));
		SetLayerRecursively(val, ((Component)stableParent).gameObject.layer);
		SetHierarchyActive(val, active: true);
		EnsureVisualRenderersActive(val, visible: true);
		preferredRenderer = SelectPreferredRenderer(((Component)val).GetComponentsInChildren<Renderer>(true), val);
		if ((Object)preferredRenderer == (Object)null)
		{
			return false;
		}
		EnsureMuzzleMarkerForItem(item, preferredRenderer);
		modelRoot = val;
		return true;
	}

	private static bool TryReplaceMeshOnExistingAnchor(Item item, Transform targetVisualRoot, bool forceRefreshMarker, out Transform modelRoot, out Renderer preferredRenderer)
	{
		modelRoot = targetVisualRoot;
		preferredRenderer = null;
		if ((Object)item == (Object)null || !HasReplacementVisualSource(item))
		{
			return false;
		}
		if (!TryResolveTargetMeshAnchor(item, targetVisualRoot, out var anchor, out var meshFilter, out var meshRenderer))
		{
			return false;
		}
		if ((Object)anchor == (Object)null || (Object)meshFilter == (Object)null || (Object)meshRenderer == (Object)null)
		{
			return false;
		}
		AkInPlaceMarker akInPlaceMarker = ((Component)anchor).GetComponent<AkInPlaceMarker>();
		if ((Object)akInPlaceMarker == (Object)null)
		{
			akInPlaceMarker = ((Component)anchor).gameObject.AddComponent<AkInPlaceMarker>();
		}
		if (!akInPlaceMarker.hasBaseTransform)
		{
			akInPlaceMarker.baseLocalPosition = anchor.localPosition;
			akInPlaceMarker.baseLocalRotation = anchor.localRotation;
			akInPlaceMarker.baseLocalScale = anchor.localScale;
			akInPlaceMarker.hasBaseTransform = true;
			akInPlaceMarker.hasBaseRotation = true;
		}
		if (!akInPlaceMarker.hasOriginalVisual)
		{
			akInPlaceMarker.originalMesh = meshFilter.sharedMesh;
			akInPlaceMarker.originalMaterials = (((Renderer)meshRenderer).sharedMaterials ?? Array.Empty<Material>()).ToArray();
			akInPlaceMarker.hasOriginalVisual = true;
		}
		anchor.localPosition = akInPlaceMarker.baseLocalPosition;
		anchor.localRotation = akInPlaceMarker.baseLocalRotation;
		anchor.localScale = akInPlaceMarker.baseLocalScale;
		((Component)anchor).gameObject.SetActive(true);
		if (!TryCreateOrRefreshInPlaceVisualRoot(item, anchor, forceRefreshMarker, out modelRoot, out preferredRenderer))
		{
			return false;
		}
		Plugin.LogDiagnosticOnce("ak-inplace-swap:" + ((Object)item).GetInstanceID() + ":" + (int)item.itemState, $"Applied in-place hierarchy visual: {DescribeItemForDiagnostics(item)}, anchor={GetTransformPath(anchor)}, root={GetTransformPath(modelRoot)}, preferred={GetTransformPath(((Component)preferredRenderer).transform)}, rootBounds={DescribeBoundsForDiagnostics(modelRoot)}");
		return true;
	}

	private static bool TryResolveTargetMeshAnchor(Item item, Transform targetVisualRoot, out Transform anchor, out MeshFilter meshFilter, out MeshRenderer meshRenderer)
	{
		anchor = targetVisualRoot;
		meshFilter = null;
		meshRenderer = null;
		if (TryResolveExactBlowgunAnchor(item, targetVisualRoot, out anchor, out meshFilter, out meshRenderer))
		{
			return true;
		}
		int bestScore = int.MinValue;
		HashSet<int> visited = new HashSet<int>();
		EvaluateTargetRendererCandidate(item?.mainRenderer, ref anchor, ref meshFilter, ref meshRenderer, ref bestScore, visited, 25000);
		if ((Object)targetVisualRoot != (Object)null)
		{
			EvaluateTargetMeshCandidate(((Component)targetVisualRoot).GetComponent<MeshFilter>(), ref anchor, ref meshFilter, ref meshRenderer, ref bestScore, visited);
			MeshFilter[] componentsInChildren = ((Component)targetVisualRoot).GetComponentsInChildren<MeshFilter>(true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				EvaluateTargetMeshCandidate(componentsInChildren[i], ref anchor, ref meshFilter, ref meshRenderer, ref bestScore, visited);
			}
		}
		if ((Object)item != (Object)null)
		{
			MeshFilter[] componentsInChildren = ((Component)item).GetComponentsInChildren<MeshFilter>(true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				EvaluateTargetMeshCandidate(componentsInChildren[i], ref anchor, ref meshFilter, ref meshRenderer, ref bestScore, visited);
			}
		}
		if ((Object)meshFilter != (Object)null)
		{
			return (Object)meshRenderer != (Object)null;
		}
		return false;
	}

	private static bool TryResolveExactBlowgunAnchor(Item item, Transform targetVisualRoot, out Transform anchor, out MeshFilter meshFilter, out MeshRenderer meshRenderer)
	{
		anchor = null;
		meshFilter = null;
		meshRenderer = null;
		Transform val = (((Object)item != (Object)null) ? ((Component)item).transform.Find("Blowgun") : null);
		if ((Object)val == (Object)null)
		{
			val = targetVisualRoot;
		}
		if ((Object)val == (Object)null)
		{
			return false;
		}
		meshFilter = ((Component)val).GetComponent<MeshFilter>();
		meshRenderer = ((Component)val).GetComponent<MeshRenderer>();
		if ((Object)meshFilter == (Object)null || (Object)meshRenderer == (Object)null)
		{
			Transform val2 = val.Find("Mesh");
			if ((Object)val2 != (Object)null)
			{
				meshFilter = ((Component)val2).GetComponent<MeshFilter>();
				meshRenderer = ((Component)val2).GetComponent<MeshRenderer>();
				if ((Object)meshFilter != (Object)null && (Object)meshRenderer != (Object)null)
				{
					val = val2;
				}
			}
		}
		if ((Object)meshFilter == (Object)null || (Object)meshRenderer == (Object)null || ShouldSkipTargetTransform(val))
		{
			return false;
		}
		anchor = val;
		return true;
	}

	private static void EvaluateTargetRendererCandidate(Renderer candidateRenderer, ref Transform anchor, ref MeshFilter meshFilter, ref MeshRenderer meshRenderer, ref int bestScore, HashSet<int> visited, int scoreBonus)
	{
		MeshRenderer val = (MeshRenderer)(object)((candidateRenderer is MeshRenderer) ? candidateRenderer : null);
		if (!((Object)val == (Object)null))
		{
			EvaluateTargetMeshCandidate(((Component)val).GetComponent<MeshFilter>(), ref anchor, ref meshFilter, ref meshRenderer, ref bestScore, visited, scoreBonus);
		}
	}

	private static void EvaluateTargetMeshCandidate(MeshFilter candidate, ref Transform anchor, ref MeshFilter meshFilter, ref MeshRenderer meshRenderer, ref int bestScore, HashSet<int> visited, int scoreBonus = 0)
	{
		if ((Object)candidate == (Object)null || (Object)candidate.sharedMesh == (Object)null || (visited != null && !visited.Add(((Object)candidate).GetInstanceID())))
		{
			return;
		}
		MeshRenderer component = ((Component)candidate).GetComponent<MeshRenderer>();
		if (!((Object)component == (Object)null) && !ShouldSkipTargetTransform(((Component)candidate).transform))
		{
			string text = ((((Object)candidate.sharedMesh).name ?? string.Empty) + "/" + (((Object)((Component)candidate).transform).name ?? string.Empty)).ToLowerInvariant();
			int num = candidate.sharedMesh.vertexCount;
			if (text.Contains("blowgun") || text.Contains("healing") || text.Contains("dart") || text.Contains("weapon"))
			{
				num += 900;
			}
			string text2 = GetTransformPath(((Component)candidate).transform).ToLowerInvariant();
			if (text2.Contains("/blowgun"))
			{
				num += 12000;
			}
			if (text2.EndsWith("/blowgun"))
			{
				num += 8000;
			}
			if (text.Contains("mesh") || text.Contains("body"))
			{
				num += 400;
			}
			if (text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("trigger") || text.Contains("collider"))
			{
				num -= 12000;
			}
			if (text.Contains("cube") && candidate.sharedMesh.vertexCount <= 200)
			{
				num -= 6000;
			}
			if (((Renderer)component).enabled)
			{
				num += 250;
			}
			num += scoreBonus;
			if (num > bestScore)
			{
				bestScore = num;
				anchor = ((Component)candidate).transform;
				meshFilter = candidate;
				meshRenderer = component;
			}
		}
	}

	private static bool ShouldSkipTargetTransform(Transform transform)
	{
		if ((Object)transform == (Object)null)
		{
			return true;
		}
		string text = (((Object)transform).name ?? string.Empty).ToLowerInvariant();
		if (text.Contains("hand") || text.Contains("arm") || text.Contains("finger"))
		{
			return true;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("trigger") || text.Contains("collider"))
		{
			return true;
		}
		return false;
	}

	private static bool TryGetBestSourceVisual(out SourceVisualCandidate best)
	{
		best = default(SourceVisualCandidate);
		GameObject akVisualPrefab = Plugin.GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			return false;
		}
		bool flag = false;
		int num = int.MinValue;
		bool flag2 = false;
		int num2 = int.MinValue;
		SourceVisualCandidate sourceVisualCandidate = default(SourceVisualCandidate);
		MeshFilter[] componentsInChildren = akVisualPrefab.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			MeshRenderer component = ((Component)val).GetComponent<MeshRenderer>();
			if ((Object)component == (Object)null)
			{
				continue;
			}
			SourceVisualCandidate? sourceVisualCandidate2 = BuildSourceVisualCandidate(((Component)val).transform, val.sharedMesh, ((Renderer)component).sharedMaterials, isSkinnedRenderer: false);
			if (sourceVisualCandidate2.HasValue)
			{
				SourceVisualCandidate value = sourceVisualCandidate2.Value;
				if (value.Score > num)
				{
					num = value.Score;
					best = value;
					flag = true;
				}
				if (!IsLikelyPlaceholderSource(value) && value.Score > num2)
				{
					num2 = value.Score;
					sourceVisualCandidate = value;
					flag2 = true;
				}
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = akVisualPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 == (Object)null || (Object)val2.sharedMesh == (Object)null)
			{
				continue;
			}
			SourceVisualCandidate? sourceVisualCandidate3 = BuildSourceVisualCandidate(((Component)val2).transform, val2.sharedMesh, ((Renderer)val2).sharedMaterials, isSkinnedRenderer: true);
			if (sourceVisualCandidate3.HasValue)
			{
				SourceVisualCandidate value2 = sourceVisualCandidate3.Value;
				if (value2.Score > num)
				{
					num = value2.Score;
					best = value2;
					flag = true;
				}
				if (!IsLikelyPlaceholderSource(value2) && value2.Score > num2)
				{
					num2 = value2.Score;
					sourceVisualCandidate = value2;
					flag2 = true;
				}
			}
		}
		if (flag && flag2 && IsLikelyPlaceholderSource(best) && sourceVisualCandidate.VertexCount > 64 && (sourceVisualCandidate.Score >= best.Score - 10000 || best.VertexCount <= 2000))
		{
			best = sourceVisualCandidate;
		}
		return flag;
	}

	private static SourceVisualCandidate? BuildSourceVisualCandidate(Transform sourceTransform, Mesh mesh, Material[] materials, bool isSkinnedRenderer)
	{
		if ((Object)sourceTransform == (Object)null || (Object)mesh == (Object)null)
		{
			return null;
		}
		int vertexCount = mesh.vertexCount;
		int num = vertexCount;
		string text = (((Object)mesh).name ?? string.Empty).ToLowerInvariant();
		string text2 = (((Object)sourceTransform).name ?? string.Empty).ToLowerInvariant();
		Material[] array = NormalizeMaterialArray(materials, Math.Max(mesh.subMeshCount, 1));
		if (materials == null || materials.Length == 0)
		{
			num -= 2000;
		}
		num = ((!HasUsableTexture(array)) ? (num - 12000) : (num + 1800));
		if (vertexCount <= 24)
		{
			num -= 6000;
		}
		if (text.Contains("cube") || text2.Contains("cube"))
		{
			num -= 12000;
		}
		if (text.Contains("plane") || text2.Contains("plane") || text.Contains("quad") || text2.Contains("quad"))
		{
			num -= 8000;
		}
		if (text2.Contains("vfx") || text2.Contains("effect") || text2.Contains("spawn"))
		{
			num -= 5000;
		}
		if (text.Contains("hand") || text2.Contains("hand") || text.Contains("arm") || text2.Contains("arm") || text.Contains("finger") || text2.Contains("finger"))
		{
			num -= 18000;
		}
		if (isSkinnedRenderer)
		{
			num -= 3000;
		}
		if (text.Contains("ak") || text2.Contains("ak") || text.Contains("weapon") || text2.Contains("weapon") || text.Contains("rifle") || text2.Contains("rifle") || text2.Contains("gun"))
		{
			num += 2500;
		}
		if (text == "mesh" || text2 == "mesh")
		{
			num += 5000;
		}
		return new SourceVisualCandidate
		{
			Mesh = mesh,
			Materials = ((array != null) ? array : Array.Empty<Material>()),
			Transform = sourceTransform,
			VertexCount = vertexCount,
			Score = num,
			Path = GetTransformPath(sourceTransform),
			MeshName = ((Object)mesh).name
		};
	}

	private static bool IsLikelyPlaceholderSource(SourceVisualCandidate candidate)
	{
		string text = (candidate.MeshName ?? string.Empty).ToLowerInvariant();
		string text2 = (candidate.Path ?? string.Empty).ToLowerInvariant();
		if ((text.Contains("cube") || text2.Contains("/cube")) && candidate.VertexCount <= 3000)
		{
			return true;
		}
		if ((text.Contains("plane") || text.Contains("quad") || text2.Contains("/plane") || text2.Contains("/quad")) && candidate.VertexCount <= 3000)
		{
			return true;
		}
		return false;
	}

	private static void DisableSiblingWeaponRenderers(Transform targetVisualRoot, Transform preservedAnchor)
	{
		if ((Object)targetVisualRoot == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)targetVisualRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && (!((Object)preservedAnchor != (Object)null) || (!((Object)(object)((Component)val).transform == (Object)(object)preservedAnchor) && !((Component)val).transform.IsChildOf(preservedAnchor))) && IsOriginalWeaponRenderer(val, targetVisualRoot))
			{
				val.enabled = false;
				val.forceRenderingOff = true;
			}
		}
	}

	private static void EnsureHeldVisibility(Item item, Transform modelRoot, string source, bool preserveExistingLayer = false)
	{
		if ((Object)item == (Object)null || (int)item.itemState != 1)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
		if ((Object)modelRoot != (Object)null)
		{
			SetItemNonHandRenderersEnabled(item, enabled: false, modelRoot);
			EnsureVisualRenderersActive(modelRoot, visible: true);
		}
		List<MeshRenderer> list = new List<MeshRenderer>();
		List<SkinnedMeshRenderer> list2 = new List<SkinnedMeshRenderer>();
		int num = 0;
		int num2 = 0;
		Renderer[] array = componentsInChildren;
		foreach (Renderer item2 in array)
		{
			if ((Object)item2 == (Object)null)
			{
				continue;
			}
			bool flag = IsHandItemRenderer(item2);
			bool flag2 = (Object)modelRoot != (Object)null && (((Component)item2).transform == modelRoot || ((Component)item2).transform.IsChildOf(modelRoot));
			if (flag2 || flag)
			{
				item2.enabled = true;
				item2.forceRenderingOff = false;
				item2.allowOcclusionWhenDynamic = false;
				SkinnedMeshRenderer val4 = (SkinnedMeshRenderer)(object)((item2 is SkinnedMeshRenderer) ? item2 : null);
				if (val4 != null)
				{
					val4.updateWhenOffscreen = true;
					list2.Add(val4);
				}
				else
				{
					MeshRenderer val5 = (MeshRenderer)(object)((item2 is MeshRenderer) ? item2 : null);
					if (val5 != null)
					{
						list.Add(val5);
					}
				}
				num++;
			}
			else
			{
				item2.enabled = false;
				item2.forceRenderingOff = true;
				item2.allowOcclusionWhenDynamic = false;
				num2++;
			}
		}
		if (list.Count == 0 && list2.Count == 0)
		{
			list = (from r in ((Component)item).GetComponentsInChildren<MeshRenderer>(true)
				where (Object)r != (Object)null
				select r).ToList();
			list2 = (from r in ((Component)item).GetComponentsInChildren<SkinnedMeshRenderer>(true)
				where (Object)r != (Object)null
				select r).ToList();
			foreach (Renderer item3 in list.Cast<Renderer>().Concat((IEnumerable<Renderer>)list2))
			{
				item3.enabled = true;
				item3.forceRenderingOff = false;
				item3.allowOcclusionWhenDynamic = false;
			}
		}
		Renderer val = SelectPreferredRenderer(list.Cast<Renderer>().Concat((IEnumerable<Renderer>)list2), modelRoot);
		if ((Object)val == (Object)null)
		{
			val = list.Cast<Renderer>().FirstOrDefault() ?? list2.Cast<Renderer>().FirstOrDefault();
		}
		if ((Object)val == (Object)null)
		{
			return;
		}
		MeshRenderer val2 = (MeshRenderer)(object)((val is MeshRenderer) ? val : null);
		if (val2 != null)
		{
			list.Remove(val2);
			list.Insert(0, val2);
		}
		else
		{
			SkinnedMeshRenderer val3 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
			if (val3 != null)
			{
				list2.Remove(val3);
				list2.Insert(0, val3);
			}
		}
		if ((Object)modelRoot != (Object)null && !preserveExistingLayer)
		{
			int layer = ResolveReferenceLayer(item, modelRoot);
			if (IsLocallyHeldByPlayer(item))
			{
				layer = ResolveBestVisibleLayerForLocalHeldItem(item, modelRoot);
			}
			SetLayerRecursively(modelRoot, layer);
		}
		Plugin.LogDiagnosticOnce("ak-held-visibility:" + ((Object)item).GetInstanceID() + ":" + source, $"EnsureHeldVisibility applied: {DescribeItemForDiagnostics(item)}, source={source}, modelRoot={GetTransformPath(modelRoot)}, enabledCount={num}, disabledCount={num2}, preferred={GetTransformPath(((Component)val).transform)}, modelBounds={DescribeBoundsForDiagnostics(modelRoot)}");
		BindItemRendererFields(item, list.ToArray(), list2.ToArray());
		EnsureMuzzleMarkerForItem(item, val);
	}

	private static void BindInPlaceRendererFields(Item item, MeshRenderer preferredRenderer, Transform modelRoot)
	{
		if (!((Object)item == (Object)null) && !((Object)preferredRenderer == (Object)null))
		{
			List<MeshRenderer> list = (from r in ((Component)item).GetComponentsInChildren<MeshRenderer>(true)
				where (Object)r != (Object)null && (((Renderer)r).enabled || (Object)r == (Object)preferredRenderer || IsHandItemRenderer((Renderer)(object)r) || ((Object)modelRoot != (Object)null && ((Component)r).transform.IsChildOf(modelRoot)))
				select r).ToList();
			List<SkinnedMeshRenderer> list2 = (from r in ((Component)item).GetComponentsInChildren<SkinnedMeshRenderer>(true)
				where (Object)r != (Object)null && (((Renderer)r).enabled || IsHandItemRenderer((Renderer)(object)r) || ((Object)modelRoot != (Object)null && ((Component)r).transform.IsChildOf(modelRoot)))
				select r).ToList();
			list.Remove(preferredRenderer);
			list.Insert(0, preferredRenderer);
			BindItemRendererFields(item, list.ToArray(), list2.ToArray());
		}
	}

	private static void EnsureLegacyHeldVisual(Item item, bool forceRefreshMarker, bool allowWhenSeparateLocalVisual = false)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		if (ShouldUseSeparateLocalFirstPersonVisual(item) && !allowWhenSeparateLocalVisual)
		{
			DestroyLegacyHeldVisual(item, restoreRenderers: false);
			return;
		}
		if (!IsLocallyHeldByPlayer(item))
		{
			DestroyLegacyHeldVisual(item, restoreRenderers: true);
			return;
		}
		AkHeldLegacyVisualMarker akHeldLegacyVisualMarker = ((Component)item).GetComponentInChildren<AkHeldLegacyVisualMarker>(true);
		if (forceRefreshMarker || (Object)akHeldLegacyVisualMarker == (Object)null)
		{
			DestroyLegacyHeldVisual(item, restoreRenderers: false);
			akHeldLegacyVisualMarker = CreateLegacyHeldVisual(item);
		}
		if (!((Object)akHeldLegacyVisualMarker == (Object)null))
		{
			Transform transform = ((Component)akHeldLegacyVisualMarker).transform;
			EnsureLegacyHeldRenderersEnabled(transform);
			Renderer legacyHeldPreferredRenderer = GetLegacyHeldPreferredRenderer(transform);
			if (!((Object)legacyHeldPreferredRenderer == (Object)null))
			{
				int num = ResolveReferenceLayer(item, transform);
				num = ResolveBestVisibleLayerForLocalHeldItem(item, transform);
				SetLayerRecursively(transform, num);
				SetItemNonHandRenderersEnabled(item, enabled: false, transform);
				SyncWeaponColliderState(item, null);
				BindLegacyHeldRendererFields(item, legacyHeldPreferredRenderer, transform);
				EnsureMuzzleMarkerForItem(item, legacyHeldPreferredRenderer);
				EnsureHeldDebugSphere(transform);
				LogHeldVisualCameraState(item, transform, "ensure");
			}
		}
	}

	private static AkHeldLegacyVisualMarker CreateLegacyHeldVisual(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		Transform transform = ResolveHeldLegacyParent(item);
		if ((Object)transform == (Object)null)
		{
			transform = ((Component)item).transform;
		}
		Transform val = CreatePreparedAkClone(transform, transform, "AK47_HeldLegacyModel");
		if ((Object)val != (Object)null)
		{
			ApplyPreparedLegacyHeldClonePose(val);
			((Component)val).gameObject.SetActive(true);
			EnsureVisualRenderersActive(val, visible: true);
			AkHeldLegacyVisualMarker akHeldLegacyVisualMarker2 = ((Component)val).GetComponent<AkHeldLegacyVisualMarker>();
			if ((Object)akHeldLegacyVisualMarker2 == (Object)null)
			{
				akHeldLegacyVisualMarker2 = ((Component)val).gameObject.AddComponent<AkHeldLegacyVisualMarker>();
			}
			Plugin.LogDiagnosticOnce("ak-held-legacy-create:" + ((Object)item).GetInstanceID(), $"Created held legacy visual from prepared clone: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(transform)}, visual={GetTransformPath(val)}, bounds={DescribeBoundsForDiagnostics(val)}, renderers={DescribeRendererCollectionForDiagnostics(((Component)val).GetComponentsInChildren<Renderer>(true))}");
			LogHeldVisualCameraState(item, val, "create");
			return akHeldLegacyVisualMarker2;
		}
		Transform targetVisualRoot = ResolveHeldLegacyParent(item);
		if ((Object)targetVisualRoot == (Object)null)
		{
			targetVisualRoot = ResolveTargetVisualRoot(item);
		}
		AkHeldLegacyVisualMarker akHeldLegacyVisualMarker = CreateAnchoredLegacyHeldVisual(item, targetVisualRoot);
		if ((Object)akHeldLegacyVisualMarker != (Object)null)
		{
			Plugin.LogDiagnosticOnce("ak-held-legacy-create:" + ((Object)item).GetInstanceID(), $"Created held legacy visual from anchored fallback: {DescribeItemForDiagnostics(item)}, visual={GetTransformPath(((Component)akHeldLegacyVisualMarker).transform)}, bounds={DescribeBoundsForDiagnostics(((Component)akHeldLegacyVisualMarker).transform)}");
			LogHeldVisualCameraState(item, ((Component)akHeldLegacyVisualMarker).transform, "fallback");
			return akHeldLegacyVisualMarker;
		}
		if (!TryResolveLegacyHeldSource(out var mesh, out var materials, out var localScale, out var localRotation, out var localPosition, out var _))
		{
			return null;
		}
		Transform transform2 = ResolveHeldLegacyParent(item);
		if ((Object)transform2 == (Object)null)
		{
			transform2 = ((Component)item).transform;
		}
		GameObject val2 = new GameObject("AK47_HeldLegacyModel");
		val2.transform.SetParent(transform2, false);
		val2.transform.localScale = localScale;
		val2.transform.localRotation = localRotation;
		val2.transform.localPosition = localPosition;
		AkHeldLegacyVisualMarker result = val2.AddComponent<AkHeldLegacyVisualMarker>();
		MeshFilter val3 = val2.AddComponent<MeshFilter>();
		MeshRenderer obj = val2.AddComponent<MeshRenderer>();
		val3.sharedMesh = mesh;
		((Renderer)obj).sharedMaterials = BuildAkVisibleMaterials(materials, Mathf.Max(mesh.subMeshCount, 1));
		NormalizeAkRenderer((Renderer)(object)obj);
		Plugin.LogDiagnosticOnce("ak-held-legacy-create:" + ((Object)item).GetInstanceID(), $"Created held legacy visual from mesh fallback: {DescribeItemForDiagnostics(item)}, parent={GetTransformPath(transform2)}, visual={GetTransformPath(val2.transform)}, pos={localPosition}, rot={localRotation.eulerAngles}, scale={localScale}");
		LogHeldVisualCameraState(item, val2.transform, "mesh-fallback");
		return result;
	}

	private static void ApplyPreparedLegacyHeldClonePose(Transform legacyRoot)
	{
		if ((Object)legacyRoot == (Object)null)
		{
			return;
		}
		Vector3 localPosition = legacyRoot.localPosition;
		Quaternion localRotation = legacyRoot.localRotation;
		Vector3 localScale = legacyRoot.localScale;
		if (TryResolveHeldPoseRelativeToBase(out var localPosition2, out var localRotation2, out var localScale2))
		{
			legacyRoot.localPosition = SanitizeLocalPosition(localPosition + localPosition2, localPosition);
			legacyRoot.localRotation = SanitizeLocalRotation(localRotation * localRotation2);
			legacyRoot.localScale = SanitizeLocalScale(Vector3.Scale(localScale, localScale2), localScale);
			Plugin.LogDiagnosticOnce("ak-held-legacy-pose:" + GetTransformPath(legacyRoot), $"Applied prepared held pose delta: root={GetTransformPath(legacyRoot)}, basePos={localPosition}, baseRot={localRotation.eulerAngles}, baseScale={localScale}, deltaPos={localPosition2}, deltaRot={localRotation2.eulerAngles}, deltaScale={localScale2}, finalPos={legacyRoot.localPosition}, finalRot={legacyRoot.localRotation.eulerAngles}, finalScale={legacyRoot.localScale}");
			return;
		}
		Plugin.LogDiagnosticOnce("ak-held-legacy-pose:" + GetTransformPath(legacyRoot), $"Held pose delta unavailable, keeping prepared clone pose: root={GetTransformPath(legacyRoot)}, pos={localPosition}, rot={localRotation.eulerAngles}, scale={localScale}");
	}

	private static AkHeldLegacyVisualMarker CreateAnchoredLegacyHeldVisual(Item item, Transform targetVisualRoot)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		if (!TryResolveBaseAkVisual(out var mesh, out var materials, out var _, out var _, out var _, out var _) || (Object)mesh == (Object)null)
		{
			return null;
		}
		if (!TryResolveTargetMeshAnchor(item, targetVisualRoot, out var anchor, out var _, out var meshRenderer) || (Object)anchor == (Object)null)
		{
			return null;
		}
		GameObject val = new GameObject("AK47_HeldLegacyModel");
		val.transform.SetParent(anchor, false);
		if (TryResolveHeldPoseRelativeToBase(out var localPosition2, out var localRotation2, out var localScale2))
		{
			val.transform.localPosition = localPosition2;
			val.transform.localRotation = localRotation2;
			val.transform.localScale = localScale2;
		}
		else
		{
			val.transform.localPosition = GetDirectAkPositionOverride();
			val.transform.localRotation = HeldLegacyRotationOffset;
			val.transform.localScale = Vector3.one * Mathf.Max(1f, GetDirectAkScaleMultiplier(Plugin.GetWeaponSelectionForItem(item)));
		}
		AkHeldLegacyVisualMarker result = val.AddComponent<AkHeldLegacyVisualMarker>();
		MeshFilter val2 = val.AddComponent<MeshFilter>();
		MeshRenderer obj = val.AddComponent<MeshRenderer>();
		val2.sharedMesh = mesh;
		((Renderer)obj).sharedMaterials = BuildVisibleMaterials(materials, ((Object)meshRenderer != (Object)null) ? ((Renderer)meshRenderer).sharedMaterials : null, Mathf.Max(mesh.subMeshCount, 1));
		NormalizeAkRenderer((Renderer)(object)obj);
		SetLayerRecursively(val.transform, ((Component)anchor).gameObject.layer);
		return result;
	}

	private static void ApplyLegacyHeldFallbackPose(Transform legacyRoot)
	{
		if (!((Object)legacyRoot == (Object)null) && TryResolvePreferredAkVisual(out var _, out var _, out var localScale, out var localRotation, out var localPosition, out var _))
		{
			legacyRoot.localPosition = localPosition;
			legacyRoot.localRotation = localRotation;
			legacyRoot.localScale = localScale;
		}
	}

	private static bool TryResolveLegacyHeldSource(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition, out string debugInfo)
	{
		return TryResolvePreferredAkVisual(out mesh, out materials, out localScale, out localRotation, out localPosition, out debugInfo);
	}

	private static Renderer GetLegacyHeldPreferredRenderer(Transform legacyRoot)
	{
		if ((Object)legacyRoot == (Object)null)
		{
			return null;
		}
		Renderer[] componentsInChildren = ((Component)legacyRoot).GetComponentsInChildren<Renderer>(true);
		return SelectPreferredRenderer(componentsInChildren, legacyRoot) ?? componentsInChildren.FirstOrDefault((Renderer r) => IsMeshBackedRenderer(r));
	}

	private static void EnsureLegacyHeldRenderersEnabled(Transform legacyRoot)
	{
		if ((Object)legacyRoot == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)legacyRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			bool flag = (val.enabled = IsMeshBackedRenderer(val));
			val.forceRenderingOff = !flag;
			ConfigureRuntimeWeaponRenderer(val);
			if (flag)
			{
				SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val2 != null)
				{
					val2.updateWhenOffscreen = true;
				}
				NormalizeRendererMaterials(val);
			}
		}
	}

	private static void BindLegacyHeldRendererFields(Item item, Renderer legacyRenderer, Transform legacyRoot)
	{
		if ((Object)item == (Object)null || (Object)legacyRenderer == (Object)null)
		{
			return;
		}
		List<MeshRenderer> list = (from r in ((Component)item).GetComponentsInChildren<MeshRenderer>(true)
			where (Object)r != (Object)null && (((Renderer)r).enabled || (Object)r == (Object)legacyRenderer || IsHandItemRenderer((Renderer)(object)r) || ((Object)legacyRoot != (Object)null && ((Component)r).transform.IsChildOf(legacyRoot)))
			select r).ToList();
		List<SkinnedMeshRenderer> list2 = (from r in ((Component)item).GetComponentsInChildren<SkinnedMeshRenderer>(true)
			where (Object)r != (Object)null && (((Renderer)r).enabled || (Object)r == (Object)legacyRenderer || IsHandItemRenderer((Renderer)(object)r) || ((Object)legacyRoot != (Object)null && ((Component)r).transform.IsChildOf(legacyRoot)))
			select r).ToList();
		Renderer obj = legacyRenderer;
		MeshRenderer val = (MeshRenderer)(object)((obj is MeshRenderer) ? obj : null);
		if (val != null)
		{
			list.Remove(val);
			list.Insert(0, val);
		}
		else
		{
			Renderer obj2 = legacyRenderer;
			SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((obj2 is SkinnedMeshRenderer) ? obj2 : null);
			if (val2 != null)
			{
				list2.Remove(val2);
				list2.Insert(0, val2);
			}
		}
		BindItemRendererFields(item, list.ToArray(), list2.ToArray());
	}

	private static void BindSimpleVisualRendererFields(Item item, Transform modelRoot)
	{
		if ((Object)item == (Object)null || (Object)modelRoot == (Object)null)
		{
			return;
		}
		List<MeshRenderer> list = (from r in ((Component)item).GetComponentsInChildren<MeshRenderer>(true)
			where (Object)r != (Object)null && (((Renderer)r).enabled || IsHandItemRenderer((Renderer)(object)r) || ((Component)r).transform.IsChildOf(modelRoot))
			select r).ToList();
		List<SkinnedMeshRenderer> list2 = (from r in ((Component)item).GetComponentsInChildren<SkinnedMeshRenderer>(true)
			where (Object)r != (Object)null && (((Renderer)r).enabled || IsHandItemRenderer((Renderer)(object)r) || ((Component)r).transform.IsChildOf(modelRoot))
			select r).ToList();
		Renderer renderer = SelectPreferredRenderer(list.Cast<Renderer>().Concat((IEnumerable<Renderer>)list2), modelRoot);
		MeshRenderer val = (MeshRenderer)(object)((renderer is MeshRenderer) ? renderer : null);
		if (val != null)
		{
			list.Remove(val);
			list.Insert(0, val);
		}
		else
		{
			SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((renderer is SkinnedMeshRenderer) ? renderer : null);
			if (val2 != null)
			{
				list2.Remove(val2);
				list2.Insert(0, val2);
			}
		}
		BindItemRendererFields(item, list.ToArray(), list2.ToArray());
	}

	private static void SetItemNonHandRenderersEnabled(Item item, bool enabled, Transform excludedRoot)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && (!((Object)excludedRoot != (Object)null) || !((Component)val).transform.IsChildOf(excludedRoot)) && !IsHandItemRenderer(val))
			{
				val.enabled = enabled;
				val.forceRenderingOff = !enabled;
				val.allowOcclusionWhenDynamic = false;
				SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val2 != null)
				{
					val2.updateWhenOffscreen = true;
				}
			}
		}
	}

	private static bool IsHandItemRenderer(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return false;
		}
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + GetTransformPath(((Component)renderer).transform)).ToLowerInvariant();
		if (!text.Contains("hand") && !text.Contains("arm"))
		{
			return text.Contains("finger");
		}
		return true;
	}

	private static void DestroyLegacyHeldVisual(Item item, bool restoreRenderers)
	{
		if (!((Object)item == (Object)null))
		{
			AkHeldLegacyVisualMarker componentInChildren = ((Component)item).GetComponentInChildren<AkHeldLegacyVisualMarker>(true);
			if ((Object)componentInChildren != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)componentInChildren).gameObject);
			}
			if (restoreRenderers)
			{
				SetItemNonHandRenderersEnabled(item, enabled: true, null);
				SetOriginalItemCollidersEnabled(item, enabled: true);
			}
			CleanupAllWeaponWorldColliders(item);
		}
	}

	private static void BindOriginalItemRendererFields(Item item)
	{
		if (!((Object)item == (Object)null))
		{
			MeshRenderer[] array = (from r in ((Component)item).GetComponentsInChildren<MeshRenderer>(true)
				where (Object)r != (Object)null && !IsCustomAkRenderer((Renderer)(object)r)
				select r).ToArray();
			SkinnedMeshRenderer[] array2 = (from r in ((Component)item).GetComponentsInChildren<SkinnedMeshRenderer>(true)
				where (Object)r != (Object)null && !IsCustomAkRenderer((Renderer)(object)r)
				select r).ToArray();
			if ((array != null && array.Length != 0) || (array2 != null && array2.Length != 0))
			{
				BindItemRendererFields(item, array, array2);
			}
		}
	}

	private static bool IsCustomAkRenderer(Renderer renderer)
	{
		if ((Object)renderer != (Object)null)
		{
			if (!((Object)((Component)renderer).GetComponentInParent<AkSimpleVisualMarker>(true) != (Object)null) && !((Object)((Component)renderer).GetComponentInParent<AkLocalVisualMarker>(true) != (Object)null))
			{
				if ((Object)((Component)renderer).GetComponentInParent<AkHeldLegacyVisualMarker>(true) != (Object)null)
				{
					return true;
				}
				return HasNamedAncestor(((Component)renderer).transform, InPlaceVisualRootName);
			}
			return true;
		}
		return false;
	}

	private static bool HasNamedAncestor(Transform transform, string ancestorName)
	{
		while ((Object)transform != (Object)null)
		{
			if (string.Equals(((Object)transform).name, ancestorName, StringComparison.Ordinal))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	private static void EnsureMuzzleMarkerForItem(Item item, Renderer preferredRenderer)
	{
		if (!((Object)item == (Object)null))
		{
			Transform val = ((Component)item).transform.Find("AK_MuzzleMarker");
			if ((Object)val == (Object)null)
			{
				GameObject val2 = new GameObject("AK_MuzzleMarker");
				val2.transform.SetParent(((Component)item).transform, false);
				val2.AddComponent<AkMuzzleMarker>();
				val = val2.transform;
			}
			if ((Object)preferredRenderer == (Object)null)
			{
				val.localPosition = Vector3.forward * 0.6f;
				val.localRotation = Quaternion.identity;
			}
			else
			{
				Bounds bounds = preferredRenderer.bounds;
				val.position = bounds.center + ((Component)preferredRenderer).transform.forward * Mathf.Max(bounds.extents.z, 0.06f);
				val.rotation = ((Component)preferredRenderer).transform.rotation;
			}
		}
	}

	private static Material[] BuildVisibleMaterials(Material[] preferredMaterials, Material[] fallbackMaterials, int subMeshCount)
	{
		if (subMeshCount <= 0)
		{
			return Array.Empty<Material>();
		}
		if (preferredMaterials != null && preferredMaterials.Length != 0 && HasUsableTexture(preferredMaterials))
		{
			return BuildDirectAkMaterials(preferredMaterials, subMeshCount);
		}
		Material[] array = preferredMaterials;
		if (array == null || array.Length == 0 || array.All((Material m) => (Object)m == (Object)null))
		{
			array = fallbackMaterials;
		}
		if (array == null || array.Length == 0 || array.All((Material m) => (Object)m == (Object)null))
		{
			Material[] array2 = (Material[])(object)new Material[subMeshCount];
			for (int num = 0; num < subMeshCount; num++)
			{
				array2[num] = CreateRuntimeMaterial(null, null);
				NormalizeMaterialForVisibility(array2[num]);
			}
			return array2;
		}
		Material[] array3 = NormalizeMaterialArray(array, subMeshCount);
		Material[] array4 = (Material[])(object)new Material[subMeshCount];
		Material val = array3.FirstOrDefault((Material m) => (Object)m != (Object)null);
		for (int num2 = 0; num2 < subMeshCount; num2++)
		{
			Material val2 = ((num2 < array3.Length) ? array3[num2] : null);
			if ((Object)val2 == (Object)null)
			{
				val2 = val;
			}
			Material template = ((fallbackMaterials != null && fallbackMaterials.Length != 0) ? fallbackMaterials[Math.Min(num2, fallbackMaterials.Length - 1)] : null);
			array4[num2] = CreateRuntimeMaterial(val2, template);
			NormalizeMaterialForVisibility(array4[num2]);
		}
		return array4;
	}

	private static Material[] NormalizeMaterialArray(Material[] sourceMaterials, int subMeshCount)
	{
		if (sourceMaterials == null || sourceMaterials.Length == 0 || subMeshCount <= 0)
		{
			return sourceMaterials ?? Array.Empty<Material>();
		}
		if (sourceMaterials.Length == subMeshCount)
		{
			return sourceMaterials;
		}
		Material[] array = (Material[])(object)new Material[subMeshCount];
		for (int i = 0; i < subMeshCount; i++)
		{
			array[i] = sourceMaterials[Math.Min(i, sourceMaterials.Length - 1)];
		}
		return array;
	}

	private static void NormalizeMaterialForVisibility(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return;
		}
		Shader val = ResolvePreferredVisibleShader();
		Texture val2 = ResolvePrimaryTexture(material);
		if ((Object)val2 == (Object)null && material.HasProperty("_BaseMap"))
		{
			val2 = material.GetTexture("_BaseMap");
		}
		if ((Object)val2 == (Object)null && material.HasProperty("_MainTex"))
		{
			val2 = material.GetTexture("_MainTex");
		}
		string text = (((Object)material.shader != (Object)null) ? ((Object)material.shader).name : string.Empty);
		bool flag = (Object)material.shader == (Object)null || !material.shader.isSupported || string.IsNullOrWhiteSpace(text);
		if (!flag && (text.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			flag = true;
		}
		if (flag && (Object)val != (Object)null)
		{
			material.shader = val;
			if ((Object)val2 != (Object)null)
			{
				if (material.HasProperty("_BaseMap"))
				{
					material.SetTexture("_BaseMap", val2);
				}
				if (material.HasProperty("_MainTex"))
				{
					material.SetTexture("_MainTex", val2);
				}
			}
		}
		if (material.HasProperty("_Surface"))
		{
			material.SetFloat("_Surface", 0f);
		}
		if (material.HasProperty("_AlphaClip"))
		{
			material.SetFloat("_AlphaClip", 0f);
		}
		if (material.HasProperty("_Cutoff"))
		{
			material.SetFloat("_Cutoff", 0f);
		}
		if (material.HasProperty("_Cull"))
		{
			material.SetInt("_Cull", 0);
		}
		if (material.HasProperty("_BaseColor"))
		{
			Color color = material.GetColor("_BaseColor");
			color.a = 1f;
			color.r = Mathf.Max(color.r, 0.85f);
			color.g = Mathf.Max(color.g, 0.85f);
			color.b = Mathf.Max(color.b, 0.85f);
			material.SetColor("_BaseColor", color);
		}
		if (material.HasProperty("_Color"))
		{
			Color color2 = material.color;
			color2.a = 1f;
			color2.r = Mathf.Max(color2.r, 0.85f);
			color2.g = Mathf.Max(color2.g, 0.85f);
			color2.b = Mathf.Max(color2.b, 0.85f);
			material.color = color2;
		}
		if (material.HasProperty("_EmissionColor"))
		{
			material.EnableKeyword("_EMISSION");
			material.SetColor("_EmissionColor", new Color(0.1f, 0.1f, 0.1f, 1f));
		}
		material.renderQueue = -1;
	}

	private static Shader ResolvePreferredVisibleShader()
	{
		return Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("W/Peak_Standard") ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
	}

	private static void NormalizeRendererMaterials(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return;
		}
		int num = 0;
		if (renderer is MeshRenderer)
		{
			MeshFilter component = ((Component)renderer).GetComponent<MeshFilter>();
			num = (((Object)component != (Object)null && (Object)component.sharedMesh != (Object)null) ? component.sharedMesh.subMeshCount : 0);
		}
		else
		{
			SkinnedMeshRenderer val = (SkinnedMeshRenderer)(object)((renderer is SkinnedMeshRenderer) ? renderer : null);
			if (val != null)
			{
				num = (((Object)val.sharedMesh != (Object)null) ? val.sharedMesh.subMeshCount : 0);
			}
		}
		if (num <= 0)
		{
			return;
		}
		Material[] array = renderer.sharedMaterials;
		if (array == null || array.Length == 0)
		{
			renderer.sharedMaterials = BuildVisibleMaterials(null, null, num);
			return;
		}
		if (array.Length > num)
		{
			Array.Resize(ref array, num);
		}
		if (array.Length < num)
		{
			array = NormalizeMaterialArray(array, num);
		}
		for (int i = 0; i < array.Length; i++)
		{
			if ((Object)array[i] == (Object)null)
			{
				array[i] = CreateRuntimeMaterial(null, null);
			}
			NormalizeMaterialForVisibility(array[i]);
		}
		renderer.sharedMaterials = array;
	}

	private static void BindItemRendererFields(Item item, MeshRenderer[] meshRenderers, SkinnedMeshRenderer[] skinnedRenderers)
	{
		List<Renderer> list = new List<Renderer>(((meshRenderers != null) ? meshRenderers.Length : 0) + ((skinnedRenderers != null) ? skinnedRenderers.Length : 0));
		if (meshRenderers != null)
		{
			list.AddRange((IEnumerable<Renderer>)meshRenderers.Where((MeshRenderer r) => (Object)r != (Object)null));
		}
		if (skinnedRenderers != null)
		{
			list.AddRange((IEnumerable<Renderer>)skinnedRenderers.Where((SkinnedMeshRenderer r) => (Object)r != (Object)null));
		}
		Renderer val = SelectPreferredRenderer(list, null) ?? list.FirstOrDefault();
		if (!((Object)val == (Object)null))
		{
			Type type = ((object)item).GetType();
			if (SetMember(item, type, MainRendererFieldNames, MainRendererPropertyNames, val))
			{
				list.Remove(val);
				list.Insert(0, val);
				SetMember(item, type, AddtlRendererFieldNames, null, list.ToArray());
			}
		}
	}

	private static Renderer SelectPreferredRenderer(IEnumerable<Renderer> renderers, Transform modelRoot)
	{
		Renderer val = null;
		int num = int.MinValue;
		Renderer val2 = null;
		int num2 = int.MinValue;
		foreach (Renderer item in renderers ?? Enumerable.Empty<Renderer>())
		{
			if ((Object)item == (Object)null || ((Object)modelRoot != (Object)null && !((Component)item).transform.IsChildOf(modelRoot)) || !IsMeshBackedRenderer(item))
			{
				continue;
			}
			int num3 = ScoreRendererCandidate(item);
			if (IsPreferredMainRendererCandidate(item))
			{
				if (num3 > num)
				{
					num = num3;
					val = item;
				}
			}
			else if (num3 > num2)
			{
				num2 = num3;
				val2 = item;
			}
		}
		return val ?? val2;
	}

	private static bool IsPreferredMainRendererCandidate(Renderer renderer)
	{
		if (!IsMeshBackedRenderer(renderer))
		{
			return false;
		}
		string text = GetTransformPath(((Component)renderer).transform).ToLowerInvariant();
		string text2 = (((Object)renderer).name ?? string.Empty).ToLowerInvariant();
		if (text2.Contains("vfx") || text2.Contains("muzzle") || text2.Contains("flash") || text2.Contains("particle") || text2.Contains("effect"))
		{
			return false;
		}
		if (text.Contains("/vfx") || text.Contains("gunshot") || text.Contains("muzzle") || text.Contains("particle") || text.Contains("effect"))
		{
			return false;
		}
		return true;
	}

	private static int ScoreRendererCandidate(Renderer renderer)
	{
		if ((Object)renderer == (Object)null || !IsMeshBackedRenderer(renderer))
		{
			return int.MinValue;
		}
		string text = GetTransformPath(((Component)renderer).transform).ToLowerInvariant();
		string text2 = (((Object)renderer).name ?? string.Empty).ToLowerInvariant();
		int num = 0;
		if (text2.Contains("vfx") || text2.Contains("muzzle") || text2.Contains("flash") || text2.Contains("particle"))
		{
			num -= 50000;
		}
		if (text.Contains("/vfx") || text.Contains("gunshot"))
		{
			num -= 40000;
		}
		Mesh rendererMesh = GetRendererMesh(renderer);
		if ((Object)rendererMesh != (Object)null)
		{
			num += rendererMesh.vertexCount;
			if (rendererMesh.subMeshCount > 1)
			{
				num += 300;
			}
		}
		if (text2.Contains("ak") || text.Contains("/ak") || text2.Contains("weapon") || text2.Contains("gun"))
		{
			num += 1000;
		}
		return num;
	}

	private static bool SetMember(object target, Type targetType, string[] fieldNames, string[] propertyNames, object value)
	{
		Type type = value.GetType();
		string[] array = fieldNames ?? Array.Empty<string>();
		foreach (string name in array)
		{
			FieldInfo field = targetType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(field == null) && field.FieldType.IsAssignableFrom(type))
			{
				field.SetValue(target, value);
				return true;
			}
		}
		array = propertyNames ?? Array.Empty<string>();
		foreach (string name2 in array)
		{
			PropertyInfo property = targetType.GetProperty(name2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(property == null) && property.CanWrite && property.PropertyType.IsAssignableFrom(type))
			{
				property.SetValue(target, value);
				return true;
			}
		}
		return false;
	}

	private static int ResolveReferenceLayer(Item item, Transform originalAnchor)
	{
		if ((Object)item != (Object)null && (Object)item.mainRenderer != (Object)null)
		{
			return ((Component)item.mainRenderer).gameObject.layer;
		}
		Renderer val = (((Object)item != (Object)null) ? ((Component)item).GetComponentsInChildren<Renderer>(true).FirstOrDefault((Renderer r) => (Object)r != (Object)null && ((Object)originalAnchor == (Object)null || !((Component)r).transform.IsChildOf(originalAnchor))) : null);
		if ((Object)val != (Object)null)
		{
			return ((Component)val).gameObject.layer;
		}
		if ((Object)originalAnchor != (Object)null)
		{
			return ((Component)originalAnchor).gameObject.layer;
		}
		if (!((Object)item != (Object)null))
		{
			return 0;
		}
		return ((Component)item).gameObject.layer;
	}

	private static string GetTransformPath(Transform transform)
	{
		if ((Object)transform == (Object)null)
		{
			return "null";
		}
		Stack<string> stack = new Stack<string>();
		Transform val = transform;
		while ((Object)val != (Object)null)
		{
			stack.Push(((Object)val).name);
			val = val.parent;
		}
		return string.Join("/", stack);
	}

	private static string DescribeMaterialStateForDiagnostics(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return "null";
		}
		Texture val = ResolvePrimaryTexture(material);
		string text = (material.HasProperty("_Cull") ? material.GetInt("_Cull").ToString() : "-");
		return $"{((Object)material).name}[shader={material.shader?.name ?? "null"},rq={material.renderQueue},cull={text},tex={(((Object)val != (Object)null) ? ((Object)val).name : "null")}]";
	}

	private static string DescribeRendererStateForDiagnostics(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return "null";
		}
		Mesh rendererMesh = GetRendererMesh(renderer);
		Transform transform = ((Component)renderer).transform;
		Material[] array = renderer.sharedMaterials ?? Array.Empty<Material>();
		string text = string.Join(" | ", array.Take(4).Select(DescribeMaterialStateForDiagnostics));
		if (array.Length > 4)
		{
			text += $" | ...+{array.Length - 4}";
		}
		string text2 = (((Object)rendererMesh != (Object)null) ? ((Object)rendererMesh).name : "null");
		int num = (((Object)rendererMesh != (Object)null) ? rendererMesh.subMeshCount : 0);
		int num2 = (((Object)rendererMesh != (Object)null) ? rendererMesh.vertexCount : 0);
		return $"{GetTransformPath(transform)}[enabled={renderer.enabled},forceOff={renderer.forceRenderingOff},active={((Component)renderer).gameObject.activeInHierarchy},layer={((Component)renderer).gameObject.layer},mesh={text2},subMeshes={num},verts={num2},mats={text}]";
	}

	private static string DescribeRendererCollectionForDiagnostics(IEnumerable<Renderer> renderers, int maxCount = 10)
	{
		if (renderers == null)
		{
			return "none";
		}
		List<string> list = renderers.Where((Renderer r) => (Object)r != (Object)null).Take(maxCount).Select(DescribeRendererStateForDiagnostics).ToList();
		if (list.Count == 0)
		{
			return "none";
		}
		int num = renderers.Count((Renderer r) => (Object)r != (Object)null);
		if (num > list.Count)
		{
			list.Add("...+" + (num - list.Count));
		}
		return string.Join(" || ", list);
	}

	private static string DescribeBoundsForDiagnostics(Transform root)
	{
		if (!TryGetCombinedRenderableBounds(root, out var bounds))
		{
			return "none";
		}
		return $"center={bounds.center},size={bounds.size}";
	}

	private static void LogWorldVisualState(Item item, Transform visualRoot, string phase, Transform targetVisualRoot)
	{
		if ((Object)item == (Object)null || (Object)visualRoot == (Object)null)
		{
			return;
		}
		int instanceID = ((Object)item).GetInstanceID();
		string text = DisplayName ?? "unknown";
		Renderer[] componentsInChildren = ((Component)visualRoot).GetComponentsInChildren<Renderer>(true);
		string text2 = DescribeRendererCollectionForDiagnostics(componentsInChildren);
		string text3 = "none";
		if (HiddenRendererCache.TryGetValue(instanceID, out var value))
		{
			text3 = DescribeRendererCollectionForDiagnostics(value);
		}
		int num = ResolveReferenceLayer(item, targetVisualRoot);
		Plugin.LogDiagnosticOnce("ak-world-state:" + phase + ":" + instanceID + ":" + text + ":" + ((Object)visualRoot).GetInstanceID(), $"World visual state: phase={phase}, {DescribeItemForDiagnostics(item)}, targetRoot={GetTransformPath(targetVisualRoot)}, targetLayer={(((Object)targetVisualRoot != (Object)null) ? ((Component)targetVisualRoot).gameObject.layer : (-1))}, refLayer={num}, visualRoot={GetTransformPath(visualRoot)}, parent={GetTransformPath(visualRoot.parent)}, localPos={visualRoot.localPosition}, localRot={visualRoot.localRotation.eulerAngles}, localScale={visualRoot.localScale}, worldPos={visualRoot.position}, worldRot={visualRoot.rotation.eulerAngles}, lossyScale={visualRoot.lossyScale}, activeSelf={((Component)visualRoot).gameObject.activeSelf}, activeInHierarchy={((Component)visualRoot).gameObject.activeInHierarchy}, bounds={DescribeBoundsForDiagnostics(visualRoot)}, renderers={text2}, hiddenRenderers={text3}");
	}

	private static void LogHeldVisualCameraState(Item item, Transform visualRoot, string phase)
	{
		if ((Object)item == (Object)null || (Object)visualRoot == (Object)null)
		{
			return;
		}
		Vector3 vector = visualRoot.position;
		if (TryGetCombinedRenderableBounds(visualRoot, out var bounds))
		{
			vector = bounds.center;
		}
		int layer = ((Component)visualRoot).gameObject.layer;
		int layer2 = -1;
		string text = "none";
		TryResolveHeldHandRendererLayer(item, out layer2, out text);
		Camera val = ResolvePreferredLocalCamera();
		string text2 = "camera=null";
		if ((Object)val != (Object)null)
		{
			Vector3 position = ((Component)val).transform.position;
			Vector3 val2 = vector - position;
			float magnitude = val2.magnitude;
			float value = ((magnitude > 0.0001f) ? Vector3.Dot(((Component)val).transform.forward, val2 / magnitude) : 0f);
			text2 = $"camera={GetTransformPath(((Component)val).transform)}, nearClip={val.nearClipPlane:0.###}, farClip={val.farClipPlane:0.###}, camLayer={((Component)val).gameObject.layer}, cullingMask={val.cullingMask}, distanceToBounds={magnitude:0.###}, dotForward={value:0.###}";
		}
		Plugin.LogDiagnosticOnce("ak-held-camera:" + phase + ":" + ((Object)item).GetInstanceID() + ":" + ((Object)visualRoot).GetInstanceID(), $"Held visual camera state: phase={phase}, {DescribeItemForDiagnostics(item)}, visualRoot={GetTransformPath(visualRoot)}, parent={GetTransformPath(visualRoot.parent)}, visualLayer={layer}, handLayer={layer2}, handSource={text}, localPos={visualRoot.localPosition}, localRot={visualRoot.localRotation.eulerAngles}, localScale={visualRoot.localScale}, worldPos={visualRoot.position}, bounds={DescribeBoundsForDiagnostics(visualRoot)}, {text2}");
	}

	private static bool ContainsKeyword(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		string text = value.ToLowerInvariant();
		string text2 = DisplayName.ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(text2) && text.Contains(text2))
		{
			return true;
		}
		string[] selectableWeaponKeywords = SelectableWeaponKeywords;
		foreach (string value2 in selectableWeaponKeywords)
		{
			if (text.Contains(value2))
			{
				return true;
			}
		}
		string[] itemKeywords = ItemKeywords;
		foreach (string value3 in itemKeywords)
		{
			if (text.Contains(value3))
			{
				return true;
			}
		}
		return false;
	}

	private static Mesh GetRendererMesh(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return null;
		}
		if (renderer is MeshRenderer)
		{
			MeshFilter component = ((Component)renderer).GetComponent<MeshFilter>();
			if (!((Object)component != (Object)null))
			{
				return null;
			}
			return component.sharedMesh;
		}
		SkinnedMeshRenderer val = (SkinnedMeshRenderer)(object)((renderer is SkinnedMeshRenderer) ? renderer : null);
		if (val != null)
		{
			return val.sharedMesh;
		}
		return null;
	}

	private static void SetLayerRecursively(Transform root, int layer)
	{
		if (!((Object)root == (Object)null))
		{
			((Component)root).gameObject.layer = layer;
			for (int i = 0; i < root.childCount; i++)
			{
				SetLayerRecursively(root.GetChild(i), layer);
			}
		}
	}

	private static int ResolveVisibleLayerForLocalPlayer(int fallbackLayer)
	{
		Camera val = ResolvePreferredLocalCamera();
		if ((Object)val == (Object)null)
		{
			return fallbackLayer;
		}
		int cullingMask = val.cullingMask;
		if (fallbackLayer >= 0 && fallbackLayer < 32 && (cullingMask & (1 << fallbackLayer)) != 0)
		{
			return fallbackLayer;
		}
		int layer = ((Component)val).gameObject.layer;
		if ((cullingMask & (1 << layer)) != 0)
		{
			return layer;
		}
		for (int i = 0; i < 32; i++)
		{
			if ((cullingMask & (1 << i)) != 0)
			{
				return i;
			}
		}
		return fallbackLayer;
	}

	private static bool TryResolveHeldHandRendererLayer(Item item, out int layer, out string sourcePath)
	{
		layer = -1;
		sourcePath = "none";
		if ((Object)item == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && IsHandItemRenderer(val))
			{
				layer = ((Component)val).gameObject.layer;
				sourcePath = GetTransformPath(((Component)val).transform);
				return true;
			}
		}
		return false;
	}

	private static int ResolveBestVisibleLayerForLocalHeldItem(Item item, Transform preferredRoot)
	{
		int num = ResolveReferenceLayer(item, preferredRoot);
		if (ShouldUseSeparateLocalFirstPersonVisual(item))
		{
			return num;
		}
		if (TryResolveHeldHandRendererLayer(item, out var layer, out var _))
		{
			return layer;
		}
		Camera val = ResolvePreferredLocalCamera();
		if ((Object)val == (Object)null)
		{
			return num;
		}
		int cullingMask = val.cullingMask;
		List<int> list = new List<int>();
		if ((Object)preferredRoot != (Object)null)
		{
			list.Add(((Component)preferredRoot).gameObject.layer);
		}
		Transform val2 = ResolveTargetVisualRoot(item);
		if ((Object)val2 != (Object)null)
		{
			list.Add(((Component)val2).gameObject.layer);
		}
		if ((Object)item != (Object)null)
		{
			Renderer[] componentsInChildren = ((Component)item).GetComponentsInChildren<Renderer>(true);
			foreach (Renderer val3 in componentsInChildren)
			{
				if (!((Object)val3 == (Object)null))
				{
					if (IsHandItemRenderer(val3))
					{
						list.Insert(0, ((Component)val3).gameObject.layer);
					}
					else
					{
						list.Add(((Component)val3).gameObject.layer);
					}
				}
			}
		}
		foreach (int item2 in list.Distinct())
		{
			if (item2 >= 0 && item2 < 32 && (cullingMask & (1 << item2)) != 0)
			{
				return item2;
			}
		}
		return ResolveVisibleLayerForLocalPlayer(num);
	}

	internal static bool TryGetTransformPoseRelativeToAkRoot(Transform sourceTransform, out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
	{
		localPosition = Vector3.zero;
		localRotation = Quaternion.identity;
		localScale = Vector3.one;
		if ((Object)sourceTransform == (Object)null || (Object)Plugin._ak47Prefab == (Object)null)
		{
			return false;
		}
		DecomposeMatrix(Plugin._ak47Prefab.transform.worldToLocalMatrix * sourceTransform.localToWorldMatrix, out localPosition, out localRotation, out localScale);
		localPosition = SanitizeLocalPosition(localPosition, Vector3.zero);
		localRotation = SanitizeLocalRotation(localRotation);
		localScale = SanitizeLocalScale(localScale, Vector3.one);
		return true;
	}

	private static Vector3 SanitizeLocalPosition(Vector3 value, Vector3 fallback)
	{
		if (!IsFiniteFloat(value.x) || !IsFiniteFloat(value.y) || !IsFiniteFloat(value.z))
		{
			return fallback;
		}
		if (value.sqrMagnitude > 100f)
		{
			return fallback;
		}
		return value;
	}

	private static Quaternion SanitizeLocalRotation(Quaternion value)
	{
		if (!IsFiniteFloat(value.x) || !IsFiniteFloat(value.y) || !IsFiniteFloat(value.z) || !IsFiniteFloat(value.w))
		{
			return Quaternion.identity;
		}
		float num = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
		if (!IsFiniteFloat(num) || num < 1E-05f)
		{
			return Quaternion.identity;
		}
		return new Quaternion(value.x / num, value.y / num, value.z / num, value.w / num);
	}

	private static Vector3 SanitizeLocalScale(Vector3 value, Vector3 fallback)
	{
		if (!IsFiniteFloat(value.x) || !IsFiniteFloat(value.y) || !IsFiniteFloat(value.z))
		{
			return fallback;
		}
		Vector3 val = default(Vector3);
		val = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
		if (val.x < 0.0001f || val.y < 0.0001f || val.z < 0.0001f)
		{
			return fallback;
		}
		if (val.x > 10f || val.y > 10f || val.z > 10f)
		{
			return fallback;
		}
		return val;
	}

	private static bool IsFiniteFloat(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	private static Camera ResolvePreferredLocalCamera()
	{
		Camera main = Camera.main;
		if ((Object)main != (Object)null && ((Behaviour)main).isActiveAndEnabled && (Object)main.targetTexture == (Object)null)
		{
			return main;
		}
		Camera[] array = Object.FindObjectsByType<Camera>((FindObjectsSortMode)0);
		Camera result = null;
		int num = int.MinValue;
		Character localCharacter = Character.localCharacter;
		Camera[] array2 = array;
		foreach (Camera val in array2)
		{
			if (!((Object)val == (Object)null) && ((Behaviour)val).isActiveAndEnabled && !((Object)val.targetTexture != (Object)null))
			{
				int num2 = 0;
				string text = ((((Object)val).name ?? string.Empty) + "/" + (((Object)((Component)val).transform.parent != (Object)null) ? ((Object)((Component)val).transform.parent).name : string.Empty)).ToLowerInvariant();
				if (text.Contains("main"))
				{
					num2 += 80;
				}
				if (text.Contains("player") || text.Contains("fps") || text.Contains("view"))
				{
					num2 += 50;
				}
				if ((Object)localCharacter != (Object)null && ((Component)val).transform.IsChildOf(((Component)localCharacter).transform))
				{
					num2 += 120;
				}
				if (val.depth > 0f)
				{
					num2 += 10;
				}
				if (num2 > num)
				{
					num = num2;
					result = val;
				}
			}
		}
		return result;
	}

	private static void SyncVisualRootToTarget(Transform visualRoot, Transform targetVisualRoot)
	{
		if (!((Object)visualRoot == (Object)null))
		{
			if ((Object)targetVisualRoot == (Object)null)
			{
				visualRoot.localPosition = Vector3.zero;
				visualRoot.localRotation = Quaternion.identity;
				visualRoot.localScale = Vector3.one;
			}
			else if ((Object)(object)visualRoot.parent == (Object)(object)targetVisualRoot)
			{
				visualRoot.localPosition = Vector3.zero;
				visualRoot.localRotation = Quaternion.identity;
				visualRoot.localScale = Vector3.one;
			}
			else
			{
				visualRoot.SetParent(targetVisualRoot, false);
				visualRoot.localPosition = Vector3.zero;
				visualRoot.localRotation = Quaternion.identity;
				visualRoot.localScale = Vector3.one;
			}
		}
	}

	private static void DecomposeMatrix(Matrix4x4 matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale)
	{
		position = matrix.GetColumn(3);
		Vector3 val = default(Vector3);
		val = new Vector3(matrix.m00, matrix.m10, matrix.m20);
		Vector3 val2 = default(Vector3);
		val2 = new Vector3(matrix.m01, matrix.m11, matrix.m21);
		Vector3 val3 = default(Vector3);
		val3 = new Vector3(matrix.m02, matrix.m12, matrix.m22);
		scale = new Vector3(val.magnitude, val2.magnitude, val3.magnitude);
		if (scale.x == 0f)
		{
			scale.x = 1f;
		}
		if (scale.y == 0f)
		{
			scale.y = 1f;
		}
		if (scale.z == 0f)
		{
			scale.z = 1f;
		}
		rotation = Quaternion.LookRotation(val3 / scale.z, val2 / scale.y);
	}
}
