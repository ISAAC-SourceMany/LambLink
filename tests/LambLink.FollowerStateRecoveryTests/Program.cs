using LambLink.Companion.Configuration;
using LambLink.Companion.Storage;
using LambLink.Protocol;

var tempDir = Path.Combine(Path.GetTempPath(), "cotl-follower-state-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
var path = Path.Combine(tempDir, "viewer-followers.json");

try
{
    var repository = new ViewerFollowerRepository(path);
    var initial = Record(12, "유르밍", generation: 1, createdAt: DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
    Assert(repository.ReplaceStateIfNewer("streamer", "viewer", "slot_0", 10, new[] { initial }), "new cloud state must be imported");
    Assert(File.Exists(path), "cloud recovery must persist viewer-followers.json");

    var reloaded = new ViewerFollowerRepository(path);
    var restored = reloaded.GetForSave("streamer", "slot_0");
    Assert(restored.Count == 1 && restored[0].FollowerId == 12 && restored[0].FollowerName == "유르밍", "imported state must survive reload");

    var older = Record(35, "오래된값", generation: 1, createdAt: DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
    Assert(!reloaded.ReplaceStateIfNewer("streamer", "viewer", "slot_0", 9, new[] { older }), "older cloud revision must not overwrite local state");
    Assert(reloaded.GetForSave("streamer", "slot_0").Single().FollowerId == 12, "older revision must leave the current mapping untouched");

    var earlierDuplicate = Record(21, "유르밍 2세", generation: 2, createdAt: DateTimeOffset.Parse("2026-08-02T00:00:00Z"));
    var latestDuplicate = Record(25, "유르밍 2세", generation: 2, createdAt: DateTimeOffset.Parse("2026-08-03T00:00:00Z"));
    Assert(reloaded.ReplaceStateIfNewer("streamer", "viewer", "slot_0", 11, new[] { earlierDuplicate, latestDuplicate }), "newer cloud revision must replace one viewer state atomically");
    var deduplicated = reloaded.GetForSave("streamer", "slot_0").Single();
    Assert(deduplicated.FollowerId == 25 && deduplicated.Generation == 2 && deduplicated.Revision == 11, "duplicate generations must keep the newest cloud record");

    Assert(!reloaded.ReplaceStateIfNewer("streamer", "viewer", "slot_0", 12, Array.Empty<ViewerFollowerRecord>()), "empty cloud history must never erase local mappings");
    Assert(reloaded.GetForSave("streamer", "slot_0").Single().FollowerId == 25, "empty recovery input must be non-destructive");

    var marker = new ChzzkFollowerMarker { FollowerId = 25, ViewerId = "viewer", Nickname = "유르밍 2세" };
    Assert(ChzzkFollowerMarkerDiff.GetChangedFollowerIds("slot_0", new[] { marker }, "slot_0", new[] { marker }).Count == 0,
        "identical marker snapshots must not trigger a UI refresh");
    Assert(ChzzkFollowerMarkerDiff.GetChangedFollowerIds("slot_0", new[] { marker }, "slot_0", new[]
    {
        new ChzzkFollowerMarker { FollowerId = 25, ViewerId = "viewer", Nickname = "변경된 이름" }
    }).Single() == 25, "nickname changes must refresh only the affected follower");
    Assert(ChzzkFollowerMarkerDiff.GetChangedFollowerIds("slot_0", new[] { marker }, "slot_0", Array.Empty<ChzzkFollowerMarker>()).Single() == 25,
        "marker removal must refresh the removed follower");
    Assert(ChzzkFollowerMarkerDiff.GetChangedFollowerIds("slot_0", new[] { marker }, "slot_1", new[]
    {
        new ChzzkFollowerMarker { FollowerId = 31, ViewerId = "viewer-2", Nickname = "다른 저장" }
    }).OrderBy(x => x).SequenceEqual(new[] { 25, 31 }), "save changes must invalidate markers from both snapshots");

    var stagingProgramDir = Path.Combine(tempDir, "staging-program");
    Directory.CreateDirectory(stagingProgramDir);
    File.WriteAllText(Path.Combine(stagingProgramDir, CompanionLaunchProfileLoader.FileName), """
    {
      "schemaVersion": 1,
      "release": "1.0.0",
      "environment": "staging",
      "apiBaseUrl": "https://staging-api.example.test",
      "frontendUrl": "https://staging.example.test",
      "dataDirectory": "C:\\Users\\viewer\\AppData\\Local\\LambLink-Staging"
    }
    """);
    var stagingProfile = CompanionLaunchProfileLoader.Load(stagingProgramDir, "1.0.0");
    Assert(stagingProfile?.IsStaging == true, "installed staging launch profile must select staging mode");
    Assert(stagingProfile?.ApiBaseUrl == "https://staging-api.example.test", "staging launch profile must retain its isolated API");
    Assert(stagingProfile?.FrontendUrl == "https://staging.example.test", "staging launch profile must retain its isolated viewer web");
    AssertThrows<InvalidDataException>(
        () => CompanionLaunchProfileLoader.Load(stagingProgramDir, "1.0.0-rc36"),
        "a stale launch profile must not silently select an environment for another release");
    Assert(CompanionLaunchProfileLoader.Load(Path.Combine(tempDir, "missing-program"), "1.0.0") is null,
        "a missing launch profile must preserve the production release default");

    var legacyDataDir = Path.Combine(tempDir, "legacy-data");
    var renamedDataDir = Path.Combine(tempDir, "renamed-data");
    Directory.CreateDirectory(Path.Combine(legacyDataDir, "nested"));
    File.WriteAllText(Path.Combine(legacyDataDir, "viewer-followers.json"), "legacy-followers");
    File.WriteAllText(Path.Combine(legacyDataDir, "nested", "viewer-appearances.json"), "legacy-appearances");
    Assert(LegacyDataMigration.CopyMissingFiles(legacyDataDir, renamedDataDir) == 2,
        "the first LambLink launch must copy all missing legacy data files");
    Assert(File.ReadAllText(Path.Combine(renamedDataDir, "viewer-followers.json")) == "legacy-followers",
        "legacy follower history must survive the brand migration");
    File.WriteAllText(Path.Combine(renamedDataDir, "viewer-followers.json"), "newer-lamblink-data");
    Assert(LegacyDataMigration.CopyMissingFiles(legacyDataDir, renamedDataDir) == 0,
        "repeated brand migration must be idempotent");
    Assert(File.ReadAllText(Path.Combine(renamedDataDir, "viewer-followers.json")) == "newer-lamblink-data",
        "brand migration must never overwrite LambLink data");

    Console.WriteLine("Follower-state, marker-diff, launch-profile, and brand-migration tests passed: 22 assertions.");
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

static ViewerFollowerRecord Record(int followerId, string followerName, int generation, DateTimeOffset createdAt) => new()
{
    StreamerChannelId = "untrusted-remote-streamer",
    ViewerChannelId = "untrusted-remote-viewer",
    LastKnownNickname = "유르밍",
    FollowerName = followerName,
    SaveId = "untrusted-remote-save",
    FollowerId = followerId,
    Generation = generation,
    CreatedAt = createdAt,
    Events = new List<ViewerFollowerHistoryEvent> { new() { Type = "Created", At = createdAt } }
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
