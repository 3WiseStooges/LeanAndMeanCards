using System;
using System.Collections.Generic;
using TMPro;
using UnboundLib;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal static class CardTargetUi
    {
        private static Overlay _overlay;
        private static ToastHost _toast;

        internal static void OpenSandbag(Player user, Action<Player> onConfirm, Action onCancel = null)
        {
            OpenPlayerTarget(
                user,
                "Sandbag Simulator",
                "Choose who rerolls their cards. The game waits until you confirm.",
                "Reroll",
                onConfirm,
                includeSelf: true,
                onCancel);
        }

        internal static void OpenPlayerTarget(
            Player user,
            string title,
            string subtitle,
            string confirmLabel,
            Action<Player> onConfirm,
            bool includeSelf,
            Action onCancel = null)
        {
            EnsureOverlay();
            _overlay.OpenTargetOnly(user, title, subtitle, confirmLabel, onConfirm, includeSelf, onCancel);
        }

        internal static bool IsOpen => _overlay != null && _overlay.gameObject.activeSelf;

        internal static void OpenCardChoices(string title, string subtitle, string confirmLabel, List<(string label, GameObject card)> cards, Action<GameObject> onConfirm, Action onCancel = null)
        {
            EnsureOverlay();
            _overlay.OpenCardChoices(title, subtitle, confirmLabel, cards, onConfirm, onCancel);
        }

        internal static void ShowToast(string message)
        {
            EnsureToast();
            _toast?.Show(message);
        }

        internal static void Close()
        {
            if (_overlay != null) _overlay.Close();
        }

        private static void EnsureOverlay()
        {
            if (_overlay != null) return;
            var canvas = Unbound.Instance?.canvas;
            if (canvas == null) return;

            var go = new GameObject("LAMC_CardTargetUi", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _overlay = go.AddComponent<Overlay>();
            _overlay.Build();
            go.SetActive(false);
        }

        private static void EnsureToast()
        {
            if (_toast != null) return;
            var canvas = Unbound.Instance?.canvas;
            if (canvas == null) return;

            var go = new GameObject("LAMC_Toast", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _toast = go.AddComponent<ToastHost>();
            _toast.Build();
            go.SetActive(true);
        }

        private sealed class ToastHost : MonoBehaviour
        {
            private TextMeshProUGUI _label;
            private CanvasGroup _group;

            internal void Build()
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -28f);
                rect.sizeDelta = new Vector2(720f, 56f);

                _group = gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
                _group.alpha = 0f;

                UiGfx.Round(gameObject.AddComponent<Image>(), UiGfx.Panel);

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(16f, 8f);
                textRect.offsetMax = new Vector2(-16f, -8f);
                _label = textGo.AddComponent<TextMeshProUGUI>();
                UiGfx.StyleTmp(_label, UiGfx.TitleFont, UiGfx.Title, TextAlignmentOptions.Center);
            }

            internal void Show(string message)
            {
                if (_label == null || string.IsNullOrWhiteSpace(message)) return;
                gameObject.SetActive(true);
                _label.text = message;
                _group.alpha = 1f;
                CancelInvoke(nameof(Hide));
                Invoke(nameof(Hide), 3f);
            }

            private void Hide()
            {
                if (_group != null) _group.alpha = 0f;
            }
        }

        private class Overlay : MonoBehaviour
        {
            private RectTransform _panel;
            private TextMeshProUGUI _title;
            private TextMeshProUGUI _subtitle;
            private Transform _playerGrid;
            private Button _confirmButton;
            private Button _cancelButton;
            private Player _actor;
            private Player _selected;
            private Action<Player> _onConfirm;
            private Action _onCancel;
            private string _confirmLabel;

            internal void Build()
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                UiGfx.Solid(
                    CreateImage("Dim", transform, Vector2.zero, Vector2.one),
                    UiGfx.Dim,
                    raycast: true);

                _panel = CreatePanel("Panel", transform, new Vector2(560f, 440f));
                UiGfx.Round(_panel.gameObject.AddComponent<Image>(), UiGfx.Panel);
                UiGfx.AddAccentBar(_panel);

                var layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 18, 16);
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                _title = CreateText("Title", _panel, UiGfx.TitleFont, FontStyles.Bold, 26f);
                _title.color = UiGfx.Title;
                _subtitle = CreateText("Subtitle", _panel, UiGfx.HintFont, FontStyles.Normal, 36f);
                _subtitle.color = UiGfx.Hint;

                var gridGo = new GameObject("PlayerGrid", typeof(RectTransform));
                gridGo.transform.SetParent(_panel, false);
                _playerGrid = gridGo.transform;
                var gridLayout = gridGo.AddComponent<GridLayoutGroup>();
                gridLayout.cellSize = new Vector2(248f, 52f);
                gridLayout.spacing = new Vector2(10f, 10f);
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 2;
                gridLayout.childAlignment = TextAnchor.UpperCenter;
                var gridLe = gridGo.AddComponent<LayoutElement>();
                gridLe.minHeight = 220f;
                gridLe.flexibleHeight = 1f;
                gridLe.preferredHeight = 260f;

                var row = new GameObject("Buttons", typeof(RectTransform));
                row.transform.SetParent(_panel, false);
                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 10f;
                rowLayout.childAlignment = TextAnchor.MiddleCenter;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = true;
                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.minHeight = 32f;
                rowLe.preferredHeight = 32f;

                _cancelButton = CreateRowButton("Cancel", row.transform, "Cancel");
                _confirmButton = CreateRowButton("Confirm", row.transform, _confirmLabel = "Confirm");
                _cancelButton.onClick.AddListener(Cancel);
            }

            internal void OpenTargetOnly(
                Player actor,
                string title,
                string subtitle,
                string confirmLabel,
                Action<Player> onConfirm,
                bool includeSelf = true,
                Action onCancel = null)
            {
                _actor = actor;
                _onConfirm = onConfirm;
                _onCancel = onCancel;
                _confirmLabel = confirmLabel;
                _title.text = title;
                _subtitle.text = subtitle;
                _confirmButton.GetComponentInChildren<TextMeshProUGUI>().text = confirmLabel;
                _selected = includeSelf ? actor : null;
                RebuildPlayerButtons(includeSelf);
                gameObject.SetActive(true);
            }

            internal void OpenCardChoices(string title, string subtitle, string confirmLabel, List<(string label, GameObject card)> cards, Action<GameObject> onConfirm, Action onCancel)
            {
                _actor = null;
                _onConfirm = null;
                _onCancel = onCancel;
                _confirmLabel = confirmLabel;
                _title.text = title;
                _subtitle.text = subtitle;
                _confirmButton.GetComponentInChildren<TextMeshProUGUI>().text = confirmLabel;
                _selected = null;
                ClearGrid();
                GameObject picked = null;
                foreach (var entry in cards)
                {
                    if (entry.card == null) continue;
                    var button = CreateLabeledButton(entry.label);
                    var captured = entry.card;
                    button.onClick.AddListener(() =>
                    {
                        picked = captured;
                        Highlight(button);
                    });
                }

                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(() =>
                {
                    if (picked == null) return;
                    onConfirm?.Invoke(picked);
                    Close();
                });
                _cancelButton.onClick.RemoveAllListeners();
                _cancelButton.onClick.AddListener(Cancel);
                gameObject.SetActive(true);
            }

            internal void Close()
            {
                gameObject.SetActive(false);
                ClearGrid();
            }

            private void Cancel()
            {
                var cancel = _onCancel;
                _onCancel = null;
                cancel?.Invoke();
                Close();
            }

            private void RebuildPlayerButtons(bool includeSelf)
            {
                ClearGrid();
                if (PlayerManager.instance?.players == null) return;

                foreach (var player in PlayerManager.instance.players)
                {
                    if (player == null) continue;
                    if (!includeSelf && _actor != null && player.playerID == _actor.playerID) continue;

                    var label = PlayerLabels.For(player);
                    if (_actor != null && player.playerID == _actor.playerID) label += " (You)";
                    var button = CreateGridButton(label, player);
                    var captured = player;
                    button.onClick.AddListener(() => SelectPlayer(captured, button));
                }

                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(() =>
                {
                    if (_selected == null) return;
                    _onCancel = null;
                    _onConfirm?.Invoke(_selected);
                    Close();
                });
                _cancelButton.onClick.RemoveAllListeners();
                _cancelButton.onClick.AddListener(Cancel);
            }

            private void SelectPlayer(Player player, Button button)
            {
                _selected = player;
                Highlight(button);
            }

            private void Highlight(Button selected)
            {
                var parent = selected.transform.parent;
                foreach (Transform child in parent)
                {
                    var img = child.GetComponent<Image>();
                    if (img != null) img.color = UiGfx.Tile;
                }

                selected.GetComponent<Image>().color = UiGfx.TileSelected;
            }

            private Button CreateLabeledButton(string label)
            {
                return CreateTile(label, _playerGrid, selected: false);
            }

            private Button CreateGridButton(string label, Player player)
            {
                var selected = _selected != null && _selected.playerID == player.playerID;
                return CreateTile(label, _playerGrid, selected);
            }

            private static Button CreateTile(string label, Transform parent, bool selected)
            {
                var go = new GameObject("Tile", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                UiGfx.Round(
                    go.GetComponent<Image>(),
                    selected ? UiGfx.TileSelected : UiGfx.Tile,
                    raycast: true);
                var button = go.GetComponent<Button>();
                UiGfx.GhostColors(button);
                var tmp = CreateInnerText(go.transform, label, UiGfx.TileFont);
                tmp.color = UiGfx.Label;
                return button;
            }

            private void ClearGrid()
            {
                if (_playerGrid == null) return;
                for (var i = _playerGrid.childCount - 1; i >= 0; i--)
                {
                    Destroy(_playerGrid.GetChild(i).gameObject);
                }
            }

            private static RectTransform CreatePanel(string name, Transform parent, Vector2 size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = Vector2.zero;
                return rect;
            }

            private static Image CreateImage(string name, Transform parent, Vector2 min, Vector2 max)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return go.GetComponent<Image>();
            }

            private static TextMeshProUGUI CreateText(string name, Transform parent, float size, FontStyles style, float height)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.AddComponent<LayoutElement>().preferredHeight = height;
                var tmp = go.AddComponent<TextMeshProUGUI>();
                UiGfx.StyleTmp(tmp, size, UiGfx.Title, TextAlignmentOptions.TopLeft);
                tmp.fontStyle = style;
                return tmp;
            }

            private static TextMeshProUGUI CreateInnerText(Transform parent, string label, float size)
            {
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(parent, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10f, 6f);
                rect.offsetMax = new Vector2(-10f, -6f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                UiGfx.StyleTmp(tmp, size, UiGfx.Label, TextAlignmentOptions.Center);
                return tmp;
            }

            private static Button CreateRowButton(string name, Transform parent, string label)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.AddComponent<LayoutElement>().preferredHeight = 32f;
                UiGfx.Round(go.GetComponent<Image>(), UiGfx.Tile, raycast: true);
                var button = go.GetComponent<Button>();
                UiGfx.GhostColors(button);
                var tmp = CreateInnerText(go.transform, label, UiGfx.ButtonFont);
                tmp.color = UiGfx.Label;
                return button;
            }
        }
    }
}
