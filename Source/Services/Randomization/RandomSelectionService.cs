using System;
using System.Collections.Generic;
using System.Linq;

namespace AnikiHelper.Services.Randomization
{
    /// <summary>
    /// Shared random-pool picker used by Aniki random modes.
    /// It avoids immediately repeating the previous candidate whenever the pool allows it.
    /// </summary>
    internal static class RandomSelectionService
    {
        public static T Pick<T>(
            IEnumerable<T> source,
            Func<T, string> keySelector,
            string previousKey,
            Random random) where T : class
        {
            if (source == null || keySelector == null)
            {
                return default(T);
            }

            var items = source.Where(x => x != null).ToList();
            if (items.Count == 0)
            {
                return default(T);
            }

            var candidates = items;
            if (items.Count > 1 && !string.IsNullOrWhiteSpace(previousKey))
            {
                var withoutPrevious = items
                    .Where(x => !string.Equals(
                        keySelector(x) ?? string.Empty,
                        previousKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (withoutPrevious.Count > 0)
                {
                    candidates = withoutPrevious;
                }
            }

            var rng = random ?? new Random();
            return candidates[rng.Next(0, candidates.Count)];
        }
    }
}
