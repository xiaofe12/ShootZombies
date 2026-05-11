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
	private Canvas ResolveHudCanvas()
	{
		GUIManager instance = GUIManager.instance;
		if ((Object)instance == (Object)null)
		{
			return null;
		}
		TextMeshProUGUI val = (((Object)instance.itemPromptMain != (Object)null) ? instance.itemPromptMain : instance.interactNameText);
		Canvas val2 = (((Object)val != (Object)null) ? ((Component)val).GetComponentInParent<Canvas>() : null);
		Canvas[] componentsInChildren = ((Component)instance).GetComponentsInChildren<Canvas>(true);
		Canvas val3 = null;
		float num = -1f;
		Canvas[] array = componentsInChildren;
		foreach (Canvas val4 in array)
		{
			if ((Object)val4 == (Object)null)
			{
				continue;
			}
			RectTransform component = ((Component)val4).GetComponent<RectTransform>();
			if ((Object)component == (Object)null)
			{
				continue;
			}
			Rect rect = component.rect;
			float num2 = Mathf.Abs(rect.width * rect.height);
			if (num2 < 1f)
			{
				continue;
			}
			if (val4.isRootCanvas)
			{
				num2 += 1000000f;
			}
			if (((Component)val4).gameObject.activeInHierarchy)
			{
				num2 += 500000f;
			}
			if ((Object)val2 != (Object)null && (Object)val4 == (Object)val2)
			{
				num2 += 250000f;
			}
			if (num2 > num)
			{
				num = num2;
				val3 = val4;
			}
		}
		if ((Object)val3 != (Object)null)
		{
			return val3;
		}
		return val2;
	}

	private TextMeshProUGUI ResolveHudTextStyleSource()
	{
		GUIManager instance = GUIManager.instance;
		if ((Object)instance == (Object)null)
		{
			return null;
		}
		if ((Object)instance.itemPromptMain != (Object)null)
		{
			return instance.itemPromptMain;
		}
		if ((Object)instance.interactNameText != (Object)null)
		{
			return instance.interactNameText;
		}
		return instance.interactPromptText;
	}

	private void ApplyGameTextStyle(TextMeshProUGUI target, Color color, float sizeMultiplier = 1f, float? customLineSpacing = null)
	{
		if (!((Object)target == (Object)null))
		{
			TextMeshProUGUI val = ResolveHudTextStyleSource();
			if ((Object)val != (Object)null)
			{
				((TMP_Text)target).font = ((TMP_Text)val).font;
				((TMP_Text)target).fontSharedMaterial = ((TMP_Text)val).fontSharedMaterial;
				((TMP_Text)target).fontStyle = ((TMP_Text)val).fontStyle;
				((TMP_Text)target).characterSpacing = ((TMP_Text)val).characterSpacing;
				((TMP_Text)target).wordSpacing = ((TMP_Text)val).wordSpacing;
				((TMP_Text)target).lineSpacing = customLineSpacing ?? ((TMP_Text)val).lineSpacing;
				((TMP_Text)target).textWrappingMode = (TextWrappingModes)0;
			}
			((TMP_Text)target).fontSize = 18f * sizeMultiplier;
			((Graphic)target).color = color;
			((TMP_Text)target).alignment = (TextAlignmentOptions)257;
		}
	}

	private static void ApplyBottomLeftAnchoredRect(RectTransform target, RectTransform canvasRect, float width, float height, float scale, float x, float y)
	{
		if ((Object)target == (Object)null || (Object)canvasRect == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one * Mathf.Max(scale, 0.3f);
		target.anchorMin = new Vector2(0f, 0f);
		target.anchorMax = new Vector2(0f, 0f);
		target.pivot = new Vector2(0f, 0f);
		target.sizeDelta = new Vector2(width, height);
		Rect rect = canvasRect.rect;
		float num5 = height * Mathf.Max(scale, 0.3f);
		float num6 = y + FogUiSharedTopReferenceHeight;
		float num7 = Mathf.Min(rect.width * FogUiCanvasOverflowXRatio, FogUiCanvasOverflowXMax);
		float num8 = Mathf.Min(rect.height * FogUiCanvasOverflowYRatio, FogUiCanvasOverflowYMax);
		float num9 = Mathf.Clamp(x, 0f - num7, rect.width + num7);
		float num10 = Mathf.Clamp(num6, 0f - num8, rect.height + num8);
		float num11 = num10 - num5;
		target.anchoredPosition = new Vector2(num9, num11);
	}

	private static void ApplyRightMiddleRect(RectTransform target, float width, float height, float rightOffset, float downOffset)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one;
		target.anchorMin = new Vector2(1f, 0.5f);
		target.anchorMax = new Vector2(1f, 0.5f);
		target.pivot = new Vector2(1f, 0.5f);
		target.sizeDelta = new Vector2(width, height);
		target.anchoredPosition = new Vector2(0f - rightOffset, 0f - downOffset);
	}

	private static void ApplyRightBottomRect(RectTransform target, float width, float height, float rightOffset, float bottomOffset)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one;
		target.anchorMin = new Vector2(1f, 0f);
		target.anchorMax = new Vector2(1f, 0f);
		target.pivot = new Vector2(1f, 0f);
		target.sizeDelta = new Vector2(width, height);
		target.anchoredPosition = new Vector2(0f - rightOffset, bottomOffset);
	}

	private static string GetFogModeLobbyNoticeTextCore(bool isChinese)
	{
		return string.Empty;
	}

	private void ClampFogModeLobbyNoticeToCanvas()
	{
		if ((Object)_fogModeLobbyNoticeRect == (Object)null)
		{
			return;
		}
		string text = NormalizeLocalizedText((Object)_fogModeLobbyNoticeText != (Object)null ? ((TMP_Text)_fogModeLobbyNoticeText).text : _lastFogModeLobbyNoticeText);
		ApplySharedFogBottomLeftLayout(_fogModeLobbyNoticeRect, FogModeLobbyNoticeWidth, GetLobbyNoticePreferredHeight(text));
	}

	private void UpdateFogModeLobbyNotice()
	{
		CleanupFogModeLobbyNotice();
	}

	private void ClampWeaponLobbyNoticeToCanvas()
	{
		if ((Object)_weaponLobbyNoticeRect == (Object)null)
		{
			return;
		}
		string text = NormalizeLocalizedText((Object)_weaponLobbyNoticeText != (Object)null ? ((TMP_Text)_weaponLobbyNoticeText).text : _lastWeaponLobbyNoticeText);
		ApplyDefaultWeaponLobbyNoticeStyle();
		Canvas val = ResolveHudCanvas();
		if ((Object)val != (Object)null && TryAnchorWeaponLobbyNoticeToCompassPrompt(val, text))
		{
			return;
		}
		ApplyRightMiddleRect(_weaponLobbyNoticeRect, LobbyNoticeWidth * WeaponLobbyNoticeScaleMultiplier, GetScaledWeaponLobbyNoticePreferredHeight(text), LobbyNoticeRightOffset, LobbyNoticeDownOffset);
	}

	private void UpdateWeaponLobbyNotice()
	{
		if (!ShouldShowWeaponLobbyNotice())
		{
			CleanupWeaponLobbyNotice();
			return;
		}
		if ((Object)_weaponLobbyNoticeObject == (Object)null || (Object)_weaponLobbyNoticeText == (Object)null || !((Object)_weaponLobbyNoticeObject.transform.parent != (Object)null))
		{
			CreateWeaponLobbyNotice();
		}
		if ((Object)_weaponLobbyNoticeText == (Object)null)
		{
			return;
		}
		string lobbyWeaponNoticeTextCore = NormalizeLocalizedText(GetLobbyWeaponNoticeTextCore(GetCachedChineseLanguageSetting()));
		bool flag = false;
		if (!string.Equals(_lastWeaponLobbyNoticeText, lobbyWeaponNoticeTextCore, StringComparison.Ordinal))
		{
			_lastWeaponLobbyNoticeText = lobbyWeaponNoticeTextCore;
			((TMP_Text)_weaponLobbyNoticeText).text = lobbyWeaponNoticeTextCore;
			flag = true;
		}
		if (flag || Time.unscaledTime - _lastWeaponLobbyNoticeLayoutTime >= LobbyNoticeLayoutRefreshInterval)
		{
			ClampWeaponLobbyNoticeToCanvas();
			_lastWeaponLobbyNoticeLayoutTime = Time.unscaledTime;
		}
		_weaponLobbyNoticeObject.SetActive(true);
	}

	private void CreateFogModeLobbyNotice()
	{
		CleanupFogModeLobbyNotice();
	}

	private void CreateWeaponLobbyNotice()
	{
		CleanupWeaponLobbyNotice();
		Canvas val = ResolveHudCanvas();
		if ((Object)val == (Object)null)
		{
			return;
		}
		GameObject val2 = new GameObject("WeaponLobbyNotice");
		val2.transform.SetParent(((Component)val).transform, false);
		RectTransform obj = (_weaponLobbyNoticeRect = val2.AddComponent<RectTransform>());
		obj.anchoredPosition = Vector2.zero;
		string lobbyWeaponNoticeTextCore = NormalizeLocalizedText(GetLobbyWeaponNoticeTextCore(GetCachedChineseLanguageSetting()));
		obj.sizeDelta = new Vector2(LobbyNoticeWidth * WeaponLobbyNoticeScaleMultiplier, GetScaledWeaponLobbyNoticePreferredHeight(lobbyWeaponNoticeTextCore));
		_weaponLobbyNoticeObject = val2;
		_weaponLobbyNoticeText = val2.AddComponent<TextMeshProUGUI>();
		ApplyDefaultWeaponLobbyNoticeStyle();
		_lastWeaponLobbyNoticeText = lobbyWeaponNoticeTextCore;
		((TMP_Text)_weaponLobbyNoticeText).text = _lastWeaponLobbyNoticeText;
		ClampWeaponLobbyNoticeToCanvas();
		_lastWeaponLobbyNoticeLayoutTime = Time.unscaledTime;
	}

	private static void CleanupFogModeLobbyNotice()
	{
		if ((Object)_fogModeLobbyNoticeObject != (Object)null)
		{
			Object.Destroy((Object)_fogModeLobbyNoticeObject);
		}
		_fogModeLobbyNoticeObject = null;
		_fogModeLobbyNoticeText = null;
		_fogModeLobbyNoticeRect = null;
		_lastFogModeLobbyNoticeText = string.Empty;
		CleanupSharedFogBottomLeftRootIfUnused();
	}

	private static void CleanupWeaponLobbyNotice()
	{
		if ((Object)_weaponLobbyNoticeObject != (Object)null)
		{
			Object.Destroy((Object)_weaponLobbyNoticeObject);
		}
		_weaponLobbyNoticeObject = null;
		_weaponLobbyNoticeText = null;
		_weaponLobbyNoticeRect = null;
		_lastWeaponLobbyNoticeText = string.Empty;
		_lastWeaponLobbyNoticeLayoutTime = -10f;
	}

	private void ApplyDefaultWeaponLobbyNoticeStyle()
	{
		if (!((Object)_weaponLobbyNoticeText == (Object)null))
		{
			ApplyGameTextStyle(_weaponLobbyNoticeText, new Color(1f, 0.94f, 0.72f), LobbyNoticeFontSizeMultiplier * WeaponLobbyNoticeScaleMultiplier, LobbyNoticeLineSpacing * WeaponLobbyNoticeScaleMultiplier);
			((TMP_Text)_weaponLobbyNoticeText).fontStyle = (FontStyles)0;
			((TMP_Text)_weaponLobbyNoticeText).alignment = TextAlignmentOptions.MidlineRight;
			((TMP_Text)_weaponLobbyNoticeText).textWrappingMode = (TextWrappingModes)0;
			((TMP_Text)_weaponLobbyNoticeText).overflowMode = (TextOverflowModes)0;
		}
	}

	private TextMeshProUGUI ResolveLobbyCompassPromptText(Canvas hudCanvas)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		TextMeshProUGUI[] componentsInChildren = ((Component)hudCanvas).GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI val = null;
		int num = int.MinValue;
		TextMeshProUGUI[] array = componentsInChildren;
		foreach (TextMeshProUGUI val2 in array)
		{
			if ((Object)val2 == (Object)null || (Object)val2 == (Object)_weaponLobbyNoticeText)
			{
				continue;
			}
			string text2 = NormalizeLocalizedText(StripRichText(((TMP_Text)val2).text)).ToLowerInvariant();
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			int num2 = 0;
			if (text2.Contains("指南针") || text2.Contains("compass") || text2.Contains("罗盘"))
			{
				num2 += 240;
			}
			else
			{
				continue;
			}
			if (text2.Contains("按") || text2.Contains("press"))
			{
				num2 += 80;
			}
			if (text2.Contains("生成") || text2.Contains("spawn") || text2.Contains("get"))
			{
				num2 += 50;
			}
			string text3 = (((Object)val2).name ?? string.Empty).ToLowerInvariant();
			if (text3.Contains("prompt") || text3.Contains("interact") || text3.Contains("hint"))
			{
				num2 += 20;
			}
			RectTransform rectTransform = ((TMP_Text)val2).rectTransform;
			if ((Object)rectTransform != (Object)null)
			{
				Vector3 position = ((Transform)rectTransform).position;
				if (position.y > (float)Screen.height * 0.35f)
				{
					num2 += 25;
				}
				if (position.x > (float)Screen.width * 0.35f)
				{
					num2 += 25;
				}
				Rect rect = rectTransform.rect;
				if (rect.width > 60f && rect.width < 1200f)
				{
					num2 += 10;
				}
			}
			if (num2 > num)
			{
				num = num2;
				val = val2;
			}
		}
		if ((Object)val != (Object)null && num >= 300)
		{
			return val;
		}
		return null;
	}

	private bool TryAnchorWeaponLobbyNoticeToCompassPrompt(Canvas hudCanvas, string text)
	{
		if ((Object)hudCanvas == (Object)null || (Object)_weaponLobbyNoticeRect == (Object)null || (Object)_weaponLobbyNoticeText == (Object)null)
		{
			return false;
		}
		TextMeshProUGUI val = ResolveLobbyCompassPromptText(hudCanvas);
		if ((Object)val == (Object)null)
		{
			return false;
		}
		RectTransform rectTransform = ((TMP_Text)val).rectTransform;
		if ((Object)rectTransform == (Object)null || !(((Object)rectTransform.parent) is RectTransform))
		{
			return false;
		}
		RectTransform val2 = (RectTransform)((Transform)rectTransform).parent;
		if ((Object)(object)((Transform)_weaponLobbyNoticeRect).parent != (Object)(object)val2)
		{
			((Transform)_weaponLobbyNoticeRect).SetParent((Transform)val2, false);
		}
		((TMP_Text)_weaponLobbyNoticeText).fontSize = ((TMP_Text)val).fontSize;
		((TMP_Text)_weaponLobbyNoticeText).fontStyle = ((TMP_Text)val).fontStyle;
		((TMP_Text)_weaponLobbyNoticeText).alignment = ((TMP_Text)val).alignment;
		((TMP_Text)_weaponLobbyNoticeText).lineSpacing = ((TMP_Text)val).lineSpacing;
		((TMP_Text)_weaponLobbyNoticeText).textWrappingMode = (TextWrappingModes)0;
		((TMP_Text)_weaponLobbyNoticeText).overflowMode = (TextOverflowModes)0;
		Canvas.ForceUpdateCanvases();
		float num = Mathf.Clamp(((TMP_Text)_weaponLobbyNoticeText).preferredWidth + 24f, WeaponLobbyNoticeMinWidth, WeaponLobbyNoticeWidth);
		float num2 = Mathf.Max(((TMP_Text)_weaponLobbyNoticeText).preferredHeight + 8f, GetWeaponLobbyNoticePreferredHeight(text));
		_weaponLobbyNoticeRect.anchorMin = rectTransform.anchorMin;
		_weaponLobbyNoticeRect.anchorMax = rectTransform.anchorMax;
		_weaponLobbyNoticeRect.pivot = rectTransform.pivot;
		_weaponLobbyNoticeRect.sizeDelta = new Vector2(num, num2);
		Vector2 anchoredPosition = rectTransform.anchoredPosition;
		float num3 = rectTransform.rect.height * 0.5f + num2 * 0.5f + WeaponLobbyNoticeCompassVerticalGap;
		_weaponLobbyNoticeRect.anchoredPosition = new Vector2(anchoredPosition.x, anchoredPosition.y - num3);
		((Transform)_weaponLobbyNoticeRect).SetAsLastSibling();
		return true;
	}

	private static RectTransform ResolveSharedFogBottomLeftRoot(Canvas hudCanvas)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		if ((Object)_sharedFogBottomLeftRootRect == (Object)null || (Object)_sharedFogBottomLeftRootObject == (Object)null)
		{
			GameObject val = new GameObject("ShootZombiesFogBottomLeftRoot");
			_sharedFogBottomLeftRootObject = val;
			_sharedFogBottomLeftRootRect = val.AddComponent<RectTransform>();
		}
		Transform transform = ((Component)hudCanvas).transform;
		if ((Object)_sharedFogBottomLeftRootCanvas != (Object)hudCanvas || (Object)_sharedFogBottomLeftRootObject.transform.parent != (Object)transform)
		{
			_sharedFogBottomLeftRootObject.transform.SetParent(transform, false);
		}
		_sharedFogBottomLeftRootCanvas = hudCanvas;
		((Transform)_sharedFogBottomLeftRootRect).localScale = Vector3.one;
		_sharedFogBottomLeftRootRect.anchorMin = Vector2.zero;
		_sharedFogBottomLeftRootRect.anchorMax = Vector2.one;
		_sharedFogBottomLeftRootRect.pivot = new Vector2(0.5f, 0.5f);
		_sharedFogBottomLeftRootRect.offsetMin = Vector2.zero;
		_sharedFogBottomLeftRootRect.offsetMax = Vector2.zero;
		_sharedFogBottomLeftRootRect.anchoredPosition = Vector2.zero;
		_sharedFogBottomLeftRootObject.transform.SetAsLastSibling();
		return _sharedFogBottomLeftRootRect;
	}

	private static void CleanupSharedFogBottomLeftRootIfUnused()
	{
		if ((Object)_fogUIContainerRect != (Object)null || (Object)_fogModeLobbyNoticeRect != (Object)null)
		{
			return;
		}
		CleanupSharedFogBottomLeftRoot();
	}

	private static void CleanupSharedFogBottomLeftRoot()
	{
		if ((Object)_sharedFogBottomLeftRootObject != (Object)null)
		{
			Object.Destroy((Object)_sharedFogBottomLeftRootObject);
		}
		_sharedFogBottomLeftRootObject = null;
		_sharedFogBottomLeftRootRect = null;
		_sharedFogBottomLeftRootCanvas = null;
	}

	private static float GetLobbyNoticePreferredHeight(string text)
	{
		return (text != null && text.Contains("\n")) ? LobbyNoticeMultiLineHeight : LobbyNoticeSingleLineHeight;
	}

	private static float GetWeaponLobbyNoticePreferredHeight(string text)
	{
		return (text != null && text.Contains("\n")) ? WeaponLobbyNoticeMultiLineHeight : WeaponLobbyNoticeSingleLineHeight;
	}

	private static float GetScaledWeaponLobbyNoticePreferredHeight(string text)
	{
		return GetLobbyNoticePreferredHeight(text) * WeaponLobbyNoticeScaleMultiplier;
	}

	private RectTransform ResolveVersionLabelRect(Canvas hudCanvas, bool forceRefresh = false)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		if (!forceRefresh && (Object)_cachedVersionLabelRect != (Object)null && (Object)((Component)_cachedVersionLabelRect).GetComponentInParent<Canvas>() == (Object)hudCanvas && ((Component)_cachedVersionLabelRect).gameObject.activeInHierarchy)
		{
			return _cachedVersionLabelRect;
		}
		if (!forceRefresh && Time.unscaledTime - _lastVersionLabelSearchTime < 1f)
		{
			return null;
		}
		_lastVersionLabelSearchTime = Time.unscaledTime;
		TextMeshProUGUI[] componentsInChildren = ((Component)hudCanvas).GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI val = null;
		int num = int.MinValue;
		TextMeshProUGUI[] array = componentsInChildren;
		foreach (TextMeshProUGUI val2 in array)
		{
			if ((Object)val2 == (Object)null)
			{
				continue;
			}
			string text = StripRichText(((TMP_Text)val2).text);
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string text2 = text.ToLowerInvariant();
			string text3 = (((Object)val2).name ?? string.Empty).ToLowerInvariant() ?? string.Empty;
			int num2 = 0;
			if (text2.Contains("1.54"))
			{
				num2 += 320;
			}
			if (text2.Contains("version") || text2.Contains("ver"))
			{
				num2 += 150;
			}
			if (LooksLikeVersionToken(text2))
			{
				num2 += 120;
			}
			if (text3.Contains("version") || text3.Contains("ver"))
			{
				num2 += 80;
			}
			if (text2.Contains("fog") || text2.Contains("speed") || text2.Contains("actual"))
			{
				num2 -= 320;
			}
			if (text3.Contains("fog") || text3.Contains("timer") || text3.Contains("shootzombies"))
			{
				num2 -= 320;
			}
			if (text.Length <= 16)
			{
				num2 += 20;
			}
			RectTransform rectTransform = ((TMP_Text)val2).rectTransform;
			if ((Object)rectTransform != (Object)null)
			{
				if (((Component)rectTransform).transform.position.y > (float)Screen.height * 0.45f)
				{
					num2 += 15;
				}
				Rect rect = rectTransform.rect;
				if (rect.width > 8f)
				{
					rect = rectTransform.rect;
					if (rect.width < 320f)
					{
						num2 += 10;
					}
				}
			}
			if (num2 > num)
			{
				num = num2;
				val = val2;
			}
		}
		if ((Object)val != (Object)null && num >= 120)
		{
			_cachedVersionLabelRect = ((TMP_Text)val).rectTransform;
			return _cachedVersionLabelRect;
		}
		_cachedVersionLabelRect = null;
		return null;
	}

	private bool TryAnchorFogUiToVersion(RectTransform containerRect, Canvas hudCanvas, bool forceRefresh = false)
	{
		if ((Object)containerRect == (Object)null || (Object)hudCanvas == (Object)null)
		{
			return false;
		}
		RectTransform val = ResolveVersionLabelRect(hudCanvas, forceRefresh);
		if ((Object)val == (Object)null || (Object)((Transform)val).parent == (Object)null)
		{
			return false;
		}
		RectTransform val2 = (RectTransform)((Transform)val).parent;
		if ((Object)val2 == (Object)null)
		{
			return false;
		}
		if ((Object)(object)((Transform)containerRect).parent != (Object)(Transform)val2)
		{
			((Transform)containerRect).SetParent((Transform)val2, false);
		}
		containerRect.anchorMin = new Vector2(0f, 1f);
		containerRect.anchorMax = new Vector2(0f, 1f);
		containerRect.pivot = new Vector2(0f, 1f);
		Canvas.ForceUpdateCanvases();
		Vector3[] array = (Vector3[])(object)new Vector3[4];
		val.GetWorldCorners(array);
		Vector3 val3 = array[2];
		TextMeshProUGUI component = ((Component)val).GetComponent<TextMeshProUGUI>();
		Rect rect = val.rect;
		float num = rect.width;
		if ((Object)component != (Object)null)
		{
			num = Mathf.Max(num, ((TMP_Text)component).preferredWidth);
		}
		float num2 = Mathf.Max(FogUiVersionHorizontalGap, num * 0.08f);
		Vector3 val4 = val3 + ((Component)val).transform.right * num2;
		Vector3 val5 = ((Transform)val2).InverseTransformPoint(val4);
		float x = val5.x;
		rect = val2.rect;
		float num3 = x + rect.width * val2.pivot.x;
		float y = val5.y;
		rect = val2.rect;
		float num4 = y - rect.height * (1f - val2.pivot.y);
		containerRect.anchoredPosition = new Vector2(num3 + FogUiAnchorLeftNudge, num4 + FogUiAnchorTopNudge);
		((Transform)containerRect).SetAsLastSibling();
		return true;
	}

	private static string StripRichText(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		bool flag = false;
		foreach (char c in value)
		{
			switch (c)
			{
			case '<':
				flag = true;
				continue;
			case '>':
				flag = false;
				continue;
			}
			if (!flag)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static bool LooksLikeVersionToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		char[] separator = new char[17]
		{
			' ', '\n', '\r', '\t', '|', ':', '(', ')', '[', ']',
			'-', '_', '/', '\\', ',', ';', '!'
		};
		string[] array = value.Split(separator, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim().ToLowerInvariant();
			if (text.Length < 3 || text.Length > 10 || !text.Contains("."))
			{
				continue;
			}
			bool flag = false;
			bool flag2 = true;
			foreach (char c in text)
			{
				if (char.IsDigit(c))
				{
					flag = true;
					continue;
				}
				switch (c)
				{
				case '.':
				case 'a':
				case 'b':
				case 'c':
				case 'd':
				case 'e':
				case 'f':
				case 'g':
				case 'h':
				case 'i':
				case 'j':
				case 'k':
				case 'l':
				case 'm':
				case 'n':
				case 'o':
				case 'p':
				case 'q':
				case 'r':
				case 's':
				case 't':
				case 'u':
				case 'v':
				case 'w':
				case 'x':
				case 'y':
				case 'z':
					continue;
				}
				flag2 = false;
				break;
			}
			if (flag2 && flag)
			{
				return true;
			}
		}
		return false;
	}

	private void ClampFogUIToCanvas()
	{
		RectTransform val = _fogUIContainerRect;
		if ((Object)val == (Object)null && (Object)_fogUIObject != (Object)null && (Object)_fogUIObject.transform.parent != (Object)null)
		{
			val = (_fogUIContainerRect = ((Component)_fogUIObject.transform.parent).GetComponent<RectTransform>());
		}
		if ((Object)val == (Object)null)
		{
			return;
		}
		ApplySharedFogBottomLeftLayout(val, val.sizeDelta.x, val.sizeDelta.y);
	}

	private void ApplySharedFogBottomLeftLayout(RectTransform target, float width, float height)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		Canvas val = ResolveHudCanvas();
		if ((Object)val == (Object)null)
		{
			val = ((Component)target).GetComponentInParent<Canvas>();
		}
		if ((Object)val == (Object)null)
		{
			return;
		}
		Canvas.ForceUpdateCanvases();
		RectTransform component = ((Component)val).GetComponent<RectTransform>();
		if ((Object)component == (Object)null)
		{
			return;
		}
		if ((Object)(object)((Transform)target).parent != (Object)(object)((Component)val).transform)
		{
			((Transform)target).SetParent(((Component)val).transform, false);
		}
		ApplyBottomLeftAnchoredRect(target, component, width, height, 1f, 0f, 0f);
	}

	private void SetFogUIVisible(bool visible)
	{
		if ((Object)_fogUIObject != (Object)null)
		{
			_fogUIObject.SetActive(visible);
		}
		if ((Object)_fogTimerUIObject != (Object)null)
		{
			_fogTimerUIObject.SetActive(false);
		}
		if ((Object)_fogUIContainerRect != (Object)null)
		{
			((Component)_fogUIContainerRect).gameObject.SetActive(visible);
		}
	}

	private void SendFogSpeed()
	{
	}

	private void DisableFrostDamage()
	{
	}

	private void StartFogMovement()
	{
	}

	private void CreateFogUI()
	{
		CleanupOldFogUI();
	}

	private void UpdateFogUI()
	{
		CleanupOldFogUI();
	}

	private void CleanupOldFogUI()
	{
		try
		{
			if ((Object)_fogUIObject != (Object)null && (Object)_fogUIObject.transform.parent != (Object)null)
			{
				Object.Destroy((Object)((Component)_fogUIObject.transform.parent).gameObject);
			}
			else if ((Object)_fogUIObject != (Object)null)
			{
				Object.Destroy((Object)_fogUIObject);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			_fogUIObject = null;
			_fogTimerUIObject = null;
			_fogUIContainerRect = null;
			_cachedVersionLabelRect = null;
			CleanupSharedFogBottomLeftRootIfUnused();
		}
	}

	private static ItemInstanceData CreateItemInstanceData(int uses)
	{
		ItemInstanceData itemInstanceData = new ItemInstanceData(Guid.NewGuid());
		if (itemInstanceData.data == null)
		{
			itemInstanceData.data = new Dictionary<DataEntryKey, DataEntryValue>();
		}
		OptionableIntItemData optionableIntItemData = new OptionableIntItemData
		{
			HasData = true,
			Value = Mathf.Max(uses, 0)
		};
		itemInstanceData.data[DataEntryKey.ItemUses] = optionableIntItemData;
		ItemInstanceDataHandler.AddInstanceData(itemInstanceData);
		return itemInstanceData;
	}

	private static bool TrySetItemIntoSlot(Player player, int slotIndex, Item item, int uses)
	{
		if ((Object)player == (Object)null || (Object)item == (Object)null || player.itemSlots == null || slotIndex < 0 || slotIndex >= player.itemSlots.Length)
		{
			return false;
		}
		player.itemSlots[slotIndex].SetItem(item, CreateItemInstanceData(uses));
		return true;
	}

	private static bool TryResolveFirstAidItem(out Item item)
	{
		item = null;
		try
		{
			Type type = typeof(Item).Assembly.GetType("ItemDatabase");
			MethodInfo methodInfo = type?.GetMethod("TryGetItem", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
			{
				typeof(ushort),
				typeof(Item).MakeByRefType()
			}, null);
			if (methodInfo == null)
			{
				return false;
			}
			object[] array = new object[2]
			{
				(ushort)29,
				null
			};
			if ((bool)methodInfo.Invoke(null, array) && array[1] is Item { } item2 && (Object)item2 != (Object)null)
			{
				item = item2;
				return true;
			}
			ushort[] array2 = new ushort[11]
			{
				6, 1, 2, 3, 4, 5, 10, 15, 20, 25,
				30
			};
			foreach (ushort itemID in array2)
			{
				array[0] = itemID;
				array[1] = null;
				if (!(bool)methodInfo.Invoke(null, array) || !(array[1] is Item { } item3) || (Object)item3 == (Object)null)
				{
					continue;
				}
				item = item3;
				string text = item.GetName()?.ToLowerInvariant();
				if (text != null && (text.Contains("first aid") || text.Contains("medkit") || text.Contains("bandage") || text.Contains("heal") || text.Contains("急救") || text.Contains("医疗")))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		item = null;
		return false;
	}
}
