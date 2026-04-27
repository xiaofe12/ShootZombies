using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;

namespace FogColdControl;

[BepInPlugin("com.github.Thanks.FogClimb", "Fog&ColdControl", "1.0.0")]
[BepInDependency(/*Could not decode attribute arguments.*/)]
public sealed class Plugin : BaseUnityPlugin
{
	private enum ConfigKey
	{
		ModEnabled,
		FogColdSuppression,
		NightColdEnabled,
		FogSpeed,
		FogDelay,
		CompassEnabled,
		CompassHotkey,
		FogPauseHotkey,
		FogUiEnabled,
		CampfireLocatorUiEnabled,
		FogUiX,
		FogUiY,
		FogUiScale
	}

	private enum VanillaProgressStartUiState
	{
		Hidden,
		Unavailable,
		Tracking
	}

	private enum FogUiIconKind
	{
		State,
		Speed,
		Buffer,
		Delay,
		Distance,
		Eta,
		Direct,
		Pause,
		FogHandling,
		Night
	}

	private sealed class FogUiEntryView
	{
		public RectTransform Root;

		public Image Icon;

		public TextMeshProUGUI Text;

		public string LastText = string.Empty;

		public FogUiIconKind LastIconKind;

		public Color LastIconColor = Color.clear;
	}

