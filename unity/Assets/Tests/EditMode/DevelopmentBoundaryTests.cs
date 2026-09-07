using System;
using System.IO;
using Elts.Config;
using Elts.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace Elts.Tests
{
    public sealed class DevelopmentBoundaryTests
    {
        [Test]
        public void StagedSyntheticConfigurationCannotAuthorizeStudy()
        {
            // Exercise the same generated files shipped in a development player.
            var config = DevelopmentConfiguration.LoadDirectory(
                Path.Combine(Application.streamingAssetsPath, "config-generated"));
            Assert.That(config.Mode, Is.EqualTo("synthetic"));
            Assert.That(config.StudyReady, Is.False);
            Assert.That(config.SourceRawSha256.Count, Is.EqualTo(6));
            Assert.Throws<InvalidOperationException>(() => config.RequireStudyReadiness());
        }

        [Test]
        public void UninitializedTrackingCannotBecomeUsablePose()
        {
            var sample = default(TrackingSample);
            Assert.That(sample.HasValidPose, Is.False);
            Assert.That(sample.Pose.HasValue, Is.False);
            Assert.That(sample.Validity, Is.EqualTo(TrackingValidity.Unavailable));
        }
    }
}
