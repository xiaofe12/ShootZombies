using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShootZombies;

internal static class AkUiPatchHelpers
{
	private const BindingFlags ReflectionFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly FieldInfo InventoryPrefabField = typeof(InventoryItemUI).GetField("_itemPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly FieldInfo InventoryItemDataField = typeof(InventoryItemUI).GetField("_itemData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly FieldInfo ItemDataField = typeof(Item).GetField("data", ReflectionFlags);

	private static readonly PropertyInfo ItemDataProperty = typeof(Item).GetProperty("data", ReflectionFlags);

	private static readonly PropertyInfo SlotItemProperty = typeof(ItemSlot).GetProperty("item", ReflectionFlags);

	private static readonly FieldInfo SlotItemField = typeof(ItemSlot).GetField("item", ReflectionFlags);

	private static readonly FieldInfo SliceItemSlotField = typeof(BackpackWheelSlice).GetField("itemSlot", ReflectionFlags);

	private static readonly Dictionary<Type, PropertyInfo> InventoryItemDataItemPropertyCache = new Dictionary<Type, PropertyInfo>();

	private static readonly Dictionary<Type, FieldInfo> InventoryItemDataItemFieldCache = new Dictionary<Type, FieldInfo>();

	private static readonly Dictionary<int, int> InventoryUiSourceKeyCache = new Dictionary<int, int>();

	private static readonly Dictionary<int, int> SliceSourceKeyCache = new Dictionary<int, int>();

	private static readonly Rect FullUvRect = new Rect(0f, 0f, 1f, 1f);

	private static readonly HashSet<InventoryItemUI> TrackedInventoryUis = new HashSet<InventoryItemUI>();

	private static readonly HashSet<BackpackWheelSlice> TrackedSlices = new HashSet<BackpackWheelSlice>();

	private static readonly HashSet<BackpackWheel> TrackedWheels = new HashSet<BackpackWheel>();

	private static readonly List<InventoryItemUI> StaleInventoryUis = new List<InventoryItemUI>();

	private static readonly List<BackpackWheelSlice> StaleSlices = new List<BackpackWheelSlice>();

	private static readonly List<BackpackWheel> StaleWheels = new List<BackpackWheel>();

	internal static void ApplyAkIcon(RawImage image, Item item)
	{
		if (!((Object)image == (Object)null) && ItemPatch.IsBlowgunLike(item))
		{
			ApplyAkIconForce(image);
		}
	}

	internal static void ApplyAkIconForce(RawImage image)
	{
		if (!((Object)image == (Object)null))
		{
			Texture2D akIconTexture = Plugin.GetAkIconTexture();
			if (!((Object)akIconTexture == (Object)null))
			{
				Graphic graphic = (Graphic)(object)image;
				if ((Object)image.texture == (Object)akIconTexture && graphic.material == null && graphic.color == Color.white && image.uvRect == FullUvRect && ((Behaviour)image).enabled)
				{
					return;
				}
				image.texture = (Texture)(object)akIconTexture;
				graphic.color = Color.white;
				graphic.material = null;
				image.uvRect = FullUvRect;
				((Behaviour)image).enabled = true;
			}
		}
	}

	internal static void ApplyAkText(TMP_Text label)
	{
		if (!((Object)label == (Object)null))
		{
			if (!string.Equals(label.text, ItemPatch.DisplayName, StringComparison.Ordinal))
			{
				label.text = ItemPatch.DisplayName;
			}
		}
	}

	internal static void EnsureAkDisplayAndVisual(Item item)
	{
		if (!((Object)item == (Object)null) && ItemPatch.IsBlowgunLike(item))
		{
			ItemPatch.ApplyAkDisplay(item);
		}
	}

	internal static void ApplyAkToInventoryUi(InventoryItemUI ui, Item knownItem = null)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return;
		}
		TrackInventoryUi(ui);
		Item val = (((Object)knownItem != (Object)null) ? knownItem : ResolveItemFromInventoryUi(ui));
		int instanceID = ((Object)ui).GetInstanceID();
		int itemSourceKey = GetItemSourceKey(val);
		if (itemSourceKey != 0 && InventoryUiSourceKeyCache.TryGetValue(instanceID, out var value) && value == itemSourceKey && IsInventoryUiAlreadyShowingAk(ui))
		{
			return;
		}
		if ((Object)val != (Object)null && ItemPatch.IsBlowgunLike(val))
		{
			EnsureAkDisplayAndVisual(val);
			ApplyAkIconForce(ui.icon);
			ApplyAkText((TMP_Text)(object)ui.nameText);
			InventoryUiSourceKeyCache[instanceID] = itemSourceKey;
			return;
		}
		InventoryUiSourceKeyCache[instanceID] = itemSourceKey;
		if ((Object)val != (Object)null)
		{
			RestoreInventoryUi(ui, val);
			return;
		}
		if (itemSourceKey == 0 && IsInventoryUiAlreadyShowingAk(ui))
		{
			ClearInventoryUi(ui);
			return;
		}
		TextMeshProUGUI nameText = ui.nameText;
		string text = (((nameText != null) ? ((TMP_Text)nameText).text : null) ?? string.Empty).ToLowerInvariant();
		RawImage icon = ui.icon;
		Texture val2 = ((icon != null) ? icon.texture : null);
		string text2 = (((Object)val2 != (Object)null) ? ((Object)val2).name : string.Empty).ToLowerInvariant();
		if (ItemPatch.ContainsWeaponKeyword(text) || text2.Contains("dart") || text2.Contains("blowgun") || ItemPatch.ContainsWeaponKeyword(text2))
		{
			ApplyAkIconForce(ui.icon);
			ApplyAkText((TMP_Text)(object)ui.nameText);
		}
	}

	internal static Item ResolveItemFromInventoryUi(InventoryItemUI ui)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return null;
		}
		try
		{
			object obj = InventoryPrefabField?.GetValue(ui);
			Item val = (Item)((obj is Item) ? obj : null);
			if (val != null)
			{
				return val;
			}
			object obj2 = InventoryItemDataField?.GetValue(ui);
			if (obj2 != null)
			{
				Item itemFromItemData = ResolveItemFromItemData(obj2);
				if ((Object)itemFromItemData != (Object)null)
				{
					return itemFromItemData;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	internal static Item ResolveItemFromSlot(ItemSlot slot)
	{
		if (slot == null || slot.IsEmpty())
		{
			return null;
		}
		try
		{
			object obj = SlotItemProperty?.GetValue(slot);
			Item val = (Item)((obj is Item) ? obj : null);
			if (val != null)
			{
				return val;
			}
			object obj2 = SlotItemField?.GetValue(slot);
			Item val2 = (Item)((obj2 is Item) ? obj2 : null);
			if (val2 != null)
			{
				return val2;
			}
		}
		catch
		{
		}
		return slot.prefab;
	}

	internal static void ApplyAkToSliceImage(BackpackWheelSlice slice, Item knownItem = null)
	{
		if (!((Object)(object)slice == (Object)null))
		{
			TrackSlice(slice);
			Item val = (((Object)knownItem != (Object)null) ? knownItem : ResolveItemFromSlice(slice));
			int instanceID = ((Object)slice).GetInstanceID();
			int itemSourceKey = GetItemSourceKey(val);
			if (itemSourceKey != 0 && SliceSourceKeyCache.TryGetValue(instanceID, out var value) && value == itemSourceKey && IsSliceAlreadyShowingAk(slice))
			{
				return;
			}
			if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val))
			{
				EnsureAkDisplayAndVisual(val);
				ApplyAkIconForce(slice.image);
			}
			else
			{
				RestoreSliceImage(slice, val);
			}
			SliceSourceKeyCache[instanceID] = itemSourceKey;
		}
	}

	internal static void ApplyAkToBackpackWheel(BackpackWheel wheel, Item knownItem = null)
	{
		if ((Object)(object)wheel == (Object)null)
		{
			return;
		}
		TrackWheel(wheel);
		Item val = knownItem;
		if ((Object)val == (Object)null)
		{
			Character localCharacter = Character.localCharacter;
			val = localCharacter?.data?.currentItem;
		}
		Character localCharacter2 = Character.localCharacter;
		if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val) && ItemPatch.IsLocallyHeldByPlayer(val, localCharacter2))
		{
			ItemPatch.ApplyAkDisplayIfNeeded(val);
			ApplyAkIcon(wheel.currentlyHeldItem, val);
			return;
		}
		RestoreBackpackWheel(wheel, ((Object)val != (Object)null && ItemPatch.IsBlowgunLike(val)) ? null : val);
	}

	internal static bool RefreshTrackedUi()
	{
		CleanupStaleTrackedUi();
		bool flag = false;
		foreach (InventoryItemUI trackedInventoryUi in TrackedInventoryUis)
		{
			if (!((Object)(object)trackedInventoryUi == (Object)null))
			{
				ApplyAkToInventoryUi(trackedInventoryUi);
				flag = true;
			}
		}
		foreach (BackpackWheelSlice trackedSlice in TrackedSlices)
		{
			if (!((Object)(object)trackedSlice == (Object)null))
			{
				ApplyAkToSliceImage(trackedSlice);
				flag = true;
			}
		}
		foreach (BackpackWheel trackedWheel in TrackedWheels)
		{
			if (!((Object)(object)trackedWheel == (Object)null))
			{
				ApplyAkToBackpackWheel(trackedWheel);
				flag = true;
			}
		}
		return flag;
	}

	internal static void ClearInventoryUiCache(InventoryItemUI ui)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return;
		}
		InventoryUiSourceKeyCache.Remove(((Object)ui).GetInstanceID());
	}

	internal static void ClearRuntimeCaches()
	{
		InventoryUiSourceKeyCache.Clear();
		SliceSourceKeyCache.Clear();
		TrackedInventoryUis.Clear();
		TrackedSlices.Clear();
		TrackedWheels.Clear();
		StaleInventoryUis.Clear();
		StaleSlices.Clear();
		StaleWheels.Clear();
	}

	private static Item ResolveItemFromItemData(object itemData)
	{
		if (itemData == null)
		{
			return null;
		}
		Type type = itemData.GetType();
		if (!InventoryItemDataItemPropertyCache.TryGetValue(type, out var value))
		{
			value = type.GetProperty("item", ReflectionFlags);
			InventoryItemDataItemPropertyCache[type] = value;
			InventoryItemDataItemFieldCache[type] = type.GetField("item", ReflectionFlags);
		}
		try
		{
			object obj = value?.GetValue(itemData) ?? InventoryItemDataItemFieldCache[type]?.GetValue(itemData);
			return (Item)((obj is Item) ? obj : null);
		}
		catch
		{
			return null;
		}
	}

	private static int GetItemSourceKey(Item item)
	{
		if (!((Object)item == (Object)null))
		{
			return ((Object)item).GetInstanceID();
		}
		return 0;
	}

	internal static bool IsInventoryUiAlreadyShowingAk(InventoryItemUI ui)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return false;
		}
		if (!string.Equals(ui.nameText?.text, ItemPatch.DisplayName, StringComparison.Ordinal))
		{
			return false;
		}
		Texture2D akIconTexture = Plugin.GetAkIconTexture();
		RawImage icon = ui.icon;
		return (Object)akIconTexture != (Object)null && !((Object)icon == (Object)null) && (Object)icon.texture == (Object)akIconTexture;
	}

	private static bool IsSliceAlreadyShowingAk(BackpackWheelSlice slice)
	{
		if ((Object)(object)slice == (Object)null)
		{
			return false;
		}
		Texture2D akIconTexture = Plugin.GetAkIconTexture();
		RawImage image = slice.image;
		return (Object)akIconTexture != (Object)null && !((Object)image == (Object)null) && (Object)image.texture == (Object)akIconTexture;
	}

	private static void RestoreInventoryUi(InventoryItemUI ui, Item item)
	{
		if ((Object)(object)ui == (Object)null || (Object)item == (Object)null)
		{
			return;
		}
		Texture itemIcon = ResolveDefaultItemIcon(item);
		if (!((Object)ui.icon == (Object)null))
		{
			ui.icon.texture = itemIcon;
			((Graphic)(object)ui.icon).color = ResolveInventoryIconColor(ui, Color.white);
			((Graphic)(object)ui.icon).material = null;
			ui.icon.uvRect = FullUvRect;
			((Behaviour)ui.icon).enabled = (Object)itemIcon != (Object)null;
		}
		if (!((Object)ui.nameText == (Object)null))
		{
			((TMP_Text)ui.nameText).text = ResolveDefaultItemName(item);
		}
	}

	private static void ClearInventoryUi(InventoryItemUI ui)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return;
		}
		if (!((Object)ui.icon == (Object)null))
		{
			ui.icon.texture = null;
			((Behaviour)ui.icon).enabled = false;
		}
		if (!((Object)ui.nameText == (Object)null))
		{
			((TMP_Text)ui.nameText).text = string.Empty;
		}
	}

	private static void RestoreSliceImage(BackpackWheelSlice slice, Item item)
	{
		if ((Object)(object)slice == (Object)null || (Object)slice.image == (Object)null)
		{
			return;
		}
		Texture itemIcon = ResolveDefaultItemIcon(item);
		slice.image.texture = itemIcon;
		((Graphic)(object)slice.image).color = ResolveSliceIconColor(slice, Color.white);
		((Graphic)(object)slice.image).material = null;
		slice.image.uvRect = FullUvRect;
		((Behaviour)slice.image).enabled = (Object)itemIcon != (Object)null;
	}

	private static void RestoreBackpackWheel(BackpackWheel wheel, Item item)
	{
		if ((Object)(object)wheel == (Object)null)
		{
			return;
		}
		RawImage currentlyHeldItem = wheel.currentlyHeldItem;
		if ((Object)currentlyHeldItem == (Object)null)
		{
			return;
		}
		Texture itemIcon = ResolveDefaultItemIcon(item);
		currentlyHeldItem.texture = itemIcon;
		((Graphic)(object)currentlyHeldItem).color = ResolveItemIconColor(item, Color.white);
		((Graphic)(object)currentlyHeldItem).material = null;
		currentlyHeldItem.uvRect = FullUvRect;
		((Behaviour)currentlyHeldItem).enabled = (Object)itemIcon != (Object)null;
	}

	private static Texture ResolveDefaultItemIcon(Item item)
	{
		if ((Object)item == (Object)null || item.UIData == null)
		{
			return null;
		}
		try
		{
			return (Texture)(object)item.UIData.GetIcon();
		}
		catch
		{
			return (Texture)(object)item.UIData.icon;
		}
	}

	private static Color ResolveInventoryIconColor(InventoryItemUI ui, Color fallback)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return fallback;
		}
		try
		{
			object itemData = InventoryItemDataField?.GetValue(ui);
			return TryGetCookColor(itemData, out var color) ? color : fallback;
		}
		catch
		{
			return fallback;
		}
	}

	private static Color ResolveSliceIconColor(BackpackWheelSlice slice, Color fallback)
	{
		ItemSlot itemSlot = ResolveItemSlotFromSlice(slice);
		return itemSlot != null && TryGetCookColor(itemSlot.data, out var color) ? color : fallback;
	}

	private static Color ResolveItemIconColor(Item item, Color fallback)
	{
		if ((Object)item == (Object)null)
		{
			return fallback;
		}
		try
		{
			object itemData = ItemDataField?.GetValue(item) ?? ItemDataProperty?.GetValue(item);
			return TryGetCookColor(itemData, out var color) ? color : fallback;
		}
		catch
		{
			return fallback;
		}
	}

	private static bool TryGetCookColor(object itemData, out Color color)
	{
		color = Color.white;
		if (itemData is ItemInstanceData data
			&& data.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cooked)
			&& cooked != null
			&& cooked.Value > 0)
		{
			color = ItemCooking.GetCookColor(cooked.Value);
			return true;
		}
		return false;
	}

	private static string ResolveDefaultItemName(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return string.Empty;
		}
		try
		{
			string itemName = item.GetItemName();
			if (!string.IsNullOrWhiteSpace(itemName))
			{
				return itemName;
			}
		}
		catch
		{
		}
		return item.UIData?.itemName ?? string.Empty;
	}

	internal static Item ResolveItemFromSlice(BackpackWheelSlice slice)
	{
		if ((Object)(object)slice == (Object)null)
		{
			return null;
		}
		try
		{
			PropertyInfo[] properties = ((object)slice).GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (!(propertyInfo == null) && propertyInfo.GetIndexParameters().Length == 0)
				{
					object value = null;
					try
					{
						value = propertyInfo.GetValue(slice);
					}
					catch
					{
					}
					Item val = ExtractItem(value);
					if ((Object)val != (Object)null)
					{
						return val;
					}
				}
			}
			FieldInfo[] fields = ((object)slice).GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				object value2 = null;
				try
				{
					value2 = fieldInfo.GetValue(slice);
				}
				catch
				{
				}
				Item val2 = ExtractItem(value2);
				if ((Object)val2 != (Object)null)
				{
					return val2;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static ItemSlot ResolveItemSlotFromSlice(BackpackWheelSlice slice)
	{
		if ((Object)(object)slice == (Object)null || SliceItemSlotField == null)
		{
			return null;
		}
		try
		{
			return SliceItemSlotField.GetValue(slice) as ItemSlot;
		}
		catch
		{
			return null;
		}
	}

	private static void TrackInventoryUi(InventoryItemUI ui)
	{
		if (!((Object)(object)ui == (Object)null) && !TrackedInventoryUis.Contains(ui))
		{
			TrackedInventoryUis.Add(ui);
		}
	}

	private static void TrackSlice(BackpackWheelSlice slice)
	{
		if (!((Object)(object)slice == (Object)null) && !TrackedSlices.Contains(slice))
		{
			TrackedSlices.Add(slice);
		}
	}

	private static void TrackWheel(BackpackWheel wheel)
	{
		if (!((Object)(object)wheel == (Object)null) && !TrackedWheels.Contains(wheel))
		{
			TrackedWheels.Add(wheel);
		}
	}

	private static void CleanupStaleTrackedUi()
	{
		RemoveDestroyedTrackedObjects(TrackedInventoryUis, StaleInventoryUis);
		RemoveDestroyedTrackedObjects(TrackedSlices, StaleSlices);
		RemoveDestroyedTrackedObjects(TrackedWheels, StaleWheels);
	}

	private static void RemoveDestroyedTrackedObjects<T>(HashSet<T> source, List<T> staleBuffer) where T : Object
	{
		if (source.Count == 0)
		{
			return;
		}
		staleBuffer.Clear();
		foreach (T item in source)
		{
			if ((Object)item == (Object)null)
			{
				staleBuffer.Add(item);
			}
		}
		for (int i = 0; i < staleBuffer.Count; i++)
		{
			source.Remove(staleBuffer[i]);
		}
		staleBuffer.Clear();
	}

	private static Item ExtractItem(object value)
	{
		Item val = (Item)((value is Item) ? value : null);
		if (val != null)
		{
			return val;
		}
		ItemSlot val2 = (ItemSlot)((value is ItemSlot) ? value : null);
		if (val2 != null)
		{
			return ResolveItemFromSlot(val2);
		}
		if (value == null)
		{
			return null;
		}
		Type type = value.GetType();
		if (!(type.FullName ?? string.Empty).StartsWith("System.ValueTuple", StringComparison.Ordinal))
		{
			return null;
		}
		try
		{
			FieldInfo field = type.GetField("Item1");
			FieldInfo field2 = type.GetField("Item2");
			object obj = field?.GetValue(value);
			object obj2 = field2?.GetValue(value);
			if (obj is BackpackReference backpackRef)
			{
				if (obj2 is byte slotIndex)
				{
					return ResolveFromBackpackTuple(backpackRef, slotIndex);
				}
				if (obj2 is int num)
				{
					return ResolveFromBackpackTuple(backpackRef, (byte)Mathf.Clamp(num, 0, 255));
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static Item ResolveFromBackpackTuple(BackpackReference backpackRef, byte slotIndex)
	{
		try
		{
			if ((object)backpackRef == null)
			{
				return null;
			}
			object data = backpackRef.GetData();
			if (data == null)
			{
				return null;
			}
			if (!((data.GetType().GetProperty("itemSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(data) ?? data.GetType().GetField("itemSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(data)) is ItemSlot[] array) || slotIndex >= array.Length)
			{
				return null;
			}
			return ResolveItemFromSlot(array[slotIndex]);
		}
		catch
		{
		}
		return null;
	}
}
