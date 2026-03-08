using NUnit.Framework;
using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Tests.Characters;

/// <summary>
/// Tests for the XPThresholds static lookup table.
/// </summary>
[TestFixture]
public class XPThresholdsTests
{
    [Test]
    public void GetXPForLevel_Level1_IsZero()
    {
        Assert.That(XPThresholds.GetXPForLevel(1), Is.EqualTo(0));
    }

    [Test]
    public void GetXPForLevel_Level2_Is300()
    {
        Assert.That(XPThresholds.GetXPForLevel(2), Is.EqualTo(300));
    }

    [Test]
    public void GetXPForLevel_Level5_Is6500()
    {
        Assert.That(XPThresholds.GetXPForLevel(5), Is.EqualTo(6500));
    }

    [Test]
    public void GetLevelForXP_BoundaryValues()
    {
        Assert.That(XPThresholds.GetLevelForXP(0), Is.EqualTo(1));
        Assert.That(XPThresholds.GetLevelForXP(299), Is.EqualTo(1));
        Assert.That(XPThresholds.GetLevelForXP(300), Is.EqualTo(2));
    }
}
