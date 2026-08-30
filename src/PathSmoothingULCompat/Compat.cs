using System;
using System.Reflection;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Applies the two compatibility patches. Both mods are resolved late and each patch is gated on
	/// its own prerequisites, so an install missing either mod - or a future version that has moved
	/// something - degrades to a log line instead of an exception.
	/// </summary>
	internal static class Compat
	{
		internal const string LogPrefix = "[PathSmoothing/UL] ";

		private const string HarmonyId = "PathSmoothing.UndeadLegacyCompat";

		private const string NotRunYet = "not applied - mod init has not run";

		/// <summary>
		/// Whether PathSmoothing is currently switched on, tracked via
		/// <see cref="SmoothingToggleTracker"/>. Assumed on until told otherwise: PathSmoothing may
		/// not have run its own InitMod yet when this is first read.
		/// </summary>
		internal static bool SmoothingActive = true;

		/// <summary>Outcome of each fix, as reported by the <c>psul</c> console command.</summary>
		internal static string EndOfPathFixStatus = NotRunYet;

		internal static string PrefixOrderFixStatus = NotRunYet;

		internal static string ToggleTrackingStatus = NotRunYet;

		/// <summary>
		/// The same two outcomes as booleans, so <c>psul</c>'s verdict line is read off state rather
		/// than off the wording of the prose above.
		/// </summary>
		internal static bool PrefixOrderFixApplied;

		internal static bool EndOfPathFixApplied;

		/// <summary>Checks the end-of-path transpiler rewrote; 1 on a healthy install.</summary>
		internal static int EndOfPathSites;

		private static bool applied;

		internal static void Apply()
		{
			if (applied)
			{
				return;
			}
			applied = true;
			try
			{
				ApplyPatches();
			}
			catch (Exception e)
			{
				Log.Error(LogPrefix + "Failed to apply compatibility patches.");
				Log.Exception(e);
			}
		}

		private static void ApplyPatches()
		{
			// ModManager loads every mod assembly before calling any IModApi.InitMod, and UL's own
			// Harmony patches are applied earlier still (it is a BepInEx plugin), so both mods are
			// fully present by the time this runs regardless of mod folder ordering.
			if (!Refs.PathSmoothingPresent)
			{
				SetAllStatuses("not applied - PathSmoothing is not installed");
				Log.Out(LogPrefix + "PathSmoothing is not installed - nothing to patch.");
				return;
			}
			if (!Refs.UndeadLegacyPresent)
			{
				SetAllStatuses("not applied - Undead Legacy is not installed");
				Log.Out(LogPrefix + "Undead Legacy is not installed - PathSmoothing needs no help here.");
				return;
			}

			// Advisory only. UL's branch is numbered 2.7.x and takes new patch numbers routinely, so
			// gating on the version would break the mod on ordinary updates. Each fix below checks
			// the code it targets instead, and says so when it no longer matches.
			UndeadLegacyVersion.Report();

			Refs.ResolveSharedSets();

			Harmony harmony = new Harmony(HarmonyId);
			TrackSmoothingToggle(harmony);
			ApplyPrefixOrderFix(harmony);
			ApplyEndOfPathFix(harmony);
		}

		/// <summary>
		/// The important one: without it PathSmoothing's smoothing is computed every tick and never
		/// consumed, and entities zig-zag along the raw grid path.
		/// </summary>
		private static void ApplyPrefixOrderFix(Harmony harmony)
		{
			if (!Refs.ResolveUndeadLegacyMoveHelperPrefix())
			{
				PrefixOrderFixStatus = "NOT APPLIED - could not find UL's UpdateMoveHelper prefix";
				Log.Error(LogPrefix + "Prefix-order fix NOT applied: PathSmoothing's smoothing will be "
					+ "overwritten before Undead Legacy reads it, and entities will follow the raw grid "
					+ "path.");
				return;
			}

			if (!MoveHelperPrefixOrderFix.Apply(harmony, Refs.UndeadLegacyMoveHelperPrefix))
			{
				PrefixOrderFixStatus = "NOT APPLIED - could not reorder (see log)";
				Log.Error(LogPrefix + "Prefix-order fix NOT applied: PathSmoothing's smoothing will be "
					+ "overwritten before Undead Legacy reads it, and entities will follow the raw grid "
					+ "path.");
				return;
			}

			PrefixOrderFixApplied = true;
			PrefixOrderFixStatus = "applied - UL's UpdateMoveHelper prefix now sorts last";
			Log.Out(LogPrefix + "Prefix-order fix applied: Undead Legacy's UpdateMoveHelper prefix now "
				+ "runs after PathSmoothing's, so the smoothed move target is the one it reads. Order: "
				+ MoveHelperPrefixOrderFix.DescribeOrder());
		}

		private static void SetAllStatuses(string status)
		{
			EndOfPathFixStatus = status;
			PrefixOrderFixStatus = status;
			ToggleTrackingStatus = status;
		}

		private static void TrackSmoothingToggle(Harmony harmony)
		{
			MethodInfo enable = Refs.PathSmoothingCommonMethod("Enable");
			MethodInfo disable = Refs.PathSmoothingCommonMethod("Disable");
			if (enable == null || disable == null)
			{
				ToggleTrackingStatus = "not applied - PathSmoothing.Common.Enable/Disable not found";
				Log.Warning(LogPrefix + "Cannot follow PathSmoothing's enabled state; the 'ps' console "
					+ "command will not switch these patches off.");
				return;
			}
			harmony.Patch(enable, postfix: new HarmonyMethod(
				AccessTools.Method(typeof(SmoothingToggleTracker), nameof(SmoothingToggleTracker.EnablePostfix))));
			harmony.Patch(disable, postfix: new HarmonyMethod(
				AccessTools.Method(typeof(SmoothingToggleTracker), nameof(SmoothingToggleTracker.DisablePostfix))));
			ToggleTrackingStatus = "applied - 'ps' also switches these patches";
		}

		private static void ApplyEndOfPathFix(Harmony harmony)
		{
			if (!Refs.ResolveUndeadLegacyMoveHelperPrefix() || !Refs.ResolveGetPathLengthDistanceSq()
				|| !EndOfPathCheckFix.Bind(Refs.GetPathLengthDistanceSq))
			{
				EndOfPathFixStatus = "NOT APPLIED - could not resolve what it patches (see log)";
				Log.Error(LogPrefix + "End-of-path fix NOT applied: entities will treat a smoothed path "
					+ "as finished while still far from its end.");
				return;
			}

			MethodInfo target = Refs.UndeadLegacyMoveHelperPrefix;
			harmony.Patch(target, transpiler: new HarmonyMethod(
				AccessTools.Method(typeof(EndOfPathCheckFix), nameof(EndOfPathCheckFix.Transpiler))));

			int sites = EndOfPathCheckFix.PatchedSites;
			if (sites == 0)
			{
				harmony.Unpatch(target, HarmonyPatchType.Transpiler, HarmonyId);
				EndOfPathFixStatus = "NOT APPLIED - no matching check found in Undead Legacy";
				Log.Error(LogPrefix + "End-of-path fix NOT applied: no 'NodeCountRemaining() <= 1' check "
					+ "found in Undead Legacy's UpdateMoveHelper prefix. Undead Legacy has probably "
					+ "changed and this patch needs updating.");
				return;
			}
			if (sites != 1)
			{
				Log.Warning(LogPrefix + "End-of-path fix rewrote " + sites + " checks in Undead Legacy's "
					+ "UpdateMoveHelper prefix; 1 was expected.");
			}
			EndOfPathFixApplied = true;
			EndOfPathSites = sites;
			EndOfPathFixStatus = "applied - " + sites + " check(s) rewritten in UL's UpdateMoveHelper prefix";
			Log.Out(LogPrefix + "End-of-path fix applied to Undead Legacy's UpdateMoveHelper prefix ("
				+ sites + " check(s) rewritten).");
		}
	}
}
