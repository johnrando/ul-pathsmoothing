namespace PathSmoothingULCompat
{
	/// <summary>
	/// Hit counts for the end-of-path fix, reported by <c>psul</c>. The load-time log only proves the
	/// IL rewrite matched; a non-zero <see cref="EndOfPathChecks"/> proves the rewritten site inside
	/// UL's prefix is actually reached. Main thread only.
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
