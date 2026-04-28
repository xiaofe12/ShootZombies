using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShootZombies;

internal sealed class LobbyConfigPanel
{
	private enum PanelThemeMode
	{
		Dark,
		Light,
		Transparent
	}

	private sealed class ConfigSource
	{
		public string Id;

		public string DisplayName;

		public ConfigFile Config;

		public bool IsShootZombies;

		public ConfigLocalizer Localizer;

		public List<ConfigSection> Sections = new List<ConfigSection>();
	}

	private sealed class ConfigLocalizer
	{
		public Func<string, string> LocalizeSectionName;

		public Func<string, string> LocalizeKeyName;

		public Func<string, string> LocalizeDescription;

		public Func<string, object, string> LocalizeOptionDisplayText;
	}

	private sealed class ConfigSection
	{
		public string Key;

		public string DisplayName;

		public List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();
	}

	private sealed class EntryBinding
	{
		public ConfigSource Source;

		public ConfigEntryBase Entry;

		public bool SuppressCallbacks;

		public Action Refresh;
	}

	private sealed class LobbyConfigPanelGuiHost : MonoBehaviour
	{
		public LobbyConfigPanel Owner;

		private void OnGUI()
		{
			Owner?.RenderImmediateGui();
		}
	}

	private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.18f);

	private static readonly Color PanelColor = new Color(0.13f, 0.15f, 0.22f, 0.93f);

	private static readonly Color HeaderColor = new Color(0.21f, 0.26f, 0.36f, 1f);

	private static readonly Color SidebarColor = new Color(0.11f, 0.12f, 0.17f, 1f);

	private static readonly Color ContentColor = new Color(0.22f, 0.31f, 0.42f, 0.97f);

	private static readonly Color CardColor = new Color(0.18f, 0.22f, 0.29f, 0.98f);

	private static readonly Color ButtonColor = new Color(0.28f, 0.31f, 0.39f, 1f);

	private static readonly Color ButtonActiveColor = new Color(0.22f, 0.62f, 0.54f, 1f);

	private static readonly Color ButtonSecondaryActiveColor = new Color(0.25f, 0.47f, 0.71f, 1f);

	private static readonly Color FieldColor = new Color(0.24f, 0.27f, 0.34f, 1f);

	private static readonly Color BorderColor = new Color(0.44f, 0.50f, 0.61f, 0.95f);

	private static readonly Color TextPrimaryColor = new Color(0.96f, 0.96f, 0.98f, 1f);

	private static readonly Color TextSecondaryColor = new Color(0.82f, 0.85f, 0.9f, 1f);

	private static readonly Color TextMutedColor = new Color(0.70f, 0.73f, 0.79f, 1f);

	private readonly Plugin _owner;

	private readonly List<ConfigSource> _sources = new List<ConfigSource>();

	private readonly List<EntryBinding> _visibleBindings = new List<EntryBinding>();

	private GameObject _rootObject;

	private Canvas _canvas;

	private RectTransform _panelRect;

	private RectTransform _pluginTabsRect;

	private RectTransform _sectionListRect;

	private RectTransform _entryListRect;

	private TextMeshProUGUI _panelTitleText;

	private TextMeshProUGUI _contentTitleText;

	private bool _isOpen;

	private bool _isDirty = true;

	private string _selectedSourceId = string.Empty;

	private string _selectedSectionKey = string.Empty;

	private ConfigEntryBase _pendingKeyCaptureEntry;

	private TextMeshProUGUI _pendingKeyCaptureText;

	private float _nextExternalRefreshTime;

	private bool _cursorStateCaptured;

	private bool _savedCursorVisible;

	private CursorLockMode _savedCursorLockMode;

	private bool _pendingRenderDiagnostic;

	private float _nextDiagnosticLogTime;

	private string _lastDiagnosticSummary = string.Empty;

	private Rect _windowRect;

	private Vector2 _entryScrollPosition;

	private readonly Dictionary<ConfigEntryBase, string> _textBufferByEntry = new Dictionary<ConfigEntryBase, string>();

	private static LobbyConfigPanel _cursorOwner;

	private PanelThemeMode _panelTheme;

	private readonly Dictionary<int, Texture2D> _immediateTextureCache = new Dictionary<int, Texture2D>();

	private GUIStyle _immediateWindowStyle;

	private GUIStyle _immediatePaneStyle;

	private GUIStyle _immediateCardStyle;

	private GUIStyle _immediateButtonStyle;

	private GUIStyle _immediateFieldStyle;

	private GUIStyle _immediateTextFieldStyle;

	private GUIStyle _immediateLabelStyle;

	private GUIStyle _immediateTitleStyle;

	private GUIStyle _immediateDescriptionStyle;

	public LobbyConfigPanel(Plugin owner)
	{
		_owner = owner;
		_panelTheme = GetConfiguredThemeMode();
	}

	private bool IsLightTheme()
	{
		return _panelTheme == PanelThemeMode.Light;
	}

	private bool IsTransparentTheme()
	{
		return _panelTheme == PanelThemeMode.Transparent;
	}

	private static bool IsChineseUi()
	{
		return Plugin.Instance?.GetCachedChineseLanguageSettingRuntime() ?? string.Equals(Plugin.GetLocalizedConfigSectionDisplayRuntime("Weapon"), "\u6b66\u5668", StringComparison.Ordinal);
	}

	private static string GetAkSoundOptionDisplayText(string value)
	{
		bool flag = IsChineseUi();
		return value switch
		{
			"ak_sound1" => (flag ? "\u97f3\u6548 1" : "Sound 1"),
			"ak_sound2" => (flag ? "\u97f3\u6548 2" : "Sound 2"),
			"ak_sound3" => (flag ? "\u97f3\u6548 3" : "Sound 3"),
			_ => value
		};
	}

	private static string GetPanelThemeOptionDisplayText(string value)
	{
		bool flag = IsChineseUi();
		return NormalizeThemeSelectionValue(value) switch
		{
			"light" => (flag ? "白色" : "Light"), 
			"transparent" => (flag ? "透明" : "Transparent"), 
			_ => (flag ? "黑色" : "Dark"), 
		};
	}

	private string GetThemeToggleIcon()
	{
		return _panelTheme switch
		{
			PanelThemeMode.Light => "\u263d", 
			PanelThemeMode.Transparent => "\u25cc", 
			_ => "\u2600", 
		};
	}

	private string GetSectionsLabel()
	{
		return IsChineseUi() ? "\u7ae0\u8282" : "Sections";
	}

	private string GetToggleStateLabel(bool value)
	{
		if (IsChineseUi())
		{
			return value ? "\u5f00\u542f" : "\u5173\u95ed";
		}
		return value ? "On" : "Off";
	}

	private string GetPressAnyKeyLabel()
	{
		return IsChineseUi() ? "\u6309\u4efb\u610f\u952e..." : "Press any key...";
	}

	private string GetNoVisibleEntriesLabel()
	{
		return IsChineseUi() ? "\u6ca1\u6709\u53ef\u663e\u793a\u7684\u914d\u7f6e\u9879\u3002" : "No visible settings.";
	}

	private float GetImmediateEntryContentWidth()
	{
		return Mathf.Clamp(_windowRect.width - 254f, 560f, 1040f);
	}

	private float GetImmediateEntryTitleWidth()
	{
		return Mathf.Clamp(GetImmediateEntryContentWidth() * 0.24f, 168f, 308f);
	}

	private float GetImmediateCompactControlWidth()
	{
		return Mathf.Clamp(GetImmediateEntryContentWidth() * 0.34f, 280f, 420f);
	}

	private float GetImmediateRangeControlWidth()
	{
		return Mathf.Clamp(GetImmediateEntryContentWidth() * 0.52f, 360f, 560f);
	}

	private float GetImmediateRangeSliderWidth()
	{
		return Mathf.Clamp(GetImmediateRangeControlWidth() - 92f, 260f, 460f);
	}

	private float GetImmediateDescriptionWidth()
	{
		return Mathf.Max(520f, GetImmediateEntryContentWidth() - 24f);
	}

	private void ToggleTheme()
	{
		SetTheme(GetNextThemeMode(_panelTheme));
	}

	private void SetTheme(PanelThemeMode theme, bool persist = true)
	{
		bool flag = _panelTheme != theme;
		if (flag)
		{
			_panelTheme = theme;
			_isDirty = true;
			if (Plugin.ShouldEmitVerboseInfoLogsRuntime())
			{
				Plugin.Log?.LogInfo((object)("[ShootZombies] LobbyConfigPanel theme changed to " + theme + "."));
			}
		}
		if (persist)
		{
			PersistThemeSelection(theme);
		}
	}

	internal static string NormalizeThemeSelectionValue(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "dark";
		}
		switch (value.Trim().ToLowerInvariant())
		{
		case "dark":
		case "black":
		case "黑":
			return "dark";
		case "light":
		case "white":
		case "白":
			return "light";
		case "transparent":
		case "clear":
		case "translucent":
		case "透":
		case "透明":
			return "transparent";
		default:
			return "dark";
		}
	}

	private static PanelThemeMode GetThemeModeFromConfigValue(string value)
	{
		return NormalizeThemeSelectionValue(value) switch
		{
			"light" => PanelThemeMode.Light, 
			"transparent" => PanelThemeMode.Transparent, 
			_ => PanelThemeMode.Dark, 
		};
	}

	private static string GetThemeConfigValue(PanelThemeMode theme)
	{
		return theme switch
		{
			PanelThemeMode.Light => "light", 
			PanelThemeMode.Transparent => "transparent", 
			_ => "dark", 
		};
	}

	private static PanelThemeMode GetNextThemeMode(PanelThemeMode current)
	{
		return current switch
		{
			PanelThemeMode.Dark => PanelThemeMode.Light, 
			PanelThemeMode.Light => PanelThemeMode.Transparent, 
			_ => PanelThemeMode.Dark, 
		};
	}

	private static PanelThemeMode GetConfiguredThemeMode()
	{
		return GetThemeModeFromConfigValue(Plugin.ConfigPanelTheme?.Value);
	}

	private void SyncThemeFromConfig()
	{
		SetTheme(GetConfiguredThemeMode(), persist: false);
	}

	private void PersistThemeSelection(PanelThemeMode theme)
	{
		if (Plugin.ConfigPanelTheme == null)
		{
			return;
		}
		string themeConfigValue = GetThemeConfigValue(theme);
		if (!string.Equals(NormalizeThemeSelectionValue(Plugin.ConfigPanelTheme.Value), themeConfigValue, StringComparison.Ordinal))
		{
			Plugin.ConfigPanelTheme.Value = themeConfigValue;
		}
		_owner?.SaveOwnedConfigRuntime();
	}

	private Color GetImmediateBackdropColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0f, 0f, 0f, 0.01f);
		}
		return IsLightTheme() ? new Color(0f, 0f, 0f, 0.06f) : new Color(0f, 0f, 0f, 0.10f);
	}

	private Color GetImmediateWindowColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0.04f, 0.04f, 0.04f, 0.34f);
		}
		return IsLightTheme() ? new Color(0.97f, 0.97f, 0.97f, 1f) : new Color(0.05f, 0.05f, 0.05f, 1f);
	}

	private Color GetImmediatePaneColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0.08f, 0.08f, 0.08f, 0.24f);
		}
		return IsLightTheme() ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.09f, 0.09f, 0.09f, 1f);
	}

	private Color GetImmediateCardColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0.11f, 0.11f, 0.11f, 0.28f);
		}
		return IsLightTheme() ? new Color(0.98f, 0.98f, 0.98f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);
	}

	private Color GetImmediateFieldColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0.16f, 0.16f, 0.16f, 0.36f);
		}
		return IsLightTheme() ? new Color(0.87f, 0.87f, 0.87f, 1f) : new Color(0.20f, 0.20f, 0.20f, 1f);
	}

	private Color GetImmediateFieldFocusColor()
	{
		if (IsTransparentTheme())
		{
			return new Color(0.20f, 0.20f, 0.20f, 0.44f);
		}
		return IsLightTheme() ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);
	}

	private Color GetImmediateButtonColor(bool active)
	{
		if (IsTransparentTheme())
		{
			return active ? new Color(0.34f, 0.34f, 0.34f, 0.78f) : new Color(0.18f, 0.18f, 0.18f, 0.58f);
		}
		if (IsLightTheme())
		{
			return active ? new Color(0.72f, 0.72f, 0.72f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
		}
		return active ? new Color(0.34f, 0.34f, 0.34f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
	}

	private Color GetImmediateThemeButtonColor(PanelThemeMode theme, bool active)
	{
		if (theme == PanelThemeMode.Light)
		{
			return active ? new Color(0.98f, 0.98f, 0.98f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);
		}
		if (theme == PanelThemeMode.Transparent)
		{
			return active ? new Color(0.18f, 0.18f, 0.18f, 0.82f) : new Color(0.22f, 0.22f, 0.22f, 0.48f);
		}
		return active ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.28f, 0.28f, 0.28f, 1f);
	}

	private Color GetImmediateThemeButtonTextColor(PanelThemeMode theme, bool active)
	{
		if (theme == PanelThemeMode.Light)
		{
			return new Color(0.08f, 0.08f, 0.08f, 1f);
		}
		if (theme == PanelThemeMode.Transparent)
		{
			return new Color(0.96f, 0.96f, 0.96f, active ? 1f : 0.92f);
		}
		return active ? new Color(0.96f, 0.96f, 0.96f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
	}

	private Color GetImmediateTextPrimaryColor()
	{
		return IsLightTheme() ? new Color(0.08f, 0.08f, 0.08f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f);
	}

	private Color GetImmediateTextMutedColor()
	{
		return IsLightTheme() ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
	}

	private Texture2D GetSolidTexture(Color color)
	{
		Color32 color2 = color;
		int key = color2.r | color2.g << 8 | color2.b << 16 | color2.a << 24;
		if (_immediateTextureCache.TryGetValue(key, out var value) && value != null)
		{
			return value;
		}
		Texture2D texture2D = new Texture2D(1, 1, TextureFormat.ARGB32, mipChain: false);
		texture2D.hideFlags = HideFlags.HideAndDontSave;
		texture2D.SetPixel(0, 0, color);
		texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
		_immediateTextureCache[key] = texture2D;
		return texture2D;
	}

	private void EnsureImmediateStyles()
	{
		_immediateWindowStyle ??= new GUIStyle(GUI.skin.window);
		_immediatePaneStyle ??= new GUIStyle(GUI.skin.box);
		_immediateCardStyle ??= new GUIStyle(GUI.skin.box);
		_immediateButtonStyle ??= new GUIStyle(GUI.skin.button);
		_immediateFieldStyle ??= new GUIStyle(GUI.skin.box);
		_immediateTextFieldStyle ??= new GUIStyle(GUI.skin.textField);
		_immediateLabelStyle ??= new GUIStyle(GUI.skin.label);
		_immediateTitleStyle ??= new GUIStyle(GUI.skin.label);
		_immediateDescriptionStyle ??= new GUIStyle(GUI.skin.label);
		Texture2D solidTexture = GetSolidTexture(Color.white);
		Color immediateTextPrimaryColor = GetImmediateTextPrimaryColor();
		Color immediateTextMutedColor = GetImmediateTextMutedColor();
		_immediateWindowStyle.normal.background = GetSolidTexture(GetImmediateWindowColor());
		_immediateWindowStyle.onNormal.background = _immediateWindowStyle.normal.background;
		_immediateWindowStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateWindowStyle.alignment = TextAnchor.UpperLeft;
		_immediateWindowStyle.fontSize = 15;
		_immediateWindowStyle.fontStyle = FontStyle.Bold;
		_immediateWindowStyle.padding = new RectOffset(12, 12, 12, 12);
		_immediatePaneStyle.normal.background = GetSolidTexture(GetImmediatePaneColor());
		_immediatePaneStyle.normal.textColor = immediateTextPrimaryColor;
		_immediatePaneStyle.border = new RectOffset(1, 1, 1, 1);
		_immediateCardStyle.normal.background = GetSolidTexture(GetImmediateCardColor());
		_immediateCardStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateCardStyle.alignment = TextAnchor.UpperLeft;
		_immediateCardStyle.padding = new RectOffset(10, 10, 8, 8);
		_immediateButtonStyle.normal.background = solidTexture;
		_immediateButtonStyle.hover.background = solidTexture;
		_immediateButtonStyle.active.background = solidTexture;
		_immediateButtonStyle.focused.background = solidTexture;
		_immediateButtonStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateButtonStyle.hover.textColor = immediateTextPrimaryColor;
		_immediateButtonStyle.active.textColor = immediateTextPrimaryColor;
		_immediateButtonStyle.focused.textColor = immediateTextPrimaryColor;
		_immediateButtonStyle.alignment = TextAnchor.MiddleCenter;
		_immediateFieldStyle.normal.background = GetSolidTexture(GetImmediateFieldColor());
		_immediateFieldStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateFieldStyle.alignment = TextAnchor.MiddleCenter;
		_immediateFieldStyle.padding = new RectOffset(8, 8, 6, 6);
		_immediateTextFieldStyle.normal.background = GetSolidTexture(GetImmediateFieldColor());
		_immediateTextFieldStyle.focused.background = GetSolidTexture(GetImmediateFieldFocusColor());
		_immediateTextFieldStyle.hover.background = _immediateTextFieldStyle.normal.background;
		_immediateTextFieldStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateTextFieldStyle.focused.textColor = immediateTextPrimaryColor;
		_immediateTextFieldStyle.hover.textColor = immediateTextPrimaryColor;
		_immediateTextFieldStyle.padding = new RectOffset(8, 8, 6, 6);
		_immediateLabelStyle.fontSize = 12;
		_immediateLabelStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateTitleStyle.fontSize = 14;
		_immediateTitleStyle.fontStyle = FontStyle.Bold;
		_immediateTitleStyle.normal.textColor = immediateTextPrimaryColor;
		_immediateDescriptionStyle.fontSize = 10;
		_immediateDescriptionStyle.wordWrap = true;
		_immediateDescriptionStyle.normal.textColor = immediateTextMutedColor;
	}

	internal static bool IsCursorOwnershipActive()
	{
		return _cursorOwner != null && _cursorOwner._isOpen;
	}

	public void Tick()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		bool flag = Plugin.IsLobbySceneRuntime(activeScene) || Plugin.IsGameplaySceneRuntime(activeScene);
		if (!flag)
		{
			if (_isOpen)
			{
				Close();
			}
			return;
		}
		SyncThemeFromConfig();
		if (_pendingKeyCaptureEntry != null)
		{
			HandleKeyCapture();
			return;
		}
		if (ShouldTogglePanel())
		{
			if (_isOpen)
			{
				Close();
			}
			else
			{
				Open();
			}
			return;
		}
		if (!_isOpen)
		{
			return;
		}
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Close();
			return;
		}
		if (_rootObject == null)
		{
			BuildUi();
			_isDirty = true;
		}
		ClampPanelToScreen();
		EnsureWindowRectInitialized();
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		if (_isDirty || Time.unscaledTime >= _nextExternalRefreshTime)
		{
			RefreshDataModel();
			RefreshUi();
			_nextExternalRefreshTime = Time.unscaledTime + 0.5f;
			_isDirty = false;
		}
	}

	public void Dispose()
	{
		Close();
		if (_cursorOwner == this)
		{
			_cursorOwner = null;
		}
		if (_rootObject != null)
		{
			UnityEngine.Object.Destroy(_rootObject);
			_rootObject = null;
			_canvas = null;
			_panelRect = null;
			_pluginTabsRect = null;
			_sectionListRect = null;
			_entryListRect = null;
			_panelTitleText = null;
			_contentTitleText = null;
		}
		foreach (Texture2D value in _immediateTextureCache.Values)
		{
			if (value != null)
			{
				UnityEngine.Object.Destroy(value);
			}
		}
		_immediateTextureCache.Clear();
	}

	private void Open()
	{
		EnsureEventSystem();
		SyncThemeFromConfig();
		if (_rootObject == null)
		{
			BuildUi();
		}
		_rootObject.SetActive(true);
		if ((UnityEngine.Object)_canvas != (UnityEngine.Object)null)
		{
			_canvas.enabled = false;
		}
		ClampPanelToScreen();
		Canvas.ForceUpdateCanvases();
		RefreshDataModel();
		RefreshUi();
		CaptureCursorState();
		EnsureWindowRectInitialized(forceReset: _windowRect.width <= 1f || _windowRect.height <= 1f);
		_cursorOwner = this;
		EventSystem.current?.SetSelectedGameObject(null);
		_isOpen = true;
		_pendingRenderDiagnostic = true;
		_isDirty = false;
		_nextExternalRefreshTime = Time.unscaledTime + 0.5f;
		if (Plugin.ShouldEmitVerboseInfoLogsRuntime())
		{
			Plugin.Log?.LogInfo((object)"[ShootZombies] LobbyConfigPanel opened.");
		}
	}

	public void NotifyLanguageChanged(bool isChineseLanguage)
	{
		_selectedSectionKey = string.Empty;
		_isDirty = true;
		_nextExternalRefreshTime = 0f;
		if (!_isOpen || _rootObject == null)
		{
			return;
		}
		RefreshDataModel();
		RefreshUi();
		_isDirty = false;
		_nextExternalRefreshTime = Time.unscaledTime + 0.5f;
		_pendingRenderDiagnostic = true;
		if (Plugin.ShouldEmitVerboseInfoLogsRuntime())
		{
			Plugin.Log?.LogInfo((object)("[ShootZombies] LobbyConfigPanel language refresh applied. isChinese=" + isChineseLanguage + "."));
		}
	}

	private void Close()
	{
		_pendingKeyCaptureEntry = null;
		_pendingKeyCaptureText = null;
		if (_rootObject != null)
		{
			_rootObject.SetActive(false);
		}
		if (_cursorOwner == this)
		{
			_cursorOwner = null;
		}
		RestoreCursorState();
		_isOpen = false;
		_pendingRenderDiagnostic = false;
	}

	private bool ShouldTogglePanel()
	{
		if (IsTypingIntoInputField())
		{
			return false;
		}
		return Input.GetKeyDown(GetOpenKey());
	}

	private KeyCode GetOpenKey()
	{
		if (Plugin.OpenConfigPanelKey != null && (int)Plugin.OpenConfigPanelKey.Value != 0)
		{
			return Plugin.OpenConfigPanelKey.Value;
		}
		return KeyCode.Backslash;
	}

	private void HandleKeyCapture()
	{
		if (_pendingKeyCaptureEntry == null)
		{
			return;
		}
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			CancelKeyCapture();
			return;
		}
		foreach (KeyCode value in Enum.GetValues(typeof(KeyCode)))
		{
			if (value == KeyCode.None || !Input.GetKeyDown(value))
			{
				continue;
			}
			TryAssignBoxedValue(_pendingKeyCaptureEntry, value);
			SaveEntryOwner(_pendingKeyCaptureEntry);
			CancelKeyCapture();
			RefreshVisibleBindings();
			return;
		}
	}

	private void CancelKeyCapture()
	{
		_pendingKeyCaptureEntry = null;
		_pendingKeyCaptureText = null;
		RefreshVisibleBindings();
	}

	private void CaptureCursorState()
	{
		if (_cursorStateCaptured)
		{
			return;
		}
		_savedCursorVisible = Cursor.visible;
		_savedCursorLockMode = Cursor.lockState;
		_cursorStateCaptured = true;
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
	}

	private void RestoreCursorState()
	{
		if (!_cursorStateCaptured)
		{
			return;
		}
		Cursor.visible = _savedCursorVisible;
		Cursor.lockState = _savedCursorLockMode;
		_cursorStateCaptured = false;
	}

	private void EnterPauseMenuMode()
	{
	}

	private void ExitPauseMenuMode()
	{
	}

	private void RefreshDataModel()
	{
		_sources.Clear();
		ConfigSource item = BuildShootZombiesSource();
		if (item != null)
		{
			_sources.Add(item);
		}
		ConfigSource fogAndColdControlSource = BuildFogAndColdControlSource();
		if (fogAndColdControlSource != null)
		{
			_sources.Add(fogAndColdControlSource);
		}
		if (_sources.Count == 0)
		{
			_selectedSourceId = string.Empty;
			_selectedSectionKey = string.Empty;
			return;
		}
		if (!_sources.Any((ConfigSource source) => string.Equals(source.Id, _selectedSourceId, StringComparison.Ordinal)))
		{
			_selectedSourceId = _sources[0].Id;
		}
		ConfigSource configSource = GetSelectedSource();
		if (configSource == null || configSource.Sections.Count == 0)
		{
			_selectedSectionKey = string.Empty;
			return;
		}
		if (!configSource.Sections.Any((ConfigSection section) => string.Equals(section.Key, _selectedSectionKey, StringComparison.Ordinal)))
		{
			_selectedSectionKey = configSource.Sections[0].Key;
		}
		MaybeLogDiagnostics();
	}

	private ConfigSource BuildShootZombiesSource()
	{
		if (Plugin.Instance == null)
		{
			return null;
		}
		ConfigFile config = ((BaseUnityPlugin)Plugin.Instance).Config;
		return BuildSource("shootzombies", "ShootZombies", config, CreateShootZombiesLocalizer(), isShootZombies: true);
	}

	private bool GetCurrentLanguageIsChinese()
	{
		return _owner?.GetCachedChineseLanguageSettingRuntime() ?? IsChineseUi();
	}

	private ConfigSource BuildFogAndColdControlSource()
	{
		bool currentLanguageIsChinese = GetCurrentLanguageIsChinese();
		foreach (PluginInfo value in Chainloader.PluginInfos.Values)
		{
			BaseUnityPlugin baseUnityPlugin = value?.Instance as BaseUnityPlugin;
			if (baseUnityPlugin == null || baseUnityPlugin == Plugin.Instance || !IsFogAndColdControlPlugin(value))
			{
				continue;
			}
			string text = value.Metadata?.GUID;
			if (string.IsNullOrWhiteSpace(text))
			{
				text = "thanks.fogandcoldcontrol";
			}
			string text2 = value.Metadata?.Name;
			if (string.IsNullOrWhiteSpace(text2))
			{
				text2 = "Fog&ColdControl";
			}
			return BuildSource(text, text2, baseUnityPlugin.Config, TryCreatePluginLocalizer(baseUnityPlugin, currentLanguageIsChinese), isShootZombies: false);
		}
		return null;
	}

	private static bool IsFogAndColdControlPlugin(PluginInfo info)
	{
		if (info == null)
		{
			return false;
		}
		string[] array = new string[4]
		{
			info.Metadata?.GUID,
			info.Metadata?.Name,
			info.Instance?.GetType().Assembly.GetName().Name,
			Path.GetFileNameWithoutExtension(info.Location ?? string.Empty)
		};
		string[] array2 = new string[4] { "Thanks.Fog&ColdControl", "Fog&ColdControl", "FogAndColdControl", "FogColdControl" };
		return array.Any((string candidate) => !string.IsNullOrWhiteSpace(candidate) && array2.Any((string matchToken) => candidate.IndexOf(matchToken, StringComparison.OrdinalIgnoreCase) >= 0));
	}

	private static ConfigLocalizer CreateShootZombiesLocalizer()
	{
		return new ConfigLocalizer
		{
			LocalizeSectionName = delegate(string section)
			{
				return Plugin.GetLocalizedConfigSectionDisplayRuntime(section);
			},
			LocalizeKeyName = delegate(string key)
			{
				return Plugin.GetLocalizedConfigKeyDisplayRuntime(key);
			},
			LocalizeDescription = delegate(string key)
			{
				return Plugin.GetLocalizedConfigDescriptionRuntime(key);
			},
			LocalizeOptionDisplayText = delegate(string key, object option)
			{
				return GetShootZombiesOptionDisplayText(key, option);
			}
		};
	}

	private static ConfigLocalizer TryCreatePluginLocalizer(BaseUnityPlugin pluginInstance, bool isChineseLanguage)
	{
		if (pluginInstance == null)
		{
			return null;
		}
		ConfigLocalizer configLocalizer = TryCreateConfigKeyEnumPluginLocalizer(pluginInstance, isChineseLanguage);
		if (configLocalizer != null)
		{
			return configLocalizer;
		}
		Type type = pluginInstance.GetType();
		if (!HasMethod(type, "GetLocalizedConfigKeyDisplayRuntime") && !HasMethod(type, "GetLocalizedKeyName") && !HasMethod(type, "GetKeyName") && !HasMethod(type, "GetLocalizedConfigSectionDisplayRuntime") && !HasMethod(type, "GetLocalizedSectionName") && !HasMethod(type, "GetSectionName") && !HasMethod(type, "GetLocalizedConfigDescriptionRuntime") && !HasMethod(type, "GetLocalizedDescription") && !HasMethod(type, "GetLocalizedConfigOptionDisplayRuntime") && !HasMethod(type, "GetLocalizedConfigOptionDisplayTextRuntime") && !HasMethod(type, "GetLocalizedOptionDisplayText") && !HasMethod(type, "GetOptionDisplayText"))
		{
			return null;
		}
		return new ConfigLocalizer
		{
			LocalizeSectionName = delegate(string section)
			{
				return TryLocalizePluginSectionName(pluginInstance, section, isChineseLanguage) ?? section;
			},
			LocalizeKeyName = delegate(string key)
			{
				return TryLocalizePluginKeyName(pluginInstance, key, isChineseLanguage) ?? key;
			},
			LocalizeDescription = delegate(string key)
			{
				return TryLocalizePluginDescription(pluginInstance, key, isChineseLanguage) ?? string.Empty;
			},
			LocalizeOptionDisplayText = delegate(string key, object option)
			{
				return TryLocalizePluginOptionDisplayText(pluginInstance, key, option, isChineseLanguage) ?? (option?.ToString() ?? string.Empty);
			}
		};
	}

	private ConfigSource BuildSource(string id, string displayName, ConfigFile config, ConfigLocalizer localizer, bool isShootZombies)
	{
		ConfigEntryBase[] configEntriesSnapshotRuntime = Plugin.GetConfigEntriesSnapshotRuntime(config);
		if (configEntriesSnapshotRuntime.Length == 0)
		{
			return null;
		}
		ConfigSource configSource = new ConfigSource
		{
			Id = id,
			DisplayName = displayName,
			Config = config,
			IsShootZombies = isShootZombies,
			Localizer = localizer
		};
		IEnumerable<IGrouping<string, ConfigEntryBase>> enumerable = from entry in configEntriesSnapshotRuntime
			where entry != null && entry.Definition != null && (!isShootZombies || Plugin.ShouldExposeOwnedConfigEntryRuntime(entry))
			group entry by (entry.Definition.Section ?? string.Empty);
		foreach (IGrouping<string, ConfigEntryBase> item in enumerable.OrderBy((IGrouping<string, ConfigEntryBase> group) => GetSectionSortIndex(configSource, group.Key)).ThenBy((IGrouping<string, ConfigEntryBase> group) => GetSectionDisplayName(configSource, group.Key), StringComparer.OrdinalIgnoreCase))
		{
			ConfigSection configSection = new ConfigSection
			{
				Key = item.Key,
				DisplayName = GetSectionDisplayName(configSource, item.Key)
			};
			configSection.Entries.AddRange(item.OrderBy((ConfigEntryBase entry) => GetEntrySortIndex(configSource, entry)).ThenBy((ConfigEntryBase entry) => GetEntryDisplayName(configSource, entry), StringComparer.OrdinalIgnoreCase));
			if (configSection.Entries.Count > 0)
			{
				configSource.Sections.Add(configSection);
			}
		}
		return (configSource.Sections.Count != 0) ? configSource : null;
	}

	private static string GetShootZombiesOptionDisplayText(string key, object option)
	{
		string text = option?.ToString() ?? string.Empty;
		if (string.Equals(key, "Config Panel Theme", StringComparison.Ordinal))
		{
			return GetPanelThemeOptionDisplayText(text);
		}
		if (string.Equals(key, "AK Sound", StringComparison.Ordinal))
		{
			return GetAkSoundOptionDisplayText(text);
		}
		if (string.Equals(key, "Weapon Selection", StringComparison.Ordinal))
		{
			return text switch
			{
				"AK47" => "AK47",
				"HK416" => "HK416",
				"MPX" => "MPX",
				_ => text
			};
		}
		if (string.Equals(key, "Behavior Difficulty", StringComparison.Ordinal))
		{
			return Plugin.GetZombieBehaviorDifficultyDisplayNameRuntime(text);
		}
		return text;
	}

	private static bool HasMethod(Type type, string methodName)
	{
		if (type == null || string.IsNullOrWhiteSpace(methodName))
		{
			return false;
		}
		return type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any((MethodInfo method) => string.Equals(method.Name, methodName, StringComparison.Ordinal));
	}

	private static bool TryResolvePluginChineseLanguage(BaseUnityPlugin pluginInstance)
	{
		if (pluginInstance != null)
		{
			Type type = pluginInstance.GetType();
			if (TryInvokeCompatibleMethod(type, pluginInstance, "GetCachedChineseLanguageSetting", out var result) && result is bool flag)
			{
				return flag;
			}
			if (TryInvokeCompatibleMethod(type, pluginInstance, "IsChineseLanguage", out result) && result is bool flag2)
			{
				return flag2;
			}
		}
			return string.Equals(Plugin.GetLocalizedConfigSectionDisplayRuntime("Weapon"), "\u6b66\u5668", StringComparison.Ordinal);
		}

	private static ConfigLocalizer TryCreateConfigKeyEnumPluginLocalizer(BaseUnityPlugin pluginInstance, bool isChineseLanguage)
	{
		if (pluginInstance == null)
		{
			return null;
			}
			Type type = pluginInstance.GetType();
			Type nestedType = type.GetNestedType("ConfigKey", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (nestedType == null || !nestedType.IsEnum)
			{
				return null;
			}
			MethodInfo method = FindCompatibleMethod(type, "GetKeyName", nestedType, typeof(bool));
			MethodInfo method2 = FindCompatibleMethod(type, "GetSectionName", nestedType, typeof(bool));
			MethodInfo method3 = FindCompatibleMethod(type, "GetLocalizedDescription", nestedType, typeof(bool));
		if (method == null || method2 == null || method3 == null)
		{
			return null;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (object value in Enum.GetValues(nestedType))
		{
			string text = InvokeStringMethod(method, pluginInstance, value, false);
			string text2 = InvokeStringMethod(method, pluginInstance, value, true);
			string text3 = InvokeStringMethod(method, pluginInstance, value, isChineseLanguage);
			AddLocalizationAlias(dictionary, text, text3);
			AddLocalizationAlias(dictionary, text2, text3);
			string text4 = InvokeStringMethod(method2, pluginInstance, value, false);
			string text5 = InvokeStringMethod(method2, pluginInstance, value, true);
			string text6 = InvokeStringMethod(method2, pluginInstance, value, isChineseLanguage);
			AddLocalizationAlias(dictionary2, text4, text6);
			AddLocalizationAlias(dictionary2, text5, text6);
			string text7 = InvokeStringMethod(method3, pluginInstance, value, false);
			string text8 = InvokeStringMethod(method3, pluginInstance, value, true);
			string text9 = InvokeStringMethod(method3, pluginInstance, value, isChineseLanguage);
			AddLocalizationAlias(dictionary3, text, text9);
			AddLocalizationAlias(dictionary3, text2, text9);
			AddLocalizationAlias(dictionary3, text7, text9);
				AddLocalizationAlias(dictionary3, text8, text9);
			}
			if (dictionary.Count == 0 && dictionary2.Count == 0 && dictionary3.Count == 0)
			{
				return null;
			}
			return new ConfigLocalizer
			{
				LocalizeSectionName = delegate(string section)
				{
					return LookupLocalization(dictionary2, section);
				},
				LocalizeKeyName = delegate(string key)
				{
					return LookupLocalization(dictionary, key);
				},
				LocalizeDescription = delegate(string key)
				{
					return LookupLocalization(dictionary3, key);
				},
				LocalizeOptionDisplayText = delegate(string key, object option)
				{
					return option?.ToString() ?? string.Empty;
				}
			};
		}

		private static MethodInfo FindCompatibleMethod(Type declaringType, string methodName, params Type[] parameterTypes)
		{
			if (declaringType == null || string.IsNullOrWhiteSpace(methodName))
			{
				return null;
			}
			return declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo method)
			{
				if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length != parameterTypes.Length)
				{
					return false;
				}
				for (int i = 0; i < parameterTypes.Length; i++)
				{
					if (parameters[i].ParameterType != parameterTypes[i])
					{
						return false;
					}
				}
				return method.ReturnType == typeof(string);
			});
		}

		private static string InvokeStringMethod(MethodInfo method, object target, params object[] arguments)
		{
			if (method == null)
			{
				return null;
			}
			try
			{
				object obj = method.Invoke(method.IsStatic ? null : target, arguments);
				return obj as string ?? obj?.ToString();
			}
			catch
			{
			}
			return null;
		}

		private static void AddLocalizationAlias(Dictionary<string, string> map, string source, string target)
		{
			if (map == null || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
			{
				return;
			}
			map[source] = target;
		}

		private static string LookupLocalization(Dictionary<string, string> map, string value)
		{
			if (map == null || string.IsNullOrWhiteSpace(value))
			{
				return null;
			}
			return map.TryGetValue(value, out var value2) ? value2 : null;
		}

		private static string TryLocalizePluginSectionName(BaseUnityPlugin pluginInstance, string section, bool isChinese)
		{
			if (pluginInstance == null || string.IsNullOrWhiteSpace(section))
		{
			return null;
		}
		Type type = pluginInstance.GetType();
		return TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigSectionDisplayRuntime", section) ?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedSectionName", section, isChinese);
	}

	private static string TryLocalizePluginKeyName(BaseUnityPlugin pluginInstance, string key, bool isChinese)
	{
		if (pluginInstance == null || string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		Type type = pluginInstance.GetType();
		return TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigKeyDisplayRuntime", key) ?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedKeyName", key, isChinese);
	}

	private static string TryLocalizePluginDescription(BaseUnityPlugin pluginInstance, string key, bool isChinese)
	{
		if (pluginInstance == null || string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		Type type = pluginInstance.GetType();
		return TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigDescriptionRuntime", key) ?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedDescription", key, isChinese);
	}

	private static string TryLocalizePluginOptionDisplayText(BaseUnityPlugin pluginInstance, string key, object option, bool isChinese)
	{
		if (pluginInstance == null)
		{
			return null;
		}
		Type type = pluginInstance.GetType();
		string text = option?.ToString() ?? string.Empty;
		return TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigOptionDisplayRuntime", key, option)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigOptionDisplayRuntime", key, text)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigOptionDisplayTextRuntime", key, option)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedConfigOptionDisplayTextRuntime", key, text)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedOptionDisplayText", key, option, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedOptionDisplayText", key, text, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedOptionDisplayText", option, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetLocalizedOptionDisplayText", text, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetOptionDisplayText", key, option, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetOptionDisplayText", key, text, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetOptionDisplayText", option, isChinese)
			?? TryInvokeStringCompatibleMethod(type, pluginInstance, "GetOptionDisplayText", text, isChinese);
	}

	private static string TryInvokeStringCompatibleMethod(Type declaringType, object target, string methodName, params object[] suppliedArguments)
	{
		if (!TryInvokeCompatibleMethod(declaringType, target, methodName, out var result, suppliedArguments) || result == null)
		{
			return null;
		}
		return result as string ?? result.ToString();
	}

	private static bool TryInvokeCompatibleMethod(Type declaringType, object target, string methodName, out object result, params object[] suppliedArguments)
	{
		result = null;
		if (declaringType == null || string.IsNullOrWhiteSpace(methodName))
		{
			return false;
		}
		MethodInfo[] array = declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where((MethodInfo method) => string.Equals(method.Name, methodName, StringComparison.Ordinal)).OrderBy((MethodInfo method) => Math.Abs(method.GetParameters().Length - (suppliedArguments?.Length ?? 0))).ThenBy((MethodInfo method) => method.GetParameters().Length).ToArray();
		foreach (MethodInfo methodInfo in array)
		{
			if (!methodInfo.IsStatic && target == null)
			{
				continue;
			}
			if (!TryBuildCompatibleInvokeArguments(methodInfo, suppliedArguments, out var invokeArguments))
			{
				continue;
			}
			try
			{
				result = methodInfo.Invoke(methodInfo.IsStatic ? null : target, invokeArguments);
				return true;
			}
			catch (TargetParameterCountException)
			{
			}
			catch (ArgumentException)
			{
			}
			catch (TargetInvocationException)
			{
				return false;
			}
			catch
			{
				return false;
			}
		}
		return false;
	}

	private static bool TryBuildCompatibleInvokeArguments(MethodInfo method, object[] suppliedArguments, out object[] invokeArguments)
	{
		invokeArguments = null;
		if (method == null)
		{
			return false;
		}
		suppliedArguments = suppliedArguments ?? Array.Empty<object>();
		ParameterInfo[] parameters = method.GetParameters();
		if (suppliedArguments.Length > parameters.Length)
		{
			return false;
		}
		object[] array = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo parameterInfo = parameters[i];
			if (i < suppliedArguments.Length)
			{
				if (!TryCoerceInvokeArgument(parameterInfo.ParameterType, suppliedArguments[i], out array[i]))
				{
					return false;
				}
			}
			else if (parameterInfo.HasDefaultValue)
			{
				array[i] = parameterInfo.DefaultValue;
			}
			else
			{
				Type type = parameterInfo.ParameterType.IsByRef ? parameterInfo.ParameterType.GetElementType() : parameterInfo.ParameterType;
				if (type == null)
				{
					return false;
				}
				if (Nullable.GetUnderlyingType(parameterInfo.ParameterType) != null || !type.IsValueType)
				{
					array[i] = null;
				}
				else
				{
					return false;
				}
			}
		}
		invokeArguments = array;
		return true;
	}

	private static bool TryCoerceInvokeArgument(Type parameterType, object value, out object coercedValue)
	{
		coercedValue = null;
		if (parameterType == null)
		{
			return false;
		}
		if (parameterType.IsByRef)
		{
			parameterType = parameterType.GetElementType();
			if (parameterType == null)
			{
				return false;
			}
		}
		Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
		if (value == null)
		{
			if (Nullable.GetUnderlyingType(parameterType) != null || !type.IsValueType)
			{
				coercedValue = null;
				return true;
			}
			return false;
		}
		if (type.IsInstanceOfType(value))
		{
			coercedValue = value;
			return true;
		}
		try
		{
			if (type.IsEnum)
			{
				if (value is string value2)
				{
					coercedValue = Enum.Parse(type, value2, ignoreCase: true);
					return true;
				}
				coercedValue = Enum.ToObject(type, value);
				return true;
			}
			if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(type))
			{
				coercedValue = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private void RefreshUi()
	{
		RebuildPluginTabs();
		RebuildSectionButtons();
		RebuildEntryContent();
		UpdatePanelHeaders();
	}

	private void UpdatePanelHeaders()
	{
		ConfigSource selectedSource = GetSelectedSource();
		ConfigSection selectedSection = GetSelectedSection();
		if (_panelTitleText != null)
		{
			_panelTitleText.text = selectedSource?.DisplayName ?? "Config";
		}
		if (_contentTitleText != null)
		{
			string text = selectedSection?.DisplayName ?? string.Empty;
			_contentTitleText.text = string.IsNullOrWhiteSpace(text) ? (selectedSource?.DisplayName ?? "Config") : ((selectedSource?.DisplayName ?? "Config") + " / " + text);
		}
	}

	private void RebuildPluginTabs()
	{
		DestroyChildren(_pluginTabsRect);
		if (_pluginTabsRect == null)
		{
			return;
		}
		HorizontalLayoutGroup orAddComponent = GetOrAddComponent<HorizontalLayoutGroup>(_pluginTabsRect.gameObject);
		orAddComponent.spacing = 10f;
		orAddComponent.childForceExpandHeight = false;
		orAddComponent.childForceExpandWidth = false;
		orAddComponent.childAlignment = TextAnchor.MiddleLeft;
		ConfigSource[] array = _sources.ToArray();
		foreach (ConfigSource source in array)
		{
			bool isActive = string.Equals(source.Id, _selectedSourceId, StringComparison.Ordinal);
			Button button = CreateButton(_pluginTabsRect, source.DisplayName, isActive ? ButtonSecondaryActiveColor : ButtonColor, 18f, 150f, 36f);
			button.onClick.AddListener(delegate
			{
				_selectedSourceId = source.Id;
				_selectedSectionKey = string.Empty;
				_isDirty = true;
				RefreshDataModel();
				RefreshUi();
			});
		}
	}

	private void RebuildSectionButtons()
	{
		DestroyChildren(_sectionListRect);
		if (_sectionListRect == null)
		{
			return;
		}
		VerticalLayoutGroup orAddComponent = GetOrAddComponent<VerticalLayoutGroup>(_sectionListRect.gameObject);
		orAddComponent.spacing = 8f;
		orAddComponent.childForceExpandHeight = false;
		orAddComponent.childForceExpandWidth = true;
		orAddComponent.padding = new RectOffset(0, 0, 0, 0);
		ConfigSource selectedSource = GetSelectedSource();
		if (selectedSource == null)
		{
			return;
		}
		foreach (ConfigSection section in selectedSource.Sections)
		{
			bool isActive = string.Equals(section.Key, _selectedSectionKey, StringComparison.Ordinal);
			Button button = CreateButton(_sectionListRect, section.DisplayName, isActive ? ButtonActiveColor : ButtonColor, 18f, -1f, 42f);
			button.onClick.AddListener(delegate
			{
				_selectedSectionKey = section.Key;
				RefreshUi();
			});
		}
	}

	private void RebuildEntryContent()
	{
		if (RebuildEntryContentManual())
		{
			return;
		}
		DestroyChildren(_entryListRect);
		_visibleBindings.Clear();
		if (_entryListRect == null)
		{
			return;
		}
		VerticalLayoutGroup orAddComponent = GetOrAddComponent<VerticalLayoutGroup>(_entryListRect.gameObject);
		orAddComponent.spacing = 12f;
		orAddComponent.childForceExpandHeight = false;
		orAddComponent.childForceExpandWidth = true;
		orAddComponent.padding = new RectOffset(4, 12, 4, 18);
		ContentSizeFitter orAddComponent2 = GetOrAddComponent<ContentSizeFitter>(_entryListRect.gameObject);
		orAddComponent2.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		ConfigSection selectedSection = GetSelectedSection();
		if (selectedSection == null || selectedSection.Entries.Count == 0)
		{
			CreateEmptyState(_entryListRect, GetNoVisibleEntriesLabel());
			RefreshEntryLayout();
			return;
		}
		foreach (ConfigEntryBase entry in selectedSection.Entries)
		{
			CreateEntryCard(_entryListRect, GetSelectedSource(), entry);
		}
		RefreshEntryLayout();
	}

	private void CreateEntryCard(Transform parent, ConfigSource source, ConfigEntryBase entry)
	{
		GameObject gameObject = CreateUiObject("EntryCard", parent);
		Image image = gameObject.AddComponent<Image>();
		image.color = CardColor;
		AddOutline(gameObject, BorderColor);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.minHeight = 88f;
		VerticalLayoutGroup verticalLayoutGroup = gameObject.AddComponent<VerticalLayoutGroup>();
		verticalLayoutGroup.padding = new RectOffset(18, 18, 14, 14);
		verticalLayoutGroup.spacing = 8f;
		verticalLayoutGroup.childControlHeight = true;
		verticalLayoutGroup.childControlWidth = true;
		verticalLayoutGroup.childForceExpandHeight = false;
		verticalLayoutGroup.childForceExpandWidth = true;
		GameObject gameObject2 = CreateUiObject("HeaderRow", gameObject.transform);
		HorizontalLayoutGroup horizontalLayoutGroup = gameObject2.AddComponent<HorizontalLayoutGroup>();
		horizontalLayoutGroup.spacing = 16f;
		horizontalLayoutGroup.childControlWidth = true;
		horizontalLayoutGroup.childControlHeight = true;
			horizontalLayoutGroup.childForceExpandWidth = true;
			horizontalLayoutGroup.childForceExpandHeight = false;
			TextMeshProUGUI textMeshProUGUI = CreateText(gameObject2.transform, GetEntryDisplayName(source, entry), 21f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
			LayoutElement layoutElement2 = textMeshProUGUI.gameObject.AddComponent<LayoutElement>();
			layoutElement2.flexibleWidth = 1f;
			EntryBinding entryBinding = new EntryBinding
			{
				Source = source,
				Entry = entry
			};
		Type entryValueType = GetEntryValueType(entry);
		if (entryValueType == typeof(bool))
		{
			CreateBoolControl(gameObject2.transform, entryBinding);
		}
		else if (entryValueType == typeof(KeyCode))
		{
			CreateKeyControl(gameObject2.transform, entryBinding);
		}
		else if (TryGetSelectableOptions(entry, entryValueType, out List<object> options))
		{
			CreateDropdownControl(gameObject2.transform, entryBinding, options);
		}
		else
		{
			CreateInputControl(gameObject2.transform, entryBinding, entryValueType);
		}
		string text = GetEntryDescriptionText(source, entry);
		if (!string.IsNullOrWhiteSpace(text))
		{
			CreateText(gameObject.transform, text, 15f, TextMutedColor, TextAlignmentOptions.TopLeft);
		}
		string entrySupplementalText = GetEntrySupplementalText(source, entry);
		if (!string.IsNullOrWhiteSpace(entrySupplementalText))
		{
			TextMeshProUGUI textMeshProUGUI2 = CreateText(gameObject.transform, entrySupplementalText, 14f, TextMutedColor, TextAlignmentOptions.TopLeft);
			textMeshProUGUI2.textWrappingMode = TextWrappingModes.Normal;
			textMeshProUGUI2.overflowMode = TextOverflowModes.Overflow;
		}
		entryBinding.Refresh?.Invoke();
		_visibleBindings.Add(entryBinding);
	}

	private bool RebuildEntryContentManual()
	{
		DestroyChildren(_entryListRect);
		_visibleBindings.Clear();
		if (_entryListRect == null)
		{
			return true;
		}
		VerticalLayoutGroup component = _entryListRect.GetComponent<VerticalLayoutGroup>();
		if ((UnityEngine.Object)component != (UnityEngine.Object)null)
		{
			component.enabled = false;
		}
		ContentSizeFitter component2 = _entryListRect.GetComponent<ContentSizeFitter>();
		if ((UnityEngine.Object)component2 != (UnityEngine.Object)null)
		{
			component2.enabled = false;
		}
		ConfigSource selectedSource = GetSelectedSource();
		ConfigSection selectedSection = GetSelectedSection();
		if (selectedSection == null || selectedSection.Entries.Count == 0)
		{
			CreateEmptyState(_entryListRect, GetNoVisibleEntriesLabel());
			_entryListRect.sizeDelta = new Vector2(0f, 220f);
			RefreshEntryLayout();
			return true;
		}
		float num = 8f;
		foreach (ConfigEntryBase entry in selectedSection.Entries)
		{
			num += CreateEntryCardManual(_entryListRect, selectedSource, entry, num) + 10f;
		}
		_entryListRect.sizeDelta = new Vector2(0f, Mathf.Max(num + 8f, 120f));
		RefreshEntryLayout();
		return true;
	}

	private float CreateEntryCardManual(Transform parent, ConfigSource source, ConfigEntryBase entry, float topOffset)
	{
		string text = GetEntryDescriptionText(source, entry);
		bool flag = !string.IsNullOrWhiteSpace(text);
		string entrySupplementalText = GetEntrySupplementalText(source, entry);
		bool flag2 = !string.IsNullOrWhiteSpace(entrySupplementalText);
		float num = flag ? 108f : 74f;
		if (flag2)
		{
			num += 128f;
		}
		GameObject gameObject = CreateUiObject("EntryCardManual", parent);
		RectTransform rectTransform = GetRectTransform(gameObject);
		rectTransform.anchorMin = new Vector2(0f, 1f);
		rectTransform.anchorMax = new Vector2(1f, 1f);
		rectTransform.pivot = new Vector2(0.5f, 1f);
		rectTransform.offsetMin = new Vector2(4f, 0f - topOffset - num);
		rectTransform.offsetMax = new Vector2(-12f, 0f - topOffset);
		Image image = gameObject.AddComponent<Image>();
		image.color = CardColor;
		AddOutline(gameObject, BorderColor);
			TextMeshProUGUI textMeshProUGUI = CreateText(gameObject.transform, GetEntryDisplayName(source, entry), 20f, TextPrimaryColor, TextAlignmentOptions.TopLeft);
		RectTransform rectTransform2 = textMeshProUGUI.rectTransform;
		rectTransform2.anchorMin = new Vector2(0f, 1f);
		rectTransform2.anchorMax = new Vector2(1f, 1f);
		rectTransform2.pivot = new Vector2(0f, 1f);
		rectTransform2.sizeDelta = new Vector2(-332f, 28f);
		rectTransform2.anchoredPosition = new Vector2(18f, -14f);
		GameObject gameObject2 = CreateUiObject("ControlHost", gameObject.transform);
		RectTransform rectTransform3 = GetRectTransform(gameObject2);
		rectTransform3.anchorMin = new Vector2(1f, 1f);
		rectTransform3.anchorMax = new Vector2(1f, 1f);
			rectTransform3.pivot = new Vector2(1f, 1f);
			rectTransform3.sizeDelta = new Vector2(286f, 38f);
			rectTransform3.anchoredPosition = new Vector2(-18f, -12f);
			EntryBinding entryBinding = new EntryBinding
			{
				Source = source,
				Entry = entry
			};
		Type entryValueType = GetEntryValueType(entry);
		if (entryValueType == typeof(bool))
		{
			CreateBoolControl(gameObject2.transform, entryBinding);
		}
		else if (entryValueType == typeof(KeyCode))
		{
			CreateKeyControl(gameObject2.transform, entryBinding);
		}
		else if (TryGetSelectableOptions(entry, entryValueType, out List<object> options))
		{
			CreateDropdownControl(gameObject2.transform, entryBinding, options);
		}
		else
		{
			CreateInputControl(gameObject2.transform, entryBinding, entryValueType);
		}
		RectTransform rectTransform4 = ((gameObject2.transform.childCount > 0) ? (gameObject2.transform.GetChild(0) as RectTransform) : null);
		if ((UnityEngine.Object)rectTransform4 != (UnityEngine.Object)null)
		{
			rectTransform4.anchorMin = new Vector2(0.5f, 0.5f);
			rectTransform4.anchorMax = new Vector2(0.5f, 0.5f);
			rectTransform4.pivot = new Vector2(0.5f, 0.5f);
			rectTransform4.anchoredPosition = Vector2.zero;
		}
		float num2 = -48f;
		if (flag)
		{
			TextMeshProUGUI textMeshProUGUI2 = CreateText(gameObject.transform, text, 14f, TextMutedColor, TextAlignmentOptions.TopLeft);
			RectTransform rectTransform5 = textMeshProUGUI2.rectTransform;
			rectTransform5.anchorMin = new Vector2(0f, 1f);
			rectTransform5.anchorMax = new Vector2(1f, 1f);
			rectTransform5.pivot = new Vector2(0f, 1f);
			rectTransform5.sizeDelta = new Vector2(-36f, 40f);
			rectTransform5.anchoredPosition = new Vector2(18f, num2);
			textMeshProUGUI2.textWrappingMode = TextWrappingModes.Normal;
			textMeshProUGUI2.overflowMode = TextOverflowModes.Ellipsis;
			num2 -= 46f;
		}
		if (flag2)
		{
			TextMeshProUGUI textMeshProUGUI3 = CreateText(gameObject.transform, entrySupplementalText, 13.5f, TextMutedColor, TextAlignmentOptions.TopLeft);
			RectTransform rectTransform6 = textMeshProUGUI3.rectTransform;
			rectTransform6.anchorMin = new Vector2(0f, 1f);
			rectTransform6.anchorMax = new Vector2(1f, 1f);
			rectTransform6.pivot = new Vector2(0f, 1f);
			rectTransform6.sizeDelta = new Vector2(-36f, 116f);
			rectTransform6.anchoredPosition = new Vector2(18f, num2);
			textMeshProUGUI3.textWrappingMode = TextWrappingModes.Normal;
			textMeshProUGUI3.overflowMode = TextOverflowModes.Overflow;
		}
		entryBinding.Refresh?.Invoke();
		_visibleBindings.Add(entryBinding);
		return num;
	}

	private void CreateBoolControl(Transform parent, EntryBinding binding)
	{
		GameObject gameObject = CreateUiObject("ToggleRoot", parent);
		GetRectTransform(gameObject).sizeDelta = new Vector2(78f, 30f);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = 78f;
		layoutElement.preferredHeight = 30f;
		Image image = gameObject.AddComponent<Image>();
		image.color = FieldColor;
		AddOutline(gameObject, BorderColor);
		Toggle toggle = gameObject.AddComponent<Toggle>();
		toggle.targetGraphic = image;
		GameObject gameObject2 = CreateUiObject("Checkmark", gameObject.transform);
		RectTransform rectTransform = GetRectTransform(gameObject2);
		rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		rectTransform.pivot = new Vector2(0.5f, 0.5f);
		rectTransform.sizeDelta = new Vector2(50f, 22f);
		Image image2 = gameObject2.AddComponent<Image>();
		image2.color = ButtonActiveColor;
		toggle.graphic = image2;
		toggle.onValueChanged.AddListener(delegate(bool value)
		{
			if (!binding.SuppressCallbacks)
			{
				TryAssignBoxedValue(binding.Entry, value);
				SaveEntryOwner(binding.Entry);
				binding.Refresh?.Invoke();
			}
		});
		binding.Refresh = delegate
		{
			binding.SuppressCallbacks = true;
			bool flag = GetBoxedValue(binding.Entry) is bool boolValue && boolValue;
			toggle.isOn = flag;
			image2.color = (flag ? ButtonActiveColor : ButtonColor);
			binding.SuppressCallbacks = false;
		};
	}

	private void CreateKeyControl(Transform parent, EntryBinding binding)
	{
		Button button = CreateButton(parent, string.Empty, FieldColor, 17f, 210f, 34f);
		TextMeshProUGUI componentInChildren = button.GetComponentInChildren<TextMeshProUGUI>();
		button.onClick.AddListener(delegate
		{
			_pendingKeyCaptureEntry = binding.Entry;
			_pendingKeyCaptureText = componentInChildren;
			RefreshVisibleBindings();
		});
		binding.Refresh = delegate
		{
			binding.SuppressCallbacks = true;
			if (_pendingKeyCaptureEntry == binding.Entry && _pendingKeyCaptureText == componentInChildren)
			{
				componentInChildren.text = "鎸変换鎰忛敭...";
			}
			else
			{
				componentInChildren.text = GetDisplayValueText(binding.Entry);
			}
			binding.SuppressCallbacks = false;
		};
	}

	private void CreateDropdownControl(Transform parent, EntryBinding binding, List<object> options)
	{
		TMP_Dropdown tMP_Dropdown = CreateDropdown(parent, 270f, 34f);
		List<string> list = options.Select((object option) => GetOptionDisplayText(binding.Source, binding.Entry, option)).ToList();
		tMP_Dropdown.ClearOptions();
		tMP_Dropdown.AddOptions(list);
		tMP_Dropdown.onValueChanged.AddListener(delegate(int index)
		{
			if (!binding.SuppressCallbacks && index >= 0 && index < options.Count)
			{
				TryAssignBoxedValue(binding.Entry, options[index]);
				SaveEntryOwner(binding.Entry);
				binding.Refresh?.Invoke();
			}
		});
		binding.Refresh = delegate
		{
			binding.SuppressCallbacks = true;
			object boxedValue = GetBoxedValue(binding.Entry);
			int value = 0;
			for (int i = 0; i < options.Count; i++)
			{
				if (ValuesEqual(options[i], boxedValue))
				{
					value = i;
					break;
				}
			}
			tMP_Dropdown.SetValueWithoutNotify(value);
			tMP_Dropdown.RefreshShownValue();
			binding.SuppressCallbacks = false;
		};
	}

	private void CreateInputControl(Transform parent, EntryBinding binding, Type valueType)
	{
		TMP_InputField tMP_InputField = CreateInputField(parent, 270f, 34f);
		tMP_InputField.onEndEdit.AddListener(delegate(string value)
		{
			if (!binding.SuppressCallbacks)
			{
				if (!TryAssignTextValue(binding.Entry, valueType, value))
				{
					binding.Refresh?.Invoke();
					return;
				}
				SaveEntryOwner(binding.Entry);
				binding.Refresh?.Invoke();
			}
		});
		binding.Refresh = delegate
		{
			binding.SuppressCallbacks = true;
			tMP_InputField.SetTextWithoutNotify(GetDisplayValueText(binding.Entry));
			binding.SuppressCallbacks = false;
		};
	}

	private void RefreshVisibleBindings()
	{
		foreach (EntryBinding visibleBinding in _visibleBindings)
		{
			visibleBinding.Refresh?.Invoke();
		}
	}

	private void RefreshEntryLayout()
	{
		if ((UnityEngine.Object)_entryListRect == (UnityEngine.Object)null)
		{
			return;
		}
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_entryListRect);
		ScrollRect componentInParent = _entryListRect.GetComponentInParent<ScrollRect>();
		if ((UnityEngine.Object)componentInParent != (UnityEngine.Object)null)
		{
			RectTransform component = componentInParent.GetComponent<RectTransform>();
			if ((UnityEngine.Object)component != (UnityEngine.Object)null && component != _entryListRect)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(component);
			}
		}
		Canvas.ForceUpdateCanvases();
	}

	private ConfigSource GetSelectedSource()
	{
		return _sources.FirstOrDefault((ConfigSource source) => string.Equals(source.Id, _selectedSourceId, StringComparison.Ordinal));
	}

	private ConfigSection GetSelectedSection()
	{
		return GetSelectedSource()?.Sections.FirstOrDefault((ConfigSection section) => string.Equals(section.Key, _selectedSectionKey, StringComparison.Ordinal));
	}

	private ConfigSource GetSourceForEntry(ConfigEntryBase entry)
	{
		if (entry == null)
		{
			return null;
		}
		return _sources.FirstOrDefault((ConfigSource source) => source.Sections.Any((ConfigSection section) => section.Entries.Contains(entry)));
	}

	private string GetOptionDisplayText(ConfigSource source, ConfigEntryBase entry, object option)
	{
		string text = option?.ToString() ?? string.Empty;
		string key = entry?.Definition?.Key ?? string.Empty;
		string text2 = source?.Localizer?.LocalizeOptionDisplayText?.Invoke(key, option);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return GetOptionDisplayText(entry, option);
	}

	private static string GetEntryDisplayName(ConfigSource source, ConfigEntryBase entry)
	{
		if (entry == null || entry.Definition == null)
		{
			return string.Empty;
		}
		string key = entry.Definition.Key;
		string text = source?.Localizer?.LocalizeKeyName?.Invoke(key);
		return string.IsNullOrWhiteSpace(text) ? key : text;
	}

	private static string GetSectionDisplayName(ConfigSource source, string section)
	{
		if (string.IsNullOrWhiteSpace(section))
		{
			return "General";
		}
		string text = source?.Localizer?.LocalizeSectionName?.Invoke(section);
		return string.IsNullOrWhiteSpace(text) ? section : text;
	}

	private static int GetEntrySortIndex(ConfigSource source, ConfigEntryBase entry)
	{
		return (source != null && source.IsShootZombies) ? Plugin.GetOwnedConfigEntrySortIndexRuntime(entry) : int.MaxValue;
	}

	private static int GetSectionSortIndex(ConfigSource source, string section)
	{
		return (source != null && source.IsShootZombies) ? Plugin.GetOwnedConfigSectionSortIndexRuntime(section) : int.MaxValue;
	}

	private string GetDisplayValueText(ConfigEntryBase entry)
	{
		object boxedValue = GetBoxedValue(entry);
		if (boxedValue == null)
		{
			return string.Empty;
		}
		if (boxedValue is float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}
		if (boxedValue is double value2)
		{
			return value2.ToString("0.###", CultureInfo.InvariantCulture);
		}
		return boxedValue.ToString();
	}

	private static string GetOptionDisplayText(ConfigEntryBase entry, object option)
	{
		string text = option?.ToString() ?? string.Empty;
		if ((object)entry == Plugin.ConfigPanelTheme)
		{
			return GetPanelThemeOptionDisplayText(text);
		}
		if ((object)entry == Plugin.AkSoundSelection)
		{
			return GetAkSoundOptionDisplayText(text);
		}
		if ((object)entry == Plugin.WeaponSelection)
		{
			return text switch
			{
				"AK47" => "AK47", 
				"HK416" => "HK416", 
				"MPX" => "MPX", 
				_ => text, 
			};
		}
		if ((object)entry == Plugin.ZombieBehaviorDifficulty)
		{
			return Plugin.GetZombieBehaviorDifficultyDisplayNameRuntime(text);
		}
		return text;
	}

	private static string GetEntryDisplayName(bool isShootZombies, ConfigEntryBase entry)
	{
		if (entry == null || entry.Definition == null)
		{
			return string.Empty;
		}
		return isShootZombies ? Plugin.GetLocalizedConfigKeyDisplayRuntime(entry.Definition.Key) : entry.Definition.Key;
	}

	private static string GetEntryDescriptionText(ConfigSource source, ConfigEntryBase entry)
	{
		if (source != null && entry?.Definition != null)
		{
			string text = source.Localizer?.LocalizeDescription?.Invoke(entry.Definition.Key);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return entry?.Description?.Description ?? string.Empty;
	}

	private static string GetEntrySupplementalText(ConfigSource source, ConfigEntryBase entry)
	{
		if (source != null && source.IsShootZombies && entry?.Definition != null && string.Equals(entry.Definition.Key, "Behavior Difficulty", StringComparison.Ordinal))
		{
			return Plugin.GetZombieBehaviorDifficultyDetailsRuntime();
		}
		return string.Empty;
	}

	private static string GetSectionDisplayName(bool isShootZombies, string section)
	{
		if (string.IsNullOrWhiteSpace(section))
		{
			return "General";
		}
		return isShootZombies ? Plugin.GetLocalizedConfigSectionDisplayRuntime(section) : section;
	}

	private static int GetEntrySortIndex(bool isShootZombies, ConfigEntryBase entry)
	{
		return isShootZombies ? Plugin.GetOwnedConfigEntrySortIndexRuntime(entry) : int.MaxValue;
	}

	private static int GetSectionSortIndex(bool isShootZombies, string section)
	{
		return isShootZombies ? Plugin.GetOwnedConfigSectionSortIndexRuntime(section) : int.MaxValue;
	}

	private static object GetBoxedValue(ConfigEntryBase entry)
	{
		return entry?.GetType().GetProperty("BoxedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(entry);
	}

	private static bool TryAssignBoxedValue(ConfigEntryBase entry, object value)
	{
		if (entry == null)
		{
			return false;
		}
		try
		{
			entry.GetType().GetProperty("BoxedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(entry, value);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private bool TryAssignTextValue(ConfigEntryBase entry, Type valueType, string text)
	{
		if (entry == null)
		{
			return false;
		}
		try
		{
			if (valueType == typeof(string))
			{
				TryAssignBoxedValue(entry, text ?? string.Empty);
				return true;
			}
			if (valueType == typeof(int))
			{
				if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out result))
				{
					return false;
				}
				if (TryGetNumericRange(entry, out var min, out var max))
				{
					result = Mathf.Clamp(result, Mathf.RoundToInt((float)min), Mathf.RoundToInt((float)max));
				}
				return TryAssignBoxedValue(entry, result);
			}
			if (valueType == typeof(float))
			{
				if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) && !float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result2))
				{
					return false;
				}
				if (TryGetNumericRange(entry, out var min2, out var max2))
				{
					result2 = Mathf.Clamp(result2, (float)min2, (float)max2);
				}
				return TryAssignBoxedValue(entry, result2);
			}
			if (valueType == typeof(double))
			{
				if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result3) && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result3))
				{
					return false;
				}
				if (TryGetNumericRange(entry, out var min3, out var max3))
				{
					result3 = Math.Min(Math.Max(result3, min3), max3);
				}
				return TryAssignBoxedValue(entry, result3);
			}
			if (valueType.IsEnum)
			{
				object value = Enum.Parse(valueType, text, ignoreCase: true);
				return TryAssignBoxedValue(entry, value);
			}
			entry.SetSerializedValue(text ?? string.Empty);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void SaveEntryOwner(ConfigEntryBase entry)
	{
		ConfigSource[] array = _sources.ToArray();
		ConfigSource configSource = array.FirstOrDefault((ConfigSource source) => source.Sections.Any((ConfigSection section) => section.Entries.Contains(entry)));
		if (configSource?.IsShootZombies ?? false)
		{
			_owner?.SaveOwnedConfigRuntime();
		}
		else
		{
			configSource?.Config?.Save();
		}
		if ((object)entry == Plugin.ConfigPanelTheme)
		{
			SyncThemeFromConfig();
			_isDirty = true;
		}
	}

	private static Type GetEntryValueType(ConfigEntryBase entry)
	{
		Type type = entry?.GetType();
		while (type != null)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ConfigEntry<>))
			{
				return type.GetGenericArguments()[0];
			}
			type = type.BaseType;
		}
		object boxedValue = GetBoxedValue(entry);
		return boxedValue?.GetType() ?? typeof(string);
	}

	private static bool TryGetSelectableOptions(ConfigEntryBase entry, Type valueType, out List<object> options)
	{
		options = new List<object>();
		if (valueType.IsEnum)
		{
			options.AddRange(Enum.GetValues(valueType).Cast<object>());
			return options.Count > 0;
		}
		if (entry != null)
		{
			string[] ownedSelectableConfigValuesRuntime = Plugin.GetOwnedSelectableConfigValuesRuntime(entry);
			if (ownedSelectableConfigValuesRuntime.Length > 0)
			{
				options.AddRange(ownedSelectableConfigValuesRuntime.Cast<object>());
				return true;
			}
		}
		object acceptableValues = entry?.Description?.AcceptableValues;
		if (acceptableValues == null)
		{
			return false;
		}
		object value = acceptableValues.GetType().GetProperty("AcceptableValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues) ?? acceptableValues.GetType().GetField("AcceptableValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues);
		if (value is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				options.Add(item);
			}
		}
		return options.Count > 0;
	}

	private static bool TryGetNumericRange(ConfigEntryBase entry, out double min, out double max)
	{
		min = 0.0;
		max = 0.0;
		object acceptableValues = entry?.Description?.AcceptableValues;
		if (acceptableValues == null)
		{
			return false;
		}
		object value = acceptableValues.GetType().GetProperty("MinValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues) ?? acceptableValues.GetType().GetField("MinValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues);
		object value2 = acceptableValues.GetType().GetProperty("MaxValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues) ?? acceptableValues.GetType().GetField("MaxValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(acceptableValues);
		if (value == null || value2 == null)
		{
			return false;
		}
		try
		{
			min = Convert.ToDouble(value, CultureInfo.InvariantCulture);
			max = Convert.ToDouble(value2, CultureInfo.InvariantCulture);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ValuesEqual(object left, object right)
	{
		if (left == null && right == null)
		{
			return true;
		}
		if (left == null || right == null)
		{
			return false;
		}
		if (left.Equals(right))
		{
			return true;
		}
		return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	private void EnsureWindowRectInitialized(bool forceReset = false)
	{
		if (!forceReset && _windowRect.width > 100f && _windowRect.height > 100f)
		{
			ClampWindowRectToScreen();
			return;
		}
		float num = Mathf.Clamp((float)Screen.width * 0.64f, 900f, 1360f);
		float num2 = Mathf.Clamp((float)Screen.height * 0.52f, 460f, 760f);
		_windowRect = new Rect(((float)Screen.width - num) * 0.5f, ((float)Screen.height - num2) * 0.5f, num, num2);
		ClampWindowRectToScreen();
	}

	private void ClampWindowRectToScreen()
	{
		if (_windowRect.width <= 0f || _windowRect.height <= 0f)
		{
			return;
		}
		float num = Mathf.Max(0f, (float)Screen.width - _windowRect.width);
		float num2 = Mathf.Max(0f, (float)Screen.height - _windowRect.height);
		_windowRect.x = Mathf.Clamp(_windowRect.x, 0f, num);
		_windowRect.y = Mathf.Clamp(_windowRect.y, 0f, num2);
	}

	private bool HandleOutsideWindowClick()
	{
		Event current = Event.current;
		if (current == null || current.type != EventType.MouseDown || current.button < 0)
		{
			return false;
		}
		if (_windowRect.Contains(current.mousePosition))
		{
			return false;
		}
		Close();
		current.Use();
		return true;
	}

	private void RenderImmediateGui()
	{
		if (!_isOpen)
		{
			return;
		}
		EnsureWindowRectInitialized();
		EnsureImmediateStyles();
		if (_pendingRenderDiagnostic)
		{
			_pendingRenderDiagnostic = false;
			if (Plugin.ShouldEmitVerboseInfoLogsRuntime())
			{
				Plugin.Log?.LogInfo((object)$"[ShootZombies] LobbyConfigPanel OnGUI active. sources={_sources.Count}, selectedSource={_selectedSourceId}, selectedSection={_selectedSectionKey}, rect={_windowRect}.");
			}
		}
		GUI.depth = -1000;
		Color color = GUI.color;
		GUI.color = Color.white;
		GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), GetSolidTexture(GetImmediateBackdropColor()));
		if (HandleOutsideWindowClick())
		{
			GUI.color = color;
			GUIUtility.ExitGUI();
			return;
		}
		_windowRect = GUI.Window(GetHashCode(), _windowRect, DrawImmediateWindow, string.Empty, _immediateWindowStyle);
		GUI.color = color;
		ClampWindowRectToScreen();
	}

	private void MaybeLogDiagnostics()
	{
		if (!_isOpen || Time.unscaledTime < _nextDiagnosticLogTime)
		{
			return;
		}
		ConfigSource selectedSource = GetSelectedSource();
		ConfigSection selectedSection = GetSelectedSection();
		int num = selectedSource?.Sections?.Count ?? 0;
		int num2 = selectedSection?.Entries?.Count ?? 0;
		string text = $"{_sources.Count}|{selectedSource?.DisplayName ?? "<none>"}|{num}|{selectedSection?.DisplayName ?? "<none>"}|{num2}";
		if (!string.Equals(text, _lastDiagnosticSummary, StringComparison.Ordinal))
		{
			_lastDiagnosticSummary = text;
			if (Plugin.ShouldEmitVerboseInfoLogsRuntime())
			{
				Plugin.Log?.LogInfo((object)$"[ShootZombies] LobbyConfigPanel data sources={_sources.Count}, selectedSource={selectedSource?.DisplayName ?? "<none>"}, sections={num}, selectedSection={selectedSection?.DisplayName ?? "<none>"}, entries={num2}.");
			}
		}
		_nextDiagnosticLogTime = Time.unscaledTime + 5f;
	}

	private void DrawImmediateWindow(int windowId)
	{
		GUIStyle label = _immediateLabelStyle;
		GUIStyle title = _immediateTitleStyle;
		GUIStyle description = _immediateDescriptionStyle;
		GUIStyle card = _immediateCardStyle;
		float num = _windowRect.width;
		float num2 = _windowRect.height;
		Color backgroundColor = GUI.backgroundColor;
		Color contentColor = GUI.contentColor;
		GUI.Label(new Rect(14f, 8f, num - 136f, 20f), GetSelectedSource()?.DisplayName ?? "Config", title);
		Rect rect = new Rect(num - 78f, 6f, 32f, 22f);
		GUI.backgroundColor = GetImmediateThemeButtonColor(_panelTheme, active: true);
		GUI.contentColor = GetImmediateThemeButtonTextColor(_panelTheme, active: true);
		if (GUI.Button(rect, GetThemeToggleIcon(), _immediateButtonStyle))
		{
			ToggleTheme();
			GUI.backgroundColor = backgroundColor;
			GUI.contentColor = contentColor;
			GUIUtility.ExitGUI();
			return;
		}
		GUI.backgroundColor = GetImmediateButtonColor(active: true);
		GUI.contentColor = contentColor;
		if (GUI.Button(new Rect(num - 38f, 6f, 26f, 22f), "x", _immediateButtonStyle))
		{
			GUI.backgroundColor = backgroundColor;
			GUI.contentColor = contentColor;
			Close();
			GUIUtility.ExitGUI();
			return;
		}
		GUI.backgroundColor = backgroundColor;
		GUI.contentColor = contentColor;
		float num3 = 34f;
		float num4 = 12f;
		float num5 = num2 - num3 - 14f;
		float num6 = 156f;
		float num7 = num - num6 - 34f;
		GUILayout.BeginArea(new Rect(num4, num3, num - num4 * 2f, 30f));
		GUILayout.BeginHorizontal();
		ConfigSource[] array = _sources.ToArray();
		foreach (ConfigSource source in array)
		{
			Color backgroundColor2 = GUI.backgroundColor;
			GUI.backgroundColor = GetImmediateButtonColor(string.Equals(source.Id, _selectedSourceId, StringComparison.Ordinal));
			if (GUILayout.Button(source.DisplayName, _immediateButtonStyle, GUILayout.Width(124f), GUILayout.Height(24f)))
			{
				_selectedSourceId = source.Id;
				_selectedSectionKey = string.Empty;
				RefreshDataModel();
			}
			GUI.backgroundColor = backgroundColor2;
			GUILayout.Space(6f);
		}
		GUILayout.EndHorizontal();
		GUILayout.EndArea();
		GUI.Box(new Rect(num4, num3 + 32f, num6, num5 - 32f), GUIContent.none, _immediatePaneStyle);
		GUI.Label(new Rect(num4 + 8f, num3 + 40f, num6 - 16f, 22f), GetSectionsLabel(), title);
		ConfigSource selectedSource = GetSelectedSource();
		float num8 = num3 + 70f;
		if (selectedSource != null)
		{
			ConfigSection[] array2 = selectedSource.Sections.ToArray();
			foreach (ConfigSection section in array2)
			{
				Color backgroundColor3 = GUI.backgroundColor;
				GUI.backgroundColor = GetImmediateButtonColor(string.Equals(section.Key, _selectedSectionKey, StringComparison.Ordinal));
				if (GUI.Button(new Rect(num4 + 12f, num8, num6 - 24f, 32f), section.DisplayName, _immediateButtonStyle))
				{
					_selectedSectionKey = section.Key;
				}
				GUI.backgroundColor = backgroundColor3;
				num8 += 38f;
			}
		}
		float num9 = num4 + num6 + 10f;
		GUI.Box(new Rect(num9, num3 + 32f, num7, num5 - 32f), GUIContent.none, _immediatePaneStyle);
		string text = GetSelectedSection()?.DisplayName ?? string.Empty;
		string text2 = string.IsNullOrWhiteSpace(text) ? (selectedSource?.DisplayName ?? "Config") : ((selectedSource?.DisplayName ?? "Config") + " / " + text);
		GUI.Label(new Rect(num9 + 10f, num3 + 40f, num7 - 20f, 24f), text2, title);
		Rect position = new Rect(num9 + 10f, num3 + 72f, num7 - 20f, num5 - 82f);
		GUILayout.BeginArea(position);
		_entryScrollPosition = GUILayout.BeginScrollView(_entryScrollPosition, alwaysShowHorizontal: false, alwaysShowVertical: true, GUIStyle.none, GUI.skin.verticalScrollbar);
		GUILayout.BeginVertical(GUILayout.Width(position.width - 22f));
		ConfigSection selectedSection = GetSelectedSection();
		if (selectedSection == null || selectedSection.Entries.Count == 0)
		{
			GUILayout.Space(8f);
			GUILayout.Label(GetNoVisibleEntriesLabel(), label);
		}
		else
		{
			ConfigEntryBase[] array3 = selectedSection.Entries.ToArray();
			foreach (ConfigEntryBase entry in array3)
			{
				DrawImmediateEntry(selectedSource, entry, label, title, description, card);
				GUILayout.Space(8f);
			}
		}
		GUILayout.EndVertical();
		GUILayout.EndScrollView();
		GUILayout.EndArea();
		GUI.DragWindow(new Rect(0f, 0f, num - 54f, 36f));
	}

	private void DrawImmediateEntry(ConfigSource source, ConfigEntryBase entry, GUIStyle labelStyle, GUIStyle titleStyle, GUIStyle descriptionStyle, GUIStyle cardStyle)
	{
		float num = GetImmediateEntryTitleWidth();
		float immediateDescriptionWidth = GetImmediateDescriptionWidth();
		GUILayout.BeginVertical(cardStyle, GUILayout.ExpandWidth(true));
		GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
			GUILayout.Label(GetEntryDisplayName(source, entry), titleStyle, GUILayout.Width(num));
		GUILayout.Space(12f);
		DrawImmediateEntryControlStableV2(entry);
		GUILayout.EndHorizontal();
		string text = GetEntryDescriptionText(source, entry);
		if (!string.IsNullOrWhiteSpace(text))
		{
			GUILayout.Space(4f);
			GUILayout.Label(text, descriptionStyle, GUILayout.MaxWidth(immediateDescriptionWidth));
		}
		string entrySupplementalText = GetEntrySupplementalText(source, entry);
		if (!string.IsNullOrWhiteSpace(entrySupplementalText))
		{
			GUILayout.Space(6f);
			GUILayout.Label(entrySupplementalText, descriptionStyle, GUILayout.MaxWidth(immediateDescriptionWidth));
		}
		GUILayout.EndVertical();
	}

	private void DrawImmediateEntryControlStable(ConfigEntryBase entry)
	{
		Type entryValueType = GetEntryValueType(entry);
		if (entryValueType == typeof(bool))
		{
			bool flag = GetBoxedValue(entry) is bool boolValue && boolValue;
			bool flag2 = GUILayout.Toggle(flag, GetToggleStateLabel(flag), GUILayout.Width(92f), GUILayout.Height(28f));
			if (flag2 != flag)
			{
				TryAssignBoxedValue(entry, flag2);
				SaveEntryOwner(entry);
			}
			return;
		}
		if (entryValueType == typeof(KeyCode))
		{
			string text = ((_pendingKeyCaptureEntry == entry) ? "鎸変换鎰忛敭..." : GetDisplayValueText(entry));
			if (GUILayout.Button(text, GUILayout.Width(280f), GUILayout.Height(28f)))
			{
				_pendingKeyCaptureEntry = entry;
				_pendingKeyCaptureText = null;
			}
			return;
		}
		if (TryDrawImmediateRangeControlStable(entry, entryValueType))
		{
			return;
		}
		if (TryGetSelectableOptions(entry, entryValueType, out List<object> options) && options.Count > 0)
		{
			if (ShouldRenderOptionSelectionBoxStable(entry))
			{
				DrawImmediateOptionSelectionBoxStable(entry, options);
				return;
			}
			object boxedValue = GetBoxedValue(entry);
			int num = Mathf.Max(0, options.FindIndex((object option) => ValuesEqual(option, boxedValue)));
			GUILayout.BeginHorizontal(GUILayout.Width(280f));
			if (GUILayout.Button("<", GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num - 1 + options.Count) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
				GUILayout.Box(GetOptionDisplayText(GetSourceForEntry(entry), entry, options[num]), GUILayout.Width(220f), GUILayout.Height(28f));
			if (GUILayout.Button(">", GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num + 1) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
			GUILayout.EndHorizontal();
			return;
		}
		string text2 = (!_textBufferByEntry.TryGetValue(entry, out var value) ? GetDisplayValueText(entry) : value);
		string text3 = "cfg_" + (entry.Definition?.Section ?? string.Empty) + "_" + (entry.Definition?.Key ?? string.Empty);
		GUI.SetNextControlName(text3);
		string text4 = GUILayout.TextField(text2 ?? string.Empty, GUILayout.Width(280f), GUILayout.Height(28f));
		if (!string.Equals(text4, text2, StringComparison.Ordinal))
		{
			_textBufferByEntry[entry] = text4;
		}
		bool flag3 = string.Equals(GUI.GetNameOfFocusedControl(), text3, StringComparison.Ordinal);
		if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && flag3)
		{
			CommitBufferedEntryText(entry, entryValueType);
			GUI.FocusControl(null);
			Event.current.Use();
		}
		if (!flag3 && _textBufferByEntry.TryGetValue(entry, out var _) && !string.Equals(_textBufferByEntry[entry], GetDisplayValueText(entry), StringComparison.Ordinal))
		{
			CommitBufferedEntryText(entry, entryValueType);
		}
	}

	private void DrawImmediateEntryControlStableV2(ConfigEntryBase entry)
	{
		Type entryValueType = GetEntryValueType(entry);
		float immediateCompactControlWidth = GetImmediateCompactControlWidth();
		if (entryValueType == typeof(bool))
		{
			bool flag = GetBoxedValue(entry) is bool boolValue && boolValue;
			Color backgroundColor = GUI.backgroundColor;
			GUI.backgroundColor = GetImmediateButtonColor(flag);
			if (GUILayout.Button(GetToggleStateLabel(flag), _immediateButtonStyle, GUILayout.Width(92f), GUILayout.Height(28f)))
			{
				TryAssignBoxedValue(entry, !flag);
				SaveEntryOwner(entry);
			}
			GUI.backgroundColor = backgroundColor;
			return;
		}
		if (entryValueType == typeof(KeyCode))
		{
			string text = ((_pendingKeyCaptureEntry == entry) ? GetPressAnyKeyLabel() : GetDisplayValueText(entry));
			Color backgroundColor2 = GUI.backgroundColor;
			GUI.backgroundColor = GetImmediateButtonColor(active: false);
			if (GUILayout.Button(text, _immediateButtonStyle, GUILayout.Width(immediateCompactControlWidth), GUILayout.Height(28f)))
			{
				_pendingKeyCaptureEntry = entry;
				_pendingKeyCaptureText = null;
			}
			GUI.backgroundColor = backgroundColor2;
			return;
		}
		if (TryDrawImmediateRangeControlStableV2(entry, entryValueType))
		{
			return;
		}
		if (TryGetSelectableOptions(entry, entryValueType, out List<object> options) && options.Count > 0)
		{
			if (ShouldRenderOptionSelectionBoxStable(entry))
			{
				DrawImmediateOptionSelectionBoxStableV2(entry, options);
				return;
			}
			object boxedValue = GetBoxedValue(entry);
			int num = Mathf.Max(0, options.FindIndex((object option) => ValuesEqual(option, boxedValue)));
			float num2 = Mathf.Clamp(immediateCompactControlWidth - 60f, 180f, 360f);
			GUILayout.BeginHorizontal(GUILayout.Width(immediateCompactControlWidth));
			Color backgroundColor3 = GUI.backgroundColor;
			GUI.backgroundColor = GetImmediateButtonColor(active: false);
			if (GUILayout.Button("<", _immediateButtonStyle, GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num - 1 + options.Count) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
				GUILayout.Box(GetOptionDisplayText(GetSourceForEntry(entry), entry, options[num]), _immediateFieldStyle, GUILayout.Width(num2), GUILayout.Height(28f));
			if (GUILayout.Button(">", _immediateButtonStyle, GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num + 1) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
			GUI.backgroundColor = backgroundColor3;
			GUILayout.EndHorizontal();
			return;
		}
		string text2 = (!_textBufferByEntry.TryGetValue(entry, out var value) ? GetDisplayValueText(entry) : value);
		string text3 = "cfg_" + (entry.Definition?.Section ?? string.Empty) + "_" + (entry.Definition?.Key ?? string.Empty);
		GUI.SetNextControlName(text3);
		string text4 = GUILayout.TextField(text2 ?? string.Empty, _immediateTextFieldStyle, GUILayout.Width(immediateCompactControlWidth), GUILayout.Height(28f));
		if (!string.Equals(text4, text2, StringComparison.Ordinal))
		{
			_textBufferByEntry[entry] = text4;
		}
		bool flag2 = string.Equals(GUI.GetNameOfFocusedControl(), text3, StringComparison.Ordinal);
		if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && flag2)
		{
			CommitBufferedEntryText(entry, entryValueType);
			GUI.FocusControl(null);
			Event.current.Use();
		}
		if (!flag2 && _textBufferByEntry.TryGetValue(entry, out var _) && !string.Equals(_textBufferByEntry[entry], GetDisplayValueText(entry), StringComparison.Ordinal))
		{
			CommitBufferedEntryText(entry, entryValueType);
		}
	}

	private bool TryDrawImmediateRangeControlStableV2(ConfigEntryBase entry, Type entryValueType)
	{
		if ((entryValueType != typeof(int) && entryValueType != typeof(float) && entryValueType != typeof(double)) || !TryGetNumericRange(entry, out var min, out var max))
		{
			return false;
		}
		float num = (float)min;
		float num2 = (float)max;
		float immediateRangeControlWidth = GetImmediateRangeControlWidth();
		float immediateRangeSliderWidth = GetImmediateRangeSliderWidth();
		GUILayout.BeginHorizontal(GUILayout.Width(immediateRangeControlWidth));
		if (entryValueType == typeof(int))
		{
			int num3 = Convert.ToInt32(GetBoxedValue(entry) ?? 0, CultureInfo.InvariantCulture);
			int num4 = Mathf.RoundToInt(GUILayout.HorizontalSlider(num3, num, num2, GUILayout.Width(immediateRangeSliderWidth)));
			num4 = Mathf.Clamp(num4, Mathf.RoundToInt(num), Mathf.RoundToInt(num2));
			if (num4 != num3)
			{
				TryAssignBoxedValue(entry, num4);
				SaveEntryOwner(entry);
			}
		}
		else if (entryValueType == typeof(double))
		{
			double num5 = Convert.ToDouble(GetBoxedValue(entry) ?? 0.0, CultureInfo.InvariantCulture);
			double num6 = GUILayout.HorizontalSlider((float)num5, num, num2, GUILayout.Width(immediateRangeSliderWidth));
			if (Math.Abs(num6 - num5) > 0.0001)
			{
				TryAssignBoxedValue(entry, num6);
				SaveEntryOwner(entry);
			}
		}
		else
		{
			float num7 = Convert.ToSingle(GetBoxedValue(entry) ?? 0f, CultureInfo.InvariantCulture);
			float num8 = GUILayout.HorizontalSlider(num7, num, num2, GUILayout.Width(immediateRangeSliderWidth));
			if (Mathf.Abs(num8 - num7) > 0.0001f)
			{
				TryAssignBoxedValue(entry, num8);
				SaveEntryOwner(entry);
			}
		}
		GUILayout.Space(10f);
		GUILayout.Label(GetDisplayValueText(entry), _immediateLabelStyle, GUILayout.Width(70f));
		GUILayout.EndHorizontal();
		return true;
	}

	private void DrawImmediateOptionSelectionBoxStableV2(ConfigEntryBase entry, List<object> options)
	{
		object boxedValue = GetBoxedValue(entry);
		int num = options.Count;
		float num2 = 86f;
		GUILayout.BeginVertical(GUILayout.Width(num2 * num + 6f * (num - 1)));
		for (int i = 0; i < options.Count; i += num)
		{
			GUILayout.BeginHorizontal();
			for (int j = i; j < Mathf.Min(i + num, options.Count); j++)
			{
				object obj = options[j];
				bool flag = ValuesEqual(obj, boxedValue);
				Color backgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = GetImmediateButtonColor(flag);
					if (GUILayout.Button(GetOptionDisplayText(GetSourceForEntry(entry), entry, obj), _immediateButtonStyle, GUILayout.Width(num2), GUILayout.Height(28f)) && !flag)
				{
					TryAssignBoxedValue(entry, obj);
					SaveEntryOwner(entry);
					boxedValue = obj;
				}
				GUI.backgroundColor = backgroundColor;
				if (j < Mathf.Min(i + num, options.Count) - 1)
				{
					GUILayout.Space(6f);
				}
			}
			GUILayout.EndHorizontal();
			if (i + num < options.Count)
			{
				GUILayout.Space(4f);
			}
		}
		GUILayout.EndVertical();
	}

	private bool TryDrawImmediateRangeControlStable(ConfigEntryBase entry, Type entryValueType)
	{
		if ((entryValueType != typeof(int) && entryValueType != typeof(float) && entryValueType != typeof(double)) || !TryGetNumericRange(entry, out var min, out var max))
		{
			return false;
		}
		float num = (float)min;
		float num2 = (float)max;
		GUILayout.BeginHorizontal(GUILayout.Width(380f));
		if (entryValueType == typeof(int))
		{
			int num3 = Convert.ToInt32(GetBoxedValue(entry) ?? 0, CultureInfo.InvariantCulture);
			int num4 = Mathf.RoundToInt(GUILayout.HorizontalSlider(num3, num, num2, GUILayout.Width(300f)));
			num4 = Mathf.Clamp(num4, Mathf.RoundToInt(num), Mathf.RoundToInt(num2));
			if (num4 != num3)
			{
				TryAssignBoxedValue(entry, num4);
				SaveEntryOwner(entry);
			}
		}
		else if (entryValueType == typeof(double))
		{
			double num5 = Convert.ToDouble(GetBoxedValue(entry) ?? 0.0, CultureInfo.InvariantCulture);
			double num6 = GUILayout.HorizontalSlider((float)num5, num, num2, GUILayout.Width(300f));
			if (Math.Abs(num6 - num5) > 0.0001)
			{
				TryAssignBoxedValue(entry, num6);
				SaveEntryOwner(entry);
			}
		}
		else
		{
			float num7 = Convert.ToSingle(GetBoxedValue(entry) ?? 0f, CultureInfo.InvariantCulture);
			float num8 = GUILayout.HorizontalSlider(num7, num, num2, GUILayout.Width(300f));
			if (Mathf.Abs(num8 - num7) > 0.0001f)
			{
				TryAssignBoxedValue(entry, num8);
				SaveEntryOwner(entry);
			}
		}
		GUILayout.Space(10f);
		GUILayout.Label(GetDisplayValueText(entry), GUILayout.Width(70f));
		GUILayout.EndHorizontal();
		return true;
	}

	private static bool ShouldRenderOptionSelectionBoxStable(ConfigEntryBase entry)
	{
		return (object)entry == Plugin.WeaponSelection || (object)entry == Plugin.AkSoundSelection || (object)entry == Plugin.ConfigPanelTheme;
	}

	private void DrawImmediateOptionSelectionBoxStable(ConfigEntryBase entry, List<object> options)
	{
		object boxedValue = GetBoxedValue(entry);
		int num = ((object)entry == Plugin.AkSoundSelection) ? options.Count : options.Count;
		float num2 = ((object)entry == Plugin.AkSoundSelection) ? 86f : 86f;
		GUILayout.BeginVertical(GUILayout.Width(num2 * num + 6f * (num - 1)));
		for (int i = 0; i < options.Count; i += num)
		{
			GUILayout.BeginHorizontal();
			for (int j = i; j < Mathf.Min(i + num, options.Count); j++)
			{
				object obj = options[j];
				bool flag = ValuesEqual(obj, boxedValue);
				Color backgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = (flag ? new Color(0.27f, 0.66f, 0.58f, 1f) : new Color(0.34f, 0.37f, 0.45f, 1f));
					if (GUILayout.Button(GetOptionDisplayText(GetSourceForEntry(entry), entry, obj), GUILayout.Width(num2), GUILayout.Height(28f)) && !flag)
				{
					TryAssignBoxedValue(entry, obj);
					SaveEntryOwner(entry);
					boxedValue = obj;
				}
				GUI.backgroundColor = backgroundColor;
				if (j < Mathf.Min(i + num, options.Count) - 1)
				{
					GUILayout.Space(6f);
				}
			}
			GUILayout.EndHorizontal();
			if (i + num < options.Count)
			{
				GUILayout.Space(4f);
			}
		}
		GUILayout.EndVertical();
	}

	private void DrawImmediateEntryControl(ConfigEntryBase entry)
	{
		Type entryValueType = GetEntryValueType(entry);
		if (entryValueType == typeof(bool))
		{
			bool flag = GetBoxedValue(entry) is bool boolValue && boolValue;
			bool flag2 = GUILayout.Toggle(flag, GetToggleStateLabel(flag), GUILayout.Width(80f), GUILayout.Height(28f));
			if (flag2 != flag)
			{
				TryAssignBoxedValue(entry, flag2);
				SaveEntryOwner(entry);
			}
			return;
		}
		if (entryValueType == typeof(KeyCode))
		{
			string text = ((_pendingKeyCaptureEntry == entry) ? "鎸変换鎰忛敭..." : GetDisplayValueText(entry));
			if (GUILayout.Button(text, GUILayout.Width(220f), GUILayout.Height(28f)))
			{
				_pendingKeyCaptureEntry = entry;
				_pendingKeyCaptureText = null;
			}
			return;
		}
		if (TryDrawImmediateRangeControl(entry, entryValueType))
		{
			return;
		}
		if (TryGetSelectableOptions(entry, entryValueType, out List<object> options) && options.Count > 0)
		{
			if (ShouldRenderOptionSelectionBox(entry))
			{
				DrawImmediateOptionSelectionBox(entry, options);
				return;
			}
			object boxedValue = GetBoxedValue(entry);
			int num = Mathf.Max(0, options.FindIndex((object option) => ValuesEqual(option, boxedValue)));
			GUILayout.BeginHorizontal(GUILayout.Width(220f));
			if (GUILayout.Button("<", GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num - 1 + options.Count) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
				GUILayout.Box(GetOptionDisplayText(GetSourceForEntry(entry), entry, options[num]), GUILayout.Width(160f), GUILayout.Height(28f));
			if (GUILayout.Button(">", GUILayout.Width(28f), GUILayout.Height(28f)))
			{
				num = (num + 1) % options.Count;
				TryAssignBoxedValue(entry, options[num]);
				SaveEntryOwner(entry);
			}
			GUILayout.EndHorizontal();
			return;
		}
		string text2 = (!_textBufferByEntry.TryGetValue(entry, out var value) ? GetDisplayValueText(entry) : value);
		string text3 = "cfg_" + (entry.Definition?.Section ?? string.Empty) + "_" + (entry.Definition?.Key ?? string.Empty);
		GUI.SetNextControlName(text3);
		string text4 = GUILayout.TextField(text2 ?? string.Empty, GUILayout.Width(260f), GUILayout.Height(28f));
		if (!string.Equals(text4, text2, StringComparison.Ordinal))
		{
			_textBufferByEntry[entry] = text4;
		}
		bool flag3 = string.Equals(GUI.GetNameOfFocusedControl(), text3, StringComparison.Ordinal);
		if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && flag3)
		{
			CommitBufferedEntryText(entry, entryValueType);
			GUI.FocusControl(null);
			Event.current.Use();
		}
		if (!flag3 && _textBufferByEntry.TryGetValue(entry, out var _) && !string.Equals(_textBufferByEntry[entry], GetDisplayValueText(entry), StringComparison.Ordinal))
		{
			CommitBufferedEntryText(entry, entryValueType);
		}
	}

	private bool TryDrawImmediateRangeControl(ConfigEntryBase entry, Type entryValueType)
	{
		if ((entryValueType != typeof(int) && entryValueType != typeof(float) && entryValueType != typeof(double)) || !TryGetNumericRange(entry, out var min, out var max))
		{
			return false;
		}
		float num = (float)min;
		float num2 = (float)max;
		GUILayout.BeginHorizontal(GUILayout.Width(320f));
		if (entryValueType == typeof(int))
		{
			int num3 = Convert.ToInt32(GetBoxedValue(entry) ?? 0, CultureInfo.InvariantCulture);
			int num4 = Mathf.RoundToInt(GUILayout.HorizontalSlider(num3, num, num2, GUILayout.Width(240f)));
			num4 = Mathf.Clamp(num4, Mathf.RoundToInt(num), Mathf.RoundToInt(num2));
			if (num4 != num3)
			{
				TryAssignBoxedValue(entry, num4);
				SaveEntryOwner(entry);
			}
		}
		else if (entryValueType == typeof(double))
		{
			double num5 = Convert.ToDouble(GetBoxedValue(entry) ?? 0.0, CultureInfo.InvariantCulture);
			double num6 = GUILayout.HorizontalSlider((float)num5, num, num2, GUILayout.Width(240f));
			if (Math.Abs(num6 - num5) > 0.0001)
			{
				TryAssignBoxedValue(entry, num6);
				SaveEntryOwner(entry);
			}
		}
		else
		{
			float num7 = Convert.ToSingle(GetBoxedValue(entry) ?? 0f, CultureInfo.InvariantCulture);
			float num8 = GUILayout.HorizontalSlider(num7, num, num2, GUILayout.Width(240f));
			if (Mathf.Abs(num8 - num7) > 0.0001f)
			{
				TryAssignBoxedValue(entry, num8);
				SaveEntryOwner(entry);
			}
		}
		GUILayout.Space(10f);
		GUILayout.Label(GetDisplayValueText(entry), GUILayout.Width(70f));
		GUILayout.EndHorizontal();
		return true;
	}

	private static bool ShouldRenderOptionSelectionBox(ConfigEntryBase entry)
	{
		return (object)entry == Plugin.WeaponSelection || (object)entry == Plugin.AkSoundSelection || (object)entry == Plugin.ConfigPanelTheme;
	}

	private void DrawImmediateOptionSelectionBox(ConfigEntryBase entry, List<object> options)
	{
		object boxedValue = GetBoxedValue(entry);
		int num = ((object)entry == Plugin.AkSoundSelection) ? options.Count : options.Count;
		float num2 = ((object)entry == Plugin.AkSoundSelection) ? 72f : 72f;
		GUILayout.BeginVertical(GUILayout.Width(num2 * num + 6f * (num - 1)));
		for (int i = 0; i < options.Count; i += num)
		{
			GUILayout.BeginHorizontal();
			for (int j = i; j < Mathf.Min(i + num, options.Count); j++)
			{
				object obj = options[j];
				bool flag = ValuesEqual(obj, boxedValue);
				Color backgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = (flag ? new Color(0.27f, 0.66f, 0.58f, 1f) : new Color(0.34f, 0.37f, 0.45f, 1f));
					if (GUILayout.Button(GetOptionDisplayText(GetSourceForEntry(entry), entry, obj), GUILayout.Width(num2), GUILayout.Height(28f)) && !flag)
				{
					TryAssignBoxedValue(entry, obj);
					SaveEntryOwner(entry);
					boxedValue = obj;
				}
				GUI.backgroundColor = backgroundColor;
				if (j < Mathf.Min(i + num, options.Count) - 1)
				{
					GUILayout.Space(6f);
				}
			}
			GUILayout.EndHorizontal();
			if (i + num < options.Count)
			{
				GUILayout.Space(4f);
			}
		}
		GUILayout.EndVertical();
	}

	private void CommitBufferedEntryText(ConfigEntryBase entry, Type valueType)
	{
		if (entry == null || !_textBufferByEntry.TryGetValue(entry, out var value))
		{
			return;
		}
		if (TryAssignTextValue(entry, valueType, value))
		{
			SaveEntryOwner(entry);
			_textBufferByEntry[entry] = GetDisplayValueText(entry);
		}
		else
		{
			_textBufferByEntry[entry] = GetDisplayValueText(entry);
		}
	}

	private void BuildUi()
	{
		_rootObject = new GameObject("ShootZombiesLobbyConfigPanel", typeof(RectTransform));
		_canvas = _rootObject.AddComponent<Canvas>();
		_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_canvas.sortingOrder = 3100;
		CanvasScaler canvasScaler = _rootObject.AddComponent<CanvasScaler>();
		canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
		canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		canvasScaler.matchWidthOrHeight = 0.5f;
		_rootObject.AddComponent<GraphicRaycaster>();
		UnityEngine.Object.DontDestroyOnLoad(_rootObject);
		GameObject gameObject = CreateUiObject("Backdrop", _rootObject.transform);
		RectTransform rectTransform = GetRectTransform(gameObject);
		StretchToFill(rectTransform);
		Image image = gameObject.AddComponent<Image>();
		image.color = BackdropColor;
		GameObject gameObject2 = CreateUiObject("Panel", gameObject.transform);
		RectTransform rectTransform2 = (_panelRect = GetRectTransform(gameObject2));
		Image image2 = gameObject2.AddComponent<Image>();
		image2.color = PanelColor;
		AddOutline(gameObject2, BorderColor);
		ClampPanelToScreen();
		GameObject gameObject3 = CreateUiObject("Header", gameObject2.transform);
		RectTransform rectTransform3 = GetRectTransform(gameObject3);
		rectTransform3.anchorMin = new Vector2(0f, 1f);
		rectTransform3.anchorMax = new Vector2(1f, 1f);
		rectTransform3.pivot = new Vector2(0.5f, 1f);
		rectTransform3.sizeDelta = new Vector2(0f, 62f);
		rectTransform3.anchoredPosition = Vector2.zero;
		Image image3 = gameObject3.AddComponent<Image>();
		image3.color = HeaderColor;
		_panelTitleText = CreateText(gameObject3.transform, "ShootZombies", 28f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
		RectTransform rectTransform4 = _panelTitleText.rectTransform;
		rectTransform4.anchorMin = new Vector2(0f, 0f);
		rectTransform4.anchorMax = new Vector2(1f, 1f);
		rectTransform4.offsetMin = new Vector2(26f, 0f);
		rectTransform4.offsetMax = new Vector2(-100f, 0f);
		Button button = CreateButton(gameObject3.transform, "x", new Color(0.55f, 0.24f, 0.24f, 1f), 22f, 40f, 32f);
		RectTransform component = button.GetComponent<RectTransform>();
		component.anchorMin = new Vector2(1f, 0.5f);
		component.anchorMax = new Vector2(1f, 0.5f);
		component.pivot = new Vector2(1f, 0.5f);
		component.anchoredPosition = new Vector2(-20f, 0f);
		button.onClick.AddListener(Close);
		GameObject gameObject4 = CreateUiObject("PluginTabs", gameObject2.transform);
		_pluginTabsRect = GetRectTransform(gameObject4);
		_pluginTabsRect.anchorMin = new Vector2(0f, 1f);
		_pluginTabsRect.anchorMax = new Vector2(1f, 1f);
		_pluginTabsRect.pivot = new Vector2(0.5f, 1f);
		_pluginTabsRect.sizeDelta = new Vector2(-40f, 42f);
		_pluginTabsRect.anchoredPosition = new Vector2(0f, -74f);
		GameObject gameObject5 = CreateUiObject("Sidebar", gameObject2.transform);
		RectTransform rectTransform5 = GetRectTransform(gameObject5);
		rectTransform5.anchorMin = new Vector2(0f, 0f);
		rectTransform5.anchorMax = new Vector2(0f, 1f);
		rectTransform5.pivot = new Vector2(0f, 1f);
		rectTransform5.sizeDelta = new Vector2(250f, -134f);
		rectTransform5.anchoredPosition = new Vector2(20f, -124f);
		Image image4 = gameObject5.AddComponent<Image>();
		image4.color = SidebarColor;
		AddOutline(gameObject5, BorderColor);
		TextMeshProUGUI textMeshProUGUI = CreateText(gameObject5.transform, "Sections", 20f, TextPrimaryColor, TextAlignmentOptions.TopLeft);
		RectTransform rectTransform6 = textMeshProUGUI.rectTransform;
		rectTransform6.anchorMin = new Vector2(0f, 1f);
		rectTransform6.anchorMax = new Vector2(1f, 1f);
		rectTransform6.pivot = new Vector2(0f, 1f);
		rectTransform6.sizeDelta = new Vector2(-28f, 28f);
		rectTransform6.anchoredPosition = new Vector2(16f, -12f);
		GameObject gameObject6 = CreateUiObject("SectionList", gameObject5.transform);
		_sectionListRect = GetRectTransform(gameObject6);
		_sectionListRect.anchorMin = new Vector2(0f, 0f);
		_sectionListRect.anchorMax = new Vector2(1f, 1f);
		_sectionListRect.offsetMin = new Vector2(14f, 14f);
		_sectionListRect.offsetMax = new Vector2(-14f, -52f);
		GameObject gameObject7 = CreateUiObject("Content", gameObject2.transform);
		RectTransform rectTransform7 = GetRectTransform(gameObject7);
		rectTransform7.anchorMin = new Vector2(0f, 0f);
		rectTransform7.anchorMax = new Vector2(1f, 1f);
		rectTransform7.offsetMin = new Vector2(286f, 20f);
		rectTransform7.offsetMax = new Vector2(-20f, -124f);
		Image image5 = gameObject7.AddComponent<Image>();
		image5.color = ContentColor;
		AddOutline(gameObject7, BorderColor);
		GameObject gameObject8 = CreateUiObject("ContentHeader", gameObject7.transform);
		RectTransform rectTransform8 = GetRectTransform(gameObject8);
		rectTransform8.anchorMin = new Vector2(0f, 1f);
		rectTransform8.anchorMax = new Vector2(1f, 1f);
		rectTransform8.pivot = new Vector2(0.5f, 1f);
		rectTransform8.sizeDelta = new Vector2(0f, 52f);
		rectTransform8.anchoredPosition = Vector2.zero;
		Image image6 = gameObject8.AddComponent<Image>();
		image6.color = HeaderColor;
		_contentTitleText = CreateText(gameObject8.transform, "Config", 24f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
		RectTransform rectTransform9 = _contentTitleText.rectTransform;
		rectTransform9.anchorMin = new Vector2(0f, 0f);
		rectTransform9.anchorMax = new Vector2(1f, 1f);
		rectTransform9.offsetMin = new Vector2(20f, 0f);
		rectTransform9.offsetMax = new Vector2(-20f, 0f);
		CreateScrollView(gameObject7.transform, out var container, out _entryListRect, new Vector2(16f, 16f), new Vector2(-16f, -68f));
		LobbyConfigPanelGuiHost lobbyConfigPanelGuiHost = _rootObject.AddComponent<LobbyConfigPanelGuiHost>();
		lobbyConfigPanelGuiHost.Owner = this;
		_canvas.enabled = false;
		_rootObject.SetActive(false);
	}

	private void ClampPanelToScreen()
	{
		if ((UnityEngine.Object)_panelRect == (UnityEngine.Object)null)
		{
			return;
		}
		float num = Mathf.Max((float)Screen.width, 1f);
		float num2 = Mathf.Max((float)Screen.height, 1f);
		Rect safeArea = Screen.safeArea;
		float num3 = Mathf.Max(Mathf.Min(safeArea.width - 96f, safeArea.width * 0.76f), 960f);
		float num4 = Mathf.Max(Mathf.Min(safeArea.height - 84f, safeArea.height * 0.74f), 620f);
		num3 = Mathf.Clamp(num3, 640f, Mathf.Max(safeArea.width - 32f, 640f));
		num4 = Mathf.Clamp(num4, 520f, Mathf.Max(safeArea.height - 32f, 520f));
		Vector2 vector = safeArea.center - new Vector2(num * 0.5f, num2 * 0.5f);
		_panelRect.anchorMin = new Vector2(0.5f, 0.5f);
		_panelRect.anchorMax = new Vector2(0.5f, 0.5f);
		_panelRect.pivot = new Vector2(0.5f, 0.5f);
		_panelRect.sizeDelta = new Vector2(num3, num4);
		_panelRect.anchoredPosition = vector;
	}

	private void CreateScrollView(Transform parent, out RectTransform viewportRect, out RectTransform contentRect, Vector2 offsetMin, Vector2 offsetMax)
	{
		GameObject gameObject = CreateUiObject("ScrollView", parent);
		RectTransform rectTransform = GetRectTransform(gameObject);
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = offsetMin;
		rectTransform.offsetMax = offsetMax;
		Image image = gameObject.AddComponent<Image>();
		image.color = new Color(0f, 0f, 0f, 0.08f);
		ScrollRect scrollRect = gameObject.AddComponent<ScrollRect>();
		scrollRect.horizontal = false;
		scrollRect.scrollSensitivity = 28f;
		scrollRect.movementType = ScrollRect.MovementType.Clamped;
		GameObject gameObject2 = CreateUiObject("Viewport", gameObject.transform);
		viewportRect = GetRectTransform(gameObject2);
		StretchToFill(viewportRect);
		Image image2 = gameObject2.AddComponent<Image>();
		image2.color = Color.clear;
		Mask mask = gameObject2.AddComponent<Mask>();
		mask.showMaskGraphic = false;
		GameObject gameObject3 = CreateUiObject("Content", gameObject2.transform);
		contentRect = GetRectTransform(gameObject3);
		contentRect.anchorMin = new Vector2(0f, 1f);
		contentRect.anchorMax = new Vector2(1f, 1f);
		contentRect.pivot = new Vector2(0.5f, 1f);
		contentRect.anchoredPosition = Vector2.zero;
		contentRect.sizeDelta = new Vector2(0f, 0f);
		scrollRect.viewport = viewportRect;
		scrollRect.content = contentRect;
	}

	private static Button CreateButton(Transform parent, string label, Color backgroundColor, float fontSize, float preferredWidth, float preferredHeight)
	{
		GameObject gameObject = CreateUiObject("Button", parent);
		RectTransform rectTransform = GetRectTransform(gameObject);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		if (preferredWidth > 0f)
		{
			layoutElement.preferredWidth = preferredWidth;
			rectTransform.sizeDelta = new Vector2(preferredWidth, preferredHeight);
		}
		else
		{
			rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, preferredHeight);
		}
		layoutElement.preferredHeight = preferredHeight;
		Image image = gameObject.AddComponent<Image>();
		image.color = backgroundColor;
		AddOutline(gameObject, BorderColor);
		Button button = gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		TextMeshProUGUI textMeshProUGUI = CreateText(gameObject.transform, label, fontSize, TextPrimaryColor, TextAlignmentOptions.Center);
		StretchToFill(textMeshProUGUI.rectTransform, 10f, 0f);
		return button;
	}

	private TMP_Dropdown CreateDropdown(Transform parent, float width, float height)
	{
		GameObject gameObject = CreateUiObject("Dropdown", parent);
		RectTransform rectTransform = GetRectTransform(gameObject);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = width;
		layoutElement.preferredHeight = height;
		rectTransform.sizeDelta = new Vector2(width, height);
		Image image = gameObject.AddComponent<Image>();
		image.color = FieldColor;
		AddOutline(gameObject, BorderColor);
		TMP_Dropdown tMP_Dropdown = gameObject.AddComponent<TMP_Dropdown>();
		tMP_Dropdown.targetGraphic = image;
		GameObject gameObject2 = CreateUiObject("Label", gameObject.transform);
		RectTransform rectTransformLabel = GetRectTransform(gameObject2);
		rectTransformLabel.anchorMin = Vector2.zero;
		rectTransformLabel.anchorMax = Vector2.one;
		rectTransformLabel.offsetMin = new Vector2(12f, 4f);
		rectTransformLabel.offsetMax = new Vector2(-30f, -4f);
		TextMeshProUGUI textMeshProUGUI = CreateText(gameObject2.transform, string.Empty, 17f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
		StretchToFill(textMeshProUGUI.rectTransform);
		tMP_Dropdown.captionText = textMeshProUGUI;
		GameObject gameObject3 = CreateUiObject("Arrow", gameObject.transform);
		RectTransform rectTransform2 = GetRectTransform(gameObject3);
		rectTransform2.anchorMin = new Vector2(1f, 0.5f);
		rectTransform2.anchorMax = new Vector2(1f, 0.5f);
		rectTransform2.pivot = new Vector2(1f, 0.5f);
		rectTransform2.sizeDelta = new Vector2(20f, 20f);
		rectTransform2.anchoredPosition = new Vector2(-8f, 0f);
		TextMeshProUGUI textMeshProUGUI2 = CreateText(gameObject3.transform, "v", 16f, TextPrimaryColor, TextAlignmentOptions.Center);
		StretchToFill(textMeshProUGUI2.rectTransform);
		GameObject gameObject4 = CreateUiObject("Template", gameObject.transform);
		RectTransform rectTransform3 = GetRectTransform(gameObject4);
		rectTransform3.anchorMin = new Vector2(0f, 0f);
		rectTransform3.anchorMax = new Vector2(1f, 0f);
		rectTransform3.pivot = new Vector2(0.5f, 1f);
		rectTransform3.sizeDelta = new Vector2(0f, 180f);
		rectTransform3.anchoredPosition = new Vector2(0f, -4f);
		Image image2 = gameObject4.AddComponent<Image>();
		image2.color = FieldColor;
		AddOutline(gameObject4, BorderColor);
		ScrollRect scrollRect = gameObject4.AddComponent<ScrollRect>();
		scrollRect.horizontal = false;
		scrollRect.scrollSensitivity = 24f;
		scrollRect.movementType = ScrollRect.MovementType.Clamped;
		GameObject gameObject5 = CreateUiObject("Viewport", gameObject4.transform);
		RectTransform rectTransform4 = GetRectTransform(gameObject5);
		StretchToFill(rectTransform4);
		Image image3 = gameObject5.AddComponent<Image>();
		image3.color = Color.clear;
		Mask mask = gameObject5.AddComponent<Mask>();
		mask.showMaskGraphic = false;
		GameObject gameObject6 = CreateUiObject("Content", gameObject5.transform);
		RectTransform rectTransform5 = GetRectTransform(gameObject6);
		rectTransform5.anchorMin = new Vector2(0f, 1f);
		rectTransform5.anchorMax = new Vector2(1f, 1f);
		rectTransform5.pivot = new Vector2(0.5f, 1f);
		rectTransform5.anchoredPosition = Vector2.zero;
		VerticalLayoutGroup verticalLayoutGroup = gameObject6.AddComponent<VerticalLayoutGroup>();
		verticalLayoutGroup.childControlWidth = true;
		verticalLayoutGroup.childControlHeight = true;
		verticalLayoutGroup.childForceExpandWidth = true;
		verticalLayoutGroup.childForceExpandHeight = false;
		verticalLayoutGroup.spacing = 2f;
		ContentSizeFitter contentSizeFitter = gameObject6.AddComponent<ContentSizeFitter>();
		contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		scrollRect.viewport = rectTransform4;
		scrollRect.content = rectTransform5;
		GameObject gameObject7 = CreateUiObject("Item", gameObject6.transform);
		RectTransform rectTransform6 = GetRectTransform(gameObject7);
		rectTransform6.sizeDelta = new Vector2(0f, 30f);
		Toggle toggle = gameObject7.AddComponent<Toggle>();
		Image image4 = gameObject7.AddComponent<Image>();
		image4.color = ButtonColor;
		toggle.targetGraphic = image4;
		GameObject gameObject8 = CreateUiObject("ItemCheckmark", gameObject7.transform);
		RectTransform rectTransform7 = GetRectTransform(gameObject8);
		rectTransform7.anchorMin = new Vector2(0f, 0.5f);
		rectTransform7.anchorMax = new Vector2(0f, 0.5f);
		rectTransform7.pivot = new Vector2(0f, 0.5f);
		rectTransform7.sizeDelta = new Vector2(18f, 18f);
		rectTransform7.anchoredPosition = new Vector2(6f, 0f);
		Image image5 = gameObject8.AddComponent<Image>();
		image5.color = ButtonActiveColor;
		toggle.graphic = image5;
		GameObject gameObject9 = CreateUiObject("ItemLabel", gameObject7.transform);
		RectTransform rectTransform8 = GetRectTransform(gameObject9);
		rectTransform8.anchorMin = Vector2.zero;
		rectTransform8.anchorMax = Vector2.one;
		rectTransform8.offsetMin = new Vector2(28f, 2f);
		rectTransform8.offsetMax = new Vector2(-6f, -2f);
		TextMeshProUGUI textMeshProUGUI3 = CreateText(gameObject9.transform, "Option", 16f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
		StretchToFill(textMeshProUGUI3.rectTransform);
		tMP_Dropdown.template = rectTransform3;
		tMP_Dropdown.itemText = textMeshProUGUI3;
		tMP_Dropdown.captionText = textMeshProUGUI;
		gameObject4.SetActive(false);
		return tMP_Dropdown;
	}

	private TMP_InputField CreateInputField(Transform parent, float width, float height)
	{
		GameObject gameObject = CreateUiObject("InputField", parent);
		RectTransform rectTransform = GetRectTransform(gameObject);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = width;
		layoutElement.preferredHeight = height;
		rectTransform.sizeDelta = new Vector2(width, height);
		Image image = gameObject.AddComponent<Image>();
		image.color = FieldColor;
		AddOutline(gameObject, BorderColor);
		TMP_InputField tMP_InputField = gameObject.AddComponent<TMP_InputField>();
		GameObject gameObject2 = CreateUiObject("TextArea", gameObject.transform);
		RectTransform textAreaRect = GetRectTransform(gameObject2);
		textAreaRect.anchorMin = Vector2.zero;
		textAreaRect.anchorMax = Vector2.one;
		textAreaRect.offsetMin = new Vector2(10f, 5f);
		textAreaRect.offsetMax = new Vector2(-10f, -5f);
		TextMeshProUGUI textMeshProUGUI = CreateText(gameObject2.transform, string.Empty, 17f, TextPrimaryColor, TextAlignmentOptions.MidlineLeft);
		StretchToFill(textMeshProUGUI.rectTransform);
		TextMeshProUGUI textMeshProUGUI2 = CreateText(gameObject2.transform, "...", 17f, TextMutedColor, TextAlignmentOptions.MidlineLeft);
		StretchToFill(textMeshProUGUI2.rectTransform);
		tMP_InputField.textViewport = textAreaRect;
		tMP_InputField.textComponent = textMeshProUGUI;
		tMP_InputField.placeholder = textMeshProUGUI2;
		tMP_InputField.caretColor = TextPrimaryColor;
		tMP_InputField.selectionColor = new Color(0.35f, 0.58f, 0.84f, 0.35f);
		return tMP_InputField;
	}

	private static void CreateEmptyState(Transform parent, string text)
	{
		TextMeshProUGUI textMeshProUGUI = CreateText(parent, text, 22f, TextMutedColor, TextAlignmentOptions.Center);
		LayoutElement layoutElement = textMeshProUGUI.gameObject.AddComponent<LayoutElement>();
		layoutElement.minHeight = 200f;
	}

	private static TextMeshProUGUI CreateText(Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment)
	{
		GameObject gameObject = CreateUiObject("Text", parent);
		TextMeshProUGUI textMeshProUGUI = gameObject.AddComponent<TextMeshProUGUI>();
		textMeshProUGUI.text = text;
		textMeshProUGUI.fontSize = fontSize;
		textMeshProUGUI.color = color;
		textMeshProUGUI.alignment = alignment;
		textMeshProUGUI.textWrappingMode = TextWrappingModes.NoWrap;
		TryApplyDefaultFont(textMeshProUGUI);
		return textMeshProUGUI;
	}

	private static void TryApplyDefaultFont(TextMeshProUGUI text)
	{
		if (text == null)
		{
			return;
		}
		GUIManager instance = GUIManager.instance;
		TextMeshProUGUI val = (((Object)instance?.itemPromptMain != (Object)null) ? instance.itemPromptMain : instance?.interactNameText);
		if ((Object)val != (Object)null)
		{
			text.font = val.font;
			text.fontSharedMaterial = val.fontSharedMaterial;
			return;
		}
		if ((Object)TMP_Settings.defaultFontAsset != (Object)null)
		{
			text.font = TMP_Settings.defaultFontAsset;
		}
	}

	private static void EnsureEventSystem()
	{
		if ((Object)EventSystem.current != (Object)null)
		{
			GameObject gameObject2 = ((Component)EventSystem.current).gameObject;
			if ((Object)gameObject2.GetComponent<StandaloneInputModule>() == (Object)null)
			{
				gameObject2.AddComponent<StandaloneInputModule>();
			}
			return;
		}
		GameObject gameObject = new GameObject("ShootZombiesConfigEventSystem");
		gameObject.AddComponent<EventSystem>();
		gameObject.AddComponent<StandaloneInputModule>();
		UnityEngine.Object.DontDestroyOnLoad(gameObject);
	}

	private static bool IsTypingIntoInputField()
	{
		GameObject currentSelectedGameObject = EventSystem.current?.currentSelectedGameObject;
		if ((Object)currentSelectedGameObject == (Object)null)
		{
			return false;
		}
		return (Object)currentSelectedGameObject.GetComponentInParent<TMP_InputField>() != (Object)null;
	}

	private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
	{
		T component = gameObject.GetComponent<T>();
		if ((Object)component == (Object)null)
		{
			component = gameObject.AddComponent<T>();
		}
		return component;
	}

	private static RectTransform GetRectTransform(GameObject gameObject)
	{
		RectTransform component = gameObject.GetComponent<RectTransform>();
		if ((Object)component == (Object)null)
		{
			component = gameObject.AddComponent<RectTransform>();
		}
		return component;
	}

	private static GameObject CreateUiObject(string name, Transform parent)
	{
		GameObject gameObject = new GameObject(name, typeof(RectTransform));
		if ((Object)parent != (Object)null)
		{
			gameObject.transform.SetParent(parent, false);
		}
		gameObject.layer = 5;
		return gameObject;
	}

	private static void DestroyChildren(Transform parent)
	{
		if ((Object)parent == (Object)null)
		{
			return;
		}
		for (int i = parent.childCount - 1; i >= 0; i--)
		{
			UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
		}
	}

	private static void StretchToFill(RectTransform rectTransform, float horizontalPadding = 0f, float verticalPadding = 0f)
	{
		if ((Object)rectTransform == (Object)null)
		{
			return;
		}
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = new Vector2(horizontalPadding, verticalPadding);
		rectTransform.offsetMax = new Vector2(0f - horizontalPadding, 0f - verticalPadding);
	}

	private static void AddOutline(GameObject gameObject, Color color)
	{
		Outline outline = gameObject.AddComponent<Outline>();
		outline.effectColor = color;
		outline.effectDistance = new Vector2(1f, -1f);
	}
}

[HarmonyPatch(typeof(CursorHandler), "Update")]
internal static class LobbyConfigPanelCursorPatch
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		if (!LobbyConfigPanel.IsCursorOwnershipActive())
		{
			return true;
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		return false;
	}
}




