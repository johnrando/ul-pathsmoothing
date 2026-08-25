using System.Collections.Generic;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// <c>psul</c> - reports whether each fix is installed and, more usefully, how many times each
	/// one has actually run. This is separate from PathSmoothing's own <c>ps</c> command and does
	/// not replace or shadow it.
	///
	/// The load-time log proves the IL rewrite matched. Only a non-zero end-of-path counter proves
	/// the rewritten call site is being reached, which is the part worth checking when the patched
	/// method is itself another mod's Harmony prefix.
	/// </summary>
	public class ConsoleCmdPathSmoothingUl : ConsoleCmdAbstract
	{
		public override bool IsExecuteOnClient => false;

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			if (_params.Count > 0 && _params[0].ToLower() == "reset")
			{
				Counters.Reset();
				Output("Counters reset.");
				return;
			}

			Output("PathSmoothing/UL compatibility patch");
			Output("  Undead Legacy version  : " + UndeadLegacyVersion.Status
				+ " (read from " + UndeadLegacyVersion.DetectedSource + ")");
			Output("  prefix-order fix       : " + Compat.PrefixOrderFixStatus);
			Output("  end-of-path fix        : " + Compat.EndOfPathFixStatus);
			Output("  approach-and-attack fix: " + Compat.ApproachAndAttackFixStatus);
			Output("  'ps' toggle tracking   : " + Compat.ToggleTrackingStatus);
			Output("  PathSmoothing switched : " + (Compat.SmoothingActive ? "on" : "off"));
			Output("  UpdateMoveHelper prefixes, in call order:");
			Output("    " + MoveHelperPrefixOrderFix.PrefixOrder);
			Output("  entities moving direct : " + Describe(Refs.DirectMovers)
				+ ", smoothing suppressed: " + Describe(Refs.DontSmoothEntities));
			Output("  end-of-path checks run : " + Counters.EndOfPathChecks
				+ " (" + Counters.EndOfPathHits + " reported end-of-path)");
			Output("  UL approach checks run : " + Counters.ApproachChecks
				+ " (" + Counters.ApproachSuppressions + " suppressed smoothing)");

			if (Counters.EndOfPathChecks == 0)
			{
				Output("Note: the end-of-path check only runs when an entity is trying to side-jump to an");
				Output("unreachable target, so zero is normal until that happens. Anything above zero");
				Output("proves the rewritten code is live.");
			}
		}

		private static void Output(string line)
		{
			SdtdConsole.Instance.Output(line);
		}

		private static string Describe(HashSet<EntityAlive> set)
		{
			return set == null ? "unavailable" : set.Count.ToString();
		}

		public override string[] getCommands()
		{
			return new string[1] { "psul" };
		}

		public override string getDescription()
		{
			return "Reports the status of the PathSmoothing/Undead Legacy compatibility patch.";
		}

		public override string getHelp()
		{
			return "Usage: psul [reset]\r\n\r\nPrints whether each compatibility fix was installed at load "
				+ "time and how many times each has run since.\r\n\r\nThe key line is the prefix call order "
				+ "for UpdateMoveHelper: PathSmoothing must appear BEFORE UndeadLegacy, or PathSmoothing's "
				+ "smoothed move target is overwritten before UL ever reads it and entities zig-zag along "
				+ "the raw grid path.\r\n\r\nA non-zero 'end-of-path checks run' is proof that the rewritten "
				+ "code inside Undead Legacy's movement prefix is executing.\r\n\r\n'psul reset' zeroes the "
				+ "counters so a specific in-game scenario can be measured on its own.\r\n\r\nThis is a "
				+ "separate command from PathSmoothing's own 'ps'.";
		}
	}
}
