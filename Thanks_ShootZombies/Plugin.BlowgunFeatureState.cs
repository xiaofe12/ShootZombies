namespace ShootZombies;

public partial class Plugin
{
	private void EnsureVanillaBlowgunFunctionalityWhenWeaponDisabled()
	{
		if (IsWeaponFeatureEnabled())
		{
			_restoredVanillaBlowgunFunctionalityForDisabledFeature = false;
			return;
		}
		if (_restoredVanillaBlowgunFunctionalityForDisabledFeature)
		{
			return;
		}
		RestoreVanillaBlowgunFeatureState();
	}

	private void RestoreVanillaBlowgunFeatureState()
	{
		BlowgunInfiniteUsePatch.RestoreVanillaSingleUseOnAllBlowguns();
		HideUseItemProgressPatch.RestoreVanillaUseProgressOnAllUi();
		_lastChargeSyncItemId = int.MinValue;
		_hasWeapon = false;
		_restoredVanillaBlowgunFunctionalityForDisabledFeature = true;
	}
}
