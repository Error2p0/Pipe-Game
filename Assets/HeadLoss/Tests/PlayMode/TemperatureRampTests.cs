using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Unity.View;
using NUnit.Framework;
using UnityEngine;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>Temperature relative to the setpoint on a diverging ramp (<c>11-ui-visuals-audio.md</c> §Visual style).</summary>
    public class TemperatureRampTests
    {
        static DataRepository Data => DataRepository.Load(new FileDataSource("Assets/HeadLoss/Data"));

        [Test]
        public void Temperature_UsesTheDivergingRamp_CentredOnTheComfortBand()
        {
            HeatmapDef def = Data.Balancing.Heatmap;
            var hm = new HeatmapState(def);
            hm.SetMode(HeatmapMode.Velocity);
            Assert.AreEqual(def.DefaultPalette, hm.Ramp, "other modes keep the chosen palette");
            hm.SetMode(HeatmapMode.Temperature);
            Assert.AreEqual(HeatmapState.DivergingRamp, hm.Ramp);
            Assert.AreEqual(def.DivergingCentreC - def.DivergingHalfRangeK, hm.RangeMin, 1e-5);
            Assert.AreEqual(def.DivergingCentreC + def.DivergingHalfRangeK, hm.RangeMax, 1e-5);
            Assert.AreEqual(0.5f, hm.Normalise(def.DivergingCentreC), 1e-5f, "white sits at the centre");
        }

        [Test]
        public void Temperature_WidensEvenlySoWhiteStaysAtTheCentre()
        {
            HeatmapDef def = Data.Balancing.Heatmap;
            var hm = new HeatmapState(def);
            hm.SetMode(HeatmapMode.Temperature);
            hm.AutoRange(15, 70);
            double half = System.Math.Ceiling((70 - def.DivergingCentreC) / def.TemperatureStepK) * def.TemperatureStepK;
            Assert.AreEqual(def.DivergingCentreC - half, hm.RangeMin, 1e-4);
            Assert.AreEqual(def.DivergingCentreC + half, hm.RangeMax, 1e-4);
            Assert.AreEqual(0.5f, hm.Normalise(def.DivergingCentreC), 1e-5f);
        }

        [Test]
        public void Diverging_Off_UsesThePaletteAndItsAnchors()
        {
            HeatmapDef def = Data.Balancing.Heatmap;
            var hm = new HeatmapState(def);
            hm.SetMode(HeatmapMode.Temperature);
            hm.SetDiverging(false);
            Assert.AreEqual(def.DefaultPalette, hm.Ramp);
            Assert.AreEqual(def.TemperatureAnchorC[0], hm.RangeMin, 1e-5);
            Assert.AreEqual(def.TemperatureAnchorC[1], hm.RangeMax, 1e-5);
        }

        [Test]
        public void DivergingRamp_IsBlueColdWhiteMiddleRedWarm()
        {
            var palette = new ViewPalette(Data);
            Color cold = palette.Ramp(HeatmapState.DivergingRamp, 0), mid = palette.Ramp(HeatmapState.DivergingRamp, 0.5f), warm = palette.Ramp(HeatmapState.DivergingRamp, 1);
            Assert.Greater(cold.b, cold.r, "cold end is blue");
            Assert.Greater(warm.r, warm.b, "warm end is red");
            Assert.Greater(Mathf.Min(mid.r, Mathf.Min(mid.g, mid.b)), 0.9f, "the middle is near white");
        }
    }
}
