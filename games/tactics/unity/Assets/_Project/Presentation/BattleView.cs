using System.Collections.Generic;
using Rustgrid.Tactics.Core;
using UnityEngine;

namespace Rustgrid.Tactics.Presentation
{
    /// <summary>
    /// Placeholder geometry for a battle: floor tiles, wall blocks and one capsule per unit.
    /// It only reads state and events; the game itself never sees this class.
    /// Cell (x, y) maps to world (x, 0, y).
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [Tooltip("URP Lit material tinted per surface. Primitives' built-in default material renders magenta under URP.")]
        [SerializeField] private Material baseMaterial;

        private readonly Dictionary<int, Transform> _units = new Dictionary<int, Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private Transform _root;
        private Transform _selection;

        public void Build(GameState state)
        {
            if (_root != null) Destroy(_root.gameObject);
            _units.Clear();
            _root = new GameObject("Board").transform;
            _root.SetParent(transform, false);

            var board = state.Board;
            for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                bool wall = board.IsWall(x, y);
                var block = Spawn(PrimitiveType.Cube, wall ? "Wall" : "Floor", wall ? new Color(0.25f, 0.24f, 0.22f) : FloorColor(x, y));
                block.localScale = wall ? new Vector3(1f, 1.4f, 1f) : new Vector3(0.96f, 0.1f, 0.96f);
                block.localPosition = new Vector3(x, wall ? 0.7f : -0.05f, y);
            }

            foreach (var unit in state.Units)
            {
                var body = Spawn(PrimitiveType.Capsule, $"Unit {unit.Id}", SideColor(unit.Side));
                body.localScale = new Vector3(0.6f, 0.75f, 0.6f);
                body.localPosition = UnitPosition(unit.X, unit.Y);
                body.gameObject.SetActive(unit.IsAlive);
                _units[unit.Id] = body;
            }

            _selection = Spawn(PrimitiveType.Cylinder, "Selection", new Color(1f, 0.85f, 0.2f));
            _selection.localScale = new Vector3(0.9f, 0.02f, 0.9f);
            _selection.gameObject.SetActive(false);
            FrameCamera(board);
        }

        public void Present(StepResult result)
        {
            foreach (var e in result.Events)
            {
                switch (e)
                {
                    case UnitMoved moved:
                        _units[moved.UnitId].localPosition = UnitPosition(moved.ToX, moved.ToY);
                        break;
                    case UnitKilled killed:
                        _units[killed.UnitId].gameObject.SetActive(false);
                        break;
                }
            }
        }

        public void ShowSelection(GameState state, int unitId)
        {
            int index = unitId < 0 ? -1 : state.IndexOfUnit(unitId);
            bool visible = index >= 0 && state.Units[index].IsAlive;
            _selection.gameObject.SetActive(visible);
            if (visible) _selection.localPosition = new Vector3(state.Units[index].X, 0.02f, state.Units[index].Y);
        }

        /// <summary>The board cell under a screen point, by intersecting the ground plane.</summary>
        public bool TryGetCell(Vector2 screenPoint, Board board, out int x, out int y)
        {
            x = y = -1;
            var ray = viewCamera.ScreenPointToRay(screenPoint);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;
            var point = transform.InverseTransformPoint(ray.GetPoint(distance));
            x = Mathf.RoundToInt(point.x);
            y = Mathf.RoundToInt(point.z);
            return board.Inside(x, y);
        }

        private void OnDestroy()
        {
            foreach (var material in _materials.Values) Destroy(material);
        }

        private Transform Spawn(PrimitiveType type, string name, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // Picking uses the ground plane, so colliders would only cost physics work.
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        private Material MaterialFor(Color color)
        {
            if (baseMaterial == null) throw new MissingReferenceException("BattleView.baseMaterial is not assigned (run Rustgrid/Rebuild Battle Scene)");
            if (!_materials.TryGetValue(color, out var material))
            {
                material = new Material(baseMaterial) { color = color };
                _materials[color] = material;
            }
            return material;
        }

        private void FrameCamera(Board board)
        {
            var center = transform.TransformPoint(new Vector3((board.Width - 1) * 0.5f, 0f, (board.Height - 1) * 0.5f));
            viewCamera.orthographic = true;
            viewCamera.orthographicSize = Mathf.Max(board.Width, board.Height) * 0.55f + 1f;
            viewCamera.transform.rotation = Quaternion.Euler(35f, 45f, 0f);
            viewCamera.transform.position = center - viewCamera.transform.forward * 60f;
            viewCamera.nearClipPlane = 0.1f;
            viewCamera.farClipPlane = 200f;
        }

        private static Vector3 UnitPosition(int x, int y) => new Vector3(x, 0.75f, y);

        private static Color FloorColor(int x, int y) =>
            (x + y) % 2 == 0 ? new Color(0.45f, 0.41f, 0.34f) : new Color(0.41f, 0.38f, 0.31f);

        private static Color SideColor(int side)
        {
            switch (side)
            {
                case 0: return new Color(0.25f, 0.55f, 0.9f);
                case 1: return new Color(0.85f, 0.3f, 0.25f);
                case 2: return new Color(0.3f, 0.75f, 0.35f);
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
