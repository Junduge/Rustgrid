using System.Collections.Generic;
using Rustgrid.Tactics.Core;
using UnityEngine;

namespace Rustgrid.Tactics.Presentation
{
    /// <summary>Temporary IMGUI overlay: turn, units, recent events. Replace with real UI later.</summary>
    public sealed class BattleHud : MonoBehaviour
    {
        private const int MaxLogLines = 10;

        private readonly List<string> _log = new List<string>();
        private GameState _state;
        private int _selectedUnitId = -1;
        private string _hint = "";

        public void Show(GameState state, int selectedUnitId)
        {
            _state = state;
            _selectedUnitId = selectedUnitId;
        }

        public void Hint(string text) => _hint = text;

        public void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > MaxLogLines) _log.RemoveAt(0);
            _hint = "";
        }

        public static string Describe(GameEvent e)
        {
            switch (e)
            {
                case TurnStarted t: return $"Round {t.Turn}: side {SideName(t.Side)} acts";
                case UnitMoved m: return $"Unit {m.UnitId} steps to ({m.ToX},{m.ToY}), {m.ApLeft} AP left";
                case ShotFired s: return $"Unit {s.UnitId} fires at {s.TargetId}: {s.Hits} hit(s) at {s.HitChance}%, {s.Damage} damage{(s.Critical ? ", critical" : "")}";
                case UnitKilled k: return $"Unit {k.UnitId} is down";
                case BattleEnded b: return b.Outcome == Outcome.Victory ? $"Side {SideName(b.WinnerSide)} wins" : "Draw";
                default: return e.GetType().Name;
            }
        }

        private static string SideName(int side) => ((char)('A' + side)).ToString();

        private void OnGUI()
        {
            if (_state == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 420, Screen.height - 24), GUI.skin.box);
            GUILayout.Label(_state.Outcome == Outcome.None
                ? $"Round {_state.Turn} — side {SideName(_state.ActiveSide)} to act"
                : $"Battle over: {_state.Outcome}{(_state.WinnerSide >= 0 ? ", side " + SideName(_state.WinnerSide) : "")}");
            GUILayout.Label("Click own unit to select, a neighbouring cell to step, an enemy to shoot. Space ends the turn.");
            foreach (var u in _state.Units)
            {
                if (!u.IsAlive) continue;
                string weapon = _state.Rules.Weapons[u.Weapon].Id;
                string mark = u.Id == _selectedUnitId ? "> " : "  ";
                GUILayout.Label($"{mark}Unit {u.Id} [{SideName(u.Side)}] ({u.X},{u.Y})  HP {u.Hp}/{u.MaxHp}  AP {u.Ap}/{u.MaxAp}  {weapon}");
            }
            if (_hint.Length > 0) GUILayout.Label("! " + _hint);
            GUILayout.Space(6);
            foreach (string line in _log) GUILayout.Label(line);
            GUILayout.EndArea();
        }
    }
}
