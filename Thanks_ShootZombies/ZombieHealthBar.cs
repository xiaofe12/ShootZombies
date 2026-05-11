using System;
using UnityEngine;
using UnityEngine.UI;

namespace ShootZombies;

public sealed class ZombieHealthBar : MonoBehaviour
{
	private const float BarWidth = 90f;
	private const float BarHeight = 10f;
	private const float HeightOffset = 1.35f;
	private const float VisibleDistance = 70f;
	private const float WorldScale = 0.012f;
	private const float ShowHealthThreshold = 0.999f;
	private const float VisibleAfterHitSeconds = 3f;

	private static readonly Color BackgroundColor = new Color(0.02f, 0.02f, 0.02f, 0.72f);
	private static readonly Color FillColor = new Color(0.95f, 0.05f, 0.04f, 0.95f);
	private static float _nextGlobalRefreshTime;

	private Character _character;
	private Transform _root;
	private Canvas _canvas;
	private RectTransform _rootRect;
	private RectTransform _fillRect;
	private float _health01 = 1f;
	private float _hideAfterUnscaledTime = -1f;

	public static void RefreshAll()
	{
		if (Time.unscaledTime < _nextGlobalRefreshTime)
		{
			return;
		}
		_nextGlobalRefreshTime = Time.unscaledTime + 0.35f;
		try
		{
			ZombieHealthBar[] bars = UnityEngine.Object.FindObjectsByType<ZombieHealthBar>((FindObjectsSortMode)0);
			foreach (ZombieHealthBar bar in bars)
			{
				if ((UnityEngine.Object)bar == (UnityEngine.Object)null)
				{
					continue;
				}
				if (!bar.ShouldStayVisible())
				{
					Destroy(((Component)bar).gameObject);
				}
			}
		}
		catch
		{
		}
	}

	public static void SetHealth(Character character, float health01)
	{
		if (!ShouldShowHealthBar(health01))
		{
			DestroyExisting(character);
			return;
		}
		Ensure(character, health01, overwrite: true);
	}

	public static void SetHealth(GameObject zombieObject, float health01)
	{
		if ((UnityEngine.Object)zombieObject == (UnityEngine.Object)null)
		{
			return;
		}
		Character character = zombieObject.GetComponent<Character>() ?? zombieObject.GetComponentInParent<Character>() ?? zombieObject.GetComponentInChildren<Character>(true);
		SetHealth(character, health01);
	}

	private static void Ensure(Character character, float health01, bool overwrite)
	{
		if (!IsZombieCharacter(character))
		{
			return;
		}
		ZombieHealthBar bar = ((Component)character).GetComponentInChildren<ZombieHealthBar>(true);
		if ((UnityEngine.Object)bar == (UnityEngine.Object)null)
		{
			GameObject root = new GameObject("ShootZombies_ZombieHealthBar", typeof(RectTransform));
			root.transform.SetParent(((Component)character).transform, false);
			bar = root.AddComponent<ZombieHealthBar>();
		}
		((Component)bar).gameObject.SetActive(true);
		if (overwrite)
		{
			bar._health01 = Mathf.Clamp01(health01);
			bar.RefreshVisibilityTimeout();
		}
		bar.Initialize(character, ((Component)bar).transform);
		if (overwrite)
		{
			bar.SetHealth01(health01);
		}
	}

	private static bool ShouldShowHealthBar(float health01)
	{
		float clamped = Mathf.Clamp01(health01);
		return clamped > 0.001f && clamped < ShowHealthThreshold;
	}

	private static void DestroyExisting(Character character)
	{
		if ((UnityEngine.Object)character == (UnityEngine.Object)null)
		{
			return;
		}
		try
		{
			ZombieHealthBar bar = ((Component)character).GetComponentInChildren<ZombieHealthBar>(true);
			if ((UnityEngine.Object)bar != (UnityEngine.Object)null)
			{
				Destroy(((Component)bar).gameObject);
			}
		}
		catch
		{
		}
	}

	private static bool IsZombieCharacter(Character character)
	{
		if ((UnityEngine.Object)character == (UnityEngine.Object)null)
		{
			return false;
		}
		try
		{
			if (character.isZombie)
			{
				return true;
			}
			if ((UnityEngine.Object)((Component)character).GetComponent<MushroomZombie>() != (UnityEngine.Object)null)
			{
				return true;
			}
			return (UnityEngine.Object)((Component)character).GetComponentInParent<MushroomZombie>() != (UnityEngine.Object)null || (UnityEngine.Object)((Component)character).GetComponentInChildren<MushroomZombie>(true) != (UnityEngine.Object)null;
		}
		catch
		{
			return false;
		}
	}

	private void Awake()
	{
		_root = ((Component)this).transform;
	}

	private void Initialize(Character character, Transform root)
	{
		_character = character;
		_root = root;
		EnsureInitialized();
		SetHealth01(_health01);
	}

