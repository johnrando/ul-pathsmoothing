using System;
using System.Collections.Generic;
using UnityEngine;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// Adds Undead Legacy's <c>EAIULM_ApproachAndAttackTarget</c> as a second recognised melee
	/// approach task for PathSmoothing's obstruction check.
	///
	/// PathSmoothing prefixes <c>EntityAlive.FindPath</c> and, when a melee approach task is
	/// running and something solid sits between attacker and target, adds the entity to
	/// <c>Common.DontSmoothEntities</c> so the upcoming path is left unsmoothed and the entity
	/// walks around the wall instead of into it. That test is a hard <c>is
	/// EAIApproachAndAttackTarget</c> against the vanilla type. UL retargets exactly one entity -
	/// <c>animalBear</c> - to its own class, which is not in that hierarchy, so the Bear silently
	/// loses wall avoidance during a melee approach. This prefix runs the same test for UL's type
	/// and feeds the same set; both prefixes are void, so both always run and the set membership
	/// is idempotent.
	/// </summary>
	internal static class UlApproachAndAttackSmoothingFix
	{
		/// <summary>
		/// The obstruction layer mask PathSmoothing casts against, reproduced verbatim so both
		/// approach tasks agree on what counts as "blocked".
		/// </summary>
		private const int ObstructionLayerMask = 1082195968;

		/// <summary>Set after the first failure so a recurring fault cannot flood the log.</summary>
		private static bool disabled;

		internal static void Prefix(EntityAlive __instance)
		{
			if (disabled || !Compat.SmoothingActive)
			{
				return;
			}
			try
			{
				CheckForObstruction(__instance);
			}
			catch (Exception e)
			{
				disabled = true;
				Log.Error(Compat.LogPrefix + "Obstruction check failed; disabling it for this session.");
				Log.Exception(e);
			}
		}

		private static void CheckForObstruction(EntityAlive entity)
		{
			if (entity == null || entity.aiManager == null)
			{
				return;
			}
			EAITaskList tasks = Refs.AiManagerTasks(entity.aiManager);
			if (tasks == null)
			{
				return;
			}
			EntityAlive target = entity.GetAttackTarget();
			if (target == null || entity.m_characterController == null)
			{
				return;
			}
			if (!IsApproachingWithUlTask(tasks))
			{
				return;
			}
			Counters.ApproachChecks++;

			Vector3 from = entity.transform.position + Vector3.up * (entity.height / 2f);
			Vector3 toTarget = target.transform.position + Vector3.up * (target.height / 2f) - from;
			float radius = entity.m_characterController.GetRadius();
			if (Physics.SphereCast(from, radius, toTarget.normalized, out RaycastHit _, toTarget.magnitude,
				ObstructionLayerMask))
			{
				Refs.DontSmoothEntities.Add(entity);
				Counters.ApproachSuppressions++;
			}
		}

		private static bool IsApproachingWithUlTask(EAITaskList tasks)
		{
			List<EAITaskEntry> executing = tasks.GetExecutingTasks();
			if (executing == null)
			{
				return false;
			}
			for (int i = 0; i < executing.Count; i++)
			{
				EAITaskEntry entry = executing[i];
				if (entry != null && Refs.UndeadLegacyApproachAndAttackTarget.IsInstanceOfType(entry.action))
				{
					return true;
				}
			}
			return false;
		}
	}
}
