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
		RestoreWeaponItemInteractionRestrictionsOnAllBlowguns();
		HideUseItemProgressPatch.RestoreVanillaUseProgressOnAllUi();
		ItemUIDataPatch.ForceRefreshVisibleUi();
		_lastChargeSyncItemId = int.MinValue;
		_hasWeapon = false;
		_restoredVanillaBlowgunFunctionalityForDisabledFeature = true;
	}
}
