using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Makes Undead Legacy's <c>UpdateMoveHelper</c> prefix sort last, so PathSmoothing's smoothing
	/// prefix runs before it. This is the fix for the visible zig-zag.
	///
	/// PathSmoothing smooths by overwriting <c>EntityMoveHelper.moveToPos</c> in a void prefix and
	/// relying on the method body to consume it. UL's prefix <em>is</em> the body (it reimplements the
	/// method and returns false). Both register at default priority, UL registers first, and every
	/// prefix runs regardless, so each tick:
	///
	///   1. ASPPathNavigate.UpdateNavigation() sets moveToPos to the next grid path node.
	///   2. UL's prefix reads it, moves the entity, returns false.
	///   3. PathSmoothing's prefix writes moveToPos = straight at the target.
	///   4. Next tick, step 1 overwrites it before anything reads it.
	///
	/// A prefix that replaces the body belongs after the void prefixes written to run before it, so
	/// UL's is demoted to <see cref="Priority.Last"/>. PathSmoothing's registration is untouched.
	/// </summary>
	internal static class MoveHelperPrefixOrderFix
	{
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
		/// Whether PathSmoothing's prefix is called before Undead Legacy's. Read live rather than at
		/// load time because <c>ps 0</c> / <c>ps 1</c> unregister and re-register PathSmoothing's.
		/// </summary>
		internal static bool OrderIsCorrect()
		{
			Patch[] ordered = OrderedPrefixes();
			int pathSmoothing = IndexOfAssembly(ordered, Refs.PathSmoothingAssemblyName);
			int undeadLegacy = IndexOfAssembly(ordered, Refs.UndeadLegacyAssemblyName);
			return pathSmoothing >= 0 && undeadLegacy >= 0 && pathSmoothing < undeadLegacy;
		}

		/// <summary>Call order by owning mod only, e.g. <c>PathSmoothing -&gt; UndeadLegacy</c>.</summary>
		internal static string ShortOrder()
		{
			Patch[] ordered = OrderedPrefixes();
			if (ordered.Length == 0)
			{
				return "no prefixes registered";
			}
			return string.Join(" -> ", Array.ConvertAll(ordered, AssemblyOf));
		}

		/// <summary>Call order with declaring type and priority, for <c>psul info</c> and the log.</summary>
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
		/// The prefixes in Harmony's call order: priority descending, then registration index
		/// ascending. Every report of the order goes through here so the forms cannot disagree.
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
