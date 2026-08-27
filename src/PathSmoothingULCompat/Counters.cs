namespace PathSmoothingULCompat
{
	/// <summary>
	/// Live hit counts for the end-of-path fix, reported by the <c>psul</c> console command.
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

		internal static void Reset()
		{
			EndOfPathChecks = 0;
			EndOfPathHits = 0;
		}
	}
}
