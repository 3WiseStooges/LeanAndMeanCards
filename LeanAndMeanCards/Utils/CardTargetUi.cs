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

        internal static void OpenSandbag(Player user, Action<Player> onConfirm)
        {
            OpenPlayerTarget(
                user,
                "Sandbag Simulator",
                "Choose who rerolls their current pick hand.",
                "REROLL HAND",
                onConfirm,
                includeSelf: true);
        }

        internal static void OpenPlayerTarget(Player user, string title, string subtitle, string confirmLabel, Action<Player> onConfirm, bool includeSelf)
        {
            EnsureOverlay();
            _overlay.OpenTargetOnly(user, title, subtitle, confirmLabel, onConfirm, includeSelf);
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

            var go = new GameObject("MM_CardTargetUi", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _overlay = go.AddComponent<Overlay>();
            _overlay.Build(canvas.transform);
            go.SetActive(false);
        }

        private static void EnsureToast()
        {
            if (_toast != null) return;
            var canvas = Unbound.Instance?.canvas;
            if (canvas == null) return;

            var go = new GameObject("MM_Toast", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _toast = go.AddComponent<ToastHost>();
            _toast.Build();
            go.SetActive(true);
        }

        private sealed class ToastHost : MonoBehaviour
        {
            private TextMeshProUGUI _label;
            private CanvasGroup _group;
            private Image _bg;

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

                _bg = gameObject.AddComponent<Image>();
                _bg.color = new Color(0.06f, 0.10f, 0.08f, 0.92f);
                _bg.raycastTarget = false;

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(16f, 8f);
                textRect.offsetMax = new Vector2(-16f, -8f);
                _label = textGo.AddComponent<TextMeshProUGUI>();
                _label.alignment = TextAlignmentOptions.Center;
                _label.fontSize = 20f;
                _label.fontStyle = FontStyles.Bold;
                _label.color = new Color(1f, 0.95f, 0.82f, 1f);
                _label.raycastTarget = false;
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
            private string _confirmLabel;

            internal void Build(Transform canvasRoot)
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var dim = CreateImage("Dim", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                dim.color = new Color(0f, 0f, 0f, 0.72f);

                _panel = CreatePanel("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(680f, 520f));
                var panelBg = _panel.gameObject.AddComponent<Image>();
                panelBg.color = new Color(0.08f, 0.12f, 0.10f, 0.98f);

                _title = CreateText("Title", _panel, new Vector2(24f, -20f), 30f, FontStyles.Bold);
                _subtitle = CreateText("Subtitle", _panel, new Vector2(24f, -58f), 18f, FontStyles.Normal);
                _subtitle.color = new Color(0.85f, 0.88f, 0.82f);

                var gridGo = new GameObject("PlayerGrid", typeof(RectTransform));
                gridGo.transform.SetParent(_panel, false);
                _playerGrid = gridGo.transform;
                var gridRect = gridGo.GetComponent<RectTransform>();
                gridRect.anchorMin = new Vector2(0f, 0.22f);
                gridRect.anchorMax = new Vector2(1f, 0.78f);
                gridRect.offsetMin = new Vector2(24f, 0f);
                gridRect.offsetMax = new Vector2(-24f, 0f);
                var layout = gridGo.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(190f, 72f);
                layout.spacing = new Vector2(12f, 12f);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 2;

                _confirmButton = CreateButton("Confirm", _panel, new Vector2(0.56f, 0.08f), _confirmLabel = "CONFIRM");
                _cancelButton = CreateButton("Cancel", _panel, new Vector2(0.08f, 0.08f), "CANCEL");
                _cancelButton.onClick.AddListener(Close);
            }

            internal void OpenTargetOnly(Player actor, string title, string subtitle, string confirmLabel, Action<Player> onConfirm, bool includeSelf = true)
            {
                _actor = actor;
                _onConfirm = onConfirm;
                _confirmLabel = confirmLabel;
                _title.text = title;
                _subtitle.text = subtitle;
                _confirmButton.GetComponentInChildren<TextMeshProUGUI>().text = confirmLabel;
                _selected = includeSelf ? actor : null;
                RebuildPlayerButtons(includeSelf, filterStealable: false);
                gameObject.SetActive(true);
            }

            internal void OpenCardChoices(string title, string subtitle, string confirmLabel, List<(string label, GameObject card)> cards, Action<GameObject> onConfirm, Action onCancel)
            {
                _actor = null;
                _onConfirm = null;
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
                        foreach (Transform child in _playerGrid)
                        {
                            var img = child.GetComponent<Image>();
                            if (img != null) img.color = new Color(0.14f, 0.20f, 0.16f, 1f);
                        }

                        button.GetComponent<Image>().color = new Color(0.18f, 0.48f, 0.30f, 1f);
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
                _cancelButton.onClick.AddListener(() =>
                {
                    onCancel?.Invoke();
                    Close();
                });
                gameObject.SetActive(true);
            }

            internal void Close()
            {
                gameObject.SetActive(false);
                ClearGrid();
            }

            private void RebuildPlayerButtons(bool includeSelf, bool filterStealable)
            {
                ClearGrid();
                if (PlayerManager.instance?.players == null) return;

                foreach (var player in PlayerManager.instance.players)
                {
                    if (player == null) continue;
                    if (!includeSelf && _actor != null && player.playerID == _actor.playerID) continue;

                    var stealable = _actor != null ? StealRules.CountStealableCards(_actor, player) : 0;
                    if (filterStealable && stealable <= 0) continue;

                    var label = BuildPlayerLabel(player, stealable, filterStealable);
                    var button = CreateGridButton(label, player);
                    var captured = player;
                    button.onClick.AddListener(() => SelectPlayer(captured, button));
                }

                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(() =>
                {
                    if (_selected == null) return;
                    _onConfirm?.Invoke(_selected);
                    Close();
                });
                _cancelButton.onClick.RemoveAllListeners();
                _cancelButton.onClick.AddListener(Close);
            }

            private void SelectPlayer(Player player, Button button)
            {
                _selected = player;
                foreach (Transform child in _playerGrid)
                {
                    var img = child.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.14f, 0.20f, 0.16f, 1f);
                }

                button.GetComponent<Image>().color = new Color(0.18f, 0.48f, 0.30f, 1f);
            }

            private Button CreateLabeledButton(string label)
            {
                var go = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_playerGrid, false);
                var image = go.GetComponent<Image>();
                image.color = new Color(0.14f, 0.20f, 0.16f, 1f);
                var button = go.GetComponent<Button>();
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8f, 4f);
                rect.offsetMax = new Vector2(-8f, -4f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = 16f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                return button;
            }

            private Button CreateGridButton(string label, Player player)
            {
                var go = new GameObject("Player_" + player.playerID, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_playerGrid, false);
                var image = go.GetComponent<Image>();
                image.color = _selected != null && _selected.playerID == player.playerID
                    ? new Color(0.18f, 0.48f, 0.30f, 1f)
                    : new Color(0.14f, 0.20f, 0.16f, 1f);
                var button = go.GetComponent<Button>();
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8f, 4f);
                rect.offsetMax = new Vector2(-8f, -4f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = 16f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                return button;
            }

            private static string BuildPlayerLabel(Player player, int stealable, bool showStealable)
            {
                var name = player.data?.view?.Owner?.NickName;
                if (string.IsNullOrEmpty(name)) name = "Player " + (player.playerID + 1);
                if (!showStealable) return name;
                return stealable > 0 ? $"{name}\n{stealable} stealable" : $"{name}\nno cards";
            }

            private void ClearGrid()
            {
                if (_playerGrid == null) return;
                for (var i = _playerGrid.childCount - 1; i >= 0; i--)
                {
                    Destroy(_playerGrid.GetChild(i).gameObject);
                }
            }

            private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = Vector2.zero;
                return rect;
            }

            private static Image CreateImage(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;
                return go.GetComponent<Image>();
            }

            private static TextMeshProUGUI CreateText(string name, Transform parent, Vector2 anchoredPos, float size, FontStyles style)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = anchoredPos;
                rect.sizeDelta = new Vector2(-48f, 32f);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = size;
                tmp.fontStyle = style;
                tmp.color = Color.white;
                return tmp;
            }

            private static Button CreateButton(string name, Transform parent, Vector2 anchorMin, string label)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMin;
                rect.pivot = new Vector2(0f, 0f);
                rect.sizeDelta = new Vector2(260f, 48f);
                rect.anchoredPosition = Vector2.zero;
                go.GetComponent<Image>().color = new Color(0.12f, 0.42f, 0.28f, 1f);
                var button = go.GetComponent<Button>();
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 18f;
                tmp.fontStyle = FontStyles.Bold;
                return button;
            }
        }
    }
}
