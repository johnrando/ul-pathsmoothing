namespace PathSmoothingULCompat
{
	/// <summary>
	/// Mirrors PathSmoothing's on/off state. Its <c>ps</c> command unpatches only the "PathSmoothing"
	/// Harmony id, which these patches are not under, so they follow <c>Common.Enable</c> /
	/// <c>Common.Disable</c> instead of staying active over unsmoothed paths.
	/// </summary>
	internal static class SmoothingToggleTracker
	{
		internal static void EnablePostfix()
		{
			Compat.SmoothingActive = true;
		}

		internal static void DisablePostfix()
		{
			Compat.SmoothingActive = false;
		}
	}
}
