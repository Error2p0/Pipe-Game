using HeadLoss.Core.Data;
using HeadLoss.Game.Session;

namespace HeadLoss.Game
{
    /// <summary>Everything the rules need, passed explicitly (no singletons in sim code).</summary>
    public sealed class GameContext
    {
        public DataRepository Data { get; }
        /// <summary>The building open in the building view, if any.</summary>
        public BuildingSession? Building { get; set; }

        public GameContext(DataRepository data) => Data = data;
    }

    /// <summary>Advances the game rules. The Unity layer calls <see cref="Tick"/> from Update.</summary>
    public sealed class GameLoop
    {
        public GameContext Context { get; }

        public GameLoop(GameContext context) => Context = context;

        /// <param name="dtSeconds">Seconds since the last tick.</param>
        public void Tick(double dtSeconds)
        {
            Context.Building?.Tick(dtSeconds);
        }
    }
}