	private readonly struct FogUiDisplayEntry
	{
		public readonly FogUiIconKind Kind;

		public readonly Color IconColor;

		public readonly string Text;

		public FogUiDisplayEntry(FogUiIconKind kind, Color iconColor, string text)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			Kind = kind;
			IconColor = iconColor;
			Text = text ?? string.Empty;
		}
	}

	public const string PluginGuid = "com.github.Thanks.FogClimb";

	public const string PluginName = "Fog&ColdControl";

	public const string PluginVersion = "1.0.0";

	private const string PreferredConfigFileName = "Thanks.Fog&ColdControl.cfg";

	private const string PreferredPluginFileName = "Thanks.Fog&ColdControl.dll";

	private static readonly string[] LegacyConfigFileNames = new string[2] { "Thanks.FogClimb.cfg", "com.github.Thanks.FogClimb.cfg" };

	private static readonly string[] LegacyPluginFileNames = new string[2] { "Thanks.FogClimb.dll", "com.github.Thanks.FogClimb.dll" };

	private const float DefaultVanillaFogSpeed = 0.3f;

	private const float DefaultFogSpeed = 0.4f;

	private const float DefaultFogDelaySeconds = 900f;

	private const float MinFogSpeed = 0.3f;

	private const float MaxFogSpeed = 20f;

	private const float MinFogDelaySeconds = 20f;

	private const float MaxFogDelaySeconds = 1000f;

	private const float DefaultFogUiX = 60f;

	private const float DefaultFogUiY = 0f;

	private const float DefaultFogUiScale = 0.9f;

	private const float MinFogUiX = -400f;

	private const float MaxFogUiX = 400f;

	private const float MinFogUiY = -500f;

	private const float MaxFogUiY = 500f;

	private const float MinFogUiScale = 0.5f;

	private const float MaxFogUiScale = 2.5f;

	private const float FogHandlerSearchIntervalSeconds = 1f;

	private const float FogStateSyncIntervalSeconds = 0.18f;

	private const float FogStateSyncSizeThreshold = 0.35f;

	private const float RemoteStatusSyncIntervalSeconds = 0.25f;

	private const float LateGameRemoteStatusSyncIntervalSeconds = 0.1f;

	private const float CompassGrantSyncIntervalSeconds = 0.75f;

	private const float FogColdPerSecond = 0.0105f;

	private const float RemoteFogSuppressionSafetyMultiplier = 1.12f;

	private const float NightColdPerSecond = 0.008f;

	private const float StatusChunkSize = 0.025f;

	private const float StalledFogResumeDelayRatio = 0.02f;

	private const float MinStalledFogResumeDelaySeconds = 2f;

	private const float MaxStalledFogResumeDelaySeconds = 8f;

	private const float HiddenFogDelayBufferSeconds = 10f;

	private const float FogEtaMinSampleIntervalSeconds = 0.1f;

	private const float FogEtaMinSizeDelta = 0.05f;

	private const float FogEtaMinReliableRate = 0.02f;

	private const float FogEtaRateSmoothing = 0.35f;

	private const float FogEtaDangerWindowSeconds = 45f;

	private const float FogEtaWarningWindowSeconds = 90f;

	private const float FogEtaDisplayStepSeconds = 0.5f;

	private const float FogDistanceDangerWindowUnits = 120f;

	private const float FogDistanceWarningWindowUnits = 300f;

	private const float FogDistanceEtaRefreshIntervalSeconds = 0.8f;

	private const string FogUiDistanceLabelColor = "#79E2D0";

	private const string FogUiDistanceValueColor = "#D9FFF5";

	private const string FogUiDistanceWarningLabelColor = "#FFB864";

	private const string FogUiDistanceWarningValueColor = "#FFE6BF";

	private const float PeakFogMinStartSize = 650f;

	private const float PeakFogMaxStartSize = 1400f;

	private const float PeakVerticalFogStopHeight = 1800f;

	private const float FogArrivalStopSize = 30f;

	private const KeyCode HiddenNightTestHotkey = (KeyCode)91;

	private const float HiddenNightTestHoldSeconds = 5f;

	private const float FallbackNightTimeNormalized = 0.85f;

	private const float RemotePlayerJoinGraceSeconds = 8f;

	private const int MaxCompassGrantsPerPlayerPerSync = 1;

	private const float CampfireCompassGrantDelaySeconds = 0.9f;

	private const float FogUiWidth = 1360f;

	private const float FogUiHeight = 34f;

	private const float FogUiHorizontalPadding = 10f;

	private const float FogUiIconSize = 19f;

	private const float FogUiIconVerticalOffset = -1f;

	private const float FogUiEntrySpacing = 14f;

	private const float FogUiEntryIconTextSpacing = 3f;

	private const float FogUiEntryTextSizeMultiplier = 0.9f;

	private const float CampfireLocatorUiWidth = 372f;

	private const float CampfireLocatorUiHeight = 24f;

	private const float CampfireLocatorTopOffset = 54f;

	private const float CampfireLocatorLineWidth = 360f;

	private const float CampfireLocatorLineHeight = 2f;

	private const float CampfireLocatorDotSize = 18f;

	private const float CampfireLocatorDotSmoothing = 12f;

	private const float CompassLobbyNoticeRightOffset = 28f;

	private const float CompassLobbyNoticeDownOffset = 0f;

	private const float CompassLobbyNoticeWidth = 735f;

	private const float CompassLobbyNoticeHeight = 81f;

	private const float CompassLobbyNoticeFontSizeMultiplier = 1.2f;

	private const float CompassLobbyNoticeLineSpacing = 1.125f;

	private const string CompassLobbyNoticeKeyColor = "#FF3B30";

	private const string FogUiSpeedLabelColor = "#8EC5FF";

	private const string FogUiSpeedValueColor = "#D6F1FF";

	private const string FogUiWaitLabelColor = "#F2C75C";

	private const string FogUiWaitValueColor = "#FFE8A3";

	private const string FogUiCountdownLabelColor = "#FF8A5B";

	private const string FogUiCountdownValueColor = "#FFD2B8";

	private const string FogUiCountdownDangerLabelColor = "#FF2D2D";

	private const string FogUiCountdownDangerValueColor = "#FFC0C0";

	private const string FogUiDelayCountdownStartLabelColor = "#FF8A8A";

	private const string FogUiDelayCountdownStartValueColor = "#FFD6D6";

	private const string FogUiDelayCountdownEndLabelColor = "#7A0000";

	private const string FogUiDelayCountdownEndValueColor = "#B31212";

	private const string FogUiStateLabelColor = "#A8E0A0";

	private const string FogUiStateRunningColor = "#B5FFB8";

	private const string FogUiStatePausedColor = "#FFC37D";

	private const string FogUiStateWaitingColor = "#FFE08A";

	private const string FogUiStateSyncingColor = "#A3D2FF";

	private const string FogUiHintLabelColor = "#B7C0CC";

	private const string FogUiHintValueColor = "#E2EAF3";

	private const string FogUiNightEnabledColor = "#9FFFA8";

	private const string FogUiNightDisabledColor = "#FFB3B3";

	private static readonly ushort CompassItemIdOverride = 0;

	private const string CompassNameKeyword = "Compass";

	private const string ModConfigPluginGuid = "com.github.PEAKModding.PEAKLib.ModConfig";

	private const string LegacyCanonicalConfigSectionName = "Fog";

	private const string CanonicalBasicConfigSectionName = "Basic";

	private const string CanonicalAdjustmentConfigSectionName = "Adjustments";

	private const string NetworkInstallStateKey = "FogClimb.Enabled";

	private const int SimplifiedChineseLanguageIndex = 9;

	private static readonly BindingFlags InstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly BindingFlags StaticBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly FieldInfo ConfigEntryDescriptionField = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", InstanceBindingFlags);

	private static readonly PropertyInfo ConfigFileEntriesProperty = typeof(ConfigFile).GetProperty("Entries", InstanceBindingFlags);

	private static readonly FieldInfo BasePluginConfigBackingField = typeof(BaseUnityPlugin).GetField("<Config>k__BackingField", InstanceBindingFlags);

	private static readonly FieldInfo OrbFogHandlerOriginsField = typeof(OrbFogHandler).GetField("origins", InstanceBindingFlags);

	private static readonly FieldInfo OrbFogHandlerSphereField = typeof(OrbFogHandler).GetField("sphere", InstanceBindingFlags);

	private static readonly int StatusTypeCount = Enum.GetNames(typeof(STATUSTYPE)).Length;

	private static readonly string[] DayNightTimeMemberCandidates = new string[5] { "currentTime", "time", "timeOfDay", "timeNormalized", "cycleTime" };

	private static readonly Color CampfireLocatorLineColor = new Color(1f, 1f, 1f, 0.95f);

	private static readonly Color CampfireLocatorDotColor = new Color(1f, 0.2f, 0.2f, 1f);

	private static int _localFogStatusSuppressionDepth;

	private Harmony _harmony;

	private OrbFogHandler _orbFogHandler;

	private FogSphere _fogSphere;

	private Fog _legacyFog;

	private bool _fogStateInitialized;

	private bool _initialDelayCompleted;

	private int _trackedFogOriginId = -1;

	private float _fogDelayTimer;

	private float _fogHiddenBufferTimer;

	private float _fogHandlerSearchTimer;

	private float _lastFogStateSyncTime = -0.18f;

	private float _lastRemoteStatusSyncTime = -0.25f;

	private float _lastCompassGrantSyncTime = -0.75f;

	private bool _lastHadFogAuthority;

	private bool _lastModEnabledState;

	private bool _lastFogUiEnabledState;

	private bool _lastCampfireLocatorUiEnabledState;

	private bool _lastDetectedChineseLanguage;

	private bool _isRefreshingLanguage;

	private bool _pendingConfigFileLocalizationRefresh;

	private bool _pendingConfigFileLocalizationSave;

	private float _lastFogUiX;

	private float _lastFogUiY;

	private float _lastFogUiScale;

	private bool _hasAdvertisedInstallState;

	private bool _lastAdvertisedInstallState;

	private float _hiddenNightTestHoldTimer;

	private bool _hiddenNightTestTriggeredThisHold;

	private bool _fogPaused;

	private readonly HashSet<int> _grantedCampfireCompassIds = new HashSet<int>();

	private readonly HashSet<int> _restoredCheckpointCampfireIds = new HashSet<int>();

	private readonly Dictionary<int, int> _playerCompassGrantCounts = new Dictionary<int, int>();

	private readonly Dictionary<int, float> _remoteFogSuppressionDebt = new Dictionary<int, float>();

	private readonly Dictionary<int, float> _remotePlayerFirstSeenTimes = new Dictionary<int, float>();

	private readonly Dictionary<int, int> _remotePlayerCompassBaselineCounts = new Dictionary<int, int>();

	private readonly Dictionary<int, float> _pendingCampfireCompassGrantTimes = new Dictionary<int, float>();

	private Item _compassItem;

	private RectTransform _fogUiRect;

	private TextMeshProUGUI _fogUiText;

	private string _lastFogUiRenderedText = string.Empty;

	private RectTransform _fogUiEntriesRect;

	private readonly List<FogUiEntryView> _fogUiEntryViews = new List<FogUiEntryView>();

	private readonly List<FogUiDisplayEntry> _fogUiDisplayEntries = new List<FogUiDisplayEntry>(8);

	private readonly Dictionary<FogUiIconKind, Sprite> _fogUiIconSprites = new Dictionary<FogUiIconKind, Sprite>();

	private Sprite _campfireLocatorDotSprite;

	private RectTransform _campfireLocatorUiRect;

	private RectTransform _campfireLocatorDotRect;

	private float _campfireLocatorCurrentDotX;

	private RectTransform _compassLobbyNoticeRect;

	private TextMeshProUGUI _compassLobbyNoticeText;

	private string _lastCompassLobbyNoticeText = string.Empty;

	private bool _initialCompassGranted;

	private int _totalCompassGrantCount;

	private bool _hasSyncedFogStateSnapshot;

	private int _lastSyncedFogOriginId = -1;

	private float _lastSyncedFogSize = float.NaN;

	private bool _lastSyncedFogIsMoving;

	private bool _lastSyncedFogHasArrived;

	private int _delayedFogOriginId = -1;

	private int _pendingSyntheticFogSegmentId = -1;

	private int _activeSyntheticFogSegmentId = -1;

	private Vector3 _syntheticFogPoint;

	private float _syntheticFogStartSize;

	private int _fogEtaTrackedOriginId = -1;

	private float _fogEtaLastObservedSize = float.NaN;

	private float _fogEtaLastObservedTime = -1f;

	private float _fogEtaEstimatedUnitsPerSecond;

	private bool _fogEtaHasReliableRate;

	private float _fogDistanceEtaLastRefreshTime = -1f;

	private bool _fogDistanceEtaHasSnapshot;

	private bool _fogDistanceEtaHasEta;

	private float _fogDistanceEtaRemainingDistance;

	private float _fogDistanceEtaSeconds;

	internal static Plugin Instance { get; private set; }

	internal static ConfigEntry<bool> ModEnabled { get; private set; }

	internal static ConfigEntry<bool> FogColdSuppression { get; private set; }

	internal static ConfigEntry<bool> NightColdEnabled { get; private set; }

	internal static ConfigEntry<float> FogSpeed { get; private set; }

	internal static ConfigEntry<float> FogDelay { get; private set; }

	internal static ConfigEntry<bool> CompassEnabled { get; private set; }

	internal static ConfigEntry<KeyCode> CompassHotkey { get; private set; }

	internal static ConfigEntry<KeyCode> FogPauseHotkey { get; private set; }

	internal static ConfigEntry<bool> FogUiEnabled { get; private set; }

	internal static ConfigEntry<bool> CampfireLocatorUiEnabled { get; private set; }

	internal static ConfigEntry<float> FogUiX { get; private set; }

	internal static ConfigEntry<float> FogUiY { get; private set; }

	internal static ConfigEntry<float> FogUiScale { get; private set; }

	private void Awake()
	{
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		Instance = this;
		_lastHadFogAuthority = HasFogAuthority();
		TryCleanupGeneratedBackupFile();
		TryCleanupLegacyPluginFile();
		EnsurePreferredConfigFile();
		_lastDetectedChineseLanguage = DetectChineseLanguage();
		InitializeConfig(_lastDetectedChineseLanguage);
		RegisterConfigChangeHandlers();
		_lastModEnabledState = IsModFeatureEnabled();
		_lastFogUiEnabledState = FogUiEnabled?.Value ?? true;
		_lastCampfireLocatorUiEnabledState = CampfireLocatorUiEnabled?.Value ?? true;
		_lastFogUiX = FogUiX?.Value ?? 60f;
		_lastFogUiY = FogUiY?.Value ?? 0f;
		_lastFogUiScale = FogUiScale?.Value ?? 0.9f;
		MarkConfigFileLocalizationDirty(saveConfigFile: true);
		_harmony = new Harmony("com.github.Thanks.FogClimb");
		_harmony.PatchAll(Assembly.GetExecutingAssembly());
		SceneManager.sceneLoaded += OnSceneLoaded;
		((BaseUnityPlugin)this).Logger.LogInfo((object)"[Fog&ColdControl] Loaded.");
	}

	private void OnDestroy()
	{
		UnregisterConfigChangeHandlers();
		SceneManager.sceneLoaded -= OnSceneLoaded;
		Harmony harmony = _harmony;
		if (harmony != null)
		{
			harmony.UnpatchSelf();
		}
		CleanupFogUi();
		CleanupCampfireLocatorUi();
		CleanupCompassLobbyNotice();
		if ((Object)(object)Instance == (Object)(object)this)
		{
			Instance = null;
		}
	}

	private void InitializeConfig(bool isChineseLanguage)
	{
		ModEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.ModEnabled), GetConfigKeyName(ConfigKey.ModEnabled), true, CreateConfigDescription(ConfigKey.ModEnabled, isChineseLanguage));
		FogColdSuppression = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.FogColdSuppression), GetConfigKeyName(ConfigKey.FogColdSuppression), false, CreateConfigDescription(ConfigKey.FogColdSuppression, isChineseLanguage));
		NightColdEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.NightColdEnabled), GetConfigKeyName(ConfigKey.NightColdEnabled), true, CreateConfigDescription(ConfigKey.NightColdEnabled, isChineseLanguage));
		FogSpeed = ((BaseUnityPlugin)this).Config.Bind<float>(GetConfigSectionName(ConfigKey.FogSpeed), GetConfigKeyName(ConfigKey.FogSpeed), 0.4f, CreateConfigDescription(ConfigKey.FogSpeed, isChineseLanguage));
		FogDelay = ((BaseUnityPlugin)this).Config.Bind<float>(GetConfigSectionName(ConfigKey.FogDelay), GetConfigKeyName(ConfigKey.FogDelay), 900f, CreateConfigDescription(ConfigKey.FogDelay, isChineseLanguage));
		CompassEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.CompassEnabled), GetConfigKeyName(ConfigKey.CompassEnabled), false, CreateConfigDescription(ConfigKey.CompassEnabled, isChineseLanguage));
		CompassHotkey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(GetConfigSectionName(ConfigKey.CompassHotkey), GetConfigKeyName(ConfigKey.CompassHotkey), (KeyCode)103, CreateConfigDescription(ConfigKey.CompassHotkey, isChineseLanguage));
		FogPauseHotkey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(GetConfigSectionName(ConfigKey.FogPauseHotkey), GetConfigKeyName(ConfigKey.FogPauseHotkey), (KeyCode)121, CreateConfigDescription(ConfigKey.FogPauseHotkey, isChineseLanguage));
		FogUiEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.FogUiEnabled), GetConfigKeyName(ConfigKey.FogUiEnabled), true, CreateConfigDescription(ConfigKey.FogUiEnabled, isChineseLanguage));
		CampfireLocatorUiEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(GetConfigSectionName(ConfigKey.CampfireLocatorUiEnabled), GetConfigKeyName(ConfigKey.CampfireLocatorUiEnabled), true, CreateConfigDescription(ConfigKey.CampfireLocatorUiEnabled, isChineseLanguage));
		FogUiX = ((BaseUnityPlugin)this).Config.Bind<float>(GetConfigSectionName(ConfigKey.FogUiX), GetConfigKeyName(ConfigKey.FogUiX), 60f, CreateConfigDescription(ConfigKey.FogUiX, isChineseLanguage));
		FogUiY = ((BaseUnityPlugin)this).Config.Bind<float>(GetConfigSectionName(ConfigKey.FogUiY), GetConfigKeyName(ConfigKey.FogUiY), 0f, CreateConfigDescription(ConfigKey.FogUiY, isChineseLanguage));
		FogUiScale = ((BaseUnityPlugin)this).Config.Bind<float>(GetConfigSectionName(ConfigKey.FogUiScale), GetConfigKeyName(ConfigKey.FogUiScale), 0.9f, CreateConfigDescription(ConfigKey.FogUiScale, isChineseLanguage));
		MigrateLocalizedConfigEntries();
		ClampConfigValues();
	}

	private void EnsurePreferredConfigFile()
	{
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Expected O, but got Unknown
		try
		{
			string configDirectory = Paths.ConfigPath;
			if (!string.IsNullOrWhiteSpace(configDirectory))
			{
				Directory.CreateDirectory(configDirectory);
				string text = Path.Combine(configDirectory, "Thanks.Fog&ColdControl.cfg");
				string text2 = LegacyConfigFileNames.Select((string fileName) => Path.Combine(configDirectory, fileName)).Concat(new string[1] { Path.Combine(configDirectory, "com.github.Thanks.FogClimb.cfg") }).Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray()
					.FirstOrDefault(File.Exists);
				if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(text, text2, StringComparison.OrdinalIgnoreCase) && !File.Exists(text))
				{
					File.Move(text2, text);
					((BaseUnityPlugin)this).Logger.LogInfo((object)"[Fog&ColdControl] Migrated config file to Thanks.Fog&ColdControl.cfg.");
				}
				if (!(BasePluginConfigBackingField == null) && (((BaseUnityPlugin)this).Config == null || !string.Equals(((BaseUnityPlugin)this).Config.ConfigFilePath, text, StringComparison.OrdinalIgnoreCase)))
				{
					ConfigFile value = new ConfigFile(text, true);
					BasePluginConfigBackingField.SetValue(this, value);
				}
			}
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to switch config path to Thanks.Fog&ColdControl.cfg: " + ex.Message));
		}
	}

	private ConfigDescription CreateConfigDescription(ConfigKey configKey, bool isChineseLanguage)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected O, but got Unknown
		return (ConfigDescription)(configKey switch
		{
			ConfigKey.FogSpeed => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.3f, 20f), Array.Empty<object>()), 
			ConfigKey.FogDelay => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)(object)new AcceptableValueRange<float>(20f, 1000f), Array.Empty<object>()), 
			ConfigKey.FogUiX => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)(object)new AcceptableValueRange<float>(-400f, 400f), Array.Empty<object>()), 
			ConfigKey.FogUiY => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)(object)new AcceptableValueRange<float>(-500f, 500f), Array.Empty<object>()), 
			ConfigKey.FogUiScale => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.5f, 2.5f), Array.Empty<object>()), 
			_ => (object)new ConfigDescription(GetLocalizedDescription(configKey, isChineseLanguage), (AcceptableValueBase)null, Array.Empty<object>()), 
		});
	}

	private void MigrateLocalizedConfigEntries()
	{
		IDictionary orphanedEntries = GetOrphanedEntries(((BaseUnityPlugin)this).Config);
		if (orphanedEntries != null && orphanedEntries.Count != 0 && (0u | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)ModEnabled, ConfigKey.ModEnabled, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogColdSuppression, ConfigKey.FogColdSuppression, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)NightColdEnabled, ConfigKey.NightColdEnabled, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogSpeed, ConfigKey.FogSpeed, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogDelay, ConfigKey.FogDelay, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)CompassEnabled, ConfigKey.CompassEnabled, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)CompassHotkey, ConfigKey.CompassHotkey, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogPauseHotkey, ConfigKey.FogPauseHotkey, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogUiEnabled, ConfigKey.FogUiEnabled, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)CampfireLocatorUiEnabled, ConfigKey.CampfireLocatorUiEnabled, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogUiX, ConfigKey.FogUiX, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogUiY, ConfigKey.FogUiY, orphanedEntries) ? 1u : 0u) | (TryMigrateLocalizedConfigValue((ConfigEntryBase)(object)FogUiScale, ConfigKey.FogUiScale, orphanedEntries) ? 1u : 0u)) != 0)
		{
			((BaseUnityPlugin)this).Config.Save();
		}
	}

	private static bool TryMigrateLocalizedConfigValue(ConfigEntryBase entry, ConfigKey configKey, IDictionary orphanedEntries)
	{
		if (((entry != null) ? entry.Definition : null) == (ConfigDefinition)null || orphanedEntries == null)
		{
			return false;
		}
		bool flag = false;
		foreach (ConfigDefinition aliasDefinition in GetAliasDefinitions(configKey))
		{
			if (DefinitionsEqual(aliasDefinition, entry.Definition) || !orphanedEntries.Contains(aliasDefinition))
			{
				continue;
			}
			if (!flag)
			{
				object obj = orphanedEntries[aliasDefinition];
				if (obj != null)
				{
					entry.SetSerializedValue(obj.ToString());
				}
				flag = true;
			}
			orphanedEntries.Remove(aliasDefinition);
		}
		return flag;
	}

	private static IEnumerable<ConfigDefinition> GetAliasDefinitions(ConfigKey configKey)
	{
		string canonicalKey = GetConfigKeyName(configKey);
		string chineseKey = GetKeyName(configKey, isChineseLanguage: true);
		string[] array = new string[4]
		{
			GetConfigSectionName(configKey),
			GetSectionName(configKey, isChineseLanguage: true),
			GetLegacyConfigSectionName(),
			GetLegacySectionName(isChineseLanguage: true)
		}.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
		string[] array2 = array;
		foreach (string section in array2)
		{
			yield return new ConfigDefinition(section, canonicalKey);
			yield return new ConfigDefinition(section, chineseKey);
		}
	}

	private static IDictionary GetOrphanedEntries(ConfigFile configFile)
	{
		return ((object)configFile)?.GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(configFile) as IDictionary;
	}

	private static bool DefinitionsEqual(ConfigDefinition left, ConfigDefinition right)
	{
		if (string.Equals((left != null) ? left.Section : null, (right != null) ? right.Section : null, StringComparison.Ordinal))
		{
			return string.Equals((left != null) ? left.Key : null, (right != null) ? right.Key : null, StringComparison.Ordinal);
		}
		return false;
	}

	private static void ClampConfigValues()
	{
		if (FogSpeed != null)
		{
			FogSpeed.Value = Mathf.Clamp(FogSpeed.Value, 0.3f, 20f);
		}
		if (FogDelay != null)
		{
			FogDelay.Value = Mathf.Clamp(FogDelay.Value, 20f, 1000f);
		}
		if (FogUiX != null)
		{
			FogUiX.Value = Mathf.Clamp(FogUiX.Value, -400f, 400f);
		}
		if (FogUiY != null)
		{
			FogUiY.Value = Mathf.Clamp(FogUiY.Value, -500f, 500f);
		}
		if (FogUiScale != null)
		{
			FogUiScale.Value = Mathf.Clamp(FogUiScale.Value, 0.5f, 2.5f);
		}
		NormalizeShootZombiesFogUiDefaults();
	}

	private static void NormalizeShootZombiesFogUiDefaults()
	{
		if (FogUiX != null && FogUiY != null && FogUiScale != null)
		{
			bool num = Approximately(FogUiX.Value, 70f) && Approximately(FogUiY.Value, 4f) && Approximately(FogUiScale.Value, 1.1f);
			bool flag = Approximately(FogUiX.Value, 10f) && Approximately(FogUiY.Value, 10f) && Approximately(FogUiScale.Value, 1.2f);
			bool flag2 = Approximately(FogUiX.Value, 4f) && Approximately(FogUiY.Value, 4f) && Approximately(FogUiScale.Value, 1.1f);
			bool flag3 = Approximately(FogUiX.Value, 60f) && Approximately(FogUiY.Value, -200f) && Approximately(FogUiScale.Value, 1.1f);
			bool flag4 = Approximately(FogUiX.Value, 60f) && Approximately(FogUiY.Value, -200f) && Approximately(FogUiScale.Value, 1f);
			bool flag5 = Approximately(FogUiX.Value, 60f) && Approximately(FogUiY.Value, 16f) && Approximately(FogUiScale.Value, 1.2f);
			bool flag6 = Approximately(FogUiX.Value, 60f) && Approximately(FogUiY.Value, 0f) && Approximately(FogUiScale.Value, 1.2f);
			if (num || flag || flag2 || flag3 || flag4 || flag5 || flag6)
			{
				FogUiX.Value = 60f;
				FogUiY.Value = 0f;
				FogUiScale.Value = 0.9f;
			}
		}
	}

	private static bool Approximately(float left, float right)
	{
		return Mathf.Abs(left - right) < 0.001f;
	}

	private void RegisterConfigChangeHandlers()
	{
		if (((BaseUnityPlugin)this).Config != null)
		{
			((BaseUnityPlugin)this).Config.SettingChanged -= OnConfigSettingChanged;
			((BaseUnityPlugin)this).Config.SettingChanged += OnConfigSettingChanged;
		}
	}

	private void UnregisterConfigChangeHandlers()
	{
		if (((BaseUnityPlugin)this).Config != null)
		{
			((BaseUnityPlugin)this).Config.SettingChanged -= OnConfigSettingChanged;
		}
	}

	private void OnConfigSettingChanged(object sender, SettingChangedEventArgs e)
	{
		MarkConfigFileLocalizationDirty(saveConfigFile: false);
	}

	private void MarkConfigFileLocalizationDirty(bool saveConfigFile)
	{
		_pendingConfigFileLocalizationRefresh = true;
		_pendingConfigFileLocalizationSave |= saveConfigFile;
	}

	private void HandlePendingConfigFileLocalizationRefresh()
	{
		if (_pendingConfigFileLocalizationRefresh && !_isRefreshingLanguage)
		{
			bool lastDetectedChineseLanguage = _lastDetectedChineseLanguage;
			bool pendingConfigFileLocalizationSave = _pendingConfigFileLocalizationSave;
			_pendingConfigFileLocalizationRefresh = false;
			_pendingConfigFileLocalizationSave = false;
			TryRefreshLocalizedConfigFile(lastDetectedChineseLanguage, pendingConfigFileLocalizationSave);
		}
	}

	private void Update()
	{
		bool flag = IsModFeatureEnabled();
		HandleAuthorityChangeIfNeeded(flag);
		if (flag != _lastModEnabledState)
		{
			_lastModEnabledState = flag;
			if (!flag)
			{
				RestoreVanillaFogSpeed();
				ResetFogRuntimeState();
				SetFogUiVisible(visible: false);
			}
			else
			{
				_fogStateInitialized = false;
				_lastFogStateSyncTime = -0.18f;
				_lastRemoteStatusSyncTime = -0.25f;
				_lastCompassGrantSyncTime = -0.75f;
			}
		}
		TryResolveRuntimeObjects();
		if (flag)
		{
			EnsureFogCoverageAcrossAllLevels();
		}
		UpdateLocalInstallStateAdvertisement();
		HandleLanguageChangeIfNeeded();
		HandlePendingConfigFileLocalizationRefresh();
		HandleFogUiConfigChanges();
		HandleManualCompassHotkey();
		HandleFogPauseHotkey();
		HandleHiddenNightTestHotkey();
		RefreshRemotePlayerJoinGraceState();
		ProcessPendingCampfireCompassGrants();
		if (!flag)
		{
			UpdateFogUi();
			UpdateCampfireLocatorUi();
			UpdateCompassLobbyNotice();
			return;
		}
		if ((Object)(object)_orbFogHandler != (Object)null && !_fogStateInitialized)
		{
			InitializeFogRuntimeState(_orbFogHandler);
		}
		if ((Object)(object)_orbFogHandler != (Object)null && HasFogAuthority())
		{
			TryRestoreCheckpointCampfireDelay();
			UpdateAuthorityFogMode();
			SyncFogStateToGuestsIfNeeded();
			SyncRemoteStatusSuppressionIfNeeded();
			SyncCompassGrantsToPlayersIfNeeded();
		}
		UpdateFogArrivalEstimate();
		UpdateFogUi();
		UpdateCampfireLocatorUi();
		UpdateCompassLobbyNotice();
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		ResetFogRuntimeState();
		_orbFogHandler = null;
		_fogSphere = null;
		_legacyFog = null;
		_compassItem = null;
		CleanupFogUi();
		CleanupCampfireLocatorUi();
		CleanupCompassLobbyNotice();
	}

	private void TryResolveRuntimeObjects()
	{
		bool flag = (Object)(object)_orbFogHandler == (Object)null;
		bool flag2 = (Object)(object)_fogSphere == (Object)null;
		bool flag3 = (Object)(object)_legacyFog == (Object)null;
		if (!flag && !flag2 && !flag3)
		{
			return;
		}
		_fogHandlerSearchTimer -= Time.unscaledDeltaTime;
		if (_fogHandlerSearchTimer > 0f)
		{
			return;
		}
		_fogHandlerSearchTimer = 1f;
		if (flag)
		{
			_orbFogHandler = Object.FindAnyObjectByType<OrbFogHandler>();
			if ((Object)(object)_orbFogHandler != (Object)null)
			{
				_fogStateInitialized = false;
			}
		}
		if (flag2)
		{
			_fogSphere = Object.FindAnyObjectByType<FogSphere>();
		}
		if (flag3)
		{
			_legacyFog = Object.FindAnyObjectByType<Fog>();
		}
	}

	private void EnsureFogCoverageAcrossAllLevels()
	{
		EnsureFogCoverage(_orbFogHandler);
	}

	private void EnsureFogCoverage(OrbFogHandler fogHandler)
	{
		if (!ShouldForceFogCoverageEverywhere() || (Object)(object)fogHandler == (Object)null)
		{
			return;
		}
		FogSphereOrigin[] array = ResolveFogOrigins(fogHandler);
		foreach (FogSphereOrigin val in array)
		{
			if (!((Object)(object)val == (Object)null) && val.disableFog)
			{
				val.disableFog = false;
			}
		}
		FogSphere val2 = ResolveFogSphere(fogHandler);
		if (!((Object)(object)val2 == (Object)null))
		{
			_fogSphere = val2;
			if (!((Component)val2).gameObject.activeSelf)
			{
				((Component)val2).gameObject.SetActive(true);
			}
			if (fogHandler.currentSize > 0f && val2.currentSize <= 0f)
			{
				val2.currentSize = fogHandler.currentSize;
			}
		}
	}

	private static FogSphereOrigin[] ResolveFogOrigins(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler == (Object)null)
		{
			return Array.Empty<FogSphereOrigin>();
		}
		if (OrbFogHandlerOriginsField?.GetValue(fogHandler) is FogSphereOrigin[] array && array.Length != 0)
		{
			return array;
		}
		FogSphereOrigin[] componentsInChildren = ((Component)fogHandler).GetComponentsInChildren<FogSphereOrigin>(true);
		if (componentsInChildren != null && componentsInChildren.Length != 0)
		{
			try
			{
				OrbFogHandlerOriginsField?.SetValue(fogHandler, componentsInChildren);
			}
			catch
			{
			}
			return componentsInChildren;
		}
		return Array.Empty<FogSphereOrigin>();
	}

	private static FogSphere ResolveFogSphere(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler == (Object)null)
		{
			return null;
		}
		object obj = OrbFogHandlerSphereField?.GetValue(fogHandler);
		FogSphere val = (FogSphere)((obj is FogSphere) ? obj : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		val = ((Component)fogHandler).GetComponentInChildren<FogSphere>(true);
		if ((Object)(object)val != (Object)null)
		{
			try
			{
				OrbFogHandlerSphereField?.SetValue(fogHandler, val);
			}
			catch
			{
			}
		}
		return val;
	}

	private void InitializeFogRuntimeState(OrbFogHandler fogHandler)
	{
		_fogStateInitialized = true;
		_trackedFogOriginId = (((Object)(object)fogHandler != (Object)null) ? fogHandler.currentID : (-1));
		_initialDelayCompleted = ShouldSkipInitialDelay(fogHandler);
		_fogDelayTimer = ((!_initialDelayCompleted) ? 0f : (FogDelay?.Value ?? 0f));
		_fogHiddenBufferTimer = (_initialDelayCompleted ? 10f : 0f);
		ResetFogArrivalEstimate();
	}

	private static bool HasAnyConfiguredFogDelay()
	{
		return true;
	}

	private static bool TryGetVanillaProgressStartThresholds(OrbFogHandler fogHandler, out float requiredHeight, out float requiredForward)
	{
		requiredHeight = 0f;
		requiredForward = 0f;
		if ((Object)(object)fogHandler == (Object)null)
		{
			return false;
		}
		requiredHeight = fogHandler.currentStartHeight;
		requiredForward = fogHandler.currentStartForward;
		if (!float.IsNaN(requiredHeight) && !float.IsInfinity(requiredHeight) && !float.IsNaN(requiredForward))
		{
			return !float.IsInfinity(requiredForward);
		}
		return false;
	}

	private static bool TryGetVanillaProgressStartProgress(OrbFogHandler fogHandler, out int passedCount, out int totalCount, out float requiredHeight, out float requiredForward)
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		passedCount = 0;
		totalCount = 0;
		requiredHeight = 0f;
		requiredForward = 0f;
		if (Ascents.currentAscent < 0 || !TryGetVanillaProgressStartThresholds(fogHandler, out requiredHeight, out requiredForward))
		{
			return false;
		}
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if (!((Object)(object)allCharacter?.data == (Object)null) && !allCharacter.data.dead)
			{
				totalCount++;
				if (allCharacter.Center.y >= requiredHeight && allCharacter.Center.z >= requiredForward)
				{
					passedCount++;
				}
			}
		}
		return totalCount > 0;
	}

	private bool ShouldSkipInitialDelay(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler == (Object)null || !HasAnyConfiguredFogDelay())
		{
			return true;
		}
		if (IsCampfireDelayPendingForOrigin(fogHandler.currentID))
		{
			return false;
		}
		if (fogHandler.currentID <= 0)
		{
			if (!fogHandler.isMoving && !(fogHandler.currentWaitTime > 1f))
			{
				return fogHandler.hasArrived;
			}
			return true;
		}
		if (ShouldHoldFogUntilCampfireActivation(fogHandler))
		{
			return false;
		}
		if (!fogHandler.isMoving && !(fogHandler.currentWaitTime > 1f))
		{
			return fogHandler.hasArrived;
		}
		return true;
	}

	private void UpdateAuthorityFogMode()
	{
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return;
		}
		ClampConfigValues();
		if (IsFogRemovedInCurrentScene())
		{
			ClearSyntheticFogStage();
			_pendingSyntheticFogSegmentId = -1;
			_delayedFogOriginId = -1;
			ApplyRemovedFogState();
			return;
		}
		TryAlignFogOriginToCurrentSegment();
		TryUpdateSyntheticFogStage();
		UpdateTrackedFogOrigin();
		if (ShouldAutoCompleteDelayForCurrentOrigin(_orbFogHandler))
		{
			_initialDelayCompleted = true;
		}
		if (!_initialDelayCompleted)
		{
			_orbFogHandler.speed = 0f;
			if (!ShouldEnforceConfiguredDelay(_orbFogHandler) || TryStartFogFromVanillaProgressTrigger(_orbFogHandler))
			{
				return;
			}
			if (_fogHiddenBufferTimer < 10f)
			{
				_fogHiddenBufferTimer = Mathf.Min(_fogHiddenBufferTimer + Time.unscaledDeltaTime, 10f);
				if (_fogHiddenBufferTimer < 10f)
				{
					return;
				}
			}
			float num = FogDelay?.Value ?? 0f;
			if (num <= 0f)
			{
				_initialDelayCompleted = true;
				ClearPendingCampfireDelayForOrigin(_orbFogHandler.currentID);
				if (!_orbFogHandler.isMoving)
				{
					StartFogMovement();
				}
				return;
			}
			_fogDelayTimer += Time.unscaledDeltaTime;
			if (_fogDelayTimer >= num)
			{
				_fogDelayTimer = num;
				_initialDelayCompleted = true;
				ClearPendingCampfireDelayForOrigin(_orbFogHandler.currentID);
				if (!_orbFogHandler.isMoving)
				{
					StartFogMovement();
				}
			}
		}
		else if (_fogPaused)
		{
			ApplyPausedFogState(syncImmediately: false);
		}
		else
		{
			_orbFogHandler.speed = FogSpeed.Value;
			TryAutoStartCustomRunFogIfNeeded();
			TryAutoResumeStalledFogMovement();
			TryGrantInitialCompassIfNeeded();
		}
	}

	private bool TryStartFogFromVanillaProgressTrigger(OrbFogHandler fogHandler)
	{
		if (!TryGetVanillaProgressStartProgress(fogHandler, out var passedCount, out var totalCount, out var requiredHeight, out var requiredForward))
		{
			return false;
		}
		if (passedCount < totalCount)
		{
			return false;
		}
		float num = Mathf.Max(10f - _fogHiddenBufferTimer, 0f);
		float num2 = Mathf.Max(FogDelay?.Value ?? 900f, 0f);
		float num3 = Mathf.Max(num2 - _fogDelayTimer, 0f);
		_initialDelayCompleted = true;
		_fogHiddenBufferTimer = 10f;
		_fogDelayTimer = num2;
		((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Starting fog early from vanilla progress trigger at origin {1}. progress={2}/{3}, thresholdY={4:F1}, thresholdZ={5:F1}, remainingBuffer={6:F1}s, remainingDelay={7:F1}s.", "Fog&ColdControl", fogHandler.currentID, passedCount, totalCount, requiredHeight, requiredForward, num, num3));
		StartFogMovement();
		return true;
	}

	private bool IsCampfireDelayPendingForOrigin(int originId)
	{
		if (originId >= 0)
		{
			return _delayedFogOriginId == originId;
		}
		return false;
	}

	private bool ShouldAutoCompleteDelayForCurrentOrigin(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler == (Object)null)
		{
			return true;
		}
		if (fogHandler.currentID == 0)
		{
			return false;
		}
		if (ShouldHoldFogUntilCampfireActivation(fogHandler))
		{
			return false;
		}
		return !IsCampfireDelayPendingForOrigin(fogHandler.currentID);
	}

	private bool ShouldEnforceConfiguredDelay(OrbFogHandler fogHandler)
	{
		if (_initialDelayCompleted || (Object)(object)fogHandler == (Object)null || !HasAnyConfiguredFogDelay())
		{
			return false;
		}
		if (fogHandler.currentID != 0)
		{
			return IsCampfireDelayPendingForOrigin(fogHandler.currentID);
		}
		return true;
	}

	private bool ShouldHoldFogUntilCampfireActivation(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler != (Object)null && fogHandler.currentID > 0 && !IsCampfireDelayPendingForOrigin(fogHandler.currentID) && !fogHandler.isMoving && !fogHandler.hasArrived)
		{
			return !_initialDelayCompleted;
		}
		return false;
	}

	private bool HasVisibleFogDelayCountdown()
	{
		return _fogHiddenBufferTimer >= 10f;
	}

	private void ClearPendingCampfireDelayForOrigin(int originId)
	{
		if (_delayedFogOriginId == originId)
		{
			_delayedFogOriginId = -1;
		}
	}

	private int GetAvailableFogOriginCount()
	{
		if (!((Object)(object)_orbFogHandler == (Object)null))
		{
			return ResolveFogOrigins(_orbFogHandler).Length;
		}
		return 0;
	}

	private bool TryResolveFogPointForCurrentOrigin(out Vector3 fogPoint, out string pointDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		pointDescription = string.Empty;
		if ((Object)(object)_fogSphere != (Object)null)
		{
			fogPoint = _fogSphere.fogPoint;
			pointDescription = "fogSphere";
			return true;
		}
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return false;
		}
		FogSphereOrigin[] array = ResolveFogOrigins(_orbFogHandler);
		if (array.Length == 0)
		{
			return false;
		}
		int num = Mathf.Clamp(_orbFogHandler.currentID, 0, array.Length - 1);
		FogSphereOrigin val = array[num] ?? array.LastOrDefault((FogSphereOrigin candidate) => (Object)(object)candidate != (Object)null);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		fogPoint = ((Component)val).transform.position;
		pointDescription = $"origin-{num}";
		return true;
	}

	private bool TryGetPreviousRealFogOriginSize(out float previousOriginSize)
	{
		previousOriginSize = 900f;
		FogSphereOrigin[] array = ResolveFogOrigins(_orbFogHandler);
		if (array.Length == 0)
		{
			return false;
		}
		int num = Mathf.Clamp(array.Length - 2, 0, array.Length - 1);
		FogSphereOrigin val = array[num];
		if ((Object)(object)val == (Object)null || val.size <= 0f)
		{
			return false;
		}
		previousOriginSize = val.size;
		return true;
	}

	private bool TryResolveFogStageCoverageAnchor(Segment fogStageSegment, out Vector3 stageAnchor, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Invalid comparison between Unknown and I4
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Invalid comparison between Unknown and I4
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected I4, but got Unknown
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_021b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		stageAnchor = Vector3.zero;
		anchorDescription = string.Empty;
		MapHandler instance = Singleton<MapHandler>.Instance;
		if ((int)fogStageSegment >= 5 && (Object)(object)instance != (Object)null && (Object)(object)instance.respawnThePeak != (Object)null)
		{
			stageAnchor = instance.respawnThePeak.position;
			anchorDescription = "respawnThePeak";
			return true;
		}
		if ((int)fogStageSegment >= 4 && (Object)(object)instance != (Object)null && (Object)(object)instance.respawnTheKiln != (Object)null)
		{
			stageAnchor = instance.respawnTheKiln.position;
			anchorDescription = "respawnTheKiln";
			return true;
		}
		if ((Object)(object)instance != (Object)null && instance.segments != null && instance.segments.Length != 0)
		{
			int num = Mathf.Clamp((int)fogStageSegment, 0, instance.segments.Length - 1);
			MapSegment val = instance.segments[num];
			if (val != null)
			{
				if ((Object)(object)val.segmentCampfire != (Object)null)
				{
					Campfire componentInChildren = val.segmentCampfire.GetComponentInChildren<Campfire>(true);
					if ((Object)(object)componentInChildren != (Object)null)
					{
						stageAnchor = ((Component)componentInChildren).transform.position;
						anchorDescription = $"{fogStageSegment} campfire";
						return true;
					}
					stageAnchor = val.segmentCampfire.transform.position;
					anchorDescription = $"{fogStageSegment} segmentCampfire";
					return true;
				}
				if ((Object)(object)val.reconnectSpawnPos != (Object)null)
				{
					stageAnchor = val.reconnectSpawnPos.position;
					anchorDescription = $"{fogStageSegment} reconnectSpawnPos";
					return true;
				}
				if ((Object)(object)val.segmentParent != (Object)null)
				{
					stageAnchor = val.segmentParent.transform.position;
					anchorDescription = $"{fogStageSegment} segmentParent";
					return true;
				}
			}
		}
		if (TryResolveSyntheticTargetAnchor(fogStageSegment, out stageAnchor, out anchorDescription))
		{
			return true;
		}
		Character localCharacter = Character.localCharacter;
		if ((Object)(object)localCharacter != (Object)null)
		{
			stageAnchor = localCharacter.Center;
			anchorDescription = "localCharacter";
			return true;
		}
		Character val2 = Character.AllCharacters.FirstOrDefault((Character character) => (Object)(object)character != (Object)null && (Object)(object)character.data != (Object)null && !character.data.dead);
		if ((Object)(object)val2 != (Object)null)
		{
			stageAnchor = val2.Center;
			anchorDescription = "firstAliveCharacter";
			return true;
		}
		return false;
	}

	private float ComputeFogStartSizeForStage(Segment fogStageSegment, Vector3 fogPoint, float baseSize, float minSize, float maxSize, float anchorMargin, float playerMargin)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Max(baseSize, minSize);
		if (TryResolveFogStageCoverageAnchor(fogStageSegment, out var stageAnchor, out var _))
		{
			num = Mathf.Max(num, Vector3.Distance(fogPoint, stageAnchor) + anchorMargin);
		}
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if (!((Object)(object)allCharacter == (Object)null) && !((Object)(object)allCharacter.data == (Object)null) && !allCharacter.data.dead)
			{
				num = Mathf.Max(num, Vector3.Distance(fogPoint, allCharacter.Center) + playerMargin);
			}
		}
		return Mathf.Clamp(num, minSize, maxSize);
	}

	private static bool ShouldUseCustomFogPositionForSegment(Segment segment)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		return (int)segment >= 5;
	}

	private static bool ShouldRemoveFogForSegment(Segment segment)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Invalid comparison between Unknown and I4
		if ((int)segment != 3)
		{
			return (int)segment == 4;
		}
		return true;
	}

	private static bool TryGetCurrentGameplaySegment(out Segment segment)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected I4, but got Unknown
		segment = (Segment)0;
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return false;
		}
		if ((Object)(object)Singleton<MapHandler>.Instance == (Object)null)
		{
			return false;
		}
		segment = (Segment)(int)MapHandler.CurrentSegmentNumber;
		return true;
	}

	private static bool IsFogRemovedInCurrentScene()
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		if (TryGetCurrentGameplaySegment(out var segment))
		{
			return ShouldRemoveFogForSegment(segment);
		}
		return false;
	}

	private void ApplyRemovedFogState()
	{
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return;
		}
		_orbFogHandler.speed = 0f;
		_orbFogHandler.isMoving = false;
		_orbFogHandler.currentWaitTime = 0f;
		_orbFogHandler.hasArrived = false;
		_orbFogHandler.currentStartHeight = float.NegativeInfinity;
		_orbFogHandler.currentStartForward = float.NegativeInfinity;
		_orbFogHandler.currentSize = 0f;
		FogSphere val = _fogSphere ?? ResolveFogSphere(_orbFogHandler);
		if (!((Object)(object)val == (Object)null))
		{
			_fogSphere = val;
			val.currentSize = 0f;
			if (((Component)val).gameObject.activeSelf)
			{
				((Component)val).gameObject.SetActive(false);
			}
		}
	}

	private bool IsLateGameFogColdSuppressionActive()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Invalid comparison between Unknown and I4
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return _activeSyntheticFogSegmentId >= 3;
		}
		if ((Object)(object)Singleton<MapHandler>.Instance != (Object)null && (int)MapHandler.CurrentSegmentNumber >= 3)
		{
			return true;
		}
		return _activeSyntheticFogSegmentId >= 3;
	}

	private void ClearSyntheticFogStage()
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		_pendingSyntheticFogSegmentId = -1;
		_activeSyntheticFogSegmentId = -1;
		_syntheticFogPoint = Vector3.zero;
		_syntheticFogStartSize = 0f;
	}

	private static bool TryMapSegmentToFogOriginId(Segment segment, int availableOriginCount, out int originId)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected I4, but got Unknown
		originId = -1;
		if (availableOriginCount <= 0)
		{
			return false;
		}
		originId = Mathf.Clamp((int)segment, 0, availableOriginCount - 1);
		return true;
	}

	private void TryAlignFogOriginToCurrentSegment()
	{
		if (!((Object)(object)_orbFogHandler == (Object)null) && HasFogAuthority() && !_orbFogHandler.isMoving && !IsFogRemovedInCurrentScene() && TryGetTargetFogOriginId(out var expectedOriginId) && _orbFogHandler.currentID != expectedOriginId)
		{
			int currentID = _orbFogHandler.currentID;
			_orbFogHandler.SetFogOrigin(expectedOriginId);
			EnsureFogCoverage(_orbFogHandler);
			SyncFogOriginToGuests();
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Aligned fog origin from {1} to {2}.", "Fog&ColdControl", currentID, expectedOriginId));
		}
	}

	private void TryUpdateSyntheticFogStage()
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected I4, but got Unknown
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || !HasFogAuthority())
		{
			return;
		}
		MapHandler instance = Singleton<MapHandler>.Instance;
		int availableFogOriginCount = GetAvailableFogOriginCount();
		if ((Object)(object)instance == (Object)null || availableFogOriginCount <= 0)
		{
			return;
		}
		int num = (int)MapHandler.CurrentSegmentNumber;
		if (num < availableFogOriginCount && _pendingSyntheticFogSegmentId < availableFogOriginCount)
		{
			if (_activeSyntheticFogSegmentId >= 0)
			{
				ClearSyntheticFogStage();
			}
			return;
		}
		int num2 = -1;
		if (_pendingSyntheticFogSegmentId >= availableFogOriginCount && num >= _pendingSyntheticFogSegmentId)
		{
			num2 = _pendingSyntheticFogSegmentId;
			_pendingSyntheticFogSegmentId = -1;
		}
		else if (num >= availableFogOriginCount)
		{
			num2 = num;
		}
		else if (_activeSyntheticFogSegmentId >= availableFogOriginCount)
		{
			num2 = _activeSyntheticFogSegmentId;
		}
		if (num2 < availableFogOriginCount)
		{
			return;
		}
		Segment val = (Segment)(byte)num2;
		if (!ShouldUseCustomFogPositionForSegment(val))
		{
			if (_activeSyntheticFogSegmentId >= availableFogOriginCount)
			{
				ClearSyntheticFogStage();
			}
		}
		else
		{
			if (!TryBuildSyntheticFogStage(val, out var fogPoint, out var fogSize, out var anchorDescription))
			{
				return;
			}
			bool num3 = _activeSyntheticFogSegmentId != num2;
			bool flag = num3 || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived || _syntheticFogStartSize <= 0f;
			bool flag2 = num3 || (flag && Vector3.Distance(_syntheticFogPoint, fogPoint) > 0.1f);
			bool flag3 = num3 || (flag && Mathf.Abs(_syntheticFogStartSize - fogSize) > 0.1f);
			_activeSyntheticFogSegmentId = num2;
			if (flag2)
			{
				_syntheticFogPoint = fogPoint;
			}
			if (flag3)
			{
				_syntheticFogStartSize = fogSize;
			}
			bool num4 = flag2 || flag3;
			ApplySyntheticFogStageToHandler(flag3);
			if (num4)
			{
				((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Activated synthetic fog stage {1} using {2}. Point={3}, size={4:F1}, delayCompleted={5}, moving={6}.", "Fog&ColdControl", num2, anchorDescription, fogPoint, fogSize, _initialDelayCompleted, _orbFogHandler.isMoving));
				if (_initialDelayCompleted && !_orbFogHandler.isMoving)
				{
					StartFogMovement();
				}
			}
		}
	}

	private void ApplySyntheticFogStageToHandler(bool resetCurrentSize)
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return;
		}
		FogSphere val = _fogSphere ?? ResolveFogSphere(_orbFogHandler);
		if ((Object)(object)val != (Object)null)
		{
			_fogSphere = val;
			if (!((Component)val).gameObject.activeSelf)
			{
				((Component)val).gameObject.SetActive(true);
			}
			val.fogPoint = _syntheticFogPoint;
			if (resetCurrentSize)
			{
				val.currentSize = _syntheticFogStartSize;
			}
		}
		_orbFogHandler.currentStartHeight = float.NegativeInfinity;
		_orbFogHandler.currentStartForward = float.NegativeInfinity;
		if (resetCurrentSize)
		{
			_orbFogHandler.currentSize = _syntheticFogStartSize;
			_orbFogHandler.currentWaitTime = 0f;
			_orbFogHandler.hasArrived = false;
		}
	}

	private bool TryBuildSyntheticFogStage(Segment syntheticSegment, out Vector3 fogPoint, out float fogSize, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		fogSize = 0f;
		anchorDescription = string.Empty;
		if (!TryResolveSyntheticFogAnchor(syntheticSegment, out fogPoint, out anchorDescription))
		{
			return false;
		}
		fogSize = ComputeSyntheticFogStartSize(syntheticSegment, fogPoint);
		return fogSize > 0f;
	}

	private bool TryBuildPeakVerticalFogStage(Segment syntheticSegment, out Vector3 fogPoint, out float fogSize, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Invalid comparison between Unknown and I4
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		fogSize = 0f;
		anchorDescription = string.Empty;
		if ((int)syntheticSegment < 5)
		{
			return false;
		}
		if (!TryResolvePeakVerticalFogTargetAnchor(syntheticSegment, out var targetAnchor, out var targetDescription))
		{
			return false;
		}
		fogPoint = new Vector3(targetAnchor.x, 1830f, targetAnchor.z);
		fogSize = ComputePeakVerticalFogStartSize(fogPoint, targetAnchor);
		if (fogSize <= 0f)
		{
			return false;
		}
		anchorDescription = $"{targetDescription} vertical-up stop@{1800f:F0}";
		return true;
	}

	private bool TryResolveSyntheticTargetAnchor(Segment syntheticSegment, out Vector3 targetAnchor, out string targetDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Invalid comparison between Unknown and I4
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Invalid comparison between Unknown and I4
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		targetAnchor = Vector3.zero;
		targetDescription = string.Empty;
		MapHandler instance = Singleton<MapHandler>.Instance;
		if ((Object)(object)instance == (Object)null)
		{
			return false;
		}
		if ((int)syntheticSegment >= 5 && (Object)(object)instance.respawnThePeak != (Object)null)
		{
			targetAnchor = instance.respawnThePeak.position;
			targetDescription = "respawnThePeak";
			return true;
		}
		if ((int)syntheticSegment >= 4)
		{
			if (instance.segments != null && instance.segments.Length > 4)
			{
				MapSegment val = instance.segments[4];
				if (val != null && (Object)(object)val.reconnectSpawnPos != (Object)null)
				{
					targetAnchor = val.reconnectSpawnPos.position;
					targetDescription = "TheKiln reconnectSpawnPos";
					return true;
				}
				if (val != null && (Object)(object)val.segmentParent != (Object)null)
				{
					targetAnchor = val.segmentParent.transform.position;
					targetDescription = "TheKiln segmentParent";
					return true;
				}
			}
			if ((Object)(object)instance.respawnTheKiln != (Object)null)
			{
				targetAnchor = instance.respawnTheKiln.position;
				targetDescription = "respawnTheKiln";
				return true;
			}
		}
		return false;
	}

	private bool TryResolveSyntheticFogAnchorFromOrigins(Segment syntheticSegment, out Vector3 fogPoint, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected I4, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		anchorDescription = string.Empty;
		FogSphereOrigin[] array = ResolveFogOrigins(_orbFogHandler);
		FogSphereOrigin val = array.LastOrDefault((FogSphereOrigin origin) => (Object)(object)origin != (Object)null);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		FogSphereOrigin val2 = array.Where((FogSphereOrigin origin) => (Object)(object)origin != (Object)null).Reverse().Skip(1)
			.FirstOrDefault();
		if ((Object)(object)val2 == (Object)null)
		{
			return false;
		}
		Vector3 val3 = ((Component)val).transform.position - ((Component)val2).transform.position;
		if (((Vector3)(ref val3)).sqrMagnitude < 1f)
		{
			return false;
		}
		int num = Mathf.Max(syntheticSegment - (array.Length - 1), 1);
		Vector3 normalized = ((Vector3)(ref val3)).normalized;
		Vector3 val4 = ((Component)val).transform.position + val3 * (float)num;
		if (TryResolveSyntheticTargetAnchor(syntheticSegment, out var targetAnchor, out var targetDescription))
		{
			float num2 = Mathf.Max(Vector3.Dot(targetAnchor - ((Component)val).transform.position, normalized) + 180f, ((Vector3)(ref val3)).magnitude * (float)num);
			val4 = ((Component)val).transform.position + normalized * num2;
			Vector3 val5 = Vector3.ProjectOnPlane(targetAnchor - val4, normalized);
			val4 += val5 * 0.25f;
			anchorDescription = "origin-extrapolated + " + targetDescription;
		}
		else
		{
			anchorDescription = "origin-extrapolated";
		}
		fogPoint = val4;
		return true;
	}

	private bool TryResolveSyntheticFogAnchor(Segment syntheticSegment, out Vector3 fogPoint, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		anchorDescription = string.Empty;
		if (TryResolveSyntheticFogAnchorFromOrigins(syntheticSegment, out fogPoint, out anchorDescription))
		{
			return true;
		}
		if ((Object)(object)Singleton<MapHandler>.Instance == (Object)null)
		{
			return false;
		}
		if (TryResolveSyntheticTargetAnchor(syntheticSegment, out fogPoint, out anchorDescription))
		{
			return true;
		}
		Character localCharacter = Character.localCharacter;
		if ((Object)(object)localCharacter != (Object)null)
		{
			fogPoint = localCharacter.Center;
			anchorDescription = "localCharacter";
			return true;
		}
		Character val = Character.AllCharacters.FirstOrDefault((Character character) => (Object)(object)character != (Object)null);
		if ((Object)(object)val != (Object)null)
		{
			fogPoint = val.Center;
			anchorDescription = "firstCharacter";
			return true;
		}
		return false;
	}

	private bool TryResolvePeakVerticalFogAnchor(Segment syntheticSegment, out Vector3 fogPoint, out string anchorDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Invalid comparison between Unknown and I4
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		anchorDescription = string.Empty;
		if ((int)syntheticSegment < 5)
		{
			return false;
		}
		if (!TryResolvePeakVerticalFogTargetAnchor(syntheticSegment, out var targetAnchor, out var targetDescription))
		{
			return false;
		}
		fogPoint = new Vector3(targetAnchor.x, 1830f, targetAnchor.z);
		anchorDescription = $"{targetDescription} vertical-up stop@{1800f:F0}";
		return true;
	}

	private bool TryResolvePeakVerticalFogTargetAnchor(Segment syntheticSegment, out Vector3 targetAnchor, out string targetDescription)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Invalid comparison between Unknown and I4
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		targetAnchor = Vector3.zero;
		targetDescription = string.Empty;
		if ((int)syntheticSegment < 5)
		{
			return false;
		}
		if (TryResolveSyntheticTargetAnchor(syntheticSegment, out targetAnchor, out targetDescription))
		{
			return true;
		}
		Character localCharacter = Character.localCharacter;
		if ((Object)(object)localCharacter != (Object)null)
		{
			targetAnchor = localCharacter.Center;
			targetDescription = "localCharacter";
			return true;
		}
		Character val = Character.AllCharacters.FirstOrDefault((Character character) => (Object)(object)character != (Object)null);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		targetAnchor = val.Center;
		targetDescription = "firstCharacter";
		return true;
	}

	private float ComputeSyntheticFogStartSize(Segment syntheticSegment, Vector3 fogPoint)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		float previousOriginSize = 900f;
		TryGetPreviousRealFogOriginSize(out previousOriginSize);
		float baseSize = previousOriginSize * 0.72f;
		return ComputeFogStartSizeForStage(syntheticSegment, fogPoint, baseSize, 650f, 1400f, 130f, 95f);
	}

	private float ComputePeakVerticalFogStartSize(Vector3 fogPoint, Vector3 targetAnchor)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		float previousOriginSize = 900f;
		TryGetPreviousRealFogOriginSize(out previousOriginSize);
		float num = Mathf.Max(previousOriginSize * 0.68f, 650f);
		float num2 = Mathf.Min(targetAnchor.y, 1620f);
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if (!((Object)(object)allCharacter == (Object)null) && !((Object)(object)allCharacter.data == (Object)null) && !allCharacter.data.dead)
			{
				num2 = Mathf.Min(num2, allCharacter.Center.y);
				num = Mathf.Max(num, Vector3.Distance(fogPoint, allCharacter.Center) + 70f);
			}
		}
		num = Mathf.Max(num, fogPoint.y - num2 + 120f);
		if (TryResolveFogStageCoverageAnchor((Segment)5, out var stageAnchor, out var _))
		{
			num = Mathf.Max(num, Vector3.Distance(fogPoint, stageAnchor) + 90f);
		}
		return Mathf.Clamp(num, 650f, 1400f);
	}

	private void TryAutoStartCustomRunFogIfNeeded()
	{
		if (!((Object)(object)_orbFogHandler == (Object)null) && HasFogAuthority() && !_fogPaused && RunSettings.IsCustomRun && !ShouldHoldFogUntilCampfireActivation(_orbFogHandler) && !_orbFogHandler.isMoving && !_orbFogHandler.hasArrived && !(_orbFogHandler.currentWaitTime > 0.2f))
		{
			StartFogMovement();
		}
	}

	private void TryAutoResumeStalledFogMovement()
	{
		if (!((Object)(object)_orbFogHandler == (Object)null) && HasFogAuthority() && !_fogPaused && _initialDelayCompleted && !ShouldHoldFogUntilCampfireActivation(_orbFogHandler) && _orbFogHandler.currentID > 0 && !_orbFogHandler.isMoving && !_orbFogHandler.hasArrived && TryGetTargetFogOriginId(out var expectedOriginId) && expectedOriginId == _orbFogHandler.currentID)
		{
			float stalledFogResumeDelaySeconds = GetStalledFogResumeDelaySeconds(_orbFogHandler);
			if (!(_orbFogHandler.currentWaitTime < stalledFogResumeDelaySeconds))
			{
				((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Resuming stalled fog at origin {1} after waiting {2:F1}s.", "Fog&ColdControl", _orbFogHandler.currentID, _orbFogHandler.currentWaitTime));
				StartFogMovement();
			}
		}
	}

	private static float GetStalledFogResumeDelaySeconds(OrbFogHandler fogHandler)
	{
		if ((Object)(object)fogHandler == (Object)null)
		{
			return 8f;
		}
		return Mathf.Clamp(fogHandler.maxWaitTime * 0.02f, 2f, 8f);
	}

	private void UpdateTrackedFogOrigin()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || _trackedFogOriginId == _orbFogHandler.currentID)
		{
			return;
		}
		bool flag = _orbFogHandler.currentID == 0 && _trackedFogOriginId > 0;
		_trackedFogOriginId = _orbFogHandler.currentID;
		if (_trackedFogOriginId > 0)
		{
			if (IsCampfireDelayPendingForOrigin(_trackedFogOriginId))
			{
				_fogHiddenBufferTimer = 0f;
				_fogDelayTimer = 0f;
				_initialDelayCompleted = false;
				if (_initialDelayCompleted)
				{
					ClearPendingCampfireDelayForOrigin(_trackedFogOriginId);
					if (!_orbFogHandler.isMoving)
					{
						StartFogMovement();
					}
				}
			}
			else
			{
				bool flag2 = (_initialDelayCompleted = _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _orbFogHandler.currentWaitTime > 0.2f);
				_fogHiddenBufferTimer = (flag2 ? 10f : 0f);
				_fogDelayTimer = ((!flag2) ? 0f : (FogDelay?.Value ?? 0f));
			}
		}
		else if (flag)
		{
			_fogHiddenBufferTimer = 0f;
			_fogDelayTimer = 0f;
			_initialDelayCompleted = ShouldSkipInitialDelay(_orbFogHandler);
		}
	}

	private void StartFogMovement()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || _fogPaused || IsFogRemovedInCurrentScene())
		{
			return;
		}
		((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Starting fog movement. currentOrigin={1}, syntheticStage={2}, pendingOrigin={3}, currentSize={4:F1}.", "Fog&ColdControl", _orbFogHandler.currentID, _activeSyntheticFogSegmentId, _delayedFogOriginId, _orbFogHandler.currentSize));
		ClearPendingCampfireDelayForOrigin(_orbFogHandler.currentID);
		bool flag = !_initialCompassGranted && _orbFogHandler.currentID == 0;
		if (!TryInvokeFogStartMovement(_orbFogHandler, out var invocationPath))
		{
			((BaseUnityPlugin)this).Logger.LogError((object)string.Format("[{0}] Failed to start fog movement because no compatible OrbFogHandler start method was found. currentOrigin={1}.", "Fog&ColdControl", _orbFogHandler.currentID));
			return;
		}
		((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Fog movement invoked via " + invocationPath + "."));
		if (PhotonNetwork.InRoom)
		{
			ForceSyncFogStateToGuests();
		}
		InitializeFogArrivalEstimate(_orbFogHandler.currentID, GetObservedFogEtaSize(), Time.unscaledTime);
		if (flag && !HasRemotePlayersInJoinGracePeriod())
		{
			_initialCompassGranted = true;
			GrantCompassToAllPlayers("initial-delay-ended");
		}
	}

	private bool TryInvokeFogStartMovement(OrbFogHandler fogHandler, out string invocationPath)
	{
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		invocationPath = string.Empty;
		if ((Object)(object)fogHandler == (Object)null)
		{
			return false;
		}
		Type type = ((object)fogHandler).GetType();
		MethodInfo method = type.GetMethod("StartMovingRPC", InstanceBindingFlags, null, Type.EmptyTypes, null);
		if (TryInvokeFogStartMethod(fogHandler, method, Array.Empty<object>()))
		{
			invocationPath = "StartMovingRPC()";
			return true;
		}
		MethodInfo method2 = type.GetMethod("StartMovingRPC", InstanceBindingFlags, null, new Type[1] { typeof(PhotonMessageInfo) }, null);
		if (TryInvokeFogStartMethod(fogHandler, method2, new object[1] { (object)default(PhotonMessageInfo) }))
		{
			invocationPath = "StartMovingRPC(PhotonMessageInfo)";
			return true;
		}
		MethodInfo method3 = type.GetMethod("WaitToMove", InstanceBindingFlags, null, Type.EmptyTypes, null);
		if (TryInvokeFogStartMethod(fogHandler, method3, Array.Empty<object>()))
		{
			invocationPath = "WaitToMove()";
			return true;
		}
		return false;
	}

	private bool TryInvokeFogStartMethod(OrbFogHandler fogHandler, MethodInfo method, object[] arguments)
	{
		if ((Object)(object)fogHandler == (Object)null || method == null)
		{
			return false;
		}
		try
		{
			method.Invoke(fogHandler, arguments);
			return true;
		}
		catch (TargetInvocationException ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Fog start method " + method.Name + " failed: " + (ex.InnerException?.Message ?? ex.Message)));
			return false;
		}
		catch (Exception ex2)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Fog start method " + method.Name + " failed: " + ex2.Message));
			return false;
		}
	}

	private void ApplyPausedFogState(bool syncImmediately)
	{
		if (!((Object)(object)_orbFogHandler == (Object)null))
		{
			_orbFogHandler.speed = 0f;
			_orbFogHandler.isMoving = false;
			if (syncImmediately)
			{
				ForceSyncFogStateToGuests();
			}
		}
	}

	private void ForceSyncFogStateToGuests()
	{
		if (!((Object)(object)_orbFogHandler == (Object)null) && HasRemotePlayers() && PhotonNetwork.IsMasterClient && !HasRemotePlayersInJoinGracePeriod())
		{
			_lastFogStateSyncTime = -0.18f;
			ResetFogStateSyncSnapshot();
			SyncFogStateToGuestsIfNeeded();
		}
	}

	private void UpdateFogArrivalEstimate()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || _fogPaused || IsFogRemovedInCurrentScene() || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			ResetFogArrivalEstimate();
			return;
		}
		float observedFogEtaSize = GetObservedFogEtaSize();
		float unscaledTime = Time.unscaledTime;
		int currentID = _orbFogHandler.currentID;
		if (_fogEtaTrackedOriginId != currentID || float.IsNaN(_fogEtaLastObservedSize) || _fogEtaLastObservedTime < 0f)
		{
			InitializeFogArrivalEstimate(currentID, observedFogEtaSize, unscaledTime);
			return;
		}
		float num = unscaledTime - _fogEtaLastObservedTime;
		if (num < 0.1f)
		{
			return;
		}
		float num2 = _fogEtaLastObservedSize - observedFogEtaSize;
		if (num2 <= -0.05f)
		{
			InitializeFogArrivalEstimate(currentID, observedFogEtaSize, unscaledTime);
		}
		else if (!(Mathf.Abs(num2) < 0.05f))
		{
			_fogEtaLastObservedSize = observedFogEtaSize;
			_fogEtaLastObservedTime = unscaledTime;
			float num3 = num2 / num;
			if (num3 < 0.02f)
			{
				UpdateFogArrivalMetricsCache(force: false);
				return;
			}
			_fogEtaEstimatedUnitsPerSecond = (_fogEtaHasReliableRate ? Mathf.Lerp(_fogEtaEstimatedUnitsPerSecond, num3, 0.35f) : num3);
			_fogEtaHasReliableRate = true;
			UpdateFogArrivalMetricsCache(force: false);
		}
	}

	private bool TryGetFogArrivalEtaSeconds(out float etaSeconds)
	{
		etaSeconds = 0f;
		UpdateFogArrivalMetricsCache(force: false);
		if (!_fogDistanceEtaHasSnapshot)
		{
			return false;
		}
		if (_fogDistanceEtaRemainingDistance <= 0.05f)
		{
			etaSeconds = 0f;
			return true;
		}
		if (!_fogDistanceEtaHasEta)
		{
			return false;
		}
		etaSeconds = _fogDistanceEtaSeconds;
		return true;
	}

	private bool TryGetFogArrivalRemainingDistance(out float remainingDistance)
	{
		remainingDistance = 0f;
		UpdateFogArrivalMetricsCache(force: false);
		if (!_fogDistanceEtaHasSnapshot)
		{
			return false;
		}
		remainingDistance = _fogDistanceEtaRemainingDistance;
		return true;
	}

	private bool TryGetDisplayedFogEtaGeometry(out float currentSize, out Vector3 fogPoint, out Vector3 playerPoint)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		currentSize = GetDisplayedFogEtaSize();
		fogPoint = Vector3.zero;
		playerPoint = Vector3.zero;
		Character localCharacter = Character.localCharacter;
		if ((Object)(object)localCharacter == (Object)null || (Object)(object)localCharacter.data == (Object)null || localCharacter.data.dead)
		{
			return false;
		}
		playerPoint = localCharacter.Center;
		string pointDescription;
		return TryResolveFogPointForCurrentOrigin(out fogPoint, out pointDescription);
	}

	private float GetObservedFogEtaSize()
	{
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return 30f;
		}
		float num = _orbFogHandler.currentSize;
		if (IsReadOnlyFogUiViewer() && TryGetGuestFogSyncOverrideSize(out var overrideSize))
		{
			num = overrideSize;
		}
		return Mathf.Max(num, 30f);
	}

	private float GetDisplayedFogEtaSize()
	{
		float observedFogEtaSize = GetObservedFogEtaSize();
		if (!_fogEtaHasReliableRate || float.IsNaN(_fogEtaLastObservedSize) || _fogEtaLastObservedTime < 0f)
		{
			return observedFogEtaSize;
		}
		if (Mathf.Abs(observedFogEtaSize - _fogEtaLastObservedSize) >= 0.05f)
		{
			return observedFogEtaSize;
		}
		float num = _fogEtaLastObservedSize - _fogEtaEstimatedUnitsPerSecond * Mathf.Max(Time.unscaledTime - _fogEtaLastObservedTime, 0f);
		return Mathf.Clamp(Mathf.Min(observedFogEtaSize, num), 30f, observedFogEtaSize);
	}

	private void InitializeFogArrivalEstimate(int originId, float observedSize, float sampleTime)
	{
		_fogEtaTrackedOriginId = originId;
		_fogEtaLastObservedSize = observedSize;
		_fogEtaLastObservedTime = sampleTime;
		_fogEtaEstimatedUnitsPerSecond = 0f;
		_fogEtaHasReliableRate = false;
	}

	private void ResetFogArrivalEstimate()
	{
		_fogEtaTrackedOriginId = -1;
		_fogEtaLastObservedSize = float.NaN;
		_fogEtaLastObservedTime = -1f;
		_fogEtaEstimatedUnitsPerSecond = 0f;
		_fogEtaHasReliableRate = false;
		ResetFogArrivalMetricsCache();
	}

	private void UpdateFogArrivalMetricsCache(bool force)
	{
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || _fogPaused || IsFogRemovedInCurrentScene() || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			ResetFogArrivalMetricsCache();
			return;
		}
		float unscaledTime = Time.unscaledTime;
		if (!force && _fogDistanceEtaLastRefreshTime >= 0f && unscaledTime - _fogDistanceEtaLastRefreshTime < 0.8f)
		{
			return;
		}
		if (!TryGetDisplayedFogEtaGeometry(out var currentSize, out var fogPoint, out var playerPoint))
		{
			ResetFogArrivalMetricsCache();
			return;
		}
		float num = Mathf.Max(currentSize - Vector3.Distance(fogPoint, playerPoint), 0f);
		_fogDistanceEtaLastRefreshTime = unscaledTime;
		_fogDistanceEtaHasSnapshot = true;
		_fogDistanceEtaRemainingDistance = num;
		float etaRate;
		if (num <= 0.05f)
		{
			_fogDistanceEtaHasEta = true;
			_fogDistanceEtaSeconds = 0f;
		}
		else if (!TryResolveFogEtaRate(out etaRate))
		{
			_fogDistanceEtaHasEta = false;
			_fogDistanceEtaSeconds = 0f;
		}
		else
		{
			_fogDistanceEtaHasEta = true;
			_fogDistanceEtaSeconds = num / etaRate;
		}
	}

	private bool TryResolveFogEtaRate(out float etaRate)
	{
		etaRate = 0f;
		if (_fogEtaHasReliableRate && _fogEtaEstimatedUnitsPerSecond >= 0.02f)
		{
			etaRate = _fogEtaEstimatedUnitsPerSecond;
		}
		else if (HasFogAuthority() && (Object)(object)_orbFogHandler != (Object)null && _orbFogHandler.speed >= 0.02f)
		{
			etaRate = _orbFogHandler.speed;
		}
		return etaRate >= 0.02f;
	}

	private void ResetFogArrivalMetricsCache()
	{
		_fogDistanceEtaLastRefreshTime = -1f;
		_fogDistanceEtaHasSnapshot = false;
		_fogDistanceEtaHasEta = false;
		_fogDistanceEtaRemainingDistance = 0f;
		_fogDistanceEtaSeconds = 0f;
	}

	private bool TryGetTargetFogOriginId(out int expectedOriginId)
	{
		if (_delayedFogOriginId >= 0)
		{
			expectedOriginId = _delayedFogOriginId;
			return true;
		}
		return TryGetExpectedFogOriginIdFromScene(out expectedOriginId);
	}

	private bool TryGetExpectedFogOriginIdFromScene(out int expectedOriginId)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		expectedOriginId = -1;
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return false;
		}
		if ((Object)(object)Singleton<MapHandler>.Instance == (Object)null)
		{
			return false;
		}
		if (ShouldRemoveFogForSegment(MapHandler.CurrentSegmentNumber))
		{
			return false;
		}
		int availableFogOriginCount = GetAvailableFogOriginCount();
		return TryMapSegmentToFogOriginId(MapHandler.CurrentSegmentNumber, availableFogOriginCount, out expectedOriginId);
	}

	private void HandleAuthorityChangeIfNeeded(bool modEnabled)
	{
		bool flag = HasFogAuthority();
		if (flag == _lastHadFogAuthority)
		{
			return;
		}
		_lastHadFogAuthority = flag;
		ResetFogStateSyncSnapshot();
		_remoteFogSuppressionDebt.Clear();
		if (!flag && PhotonNetwork.InRoom)
		{
			RestoreVanillaFogSpeed();
			ResetFogRuntimeState();
			SetFogUiVisible(visible: false);
			SetCampfireLocatorUiVisible(visible: false);
			CleanupCompassLobbyNotice();
			return;
		}
		_lastFogStateSyncTime = -0.18f;
		_lastRemoteStatusSyncTime = -0.25f;
		_lastCompassGrantSyncTime = -0.75f;
		if (modEnabled)
		{
			_fogStateInitialized = false;
		}
	}

	private static bool HasRemotePlayers()
	{
		if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
		{
			return PhotonNetwork.CurrentRoom.PlayerCount > 1;
		}
		return false;
	}

	private void RefreshRemotePlayerJoinGraceState()
	{
		if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
		{
			_remotePlayerFirstSeenTimes.Clear();
			_remotePlayerCompassBaselineCounts.Clear();
			return;
		}
		Player localPlayer = PhotonNetwork.LocalPlayer;
		int num = ((localPlayer != null) ? localPlayer.ActorNumber : (-1));
		HashSet<int> activeRemoteActors = new HashSet<int>();
		bool flag = HasFogAuthority();
		foreach (Player value in PhotonNetwork.CurrentRoom.Players.Values)
		{
			if (value == null || value.ActorNumber <= 0 || value.ActorNumber == num)
			{
				continue;
			}
			activeRemoteActors.Add(value.ActorNumber);
			if (_remotePlayerFirstSeenTimes.ContainsKey(value.ActorNumber))
			{
				continue;
			}
			_remotePlayerFirstSeenTimes[value.ActorNumber] = Time.unscaledTime;
			_remotePlayerCompassBaselineCounts[value.ActorNumber] = _totalCompassGrantCount;
			if (flag)
			{
				if (_totalCompassGrantCount > 0)
				{
					((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Delaying host sync for remote player #{1} for {2:F1}s. Skipping {3} historical compass grants for late join safety.", "Fog&ColdControl", value.ActorNumber, 8f, _totalCompassGrantCount));
				}
				else
				{
					((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Delaying host sync for remote player #{1} for {2:F1}s.", "Fog&ColdControl", value.ActorNumber, 8f));
				}
			}
		}
		int[] array = _remotePlayerFirstSeenTimes.Keys.Where((int actorNumber) => !activeRemoteActors.Contains(actorNumber)).ToArray();
		foreach (int key in array)
		{
			_remotePlayerFirstSeenTimes.Remove(key);
			_remotePlayerCompassBaselineCounts.Remove(key);
		}
	}

	private bool HasRemotePlayersInJoinGracePeriod()
	{
		if (!HasRemotePlayers())
		{
			return false;
		}
		float now = Time.unscaledTime;
		return _remotePlayerFirstSeenTimes.Values.Any((float firstSeenTime) => now - firstSeenTime < 8f);
	}

	private void ResetFogStateSyncSnapshot()
	{
		_hasSyncedFogStateSnapshot = false;
		_lastSyncedFogOriginId = -1;
		_lastSyncedFogSize = float.NaN;
		_lastSyncedFogIsMoving = false;
		_lastSyncedFogHasArrived = false;
	}

	private void CacheFogStateSyncSnapshot()
	{
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			ResetFogStateSyncSnapshot();
			return;
		}
		_hasSyncedFogStateSnapshot = true;
		_lastSyncedFogOriginId = _orbFogHandler.currentID;
		_lastSyncedFogSize = _orbFogHandler.currentSize;
		_lastSyncedFogIsMoving = _orbFogHandler.isMoving;
		_lastSyncedFogHasArrived = _orbFogHandler.hasArrived;
	}

	private bool NeedsFogStateInitSync()
	{
		if (_hasSyncedFogStateSnapshot && !((Object)(object)_orbFogHandler == (Object)null) && _orbFogHandler.currentID == _lastSyncedFogOriginId)
		{
			return _orbFogHandler.hasArrived != _lastSyncedFogHasArrived;
		}
		return true;
	}

	private bool NeedsFogStateDeltaSync()
	{
		if (_hasSyncedFogStateSnapshot && !((Object)(object)_orbFogHandler == (Object)null) && _orbFogHandler.isMoving == _lastSyncedFogIsMoving && !float.IsNaN(_lastSyncedFogSize))
		{
			return Mathf.Abs(_orbFogHandler.currentSize - _lastSyncedFogSize) >= 0.35f;
		}
		return true;
	}

	private float GetFogStateSyncSizeForGuests()
	{
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return 0f;
		}
		if (IsFogRemovedInCurrentScene())
		{
			return 0f;
		}
		if (!TryGetGuestFogSyncOverrideSize(out var overrideSize))
		{
			return _orbFogHandler.currentSize;
		}
		return overrideSize;
	}

	private bool TryGetGuestFogSyncOverrideSize(out float overrideSize)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		overrideSize = 0f;
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return false;
		}
		if (!TryGetActiveFogSyncReference(out var customFogPoint, out var fogSegment))
		{
			return false;
		}
		if (!TryResolveFogOriginPoint(_orbFogHandler.currentID, out var fogPoint))
		{
			return false;
		}
		if (Vector3.Distance(fogPoint, customFogPoint) <= 0.05f)
		{
			return false;
		}
		if (!TryResolveFogStageCoverageAnchor(fogSegment, out var stageAnchor, out var _))
		{
			return false;
		}
		float num = Vector3.Distance(customFogPoint, stageAnchor);
		float num2 = Vector3.Distance(fogPoint, stageAnchor);
		overrideSize = Mathf.Clamp(num2 + (_orbFogHandler.currentSize - num), 30f, 6000f);
		return !Approximately(overrideSize, _orbFogHandler.currentSize);
	}

	private bool TryGetActiveFogSyncReference(out Vector3 customFogPoint, out Segment fogSegment)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		customFogPoint = Vector3.zero;
		fogSegment = (Segment)0;
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return false;
		}
		int availableFogOriginCount = GetAvailableFogOriginCount();
		if (_activeSyntheticFogSegmentId >= availableFogOriginCount && _activeSyntheticFogSegmentId >= 0)
		{
			customFogPoint = _syntheticFogPoint;
			fogSegment = (Segment)(byte)_activeSyntheticFogSegmentId;
			return ((Vector3)(ref customFogPoint)).sqrMagnitude > 0.001f;
		}
		return false;
	}

	private bool TryResolveFogOriginPoint(int originId, out Vector3 fogPoint)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		fogPoint = Vector3.zero;
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return false;
		}
		FogSphereOrigin[] array = ResolveFogOrigins(_orbFogHandler);
		if (array.Length == 0)
		{
			return false;
		}
		int num = Mathf.Clamp(originId, 0, array.Length - 1);
		FogSphereOrigin val = array[num] ?? array.LastOrDefault((FogSphereOrigin candidate) => (Object)(object)candidate != (Object)null);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		fogPoint = ((Component)val).transform.position;
		return true;
	}

	private void SyncFogOriginToGuests()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || !HasRemotePlayers() || !PhotonNetwork.IsMasterClient)
		{
			ResetFogStateSyncSnapshot();
		}
		else
		{
			if (HasRemotePlayersInJoinGracePeriod())
			{
				return;
			}
			PhotonView component = ((Component)_orbFogHandler).GetComponent<PhotonView>();
			if (!((Object)(object)component == (Object)null))
			{
				try
				{
					float fogStateSyncSizeForGuests = GetFogStateSyncSizeForGuests();
					component.RPC("RPC_InitFog", (RpcTarget)1, new object[4] { _orbFogHandler.currentID, fogStateSyncSizeForGuests, _orbFogHandler.hasArrived, _orbFogHandler.isMoving });
					_lastFogStateSyncTime = Time.unscaledTime;
					CacheFogStateSyncSnapshot();
				}
				catch (Exception ex)
				{
					((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Fog origin sync skipped: " + ex.Message));
				}
			}
		}
	}

	private void TryGrantInitialCompassIfNeeded()
	{
		if (!_initialCompassGranted && !((Object)(object)_orbFogHandler == (Object)null) && _orbFogHandler.currentID == 0 && _initialDelayCompleted && _orbFogHandler.isMoving && !HasRemotePlayersInJoinGracePeriod())
		{
			_initialCompassGranted = true;
			GrantCompassToAllPlayers("initial-delay-ended");
		}
	}

	private void SyncFogStateToGuestsIfNeeded()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || !HasRemotePlayers() || !PhotonNetwork.IsMasterClient)
		{
			ResetFogStateSyncSnapshot();
		}
		else
		{
			if (HasRemotePlayersInJoinGracePeriod())
			{
				return;
			}
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime - _lastFogStateSyncTime < 0.18f)
			{
				return;
			}
			_lastFogStateSyncTime = unscaledTime;
			PhotonView component = ((Component)_orbFogHandler).GetComponent<PhotonView>();
			if ((Object)(object)component == (Object)null)
			{
				return;
			}
			bool flag = NeedsFogStateInitSync();
			bool flag2 = NeedsFogStateDeltaSync();
			if (!flag && !flag2)
			{
				return;
			}
			try
			{
				float fogStateSyncSizeForGuests = GetFogStateSyncSizeForGuests();
				if (flag)
				{
					component.RPC("RPC_InitFog", (RpcTarget)1, new object[4] { _orbFogHandler.currentID, fogStateSyncSizeForGuests, _orbFogHandler.hasArrived, _orbFogHandler.isMoving });
				}
				else
				{
					component.RPC("RPCA_SyncFog", (RpcTarget)1, new object[2] { fogStateSyncSizeForGuests, _orbFogHandler.isMoving });
				}
				CacheFogStateSyncSnapshot();
			}
			catch (Exception ex)
			{
				((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Fog sync skipped: " + ex.Message));
			}
		}
	}

	private void SyncRemoteStatusSuppressionIfNeeded()
	{
		if (!ShouldSuppressFogColdDamage() || !HasRemotePlayers() || !PhotonNetwork.IsMasterClient)
		{
			_remoteFogSuppressionDebt.Clear();
			return;
		}
		float remoteStatusSyncIntervalSeconds = GetRemoteStatusSyncIntervalSeconds();
		float unscaledTime = Time.unscaledTime;
		float num = ((_lastRemoteStatusSyncTime < 0f) ? remoteStatusSyncIntervalSeconds : (unscaledTime - _lastRemoteStatusSyncTime));
		if (num < remoteStatusSyncIntervalSeconds)
		{
			return;
		}
		_lastRemoteStatusSyncTime = unscaledTime;
		HashSet<int> activeKeys = new HashSet<int>();
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if (!ShouldSendRemoteStatusSuppression(allCharacter))
			{
				ForgetRemoteStatusSuppression(allCharacter);
				continue;
			}
			activeKeys.Add(GetRemoteStatusSuppressionKey(allCharacter));
			float[] array = BuildRemoteStatusSuppressionPayload(allCharacter, num);
			if (array != null)
			{
				try
				{
					((MonoBehaviourPun)allCharacter).photonView.RPC("RPC_ApplyStatusesFromFloatArray", ((MonoBehaviourPun)allCharacter).photonView.Owner, new object[1] { array });
				}
				catch (Exception ex)
				{
					((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Remote status suppression skipped for " + allCharacter.characterName + ": " + ex.Message));
				}
			}
		}
		int[] array2 = _remoteFogSuppressionDebt.Keys.Where((int item) => !activeKeys.Contains(item)).ToArray();
		foreach (int key in array2)
		{
			_remoteFogSuppressionDebt.Remove(key);
		}
	}

	private bool ShouldDisableNightColdInCurrentStage()
	{
		float currentNormalizedTime;
		if (!IsNightColdFeatureEnabled() && IsNightColdStageActive())
		{
			return TryIsNightTime(out currentNormalizedTime);
		}
		return false;
	}

	private bool IsNightColdStageActive()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Invalid comparison between Unknown and I4
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return _activeSyntheticFogSegmentId >= 3;
		}
		if (TryGetCurrentGameplaySegment(out var segment))
		{
			return (int)segment >= 3;
		}
		return _activeSyntheticFogSegmentId >= 3;
	}

	private bool ShouldSuppressConfiguredNightCold(Character character)
	{
		float currentNormalizedTime;
		if ((Object)(object)character != (Object)null && (Object)(object)character.data != (Object)null && !character.data.dead && !character.isBot && !character.isZombie && !IsNightColdFeatureEnabled() && IsNightColdStageActive())
		{
			return TryIsNightTime(out currentNormalizedTime);
		}
		return false;
	}

	private static bool TryIsNightTime(out float currentNormalizedTime)
	{
		currentNormalizedTime = 0f;
		DayNightManager val = DayNightManager.instance ?? Object.FindAnyObjectByType<DayNightManager>();
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		if (TryGetIsDayFactor(val, out var isDayFactor))
		{
			currentNormalizedTime = Mathf.Clamp01(isDayFactor);
			return isDayFactor < 0.5f;
		}
		if (!TryGetCurrentDayNightTimeNormalized(val, out currentNormalizedTime))
		{
			return false;
		}
		float rangeStart = Mathf.Repeat(val.dayStart, 1f);
		float rangeEnd = Mathf.Repeat(val.dayEnd, 1f);
		return !IsTimeWithinWrappedRange(currentNormalizedTime, rangeStart, rangeEnd);
	}

	private static bool TryGetIsDayFactor(DayNightManager dayNightManager, out float isDayFactor)
	{
		isDayFactor = 0f;
		if ((Object)(object)dayNightManager == (Object)null)
		{
			return false;
		}
		Type type = ((object)dayNightManager).GetType();
		if (TryReadSingleValue(type.GetProperty("isDay", InstanceBindingFlags)?.GetValue(dayNightManager), out var value))
		{
			isDayFactor = Mathf.Clamp01(value);
			return true;
		}
		if (TryReadSingleValue(type.GetField("isDay", InstanceBindingFlags)?.GetValue(dayNightManager), out var value2))
		{
			isDayFactor = Mathf.Clamp01(value2);
			return true;
		}
		return false;
	}

	private static bool IsTimeWithinWrappedRange(float value, float rangeStart, float rangeEnd)
	{
		value = Mathf.Repeat(value, 1f);
		rangeStart = Mathf.Repeat(rangeStart, 1f);
		rangeEnd = Mathf.Repeat(rangeEnd, 1f);
		if (Mathf.Abs(rangeStart - rangeEnd) < 0.0001f)
		{
			return true;
		}
		if (rangeStart < rangeEnd)
		{
			if (value >= rangeStart)
			{
				return value < rangeEnd;
			}
			return false;
		}
		if (!(value >= rangeStart))
		{
			return value < rangeEnd;
		}
		return true;
	}

	private static bool TryGetCurrentDayNightTimeNormalized(DayNightManager dayNightManager, out float currentNormalizedTime)
	{
		currentNormalizedTime = 0f;
		if ((Object)(object)dayNightManager == (Object)null)
		{
			return false;
		}
		Type type = ((object)dayNightManager).GetType();
		for (int i = 0; i < DayNightTimeMemberCandidates.Length; i++)
		{
			string name = DayNightTimeMemberCandidates[i];
			if (TryReadSingleValue(type.GetProperty(name, InstanceBindingFlags)?.GetValue(dayNightManager), out var value))
			{
				currentNormalizedTime = Mathf.Repeat(value, 1f);
				return true;
			}
			if (TryReadSingleValue(type.GetField(name, InstanceBindingFlags)?.GetValue(dayNightManager), out var value2))
			{
				currentNormalizedTime = Mathf.Repeat(value2, 1f);
				return true;
			}
		}
		return false;
	}

	private static bool TryReadSingleValue(object rawValue, out float value)
	{
		value = 0f;
		if (!(rawValue is bool flag))
		{
			if (!(rawValue is float num))
			{
				if (!(rawValue is double num2))
				{
					if (rawValue is int num3)
					{
						value = num3;
						return true;
					}
					return false;
				}
				value = (float)num2;
				return true;
			}
			value = num;
			return true;
		}
		value = (flag ? 1f : 0f);
		return true;
	}

	private static bool ShouldSendRemoteStatusSuppression(Character character)
	{
		if (ShouldSuppressFogColdDamage() && HasFogAuthority() && (Object)(object)character != (Object)null && (Object)(object)((MonoBehaviourPun)character).photonView != (Object)null && ((MonoBehaviourPun)character).photonView.Owner != null && !((MonoBehaviourPun)character).photonView.IsMine && !character.isBot && !character.isZombie && (Object)(object)character.data != (Object)null && !character.data.dead)
		{
			if (!((Object)(object)Instance == (Object)null))
			{
				return Instance.IsCharacterPastJoinGrace(character);
			}
			return true;
		}
		return false;
	}

	private static float GetRemoteStatusSyncIntervalSeconds()
	{
		if (!ShouldPreserveVanillaLateGameNoCold())
		{
			return 0.25f;
		}
		return 0.1f;
	}

	private float[] BuildRemoteStatusSuppressionPayload(Character character, float elapsed)
	{
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		if (!ShouldSuppressFogColdDamage())
		{
			if ((Object)(object)character != (Object)null)
			{
				_remoteFogSuppressionDebt.Remove(GetRemoteStatusSuppressionKey(character));
			}
			return null;
		}
		if ((Object)(object)character?.data == (Object)null)
		{
			return null;
		}
		int remoteStatusSuppressionKey = GetRemoteStatusSuppressionKey(character);
		float value;
		float num = (_remoteFogSuppressionDebt.TryGetValue(remoteStatusSuppressionKey, out value) ? value : 0f);
		if (TryGetCurrentStatusSuppressionRate(character, out var ratePerSecond))
		{
			num += Mathf.Max(ratePerSecond, 0f) * Mathf.Max(elapsed, 0f);
		}
		if (num <= 0f)
		{
			_remoteFogSuppressionDebt.Remove(remoteStatusSuppressionKey);
			return null;
		}
		STATUSTYPE fogSuppressionStatusType = GetFogSuppressionStatusType(character);
		float suppressionTransferAmount = GetSuppressionTransferAmount(num);
		_remoteFogSuppressionDebt[remoteStatusSuppressionKey] = Mathf.Max(num - suppressionTransferAmount, 0f);
		if (_remoteFogSuppressionDebt[remoteStatusSuppressionKey] <= 0.0001f)
		{
			_remoteFogSuppressionDebt.Remove(remoteStatusSuppressionKey);
		}
		if (suppressionTransferAmount <= 0f)
		{
			return null;
		}
		float[] array = new float[StatusTypeCount];
		array[fogSuppressionStatusType] = 0f - suppressionTransferAmount;
		return array;
	}

	private bool TryGetCurrentStatusSuppressionRate(Character character, out float ratePerSecond)
	{
		ratePerSecond = 0f;
		if (!ShouldSuppressFogColdDamage() || (Object)(object)character?.data == (Object)null)
		{
			return false;
		}
		float num = 0f;
		if (IsCharacterInsideFogSphere(character))
		{
			num += 0.0105f;
		}
		if (IsCharacterInsideLegacyFog(character, out var ratePerSecond2))
		{
			num += ratePerSecond2;
		}
		if (num > 0f)
		{
			ratePerSecond += ConvertColdBaseRateToSuppressionRate(character, num) * 1.12f;
		}
		if (ShouldSuppressConfiguredNightCold(character))
		{
			ratePerSecond += ConvertColdBaseRateToSuppressionRate(character, 0.008f);
		}
		return ratePerSecond > 0f;
	}

	private static float ConvertColdBaseRateToSuppressionRate(Character character, float baseRatePerSecond)
	{
		if ((Object)(object)character?.data == (Object)null || baseRatePerSecond <= 0f)
		{
			return 0f;
		}
		float num = Mathf.Max(baseRatePerSecond, 0f);
		if (character.data.isSkeleton)
		{
			num /= 8f;
		}
		return num * GetCurrentColdDifficultyMultiplier();
	}

	private static float GetCurrentColdDifficultyMultiplier()
	{
		try
		{
			return Mathf.Max(Ascents.etcDamageMultiplier, 0f);
		}
		catch
		{
			return 1f;
		}
	}

	private bool IsCharacterInsideAnyFog(Character character)
	{
		float ratePerSecond;
		if (!IsCharacterInsideFogSphere(character))
		{
			return IsCharacterInsideLegacyFog(character, out ratePerSecond);
		}
		return true;
	}

	private bool IsCharacterInsideFogSphere(Character character)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_fogSphere != (Object)null && (Object)(object)character != (Object)null && Mathf.Approximately(_fogSphere.ENABLE, 1f))
		{
			return Vector3.Distance(_fogSphere.fogPoint, character.Center) > _fogSphere.currentSize;
		}
		return false;
	}

	private bool IsCharacterInsideLegacyFog(Character character, out float ratePerSecond)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		ratePerSecond = 0f;
		if ((Object)(object)_legacyFog == (Object)null || (Object)(object)character == (Object)null || !((Behaviour)_legacyFog).isActiveAndEnabled || !((Component)_legacyFog).gameObject.activeInHierarchy)
		{
			return false;
		}
		if (!(character.Center.y < ((Component)_legacyFog).transform.position.y))
		{
			return false;
		}
		ratePerSecond = Mathf.Max(_legacyFog.amount, 0f);
		return ratePerSecond > 0f;
	}

	private bool TryResolveCompassItem()
	{
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_compassItem != (Object)null)
		{
			return true;
		}
		if (TryResolveCompassItemOverride(out var item))
		{
			_compassItem = item;
			return true;
		}
		ItemDatabase instance = SingletonAsset<ItemDatabase>.Instance;
		if ((Object)(object)instance == (Object)null || ((DatabaseAsset<ItemDatabase, Item>)(object)instance).Objects == null)
		{
			return false;
		}
		List<string> list = new List<string>();
		foreach (Item @object in ((DatabaseAsset<ItemDatabase, Item>)(object)instance).Objects)
		{
			if ((Object)(object)@object == (Object)null)
			{
				continue;
			}
			CompassPointer componentInChildren = ((Component)@object).GetComponentInChildren<CompassPointer>(true);
			if (!((Object)(object)componentInChildren == (Object)null) && (int)componentInChildren.compassType == 0)
			{
				list.Add(DescribeCompassItem(@object));
				if (LooksLikeStandardCompassItem(@object))
				{
					_compassItem = @object;
					((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Using normal compass item: " + DescribeCompassItem(@object)));
					return true;
				}
			}
		}
		if (list.Count > 0)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to identify the standard compass automatically. Set CompassItemIdOverride manually. Candidates: " + string.Join(", ", list)));
		}
		return false;
	}

	private bool TryResolveCompassItemOverride(out Item item)
	{
		item = null;
		if (CompassItemIdOverride <= 0)
		{
			return false;
		}
		Item val = default(Item);
		if (!ItemDatabase.TryGetItem(CompassItemIdOverride, ref val) || (Object)(object)val == (Object)null)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)string.Format("[{0}] CompassItemIdOverride={1} is invalid.", "Fog&ColdControl", CompassItemIdOverride));
			return false;
		}
		item = val;
		((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Using compass item override: " + DescribeCompassItem(item)));
		return true;
	}

	private static bool LooksLikeStandardCompassItem(Item item)
	{
		if ((Object)(object)item == (Object)null)
		{
			return false;
		}
		string obj = ((Object)item).name ?? string.Empty;
		string text = item.UIData?.itemName ?? string.Empty;
		if (obj.IndexOf("Compass", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text.IndexOf("Compass", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private static string DescribeCompassItem(Item item)
	{
		if ((Object)(object)item == (Object)null)
		{
			return "<null>";
		}
		string arg = item.UIData?.itemName ?? string.Empty;
		return $"{((Object)item).name} (itemID={item.itemID}, uiName={arg})";
	}

	private IEnumerable<Player> EnumerateTargetPlayers()
	{
		HashSet<int> yieldedPlayerIds = new HashSet<int>();
		if (PhotonNetwork.InRoom)
		{
			foreach (Player allPlayer in PlayerHandler.GetAllPlayers())
			{
				if ((Object)(object)allPlayer != (Object)null && yieldedPlayerIds.Add(((Object)allPlayer).GetInstanceID()))
				{
					yield return allPlayer;
				}
			}
		}
		Player[] array = Object.FindObjectsByType<Player>((FindObjectsSortMode)0);
		Player[] array2 = array;
		foreach (Player val in array2)
		{
			if ((Object)(object)val != (Object)null && yieldedPlayerIds.Add(((Object)val).GetInstanceID()))
			{
				yield return val;
			}
		}
	}

	private void GrantCompassToAllPlayers(string reason)
	{
		if (IsModFeatureEnabled() && HasFogAuthority() && IsCompassFeatureEnabled())
		{
			_totalCompassGrantCount++;
			((BaseUnityPlugin)this).Logger.LogDebug((object)string.Format("[{0}] Queued compass grant #{1} ({2}).", "Fog&ColdControl", _totalCompassGrantCount, reason));
			SyncCompassGrantsToPlayers(force: true, reason);
		}
	}

	private void SyncCompassGrantsToPlayersIfNeeded()
	{
		if (IsModFeatureEnabled() && HasFogAuthority() && IsCompassFeatureEnabled() && _totalCompassGrantCount > 0 && !(Time.unscaledTime - _lastCompassGrantSyncTime < 0.75f))
		{
			SyncCompassGrantsToPlayers(force: false, "periodic-sync");
		}
	}

	private void SyncCompassGrantsToPlayers(bool force, string reason)
	{
		if (!IsModFeatureEnabled() || !HasFogAuthority() || !IsCompassFeatureEnabled() || _totalCompassGrantCount <= 0)
		{
			return;
		}
		if (!TryResolveCompassItem())
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to resolve compass item while syncing grants (" + reason + ")."));
			return;
		}
		_lastCompassGrantSyncTime = Time.unscaledTime;
		HashSet<int> activePlayerKeys = new HashSet<int>();
		Dictionary<int, Player> dictionary = new Dictionary<int, Player>();
		foreach (Player item in EnumerateTargetPlayers())
		{
			if (!IsPlayerPastJoinGrace(item))
			{
				continue;
			}
			int playerCompassGrantKey = GetPlayerCompassGrantKey(item);
			activePlayerKeys.Add(playerCompassGrantKey);
			PhotonView component = ((Component)item).GetComponent<PhotonView>();
			int? obj;
			if (component == null)
			{
				obj = null;
			}
			else
			{
				Player owner = component.Owner;
				obj = ((owner != null) ? new int?(owner.ActorNumber) : ((int?)null));
			}
			int num = obj ?? (-1);
			if (num > 0 && !dictionary.ContainsKey(num))
			{
				dictionary[num] = item;
			}
			int num2 = GetDeliveredCompassGrantCount(item);
			int num3 = 0;
			while (num2 < _totalCompassGrantCount && GrantCompassToPlayer(item, $"{reason}-{num2 + 1}/{_totalCompassGrantCount}"))
			{
				num2++;
				num3++;
				if (num3 >= 1)
				{
					break;
				}
			}
			_playerCompassGrantCounts[playerCompassGrantKey] = num2;
		}
		TrySyncCompassGrantsToMissingActorPlayers(reason, activePlayerKeys, dictionary);
		int[] array = _playerCompassGrantCounts.Keys.Where((int item) => !activePlayerKeys.Contains(item)).ToArray();
		foreach (int key in array)
		{
			_playerCompassGrantCounts.Remove(key);
		}
	}

	private void TrySyncCompassGrantsToMissingActorPlayers(string reason, HashSet<int> activePlayerKeys, Dictionary<int, Player> playersByActorNumber)
	{
		if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
		{
			return;
		}
		Player localPlayer = PhotonNetwork.LocalPlayer;
		int num = ((localPlayer != null) ? localPlayer.ActorNumber : (-1));
		foreach (Player item in PhotonNetwork.CurrentRoom.Players.Values.OrderBy((Player player) => (player == null) ? int.MaxValue : player.ActorNumber))
		{
			int num2 = ((item != null) ? item.ActorNumber : (-1));
			if (num2 <= 0 || num2 == num || playersByActorNumber.ContainsKey(num2))
			{
				continue;
			}
			activePlayerKeys.Add(num2);
			if (!IsActorPastJoinGrace(num2))
			{
				continue;
			}
			int num3 = GetDeliveredCompassGrantCountForActor(num2);
			int num4 = 0;
			while (num3 < _totalCompassGrantCount && GrantCompassToActor(num2, $"{reason}-{num3 + 1}/{_totalCompassGrantCount}"))
			{
				num3++;
				num4++;
				if (num4 >= 1)
				{
					break;
				}
			}
			_playerCompassGrantCounts[num2] = num3;
		}
	}

	private static int GetPlayerCompassGrantKey(Player player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return 0;
		}
		PhotonView component = ((Component)player).GetComponent<PhotonView>();
		if (((component != null) ? component.Owner : null) != null)
		{
			return component.Owner.ActorNumber;
		}
		return ((Object)player).GetInstanceID();
	}

	private int GetDeliveredCompassGrantCount(Player player)
	{
		int playerCompassGrantKey = GetPlayerCompassGrantKey(player);
		if (_playerCompassGrantCounts.TryGetValue(playerCompassGrantKey, out var value))
		{
			return value;
		}
		PhotonView val = (((Object)(object)player != (Object)null) ? ((Component)player).GetComponent<PhotonView>() : null);
		if (((val != null) ? val.Owner : null) != null && _remotePlayerCompassBaselineCounts.TryGetValue(val.Owner.ActorNumber, out var value2))
		{
			return value2;
		}
		return 0;
	}

	private int GetDeliveredCompassGrantCountForActor(int actorNumber)
	{
		if (_playerCompassGrantCounts.TryGetValue(actorNumber, out var value))
		{
			return value;
		}
		if (_remotePlayerCompassBaselineCounts.TryGetValue(actorNumber, out var value2))
		{
			return value2;
		}
		return 0;
	}

	private bool IsPlayerPastJoinGrace(Player player)
	{
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		PhotonView component = ((Component)player).GetComponent<PhotonView>();
		if (((component != null) ? component.Owner : null) == null || component.IsMine)
		{
			return true;
		}
		if (!_remotePlayerFirstSeenTimes.TryGetValue(component.Owner.ActorNumber, out var value))
		{
			return true;
		}
		return Time.unscaledTime - value >= 8f;
	}

	private bool IsActorPastJoinGrace(int actorNumber)
	{
		if (actorNumber <= 0)
		{
			return false;
		}
		Player localPlayer = PhotonNetwork.LocalPlayer;
		int num = ((localPlayer != null) ? localPlayer.ActorNumber : (-1));
		if (actorNumber == num)
		{
			return true;
		}
		if (!_remotePlayerFirstSeenTimes.TryGetValue(actorNumber, out var value))
		{
			return true;
		}
		return Time.unscaledTime - value >= 8f;
	}

	private void ProcessPendingCampfireCompassGrants()
	{
		if (IsModFeatureEnabled() && HasFogAuthority() && IsCompassFeatureEnabled() && _pendingCampfireCompassGrantTimes.Count > 0 && !LoadingScreenHandler.loading)
		{
			float now = Time.unscaledTime;
			int[] array = (from entry in _pendingCampfireCompassGrantTimes
				where now >= entry.Value
				select entry.Key).ToArray();
			foreach (int num2 in array)
			{
				_pendingCampfireCompassGrantTimes.Remove(num2);
				((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Delivering delayed campfire compass grant for campfire #{1}.", "Fog&ColdControl", num2));
				GrantCompassToAllPlayers($"campfire-{num2}");
			}
		}
	}

	private bool GrantCompassToPlayer(Player player, string reason)
	{
		if ((Object)(object)player == (Object)null || (Object)(object)_compassItem == (Object)null || !IsPlayerPastJoinGrace(player))
		{
			return false;
		}
		PhotonView component = ((Component)player).GetComponent<PhotonView>();
		int? obj;
		if (component == null)
		{
			obj = null;
		}
		else
		{
			Player owner = component.Owner;
			obj = ((owner != null) ? new int?(owner.ActorNumber) : ((int?)null));
		}
		int actorNumber = obj ?? (-1);
		if (((component != null) ? component.Owner : null) != null && !component.IsMine)
		{
			Character val = player.character;
			if ((Object)(object)val == (Object)null || (Object)(object)val.data == (Object)null || val.data.dead)
			{
				val = Character.AllCharacters.FirstOrDefault((Character character) => (Object)(object)character != (Object)null && (Object)(object)((MonoBehaviourPun)character).photonView != (Object)null && ((MonoBehaviourPun)character).photonView.Owner != null && ((MonoBehaviourPun)character).photonView.Owner.ActorNumber == actorNumber && !character.isBot && !character.isZombie && (Object)(object)character.data != (Object)null && !character.data.dead);
			}
			if ((Object)(object)val != (Object)null)
			{
				return DropCompassInFrontOfCharacter(val, actorNumber, reason);
			}
		}
		return DropCompassInFrontOfPlayer(player, reason, preferLocalViewAnchor: true);
	}

	private bool GrantCompassToActor(int actorNumber, string reason)
	{
		if (actorNumber <= 0 || (Object)(object)_compassItem == (Object)null || !IsActorPastJoinGrace(actorNumber))
		{
			return false;
		}
		Player val = EnumerateTargetPlayers().FirstOrDefault(delegate(Player player)
		{
			PhotonView obj = (((Object)(object)player != (Object)null) ? ((Component)player).GetComponent<PhotonView>() : null);
			if (obj == null)
			{
				return false;
			}
			Player owner = obj.Owner;
			return ((owner != null) ? new int?(owner.ActorNumber) : ((int?)null)) == actorNumber;
		});
		if ((Object)(object)val != (Object)null)
		{
			return GrantCompassToPlayer(val, reason);
		}
		Character val2 = Character.AllCharacters.FirstOrDefault((Character character) => (Object)(object)character != (Object)null && (Object)(object)((MonoBehaviourPun)character).photonView != (Object)null && ((MonoBehaviourPun)character).photonView.Owner != null && ((MonoBehaviourPun)character).photonView.Owner.ActorNumber == actorNumber && !character.isBot && !character.isZombie && (Object)(object)character.data != (Object)null && !character.data.dead);
		if ((Object)(object)val2 == (Object)null)
		{
			return false;
		}
		return DropCompassInFrontOfCharacter(val2, actorNumber, reason);
	}

	private bool DropCompassInFrontOfPlayer(Player player, string reason)
	{
		return DropCompassInFrontOfPlayer(player, reason, preferLocalViewAnchor: false);
	}

	private bool DropCompassInFrontOfPlayer(Player player, string reason, bool preferLocalViewAnchor)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)player == (Object)null || (Object)(object)_compassItem == (Object)null)
		{
			return false;
		}
		ResolveCompassSpawnPose(player, preferLocalViewAnchor, out var spawnPosition, out var spawnRotation);
		try
		{
			if (PhotonNetwork.InRoom)
			{
				PhotonNetwork.InstantiateItemRoom(((Object)_compassItem).name, spawnPosition, spawnRotation);
			}
			else
			{
				Object.Instantiate<GameObject>(((Component)_compassItem).gameObject, spawnPosition, spawnRotation);
			}
			((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Spawned compass in front of " + ((Object)player).name + " (" + reason + ")."));
			return true;
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to spawn compass in front of " + ((Object)player).name + " (" + reason + "): " + ex.Message));
			return false;
		}
	}

	private bool DropCompassInFrontOfCharacter(Character character, int actorNumber, string reason)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)character == (Object)null || (Object)(object)_compassItem == (Object)null)
		{
			return false;
		}
		ResolveCompassSpawnPose(character, out var spawnPosition, out var spawnRotation);
		try
		{
			if (PhotonNetwork.InRoom)
			{
				PhotonNetwork.InstantiateItemRoom(((Object)_compassItem).name, spawnPosition, spawnRotation);
			}
			else
			{
				Object.Instantiate<GameObject>(((Component)_compassItem).gameObject, spawnPosition, spawnRotation);
			}
			((BaseUnityPlugin)this).Logger.LogDebug((object)string.Format("[{0}] Spawned compass near actor #{1} ({2}).", "Fog&ColdControl", actorNumber, reason));
			return true;
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)string.Format("[{0}] Failed to spawn compass near actor #{1} ({2}): {3}", "Fog&ColdControl", actorNumber, reason, ex.Message));
			return false;
		}
	}

	private static Vector3 GetCompassDropPosition(Player player)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		ResolveCompassSpawnPose(player, out var spawnPosition, out var _);
		return spawnPosition;
	}

	private static Quaternion GetCompassDropRotation(Player player)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		ResolveCompassSpawnPose(player, out var _, out var spawnRotation);
		return spawnRotation;
	}

	private static void ResolveCompassSpawnPose(Player player, out Vector3 spawnPosition, out Quaternion spawnRotation)
	{
		ResolveCompassSpawnPose(player, preferLocalViewAnchor: false, out spawnPosition, out spawnRotation);
	}

	private static void ResolveCompassSpawnPose(Player player, bool preferLocalViewAnchor, out Vector3 spawnPosition, out Quaternion spawnRotation)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		if (!preferLocalViewAnchor || !TryResolveLocalViewCompassSpawnPose(player, out spawnPosition, out spawnRotation))
		{
			Vector3 val = ResolveCompassFaceAnchorPosition(player);
			Vector3 val2 = ResolveCompassFacingForward(player);
			if (((Vector3)(ref val2)).sqrMagnitude < 0.01f)
			{
				val2 = Vector3.forward;
			}
			val2 = ((Vector3)(ref val2)).normalized;
			Vector3 val3 = Vector3.ProjectOnPlane(val2, Vector3.up);
			if (((Vector3)(ref val3)).sqrMagnitude < 0.01f)
			{
				val3 = Vector3.ProjectOnPlane(((Component)player).transform.forward, Vector3.up);
			}
			if (((Vector3)(ref val3)).sqrMagnitude < 0.01f)
			{
				val3 = Vector3.forward;
			}
			val3 = ((Vector3)(ref val3)).normalized;
			spawnPosition = val + val2 * 0.85f;
			spawnRotation = Quaternion.LookRotation(val3, Vector3.up);
		}
	}

	private static void ResolveCompassSpawnPose(Character character, out Vector3 spawnPosition, out Quaternion spawnRotation)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = character.Center + Vector3.up * 0.5f;
		Vector3 val2 = ((Component)character).transform.forward;
		if (((Vector3)(ref val2)).sqrMagnitude < 0.01f)
		{
			val2 = Vector3.forward;
		}
		val2 = ((Vector3)(ref val2)).normalized;
		Vector3 val3 = Vector3.ProjectOnPlane(val2, Vector3.up);
		if (((Vector3)(ref val3)).sqrMagnitude < 0.01f)
		{
			val3 = Vector3.forward;
		}
		val3 = ((Vector3)(ref val3)).normalized;
		spawnPosition = val + val2 * 0.85f;
		spawnRotation = Quaternion.LookRotation(val3, Vector3.up);
	}

	private static bool TryResolveLocalViewCompassSpawnPose(Player player, out Vector3 spawnPosition, out Quaternion spawnRotation)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		spawnPosition = default(Vector3);
		spawnRotation = Quaternion.identity;
		object obj = Player.localPlayer;
		if (obj == null)
		{
			Character localCharacter = Character.localCharacter;
			obj = ((localCharacter != null) ? localCharacter.player : null);
		}
		Player val = (Player)obj;
		if ((Object)(object)player == (Object)null || (Object)(object)val == (Object)null || (Object)(object)player != (Object)(object)val)
		{
			return false;
		}
		Camera val2 = Camera.main;
		if ((Object)(object)val2 == (Object)null || !((Behaviour)val2).isActiveAndEnabled)
		{
			val2 = Object.FindObjectsByType<Camera>((FindObjectsSortMode)0).FirstOrDefault((Camera candidate) => (Object)(object)candidate != (Object)null && ((Behaviour)candidate).isActiveAndEnabled && ((Component)candidate).gameObject.activeInHierarchy);
		}
		if ((Object)(object)val2 == (Object)null)
		{
			return false;
		}
		Transform transform = ((Component)val2).transform;
		Vector3 val3 = transform.forward;
		if (((Vector3)(ref val3)).sqrMagnitude < 0.01f)
		{
			return false;
		}
		val3 = ((Vector3)(ref val3)).normalized;
		Vector3 val4 = Vector3.ProjectOnPlane(val3, Vector3.up);
		if (((Vector3)(ref val4)).sqrMagnitude < 0.01f)
		{
			val4 = Vector3.ProjectOnPlane(((Component)player).transform.forward, Vector3.up);
		}
		if (((Vector3)(ref val4)).sqrMagnitude < 0.01f)
		{
			val4 = Vector3.forward;
		}
		val4 = ((Vector3)(ref val4)).normalized;
		spawnPosition = transform.position + val3 * 0.9f;
		spawnRotation = Quaternion.LookRotation(val4, Vector3.up);
		return true;
	}

	private static Vector3 ResolveCompassFaceAnchorPosition(Player player)
	{
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		if (TryGetCompassFaceAnchor(player, out var anchor))
		{
			Vector3 val = ((((Object)anchor).name.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0) ? (Vector3.down * 0.03f) : Vector3.zero);
			return anchor.position + val;
		}
		Character character = player.character;
		if ((Object)(object)character != (Object)null)
		{
			Vector3 center = character.Center;
			return new Vector3(center.x, center.y + 0.5f, center.z);
		}
		return ((Component)player).transform.position + Vector3.up * 1.45f;
	}

	private static Vector3 ResolveCompassFacingForward(Player player)
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		if (TryGetCompassFaceAnchor(player, out var anchor) && ShouldUseCompassAnchorForward(anchor))
		{
			Vector3 forward = anchor.forward;
			if (((Vector3)(ref forward)).sqrMagnitude >= 0.01f)
			{
				return forward;
			}
		}
		Character character = player.character;
		Vector3 forward2;
		if ((Object)(object)character != (Object)null)
		{
			forward2 = ((Component)character).transform.forward;
			if (((Vector3)(ref forward2)).sqrMagnitude >= 0.01f)
			{
				return ((Component)character).transform.forward;
			}
		}
		forward2 = ((Component)player).transform.forward;
		if (((Vector3)(ref forward2)).sqrMagnitude >= 0.01f)
		{
			return ((Component)player).transform.forward;
		}
		if (TryGetCompassFaceAnchor(player, out anchor))
		{
			forward2 = anchor.forward;
			if (((Vector3)(ref forward2)).sqrMagnitude >= 0.01f)
			{
				return anchor.forward;
			}
		}
		return Vector3.forward;
	}

	private static bool TryGetCompassFaceAnchor(Player player, out Transform anchor)
	{
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		anchor = null;
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		Transform[] obj = ((!((Object)(object)player.character != (Object)null) || !((Object)(object)((Component)player.character).transform != (Object)(object)((Component)player).transform)) ? new Transform[1] { ((Component)player).transform } : new Transform[2]
		{
			((Component)player.character).transform,
			((Component)player).transform
		});
		int num = int.MaxValue;
		float num2 = float.MinValue;
		Transform[] array = (Transform[])(object)obj;
		foreach (Transform val in array)
		{
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			Transform[] componentsInChildren = ((Component)val).GetComponentsInChildren<Transform>(true);
			foreach (Transform val2 in componentsInChildren)
			{
				int compassFaceAnchorScore = GetCompassFaceAnchorScore(val2);
				if (compassFaceAnchorScore != int.MaxValue)
				{
					float y = val2.position.y;
					if (compassFaceAnchorScore < num || (compassFaceAnchorScore == num && y > num2))
					{
						num = compassFaceAnchorScore;
						num2 = y;
						anchor = val2;
					}
				}
			}
		}
		return (Object)(object)anchor != (Object)null;
	}

	private static int GetCompassFaceAnchorScore(Transform candidate)
	{
		if ((Object)(object)candidate == (Object)null)
		{
			return int.MaxValue;
		}
		string text = ((Object)candidate).name ?? string.Empty;
		if (text.Equals("MainCamera", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (text.IndexOf("maincamera", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 1;
		}
		if (text.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 2;
		}
		if (text.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 3;
		}
		if (text.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 4;
		}
		if (text.IndexOf("look", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 5;
		}
		if (text.IndexOf("jaw", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 6;
		}
		return int.MaxValue;
	}

	private static bool ShouldUseCompassAnchorForward(Transform anchor)
	{
		if ((Object)(object)anchor == (Object)null)
		{
			return false;
		}
		string text = ((Object)anchor).name ?? string.Empty;
		if (text.IndexOf("camera", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text.IndexOf("look", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private void HandleManualCompassHotkey()
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled() || !HasFogAuthority() || !IsCompassFeatureEnabled())
		{
			return;
		}
		KeyCode compassHotkey = GetCompassHotkey();
		if ((int)compassHotkey != 0)
		{
			GUIManager instance = GUIManager.instance;
			if ((!((Object)(object)instance != (Object)null) || !instance.windowBlockingInput) && Input.GetKeyDown(compassHotkey))
			{
				TrySpawnManualCompassForLocalPlayer();
			}
		}
	}

	private void HandleFogPauseHotkey()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled() || !HasFogAuthority() || !IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return;
		}
		KeyCode fogPauseHotkey = GetFogPauseHotkey();
		if ((int)fogPauseHotkey == 0)
		{
			return;
		}
		GUIManager instance = GUIManager.instance;
		if (((Object)(object)instance != (Object)null && instance.windowBlockingInput) || !Input.GetKeyDown(fogPauseHotkey))
		{
			return;
		}
		_fogPaused = !_fogPaused;
		if (_fogPaused)
		{
			ApplyPausedFogState(syncImmediately: true);
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Fog paused by hotkey {1}.", "Fog&ColdControl", fogPauseHotkey));
			return;
		}
		((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Fog resumed by hotkey {1}.", "Fog&ColdControl", fogPauseHotkey));
		if ((Object)(object)_orbFogHandler != (Object)null && _initialDelayCompleted && !ShouldHoldFogUntilCampfireActivation(_orbFogHandler) && !_orbFogHandler.isMoving)
		{
			StartFogMovement();
		}
		ForceSyncFogStateToGuests();
	}

	private void HandleHiddenNightTestHotkey()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled() || !HasFogAuthority())
		{
			ResetHiddenNightTestHotkeyState();
			return;
		}
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			ResetHiddenNightTestHotkeyState();
			return;
		}
		GUIManager instance = GUIManager.instance;
		if ((Object)(object)instance != (Object)null && instance.windowBlockingInput)
		{
			ResetHiddenNightTestHotkeyState();
		}
		else if (!Input.GetKey((KeyCode)91))
		{
			ResetHiddenNightTestHotkeyState();
		}
		else if (!_hiddenNightTestTriggeredThisHold)
		{
			_hiddenNightTestHoldTimer += Time.unscaledDeltaTime;
			if (!(_hiddenNightTestHoldTimer < 5f))
			{
				_hiddenNightTestTriggeredThisHold = true;
				SwitchToNightForTesting();
			}
		}
	}

	private void ResetHiddenNightTestHotkeyState()
	{
		_hiddenNightTestHoldTimer = 0f;
		_hiddenNightTestTriggeredThisHold = false;
	}

	private void SwitchToNightForTesting()
	{
		DayNightManager val = Object.FindAnyObjectByType<DayNightManager>();
		if ((Object)(object)val == (Object)null)
		{
			((BaseUnityPlugin)this).Logger.LogDebug((object)"[Fog&ColdControl] Hidden night-test hotkey skipped because DayNightManager is unavailable.");
			return;
		}
		float num = CalculateNightTestTime(val);
		try
		{
			val.setTimeOfDay(num);
			val.UpdateCycle();
			PhotonView component = ((Component)val).GetComponent<PhotonView>();
			if (PhotonNetwork.InRoom && (Object)(object)component != (Object)null)
			{
				component.RPC("RPCA_SyncTime", (RpcTarget)1, new object[1] { num });
			}
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Hidden night-test hotkey forced night at {1:F3}{2}", "Fog&ColdControl", num, PhotonNetwork.InRoom ? " and synced it to guests." : "."));
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Hidden night-test hotkey failed: " + ex.Message));
		}
	}

	private static float CalculateNightTestTime(DayNightManager dayNightManager)
	{
		if ((Object)(object)dayNightManager == (Object)null)
		{
			return 0.85f;
		}
		float num = Mathf.Repeat(dayNightManager.dayStart, 1f);
		float num2 = Mathf.Repeat(dayNightManager.dayEnd, 1f);
		float num3 = Mathf.Repeat(num - num2, 1f);
		if (num3 < 0.01f)
		{
			return 0.85f;
		}
		return Mathf.Repeat(num2 + num3 * 0.5f, 1f);
	}

	private void TrySpawnManualCompassForLocalPlayer()
	{
		if (IsCompassFeatureEnabled())
		{
			Player localPlayer;
			if (!TryResolveCompassItem())
			{
				((BaseUnityPlugin)this).Logger.LogWarning((object)"[Fog&ColdControl] Failed to resolve compass item for manual hotkey spawn.");
			}
			else if (TryGetLocalPlayablePlayer(out localPlayer) && !DropCompassInFrontOfPlayer(localPlayer, "manual-hotkey", preferLocalViewAnchor: true))
			{
				((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Manual compass hotkey spawn failed for " + ((Object)localPlayer).name + "."));
			}
		}
	}

	private static bool TryGetLocalPlayablePlayer(out Player localPlayer)
	{
		object obj = Player.localPlayer;
		if (obj == null)
		{
			Character localCharacter = Character.localCharacter;
			obj = ((localCharacter != null) ? localCharacter.player : null);
		}
		localPlayer = (Player)obj;
		Player obj2 = localPlayer;
		Character val = ((obj2 != null) ? obj2.character : null) ?? Character.localCharacter;
		if ((Object)(object)localPlayer != (Object)null && (Object)(object)val != (Object)null && !val.isBot)
		{
			return !val.isZombie;
		}
		return false;
	}

	private void HandleFogUiConfigChanges()
	{
		bool flag = FogUiEnabled?.Value ?? true;
		bool flag2 = CampfireLocatorUiEnabled?.Value ?? true;
		float num = FogUiX?.Value ?? 60f;
		float num2 = FogUiY?.Value ?? 0f;
		float num3 = FogUiScale?.Value ?? 0.9f;
		if (flag != _lastFogUiEnabledState || flag2 != _lastCampfireLocatorUiEnabledState || !Approximately(num, _lastFogUiX) || !Approximately(num2, _lastFogUiY) || !Approximately(num3, _lastFogUiScale))
		{
			_lastFogUiEnabledState = flag;
			_lastCampfireLocatorUiEnabledState = flag2;
			_lastFogUiX = num;
			_lastFogUiY = num2;
			_lastFogUiScale = num3;
			CreateFogUi();
			CreateCampfireLocatorUi();
		}
	}

	private void HandleLanguageChangeIfNeeded()
	{
		bool flag = DetectChineseLanguage();
		if (flag != _lastDetectedChineseLanguage)
		{
			ReinitializeLocalizedConfig(flag);
		}
		TryLocalizeVisibleModConfigUi();
	}

	private void ReinitializeLocalizedConfig(bool isChineseLanguage)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		if (_isRefreshingLanguage)
		{
			return;
		}
		_isRefreshingLanguage = true;
		try
		{
			_lastDetectedChineseLanguage = isChineseLanguage;
			ApplyLocalizedConfigMetadata(isChineseLanguage);
			MarkConfigFileLocalizationDirty(saveConfigFile: true);
			if ((Object)(object)_fogUiText != (Object)null)
			{
				ApplyGameTextStyle(_fogUiText, Color.white);
			}
			if ((Object)(object)_compassLobbyNoticeText != (Object)null)
			{
				ApplyCompassLobbyNoticeStyle(_compassLobbyNoticeText);
			}
			RefreshFogUiEntryStyles();
			TryLocalizeVisibleModConfigUi();
			UpdateFogUi();
			UpdateCompassLobbyNotice();
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to reinitialize localized config: " + ex.Message));
		}
		finally
		{
			_isRefreshingLanguage = false;
		}
	}

	private void ApplyLocalizedConfigMetadata(bool isChineseLanguage)
	{
		try
		{
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			foreach (ConfigEntryBase val in configEntriesSnapshot)
			{
				if (!(((val != null) ? val.Definition : null) == (ConfigDefinition)null) && val.Description != null && TryGetConfigKey(val.Definition.Key, out var configKey))
				{
					string localizedDescription = GetLocalizedDescription(configKey, isChineseLanguage);
					if (!string.IsNullOrWhiteSpace(localizedDescription))
					{
						SetPrivateField(val.Description, "<Description>k__BackingField", localizedDescription);
					}
				}
			}
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to apply localized config metadata: " + ex.Message));
		}
	}

	private static ConfigEntryBase[] GetConfigEntriesSnapshot(ConfigFile configFile)
	{
		if (configFile == null)
		{
			return Array.Empty<ConfigEntryBase>();
		}
		if (!(ConfigFileEntriesProperty?.GetValue(configFile) is IDictionary { Count: not 0 } dictionary))
		{
			return Array.Empty<ConfigEntryBase>();
		}
		return (from entry in dictionary.Values.Cast<object>().OfType<ConfigEntryBase>()
			where entry != null
			select entry).ToArray();
	}

	private static void SetPrivateField(object target, string fieldName, object value)
	{
		if (target != null && !string.IsNullOrWhiteSpace(fieldName))
		{
			target.GetType().GetField(fieldName, InstanceBindingFlags)?.SetValue(target, value);
		}
	}

	private static bool TryGetConfigKey(string keyName, out ConfigKey configKey)
	{
		foreach (ConfigKey value in Enum.GetValues(typeof(ConfigKey)))
		{
			if (string.Equals(keyName, GetConfigKeyName(value), StringComparison.OrdinalIgnoreCase) || string.Equals(keyName, GetKeyName(value, isChineseLanguage: true), StringComparison.OrdinalIgnoreCase))
			{
				configKey = value;
				return true;
			}
		}
		configKey = ConfigKey.ModEnabled;
		return false;
	}

	private void TryRefreshLocalizedConfigFile(bool isChineseLanguage, bool saveConfigFile)
	{
		try
		{
			if (((BaseUnityPlugin)this).Config != null && !string.IsNullOrWhiteSpace(((BaseUnityPlugin)this).Config.ConfigFilePath))
			{
				if (saveConfigFile)
				{
					((BaseUnityPlugin)this).Config.Save();
				}
				RewriteConfigFileLocalization(((BaseUnityPlugin)this).Config.ConfigFilePath, isChineseLanguage);
			}
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)("[Fog&ColdControl] Failed to refresh localized config file: " + ex.Message));
		}
	}

	private static void RewriteConfigFileLocalization(string configFilePath, bool isChineseLanguage)
	{
		if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
		{
			return;
		}
		string[] array = File.ReadAllLines(configFilePath);
		string[] array2 = new string[array.Length];
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			string obj = array[i] ?? string.Empty;
			if (!string.Equals(obj, array2[i] = RewriteConfigFileLine(obj, isChineseLanguage), StringComparison.Ordinal))
			{
				flag = true;
			}
		}
		if (flag)
		{
			File.WriteAllLines(configFilePath, array2);
		}
	}

	private static string RewriteConfigFileLine(string line, bool isChineseLanguage)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return line ?? string.Empty;
		}
		string text = line.Trim();
		if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
		{
			if (!TryGetLocalizedSectionName(text.Substring(1, text.Length - 2).Trim(), isChineseLanguage, out var localizedSectionName))
			{
				return line;
			}
			int num = line.IndexOf('[');
			int num2 = line.LastIndexOf(']');
			if (num < 0 || num2 < num)
			{
				return line;
			}
			return line.Substring(0, num + 1) + localizedSectionName + line.Substring(num2);
		}
		if (text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith(";", StringComparison.Ordinal))
		{
			return line;
		}
		int num3 = line.IndexOf('=');
		if (num3 <= 0)
		{
			return line;
		}
		int i;
		for (i = 0; i < num3 && char.IsWhiteSpace(line[i]); i++)
		{
		}
		int num4 = num3 - 1;
		while (num4 >= i && char.IsWhiteSpace(line[num4]))
		{
			num4--;
		}
		if (num4 < i)
		{
			return line;
		}
		if (!TryGetConfigKey(line.Substring(i, num4 - i + 1), out var configKey))
		{
			return line;
		}
		string keyName = GetKeyName(configKey, isChineseLanguage);
		return line.Substring(0, i) + keyName + line.Substring(num4 + 1);
	}

	private static bool TryGetLocalizedSectionName(string sectionName, bool isChineseLanguage, out string localizedSectionName)
	{
		if (MatchesAdjustmentSectionName(sectionName))
		{
			localizedSectionName = GetSectionName(ConfigKey.FogUiX, isChineseLanguage);
			return true;
		}
		if (MatchesBasicSectionName(sectionName))
		{
			localizedSectionName = GetSectionName(ConfigKey.ModEnabled, isChineseLanguage);
			return true;
		}
		localizedSectionName = string.Empty;
		return false;
	}

	private static bool MatchesBasicSectionName(string sectionName)
	{
		if (!string.Equals(sectionName, "Basic", StringComparison.OrdinalIgnoreCase) && !string.Equals(sectionName, GetSectionName(ConfigKey.ModEnabled, isChineseLanguage: true), StringComparison.Ordinal) && !string.Equals(sectionName, GetLegacyConfigSectionName(), StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(sectionName, GetLegacySectionName(isChineseLanguage: true), StringComparison.Ordinal);
		}
		return true;
	}

	private static bool MatchesAdjustmentSectionName(string sectionName)
	{
		if (!string.Equals(sectionName, "Adjustments", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(sectionName, GetSectionName(ConfigKey.FogUiX, isChineseLanguage: true), StringComparison.Ordinal);
		}
		return true;
	}

	private void TryLocalizeVisibleModConfigUi()
	{
		if (!TryGetModConfigMenuInstance(out var menuType, out var menuInstance))
		{
			return;
		}
		Behaviour val = (Behaviour)((menuInstance is Behaviour) ? menuInstance : null);
		if (val == null || (Object)(object)val == (Object)null)
		{
			return;
		}
		try
		{
			if (!val.isActiveAndEnabled || !((Component)val).gameObject.activeInHierarchy)
			{
				return;
			}
		}
		catch
		{
			return;
		}
		Dictionary<string, string> map = BuildModConfigUiLocalizationMap(DetectChineseLanguage());
		foreach (Transform item in EnumerateModConfigUiRoots(menuInstance, menuType))
		{
			ApplyTextLocalizationToRoot(item, map);
		}
	}

	private static bool TryGetModConfigMenuInstance(out Type menuType, out object menuInstance)
	{
		menuType = null;
		menuInstance = null;
		if (!Chainloader.PluginInfos.TryGetValue("com.github.PEAKModding.PEAKLib.ModConfig", out var value) || (Object)(object)((value != null) ? value.Instance : null) == (Object)null)
		{
			return false;
		}
		Assembly assembly = ((object)value.Instance).GetType().Assembly;
		menuType = assembly.GetType("PEAKLib.ModConfig.Components.ModdedSettingsMenu");
		menuInstance = menuType?.GetProperty("Instance", StaticBindingFlags)?.GetValue(null);
		if (menuType != null)
		{
			return menuInstance != null;
		}
		return false;
	}

	private IEnumerable<Transform> EnumerateModConfigUiRoots(object menuInstance, Type menuType)
	{
		HashSet<int> visited = new HashSet<int>();
		foreach (Transform item in EnumerateCandidateTransforms(menuInstance, menuType))
		{
			if ((Object)(object)item != (Object)null && visited.Add(((Object)item).GetInstanceID()))
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<Transform> EnumerateCandidateTransforms(object menuInstance, Type menuType)
	{
		Component val = (Component)((menuInstance is Component) ? menuInstance : null);
		if (val != null)
		{
			yield return val.transform;
		}
		object obj = menuType?.GetProperty("Content", InstanceBindingFlags)?.GetValue(menuInstance);
		Transform val2 = (Transform)((obj is Transform) ? obj : null);
		if (val2 != null)
		{
			yield return val2;
		}
	}

	private void ApplyTextLocalizationToRoot(Transform root, Dictionary<string, string> map)
	{
		if ((Object)(object)root == (Object)null || map == null || map.Count == 0)
		{
			return;
		}
		TMP_Text[] componentsInChildren = ((Component)root).GetComponentsInChildren<TMP_Text>(true);
		foreach (TMP_Text val in componentsInChildren)
		{
			if (!((Object)(object)val == (Object)null))
			{
				string text = val.text?.Trim();
				if (!string.IsNullOrWhiteSpace(text) && map.TryGetValue(text, out var value) && !string.Equals(val.text, value, StringComparison.Ordinal))
				{
					val.text = value;
				}
			}
		}
	}

	private Dictionary<string, string> BuildModConfigUiLocalizationMap(bool isChineseLanguage)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		AddUiLocalizationPair(dictionary, "Fog Climb", "Fog&ColdControl");
		AddUiLocalizationPair(dictionary, "FogClimb", "Fog&ColdControl");
		AddUiLocalizationPair(dictionary, "Fog&ColdControl", "Fog&ColdControl");
		AddUiLocalizationPair(dictionary, "FogAndColdControl", "Fog&ColdControl");
		AddUiLocalizationPair(dictionary, "毒雾攀登", GetLocalizedModDisplayName(isChineseLanguage));
		foreach (string sectionName in (from ConfigKey configKey3 in Enum.GetValues(typeof(ConfigKey))
			select GetConfigSectionName(configKey3)).Concat(from ConfigKey configKey3 in Enum.GetValues(typeof(ConfigKey))
			select GetSectionName(configKey3, isChineseLanguage: true)).Concat(new string[2]
		{
			GetLegacyConfigSectionName(),
			GetLegacySectionName(isChineseLanguage: true)
		}).Distinct(StringComparer.Ordinal))
		{
			if (string.Equals(sectionName, GetLegacyConfigSectionName(), StringComparison.Ordinal) || string.Equals(sectionName, GetLegacySectionName(isChineseLanguage: true), StringComparison.Ordinal))
			{
				AddUiLocalizationPair(dictionary, sectionName, GetSectionName(ConfigKey.ModEnabled, isChineseLanguage));
				continue;
			}
			ConfigKey configKey = Enum.GetValues(typeof(ConfigKey)).Cast<ConfigKey>().FirstOrDefault((ConfigKey candidate) => string.Equals(sectionName, GetConfigSectionName(candidate), StringComparison.OrdinalIgnoreCase) || string.Equals(sectionName, GetSectionName(candidate, isChineseLanguage: true), StringComparison.Ordinal));
			AddUiLocalizationPair(dictionary, sectionName, GetSectionName(configKey, isChineseLanguage));
		}
		foreach (ConfigKey value in Enum.GetValues(typeof(ConfigKey)))
		{
			AddUiLocalizationPair(dictionary, GetConfigKeyName(value), GetKeyName(value, isChineseLanguage));
			AddUiLocalizationPair(dictionary, GetKeyName(value, isChineseLanguage: true), GetKeyName(value, isChineseLanguage));
			AddUiLocalizationPair(dictionary, GetLocalizedDescription(value, isChineseLanguage: false), GetLocalizedDescription(value, isChineseLanguage));
			AddUiLocalizationPair(dictionary, GetLocalizedDescription(value, isChineseLanguage: true), GetLocalizedDescription(value, isChineseLanguage));
		}
		return dictionary;
	}

	private static void AddUiLocalizationPair(Dictionary<string, string> map, string source, string localized)
	{
		if (map != null && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(localized))
		{
			string text = source.Trim();
			string text2 = (map[text] = localized.Trim());
			map[text2] = text2;
			string key = text.Replace(" ", string.Empty);
			string key2 = text2.Replace(" ", string.Empty);
			if (!map.ContainsKey(key))
			{
				map[key] = text2;
			}
			if (!map.ContainsKey(key2))
			{
				map[key2] = text2;
			}
			map[text.ToUpperInvariant()] = text2;
			map[text2.ToUpperInvariant()] = text2;
		}
	}

	private static KeyCode GetCompassHotkey()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		return (KeyCode)(((??)CompassHotkey?.Value) ?? 103);
	}

	private static KeyCode GetFogPauseHotkey()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		return (KeyCode)(((??)FogPauseHotkey?.Value) ?? 121);
	}

	private unsafe static string GetFogPauseHotkeyLabel()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		KeyCode fogPauseHotkey = GetFogPauseHotkey();
		string text = ((object)(*(KeyCode*)(&fogPauseHotkey))/*cast due to .constrained prefix*/).ToString();
		if (string.IsNullOrWhiteSpace(text) || (int)fogPauseHotkey == 0)
		{
			return "None";
		}
		return text.ToUpperInvariant();
	}

	private unsafe static string GetCompassHotkeyLabel()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		KeyCode compassHotkey = GetCompassHotkey();
		string text = ((object)(*(KeyCode*)(&compassHotkey))/*cast due to .constrained prefix*/).ToString();
		if (string.IsNullOrWhiteSpace(text) || (int)compassHotkey == 0)
		{
			return "G";
		}
		return text.ToUpperInvariant();
	}

	private string GetCompassLobbyNoticeText(bool isChineseLanguage)
	{
		string text = "<color=#FF3B30>" + GetCompassHotkeyLabel() + "</color>";
		if (!isChineseLanguage)
		{
			return "Press " + text + " to spawn compass";
		}
		return "按 " + text + " 生成指南针";
	}

	private bool ShouldShowCompassLobbyNotice()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		if (IsModFeatureEnabled() && HasFogAuthority() && IsCompassFeatureEnabled() && (int)GetCompassHotkey() != 0)
		{
			return IsAirportScene(SceneManager.GetActiveScene());
		}
		return false;
	}

	private string GetCompassLobbyNoticeTextSafe(bool isChineseLanguage)
	{
		string text = "<color=#FF3B30>" + GetCompassHotkeyLabel() + "</color>";
		if (!isChineseLanguage)
		{
			return "Press " + text + " to spawn compass";
		}
		return "按 " + text + " 生成指南针";
	}

	private void UpdateCompassLobbyNotice()
	{
		if (!ShouldShowCompassLobbyNotice())
		{
			CleanupCompassLobbyNotice();
			return;
		}
		Canvas val = ResolveHudCanvas();
		if (!IsCanvasUsable(val))
		{
			CleanupCompassLobbyNotice();
			return;
		}
		if ((Object)(object)_compassLobbyNoticeRect == (Object)null || (Object)(object)_compassLobbyNoticeText == (Object)null || (Object)(object)((Transform)_compassLobbyNoticeRect).parent != (Object)(object)((Component)val).transform)
		{
			CreateCompassLobbyNotice(val);
			if ((Object)(object)_compassLobbyNoticeRect == (Object)null || (Object)(object)_compassLobbyNoticeText == (Object)null)
			{
				return;
			}
		}
		string compassLobbyNoticeTextSafe = GetCompassLobbyNoticeTextSafe(DetectChineseLanguage());
		if (!string.Equals(_lastCompassLobbyNoticeText, compassLobbyNoticeTextSafe, StringComparison.Ordinal))
		{
			_lastCompassLobbyNoticeText = compassLobbyNoticeTextSafe;
			((TMP_Text)_compassLobbyNoticeText).text = compassLobbyNoticeTextSafe;
		}
		ClampCompassLobbyNoticeToCanvas(val);
		((Component)_compassLobbyNoticeRect).gameObject.SetActive(true);
	}

	private void CreateCompassLobbyNotice(Canvas targetCanvas = null)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		CleanupCompassLobbyNotice();
		Canvas val = targetCanvas ?? ResolveHudCanvas();
		if (IsCanvasUsable(val))
		{
			GameObject val2 = new GameObject("FogClimbCompassLobbyNotice", new Type[1] { typeof(RectTransform) });
			_compassLobbyNoticeRect = val2.GetComponent<RectTransform>();
			((Transform)_compassLobbyNoticeRect).SetParent(((Component)val).transform, false);
			((Transform)_compassLobbyNoticeRect).SetAsLastSibling();
			_compassLobbyNoticeRect.sizeDelta = new Vector2(735f, 81f);
			_compassLobbyNoticeText = val2.AddComponent<TextMeshProUGUI>();
			ApplyCompassLobbyNoticeStyle(_compassLobbyNoticeText);
			_lastCompassLobbyNoticeText = GetCompassLobbyNoticeTextSafe(DetectChineseLanguage());
			((TMP_Text)_compassLobbyNoticeText).text = _lastCompassLobbyNoticeText;
			ClampCompassLobbyNoticeToCanvas(val);
		}
	}

	private void ApplyCompassLobbyNoticeStyle(TextMeshProUGUI target)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)target == (Object)null))
		{
			ApplyGameTextStyle(target, new Color(1f, 0.94f, 0.72f), 1.2f);
			((TMP_Text)target).fontStyle = (FontStyles)0;
			((TMP_Text)target).alignment = (TextAlignmentOptions)4100;
			((TMP_Text)target).textWrappingMode = (TextWrappingModes)0;
			((TMP_Text)target).overflowMode = (TextOverflowModes)0;
			((TMP_Text)target).lineSpacing = 1.125f;
		}
	}

	private void ClampCompassLobbyNoticeToCanvas(Canvas targetCanvas = null)
	{
		if ((Object)(object)_compassLobbyNoticeRect == (Object)null)
		{
			return;
		}
		Canvas val = targetCanvas ?? ResolveHudCanvas() ?? ((Component)_compassLobbyNoticeRect).GetComponentInParent<Canvas>();
		if (IsCanvasUsable(val))
		{
			if ((Object)(object)((Transform)_compassLobbyNoticeRect).parent != (Object)(object)((Component)val).transform)
			{
				((Transform)_compassLobbyNoticeRect).SetParent(((Component)val).transform, false);
			}
			ApplyRightMiddleAnchoredRect(_compassLobbyNoticeRect, 735f, 81f, 28f, 0f);
		}
	}

	private void CleanupCompassLobbyNotice()
	{
		if ((Object)(object)_compassLobbyNoticeRect != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)_compassLobbyNoticeRect).gameObject);
		}
		_compassLobbyNoticeRect = null;
		_compassLobbyNoticeText = null;
		_lastCompassLobbyNoticeText = string.Empty;
	}

	private bool ShouldShowFogUi(Canvas targetCanvas = null)
	{
		if (IsModFeatureEnabled())
		{
			ConfigEntry<bool> fogUiEnabled = FogUiEnabled;
			if (fogUiEnabled == null || fogUiEnabled.Value)
			{
				if (LoadingScreenHandler.loading || IsFogUiBlockedByOverlay() || !IsFogUiSceneAllowed() || ShouldHideFogUiForCurrentStage())
				{
					return false;
				}
				return IsCanvasUsable(targetCanvas ?? ResolveHudCanvas());
			}
		}
		return false;
	}

	private bool ShouldHideFogUiForCurrentStage()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Invalid comparison between Unknown and I4
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return false;
		}
		if (TryGetCurrentGameplaySegment(out var segment))
		{
			return (int)segment >= 3;
		}
		return _activeSyntheticFogSegmentId >= 3;
	}

	private static bool IsFogUiBlockedByOverlay()
	{
		GUIManager instance = GUIManager.instance;
		if ((Object)(object)instance != (Object)null && (Object)(object)instance.endScreen != (Object)null)
		{
			return ((MenuWindow)instance.endScreen).isOpen;
		}
		return false;
	}

	private static bool IsFogUiSceneAllowed()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		Scene activeScene = SceneManager.GetActiveScene();
		if (!((Scene)(ref activeScene)).IsValid())
		{
			return false;
		}
		if (!IsAirportScene(activeScene))
		{
			return IsGameplayFogScene(activeScene);
		}
		return true;
	}

	private static bool IsAirportScene(Scene scene)
	{
		return string.Equals(((Scene)(ref scene)).name, "Airport", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsGameplayFogScene(Scene scene)
	{
		string text = ((Scene)(ref scene)).name ?? string.Empty;
		if (text.IndexOf("Island", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Level_", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		return GameHandler.IsOnIsland;
	}

	private Canvas ResolveHudCanvas()
	{
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Invalid comparison between Unknown and I4
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		if (!IsFogUiSceneAllowed())
		{
			return null;
		}
		GUIManager instance = GUIManager.instance;
		if ((Object)(object)instance != (Object)null)
		{
			if (IsCanvasUsable(instance.hudCanvas))
			{
				return instance.hudCanvas;
			}
			TextMeshProUGUI val = (((Object)(object)instance.itemPromptMain != (Object)null) ? instance.itemPromptMain : instance.interactNameText);
			if ((Object)(object)val != (Object)null)
			{
				Canvas componentInParent = ((Component)val).GetComponentInParent<Canvas>();
				if ((Object)(object)componentInParent != (Object)null)
				{
					return componentInParent;
				}
			}
		}
		Canvas[] array = Object.FindObjectsByType<Canvas>((FindObjectsSortMode)0);
		Canvas val2 = null;
		int num = int.MinValue;
		float num2 = -1f;
		Canvas[] array2 = array;
		foreach (Canvas val3 in array2)
		{
			if (!((Object)(object)val3 == (Object)null) && val3.isRootCanvas && ((Behaviour)val3).isActiveAndEnabled && ((Component)val3).gameObject.activeInHierarchy && (int)val3.renderMode != 2)
			{
				RectTransform component = ((Component)val3).GetComponent<RectTransform>();
				float num3;
				if (!((Object)(object)component != (Object)null))
				{
					num3 = 0f;
				}
				else
				{
					Rect rect = component.rect;
					float width = ((Rect)(ref rect)).width;
					rect = component.rect;
					num3 = Mathf.Abs(width * ((Rect)(ref rect)).height);
				}
				float num4 = num3;
				if ((Object)(object)val2 == (Object)null || val3.sortingOrder > num || (val3.sortingOrder == num && num4 > num2))
				{
					val2 = val3;
					num = val3.sortingOrder;
					num2 = num4;
				}
			}
		}
		return val2;
	}

	private static bool IsCanvasUsable(Canvas canvas)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Invalid comparison between Unknown and I4
		if ((Object)(object)canvas != (Object)null && canvas.isRootCanvas && ((Behaviour)canvas).isActiveAndEnabled && ((Component)canvas).gameObject.activeInHierarchy)
		{
			return (int)canvas.renderMode != 2;
		}
		return false;
	}

	private bool NeedsFogUiRebuild(Canvas targetCanvas)
	{
		if ((Object)(object)_fogUiRect == (Object)null || (Object)(object)_fogUiText == (Object)null || (Object)(object)_fogUiEntriesRect == (Object)null || !IsCanvasUsable(targetCanvas))
		{
			return true;
		}
		if (IsCanvasUsable(((Component)_fogUiRect).GetComponentInParent<Canvas>()))
		{
			return (Object)(object)((Transform)_fogUiRect).parent != (Object)(object)((Component)targetCanvas).transform;
		}
		return true;
	}

	private TextMeshProUGUI ResolveFogStyleSource()
	{
		bool isChineseLanguage = DetectChineseLanguage();
		GUIManager instance = GUIManager.instance;
		if ((Object)(object)instance != (Object)null)
		{
			TextMeshProUGUI[] array = (TextMeshProUGUI[])(object)new TextMeshProUGUI[3] { instance.itemPromptMain, instance.interactNameText, instance.interactPromptText };
			TextMeshProUGUI[] array2 = array;
			foreach (TextMeshProUGUI val in array2)
			{
				if (IsTextSourceSuitable(val, isChineseLanguage))
				{
					return val;
				}
			}
			array2 = array;
			foreach (TextMeshProUGUI val2 in array2)
			{
				if ((Object)(object)val2 != (Object)null)
				{
					return val2;
				}
			}
		}
		TextMeshProUGUI val3 = FindLocalizedTextSource(isChineseLanguage);
		if ((Object)(object)val3 != (Object)null)
		{
			return val3;
		}
		return null;
	}

	private static TextMeshProUGUI FindLocalizedTextSource(bool isChineseLanguage)
	{
		TextMeshProUGUI result = null;
		int num = int.MinValue;
		TextMeshProUGUI[] array = Object.FindObjectsByType<TextMeshProUGUI>((FindObjectsSortMode)0);
		foreach (TextMeshProUGUI val in array)
		{
			if (!((Object)(object)val == (Object)null) && !((Object)(object)((TMP_Text)val).font == (Object)null) && ((Behaviour)val).isActiveAndEnabled && ((Component)val).gameObject.activeInHierarchy)
			{
				int num2 = 0;
				if (ContainsLocalizedCharacters(((TMP_Text)val).text, isChineseLanguage))
				{
					num2 += 100;
				}
				if ((Object)(object)((Component)val).GetComponentInParent<Canvas>() != (Object)null)
				{
					num2 += 20;
				}
				if ((Object)(object)((TMP_Text)val).fontSharedMaterial != (Object)null)
				{
					num2 += 5;
				}
				if (num2 > num)
				{
					num = num2;
					result = val;
				}
			}
		}
		if (num <= 0)
		{
			return null;
		}
		return result;
	}

	private static bool IsTextSourceSuitable(TextMeshProUGUI source, bool isChineseLanguage)
	{
		if ((Object)(object)source == (Object)null || (Object)(object)((TMP_Text)source).font == (Object)null)
		{
			return false;
		}
		if (isChineseLanguage)
		{
			return ContainsLocalizedCharacters(((TMP_Text)source).text, isChineseLanguage: true);
		}
		return true;
	}

	private static bool ContainsLocalizedCharacters(string value, bool isChineseLanguage)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		foreach (char c in value)
		{
			if (isChineseLanguage)
			{
				if (c >= '㐀' && c <= '鿿')
				{
					return true;
				}
			}
			else if (c <= '\u007f' && char.IsLetter(c))
			{
				return true;
			}
		}
		return false;
	}

	private void ApplyGameTextStyle(TextMeshProUGUI target, Color color, float sizeMultiplier = 1f)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)target == (Object)null))
		{
			TextMeshProUGUI val = ResolveFogStyleSource();
			if ((Object)(object)val != (Object)null)
			{
				((TMP_Text)target).font = ((TMP_Text)val).font;
				((TMP_Text)target).fontSharedMaterial = ((TMP_Text)val).fontSharedMaterial;
				((TMP_Text)target).fontStyle = ((TMP_Text)val).fontStyle;
				((TMP_Text)target).characterSpacing = ((TMP_Text)val).characterSpacing;
				((TMP_Text)target).wordSpacing = ((TMP_Text)val).wordSpacing;
				((TMP_Text)target).lineSpacing = ((TMP_Text)val).lineSpacing;
			}
			else if ((Object)(object)TMP_Settings.defaultFontAsset != (Object)null)
			{
				((TMP_Text)target).font = TMP_Settings.defaultFontAsset;
			}
			((TMP_Text)target).textWrappingMode = (TextWrappingModes)0;
			((TMP_Text)target).fontSize = 18f * sizeMultiplier;
			((Graphic)target).color = color;
			((TMP_Text)target).alignment = (TextAlignmentOptions)1025;
		}
	}

	private bool TryResolveNextCampfireTarget(out Vector3 targetPosition)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected I4, but got Unknown
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		targetPosition = Vector3.zero;
		if (!IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return false;
		}
		MapHandler instance = Singleton<MapHandler>.Instance;
		if ((Object)(object)instance == (Object)null || instance.segments == null || instance.segments.Length == 0)
		{
			return false;
		}
		for (int i = Mathf.Clamp((int)MapHandler.CurrentSegmentNumber, 0, instance.segments.Length - 1); i < instance.segments.Length; i++)
		{
			if (TryResolveSegmentCampfire(instance.segments[i], out var campfire) && !campfire.Lit)
			{
				targetPosition = ((Component)campfire).transform.position;
				return true;
			}
		}
		return false;
	}

	private static bool TryResolveSegmentCampfire(MapSegment mapSegment, out Campfire campfire)
	{
		campfire = null;
		GameObject val = ((mapSegment != null) ? mapSegment.segmentCampfire : null);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		campfire = val.GetComponentInChildren<Campfire>(true);
		return (Object)(object)campfire != (Object)null;
	}

	private bool IsCharacterInsideVisibleFog(Character character)
	{
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)character == (Object)null || (Object)(object)character.data == (Object)null || character.data.dead)
		{
			return false;
		}
		if ((Object)(object)_fogSphere != (Object)null && ((Component)_fogSphere).gameObject.activeInHierarchy && Mathf.Approximately(_fogSphere.ENABLE, 1f) && _fogSphere.currentSize > 30f && Vector3.Distance(_fogSphere.fogPoint, character.Center) > _fogSphere.currentSize)
		{
			return true;
		}
		float ratePerSecond;
		return IsCharacterInsideLegacyFog(character, out ratePerSecond);
	}

	private bool ShouldShowCampfireLocatorUi(Canvas targetCanvas = null)
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		if (IsModFeatureEnabled())
		{
			ConfigEntry<bool> fogUiEnabled = FogUiEnabled;
			if (fogUiEnabled == null || fogUiEnabled.Value)
			{
				ConfigEntry<bool> campfireLocatorUiEnabled = CampfireLocatorUiEnabled;
				if (campfireLocatorUiEnabled == null || campfireLocatorUiEnabled.Value)
				{
					if (LoadingScreenHandler.loading || IsFogUiBlockedByOverlay() || !IsGameplayFogScene(SceneManager.GetActiveScene()))
					{
						return false;
					}
					Character localCharacter = Character.localCharacter;
					if (!IsCharacterInsideVisibleFog(localCharacter))
					{
						return false;
					}
					Vector3 targetPosition;
					if (IsCanvasUsable(targetCanvas ?? ResolveHudCanvas()))
					{
						return TryResolveNextCampfireTarget(out targetPosition);
					}
					return false;
				}
			}
		}
		return false;
	}

	private bool NeedsCampfireLocatorUiRebuild(Canvas targetCanvas)
	{
		if ((Object)(object)_campfireLocatorUiRect == (Object)null || (Object)(object)_campfireLocatorDotRect == (Object)null || !IsCanvasUsable(targetCanvas))
		{
			return true;
		}
		if (IsCanvasUsable(((Component)_campfireLocatorUiRect).GetComponentInParent<Canvas>()))
		{
			return (Object)(object)((Transform)_campfireLocatorUiRect).parent != (Object)(object)((Component)targetCanvas).transform;
		}
		return true;
	}

	private void CreateCampfireLocatorUi(Canvas targetCanvas = null)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected O, but got Unknown
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_0209: Unknown result type (might be due to invalid IL or missing references)
		CleanupCampfireLocatorUi();
		Canvas val = targetCanvas ?? ResolveHudCanvas();
		if (!ShouldShowCampfireLocatorUi(val))
		{
			return;
		}
		try
		{
			GameObject val2 = new GameObject("FogClimbCampfireLocatorUI", new Type[1] { typeof(RectTransform) });
			_campfireLocatorUiRect = val2.GetComponent<RectTransform>();
			((Transform)_campfireLocatorUiRect).SetParent(((Component)val).transform, false);
			((Transform)_campfireLocatorUiRect).SetAsLastSibling();
			_campfireLocatorUiRect.sizeDelta = new Vector2(372f, 24f);
			GameObject val3 = new GameObject("Line", new Type[2]
			{
				typeof(RectTransform),
				typeof(Image)
			});
			RectTransform component = val3.GetComponent<RectTransform>();
			((Transform)component).SetParent((Transform)(object)_campfireLocatorUiRect, false);
			component.anchorMin = new Vector2(0.5f, 0.5f);
			component.anchorMax = new Vector2(0.5f, 0.5f);
			component.pivot = new Vector2(0.5f, 0.5f);
			component.anchoredPosition = Vector2.zero;
			component.sizeDelta = new Vector2(360f, 2f);
			((Graphic)val3.GetComponent<Image>()).color = CampfireLocatorLineColor;
			GameObject val4 = new GameObject("Dot", new Type[2]
			{
				typeof(RectTransform),
				typeof(Image)
			});
			_campfireLocatorDotRect = val4.GetComponent<RectTransform>();
			((Transform)_campfireLocatorDotRect).SetParent((Transform)(object)_campfireLocatorUiRect, false);
			_campfireLocatorDotRect.anchorMin = new Vector2(0.5f, 0.5f);
			_campfireLocatorDotRect.anchorMax = new Vector2(0.5f, 0.5f);
			_campfireLocatorDotRect.pivot = new Vector2(0.5f, 0.5f);
			_campfireLocatorDotRect.anchoredPosition = Vector2.zero;
			_campfireLocatorDotRect.sizeDelta = new Vector2(18f, 18f);
			Image component2 = val4.GetComponent<Image>();
			((Graphic)component2).raycastTarget = false;
			component2.preserveAspect = true;
			component2.sprite = GetCampfireLocatorDotSprite();
			((Graphic)component2).color = Color.white;
			_campfireLocatorCurrentDotX = 0f;
			ClampCampfireLocatorUiToCanvas(val);
			SetCampfireLocatorUiVisible(visible: true);
		}
		catch
		{
			CleanupCampfireLocatorUi();
		}
	}

	private void UpdateCampfireLocatorUi()
	{
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		if ((FogUiEnabled != null && !FogUiEnabled.Value) || (CampfireLocatorUiEnabled != null && !CampfireLocatorUiEnabled.Value))
		{
			SetCampfireLocatorUiVisible(visible: false);
			return;
		}
		Canvas targetCanvas = ResolveHudCanvas();
		if (!ShouldShowCampfireLocatorUi(targetCanvas))
		{
			SetCampfireLocatorUiVisible(visible: false);
			return;
		}
		if (NeedsCampfireLocatorUiRebuild(targetCanvas))
		{
			CreateCampfireLocatorUi(targetCanvas);
			if ((Object)(object)_campfireLocatorUiRect == (Object)null || (Object)(object)_campfireLocatorDotRect == (Object)null)
			{
				return;
			}
		}
		if (!TryResolveNextCampfireTarget(out var targetPosition))
		{
			SetCampfireLocatorUiVisible(visible: false);
			return;
		}
		SetCampfireLocatorUiVisible(visible: true);
		ClampCampfireLocatorUiToCanvas(targetCanvas);
		UpdateCampfireLocatorDot(targetPosition);
	}

	private void UpdateCampfireLocatorDot(Vector3 targetPosition)
	{
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_campfireLocatorDotRect == (Object)null)
		{
			return;
		}
		float num = Mathf.Max(0f, 171f);
		float num2 = 0f;
		Camera val = ResolveActiveViewCamera();
		if ((Object)(object)val != (Object)null)
		{
			Vector3 val2 = val.WorldToViewportPoint(targetPosition);
			if (val2.z > 0.05f)
			{
				num2 = Mathf.Lerp(0f - num, num, Mathf.Clamp01(val2.x));
			}
			else
			{
				Vector3 val3 = Vector3.ProjectOnPlane(targetPosition - ((Component)val).transform.position, Vector3.up);
				Vector3 val4 = Vector3.ProjectOnPlane(((Component)val).transform.right, Vector3.up);
				float num3 = 0f;
				if (((Vector3)(ref val3)).sqrMagnitude >= 0.01f && ((Vector3)(ref val4)).sqrMagnitude >= 0.01f)
				{
					num3 = Vector3.Dot(((Vector3)(ref val3)).normalized, ((Vector3)(ref val4)).normalized);
				}
				num2 = ((num3 >= 0f) ? num : (0f - num));
			}
		}
		float num4 = 1f - Mathf.Exp(0f - 12f * Mathf.Max(Time.unscaledDeltaTime, 0f));
		_campfireLocatorCurrentDotX = Mathf.Lerp(_campfireLocatorCurrentDotX, num2, num4);
		_campfireLocatorDotRect.anchoredPosition = new Vector2(_campfireLocatorCurrentDotX, 0f);
	}

	private static Camera ResolveActiveViewCamera()
	{
		Camera main = Camera.main;
		if ((Object)(object)main != (Object)null && ((Behaviour)main).isActiveAndEnabled && ((Component)main).gameObject.activeInHierarchy)
		{
			return main;
		}
		return Object.FindObjectsByType<Camera>((FindObjectsSortMode)0).FirstOrDefault((Camera candidate) => (Object)(object)candidate != (Object)null && ((Behaviour)candidate).isActiveAndEnabled && ((Component)candidate).gameObject.activeInHierarchy);
	}

	private void CreateFogUi(Canvas targetCanvas = null)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Expected O, but got Unknown
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Expected O, but got Unknown
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_0273: Unknown result type (might be due to invalid IL or missing references)
		//IL_027d: Expected O, but got Unknown
		CleanupFogUi();
		Canvas val = targetCanvas ?? ResolveHudCanvas();
		if (!ShouldShowFogUi(val))
		{
			return;
		}
		try
		{
			GameObject val2 = new GameObject("FogClimbUI", new Type[1] { typeof(RectTransform) });
			_fogUiRect = val2.GetComponent<RectTransform>();
			((Transform)_fogUiRect).SetParent(((Component)val).transform, false);
			_fogUiRect.anchorMin = Vector2.zero;
			_fogUiRect.anchorMax = Vector2.zero;
			_fogUiRect.pivot = Vector2.zero;
			_fogUiRect.sizeDelta = new Vector2(1360f, 34f);
			((Transform)_fogUiRect).SetAsLastSibling();
			GameObject val3 = new GameObject("FogClimbUILabel", new Type[1] { typeof(RectTransform) });
			RectTransform component = val3.GetComponent<RectTransform>();
			((Transform)component).SetParent((Transform)(object)_fogUiRect, false);
			component.anchorMin = Vector2.zero;
			component.anchorMax = Vector2.zero;
			component.pivot = Vector2.zero;
			component.anchoredPosition = Vector2.zero;
			component.sizeDelta = new Vector2(1360f, 34f);
			_fogUiText = val3.AddComponent<TextMeshProUGUI>();
			ApplyGameTextStyle(_fogUiText, Color.white);
			((TMP_Text)_fogUiText).richText = true;
			((TMP_Text)_fogUiText).alignment = (TextAlignmentOptions)4097;
			((TMP_Text)_fogUiText).margin = new Vector4(10f, 0f, 10f, 0f);
			((TMP_Text)_fogUiText).text = string.Empty;
			((Behaviour)_fogUiText).enabled = false;
			GameObject val4 = new GameObject("FogClimbUIEntries", new Type[2]
			{
				typeof(RectTransform),
				typeof(HorizontalLayoutGroup)
			});
			_fogUiEntriesRect = val4.GetComponent<RectTransform>();
			((Transform)_fogUiEntriesRect).SetParent((Transform)(object)_fogUiRect, false);
			_fogUiEntriesRect.anchorMin = Vector2.zero;
			_fogUiEntriesRect.anchorMax = Vector2.zero;
			_fogUiEntriesRect.pivot = Vector2.zero;
			_fogUiEntriesRect.anchoredPosition = Vector2.zero;
			_fogUiEntriesRect.sizeDelta = new Vector2(1360f, 34f);
			HorizontalLayoutGroup component2 = val4.GetComponent<HorizontalLayoutGroup>();
			((HorizontalOrVerticalLayoutGroup)component2).childControlWidth = true;
			((HorizontalOrVerticalLayoutGroup)component2).childControlHeight = true;
			((HorizontalOrVerticalLayoutGroup)component2).childForceExpandWidth = false;
			((HorizontalOrVerticalLayoutGroup)component2).childForceExpandHeight = false;
			((HorizontalOrVerticalLayoutGroup)component2).spacing = 14f;
			((LayoutGroup)component2).padding = new RectOffset(10, 10, 0, 0);
			_lastFogUiRenderedText = string.Empty;
			ClampFogUiToCanvas(val);
			SetFogUiVisible(visible: true);
		}
		catch
		{
			CleanupFogUi();
		}
	}

	private void UpdateFogUi()
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		if (FogUiEnabled != null && !FogUiEnabled.Value)
		{
			SetFogUiVisible(visible: false);
			return;
		}
		Canvas targetCanvas = ResolveHudCanvas();
		if (!ShouldShowFogUi(targetCanvas))
		{
			SetFogUiVisible(visible: false);
			return;
		}
		if (NeedsFogUiRebuild(targetCanvas))
		{
			CreateFogUi(targetCanvas);
			if ((Object)(object)_fogUiRect == (Object)null || (Object)(object)_fogUiText == (Object)null)
			{
				return;
			}
		}
		SetFogUiVisible(visible: true);
		ClampFogUiToCanvas(targetCanvas);
		bool flag = DetectChineseLanguage();
		bool flag2 = IsAirportScene(SceneManager.GetActiveScene());
		if ((Object)(object)_fogUiEntriesRect != (Object)null)
		{
			PopulateFogUiEntries(flag, flag2);
			return;
		}
		if (IsReadOnlyFogUiViewer())
		{
			SetFogUiText(BuildReadOnlyFogUiText(flag));
			return;
		}
		float num = (_fogPaused ? 0f : (((Object)(object)_orbFogHandler != (Object)null) ? _orbFogHandler.speed : (FogSpeed?.Value ?? 0.4f)));
		string text = BuildFogUiBadge("ST", "#A8E0A0", GetFogRuntimeStateLabel(flag));
		string text2 = BuildFogUiBadge("SPD", "#8EC5FF", Colorize(num.ToString("0.##"), "#D6F1FF"));
		if (flag2)
		{
			SetFogUiText(BuildFogUiLine(text, text2, GetCompactFogHandlingBadge(), GetCompactNightBadge()));
			return;
		}
		string compactTimingBadge = GetCompactTimingBadge();
		string compactDistanceBadge = GetCompactDistanceBadge();
		string compactEtaBadge = GetCompactEtaBadge(flag);
		string compactDirectStartBadge = GetCompactDirectStartBadge(flag);
		if (!string.IsNullOrWhiteSpace(compactTimingBadge))
		{
			SetFogUiText(BuildFogUiLine(text, text2, compactTimingBadge, compactDirectStartBadge));
			return;
		}
		SetFogUiText(BuildFogUiLine(text, text2, compactDistanceBadge, compactEtaBadge, compactDirectStartBadge));
		if (!ShouldUseLegacyFogUiLayout())
		{
			return;
		}
		if (IsReadOnlyFogUiViewer())
		{
			SetFogUiText(BuildReadOnlyFogUiText(flag));
			return;
		}
		float num2 = (_fogPaused ? 0f : (((Object)(object)_orbFogHandler != (Object)null) ? _orbFogHandler.speed : (FogSpeed?.Value ?? 0.4f)));
		string text3 = (flag ? "毒雾速度:" : "FogSpeed:");
		string text4 = (flag ? "当前状态:" : "State:");
		string text5 = Colorize(text3, "#8EC5FF") + " " + Colorize(num2.ToString("0.##"), "#D6F1FF");
		string text6 = Colorize(text4, "#A8E0A0") + " " + GetFogRuntimeStateLabel(flag);
		string fogTimingLabel = GetFogTimingLabel(flag);
		string resolvedFogDistanceLabel = GetResolvedFogDistanceLabel(flag);
		string resolvedFogArrivalEtaLabel = GetResolvedFogArrivalEtaLabel(flag);
		string text7 = BuildFogUiLine(text5, text6, fogTimingLabel, resolvedFogDistanceLabel, resolvedFogArrivalEtaLabel);
		string vanillaProgressStartUiLabel = GetVanillaProgressStartUiLabel(flag);
		if (!string.IsNullOrWhiteSpace(vanillaProgressStartUiLabel))
		{
			text7 = BuildFogUiLine(text7, vanillaProgressStartUiLabel);
		}
		if (!flag2)
		{
			SetFogUiText(text7);
			return;
		}
		string fogHandlingUiLabel = GetFogHandlingUiLabel(flag);
		string nightColdUiLabel = GetNightColdUiLabel(flag);
		SetFogUiText(BuildFogUiLine(text7, fogHandlingUiLabel, nightColdUiLabel));
	}

	private string BuildReadOnlyFogUiText(bool isChineseLanguage)
	{
		string text = BuildFogUiBadge("ST", "#A8E0A0", GetGuestFogRuntimeStateLabel(isChineseLanguage));
		return BuildFogUiLine(text, GetCompactDistanceBadge(), GetCompactEtaBadge(isChineseLanguage));
	}

	private static string BuildFogUiLine(params string[] parts)
	{
		if (parts == null || parts.Length == 0)
		{
			return string.Empty;
		}
		return string.Join("  |  ", from part in parts
			where !string.IsNullOrWhiteSpace(part)
			select part.Trim());
	}

	private static bool ShouldUseLegacyFogUiLayout()
	{
		return false;
	}

	private void PopulateFogUiEntries(bool isChineseLanguage, bool isLobbyScene)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		_fogUiDisplayEntries.Clear();
		if (IsReadOnlyFogUiViewer())
		{
			_fogUiDisplayEntries.Add(new FogUiDisplayEntry(FogUiIconKind.State, ParseColorOrDefault("#A8E0A0"), GetGuestFogStateUiEntryText(isChineseLanguage)));
			TryAppendFogDistanceEntry(_fogUiDisplayEntries, isChineseLanguage);
			TryAppendFogEtaEntry(_fogUiDisplayEntries, isChineseLanguage);
			ApplyFogUiEntries();
			return;
		}
		_fogUiDisplayEntries.Add(new FogUiDisplayEntry(FogUiIconKind.State, ParseColorOrDefault("#A8E0A0"), GetFogStateUiEntryText(isChineseLanguage)));
		_fogUiDisplayEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Speed, ParseColorOrDefault("#8EC5FF"), GetFogSpeedUiEntryText(isChineseLanguage)));
		if (isLobbyScene)
		{
			_fogUiDisplayEntries.Add(new FogUiDisplayEntry(FogUiIconKind.FogHandling, ParseColorOrDefault("#B7C0CC"), GetFogHandlingUiEntryText(isChineseLanguage)));
			_fogUiDisplayEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Night, ParseColorOrDefault("#B7C0CC"), GetNightColdUiEntryText(isChineseLanguage)));
			ApplyFogUiEntries();
		}
		else if (TryAppendFogTimingEntry(_fogUiDisplayEntries, isChineseLanguage))
		{
			TryAppendDirectStartEntry(_fogUiDisplayEntries, isChineseLanguage);
			ApplyFogUiEntries();
		}
		else
		{
			TryAppendFogDistanceEntry(_fogUiDisplayEntries, isChineseLanguage);
			TryAppendFogEtaEntry(_fogUiDisplayEntries, isChineseLanguage);
			TryAppendDirectStartEntry(_fogUiDisplayEntries, isChineseLanguage);
			ApplyFogUiEntries();
		}
	}

	private bool TryAppendFogTimingEntry(List<FogUiDisplayEntry> targetEntries, bool isChineseLanguage)
	{
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Max(FogDelay?.Value ?? 900f, 0f);
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted)
		{
			return false;
		}
		float num2 = Mathf.Max(10f - _fogHiddenBufferTimer, 0f);
		if (num2 > 0.05f)
		{
			float t = Mathf.Clamp01(1f - num2 / 10f);
			string hexColor = LerpHexColor("#FF8A5B", "#FF2D2D", t);
			targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Buffer, ParseColorOrDefault(hexColor), GetFogTimingUiEntryText(isChineseLanguage)));
			return true;
		}
		float num3 = Mathf.Max(num - _fogDelayTimer, 0f);
		if (num3 <= 0.05f)
		{
			return false;
		}
		float t2 = ((num <= 0.01f) ? 1f : Mathf.Clamp01(1f - num3 / num));
		string hexColor2 = LerpHexColor("#FF8A8A", "#7A0000", t2);
		targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Delay, ParseColorOrDefault(hexColor2), GetFogTimingUiEntryText(isChineseLanguage)));
		return true;
	}

	private void TryAppendFogDistanceEntry(List<FogUiDisplayEntry> targetEntries, bool isChineseLanguage)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		if (TryGetFogArrivalRemainingDistance(out var remainingDistance) && !(remainingDistance <= 0.05f))
		{
			GetFogDistanceColors(remainingDistance, out var labelColor, out var _);
			targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Distance, ParseColorOrDefault(labelColor), GetFogDistanceUiEntryText(isChineseLanguage)));
		}
	}

	private void TryAppendFogEtaEntry(List<FogUiDisplayEntry> targetEntries, bool isChineseLanguage)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)_orbFogHandler == (Object)null) && !IsFogRemovedInCurrentScene() && !_fogPaused && _orbFogHandler.isMoving && !_orbFogHandler.hasArrived)
		{
			if (!TryGetFogArrivalEtaSeconds(out var etaSeconds))
			{
				targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Eta, ParseColorOrDefault("#B7C0CC"), GetFogEtaUiEntryText(isChineseLanguage)));
			}
			else if (!(etaSeconds <= 0.05f))
			{
				GetFogEtaColors(QuantizeFogEtaSeconds(etaSeconds), out var labelColor, out var _);
				targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Eta, ParseColorOrDefault(labelColor), GetFogEtaUiEntryText(isChineseLanguage)));
			}
		}
	}

	private void TryAppendDirectStartEntry(List<FogUiDisplayEntry> targetEntries, bool isChineseLanguage)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || IsAirportScene(SceneManager.GetActiveScene()) || IsFogRemovedInCurrentScene() || _fogPaused || _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted || !ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return;
		}
		switch (GetVanillaProgressStartUiState())
		{
		case VanillaProgressStartUiState.Unavailable:
			targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Direct, ParseColorOrDefault("#B7C0CC"), GetDirectStartUiEntryText(isChineseLanguage)));
			break;
		case VanillaProgressStartUiState.Tracking:
		{
			if (!TryGetVanillaProgressStartProgress(_orbFogHandler, out var passedCount, out var totalCount, out var _, out var _))
			{
				targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Direct, ParseColorOrDefault("#B7C0CC"), GetDirectStartUiEntryText(isChineseLanguage)));
				break;
			}
			float t = ((totalCount <= 0) ? 0f : Mathf.Clamp01((float)passedCount / (float)totalCount));
			string hexColor = LerpHexColor("#E2EAF3", "#B5FFB8", t);
			targetEntries.Add(new FogUiDisplayEntry(FogUiIconKind.Direct, ParseColorOrDefault(hexColor), GetDirectStartUiEntryText(isChineseLanguage)));
			break;
		}
		}
	}

	private void ApplyFogUiEntries()
	{
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_fogUiEntriesRect == (Object)null)
		{
			return;
		}
		EnsureFogUiEntryViewCount(_fogUiDisplayEntries.Count);
		for (int i = 0; i < _fogUiEntryViews.Count; i++)
		{
			FogUiEntryView fogUiEntryView = _fogUiEntryViews[i];
			bool flag = i < _fogUiDisplayEntries.Count;
			if ((Object)(object)fogUiEntryView?.Root == (Object)null)
			{
				continue;
			}
			((Component)fogUiEntryView.Root).gameObject.SetActive(flag);
			if (flag)
			{
				FogUiDisplayEntry fogUiDisplayEntry = _fogUiDisplayEntries[i];
				if ((Object)(object)fogUiEntryView.Icon != (Object)null && (fogUiEntryView.LastIconKind != fogUiDisplayEntry.Kind || fogUiEntryView.LastIconColor != fogUiDisplayEntry.IconColor))
				{
					fogUiEntryView.Icon.sprite = GetFogUiIconSprite(fogUiDisplayEntry.Kind);
					((Graphic)fogUiEntryView.Icon).color = fogUiDisplayEntry.IconColor;
					fogUiEntryView.LastIconKind = fogUiDisplayEntry.Kind;
					fogUiEntryView.LastIconColor = fogUiDisplayEntry.IconColor;
				}
				if ((Object)(object)fogUiEntryView.Text != (Object)null && !string.Equals(fogUiEntryView.LastText, fogUiDisplayEntry.Text, StringComparison.Ordinal))
				{
					fogUiEntryView.LastText = fogUiDisplayEntry.Text;
					((TMP_Text)fogUiEntryView.Text).text = fogUiDisplayEntry.Text;
				}
			}
		}
	}

	private static string BuildFogUiBadge(string tag, string tagColor, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = (string.IsNullOrWhiteSpace(tag) ? string.Empty : ("[" + tag.Trim().ToUpperInvariant() + "]"));
		if (string.IsNullOrEmpty(text))
		{
			return value.Trim();
		}
		return Colorize(text, tagColor) + " " + value.Trim();
	}

	private static Color ParseColorOrDefault(string hexColor, string fallbackHex = "#FFFFFF")
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		if (TryParseHtmlColor(hexColor, out var color))
		{
			return color;
		}
		if (TryParseHtmlColor(fallbackHex, out var color2))
		{
			return color2;
		}
		return Color.white;
	}

	private static string GetCompactFogHandlingValue()
	{
		if (!IsFogColdSuppressionEnabled())
		{
			return Colorize("VAN", "#FFC37D");
		}
		return Colorize("BLK", "#9FFFA8");
	}

	private static string GetCompactNightValue()
	{
		if (!IsNightColdFeatureEnabled())
		{
			return Colorize("OFF", "#FFB3B3");
		}
		return Colorize("ON", "#9FFFA8");
	}

	private string GetCompactTimingBadge()
	{
		float num = Mathf.Max(FogDelay?.Value ?? 900f, 0f);
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted)
		{
			return string.Empty;
		}
		float num2 = Mathf.Max(10f - _fogHiddenBufferTimer, 0f);
		if (num2 > 0.05f)
		{
			float t = Mathf.Clamp01(1f - num2 / 10f);
			string tagColor = LerpHexColor("#FF8A5B", "#FF2D2D", t);
			string hexColor = LerpHexColor("#FFD2B8", "#FFC0C0", t);
			return BuildFogUiBadge("BUF", tagColor, Colorize($"{num2:F1}s", hexColor));
		}
		float num3 = Mathf.Max(num - _fogDelayTimer, 0f);
		if (num3 <= 0.05f)
		{
			return string.Empty;
		}
		float t2 = ((num <= 0.01f) ? 1f : Mathf.Clamp01(1f - num3 / num));
		string tagColor2 = LerpHexColor("#FF8A8A", "#7A0000", t2);
		string hexColor2 = LerpHexColor("#FFD6D6", "#B31212", t2);
		return BuildFogUiBadge("DLY", tagColor2, Colorize($"{num3:F1}s", hexColor2));
	}

	private string GetCompactDistanceBadge()
	{
		if (!TryGetFogArrivalRemainingDistance(out var remainingDistance) || remainingDistance <= 0.05f)
		{
			return string.Empty;
		}
		GetFogDistanceColors(remainingDistance, out var labelColor, out var valueColor);
		return BuildFogUiBadge("DIS", labelColor, Colorize(FormatFogDistanceValue(remainingDistance), valueColor));
	}

	private string GetCompactEtaBadge(bool isChineseLanguage)
	{
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			return string.Empty;
		}
		if (!TryGetFogArrivalEtaSeconds(out var etaSeconds))
		{
			return BuildFogUiBadge("ETA", "#B7C0CC", Colorize(isChineseLanguage ? "估算中" : "...", "#E2EAF3"));
		}
		if (etaSeconds <= 0.05f)
		{
			return string.Empty;
		}
		float num = QuantizeFogEtaSeconds(etaSeconds);
		GetFogEtaColors(num, out var labelColor, out var valueColor);
		return BuildFogUiBadge("ETA", labelColor, Colorize($"{num:F1}s", valueColor));
	}

	private string GetCompactDirectStartBadge(bool isChineseLanguage)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || IsAirportScene(SceneManager.GetActiveScene()) || IsFogRemovedInCurrentScene() || _fogPaused)
		{
			return string.Empty;
		}
		if (_orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted || !ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return string.Empty;
		}
		switch (GetVanillaProgressStartUiState())
		{
		case VanillaProgressStartUiState.Unavailable:
			return BuildFogUiBadge("DIR", "#B7C0CC", Colorize("N/A", "#FFB3B3"));
		default:
			return string.Empty;
		case VanillaProgressStartUiState.Tracking:
		{
			if (!TryGetVanillaProgressStartProgress(_orbFogHandler, out var passedCount, out var totalCount, out var _, out var _))
			{
				return BuildFogUiBadge("DIR", "#B7C0CC", Colorize(isChineseLanguage ? "进度" : "PRG", "#E2EAF3"));
			}
			float t = ((totalCount <= 0) ? 0f : Mathf.Clamp01((float)passedCount / (float)totalCount));
			string hexColor = LerpHexColor("#E2EAF3", "#B5FFB8", t);
			return BuildFogUiBadge("DIR", "#B7C0CC", Colorize($"{passedCount}/{totalCount}", hexColor));
		}
		}
	}

	private static string GetCompactPauseBadge()
	{
		return BuildFogUiBadge("PAU", "#B7C0CC", Colorize(GetFogPauseHotkeyLabel(), "#E2EAF3"));
	}

	private static string GetCompactFogHandlingBadge()
	{
		if (IsFogColdSuppressionEnabled())
		{
			return BuildFogUiBadge("FOG", "#B7C0CC", Colorize("BLK", "#9FFFA8"));
		}
		return BuildFogUiBadge("FOG", "#B7C0CC", Colorize("VAN", "#FFC37D"));
	}

	private static string GetCompactNightBadge()
	{
		if (IsNightColdFeatureEnabled())
		{
			return BuildFogUiBadge("NGT", "#B7C0CC", Colorize("ON", "#9FFFA8"));
		}
		return BuildFogUiBadge("NGT", "#B7C0CC", Colorize("OFF", "#FFB3B3"));
	}

	private string GetGuestFogRuntimeStateLabel(bool isChineseLanguage)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled())
		{
			return Colorize(isChineseLanguage ? "关闭" : "OFF", "#FFC37D");
		}
		if (IsAirportScene(SceneManager.GetActiveScene()))
		{
			return Colorize(isChineseLanguage ? "大厅" : "LOBBY", "#A3D2FF");
		}
		if (IsFogRemovedInCurrentScene())
		{
			return Colorize(isChineseLanguage ? "已禁用" : "DISABLED", "#FFC37D");
		}
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return Colorize(isChineseLanguage ? "同步中" : "SYNCING", "#A3D2FF");
		}
		if (_orbFogHandler.isMoving)
		{
			return Colorize(isChineseLanguage ? "运行中" : "RUNNING", "#B5FFB8");
		}
		if (_orbFogHandler.hasArrived)
		{
			return Colorize(isChineseLanguage ? "已到达" : "ARRIVED", "#B5FFB8");
		}
		if (ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return Colorize(isChineseLanguage ? "等待中" : "WAITING", "#FFE08A");
		}
		return Colorize(isChineseLanguage ? "待机" : "IDLE", "#A3D2FF");
	}

	private string GetFogTimingLabel(bool isChineseLanguage)
	{
		float num = Mathf.Max(FogDelay?.Value ?? 900f, 0f);
		string text = (isChineseLanguage ? "毒雾延迟:" : "Fog Delay:");
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return Colorize(text, "#F2C75C") + " " + Colorize($"{num:F1}s", "#FFE8A3");
		}
		if (IsFogRemovedInCurrentScene())
		{
			return string.Empty;
		}
		if (_fogPaused)
		{
			return string.Empty;
		}
		if (_orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted)
		{
			return string.Empty;
		}
		float num2 = Mathf.Max(10f - _fogHiddenBufferTimer, 0f);
		float num3 = Mathf.Max(num - _fogDelayTimer, 0f);
		if (num2 > 0.05f)
		{
			float t = Mathf.Clamp01(1f - num2 / 10f);
			string hexColor = LerpHexColor("#FF8A5B", "#FF2D2D", t);
			string hexColor2 = LerpHexColor("#FFD2B8", "#FFC0C0", t);
			return Colorize(isChineseLanguage ? "缓冲:" : "Buffer:", hexColor) + " " + Colorize($"{num2:F1}s", hexColor2);
		}
		if (num3 <= 0.05f)
		{
			return string.Empty;
		}
		float t2 = ((num <= 0.01f) ? 1f : Mathf.Clamp01(1f - num3 / num));
		string hexColor3 = LerpHexColor("#FF8A8A", "#7A0000", t2);
		string hexColor4 = LerpHexColor("#FFD6D6", "#B31212", t2);
		return Colorize(text, hexColor3) + " " + Colorize($"{num3:F1}s", hexColor4);
	}

	private string GetFogArrivalEtaLabel(bool isChineseLanguage)
	{
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			return string.Empty;
		}
		string text = (isChineseLanguage ? "到达:" : "ETA:");
		if (!TryGetFogArrivalEtaSeconds(out var etaSeconds))
		{
			string text2 = (isChineseLanguage ? "估算中" : "Estimating");
			return Colorize(text, "#B7C0CC") + " " + Colorize(text2, "#E2EAF3");
		}
		if (etaSeconds <= 0.05f)
		{
			return string.Empty;
		}
		GetFogEtaColors(etaSeconds, out var labelColor, out var valueColor);
		return Colorize(text, labelColor) + " " + Colorize($"{etaSeconds:F1}s", valueColor);
	}

	private string GetFogDistanceLabel(bool isChineseLanguage)
	{
		if (!TryGetFogArrivalRemainingDistance(out var remainingDistance))
		{
			return string.Empty;
		}
		if (remainingDistance <= 0.05f)
		{
			return string.Empty;
		}
		GetFogDistanceColors(remainingDistance, out var labelColor, out var valueColor);
		return Colorize(isChineseLanguage ? "距离:" : "Distance:", labelColor) + " " + Colorize(FormatFogDistanceValue(remainingDistance), valueColor);
	}

	private string GetResolvedFogArrivalEtaLabel(bool isChineseLanguage)
	{
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			return string.Empty;
		}
		string text = (isChineseLanguage ? "到达:" : "ETA:");
		if (!TryGetFogArrivalEtaSeconds(out var etaSeconds))
		{
			string text2 = (isChineseLanguage ? "估算中" : "Estimating");
			return Colorize(text, "#B7C0CC") + " " + Colorize(text2, "#E2EAF3");
		}
		if (etaSeconds <= 0.05f)
		{
			return string.Empty;
		}
		float num = QuantizeFogEtaSeconds(etaSeconds);
		GetFogEtaColors(num, out var labelColor, out var valueColor);
		return Colorize(text, labelColor) + " " + Colorize($"{num:F1}s", valueColor);
	}

	private string GetResolvedFogDistanceLabel(bool isChineseLanguage)
	{
		if (!TryGetFogArrivalRemainingDistance(out var remainingDistance))
		{
			return string.Empty;
		}
		if (remainingDistance <= 0.05f)
		{
			return string.Empty;
		}
		GetFogDistanceColors(remainingDistance, out var labelColor, out var valueColor);
		return Colorize(isChineseLanguage ? "距离:" : "Distance:", labelColor) + " " + Colorize(FormatFogDistanceValue(remainingDistance), valueColor);
	}

	private static void GetFogEtaColors(float etaSeconds, out string labelColor, out string valueColor)
	{
		float t = Mathf.Clamp01(1f - etaSeconds / 90f);
		labelColor = LerpHexColor("#79E2D0", "#FFB864", t);
		valueColor = LerpHexColor("#D9FFF5", "#FFE6BF", t);
		if (!(etaSeconds > 45f))
		{
			float t2 = Mathf.Clamp01(1f - etaSeconds / 45f);
			labelColor = LerpHexColor(labelColor, "#FF2D2D", t2);
			valueColor = LerpHexColor(valueColor, "#FFC0C0", t2);
		}
	}

	private static void GetFogDistanceColors(float remainingDistance, out string labelColor, out string valueColor)
	{
		float t = Mathf.Clamp01(1f - remainingDistance / 300f);
		labelColor = LerpHexColor("#79E2D0", "#FFB864", t);
		valueColor = LerpHexColor("#D9FFF5", "#FFE6BF", t);
		if (!(remainingDistance > 120f))
		{
			float t2 = Mathf.Clamp01(1f - remainingDistance / 120f);
			labelColor = LerpHexColor(labelColor, "#FF2D2D", t2);
			valueColor = LerpHexColor(valueColor, "#FFC0C0", t2);
		}
	}

	private static string FormatFogDistanceValue(float remainingDistance)
	{
		return $"{Mathf.RoundToInt(Mathf.Max(remainingDistance, 0f))}m";
	}

	private static float QuantizeFogEtaSeconds(float etaSeconds)
	{
		if (etaSeconds <= 0f)
		{
			return 0f;
		}
		float num = Mathf.Max(0.5f, 0.1f);
		return Mathf.Ceil(etaSeconds / num) * num;
	}

	private string GetVanillaProgressStartUiLabel(bool isChineseLanguage)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || IsAirportScene(SceneManager.GetActiveScene()) || IsFogRemovedInCurrentScene() || _fogPaused)
		{
			return string.Empty;
		}
		if (_orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted || !ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return string.Empty;
		}
		string text = (isChineseLanguage ? "切入:" : "DirectStart:");
		switch (GetVanillaProgressStartUiState())
		{
		case VanillaProgressStartUiState.Unavailable:
			return Colorize(text, "#B7C0CC") + " " + Colorize(isChineseLanguage ? "当前段无原版阈值" : "N/A", "#FFB3B3");
		default:
			return string.Empty;
		case VanillaProgressStartUiState.Tracking:
		{
			if (!TryGetVanillaProgressStartProgress(_orbFogHandler, out var passedCount, out var totalCount, out var _, out var _))
			{
				return Colorize(text, "#B7C0CC") + " " + Colorize(isChineseLanguage ? "进度触发" : "Progress", "#E2EAF3");
			}
			float t = ((totalCount <= 0) ? 0f : Mathf.Clamp01((float)passedCount / (float)totalCount));
			string hexColor = LerpHexColor("#E2EAF3", "#B5FFB8", t);
			string text2 = (isChineseLanguage ? $"推进 {passedCount}/{totalCount}" : $"{passedCount}/{totalCount}");
			return Colorize(text, "#B7C0CC") + " " + Colorize(text2, hexColor);
		}
		}
	}

	private string GetFogStateUiEntryText(bool isChineseLanguage)
	{
		return GetFogRuntimeStateLabel(isChineseLanguage);
	}

	private string GetGuestFogStateUiEntryText(bool isChineseLanguage)
	{
		return GetGuestFogRuntimeStateLabel(isChineseLanguage);
	}

	private string GetFogSpeedUiEntryText(bool isChineseLanguage)
	{
		return Colorize((_fogPaused ? 0f : (((Object)(object)_orbFogHandler != (Object)null) ? _orbFogHandler.speed : (FogSpeed?.Value ?? 0.4f))).ToString("0.##"), "#D6F1FF");
	}

	private string GetLobbyPauseUiEntryText(bool isChineseLanguage)
	{
		return Colorize(GetFogPauseHotkeyLabel(), "#E2EAF3");
	}

	private string GetFogHandlingUiEntryText(bool isChineseLanguage)
	{
		if (IsFogColdSuppressionEnabled())
		{
			if (!isChineseLanguage)
			{
				return Colorize("Block Fog Cold", "#9FFFA8");
			}
			return Colorize("阻止毒雾寒冷", "#9FFFA8");
		}
		if (!isChineseLanguage)
		{
			return Colorize("Fog Cold", "#FFC37D");
		}
		return Colorize("毒雾寒冷", "#FFC37D");
	}

	private string GetNightColdUiEntryText(bool isChineseLanguage)
	{
		if (IsNightColdFeatureEnabled())
		{
			if (!isChineseLanguage)
			{
				return Colorize("Night Cold On", "#9FFFA8");
			}
			return Colorize("夜晚寒冷开启", "#9FFFA8");
		}
		if (!isChineseLanguage)
		{
			return Colorize("Night Cold Off", "#FFB3B3");
		}
		return Colorize("夜晚寒冷关闭", "#FFB3B3");
	}

	private string GetFogTimingUiEntryText(bool isChineseLanguage)
	{
		float num = Mathf.Max(FogDelay?.Value ?? 900f, 0f);
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return Colorize($"{num:F1}s", "#FFE8A3");
		}
		if (IsFogRemovedInCurrentScene() || _fogPaused || _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted)
		{
			return string.Empty;
		}
		float num2 = Mathf.Max(10f - _fogHiddenBufferTimer, 0f);
		if (num2 > 0.05f)
		{
			float t = Mathf.Clamp01(1f - num2 / 10f);
			string hexColor = LerpHexColor("#FFD2B8", "#FFC0C0", t);
			return Colorize($"{num2:F1}s", hexColor);
		}
		float num3 = Mathf.Max(num - _fogDelayTimer, 0f);
		if (num3 <= 0.05f)
		{
			return string.Empty;
		}
		float t2 = ((num <= 0.01f) ? 1f : Mathf.Clamp01(1f - num3 / num));
		string hexColor2 = LerpHexColor("#FFD6D6", "#B31212", t2);
		return Colorize($"{num3:F1}s", hexColor2);
	}

	private string GetFogDistanceUiEntryText(bool isChineseLanguage)
	{
		if (!TryGetFogArrivalRemainingDistance(out var remainingDistance) || remainingDistance <= 0.05f)
		{
			return string.Empty;
		}
		GetFogDistanceColors(remainingDistance, out var _, out var valueColor);
		return Colorize(FormatFogDistanceValue(remainingDistance), valueColor);
	}

	private string GetFogEtaUiEntryText(bool isChineseLanguage)
	{
		if ((Object)(object)_orbFogHandler == (Object)null || IsFogRemovedInCurrentScene() || _fogPaused || !_orbFogHandler.isMoving || _orbFogHandler.hasArrived)
		{
			return string.Empty;
		}
		if (!TryGetFogArrivalEtaSeconds(out var etaSeconds))
		{
			return Colorize(isChineseLanguage ? "估算中" : "Estimating", "#E2EAF3");
		}
		if (etaSeconds <= 0.05f)
		{
			return string.Empty;
		}
		float num = QuantizeFogEtaSeconds(etaSeconds);
		GetFogEtaColors(num, out var _, out var valueColor);
		return Colorize($"{num:F1}s", valueColor);
	}

	private string GetDirectStartUiEntryText(bool isChineseLanguage)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_orbFogHandler == (Object)null || IsAirportScene(SceneManager.GetActiveScene()) || IsFogRemovedInCurrentScene() || _fogPaused)
		{
			return string.Empty;
		}
		if (_orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted || !ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return string.Empty;
		}
		switch (GetVanillaProgressStartUiState())
		{
		case VanillaProgressStartUiState.Unavailable:
			return Colorize("N/A", "#FFB3B3");
		default:
			return string.Empty;
		case VanillaProgressStartUiState.Tracking:
		{
			if (!TryGetVanillaProgressStartProgress(_orbFogHandler, out var passedCount, out var totalCount, out var _, out var _))
			{
				return Colorize(isChineseLanguage ? "进度触发" : "Progress", "#E2EAF3");
			}
			float t = ((totalCount <= 0) ? 0f : Mathf.Clamp01((float)passedCount / (float)totalCount));
			string hexColor = LerpHexColor("#E2EAF3", "#B5FFB8", t);
			return Colorize(isChineseLanguage ? $"推进 {passedCount}/{totalCount}" : $"{passedCount}/{totalCount}", hexColor);
		}
		}
	}

	private VanillaProgressStartUiState GetVanillaProgressStartUiState()
	{
		if ((Object)(object)_orbFogHandler == (Object)null || _orbFogHandler.isMoving || _orbFogHandler.hasArrived || _initialDelayCompleted || !ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return VanillaProgressStartUiState.Hidden;
		}
		if (!TryGetVanillaProgressStartThresholds(_orbFogHandler, out var _, out var _) || Ascents.currentAscent < 0)
		{
			return VanillaProgressStartUiState.Unavailable;
		}
		return VanillaProgressStartUiState.Tracking;
	}

	private string GetFogRuntimeStateLabel(bool isChineseLanguage)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled())
		{
			return Colorize(isChineseLanguage ? "关闭" : "OFF", "#FFC37D");
		}
		if (IsAirportScene(SceneManager.GetActiveScene()))
		{
			return Colorize(isChineseLanguage ? "大厅" : "LOBBY", "#A3D2FF");
		}
		if (IsFogRemovedInCurrentScene())
		{
			return Colorize(isChineseLanguage ? "已禁用" : "DISABLED", "#FFC37D");
		}
		if (_fogPaused)
		{
			return Colorize(isChineseLanguage ? "房主已暂停" : "HOST PAUSED", "#FFC37D");
		}
		if ((Object)(object)_orbFogHandler == (Object)null)
		{
			return Colorize(isChineseLanguage ? "同步中" : "SYNCING", "#A3D2FF");
		}
		if (!_initialDelayCompleted || ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return Colorize(isChineseLanguage ? "等待中" : "WAITING", "#FFE08A");
		}
		if (_orbFogHandler.isMoving)
		{
			return Colorize(isChineseLanguage ? "运行中" : "RUNNING", "#B5FFB8");
		}
		if (_orbFogHandler.hasArrived)
		{
			return Colorize(isChineseLanguage ? "已到达" : "ARRIVED", "#B5FFB8");
		}
		return Colorize(isChineseLanguage ? "待机" : "IDLE", "#A3D2FF");
	}

	private static string GetLobbyPauseHintLabel(bool isChineseLanguage)
	{
		string fogPauseHotkeyLabel = GetFogPauseHotkeyLabel();
		if (!isChineseLanguage)
		{
			return Colorize("Pause:", "#B7C0CC") + " " + Colorize(fogPauseHotkeyLabel, "#E2EAF3");
		}
		return Colorize("暂停键:", "#B7C0CC") + " " + Colorize(fogPauseHotkeyLabel, "#E2EAF3");
	}

	private static string GetLobbyCompassHintLabel(bool isChineseLanguage)
	{
		if (!IsCompassFeatureEnabled())
		{
			return string.Empty;
		}
		string text = (isChineseLanguage ? "指南针:" : "Compass:");
		return string.Concat(str2: Colorize(isChineseLanguage ? ("按 " + GetCompassHotkeyLabel() + " 生成") : ("Press " + GetCompassHotkeyLabel() + " to spawn"), "#E2EAF3"), str0: Colorize(text, "#B7C0CC"), str1: " ");
	}

	private static string GetFogHandlingUiLabel(bool isChineseLanguage)
	{
		string text = (isChineseLanguage ? "毒雾寒冷:" : "Fog Cold:");
		if (IsFogColdSuppressionEnabled())
		{
			if (!isChineseLanguage)
			{
				return Colorize(text, "#B7C0CC") + " " + Colorize("Block Fog Cold", "#9FFFA8");
			}
			return Colorize(text, "#B7C0CC") + " " + Colorize("阻止毒雾寒冷", "#9FFFA8");
		}
		if (!isChineseLanguage)
		{
			return Colorize(text, "#B7C0CC") + " " + Colorize("Fog Cold", "#FFC37D");
		}
		return Colorize(text, "#B7C0CC") + " " + Colorize("毒雾寒冷", "#FFC37D");
	}

	private static string GetNightColdUiLabel(bool isChineseLanguage)
	{
		string text = (isChineseLanguage ? "夜晚寒冷:" : "Night Cold:");
		if (IsNightColdFeatureEnabled())
		{
			if (!isChineseLanguage)
			{
				return Colorize(text, "#B7C0CC") + " " + Colorize("Night Cold On", "#9FFFA8");
			}
			return Colorize(text, "#B7C0CC") + " " + Colorize("夜晚寒冷开启", "#9FFFA8");
		}
		if (!isChineseLanguage)
		{
			return Colorize(text, "#B7C0CC") + " " + Colorize("Night Cold Off", "#FFB3B3");
		}
		return Colorize(text, "#B7C0CC") + " " + Colorize("夜晚寒冷关闭", "#FFB3B3");
	}

	private static string Colorize(string text, string hexColor)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(hexColor))
		{
			return text ?? string.Empty;
		}
		return "<color=" + hexColor + ">" + text + "</color>";
	}

	private static string LerpHexColor(string fromHex, string toHex, float t)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if (!TryParseHtmlColor(fromHex, out var color) || !TryParseHtmlColor(toHex, out var color2))
		{
			if (!(t >= 0.5f))
			{
				return fromHex;
			}
			return toHex;
		}
		Color val = Color.Lerp(color, color2, Mathf.Clamp01(t));
		return "#" + ColorUtility.ToHtmlStringRGB(val);
	}

	private static bool TryParseHtmlColor(string hexColor, out Color color)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		color = Color.white;
		if (string.IsNullOrWhiteSpace(hexColor))
		{
			return false;
		}
		return ColorUtility.TryParseHtmlString(hexColor.StartsWith("#", StringComparison.Ordinal) ? hexColor : ("#" + hexColor), ref color);
	}

	private bool HasFogCountdownLabel()
	{
		if (HasFogAuthority() && (Object)(object)_orbFogHandler != (Object)null && ShouldEnforceConfiguredDelay(_orbFogHandler))
		{
			return HasVisibleFogDelayCountdown();
		}
		return false;
	}

	private void ClampFogUiToCanvas(Canvas targetCanvas = null)
	{
		if ((Object)(object)_fogUiRect == (Object)null)
		{
			return;
		}
		Canvas val = targetCanvas ?? ResolveHudCanvas() ?? ((Component)_fogUiRect).GetComponentInParent<Canvas>();
		if (IsCanvasUsable(val))
		{
			if ((Object)(object)((Transform)_fogUiRect).parent != (Object)(object)((Component)val).transform)
			{
				((Transform)_fogUiRect).SetParent(((Component)val).transform, false);
			}
			RectTransform component = ((Component)val).GetComponent<RectTransform>();
			if (!((Object)(object)component == (Object)null))
			{
				ApplyBottomLeftAnchoredRect(_fogUiRect, component, 1360f, 34f, FogUiScale?.Value ?? 0.9f, FogUiX?.Value ?? 60f, FogUiY?.Value ?? 0f);
			}
		}
	}

	private void ClampCampfireLocatorUiToCanvas(Canvas targetCanvas = null)
	{
		if ((Object)(object)_campfireLocatorUiRect == (Object)null)
		{
			return;
		}
		Canvas val = targetCanvas ?? ResolveHudCanvas() ?? ((Component)_campfireLocatorUiRect).GetComponentInParent<Canvas>();
		if (IsCanvasUsable(val))
		{
			if ((Object)(object)((Transform)_campfireLocatorUiRect).parent != (Object)(object)((Component)val).transform)
			{
				((Transform)_campfireLocatorUiRect).SetParent(((Component)val).transform, false);
			}
			RectTransform component = ((Component)val).GetComponent<RectTransform>();
			if (!((Object)(object)component == (Object)null))
			{
				ApplyTopCenterAnchoredRect(_campfireLocatorUiRect, component, 372f, 24f, 1f, 54f);
			}
		}
	}

	private static void ApplyBottomLeftAnchoredRect(RectTransform target, RectTransform canvasRect, float width, float height, float scale, float x, float y)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)target == (Object)null) && !((Object)(object)canvasRect == (Object)null))
		{
			((Transform)target).localScale = Vector3.one * Mathf.Max(scale, 0.5f);
			target.anchorMin = Vector2.zero;
			target.anchorMax = Vector2.zero;
			target.pivot = Vector2.zero;
			target.sizeDelta = new Vector2(width, height);
			float num = width * ((Transform)target).localScale.x;
			float num2 = height * ((Transform)target).localScale.y;
			Rect rect = canvasRect.rect;
			float num3 = Mathf.Clamp(x, 0f, Mathf.Max(0f, ((Rect)(ref rect)).width - num));
			float num4 = Mathf.Clamp(y, 0f, Mathf.Max(0f, ((Rect)(ref rect)).height - num2));
			target.anchoredPosition = new Vector2(num3, num4);
		}
	}

	private static void ApplyTopCenterAnchoredRect(RectTransform target, RectTransform canvasRect, float width, float height, float scale, float topOffset)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)target == (Object)null) && !((Object)(object)canvasRect == (Object)null))
		{
			((Transform)target).localScale = Vector3.one * Mathf.Clamp(scale, 0.5f, 2.5f);
			target.anchorMin = new Vector2(0.5f, 1f);
			target.anchorMax = new Vector2(0.5f, 1f);
			target.pivot = new Vector2(0.5f, 1f);
			target.sizeDelta = new Vector2(width, height);
			target.anchoredPosition = new Vector2(0f, 0f - topOffset);
		}
	}

	private static void ApplyRightMiddleAnchoredRect(RectTransform target, float width, float height, float rightOffset, float downOffset)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)target == (Object)null))
		{
			((Transform)target).localScale = Vector3.one;
			target.anchorMin = new Vector2(1f, 0.5f);
			target.anchorMax = new Vector2(1f, 0.5f);
			target.pivot = new Vector2(1f, 0.5f);
			target.sizeDelta = new Vector2(width, height);
			target.anchoredPosition = new Vector2(0f - rightOffset, 0f - downOffset);
		}
	}

	private void SetFogUiVisible(bool visible)
	{
		if ((Object)(object)_fogUiRect != (Object)null)
		{
			((Component)_fogUiRect).gameObject.SetActive(visible);
		}
	}

	private void SetFogUiText(string text)
	{
		if (!((Object)(object)_fogUiText == (Object)null))
		{
			string text2 = text ?? string.Empty;
			if (!string.Equals(_lastFogUiRenderedText, text2, StringComparison.Ordinal))
			{
				_lastFogUiRenderedText = text2;
				((TMP_Text)_fogUiText).text = text2;
			}
		}
	}

	private void EnsureFogUiEntryViewCount(int targetCount)
	{
		if (!((Object)(object)_fogUiEntriesRect == (Object)null))
		{
			while (_fogUiEntryViews.Count < targetCount)
			{
				_fogUiEntryViews.Add(CreateFogUiEntryView(_fogUiEntryViews.Count));
			}
		}
	}

	private FogUiEntryView CreateFogUiEntryView(int index)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = new GameObject($"Entry{index}", new Type[4]
		{
			typeof(RectTransform),
			typeof(HorizontalLayoutGroup),
			typeof(ContentSizeFitter),
			typeof(LayoutElement)
		});
		RectTransform component = val.GetComponent<RectTransform>();
		((Transform)component).SetParent((Transform)(object)_fogUiEntriesRect, false);
		HorizontalLayoutGroup component2 = val.GetComponent<HorizontalLayoutGroup>();
		((HorizontalOrVerticalLayoutGroup)component2).childControlWidth = true;
		((HorizontalOrVerticalLayoutGroup)component2).childControlHeight = true;
		((HorizontalOrVerticalLayoutGroup)component2).childForceExpandWidth = false;
		((HorizontalOrVerticalLayoutGroup)component2).childForceExpandHeight = false;
		((HorizontalOrVerticalLayoutGroup)component2).spacing = 3f;
		ContentSizeFitter component3 = val.GetComponent<ContentSizeFitter>();
		component3.horizontalFit = (FitMode)2;
		component3.verticalFit = (FitMode)2;
		LayoutElement component4 = val.GetComponent<LayoutElement>();
		component4.minHeight = 34f;
		component4.preferredHeight = 34f;
		GameObject val2 = new GameObject("Icon", new Type[3]
		{
			typeof(RectTransform),
			typeof(Image),
			typeof(LayoutElement)
		});
		RectTransform component5 = val2.GetComponent<RectTransform>();
		((Transform)component5).SetParent((Transform)(object)component, false);
		component5.sizeDelta = new Vector2(19f, 19f);
		component5.anchoredPosition = new Vector2(0f, -1f);
		LayoutElement component6 = val2.GetComponent<LayoutElement>();
		component6.minWidth = 19f;
		component6.preferredWidth = 19f;
		component6.minHeight = 19f;
		component6.preferredHeight = 19f;
		component6.flexibleWidth = 0f;
		Image component7 = val2.GetComponent<Image>();
		((Graphic)component7).raycastTarget = false;
		component7.preserveAspect = true;
		GameObject val3 = new GameObject("Text", new Type[1] { typeof(RectTransform) });
		((Transform)val3.GetComponent<RectTransform>()).SetParent((Transform)(object)component, false);
		TextMeshProUGUI val4 = val3.AddComponent<TextMeshProUGUI>();
		ApplyGameTextStyle(val4, Color.white, 0.9f);
		((TMP_Text)val4).richText = true;
		((TMP_Text)val4).alignment = (TextAlignmentOptions)4097;
		((TMP_Text)val4).overflowMode = (TextOverflowModes)0;
		((TMP_Text)val4).enableAutoSizing = false;
		((TMP_Text)val4).text = string.Empty;
		return new FogUiEntryView
		{
			Root = component,
			Icon = component7,
			Text = val4
		};
	}

	private void RefreshFogUiEntryStyles()
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		foreach (FogUiEntryView fogUiEntryView in _fogUiEntryViews)
		{
			if (!((Object)(object)fogUiEntryView?.Text == (Object)null))
			{
				ApplyGameTextStyle(fogUiEntryView.Text, Color.white, 0.9f);
				((TMP_Text)fogUiEntryView.Text).richText = true;
				((TMP_Text)fogUiEntryView.Text).alignment = (TextAlignmentOptions)4097;
				((TMP_Text)fogUiEntryView.Text).overflowMode = (TextOverflowModes)0;
			}
		}
	}

	private Sprite GetFogUiIconSprite(FogUiIconKind iconKind)
	{
		if (_fogUiIconSprites.TryGetValue(iconKind, out var value) && (Object)(object)value != (Object)null)
		{
			return value;
		}
		value = CreateFogUiIconSprite(iconKind);
		_fogUiIconSprites[iconKind] = value;
		return value;
	}

	private Sprite GetCampfireLocatorDotSprite()
	{
		if ((Object)(object)_campfireLocatorDotSprite != (Object)null)
		{
			return _campfireLocatorDotSprite;
		}
		_campfireLocatorDotSprite = CreateCampfireLocatorDotSprite();
		return _campfireLocatorDotSprite;
	}

	private static Sprite CreateCampfireLocatorDotSprite()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected O, but got Unknown
		Texture2D val = new Texture2D(32, 32, (TextureFormat)4, false)
		{
			filterMode = (FilterMode)1,
			wrapMode = (TextureWrapMode)1
		};
		Color32[] array = Enumerable.Repeat<Color32>(new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0), 1024).ToArray();
		Color32 color = default(Color32);
		((Color32)(ref color))..ctor(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
		Color32 color2 = default(Color32);
		((Color32)(ref color2))..ctor((byte)170, (byte)24, (byte)24, byte.MaxValue);
		Color32 color3 = default(Color32);
		((Color32)(ref color3))..ctor(byte.MaxValue, (byte)62, (byte)62, byte.MaxValue);
		FillCircle(array, 32, 16f, 16f, 11.8f, color);
		FillCircle(array, 32, 16f, 16f, 9.8f, color2);
		FillCircle(array, 32, 16f, 16f, 6.8f, color3);
		val.SetPixels32(array);
		val.Apply(false, false);
		return Sprite.Create(val, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 100f);
	}

	private static Sprite CreateFogUiIconSprite(FogUiIconKind iconKind)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected O, but got Unknown
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0466: Unknown result type (might be due to invalid IL or missing references)
		//IL_0492: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cf: Unknown result type (might be due to invalid IL or missing references)
		Texture2D val = new Texture2D(24, 24, (TextureFormat)4, false);
		((Texture)val).filterMode = (FilterMode)0;
		((Texture)val).wrapMode = (TextureWrapMode)1;
		Color32[] array = Enumerable.Repeat<Color32>(new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0), 576).ToArray();
		switch (iconKind)
		{
		case FogUiIconKind.State:
			DrawCircleOutline(array, 24, 12f, 8f, 4.5f, 2f);
			DrawLine(array, 24, 12f, 12f, 12f, 20f, 2f);
			DrawLine(array, 24, 9f, 18f, 15f, 18f, 2f);
			break;
		case FogUiIconKind.Speed:
			DrawLine(array, 24, 5f, 8f, 16f, 8f, 2f);
			DrawLine(array, 24, 3f, 12f, 18f, 12f, 2f);
			DrawLine(array, 24, 7f, 16f, 20f, 16f, 2f);
			break;
		case FogUiIconKind.Buffer:
			DrawLine(array, 24, 7f, 4f, 17f, 4f, 2f);
			DrawLine(array, 24, 7f, 20f, 17f, 20f, 2f);
			DrawLine(array, 24, 8f, 6f, 16f, 12f, 2f);
			DrawLine(array, 24, 16f, 12f, 8f, 18f, 2f);
			break;
		case FogUiIconKind.Delay:
		case FogUiIconKind.Eta:
			DrawCircleOutline(array, 24, 12f, 12f, 8f, 2f);
			DrawLine(array, 24, 12f, 12f, 12f, 7f, 2f);
			DrawLine(array, 24, 12f, 12f, 16f, 14f, 2f);
			break;
		case FogUiIconKind.Distance:
			DrawLine(array, 24, 5f, 12f, 19f, 12f, 2f);
			DrawLine(array, 24, 5f, 8f, 5f, 16f, 2f);
			DrawLine(array, 24, 19f, 8f, 19f, 16f, 2f);
			DrawLine(array, 24, 10f, 10f, 10f, 14f, 2f);
			DrawLine(array, 24, 14f, 10f, 14f, 14f, 2f);
			break;
		case FogUiIconKind.Direct:
			DrawCircleOutline(array, 24, 9f, 15f, 3.2f, 1.8f);
			DrawLine(array, 24, 11f, 13f, 19f, 6f, 2f);
			DrawLine(array, 24, 14f, 6f, 19f, 6f, 2f);
			DrawLine(array, 24, 19f, 6f, 19f, 11f, 2f);
			break;
		case FogUiIconKind.Pause:
			DrawRectOutline(array, 24, 5f, 5f, 14f, 14f, 2f);
			DrawLine(array, 24, 9f, 12f, 15f, 12f, 2f);
			break;
		case FogUiIconKind.FogHandling:
			DrawLine(array, 24, 12f, 4f, 12f, 20f, 1.8f);
			DrawLine(array, 24, 4f, 12f, 20f, 12f, 1.8f);
			DrawLine(array, 24, 6f, 6f, 18f, 18f, 1.8f);
			DrawLine(array, 24, 18f, 6f, 6f, 18f, 1.8f);
			DrawCircleOutline(array, 24, 12f, 12f, 3f, 1.2f);
			break;
		case FogUiIconKind.Night:
			FillCircle(array, 24, 12f, 12f, 8f, new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			FillCircle(array, 24, 15f, 10f, 8f, new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)0));
			break;
		}
		val.SetPixels32(array);
		val.Apply(false, false);
		return Sprite.Create(val, new Rect(0f, 0f, 24f, 24f), new Vector2(0.5f, 0.5f), 100f);
	}

	private static void DrawCircleOutline(Color32[] pixels, int size, float centerX, float centerY, float radius, float thickness)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Max(radius - thickness, 0f);
		float num2 = radius + thickness;
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				float num3 = Vector2.Distance(new Vector2((float)j + 0.5f, (float)i + 0.5f), new Vector2(centerX, centerY));
				if (num3 >= num && num3 <= num2)
				{
					pixels[i * size + j] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
				}
			}
		}
	}

	private static void FillCircle(Color32[] pixels, int size, float centerX, float centerY, float radius, Color32 color)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		float num = radius * radius;
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				float num2 = (float)j + 0.5f - centerX;
				float num3 = (float)i + 0.5f - centerY;
				if (num2 * num2 + num3 * num3 <= num)
				{
					pixels[i * size + j] = color;
				}
			}
		}
	}

	private static void FillEllipse(Color32[] pixels, int size, float centerX, float centerY, float radiusX, float radiusY, Color32 color)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Max(radiusX, 0.01f);
		float num2 = Mathf.Max(radiusY, 0.01f);
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				float num3 = ((float)j + 0.5f - centerX) / num;
				float num4 = ((float)i + 0.5f - centerY) / num2;
				if (num3 * num3 + num4 * num4 <= 1f)
				{
					pixels[i * size + j] = color;
				}
			}
		}
	}

	private static void DrawRectOutline(Color32[] pixels, int size, float x, float y, float width, float height, float thickness)
	{
		DrawLine(pixels, size, x, y, x + width, y, thickness);
		DrawLine(pixels, size, x, y + height, x + width, y + height, thickness);
		DrawLine(pixels, size, x, y, x, y + height, thickness);
		DrawLine(pixels, size, x + width, y, x + width, y + height, thickness);
	}

	private static void FillRect(Color32[] pixels, int size, int startX, int startY, int width, int height, Color32 color)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		for (int i = startY; i < startY + height; i++)
		{
			if (i < 0 || i >= size)
			{
				continue;
			}
			for (int j = startX; j < startX + width; j++)
			{
				if (j >= 0 && j < size)
				{
					pixels[i * size + j] = color;
				}
			}
		}
	}

	private static void DrawLine(Color32[] pixels, int size, float x0, float y0, float x1, float y1, float thickness)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		DrawLine(pixels, size, x0, y0, x1, y1, thickness, new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private static void DrawLine(Color32[] pixels, int size, float x0, float y0, float x1, float y1, float thickness, Color32 color)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		Vector2 start = default(Vector2);
		((Vector2)(ref start))..ctor(x0, y0);
		Vector2 end = default(Vector2);
		((Vector2)(ref end))..ctor(x1, y1);
		float num = Mathf.Max(thickness * 0.5f, 0.5f);
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				if (DistanceToSegment(new Vector2((float)j + 0.5f, (float)i + 0.5f), start, end) <= num)
				{
					pixels[i * size + j] = color;
				}
			}
		}
	}

	private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = end - start;
		float sqrMagnitude = ((Vector2)(ref val)).sqrMagnitude;
		if (sqrMagnitude <= 0.0001f)
		{
			return Vector2.Distance(point, start);
		}
		float num = Mathf.Clamp01(Vector2.Dot(point - start, val) / sqrMagnitude);
		Vector2 val2 = start + val * num;
		return Vector2.Distance(point, val2);
	}

	private void SetCampfireLocatorUiVisible(bool visible)
	{
		if ((Object)(object)_campfireLocatorUiRect != (Object)null)
		{
			((Component)_campfireLocatorUiRect).gameObject.SetActive(visible);
		}
	}

	private void CleanupFogUi()
	{
		if ((Object)(object)_fogUiRect != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)_fogUiRect).gameObject);
		}
		_fogUiRect = null;
		_fogUiText = null;
		_fogUiEntriesRect = null;
		_fogUiEntryViews.Clear();
		_fogUiDisplayEntries.Clear();
		_lastFogUiRenderedText = string.Empty;
	}

	private void CleanupCampfireLocatorUi()
	{
		if ((Object)(object)_campfireLocatorUiRect != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)_campfireLocatorUiRect).gameObject);
		}
		_campfireLocatorUiRect = null;
		_campfireLocatorDotRect = null;
		_campfireLocatorCurrentDotX = 0f;
	}

	private void RestoreVanillaFogSpeed()
	{
		if ((Object)(object)_orbFogHandler != (Object)null)
		{
			_orbFogHandler.speed = 0.3f;
		}
	}

	private void ResetFogRuntimeState()
	{
		_fogStateInitialized = false;
		_initialDelayCompleted = false;
		_initialCompassGranted = false;
		_totalCompassGrantCount = 0;
		_delayedFogOriginId = -1;
		_trackedFogOriginId = -1;
		ClearSyntheticFogStage();
		_fogDelayTimer = 0f;
		_fogHiddenBufferTimer = 0f;
		_lastFogStateSyncTime = -0.18f;
		_lastRemoteStatusSyncTime = -0.25f;
		_lastCompassGrantSyncTime = -0.75f;
		_fogHandlerSearchTimer = 0f;
		_grantedCampfireCompassIds.Clear();
		_restoredCheckpointCampfireIds.Clear();
		_playerCompassGrantCounts.Clear();
		_remoteFogSuppressionDebt.Clear();
		_remotePlayerCompassBaselineCounts.Clear();
		_pendingCampfireCompassGrantTimes.Clear();
		_fogPaused = false;
		_localFogStatusSuppressionDepth = 0;
		ResetHiddenNightTestHotkeyState();
		ResetFogArrivalEstimate();
		ResetFogStateSyncSnapshot();
	}

	private static bool DetectChineseLanguage()
	{
		if (TryGetConfiguredGameLanguage(out var isChineseLanguage))
		{
			return isChineseLanguage;
		}
		if (TryGetLocalizedTextLanguageName(out var languageName))
		{
			return IsChineseLanguageName(languageName);
		}
		return false;
	}

	private static bool TryGetConfiguredGameLanguage(out bool isChineseLanguage)
	{
		isChineseLanguage = false;
		try
		{
			if (!PlayerPrefs.HasKey("LanguageSetting"))
			{
				return false;
			}
			int result = PlayerPrefs.GetInt("LanguageSetting", int.MinValue);
			if (result != int.MinValue)
			{
				isChineseLanguage = result == 9;
				return true;
			}
			string text = PlayerPrefs.GetString("LanguageSetting", string.Empty);
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
			{
				isChineseLanguage = result == 9;
				return true;
			}
			isChineseLanguage = IsChineseLanguageName(text);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetLocalizedTextLanguageName(out string languageName)
	{
		languageName = string.Empty;
		try
		{
			languageName = ((object)System.Runtime.CompilerServices.Unsafe.As<Language, Language>(ref LocalizedText.CURRENT_LANGUAGE)/*cast due to .constrained prefix*/).ToString();
			return !string.IsNullOrWhiteSpace(languageName);
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
		if (languageName.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) < 0 && languageName.IndexOf("中文", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return languageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string GetConfigSectionName(ConfigKey configKey)
	{
		return GetSectionName(configKey, isChineseLanguage: false);
	}

	private static string GetSectionName(ConfigKey configKey, bool isChineseLanguage)
	{
		if (IsAdjustmentConfigKey(configKey))
		{
			if (!isChineseLanguage)
			{
				return "Adjustments";
			}
			return "调整";
		}
		if (!isChineseLanguage)
		{
			return "Basic";
		}
		return "基本";
	}

	private static string GetLegacyConfigSectionName()
	{
		return "Fog";
	}

	private static string GetLegacySectionName(bool isChineseLanguage)
	{
		if (!isChineseLanguage)
		{
			return "Fog";
		}
		return "毒雾";
	}

	private static bool IsAdjustmentConfigKey(ConfigKey configKey)
	{
		if (configKey != ConfigKey.FogUiX && configKey != ConfigKey.FogUiY)
		{
			return configKey == ConfigKey.FogUiScale;
		}
		return true;
	}

	private static string GetLocalizedModDisplayName(bool isChineseLanguage)
	{
		return "Fog&ColdControl";
	}

	private static string GetConfigKeyName(ConfigKey configKey)
	{
		return GetKeyName(configKey, isChineseLanguage: false);
	}

	private static string GetKeyName(ConfigKey configKey, bool isChineseLanguage)
	{
		return configKey switch
		{
			ConfigKey.ModEnabled => isChineseLanguage ? "模块开关" : "Enable Mod", 
			ConfigKey.FogColdSuppression => isChineseLanguage ? "毒雾寒冷" : "Suppress Fog Cold", 
			ConfigKey.NightColdEnabled => isChineseLanguage ? "夜晚寒冷" : "Night Cold", 
			ConfigKey.FogSpeed => isChineseLanguage ? "毒雾移动速度" : "Fog Speed", 
			ConfigKey.FogDelay => isChineseLanguage ? "毒雾延迟时间s" : "Fog Delay (s)", 
			ConfigKey.CompassEnabled => isChineseLanguage ? "指南针功能开关" : "Compass Feature", 
			ConfigKey.CompassHotkey => isChineseLanguage ? "指南针生成按键" : "Compass Hotkey", 
			ConfigKey.FogPauseHotkey => isChineseLanguage ? "毒雾暂停按键" : "Pause Fog Hotkey", 
			ConfigKey.FogUiEnabled => isChineseLanguage ? "UI启用" : "Fog UI", 
			ConfigKey.CampfireLocatorUiEnabled => isChineseLanguage ? "篭火位置 HUD" : "Campfire Locator HUD", 
			ConfigKey.FogUiX => isChineseLanguage ? "UI X位置" : "UI X Position", 
			ConfigKey.FogUiY => isChineseLanguage ? "UI Y位置" : "UI Y Position", 
			ConfigKey.FogUiScale => isChineseLanguage ? "UI缩放" : "UI Scale", 
			_ => string.Empty, 
		};
	}

	private static string GetLocalizedDescription(ConfigKey configKey, bool isChineseLanguage)
	{
		return (configKey switch
		{
			ConfigKey.ModEnabled => isChineseLanguage ? "总开关。开启后由主机接管 Fog&ColdControl 的毒雾、联机同步和指南针奖励；关闭后不生效。" : "Master switch for Fog&ColdControl. When enabled, the host controls fog behavior, multiplayer sync, and compass rewards.", 
			ConfigKey.FogColdSuppression => isChineseLanguage ? "控制是否阻止毒雾带来的寒冷值。只影响毒雾寒冷，不影响夜晚寒冷。默认关闭。" : "Blocks cold caused by fog only. This does not affect night cold. Default: Off.", 
			ConfigKey.NightColdEnabled => isChineseLanguage ? "控制夜晚寒冷功能。只在天阶 5 及以上生效，行为与原版一致。默认开启。" : "Controls night cold. Only applies on Ascent 5 and above, matching vanilla behavior. Default: On.", 
			ConfigKey.FogSpeed => isChineseLanguage ? "控制毒雾推进速度。范围 0.3~20，数值越大，毒雾推进越快。默认 0.4。" : "Controls how fast the fog moves. Range: 0.3 to 20. Higher values make the fog move faster. Default: 0.4.", 
			ConfigKey.FogDelay => isChineseLanguage ? "控制首段毒雾开始移动前的等待时间。范围 20~1000 秒。默认 900 秒。" : "Controls how long the first fog segment waits before moving. Range: 20 to 1000 seconds. Default: 900 seconds.", 
			ConfigKey.CompassEnabled => isChineseLanguage ? "指南针功能总开关。控制自动发放、按键生成和大厅提示。默认关闭。" : "Master switch for compass features, including automatic rewards, hotkey spawning, and the lobby prompt. Default: Off.", 
			ConfigKey.CompassHotkey => isChineseLanguage ? "按下后在玩家正前方生成一个普通指南针。设为 None 可禁用该按键。" : "Spawns a normal compass in front of the player. Set this to None to disable the hotkey.", 
			ConfigKey.FogPauseHotkey => isChineseLanguage ? "主机专用按键，用于暂停或继续毒雾推进。设为 None 可禁用。默认 Y。" : "Host-only hotkey used to pause or resume fog movement. Set this to None to disable it. Default: Y.", 
			ConfigKey.FogUiEnabled => isChineseLanguage ? "控制是否显示毒雾相关 HUD。" : "Shows or hides the Fog&ColdControl HUD.", 
			ConfigKey.CampfireLocatorUiEnabled => isChineseLanguage ? "控制是否显示屏幕顶部的篭火位置 HUD。默认开启。" : "Shows or hides the top-screen campfire locator HUD. Default: On.", 
			ConfigKey.FogUiX => isChineseLanguage ? "调整毒雾 HUD 的水平位置。默认 60。" : "Adjusts the horizontal position of the Fog&ColdControl HUD. Default: 60.", 
			ConfigKey.FogUiY => isChineseLanguage ? "调整毒雾 HUD 的垂直位置。默认 0。" : "Adjusts the vertical position of the Fog&ColdControl HUD. Default: 0.", 
			ConfigKey.FogUiScale => isChineseLanguage ? "调整毒雾 HUD 的整体缩放。默认 0.9。" : "Adjusts the overall scale of the Fog&ColdControl HUD. Default: 0.9.", 
			_ => string.Empty, 
		}).Replace("FogClimb", "Fog&ColdControl").Replace("Fog Climb", "Fog&ColdControl");
	}

	internal static bool IsModFeatureEnabled()
	{
		if (ModEnabled != null)
		{
			return ModEnabled.Value;
		}
		return true;
	}

	private static bool HasFogAuthority()
	{
		if (PhotonNetwork.InRoom)
		{
			return PhotonNetwork.IsMasterClient;
		}
		return true;
	}

	private static bool IsReadOnlyFogUiViewer()
	{
		if (PhotonNetwork.InRoom)
		{
			return !PhotonNetwork.IsMasterClient;
		}
		return false;
	}

	internal static bool IsCompassFeatureEnabled()
	{
		if (CompassEnabled != null)
		{
			return CompassEnabled.Value;
		}
		return true;
	}

	internal static bool IsFogColdSuppressionEnabled()
	{
		if (FogColdSuppression != null)
		{
			return FogColdSuppression.Value;
		}
		return true;
	}

	internal static bool IsNightColdFeatureEnabled()
	{
		if (NightColdEnabled != null)
		{
			return NightColdEnabled.Value;
		}
		return true;
	}

	internal static bool ShouldPreserveVanillaLateGameNoCold()
	{
		if (HasFogAuthority() && IsModFeatureEnabled() && (Object)(object)Instance != (Object)null)
		{
			return Instance.IsLateGameFogColdSuppressionActive();
		}
		return false;
	}

	internal static bool ShouldSuppressFogColdDamage()
	{
		if (HasFogAuthority() && IsModFeatureEnabled())
		{
			if (!IsFogColdSuppressionEnabled())
			{
				return ShouldPreserveVanillaLateGameNoCold();
			}
			return true;
		}
		return false;
	}

	internal static bool ShouldForceFogCoverageEverywhere()
	{
		if (HasFogAuthority())
		{
			return IsModFeatureEnabled();
		}
		return false;
	}

	internal static void NotifyFogHandlerChanged(OrbFogHandler fogHandler)
	{
		Instance?.EnsureFogCoverage(fogHandler);
	}

	internal static void BeginLocalFogStatusSuppression()
	{
		if (ShouldSuppressFogColdDamage())
		{
			_localFogStatusSuppressionDepth++;
		}
	}

	internal static void EndLocalFogStatusSuppression()
	{
		if (_localFogStatusSuppressionDepth > 0)
		{
			_localFogStatusSuppressionDepth--;
		}
	}

	internal static bool ShouldSuppressLocalFogSourceStatus(CharacterAfflictions afflictions, STATUSTYPE statusType)
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Invalid comparison between Unknown and I4
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Invalid comparison between Unknown and I4
		if (!ShouldSuppressFogColdDamage() || (Object)(object)afflictions == (Object)null)
		{
			return false;
		}
		Character character = afflictions.character;
		if ((Object)(object)character == (Object)null || (Object)(object)character != (Object)(object)Character.localCharacter || character.isBot || character.isZombie || (Object)(object)character.data == (Object)null)
		{
			return false;
		}
		if (!(character.data.isSkeleton ? ((int)statusType == 0) : ((int)statusType == 2)))
		{
			return false;
		}
		if (_localFogStatusSuppressionDepth > 0)
		{
			return true;
		}
		if ((Object)(object)Instance != (Object)null && Instance.ShouldDisableNightColdInCurrentStage())
		{
			return true;
		}
		if (ShouldPreserveVanillaLateGameNoCold() && (Object)(object)Instance != (Object)null)
		{
			return Instance.IsCharacterInsideAnyFog(character);
		}
		return false;
	}

	internal static bool ShouldBlockVanillaOrbFogWait(OrbFogHandler fogHandler)
	{
		if (!IsModFeatureEnabled() || (Object)(object)Instance == (Object)null || (Object)(object)fogHandler == (Object)null || !HasFogAuthority())
		{
			return false;
		}
		if (!Instance._fogStateInitialized)
		{
			Instance.InitializeFogRuntimeState(fogHandler);
		}
		Instance.UpdateTrackedFogOrigin();
		if (!IsFogRemovedInCurrentScene() && !Instance.ShouldEnforceConfiguredDelay(fogHandler))
		{
			return Instance.ShouldHoldFogUntilCampfireActivation(fogHandler);
		}
		return true;
	}

	internal static void NotifyCampfireLit(Campfire campfire)
	{
		Instance?.HandleCampfireLit(campfire);
	}

	private void UpdateLocalInstallStateAdvertisement()
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Expected O, but got Unknown
		//IL_0059: Expected O, but got Unknown
		if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
		{
			_hasAdvertisedInstallState = false;
			return;
		}
		bool flag = ShouldSuppressFogColdDamage();
		if (_hasAdvertisedInstallState && _lastAdvertisedInstallState == flag && LocalInstallStateMatches(flag))
		{
			return;
		}
		try
		{
			Player localPlayer = PhotonNetwork.LocalPlayer;
			Hashtable val = new Hashtable();
			((Dictionary<object, object>)val).Add((object)"FogClimb.Enabled", (object)flag);
			localPlayer.SetCustomProperties(val, (Hashtable)null, (WebFlags)null);
			_lastAdvertisedInstallState = flag;
			_hasAdvertisedInstallState = true;
		}
		catch (Exception ex)
		{
			_hasAdvertisedInstallState = false;
			((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Failed to publish install-state metadata: " + ex.Message));
		}
	}

	private static bool LocalInstallStateMatches(bool expected)
	{
		Player localPlayer = PhotonNetwork.LocalPlayer;
		if (((localPlayer != null) ? localPlayer.CustomProperties : null) == null || !((Dictionary<object, object>)(object)localPlayer.CustomProperties).ContainsKey((object)"FogClimb.Enabled"))
		{
			return false;
		}
		if (TryReadInstallState(localPlayer, out var isEnabled))
		{
			return isEnabled == expected;
		}
		return false;
	}

	private static bool HasRemoteFogSuppressionSupport(Player player)
	{
		bool isEnabled;
		return TryReadInstallState(player, out isEnabled) && isEnabled;
	}

	private static bool TryReadInstallState(Player player, out bool isEnabled)
	{
		isEnabled = false;
		if (((player != null) ? player.CustomProperties : null) == null || !((Dictionary<object, object>)(object)player.CustomProperties).ContainsKey((object)"FogClimb.Enabled"))
		{
			return false;
		}
		object obj = player.CustomProperties[(object)"FogClimb.Enabled"];
		if (!(obj is bool flag))
		{
			if (obj is string value)
			{
				return bool.TryParse(value, out isEnabled);
			}
			return false;
		}
		isEnabled = flag;
		return true;
	}

	private static int GetRemoteStatusSuppressionKey(Character character)
	{
		int? obj;
		if (character == null)
		{
			obj = null;
		}
		else
		{
			PhotonView photonView = ((MonoBehaviourPun)character).photonView;
			if (photonView == null)
			{
				obj = null;
			}
			else
			{
				Player owner = photonView.Owner;
				obj = ((owner != null) ? new int?(owner.ActorNumber) : ((int?)null));
			}
		}
		int? num = obj;
		if (!num.HasValue)
		{
			if (character == null)
			{
				return 0;
			}
			return ((Object)character).GetInstanceID();
		}
		return num.GetValueOrDefault();
	}

	private bool IsCharacterPastJoinGrace(Character character)
	{
		object obj;
		if (character == null)
		{
			obj = null;
		}
		else
		{
			PhotonView photonView = ((MonoBehaviourPun)character).photonView;
			obj = ((photonView != null) ? photonView.Owner : null);
		}
		if (obj == null || ((MonoBehaviourPun)character).photonView.IsMine)
		{
			return true;
		}
		if (!_remotePlayerFirstSeenTimes.TryGetValue(((MonoBehaviourPun)character).photonView.Owner.ActorNumber, out var value))
		{
			return true;
		}
		return Time.unscaledTime - value >= 8f;
	}

	private void ForgetRemoteStatusSuppression(Character character)
	{
		int remoteStatusSuppressionKey = GetRemoteStatusSuppressionKey(character);
		if (remoteStatusSuppressionKey != 0)
		{
			_remoteFogSuppressionDebt.Remove(remoteStatusSuppressionKey);
		}
	}

	private void TryRestoreCheckpointCampfireDelay()
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		if (!IsModFeatureEnabled() || !HasFogAuthority() || (Object)(object)_orbFogHandler == (Object)null || (Object)(object)Character.localCharacter == (Object)null || !IsGameplayFogScene(SceneManager.GetActiveScene()) || LoadingScreenHandler.loading)
		{
			return;
		}
		Campfire val = null;
		try
		{
			val = MapHandler.CurrentCampfire;
		}
		catch
		{
		}
		if ((Object)(object)val == (Object)null || !val.Lit)
		{
			return;
		}
		int instanceID = ((Object)val).GetInstanceID();
		if (!_grantedCampfireCompassIds.Contains(instanceID) && !_restoredCheckpointCampfireIds.Contains(instanceID))
		{
			int delayedOriginId;
			if (_orbFogHandler.isMoving || _orbFogHandler.currentWaitTime > 0.2f)
			{
				_restoredCheckpointCampfireIds.Add(instanceID);
			}
			else if (ScheduleFogDelayAfterCampfire(val, out delayedOriginId))
			{
				_restoredCheckpointCampfireIds.Add(instanceID);
				((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Restored fog delay from lit checkpoint campfire: " + ((Object)val).name + "."));
			}
		}
	}

	private static STATUSTYPE GetFogSuppressionStatusType(Character character)
	{
		if ((Object)(object)character?.data != (Object)null && character.data.isSkeleton)
		{
			return (STATUSTYPE)0;
		}
		return (STATUSTYPE)2;
	}

	private static float GetSuppressionTransferAmount(float pendingDebt)
	{
		if (pendingDebt <= 0f)
		{
			return 0f;
		}
		float num = Mathf.Floor(pendingDebt / 0.025f) * 0.025f;
		if (num >= 0.025f)
		{
			return num;
		}
		if (!(pendingDebt < 0.025f))
		{
			return 0f;
		}
		return pendingDebt;
	}

	private void TryCleanupGeneratedBackupFile()
	{
		try
		{
			PluginInfo info = ((BaseUnityPlugin)this).Info;
			string text = ((info != null) ? info.Location : null);
			if (string.IsNullOrWhiteSpace(text))
			{
				return;
			}
			List<string> list = new List<string> { text + "-unrpcpatched.old" };
			string directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				string[] legacyPluginFileNames = LegacyPluginFileNames;
				foreach (string text2 in legacyPluginFileNames)
				{
					list.Add(Path.Combine(directoryName, text2 + "-unrpcpatched.old"));
				}
			}
			foreach (string item in list.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (File.Exists(item))
				{
					File.Delete(item);
					((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Removed generated backup file: " + Path.GetFileName(item)));
				}
			}
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Failed to clean generated backup file: " + ex.Message));
		}
	}

	private void TryCleanupLegacyPluginFile()
	{
		try
		{
			PluginInfo info = ((BaseUnityPlugin)this).Info;
			string text = ((info != null) ? info.Location : null);
			if (string.IsNullOrWhiteSpace(text) || !string.Equals(Path.GetFileName(text), "Thanks.Fog&ColdControl.dll", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			string directoryName = Path.GetDirectoryName(text);
			if (string.IsNullOrWhiteSpace(directoryName))
			{
				return;
			}
			string[] legacyPluginFileNames = LegacyPluginFileNames;
			foreach (string text2 in legacyPluginFileNames)
			{
				string path = Path.Combine(directoryName, text2);
				if (File.Exists(path))
				{
					File.Delete(path);
					((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Removed legacy plugin file: " + text2));
				}
			}
		}
		catch (Exception ex)
		{
			((BaseUnityPlugin)this).Logger.LogDebug((object)("[Fog&ColdControl] Failed to clean legacy plugin file: " + ex.Message));
		}
	}

	private void HandleCampfireLit(Campfire campfire)
	{
		if (!IsModFeatureEnabled() || !HasFogAuthority() || (Object)(object)campfire == (Object)null)
		{
			return;
		}
		int instanceID = ((Object)campfire).GetInstanceID();
		if (_grantedCampfireCompassIds.Add(instanceID))
		{
			int delayedOriginId;
			bool flag = ScheduleFogDelayAfterCampfire(campfire, out delayedOriginId);
			if (!flag)
			{
				((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Skipping campfire compass grant for " + ((Object)campfire).name + " because no fog segment is scheduled after this campfire."));
			}
			else if (flag && delayedOriginId == 0 && !_initialCompassGranted)
			{
				((BaseUnityPlugin)this).Logger.LogInfo((object)("[Fog&ColdControl] Skipping campfire compass grant for the opening campfire (" + ((Object)campfire).name + ") because the initial fog-rise grant is still pending."));
			}
			else if (IsCompassFeatureEnabled())
			{
				_pendingCampfireCompassGrantTimes[instanceID] = Time.unscaledTime + 0.9f;
				((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Scheduled campfire compass grant for {1} in {2:F1}s.", "Fog&ColdControl", ((Object)campfire).name, 0.9f));
			}
		}
	}

	private bool ScheduleFogDelayAfterCampfire(Campfire campfire, out int delayedOriginId)
	{
		delayedOriginId = -1;
		if ((Object)(object)campfire == (Object)null)
		{
			return false;
		}
		if (!TryResolveCampfireFogOriginId(campfire, out delayedOriginId))
		{
			return false;
		}
		_delayedFogOriginId = delayedOriginId;
		_fogHiddenBufferTimer = 0f;
		_fogDelayTimer = 0f;
		_initialDelayCompleted = false;
		if ((Object)(object)_orbFogHandler != (Object)null)
		{
			if (_orbFogHandler.currentID != delayedOriginId)
			{
				_orbFogHandler.SetFogOrigin(delayedOriginId);
				EnsureFogCoverage(_orbFogHandler);
				SyncFogOriginToGuests();
			}
			_orbFogHandler.isMoving = false;
			_orbFogHandler.currentWaitTime = 0f;
			_orbFogHandler.speed = 0f;
			_orbFogHandler.hasArrived = false;
			if (_orbFogHandler.currentID == delayedOriginId && _initialDelayCompleted)
			{
				StartFogMovement();
			}
		}
		float num = FogDelay?.Value ?? 900f;
		((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Applied hidden fog buffer {1:F1}s and configured delay {2:F1}s after lighting campfire. Next origin: {3}.", "Fog&ColdControl", 10f, num, delayedOriginId));
		return true;
	}

	private bool TryResolveCampfireFogOriginId(Campfire campfire, out int delayedOriginId)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Invalid comparison between Unknown and I4
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected I4, but got Unknown
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Expected I4, but got Unknown
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Expected I4, but got Unknown
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected I4, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Expected I4, but got Unknown
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Invalid comparison between Unknown and I4
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ae: Expected I4, but got Unknown
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023e: Expected I4, but got Unknown
		//IL_0242: Unknown result type (might be due to invalid IL or missing references)
		delayedOriginId = -1;
		if ((Object)(object)campfire == (Object)null)
		{
			return false;
		}
		int availableFogOriginCount = GetAvailableFogOriginCount();
		if (availableFogOriginCount <= 0)
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)"[Fog&ColdControl] Unable to resolve fog origin after lighting campfire because no fog origins were found.");
			return false;
		}
		Segment advanceToSegment = campfire.advanceToSegment;
		if (ShouldRemoveFogForSegment(advanceToSegment))
		{
			_pendingSyntheticFogSegmentId = -1;
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Skipping fog scheduling for removed segment {1} ({2}). Campfire={3}.", "Fog&ColdControl", (int)advanceToSegment, advanceToSegment, ((Object)campfire).name));
			return false;
		}
		if ((int)advanceToSegment >= availableFogOriginCount)
		{
			if (!ShouldUseCustomFogPositionForSegment(advanceToSegment))
			{
				_pendingSyntheticFogSegmentId = -1;
				((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] No synthetic fog stage is defined for segment {1} ({2}). Campfire={3}.", "Fog&ColdControl", (int)advanceToSegment, advanceToSegment, ((Object)campfire).name));
				return false;
			}
			_pendingSyntheticFogSegmentId = (int)advanceToSegment;
			delayedOriginId = availableFogOriginCount - 1;
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Armed synthetic fog stage for segment {1} ({2}) because only {3} real fog origins are available. Campfire={4}, fallbackOrigin={5}.", "Fog&ColdControl", (int)advanceToSegment, advanceToSegment, availableFogOriginCount, ((Object)campfire).name, delayedOriginId));
			return delayedOriginId >= 0;
		}
		_pendingSyntheticFogSegmentId = -1;
		int num = (((Object)(object)_orbFogHandler != (Object)null) ? _orbFogHandler.currentID : (-1));
		TryMapSegmentToFogOriginId(campfire.advanceToSegment, availableFogOriginCount, out var originId);
		delayedOriginId = originId;
		int num2 = -1;
		int originId2 = -1;
		if ((Object)(object)Singleton<MapHandler>.Instance != (Object)null)
		{
			num2 = (int)MapHandler.CurrentSegmentNumber;
			TryMapSegmentToFogOriginId(MapHandler.CurrentSegmentNumber, availableFogOriginCount, out originId2);
			if (originId2 > delayedOriginId)
			{
				delayedOriginId = originId2;
			}
		}
		bool flag = num >= 0 && num2 > num;
		bool flag2 = num >= 0 && (int)campfire.advanceToSegment > num;
		if (num >= 0 && delayedOriginId <= num && (flag || flag2))
		{
			int num3 = Mathf.Min(num + 1, availableFogOriginCount - 1);
			if (num3 > delayedOriginId)
			{
				delayedOriginId = num3;
			}
		}
		if (num >= 0 && delayedOriginId <= num && (flag || flag2))
		{
			((BaseUnityPlugin)this).Logger.LogWarning((object)string.Format("[{0}] Fog origin did not advance after lighting campfire. availableOrigins={1}, currentOrigin={2}, sceneSegment={3}, campfireAdvance={4} ({5}), resolvedOrigin={6}, campfireName={7}.", "Fog&ColdControl", availableFogOriginCount, num, num2, (int)campfire.advanceToSegment, campfire.advanceToSegment, delayedOriginId, ((Object)campfire).name));
		}
		else
		{
			((BaseUnityPlugin)this).Logger.LogInfo((object)string.Format("[{0}] Resolved fog origin after lighting campfire. availableOrigins={1}, currentOrigin={2}, sceneSegment={3}, campfireAdvance={4} ({5}), resolvedOrigin={6}, campfireName={7}.", "Fog&ColdControl", availableFogOriginCount, num, num2, (int)campfire.advanceToSegment, campfire.advanceToSegment, delayedOriginId, ((Object)campfire).name));
		}
		return delayedOriginId >= 0;
	}
}
