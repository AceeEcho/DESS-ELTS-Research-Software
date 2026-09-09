using System;
using System.IO;
using System.Linq;
using Elts.Config;
using Elts.Logging;
using Elts.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace Elts.Tests
{
    public sealed class DevelopmentBoundaryTests
    {
        [Test]
        public void LoggingImportsAsItsOwnEngineIndependentAssembly()
        {
            // A malformed asmdef metadata file can silently fall back to Assembly-CSharp.
            // Verify the actual imported boundary, not merely a successful test runner.
            var assembly = typeof(SessionLogWriter).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo("Elts.Logging"));
            Assert.That(assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }

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
