using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(InventoryItemUI), "SetItem")]
public static class InventoryItemUiPatch
{
	private readonly struct SetItemState : IEquatable<SetItemState>
	{
		private readonly int _prefabInstanceId;

		private readonly int _prefabItemId;

		private readonly int _resolvedItemInstanceId;

		private readonly int _itemDataIdentity;

		private readonly string _displayName;

		private readonly bool _isBlowgunLike;

		public SetItemState(ItemSlot slot, Item resolvedItem, bool isBlowgunLike)
		{
			Item prefab = slot?.prefab;
			_prefabInstanceId = (Object)prefab != (Object)null ? ((Object)prefab).GetInstanceID() : 0;
			_prefabItemId = (Object)prefab != (Object)null ? prefab.itemID : 0;
			_resolvedItemInstanceId = (Object)resolvedItem != (Object)null ? ((Object)resolvedItem).GetInstanceID() : 0;
			_itemDataIdentity = slot?.data != null ? RuntimeHelpers.GetHashCode(slot.data) : 0;
			_displayName = ItemPatch.DisplayName ?? string.Empty;
			_isBlowgunLike = isBlowgunLike;
		}

		public bool Equals(SetItemState other)
		{
			return _prefabInstanceId == other._prefabInstanceId
				&& _prefabItemId == other._prefabItemId
				&& _resolvedItemInstanceId == other._resolvedItemInstanceId
				&& _itemDataIdentity == other._itemDataIdentity
				&& string.Equals(_displayName, other._displayName, StringComparison.Ordinal)
				&& _isBlowgunLike == other._isBlowgunLike;
		}
	}

	private static readonly Dictionary<int, SetItemState> LastStateByUiId = new Dictionary<int, SetItemState>();

	[HarmonyPostfix]
	public static void SetItemPostfix(InventoryItemUI __instance, ItemSlot slot)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		int instanceID = ((Object)__instance).GetInstanceID();
		if (slot == null || slot.IsEmpty())
		{
			LastStateByUiId.Remove(instanceID);
			AkUiPatchHelpers.ClearInventoryUiCache(__instance);
			return;
		}
		try
		{
			Item val = AkUiPatchHelpers.ResolveItemFromSlot(slot);
			Item item = (((Object)val != (Object)null) ? val : slot.prefab);
			bool flag = ItemPatch.IsBlowgunLike(item);
			SetItemState setItemState = new SetItemState(slot, item, flag);
			if (LastStateByUiId.TryGetValue(instanceID, out var value)
				&& value.Equals(setItemState)
				&& (!flag || AkUiPatchHelpers.IsInventoryUiAlreadyShowingAk(__instance)))
			{
				return;
			}
			LastStateByUiId[instanceID] = setItemState;
			if (flag)
			{
				ItemPatch.ApplyAkDisplayIfNeeded(item);
				if ((Object)slot.prefab != (Object)null)
				{
					ItemPatch.ApplyAkDisplayIfNeeded(slot.prefab);
				}
				AkUiPatchHelpers.ApplyAkToInventoryUi(__instance, item);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] InventoryItemUiPatch failed: " + ex));
		}
	}

	internal static void ClearCaches()
	{
		LastStateByUiId.Clear();
	}
}
