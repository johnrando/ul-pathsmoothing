using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Reports which Undead Legacy build is running and warns when it is not the one these patches
	/// were tested against. It does not gate anything - UL's current branch is numbered 2.7.x and
	/// takes a new patch number regularly, so refusing to install on any unrecognised number would
	/// break the mod on routine updates.
	///
	/// The real safety net is structural, not version-based: each fix checks the code it targets and
	/// logs loudly if it no longer matches. The transpiler in particular matches an exact IL pattern
	/// and removes itself when it does not find it. What that cannot catch is a *semantic* change -
	/// same code shape, different meaning - which is what this warning is for.
	///
	/// Only two of UL's version markers are trustworthy, both on <c>H_UndeadLegacy</c>: the
	/// <c>[BepInPlugin]</c> attribute and the <c>pluginVersion</c> literal. The assembly version is
	/// hardcoded 1.0.0.0 and <c>ModInfo.xml</c> lags reality (it read 2.7.01 on a 2.7.15 install), so
	/// neither is used here.
	/// </summary>
	internal static class UndeadLegacyVersion
	{
		/// <summary>
		/// The Undead Legacy build whose movement code was actually read and confirmed to match what
		/// these patches expect. Bump only after re-checking UL's movement code against what each fix
		/// targets - bumping it alone just silences the warning.
		/// </summary>
		internal const string TestedAgainst = "2.7.15";

		internal static string DetectedRaw = "not detected";

		internal static string DetectedSource = "none";

		/// <summary>One-line summary for the <c>psul</c> console command.</summary>
		internal static string Status = "not checked";

		/// <summary>
		/// Logs the detected version, warning if it is anything other than
		/// <see cref="TestedAgainst"/>. Never blocks: patching continues either way.
		/// </summary>
		internal static void Report()
		{
			string raw = Detect();
			if (raw == null)
			{
				DetectedRaw = "unknown";
				Status = "unknown - only tested under " + TestedAgainst;
				Log.Warning(Compat.LogPrefix + "Could not read Undead Legacy's version. This patch has "
					+ "only been tested under Undead Legacy " + TestedAgainst + ". It will still install, "
					+ "and each fix logs an error if the code it targets no longer matches.");
				return;
			}

			DetectedRaw = raw;
			Version detected = Parse(raw);
			Version tested = Parse(TestedAgainst);

			if (detected != null && tested != null && detected == tested)
			{
				Status = raw + " - tested";
				Log.Out(Compat.LogPrefix + "Undead Legacy " + raw + " detected (from " + DetectedSource
					+ "), which is the version this patch was tested against.");
				return;
			}

			Status = raw + " - UNTESTED, only tested under " + TestedAgainst;
			Log.Warning(Compat.LogPrefix + "Undead Legacy " + raw + " detected (from " + DetectedSource
				+ "), but this patch has only been tested under Undead Legacy " + TestedAgainst
				+ ". It will still install, and each fix logs an error if the code it targets no longer "
				+ "matches - but re-verify movement behaviour, and check 'psul' if zombies look wrong.");
		}

		private static string Detect()
		{
			Type plugin = Refs.UndeadLegacyType("H_UndeadLegacy");
			if (plugin == null)
			{
				return null;
			}
			return ReadFromBepInPluginAttribute(plugin) ?? ReadFromVersionConstant(plugin);
		}

		/// <summary>
		/// Reads the third argument of <c>[BepInPlugin(guid, name, version)]</c>. Uses
		/// <see cref="CustomAttributeData"/> so this assembly needs no reference to BepInEx.
		/// </summary>
		private static string ReadFromBepInPluginAttribute(Type plugin)
		{
			try
			{
				foreach (CustomAttributeData attribute in CustomAttributeData.GetCustomAttributes(plugin))
				{
					if (attribute.Constructor?.DeclaringType?.Name != "BepInPlugin")
					{
						continue;
					}
					IList<CustomAttributeTypedArgument> args = attribute.ConstructorArguments;
					if (args.Count < 3)
					{
						continue;
					}
					if (args[2].Value is string version && version.Length > 0)
					{
						DetectedSource = "[BepInPlugin] attribute";
						return version;
					}
				}
			}
			catch (Exception e)
			{
				Log.Warning(Compat.LogPrefix + "Could not read Undead Legacy's [BepInPlugin] attribute: "
					+ e.Message);
			}
			return null;
		}

		private static string ReadFromVersionConstant(Type plugin)
		{
			try
			{
				FieldInfo field = AccessTools.Field(plugin, "pluginVersion");
				if (field != null && field.IsLiteral && field.GetRawConstantValue() is string version
					&& version.Length > 0)
				{
					DetectedSource = "pluginVersion constant";
					return version;
				}
			}
			catch (Exception e)
			{
				Log.Warning(Compat.LogPrefix + "Could not read Undead Legacy's pluginVersion constant: "
					+ e.Message);
			}
			return null;
		}

		/// <summary>
		/// Tolerant parse, normalised to exactly major.minor.patch - the shape Undead Legacy uses.
		///
		/// Trims a leading 'v' and anything from the first non-version character, so "v2.7.15-beta"
		/// reads as 2.7.15 and a branch label like "2.7.x" reads as 2.7.0. Missing components become 0
		/// and a fourth is dropped, so "2.7.15.0" matches "2.7.15" rather than counting as different -
		/// without that, System.Version leaves absent components at -1.
		/// </summary>
		private static Version Parse(string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return null;
			}
			string text = raw.Trim();
			if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
			{
				text = text.Substring(1);
			}
			int end = 0;
			while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
			{
				end++;
			}
			text = text.Substring(0, end).Trim('.');
			if (text.Length == 0)
			{
				return null;
			}
			if (text.IndexOf('.') < 0)
			{
				text += ".0";
			}
			if (!Version.TryParse(text, out Version parsed))
			{
				return null;
			}
			return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
		}
	}
}
