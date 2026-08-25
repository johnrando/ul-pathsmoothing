namespace PathSmoothingULCompat
{
	/// <summary>
	/// Live hit counts for both fixes, reported by the <c>psul</c> console command.
	///
	/// These exist because the load-time log can only prove the IL rewrite matched; it cannot prove
	/// the rewritten call site is reached at runtime, which is the one thing worth being sure of
	/// when the patched method is itself another mod's Harmony prefix. A non-zero
	/// <see cref="EndOfPathChecks"/> is that proof. All writes happen on the main thread.
	/// </summary>
	internal static class Counters
	{
		/// <summary>Times the rewritten end-of-path call site ran.</summary>
		internal static int EndOfPathChecks;

		/// <summary>Of those, how many reported "at the end of the path".</summary>
		internal static int EndOfPathHits;

		/// <summary>Times an entity was found running UL's approach-and-attack task.</summary>
		internal static int ApproachChecks;

		/// <summary>Of those, how many were blocked and had smoothing suppressed.</summary>
		internal static int ApproachSuppressions;

		internal static void Reset()
		{
			EndOfPathChecks = 0;
			EndOfPathHits = 0;
			ApproachChecks = 0;
			ApproachSuppressions = 0;
		}
	}
}
