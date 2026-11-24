using System;
using Rustgrid.Tactics.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rustgrid.Tactics.Presentation
{
    /// <summary>
    /// Turns clicks into commands: click an own unit to select it, a neighbouring cell to step,
    /// an enemy to shoot; Space ends the turn. Legality is decided by the game, not here.
    /// </summary>
    public sealed class BattleInput : MonoBehaviour
    {
        [SerializeField] private BattleView view;

        private GameState _state;
        private bool _enabled;

        public event Action<Command> CommandIssued;
        public event Action SelectionChanged;

        public int SelectedUnitId { get; private set; } = -1;

        public void Show(GameState state, bool humanTurn)
        {
            _state = state;
            _enabled = humanTurn;
            int index = SelectedUnitId < 0 ? -1 : state.IndexOfUnit(SelectedUnitId);
            if (index < 0 || !state.Units[index].IsAlive || state.Units[index].Side != state.ActiveSide) SelectedUnitId = -1;
        }

        private void Update()
        {
            if (!_enabled || _state == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                CommandIssued?.Invoke(new EndTurnCommand());
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (!view.TryGetCell(mouse.position.ReadValue(), _state.Board, out int x, out int y)) return;

            int index = _state.UnitIndexAt(x, y);
            if (index >= 0 && _state.Units[index].Side == _state.ActiveSide)
            {
                SelectedUnitId = _state.Units[index].Id;
                SelectionChanged?.Invoke();
                return;
            }
            if (SelectedUnitId < 0) return;
            CommandIssued?.Invoke(index >= 0
                ? new ShootCommand(SelectedUnitId, _state.Units[index].Id)
                : (Command)new MoveCommand(SelectedUnitId, x, y));
        }
    }
}
