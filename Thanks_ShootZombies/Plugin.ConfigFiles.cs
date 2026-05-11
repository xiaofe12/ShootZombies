using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace ShootZombies;

public partial class Plugin
{
	private void RefreshLocalizedConfigFiles(bool isChinese)
	{
		try
		{
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			config?.Save();
			CleanupAuxiliaryConfigFiles();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RefreshLocalizedConfigFiles failed: " + DescribeReflectionException(ex)));
		}
	}

	private void PreparePrimaryConfigFile()
	{
		try
		{
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath))
			{
				string directoryName = Path.GetDirectoryName(canonicalConfigPath);
				if (!string.IsNullOrWhiteSpace(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
			}
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath) && !File.Exists(canonicalConfigPath))
			{
				string text = GetLatestExistingConfigPath(new string[7]
				{
					GetCanonicalConfigPath(),
					GetPreviousCanonicalConfigPath(),
					GetLegacyCanonicalConfigPath(),
					Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.zh-CN.cfg"),
					Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.en.cfg"),
					GetLocalizedConfigMirrorPath(),
					GetPreviousLocalizedConfigMirrorPath()
				});
				if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
				{
					File.Copy(text, canonicalConfigPath, overwrite: true);
				}
			}
			CleanupAuxiliaryConfigFiles();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] PreparePrimaryConfigFile failed: " + ex.Message));
		}
	}

	private static string GetLatestExistingConfigPath(IEnumerable<string> candidatePaths)
	{
		string text = string.Empty;
		DateTime dateTime = DateTime.MinValue;
		if (candidatePaths == null)
		{
			return text;
		}
		foreach (string candidatePath in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
			{
				continue;
			}
			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(candidatePath);
			if (string.IsNullOrWhiteSpace(text) || lastWriteTimeUtc > dateTime)
			{
				text = candidatePath;
				dateTime = lastWriteTimeUtc;
			}
		}
		return text;
	}

	private static string ReadConfigMetadataValue(string configPath, string metadataPrefix)
	{
		if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(metadataPrefix) || !File.Exists(configPath))
		{
			return string.Empty;
		}
		try
		{
			foreach (string item in File.ReadAllLines(configPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				string text = (item ?? string.Empty).Trim();
				if (text.StartsWith(metadataPrefix, StringComparison.Ordinal))
				{
					return text.Substring(metadataPrefix.Length).Trim();
				}
			}
		}
		catch
		{
		}
		return string.Empty;
	}

	private string ReadStoredZombieBehaviorDifficultySelection()
	{
		string text = ReadConfigMetadataValue(GetCanonicalConfigPath(), ConfigMetadataZombieDifficultyPrefix);
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		return NormalizeZombieBehaviorDifficultySelection(text);
	}

	private void CleanupAuxiliaryConfigFiles()
	{
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath))
			{
				hashSet.Add(canonicalConfigPath);
			}
			HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] array2 = new string[7]
			{
				GetPreviousCanonicalConfigPath(),
				GetLegacyCanonicalConfigPath(),
				Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.zh-CN.cfg"),
				Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.en.cfg"),
				GetLocalizedConfigMirrorPath(),
				GetPreviousLocalizedConfigMirrorPath(),
				GetLegacyLocalizedConfigMirrorPath()
			};
			foreach (string item2 in array2)
			{
				if (!string.IsNullOrWhiteSpace(item2) && !hashSet.Contains(item2))
				{
					hashSet2.Add(item2);
				}
			}
			foreach (string item3 in hashSet2)
			{
				if (File.Exists(item3))
				{
					File.Delete(item3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] CleanupAuxiliaryConfigFiles failed: " + ex.Message));
		}
	}

	private void NormalizeCanonicalConfigEncoding()
	{
		try
		{
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath) && File.Exists(canonicalConfigPath))
			{
				string contents = File.ReadAllText(canonicalConfigPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				File.WriteAllText(canonicalConfigPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] NormalizeCanonicalConfigEncoding failed: " + ex.Message));
		}
	}

	private static string GetCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, CanonicalConfigFileName);
	}

	private string GetActivePluginConfigPath()
	{
		if (!string.IsNullOrWhiteSpace(_pluginConfigPath))
		{
			return _pluginConfigPath;
		}
		return GetCanonicalConfigPath();
	}

	private static string GetConfigFilePath(ConfigFile config)
	{
		return (((object)config)?.GetType().GetProperty("ConfigFilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) as string) ?? string.Empty;
	}

	private static string GetPreviousCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, PreviousCanonicalConfigFileName);
	}

	private static string GetLegacyCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, LegacyCanonicalConfigFileName);
	}

	private static string GetLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, LocalizedConfigMirrorFileName);
	}

	private static string GetPreviousLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, PreviousLocalizedConfigMirrorFileName);
	}

	private static string GetLegacyLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, LegacyLocalizedConfigMirrorFileName);
	}
}
