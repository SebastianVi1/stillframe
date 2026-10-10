using Stillframe.Core;

namespace Stillframe.Tests;

[TestClass]
public sealed class CoreTests
{
    private string root = null!;
    [TestInitialize] public void Prepare() => root = Directory.CreateTempSubdirectory("stillframe-tests-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public void CatalogFiltersFormatsCaseInsensitively()
    {
        foreach (var name in new[] { "a.JPG", "b.png", "c.jpeg", "d.BMP", "notes.txt", "clip.gif", "image.webp" }) File.WriteAllText(Path.Combine(root, name), "test");
        Assert.HasCount(4, ImageCatalog.Scan(root, false).Files);
    }
    [TestMethod]
    public void SubfoldersAreOptional()
    {
        var child = Directory.CreateDirectory(Path.Combine(root, "album"));
        File.WriteAllText(Path.Combine(root, "a.jpg"), "test"); File.WriteAllText(Path.Combine(child.FullName, "b.jpg"), "test");
        Assert.HasCount(1, ImageCatalog.Scan(root, false).Files);
        Assert.HasCount(2, ImageCatalog.Scan(root, true).Files);
    }
    [TestMethod]
    public void MissingFolderOffersRecovery()
    {
        var result = ImageCatalog.Scan(Path.Combine(root, "missing"), true);
        Assert.IsEmpty(result.Files); StringAssert.Contains(result.Warning!, "unavailable");
    }
    [TestMethod]
    public void CatalogHonorsCancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => ImageCatalog.Scan(root, true, cancel.Token));
    }
    [TestMethod]
    public void EmptyFolderIsValid()
    {
        var result = ImageCatalog.Scan(root, false);
        Assert.IsEmpty(result.Files); Assert.IsNull(result.Warning);
    }
    [TestMethod]
    public void FilenameOrderWrapsAndNavigatesBackward()
    {
        var playlist = new Playlist(); playlist.Replace(Photos("c.png", "a.png", "b.png"), ImageOrder.Filename);
        Assert.AreEqual("a.png", playlist.Move(1)); Assert.AreEqual("b.png", playlist.Move(1));
        Assert.AreEqual("a.png", playlist.Move(-1)); Assert.AreEqual("c.png", playlist.Move(-1));
        Assert.AreEqual("a.png", playlist.Move(1));
    }
    [TestMethod]
    public void ModificationOrderUsesTimestamp()
    {
        var playlist = new Playlist(); var now = DateTime.UtcNow;
        playlist.Replace([new("new.jpg", now), new("old.jpg", now.AddDays(-1))], ImageOrder.ModifiedDate);
        Assert.AreEqual("old.jpg", playlist.Move(1)); Assert.AreEqual("new.jpg", playlist.Move(1));
    }
    [TestMethod]
    public void ShuffleDoesNotRepeatWithinCycle()
    {
        var playlist = new Playlist(42); var photos = Enumerable.Range(0, 30).Select(i => new PhotoFile($"{i}.jpg", DateTime.MinValue)).ToList();
        playlist.Replace(photos, ImageOrder.Shuffle);
        var cycle = Enumerable.Range(0, photos.Count).Select(_ => playlist.Move(1)).ToList();
        Assert.AreEqual(photos.Count, cycle.Distinct().Count()); Assert.AreEqual(cycle[0], playlist.Move(1));
        Assert.IsFalse(cycle.SequenceEqual(photos.Select(p => p.Path)));
    }
    [TestMethod]
    public void RefreshPreservesCurrentPhoto()
    {
        var playlist = new Playlist(); playlist.Replace(Photos("a.jpg", "b.jpg"), ImageOrder.Filename); playlist.Move(1); playlist.Move(1);
        playlist.Replace(Photos("a.jpg", "b.jpg", "c.jpg"), ImageOrder.Filename, playlist.Current);
        Assert.AreEqual("b.jpg", playlist.Current); Assert.AreEqual("c.jpg", playlist.Next);
    }
    [TestMethod]
    public void DeletedCurrentPhotoResetsSafely()
    {
        var playlist = new Playlist(); playlist.Replace(Photos("a.jpg"), ImageOrder.Filename); playlist.Move(1);
        playlist.Replace(Photos("b.jpg"), ImageOrder.Filename, playlist.Current);
        Assert.IsNull(playlist.Current); Assert.AreEqual("b.jpg", playlist.Move(1));
    }
    [TestMethod]
    public void SinglePhotoAndEmptyPlaylistsAreSafe()
    {
        var playlist = new Playlist(); Assert.IsNull(playlist.Move(-1));
        playlist.Replace(Photos("a.jpg"), ImageOrder.Shuffle); Assert.AreEqual("a.jpg", playlist.Move(-1)); Assert.AreEqual("a.jpg", playlist.Move(1));
    }
    [TestMethod]
    public void PreferencesRoundTripNegativeCoordinatesAndEnums()
    {
        var store = new PreferencesStore(root);
        store.Save(new() { Folder = @"C:\Photos", X = -500, Y = 120, Order = ImageOrder.Shuffle, Fit = ImageFit.Fit, Playing = false });
        var restored = store.Load(); Assert.AreEqual(-500, restored.X); Assert.AreEqual(ImageOrder.Shuffle, restored.Order);
        Assert.AreEqual(ImageFit.Fit, restored.Fit); Assert.IsFalse(restored.Playing);
        Assert.IsFalse(File.Exists(store.FilePath + ".tmp"));
    }
    [TestMethod]
    public void CorruptPreferencesRecoverWithoutOverwritingEvidence()
    {
        var store = new PreferencesStore(root); File.WriteAllText(store.FilePath, "{ broken");
        Assert.AreEqual(10, store.Load().IntervalSeconds); Assert.IsNotNull(store.LoadWarning);
        Assert.AreEqual("{ broken", File.ReadAllText(store.FilePath));
    }
    [TestMethod]
    public void InvalidPreferencesAreNormalized()
    {
        var preferences = new Preferences { IntervalSeconds = double.NaN, Width = -1, Height = double.PositiveInfinity, Order = (ImageOrder)999, Fit = (ImageFit)999 };
        preferences.Normalize(); Assert.AreEqual(10, preferences.IntervalSeconds); Assert.AreEqual(180, preferences.Width);
        Assert.AreEqual(240, preferences.Height); Assert.AreEqual(ImageOrder.Filename, preferences.Order); Assert.AreEqual(ImageFit.Fill, preferences.Fit);
    }
    private static List<PhotoFile> Photos(params string[] names) => names.Select(name => new PhotoFile(name, DateTime.MinValue)).ToList();
}
