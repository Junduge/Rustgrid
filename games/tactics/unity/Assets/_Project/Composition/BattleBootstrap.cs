using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;
using Rustgrid.Tactics.Core;
using Rustgrid.Tactics.Presentation;
using UnityEngine;

namespace Rustgrid.Tactics.Composition
{
    /// <summary>
    /// Composition root of the battle scene: loads data, creates the game, wires view, input and HUD.
    /// Local play only (side A human, other sides bots) until the multiplayer server exists.
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] private string levelId = "arena_01";
        [SerializeField] private long seed = 1;
        [SerializeField] private bool botsPlayOtherSides = true;
        [SerializeField] private float botDelaySeconds = 0.3f;
        [SerializeField] private BattleView view;
        [SerializeField] private BattleInput input;
        [SerializeField] private BattleHud hud;

        private Game _game;
        private IAgent _bot;
        private Rng _botRng;
        private float _nextBotTime;

        private void Start()
        {
            var data = GameDataSource.ForThisRuntime();
            var rules = TacticsData.ReadRules(data.ReadRules());
            var level = TacticsData.ReadLevel(data.ReadLevel(levelId));
            _game = Game.Create(level, rules, (ulong)seed);
            _bot = new GreedyAgent();
            _botRng = new Rng((ulong)seed ^ 0xB07_5EEDUL);

            view.Build(_game.State);
            input.CommandIssued += Submit;
            input.SelectionChanged += Refresh;
            hud.Log($"{level.LevelId} ({StableHasher.ToHex(_game.State.Rules.LevelHash)}), seed {seed}");
            Refresh();
        }

        private void OnDestroy()
        {
            if (input == null) return;
            input.CommandIssued -= Submit;
            input.SelectionChanged -= Refresh;
        }

        private void Update()
        {
            // Pacing for watching bots is presentation timing; the rules never see Time.
            if (_game == null || _game.IsOver || !IsBotSide(_game.State.ActiveSide) || Time.time < _nextBotTime) return;
            _nextBotTime = Time.time + botDelaySeconds;
            Submit(_bot.Choose(_game, _game.LegalCommands(), ref _botRng));
        }

        private bool IsBotSide(int side) => botsPlayOtherSides && side != 0;

        private void Submit(Command command)
        {
            try
            {
                var result = _game.Apply(command);
                view.Present(result);
                foreach (var e in result.Events) hud.Log(BattleHud.Describe(e));
            }
            catch (InvalidCommandException e)
            {
                hud.Hint(e.Message);
            }
            Refresh();
        }

        private void Refresh()
        {
            var state = _game.State;
            input.Show(state, !_game.IsOver && !IsBotSide(state.ActiveSide));
            view.ShowSelection(state, input.SelectedUnitId);
            hud.Show(state, input.SelectedUnitId);
        }
    }
}
