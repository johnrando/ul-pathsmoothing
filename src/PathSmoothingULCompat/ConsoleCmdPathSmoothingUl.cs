using System.Collections.Generic;

namespace PathSmoothingULCompat
{
	/// <summary>
	/// <c>psul</c> prints a short verdict block; <c>psul info</c> adds version, patch state, prefix
	/// call order and counters; <c>psul reset</c> zeroes the counters. There is nothing to set: both
	/// fixes are load-time Harmony work and PathSmoothing's own <c>ps</c> switches the behaviour off.
	/// Separate from <c>ps</c>, which is not shadowed.
	/// </summary>
	public class ConsoleCmdPathSmoothingUl : ConsoleCmdAbstract
	{
		public override bool IsExecuteOnClient => false;

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			string command = _params.Count > 0 ? _params[0].ToLowerInvariant() : string.Empty;

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

		/// <summary>The verdict and the three lines it is drawn from.</summary>
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

		/// <summary>Labels pad to the longest one ("end-of-path checks") so both blocks share a column.</summary>
		private static void Line(string _label, string _value)
		{
			Output("  " + _label.PadRight(18) + ": " + _value);
		}

		/// <summary>IDLE under <c>ps 0</c> is by design: the patches are inert and there is no order to check.</summary>
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

		/// <summary>Read live from Harmony; PathSmoothing must come first or its target is overwritten.</summary>
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
