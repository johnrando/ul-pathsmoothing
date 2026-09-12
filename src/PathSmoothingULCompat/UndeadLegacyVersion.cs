using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Reports the running Undead Legacy build and warns when it is outside the tested range. Advisory
	/// only: UL's 2.7.x branch takes new patch numbers routinely, and each fix already checks the code
	/// it targets. The warning covers what that cannot: a semantic change with the same code shape.
	///
	/// Only the <c>[BepInPlugin]</c> attribute and the <c>pluginVersion</c> literal on
	/// <c>H_UndeadLegacy</c> are trustworthy. The assembly version is hardcoded 1.0.0.0 and
	/// <c>ModInfo.xml</c> lags reality.
	/// </summary>
	internal static class UndeadLegacyVersion
	{
		/// <summary>Oldest Undead Legacy build these patches were run against.</summary>
		internal const string TestedFrom = "2.7.15";

		/// <summary>Newest build tested. Bump only after re-checking UL's movement code against each fix.</summary>
		internal const string TestedTo = "2.7.31";

		private static string TestedRange =>
			TestedFrom == TestedTo ? TestedFrom : TestedFrom + " - " + TestedTo;

		internal static string DetectedRaw = "not detected";

		internal static string DetectedSource = "none";

		/// <summary>One-line summary for <c>psul</c>.</summary>
		internal static string Status = "not checked";

		/// <summary>Logs the detected version, warning if outside the tested range. Never blocks.</summary>
		internal static void Report()
		{
			string raw = Detect();
			if (raw == null)
			{
				DetectedRaw = "unknown";
				Status = "unknown - only tested under " + TestedRange;
				Log.Warning(Compat.LogPrefix + "Could not read Undead Legacy's version. This patch has "
					+ "only been tested under Undead Legacy " + TestedRange + ". It will still install, "
					+ "and each fix logs an error if the code it targets no longer matches.");
				return;
			}

			DetectedRaw = raw;
			Version detected = Parse(raw);
			Version from = Parse(TestedFrom);
			Version to = Parse(TestedTo);

			if (detected != null && from != null && to != null && detected >= from && detected <= to)
			{
				Status = raw + " - tested";
				Log.Out(Compat.LogPrefix + "Undead Legacy " + raw + " detected (from " + DetectedSource
					+ "), which is within the range this patch was tested against (" + TestedRange + ").");
				return;
			}

			Status = raw + " - UNTESTED, only tested under " + TestedRange;
			Log.Warning(Compat.LogPrefix + "Undead Legacy " + raw + " detected (from " + DetectedSource
				+ "), but this patch has only been tested under Undead Legacy " + TestedRange
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

		/// <summary>Third argument of <c>[BepInPlugin(guid, name, version)]</c>, read without referencing BepInEx.</summary>
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
		/// Tolerant parse normalised to major.minor.patch: trims a leading 'v' and any suffix, so
		/// "v2.7.15-beta" is 2.7.15, "2.7.x" is 2.7.0 and "2.7.15.0" equals "2.7.15".
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
