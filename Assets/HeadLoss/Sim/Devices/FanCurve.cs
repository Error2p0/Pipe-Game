using System;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Sim.Devices
{
    /// <summary>
    /// Fan or pump performance curve (<c>05-simulation.md</c> §2). Full-speed points interpolated with
    /// a monotone cubic; beyond Q_max extended linearly; reverse flow Δp = Δp(0) − K_rev·ρ/(2A²)·Q|Q|;
    /// speed s scales by the affinity laws Δp(Q, s) = s²·Δp₁(Q/s), η(Q, s) = η₁(Q/s).
    /// </summary>
    public sealed class FanCurve
    {
        readonly MonotoneCubic _pressure;
        readonly MonotoneCubic? _efficiency;

        /// <summary>Shutoff pressure at full speed, Pa.</summary>
        public double ShutoffPressure { get; }
        /// <summary>Flow at which the pressure rise reaches zero at full speed, m³/s.</summary>
        public double MaxFlow { get; }
        /// <summary>Reverse-flow loss coefficient.</summary>
        public double ReverseK { get; }
        /// <summary>True when the device draws power (P = Q·Δp/η).</summary>
        public bool HasPower => _efficiency != null;

        /// <param name="flows">Full-speed flows, m³/s, from 0 ascending.</param>
        /// <param name="pressures">Pressure rise at each flow, Pa.</param>
        /// <param name="efficiencies">Efficiency at each flow, or null for unpowered devices.</param>
        /// <param name="reverseK">Reverse-flow K referenced to the connection area.</param>
        public FanCurve(double[] flows, double[] pressures, double[]? efficiencies, double reverseK)
        {
            _pressure = new MonotoneCubic(flows, pressures);
            _efficiency = efficiencies != null ? new MonotoneCubic(flows, efficiencies) : null;
            ShutoffPressure = pressures[0];
            MaxFlow = flows[flows.Length - 1];
            ReverseK = reverseK;
        }

        /// <summary>Builds a curve from <c>equipment.json</c> (flows in m³/h there).</summary>
        public static FanCurve FromDef(EquipmentDef def)
        {
            int n = def.Curve.Count;
            var q = new double[n];
            var p = new double[n];
            var e = new double[n];
            for (int i = 0; i < n; i++)
            {
                q[i] = Units.M3hToM3s(def.Curve[i].FlowM3h);
                p[i] = def.Curve[i].PressurePa;
                e[i] = def.Curve[i].Efficiency;
            }
            return new FanCurve(q, p, def.PowerModel == "curve" ? e : null, def.ReverseK);
        }

        /// <summary>Analytic quadratic curve Δp = Δp0·(1 − (Q/Qmax)²) sampled at <paramref name="points"/> points.</summary>
        public static FanCurve Quadratic(double shutoffPa, double maxFlowM3s, int points = 6, double peakEfficiency = 0, double reverseK = 5)
        {
            var q = new double[points];
            var p = new double[points];
            var e = new double[points];
            for (int i = 0; i < points; i++)
            {
                double x = (double)i / (points - 1);
                q[i] = x * maxFlowM3s;
                p[i] = shutoffPa * (1 - x * x);
                e[i] = Math.Max(0.1 * peakEfficiency, peakEfficiency * (1 - Math.Pow((x - 0.6) / 0.6, 2)));
            }
            return new FanCurve(q, p, peakEfficiency > 0 ? e : null, reverseK);
        }

        /// <summary>Pressure rise, Pa, and its derivative d(Δp)/dQ.</summary>
        /// <param name="flow">m³/s through the device (positive = forward).</param>
        /// <param name="speed">Speed fraction s ∈ (0, 1].</param>
        /// <param name="density">kg/m³ (reverse-flow term).</param>
        /// <param name="area">Connection area, m² (reverse-flow term).</param>
        public double PressureRise(double flow, double speed, double density, double area, out double derivative)
        {
            if (flow < 0)
            {
                double c = ReverseK * density / (2.0 * area * area);
                // rise = s²Δp0 + c·Q² for Q < 0, so d(rise)/dQ = 2cQ (negative, like the forward curve).
                derivative = 2.0 * c * flow;
                return speed * speed * ShutoffPressure - c * flow * Math.Abs(flow);
            }
            double p1 = _pressure.Evaluate(flow / speed, out double d1);
            derivative = speed * d1;
            return speed * speed * p1;
        }

        /// <summary>Efficiency at the operating point (affinity: η₁(Q/s)); 0 for unpowered devices.</summary>
        public double Efficiency(double flow, double speed)
        {
            if (_efficiency == null || speed <= 0) return 0;
            double q = Math.Max(0, Math.Min(flow / speed, MaxFlow));
            return Math.Max(1e-3, _efficiency.Evaluate(q));
        }

        /// <summary>Input power P = Q·Δp/η, W (0 when unpowered or not delivering).</summary>
        public double Power(double flow, double pressureRise, double speed)
        {
            if (_efficiency == null || flow <= 0 || pressureRise <= 0) return 0;
            return flow * pressureRise / Efficiency(flow, speed);
        }
    }
}
