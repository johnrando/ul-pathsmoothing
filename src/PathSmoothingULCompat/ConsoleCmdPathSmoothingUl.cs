using System.Collections.Generic;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// <c>psul</c> - prints a short block saying whether the compatibility patch is doing its job.
	/// <c>psul info</c> prints the diagnostics and counters, <c>psul reset</c> zeroes them.
	///
	/// There is nothing to set here: both fixes are Harmony work done once at load, and
	/// PathSmoothing's own <c>ps</c> already switches the behaviour off. So unlike the sibling mods
	/// the bare command toggles nothing - it answers one question, "is this working", in as few lines
	/// as that takes.
	///
	/// Everything behind that answer lives in <c>psul info</c>, and it matters more here than in
	/// those siblings, because neither fix is visible from the outside. The prefix call order is the
	/// direct evidence for the ordering fix, read live from Harmony rather than remembered from load
	/// time. The end-of-path counter is the only evidence for the other one: the startup log can
	/// prove the IL rewrite matched, but only a non-zero count proves the rewritten call site - which
	/// sits inside another mod's Harmony prefix - is actually being reached.
	///
	/// This is separate from PathSmoothing's own <c>ps</c> command and does not shadow it.
	/// </summary>
	public class ConsoleCmdPathSmoothingUl : ConsoleCmdAbstract
	{
		public override bool IsExecuteOnClient => false;

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			string command = _params.Count > 0 ? _params[0].ToLower() : string.Empty;

			switch (command)
			{
			case "":
				OutputStatus();
				return;

			case "info":
				OutputInfo();
				return;

			case "reset":
				Counters.Reset();
				Output("Counters reset.");
				return;

			default:
				Output("Unknown option '" + _params[0] + "'. Try: psul [info|reset]");
				return;
			}
		}

		/// <summary>The short block: the verdict, and the three lines it is drawn from.</summary>
		private static void OutputBlock()
		{
			Output(Verdict());
			Line("prefix order", OrderLine());
			Line("end-of-path fix", EndOfPathLine());
			Line("smoothing (ps)", SmoothingLine());
		}

		private static void OutputStatus()
		{
			OutputBlock();
			Line("psul info", "version, counters and diagnostics");
		}

		/// <summary>The short block, with the read-only lines appended in the same column.</summary>
		private static void OutputInfo()
		{
			OutputBlock();
			Line("Undead Legacy", UndeadLegacyVersion.Status
				+ " (read from " + UndeadLegacyVersion.DetectedSource + ")");
			Line("prefix-order fix", Compat.PrefixOrderFixStatus);
			Line("end-of-path fix", Compat.EndOfPathFixStatus);
			Line("'ps' tracking", Compat.ToggleTrackingStatus);
			Line("prefix call order", MoveHelperPrefixOrderFix.DescribeOrder());
			Line("entities", Describe(Refs.DirectMovers) + " moving direct, "
				+ Describe(Refs.DontSmoothEntities) + " smoothing suppressed");
			Line("end-of-path checks", Counters.EndOfPathChecks
				+ " (" + Counters.EndOfPathHits + " reported end-of-path)");

			if (Counters.EndOfPathChecks == 0)
			{
				Output("Note: the end-of-path check only runs when an entity is trying to side-jump to an");
				Output("unreachable target, so zero is normal until that happens. Anything above zero");
				Output("proves the rewritten code is live.");
			}
		}

		/// <summary>
		/// One line of the block. Every label is padded to the width of the longest one -
		/// "end-of-path checks" - so the short block and the read-only lines share a column and
		/// <c>psul info</c> reads as one block rather than two.
		/// </summary>
		private static void Line(string _label, string _value)
		{
			Output("  " + _label.PadRight(18) + ": " + _value);
		}

		/// <summary>
		/// The headline. Switched off is its own answer rather than a fault: with <c>ps 0</c> these
		/// patches are inert by design, and PathSmoothing's prefix is unregistered, so there is no
		/// call order left to be correct.
		/// </summary>
		private static string Verdict()
		{
			if (!Compat.SmoothingActive)
			{
				return "PathSmoothing/UL compatibility patch is IDLE - PathSmoothing is switched off";
			}
			bool working = Compat.PrefixOrderFixApplied && Compat.EndOfPathFixApplied
				&& MoveHelperPrefixOrderFix.OrderIsCorrect();
			return "PathSmoothing/UL compatibility patch is " + (working ? "WORKING" : "NOT WORKING");
		}

		/// <summary>
		/// The call order, read live from Harmony. PathSmoothing has to come first, or its smoothed
		/// move target is overwritten before Undead Legacy ever reads it.
		/// </summary>
		private static string OrderLine()
		{
			string order = MoveHelperPrefixOrderFix.ShortOrder();
			if (MoveHelperPrefixOrderFix.OrderIsCorrect())
			{
				return order + " (correct)";
			}
			if (!Compat.SmoothingActive)
			{
				return order;
			}
			return order + " (WRONG - PathSmoothing must come first)";
		}

		private static string EndOfPathLine()
		{
			if (!Compat.EndOfPathFixApplied)
			{
				return "NOT APPLIED - see 'psul info'";
			}
			return "applied, " + Compat.EndOfPathSites
				+ (Compat.EndOfPathSites == 1 ? " check rewritten" : " checks rewritten");
		}

		private static string SmoothingLine()
		{
			return Compat.SmoothingActive ? "on" : "off - 'ps 1' switches PathSmoothing back on";
		}

		private static string Describe(HashSet<EntityAlive> _set)
		{
			return _set == null ? "unavailable" : _set.Count.ToString();
		}

		private static void Output(string _line)
		{
			SdtdConsole.Instance.Output(_line);
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
			return "Usage: psul [info|reset]"
				+ "\r\n\r\nReports whether this mod's two fixes to the PathSmoothing/Undead Legacy "
				+ "clash are in place. There is nothing to set.\r\n\r\n'psul' prints the verdict: the "
				+ "call order of the two movement prefixes, whether the end-of-path rewrite landed, "
				+ "and whether PathSmoothing is switched on. PathSmoothing must come BEFORE "
				+ "UndeadLegacy, or its smoothing is overwritten before UL reads it. With 'ps 0' the "
				+ "block reports idle, which is by design.\r\n\r\n'psul info' adds the Undead Legacy "
				+ "version, the full patch state and the counters. 'end-of-path checks' above zero is "
				+ "the proof the rewritten code is being reached; it only moves when an entity tries "
				+ "to jump a gap to a target it cannot path to.\r\n\r\n'psul reset' zeroes the "
				+ "counters.\r\n\r\n'ps' is PathSmoothing's own command and is untouched by this mod.";
		}
	}
}
