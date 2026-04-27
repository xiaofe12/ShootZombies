using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildShootZombiesWeaponBundle
{
    private const string BundleName = "ak47_shootzombies.peakbundle";
    private const string OutputDirectoryEnvironmentVariable = "CODEX_SHOOTZOMBIES_WEAPON_BUNDLE_OUTPUT";
    private const string TriggerFileRelativePath = "Assets/CodexBuild/BuildShootZombiesWeaponBundle.trigger.txt";

    private static bool _isTriggeredBuildRunning;
    private static double _nextTriggerPollTime;

    private static readonly string[] AssetPaths =
    {
        "Assets/Weapon/Weapon.prefab",
        "Assets/Weapon/AK47_icon.png",
        "Assets/Weapon/MPX_icon.png",
        "Assets/Weapon/HK416_icon.png",
    };

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update -= PollTriggerFile;
        EditorApplication.update += PollTriggerFile;
    }

    [MenuItem("Tools/Codex/Build ShootZombies Weapon Bundle")]
    public static void RunFromMenu()
    {
        Run();
    }

    public static void Run()
    {
        var outputDirectory = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(
                $"Environment variable '{OutputDirectoryEnvironmentVariable}' is required.");
        }

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        foreach (var assetPath in AssetPaths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
            {
                throw new FileNotFoundException($"Asset not found: {assetPath}");
            }
        }

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = AssetPaths,
        };

        var target = EditorUserBuildSettings.activeBuildTarget;
        var manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
            target);

        if (manifest == null)
        {
            throw new InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");
        }

        var bundlePath = Path.Combine(outputDirectory, BundleName);
        if (!File.Exists(bundlePath))
        {
            throw new FileNotFoundException($"Bundle was not produced: {bundlePath}");
        }

        Debug.Log($"[Codex] ShootZombies weapon bundle built: {bundlePath}");
    }

    private static void PollTriggerFile()
    {
        if (_isTriggeredBuildRunning || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup < _nextTriggerPollTime)
        {
            return;
        }

        _nextTriggerPollTime = EditorApplication.timeSinceStartup + 2.0;

        var triggerPath = GetTriggerFileAbsolutePath();
        if (!File.Exists(triggerPath))
        {
            return;
        }

        var outputDirectory = File.ReadAllText(triggerPath).Trim();
        try
        {
            _isTriggeredBuildRunning = true;
            Environment.SetEnvironmentVariable(OutputDirectoryEnvironmentVariable, outputDirectory);
            Run();
            Debug.Log($"[Codex] Triggered ShootZombies bundle build from: {triggerPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Codex] Triggered ShootZombies bundle build failed: {ex}");
        }
        finally
        {
            _isTriggeredBuildRunning = false;
            try
            {
                File.Delete(triggerPath);
            }
            catch
            {
            }

            AssetDatabase.Refresh();
        }
    }

    private static string GetTriggerFileAbsolutePath()
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), TriggerFileRelativePath));
    }
}
