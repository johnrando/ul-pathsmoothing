using System;
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
		private const string PathSmoothingAssembly = "PathSmoothing";

		private const string UndeadLegacyAssembly = "UndeadLegacy";

		internal static bool Apply(Harmony harmony, MethodInfo undeadLegacyPrefix)
		{
			MethodBase original = Original();
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
			return reordered != null && reordered.priority == Priority.Last;
		}

		/// <summary>
		/// Whether PathSmoothing's prefix will be called before Undead Legacy's - the whole point of
		/// this fix, and the one thing worth checking at a glance.
		///
		/// Read live rather than remembered from load time, because it can genuinely change
		/// afterwards: <c>ps 0</c> unpatches PathSmoothing's prefix and <c>ps 1</c> re-registers it
		/// with a fresh index. False when either prefix is missing, which is the honest answer while
		/// PathSmoothing is switched off.
		/// </summary>
		internal static bool OrderIsCorrect()
		{
			Patch[] ordered = OrderedPrefixes();
			int pathSmoothing = IndexOfAssembly(ordered, PathSmoothingAssembly);
			int undeadLegacy = IndexOfAssembly(ordered, UndeadLegacyAssembly);
			return pathSmoothing >= 0 && undeadLegacy >= 0 && pathSmoothing < undeadLegacy;
		}

		/// <summary>
		/// The call order as just the owning mods - <c>PathSmoothing -&gt; UndeadLegacy</c>. What
		/// <c>psul</c> shows in its short block; <see cref="DescribeOrder"/> is the same list with the
		/// detail kept.
		/// </summary>
		internal static string ShortOrder()
		{
			Patch[] ordered = OrderedPrefixes();
			if (ordered.Length == 0)
			{
				return "no prefixes registered";
			}
			return string.Join(" -> ", Array.ConvertAll(ordered, AssemblyOf));
		}

		/// <summary>
		/// The call order with each prefix's declaring type and priority, for <c>psul info</c> and the
		/// load-time log line.
		/// </summary>
		internal static string DescribeOrder()
		{
			Patch[] ordered = OrderedPrefixes();
			if (ordered.Length == 0)
			{
				return "no prefixes registered";
			}
			return string.Join(" -> ", Array.ConvertAll(ordered, Describe));
		}

		private static MethodBase Original()
		{
			return AccessTools.Method(typeof(EntityMoveHelper), "UpdateMoveHelper");
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
		/// The prefixes in the order Harmony will call them: priority descending, then registration
		/// index ascending - the same rule <c>PatchSorter</c> applies. Everything reported about the
		/// order goes through here, so the short form, the long form and the verdict cannot disagree.
		/// </summary>
		private static Patch[] OrderedPrefixes()
		{
			MethodBase original = Original();
			Patches info = original == null ? null : Harmony.GetPatchInfo(original);
			if (info == null)
			{
				return new Patch[0];
			}
			Patch[] prefixes = new Patch[info.Prefixes.Count];
			info.Prefixes.CopyTo(prefixes, 0);
			Array.Sort(prefixes, ComparePatches);
			return prefixes;
		}

		/// <summary>Harmony's own rule: priority descending, then registration index ascending.</summary>
		private static int ComparePatches(Patch left, Patch right)
		{
			int byPriority = right.priority.CompareTo(left.priority);
			return byPriority != 0 ? byPriority : left.index.CompareTo(right.index);
		}

		private static int IndexOfAssembly(Patch[] ordered, string assemblyName)
		{
			for (int i = 0; i < ordered.Length; i++)
			{
				if (AssemblyOf(ordered[i]) == assemblyName)
				{
					return i;
				}
			}
			return -1;
		}

		private static string AssemblyOf(Patch patch)
		{
			return patch.PatchMethod.DeclaringType?.Assembly.GetName().Name ?? "?";
		}

		private static string Describe(Patch patch)
		{
			MethodInfo method = patch.PatchMethod;
			return AssemblyOf(patch) + "." + (method.DeclaringType?.Name ?? "?")
				+ " (priority " + patch.priority + ")";
		}
	}
}
