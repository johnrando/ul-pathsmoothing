using System;
using System.Collections.Generic;
using System.Reflection;
using GamePath;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Late-bound handles onto the two mods this patch sits between. Neither is referenced
	/// at compile time: the patch has to load, say something useful and then stay out of the
	/// way when either mod is absent or has moved the member we need.
	/// </summary>
	internal static class Refs
	{
		private const string PathSmoothingAssemblyName = "PathSmoothing";
		private const string UndeadLegacyAssemblyName = "UndeadLegacy";

		/// <summary>
		/// <c>PathSmoothing.Utils.GetPathLengthDistanceSq(PathEntity)</c> - the real path-length
		/// calculation PathSmoothing substitutes for <c>PathEntity.NodeCountRemaining()</c>.
		/// </summary>
		internal static MethodInfo GetPathLengthDistanceSq;

		/// <summary>
		/// <c>PathSmoothing.Common.DontSmoothEntities</c>. The field is <c>static readonly</c>, so
		/// caching the set itself instead of the field is safe.
		/// </summary>
		internal static HashSet<EntityAlive> DontSmoothEntities;

		/// <summary>
		/// <c>PathSmoothing.Common.DirectMovers</c> - entities PathSmoothing has cleared to ignore
		/// their grid path and head straight for the target. Diagnostics only.
		/// </summary>
		internal static HashSet<EntityAlive> DirectMovers;

		/// <summary>
		/// UL's <c>H_ZombieDiggingPatch+EntityMoveHelper_UpdateMoveHelper.Prefix</c>: a from-scratch
		/// reimplementation of <c>EntityMoveHelper.UpdateMoveHelper</c> that returns false on every
		/// exit path, so the vanilla body PathSmoothing transpiles never executes.
		/// </summary>
		internal static MethodInfo UndeadLegacyMoveHelperPrefix;

		internal static bool PathSmoothingPresent => FindAssembly(PathSmoothingAssemblyName) != null;

		internal static bool UndeadLegacyPresent => FindAssembly(UndeadLegacyAssemblyName) != null;

		/// <summary>Looks up a type in Undead Legacy's assembly, logging if it is missing.</summary>
		internal static Type UndeadLegacyType(string typeName)
		{
			return FindType(UndeadLegacyAssemblyName, typeName);
		}

		/// <summary>Looks up a parameterless method on <c>PathSmoothing.Common</c>.</summary>
		internal static MethodInfo PathSmoothingCommonMethod(string name)
		{
			return FindMethod(PathSmoothingAssemblyName, "PathSmoothing.Common", name, Type.EmptyTypes);
		}

		/// <summary>
		/// Resolves UL's UpdateMoveHelper prefix. Needed by both the prefix-order fix and the
		/// end-of-path transpiler, so it is resolved on its own.
		/// </summary>
		internal static bool ResolveUndeadLegacyMoveHelperPrefix()
		{
			if (UndeadLegacyMoveHelperPrefix != null)
			{
				return true;
			}

			Type diggingPatch = FindType(UndeadLegacyAssemblyName, "H_ZombieDiggingPatch");
			if (diggingPatch == null)
			{
				return false;
			}
			Type inner = diggingPatch.GetNestedType(
				"EntityMoveHelper_UpdateMoveHelper", BindingFlags.Public | BindingFlags.NonPublic);
			if (inner == null)
			{
				Log.Error(Compat.LogPrefix
					+ "UndeadLegacy H_ZombieDiggingPatch has no nested EntityMoveHelper_UpdateMoveHelper type.");
				return false;
			}
			UndeadLegacyMoveHelperPrefix = AccessTools.Method(inner, "Prefix");
			if (UndeadLegacyMoveHelperPrefix == null)
			{
				Log.Error(Compat.LogPrefix
					+ "UndeadLegacy H_ZombieDiggingPatch+EntityMoveHelper_UpdateMoveHelper has no Prefix method.");
				return false;
			}
			return true;
		}

		/// <summary>Resolves the path-length calculation the end-of-path transpiler calls.</summary>
		internal static bool ResolveGetPathLengthDistanceSq()
		{
			GetPathLengthDistanceSq = FindMethod(
				PathSmoothingAssemblyName, "PathSmoothing.Utils", "GetPathLengthDistanceSq", typeof(PathEntity));
			if (GetPathLengthDistanceSq == null)
			{
				return false;
			}
			if (GetPathLengthDistanceSq.ReturnType != typeof(float))
			{
				Log.Error(Compat.LogPrefix + "PathSmoothing.Utils.GetPathLengthDistanceSq returns "
					+ GetPathLengthDistanceSq.ReturnType.Name + ", expected float.");
				return false;
			}
			return true;
		}

		/// <summary>Resolves PathSmoothing's two shared entity sets. Best effort; diagnostics use them too.</summary>
		internal static bool ResolveSharedSets()
		{
			DontSmoothEntities = ResolveSet("DontSmoothEntities");
			DirectMovers = ResolveSet("DirectMovers");
			return DontSmoothEntities != null;
		}

		private static HashSet<EntityAlive> ResolveSet(string fieldName)
		{
			FieldInfo field = FindField(PathSmoothingAssemblyName, "PathSmoothing.Common", fieldName);
			if (field == null)
			{
				return null;
			}
			HashSet<EntityAlive> set = field.GetValue(null) as HashSet<EntityAlive>;
			if (set == null)
			{
				Log.Error(Compat.LogPrefix + "PathSmoothing.Common." + fieldName + " is not a "
					+ "HashSet<EntityAlive> (found " + field.FieldType.FullName + ").");
			}
			return set;
		}

		private static Assembly FindAssembly(string simpleName)
		{
			Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < loaded.Length; i++)
			{
				if (string.Equals(loaded[i].GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
				{
					return loaded[i];
				}
			}
			return null;
		}

		private static Type FindType(string assemblyName, string typeName)
		{
			Assembly assembly = FindAssembly(assemblyName);
			if (assembly == null)
			{
				Log.Warning(Compat.LogPrefix + "Assembly '" + assemblyName + "' is not loaded.");
				return null;
			}
			Type type = assembly.GetType(typeName, false);
			if (type == null)
			{
				Log.Error(Compat.LogPrefix + "Type '" + typeName + "' not found in " + assemblyName + ".");
			}
			return type;
		}

		private static MethodInfo FindMethod(string assemblyName, string typeName, string methodName,
			params Type[] parameters)
		{
			Type type = FindType(assemblyName, typeName);
			if (type == null)
			{
				return null;
			}
			MethodInfo method = AccessTools.Method(type, methodName, parameters);
			if (method == null)
			{
				Log.Error(Compat.LogPrefix + typeName + "." + methodName + " not found in " + assemblyName + ".");
			}
			return method;
		}

		private static FieldInfo FindField(string assemblyName, string typeName, string fieldName)
		{
			Type type = FindType(assemblyName, typeName);
			if (type == null)
			{
				return null;
			}
			FieldInfo field = AccessTools.Field(type, fieldName);
			if (field == null)
			{
				Log.Error(Compat.LogPrefix + typeName + "." + fieldName + " not found in " + assemblyName + ".");
			}
			return field;
		}
	}
}
