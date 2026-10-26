using System;

namespace HeadLoss.Game.Buildings
{
    /// <summary>Building tile coordinate (1 m tiles, origin bottom-left).</summary>
    public readonly struct TilePos : IEquatable<TilePos>
    {
        public readonly int X;
        public readonly int Y;

        public TilePos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(TilePos other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is TilePos t && Equals(t);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public static bool operator ==(TilePos a, TilePos b) => a.Equals(b);
        public static bool operator !=(TilePos a, TilePos b) => !a.Equals(b);
        public override string ToString() => $"({X}, {Y})";

        /// <summary>|dx| + |dy|.</summary>
        public int ManhattanTo(TilePos o) => Math.Abs(o.X - X) + Math.Abs(o.Y - Y);

        /// <summary>True for the four orthogonal neighbours.</summary>
        public bool IsAdjacentTo(TilePos o) => ManhattanTo(o) == 1;

        /// <summary>Unit step toward <paramref name="o"/> along one axis (o must be on the same row or column).</summary>
        public (int dx, int dy) DirectionTo(TilePos o) => (Math.Sign(o.X - X), Math.Sign(o.Y - Y));
    }
}