	private bool EnsureInitialized()
	{
		if ((UnityEngine.Object)_root == (UnityEngine.Object)null)
		{
			_root = ((Component)this).transform;
		}
		if ((UnityEngine.Object)_character == (UnityEngine.Object)null)
		{
			_character = ((Component)this).GetComponentInParent<Character>() ?? ((Component)this).GetComponentInChildren<Character>(true);
		}
		if (!IsZombieCharacter(_character))
		{
			return false;
		}
		if ((UnityEngine.Object)_canvas == (UnityEngine.Object)null)
		{
			_canvas = ((Component)this).GetComponent<Canvas>() ?? ((Component)this).gameObject.AddComponent<Canvas>();
		}
		_canvas.renderMode = RenderMode.WorldSpace;
		_canvas.sortingOrder = 1500;
		CanvasScaler scaler = ((Component)this).GetComponent<CanvasScaler>() ?? ((Component)this).gameObject.AddComponent<CanvasScaler>();
		scaler.dynamicPixelsPerUnit = 120f;
		GraphicRaycaster raycaster = ((Component)this).GetComponent<GraphicRaycaster>() ?? ((Component)this).gameObject.AddComponent<GraphicRaycaster>();
		raycaster.enabled = false;

		_rootRect = ((Component)this).gameObject.GetComponent<RectTransform>();
		_rootRect.sizeDelta = new Vector2(BarWidth, BarHeight);
		_rootRect.pivot = new Vector2(0.5f, 0.5f);

		RectTransform backgroundRect = FindChildRect("Background");
		if ((UnityEngine.Object)backgroundRect == (UnityEngine.Object)null)
		{
			backgroundRect = CreateImage("Background", BackgroundColor).GetComponent<RectTransform>();
		}
		else
		{
			Image backgroundImage = ((Component)backgroundRect).GetComponent<Image>();
			if ((UnityEngine.Object)backgroundImage != (UnityEngine.Object)null)
			{
				backgroundImage.color = BackgroundColor;
				backgroundImage.raycastTarget = false;
			}
		}
		backgroundRect.anchorMin = Vector2.zero;
		backgroundRect.anchorMax = Vector2.one;
		backgroundRect.offsetMin = Vector2.zero;
		backgroundRect.offsetMax = Vector2.zero;

		_fillRect = FindChildRect("Fill");
		if ((UnityEngine.Object)_fillRect == (UnityEngine.Object)null)
		{
			_fillRect = CreateImage("Fill", FillColor).GetComponent<RectTransform>();
		}
		else
		{
			Image fillImage = ((Component)_fillRect).GetComponent<Image>();
			if ((UnityEngine.Object)fillImage != (UnityEngine.Object)null)
			{
				fillImage.color = FillColor;
				fillImage.raycastTarget = false;
			}
		}
		_fillRect.anchorMin = new Vector2(0f, 0f);
		_fillRect.anchorMax = new Vector2(_health01, 1f);
		_fillRect.pivot = new Vector2(0f, 0.5f);
		_fillRect.offsetMin = Vector2.zero;
		_fillRect.offsetMax = Vector2.zero;
		_fillRect.SetAsLastSibling();
		return (UnityEngine.Object)_root != (UnityEngine.Object)null && (UnityEngine.Object)_canvas != (UnityEngine.Object)null && (UnityEngine.Object)_fillRect != (UnityEngine.Object)null;
	}

	private RectTransform FindChildRect(string childName)
	{
		if ((UnityEngine.Object)_root == (UnityEngine.Object)null)
		{
			return null;
		}
		Transform child = _root.Find(childName);
		if ((UnityEngine.Object)child == (UnityEngine.Object)null)
		{
			return null;
		}
		return ((Component)child).GetComponent<RectTransform>();
	}

	private GameObject CreateImage(string name, Color color)
	{
		GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		child.transform.SetParent(_root, false);
		Image image = child.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
		return child;
	}

	private void SetHealth01(float health01)
	{
		_health01 = Mathf.Clamp01(health01);
		if (!ShouldShowHealthBar(_health01))
		{
			Destroy(((Component)this).gameObject);
			return;
		}
		if ((UnityEngine.Object)_fillRect != (UnityEngine.Object)null)
		{
			_fillRect.anchorMin = new Vector2(0f, 0f);
			_fillRect.anchorMax = new Vector2(_health01, 1f);
			_fillRect.offsetMin = Vector2.zero;
			_fillRect.offsetMax = Vector2.zero;
			((Component)_fillRect).gameObject.SetActive(_health01 > 0.001f);
		}
	}

	private void LateUpdate()
	{
		try
		{
			LateUpdateSafe();
		}
		catch
		{
			Destroy(((Component)this).gameObject);
		}
	}

	private void LateUpdateSafe()
	{
		if (!Plugin.IsModFeatureEnabled() || !ShouldStayVisible() || !EnsureInitialized())
		{
			Destroy(((Component)this).gameObject);
			return;
		}
		Camera camera = Camera.main;
		Character observer = Character.observedCharacter ?? Character.localCharacter;
		if ((UnityEngine.Object)camera == (UnityEngine.Object)null || (UnityEngine.Object)observer == (UnityEngine.Object)null)
		{
			_canvas.enabled = false;
			return;
		}
		Vector3 targetPosition = _character.Center + Vector3.up * HeightOffset;
		float distance = Vector3.Distance(observer.Center, targetPosition);
		Vector3 viewport = camera.WorldToViewportPoint(targetPosition);
		bool visible = distance <= VisibleDistance && viewport.z > 0f && viewport.x >= -0.08f && viewport.x <= 1.08f && viewport.y >= -0.08f && viewport.y <= 1.08f;
		_canvas.enabled = visible;
		if (!visible)
		{
			return;
		}
		_canvas.worldCamera = camera;
		_root.position = targetPosition;
		_root.rotation = camera.transform.rotation;
		float scale = WorldScale * Mathf.Lerp(1f, 1.7f, Mathf.Clamp01(distance / VisibleDistance));
		_root.localScale = new Vector3(scale, scale, scale);
	}

	private bool ShouldStayVisible()
	{
		return ShouldShowHealthBar(_health01) && !HasVisibilityTimedOut() && IsZombieCharacter(_character);
	}

	private void RefreshVisibilityTimeout()
	{
		_hideAfterUnscaledTime = Time.unscaledTime + VisibleAfterHitSeconds;
	}

	private bool HasVisibilityTimedOut()
	{
		return _hideAfterUnscaledTime >= 0f && Time.unscaledTime > _hideAfterUnscaledTime;
	}
}
