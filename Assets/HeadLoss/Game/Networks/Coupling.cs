using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Networks
{
    /// <summary>
    /// One device joining two networks (<c>05-simulation.md</c> §1.5): it draws from <see cref="Parent"/> what it sends into
    /// <see cref="Child"/> (a water heater: the same flow), or burns from <see cref="Parent"/> what the heat it gives
    /// <see cref="Child"/> needs (a boiler: <c>Q_gas = P_heat / (η · LHV · ρ_gas)</c>).
    /// </summary>
    public sealed class Coupling
    {
        public string Key = "";
        public EquipmentDef Def = null!;
        /// <summary>The network the device draws from (its in or fuel port).</summary>
        public string Parent = "";
        /// <summary>The network the device feeds (its out or flow port).</summary>
        public string Child = "";
        /// <summary>The device draws fuel for its heat (fuel port) rather than the flow it passes on (in port).</summary>
        public bool Fuel;
    }

    /// <summary>The couplings of a state and the order to solve its networks in.</summary>
    public static class Couplings
    {
        /// <summary>Times the coupled networks are solved in a row (<c>05-simulation.md</c> §1.5: iterate twice).</summary>
        public const int Passes = 2;

        /// <summary>Every device that both draws from one of <paramref name="networks"/> and feeds another.</summary>
        public static List<Coupling> Of(DataRepository data, BuildingState state, IReadOnlyCollection<string> networks)
        {
            var list = new List<Coupling>();
            foreach (PlacedDevice d in state.Devices)
            {
                EquipmentDef? def = data.FindEquipment(d.EquipmentId);
                if (def == null) continue;
                PortDef? from = def.Ports.Find(p => p.Role == "in" || p.Role == "fuel");
                PortDef? to = def.Ports.Find(p => p.Role == "out" || p.Role == "flow");
                if (from == null || to == null || from.Network == to.Network) continue;
                if (!Contains(networks, from.Network) || !Contains(networks, to.Network)) continue;
                list.Add(new Coupling { Key = d.Key, Def = def, Parent = from.Network, Child = to.Network, Fuel = from.Role == "fuel" });
            }
            return list;
        }

        static bool Contains(IReadOnlyCollection<string> c, string id)
        {
            foreach (string s in c) if (s == id) return true;
            return false;
        }

        /// <summary>
        /// Networks in dependency order: a device's child before its parent (hot before cold, heating before gas), the rest as given.
        /// The first network stays first (the air supply). A cycle of couplings keeps the given order for what is left.
        /// </summary>
        public static List<string> Order(IReadOnlyList<string> networks, IReadOnlyList<Coupling> couplings)
        {
            var order = new List<string>();
            var placed = new HashSet<string>();
            void Place(string n, HashSet<string> visiting)
            {
                if (placed.Contains(n) || !visiting.Add(n)) return;
                foreach (Coupling c in couplings) if (c.Parent == n) Place(c.Child, visiting);
                if (placed.Add(n)) order.Add(n);
            }
            if (networks.Count > 0)
            {
                placed.Add(networks[0]);
                order.Add(networks[0]);
            }
            foreach (string n in networks) Place(n, new HashSet<string>());
            return order;
        }

        /// <summary>Gas a boiler burns for its heat, m³/s at the gas's density: <c>P_heat / (η · LHV · ρ_gas)</c>.</summary>
        /// <param name="heatW">Heat given to the water, W.</param>
        /// <param name="efficiency">Thermal efficiency, 0–1.</param>
        /// <param name="lhvMJPerKg">Lower heating value, MJ/kg.</param>
        /// <param name="density">kg/m³.</param>
        public static double FuelFlow(double heatW, double efficiency, double lhvMJPerKg, double density) =>
            heatW <= 0 || efficiency <= 0 || lhvMJPerKg <= 0 || density <= 0 ? 0 : heatW / (efficiency * lhvMJPerKg * 1e6 * density);
    }
}
