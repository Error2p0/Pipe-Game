using System;

namespace HeadLoss.Game.Buildings
{
    /// <summary>Building tile coordinate (1 m tiles, origin bottom-left) on a floor (0 = ground, −1 = basement).</summary>
    public readonly struct TilePos : IEquatable<TilePos>
    {
        public readonly int X;
        public readonly int Y;
        /// <summary>Floor index.</summary>
        public readonly int F;

        public TilePos(int x, int y, int floor = 0)
        {
            X = x;
            Y = y;
            F = floor;
        }

        public bool Equals(TilePos other) => X == other.X && Y == other.Y && F == other.F;
        public override bool Equals(object? obj) => obj is TilePos t && Equals(t);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663 ^ F * 83492791);
        public static bool operator ==(TilePos a, TilePos b) => a.Equals(b);
        public static bool operator !=(TilePos a, TilePos b) => !a.Equals(b);
        public override string ToString() => F == 0 ? $"({X}, {Y})" : $"({X}, {Y}, floor {F})";

        /// <summary>The same tile on another floor.</summary>
        public TilePos OnFloor(int floor) => new TilePos(X, Y, floor);

        /// <summary>|dx| + |dy| (horizontal tiles; floors are not counted).</summary>
        public int ManhattanTo(TilePos o) => Math.Abs(o.X - X) + Math.Abs(o.Y - Y);

        /// <summary>True for the four orthogonal neighbours on the same floor.</summary>
        public bool IsAdjacentTo(TilePos o) => F == o.F && ManhattanTo(o) == 1;

        /// <summary>True for the same tile one floor up or down (a riser step).</summary>
        public bool IsVerticalNeighbour(TilePos o) => X == o.X && Y == o.Y && Math.Abs(F - o.F) == 1;

        /// <summary>True when <paramref name="o"/> is straight above or below (same tile, another floor).</summary>
        public bool IsVerticalTo(TilePos o) => X == o.X && Y == o.Y && F != o.F;

        /// <summary>Unit step toward <paramref name="o"/> along one horizontal axis (o must be on the same row or column and floor).</summary>
        public (int dx, int dy) DirectionTo(TilePos o) => (Math.Sign(o.X - X), Math.Sign(o.Y - Y));

        /// <summary>Unit step toward <paramref name="o"/> along the single axis they differ on (x, y or floor).</summary>
        public (int dx, int dy, int df) StepTo(TilePos o) => (Math.Sign(o.X - X), Math.Sign(o.Y - Y), Math.Sign(o.F - F));

        /// <summary>The next tile toward <paramref name="o"/> on a straight run (horizontal or vertical).</summary>
        public TilePos Toward(TilePos o)
        {
            (int dx, int dy, int df) = StepTo(o);
            return new TilePos(X + dx, Y + dy, F + df);
        }

        /// <summary>Tiles from here to <paramref name="o"/> along a straight run, counting each step (horizontal metres or floors).</summary>
        public int StepsTo(TilePos o) => Math.Abs(o.X - X) + Math.Abs(o.Y - Y) + Math.Abs(o.F - F);

        /// <summary>Length of a straight run to <paramref name="o"/>, m: horizontal tiles are 1 m, floors are the floor height.</summary>
        /// <param name="floorHeight">m.</param>
        public double LengthTo(TilePos o, double floorHeight) => ManhattanTo(o) + Math.Abs(o.F - F) * floorHeight;

        /// <summary>True when the three tiles are on one straight line (horizontal or vertical) with this one in the middle.</summary>
        public static bool Collinear(TilePos a, TilePos mid, TilePos b)
        {
            (int ax, int ay, int af) = a.StepTo(mid);
            (int bx, int by, int bf) = mid.StepTo(b);
            return ax == bx && ay == by && af == bf;
        }
    }
}
