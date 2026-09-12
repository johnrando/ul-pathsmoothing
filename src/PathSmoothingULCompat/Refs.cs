using System;
using System.Collections.Generic;
using System.Reflection;
using GamePath;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Late-bound handles onto the two mods this patch sits between. Neither is referenced at
	/// compile time, so the patch loads and logs cleanly when either is absent or has moved a member.
	/// </summary>
	internal static class Refs
	{
		internal const string PathSmoothingAssemblyName = "PathSmoothing";
		internal const string UndeadLegacyAssemblyName = "UndeadLegacy";

		/// <summary><c>PathSmoothing.Utils.GetPathLengthDistanceSq(PathEntity)</c>.</summary>
		internal static MethodInfo GetPathLengthDistanceSq;

		/// <summary><c>PathSmoothing.Common.DontSmoothEntities</c> (static readonly, so the set itself is cached). Diagnostics only.</summary>
		internal static HashSet<EntityAlive> DontSmoothEntities;

		/// <summary><c>PathSmoothing.Common.DirectMovers</c>. Diagnostics only.</summary>
		internal static HashSet<EntityAlive> DirectMovers;

		/// <summary>
		/// UL's <c>H_ZombieDiggingPatch+EntityMoveHelper_UpdateMoveHelper.Prefix</c>: a reimplementation
		/// of <c>EntityMoveHelper.UpdateMoveHelper</c> that returns false on every exit path.
		/// </summary>
		internal static MethodInfo UndeadLegacyMoveHelperPrefix;

		private static readonly Dictionary<string, Assembly> assemblies =
			new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

		internal static bool PathSmoothingPresent => FindAssembly(PathSmoothingAssemblyName) != null;

		internal static bool UndeadLegacyPresent => FindAssembly(UndeadLegacyAssemblyName) != null;

		internal static Type UndeadLegacyType(string typeName)
		{
			return FindType(UndeadLegacyAssemblyName, typeName);
		}

		/// <summary>Looks up a parameterless method on <c>PathSmoothing.Common</c>.</summary>
		internal static MethodInfo PathSmoothingCommonMethod(string name)
		{
			return FindMethod(PathSmoothingAssemblyName, "PathSmoothing.Common", name, Type.EmptyTypes);
		}

		/// <summary>Resolves UL's UpdateMoveHelper prefix once; both fixes need it.</summary>
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

		/// <summary>Best effort: the sets are only used by <c>psul info</c>.</summary>
		internal static void ResolveSharedSets()
		{
			DontSmoothEntities = ResolveSet("DontSmoothEntities");
			DirectMovers = ResolveSet("DirectMovers");
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
			if (assemblies.TryGetValue(simpleName, out Assembly cached))
			{
				return cached;
			}
			Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < loaded.Length; i++)
			{
				if (string.Equals(loaded[i].GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
				{
					assemblies[simpleName] = loaded[i];
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
