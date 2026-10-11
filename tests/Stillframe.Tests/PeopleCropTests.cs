using Stillframe.Core;

namespace Stillframe.Tests;

[TestClass]
public sealed class PeopleCropTests
{
    [TestMethod]
    public void WidePhotoCentersPersonOnTheRight()
    {
        var crop = PeopleCrop.Choose(2, 1, [new(.8, .3, .1, .15)]);
        Assert.IsFalse(crop.ShowWholePhoto);
        Assert.AreEqual(.5, crop.Bounds.X, .00001);
        Assert.IsTrue(crop.Bounds.X <= .8 && crop.Bounds.X + crop.Bounds.Width >= .9);
    }

    [TestMethod]
    public void PortraitKeepsHeadroomForPersonNearTop()
    {
        var crop = PeopleCrop.Choose(.6, 1.5, [new(.35, .06, .2, .1)]);
        Assert.IsFalse(crop.ShowWholePhoto);
        Assert.AreEqual(0, crop.Bounds.Y, .00001);
        Assert.IsGreaterThanOrEqualTo(.26, crop.Bounds.Y + crop.Bounds.Height);
    }

    [TestMethod]
    public void GroupAcrossWidePhotoUsesWholePhoto()
    {
        var crop = PeopleCrop.Choose(2.5, 1, [new(.05, .3, .1, .15), new(.85, .3, .1, .15)]);
        Assert.IsTrue(crop.ShowWholePhoto);
        Assert.AreEqual(new PhotoBounds(0, 0, 1, 1), crop.Bounds);
    }

    [TestMethod]
    public void TallGroupUsesWholePhotoInsteadOfCuttingHeads()
    {
        var crop = PeopleCrop.Choose(.6, 2, [new(.25, .15, .12, .12), new(.55, .55, .12, .12)]);
        Assert.IsTrue(crop.ShowWholePhoto);
    }

    [TestMethod]
    public void AdjacentFacesStayInsideOneCrop()
    {
        var faces = new PhotoBounds[] { new(.45, .25, .07, .1), new(.6, .28, .07, .1) };
        var crop = PeopleCrop.Choose(2, 1, faces);
        Assert.IsFalse(crop.ShowWholePhoto);
        foreach (var face in faces)
        {
            Assert.IsTrue(crop.Bounds.X <= face.X && crop.Bounds.X + crop.Bounds.Width >= face.X + face.Width);
            Assert.IsTrue(crop.Bounds.Y <= face.Y && crop.Bounds.Y + crop.Bounds.Height >= face.Y + face.Height);
        }
    }

    [TestMethod]
    public void NoFacesPreservesOrdinaryCenterCropping()
    {
        var crop = PeopleCrop.Choose(2, 1, []);
        Assert.IsFalse(crop.ShowWholePhoto);
        Assert.AreEqual(new PhotoBounds(0, 0, 1, 1), crop.Bounds);
    }

    [TestMethod]
    public void PeoplePreferenceDefaultsOnAndRoundTrips()
    {
        var directory = Directory.CreateTempSubdirectory("stillframe-framing-").FullName;
        try
        {
            var store = new PreferencesStore(directory);
            Assert.IsTrue(store.Load().CenterPeople);
            store.Save(new Preferences { CenterPeople = false });
            Assert.IsFalse(store.Load().CenterPeople);
        }
        finally { Directory.Delete(directory, true); }
    }
}
