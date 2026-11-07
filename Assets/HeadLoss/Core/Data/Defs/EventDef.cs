using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>events.json</c> (<c>10-events.md</c> §Events).</summary>
    public sealed class EventDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>The calm banner line; {district}, {facility} and {building} are filled in.</summary>
        public string Banner = "";
        /// <summary>city, district, facility or building: what one occurrence targets.</summary>
        public string Scope = "";
        /// <summary>Relative chance among the events that can start now.</summary>
        public double Weight;
        /// <summary>Duration range, min (a building event's longest wait for the player to adapt is <see cref="MaxMinutes"/>).</summary>
        public double MinMinutes;
        public double MaxMinutes;
        /// <summary>
        /// What must exist for the event to make sense (<c>10-events.md</c>: "no cold snap before heating exists"): heating, cooling,
        /// power (the power unlock), online-building. All must hold.
        /// </summary>
        public List<string> Requires = new List<string>();
        /// <summary>District scope: the district kinds it can hit (empty = any).</summary>
        public List<string> DistrictKinds = new List<string>();
        /// <summary>Facility scope: the facility ids it can hit (empty = any but substations).</summary>
        public List<string> Facilities = new List<string>();

        // ----- effects -----

        /// <summary>Outside temperature shift while it lasts, K.</summary>
        public double OutsideTemperatureShiftK;
        /// <summary>Solar gain multiplier while it lasts (1 = none).</summary>
        public double SolarFactor = 1;
        /// <summary>Multiplier on outlet requirements (minimum, ideal, code maximum) while it lasts (1 = none).</summary>
        public double RequirementFactor = 1;
        /// <summary>Outlet types the requirement factor applies to (empty = every outlet).</summary>
        public List<string> RequirementOutlets = new List<string>();
        /// <summary>Raise only the minimum (the festival): the ideal and the code maximum stay.</summary>
        public bool MinimumOnly;
        /// <summary>Facility source pressure and capacity multiplier while it lasts (1 = none).</summary>
        public double FacilityFactor = 1;
        /// <summary>The district loses its power (the outage cascade, <c>10-events.md</c> §Power outage cascade).</summary>
        public bool PowerOutage;
        /// <summary>New tenant: share of a building's outlets whose requirements are regenerated, range 0–1.</summary>
        public double ChangeShareMin;
        public double ChangeShareMax;

        // ----- reward -----

        /// <summary>Pay multiplier for buildings that earn the reward (1 = none).</summary>
        public double RewardPayFactor = 1;
        /// <summary>keep-up (meet every raised minimum while it lasts) or adapt (meet every minimum again within <see cref="AdaptMinutes"/>).</summary>
        public string RewardRule = "";
        /// <summary>Adapt rule: the time the player has after the change, min (pauses offline).</summary>
        public double AdaptMinutes;
        /// <summary>Adapt rule: how long the reward lasts once earned, min.</summary>
        public double RewardMinutes;
    }

    /// <summary><c>inspections.json</c> (<c>10-events.md</c> §Inspections): the inspection kinds and the energy rating bands.</summary>
    public sealed class InspectionsDef
    {
        public List<InspectionKindDef> Kinds = new List<InspectionKindDef>();
        /// <summary>Energy rating bands, best first; the last one has no upper limit.</summary>
        public List<RatingBandDef> EnergyRating = new List<RatingBandDef>();
    }

    public sealed class InspectionKindDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>What it checks, one calm line.</summary>
        public string Checks = "";
    }

    /// <summary>One energy rating grade: score = P_actual / P_reference at or below <see cref="MaxScore"/>.</summary>
    public sealed class RatingBandDef
    {
        public string Grade = "";
        /// <summary>Highest score in the band (0 = no upper limit, the last band).</summary>
        public double MaxScore;
        /// <summary>Pay multiplier for the grade.</summary>
        public double PayFactor;
    }
}
