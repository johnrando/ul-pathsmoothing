using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Makes Undead Legacy's <c>UpdateMoveHelper</c> prefix sort last, so PathSmoothing's smoothing
	/// prefix runs before it instead of after.
	///
	/// This is the fix for the visible zig-zag. PathSmoothing's smoothing works by overwriting
	/// <c>EntityMoveHelper.moveToPos</c> in a void prefix - pointing the entity straight at its
	/// target instead of at the next grid path node - and relying on the method body it precedes to
	/// consume that value. UL's prefix *is* the body (it reimplements the method and returns false),
	/// but both prefixes are registered at default priority, and UL registers roughly a second
	/// earlier: it is a BepInEx plugin patched at chainloader time, while PathSmoothing patches from
	/// ModManager's InitMod. Equal priority falls back to registration index, so UL runs first.
	///
	/// Every prefix still runs - HarmonyX calls them all unconditionally and only consults the
	/// accumulated "run original" flag afterwards - but the order makes the write useless:
	///
	///   1. ASPPathNavigate.UpdateNavigation() sets moveToPos to the next grid path node. Every tick,
	///      immediately before UpdateMoveHelper.
	///   2. UL's prefix reads moveToPos - the grid node - moves the entity, returns false.
	///   3. PathSmoothing's prefix writes moveToPos = straight at the target.
	///   4. Next tick, step 1 overwrites it before anything reads it.
	///
	/// So the smoothing target is recomputed every tick and never once consumed, and entities follow
	/// the raw grid path, stepping diagonally between voxel rows.
	///
	/// Demoting UL's prefix to <see cref="Priority.Last"/> restores the contract PathSmoothing was
	/// written against: a prefix that replaces the method body belongs after the void prefixes that
	/// expect to run before that body. PathSmoothing's own registration is left completely untouched,
	/// so its <c>ps</c> enable/disable keeps working exactly as designed.
	/// </summary>
	internal static class MoveHelperPrefixOrderFix
	{
		/// <summary>Live prefix order, newest read; surfaced by the <c>psul</c> command.</summary>
		internal static string PrefixOrder = "not inspected";

		internal static bool Apply(Harmony harmony, MethodInfo undeadLegacyPrefix)
		{
			MethodBase original = AccessTools.Method(typeof(EntityMoveHelper), "UpdateMoveHelper");
			if (original == null)
			{
				Log.Error(Compat.LogPrefix + "EntityMoveHelper.UpdateMoveHelper not found.");
				return false;
			}

			Patch registered = FindPrefix(original, undeadLegacyPrefix);
			if (registered == null)
			{
				Log.Error(Compat.LogPrefix + "Undead Legacy's prefix is not registered on "
					+ "EntityMoveHelper.UpdateMoveHelper.");
				return false;
			}
			if (registered.priority == Priority.Last)
			{
				PrefixOrder = DescribeOrder(original);
				Log.Out(Compat.LogPrefix + "Undead Legacy's UpdateMoveHelper prefix already sorts last; "
					+ "leaving it alone.");
				return true;
			}

			harmony.Unpatch(original, undeadLegacyPrefix);
			harmony.Patch(original, prefix: new HarmonyMethod(undeadLegacyPrefix)
			{
				priority = Priority.Last
			});

			Patch reordered = FindPrefix(original, undeadLegacyPrefix);
			PrefixOrder = DescribeOrder(original);
			return reordered != null && reordered.priority == Priority.Last;
		}

		private static Patch FindPrefix(MethodBase original, MethodInfo patchMethod)
		{
			Patches info = Harmony.GetPatchInfo(original);
			if (info == null)
			{
				return null;
			}
			return info.Prefixes.FirstOrDefault(p => (object)p.PatchMethod == patchMethod);
		}

		/// <summary>
		/// Renders the prefixes in the order Harmony will call them: priority descending, then
		/// registration index ascending - the same rule <c>PatchSorter</c> applies.
		/// </summary>
		private static string DescribeOrder(MethodBase original)
		{
			Patches info = Harmony.GetPatchInfo(original);
			if (info == null || info.Prefixes.Count == 0)
			{
				return "no prefixes registered";
			}
			IEnumerable<string> ordered = info.Prefixes
				.OrderByDescending(p => p.priority)
				.ThenBy(p => p.index)
				.Select(Describe);
			return string.Join(" -> ", ordered.ToArray());
		}

		private static string Describe(Patch patch)
		{
			MethodInfo method = patch.PatchMethod;
			string owner = method.DeclaringType?.Assembly.GetName().Name ?? "?";
			return owner + "." + (method.DeclaringType?.Name ?? "?") + " (priority " + patch.priority + ")";
		}
	}
}
