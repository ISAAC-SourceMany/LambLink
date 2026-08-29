using System;
using System.Collections.Generic;
using System.Linq;

namespace LambLink.Protocol;

public static class ChzzkFollowerMarkerDiff
{
    public static IReadOnlyCollection<int> GetChangedFollowerIds(
        string currentSaveId,
        IEnumerable<ChzzkFollowerMarker>? current,
        string nextSaveId,
        IEnumerable<ChzzkFollowerMarker>? next)
    {
        var before = ToDictionary(current);
        var after = ToDictionary(next);
        var ids = new HashSet<int>();

        if (!string.Equals(currentSaveId ?? string.Empty, nextSaveId ?? string.Empty, StringComparison.Ordinal))
        {
            ids.UnionWith(before.Keys);
            ids.UnionWith(after.Keys);
            return ids;
        }

        foreach (var id in before.Keys.Concat(after.Keys))
        {
            before.TryGetValue(id, out var left);
            after.TryGetValue(id, out var right);
            if (!AreEquivalent(left, right)) ids.Add(id);
        }

        return ids;
    }

    public static bool AreEquivalent(ChzzkFollowerMarker? left, ChzzkFollowerMarker? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;
        return left.FollowerId == right.FollowerId &&
               string.Equals(left.ViewerId ?? string.Empty, right.ViewerId ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(left.Nickname ?? string.Empty, right.Nickname ?? string.Empty, StringComparison.Ordinal);
    }

    private static Dictionary<int, ChzzkFollowerMarker> ToDictionary(IEnumerable<ChzzkFollowerMarker>? markers)
    {
        var result = new Dictionary<int, ChzzkFollowerMarker>();
        if (markers == null) return result;
        foreach (var marker in markers)
        {
            if (marker == null || marker.FollowerId <= 0) continue;
            result[marker.FollowerId] = marker;
        }
        return result;
    }
}
