using Stillframe.Core;

namespace Stillframe.Tests;

[TestClass]
public sealed class PeopleCropTests
{
    [TestMethod]
    public void WidePhotoCentersPersonOnTheRight()
    {
        var crop = PeopleCrop.Choose(2, 1, [new(.8, .3, .1, .15)]);
        Assert.AreEqual(.5, crop.X, .00001);
        Assert.IsTrue(crop.X <= .8 && crop.X + crop.Width >= .9);
    }

    [TestMethod]
    public void PortraitKeepsHeadroomForPersonNearTop()
    {
        var crop = PeopleCrop.Choose(.6, 1.5, [new(.35, .06, .2, .1)]);
        Assert.AreEqual(0, crop.Y, .00001);
        Assert.IsGreaterThanOrEqualTo(.26, crop.Y + crop.Height);
    }

    [TestMethod]
    public void GroupAcrossWidePhotoKeepsFillAndCentersGroup()
    {
        var crop = PeopleCrop.Choose(2.5, 1, [new(.05, .3, .1, .15), new(.85, .3, .1, .15)]);
        Assert.AreEqual(.4, crop.Width, .00001);
        Assert.AreEqual(.3, crop.X, .00001);
        Assert.AreEqual(1, crop.Height, .00001);
    }

    [TestMethod]
    public void TallGroupKeepsFillWithoutChangingScale()
    {
        var crop = PeopleCrop.Choose(.6, 2, [new(.25, .15, .12, .12), new(.55, .55, .12, .12)]);
        Assert.AreEqual(.3, crop.Height, .00001);
        Assert.AreEqual(1, crop.Width, .00001);
        Assert.IsTrue(crop.Y >= 0 && crop.Y + crop.Height <= 1);
    }

    [TestMethod]
    public void AdjacentFacesStayInsideOneCrop()
    {
        var faces = new PhotoBounds[] { new(.45, .25, .07, .1), new(.6, .28, .07, .1) };
        var crop = PeopleCrop.Choose(2, 1, faces);
        foreach (var face in faces)
        {
            Assert.IsTrue(crop.X <= face.X && crop.X + crop.Width >= face.X + face.Width);
            Assert.IsTrue(crop.Y <= face.Y && crop.Y + crop.Height >= face.Y + face.Height);
        }
    }

    [TestMethod]
    public void NoFacesPreservesOrdinaryCenterCropping()
    {
        var crop = PeopleCrop.Choose(2, 1, []);
        Assert.AreEqual(new PhotoBounds(0, 0, 1, 1), crop);
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
