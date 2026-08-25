namespace PathSmoothingULCompat
{
	/// <summary>
	/// Entry point. ModManager loads every mod assembly before calling any InitMod, so both
	/// PathSmoothing and Undead Legacy are guaranteed to be resolvable from here.
	/// </summary>
	public class ModApi : IModApi
	{
		public void InitMod(Mod _modInstance)
		{
			Compat.Apply();
		}
	}
}
