namespace PathSmoothingULCompat
{
	/// <summary>
	/// Mirrors PathSmoothing's own on/off state. The <c>ps</c> / <c>pathsmoothing</c> console command
	/// unpatches and re-patches everything under the "PathSmoothing" Harmony id; these patches live
	/// under a different id and so survive it. Following <c>Common.Enable</c> / <c>Common.Disable</c>
	/// keeps the re-applied fixes in step instead of leaving them active over unsmoothed paths.
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
