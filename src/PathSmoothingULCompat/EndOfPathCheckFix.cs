using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GamePath;
using HarmonyLib;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Re-applies PathSmoothing's <c>FarBlockAttackFix</c> IL edit inside Undead Legacy's
	/// <c>H_ZombieDiggingPatch</c> prefix.
	///
	/// PathSmoothing transpiles vanilla <c>EntityMoveHelper.UpdateMoveHelper</c> to replace its
	/// <c>path.NodeCountRemaining() &lt;= 1</c> end-of-path test with a real path length, because
	/// smoothing changes node spacing and makes the node count a bad proxy for "nearly there".
	/// UL prefixes the same vanilla method and returns false on every exit path, so that vanilla
	/// body - and PathSmoothing's edit of it - never runs, and UL's own untouched copy of the check
	/// applies instead. Transpiling UL's prefix puts the fix back on the code path that executes.
	/// </summary>
	internal static class EndOfPathCheckFix
	{
		/// <summary>The squared path length PathSmoothing treats as "at the end of the path".</summary>
		private const float EndOfPathRangeThresholdSq = 1f;

		/// <summary>Sites rewritten by the last transpiler run; read back by <see cref="Compat"/>.</summary>
		internal static int PatchedSites;

		private static Func<PathEntity, float> pathLengthDistanceSq;

		internal static bool Bind(MethodInfo getPathLengthDistanceSq)
		{
			pathLengthDistanceSq = (Func<PathEntity, float>)Delegate.CreateDelegate(
				typeof(Func<PathEntity, float>), getPathLengthDistanceSq, false);
			if (pathLengthDistanceSq == null)
			{
				Log.Error(Compat.LogPrefix + "PathSmoothing.Utils.GetPathLengthDistanceSq does not match "
					+ "float (PathEntity).");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Stands in for <c>path.NodeCountRemaining()</c> at the rewritten call sites. Going through
		/// this shim rather than calling PathSmoothing directly keeps the <c>ps</c> console command
		/// meaningful: with smoothing switched off, node spacing is normal again and the original
		/// node-count test is the right one, so it is handed back exactly - <c>&lt;= 1</c> nodes maps
		/// to a value at the threshold, more than that maps to a value above it.
		/// </summary>
		internal static float EndOfPathDistanceSq(PathEntity path)
		{
			float distanceSq = Compat.SmoothingActive
				? pathLengthDistanceSq(path)
				: (path.NodeCountRemaining() <= 1 ? 0f : EndOfPathRangeThresholdSq * 2f);

			Counters.EndOfPathChecks++;
			if (distanceSq <= EndOfPathRangeThresholdSq)
			{
				Counters.EndOfPathHits++;
			}
			return distanceSq;
		}

		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
			MethodInfo replacement = AccessTools.Method(typeof(EndOfPathCheckFix), nameof(EndOfPathDistanceSq));
			PatchedSites = 0;

			// Editing in place rather than replacing instructions keeps any labels and exception
			// block boundaries attached to them.
			for (int i = 0; i < codes.Count - 1; i++)
			{
				MethodInfo called = codes[i].operand as MethodInfo;
				if (called == null || called.Name != "NodeCountRemaining"
					|| called.DeclaringType != typeof(PathEntity))
				{
					continue;
				}

				if (!IsLoadInt32(codes[i + 1], 1))
				{
					Log.Warning(Compat.LogPrefix + "Skipping a NodeCountRemaining() call that is not "
						+ "compared against 1 (followed by " + codes[i + 1].opcode + ").");
					continue;
				}

				// int32 NodeCountRemaining()  ->  float32 EndOfPathDistanceSq(PathEntity), which
				// consumes the same PathEntity already on the stack. The comparison that follows
				// (cgt/ceq) is valid on float32 operands as-is.
				codes[i].opcode = OpCodes.Call;
				codes[i].operand = replacement;
				codes[i + 1].opcode = OpCodes.Ldc_R4;
				codes[i + 1].operand = EndOfPathRangeThresholdSq;
				PatchedSites++;
			}

			return codes;
		}

		private static bool IsLoadInt32(CodeInstruction instruction, int value)
		{
			OpCode opcode = instruction.opcode;
			if (opcode == OpCodes.Ldc_I4_1)
			{
				return value == 1;
			}
			if (opcode == OpCodes.Ldc_I4_S)
			{
				return instruction.operand is sbyte b && b == value;
			}
			if (opcode == OpCodes.Ldc_I4)
			{
				return instruction.operand is int i && i == value;
			}
			return false;
		}
	}
}
