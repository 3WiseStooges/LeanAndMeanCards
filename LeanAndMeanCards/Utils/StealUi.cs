using System.Collections.Generic;
using LeanAndMeanCards.Cards;
using TMPro;
using UnboundLib;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal static class StealUi
    {
        private static StealOverlay _overlay;

        internal static bool TryOpen(Player thief)
        {
            if (thief == null) return false;
            EnsureOverlay();
            if (_overlay != null && _overlay.gameObject.activeSelf) return true;
            if (!StealLedger.HasAnyStealableTarget(thief))
            {
                StealLedger.OnStealUiClosedWithoutSteal(thief);
                PlayerNotice.Show(thief, "Nobody has cards to steal yet.");
                return false;
            }

            EnsureOverlay();
            if (_overlay == null) return false;

            StealLedger.OnStealUiOpened(thief);
            _overlay.Open(thief);
            return true;
        }

        internal static void Close()
        {
            if (_overlay != null) _overlay.Close(false);
        }

        internal static void OnStealResult(bool ok, string message)
        {
            if (_overlay != null) _overlay.HandleStealResult(ok, message);
        }

        private static void EnsureOverlay()
        {
            if (_overlay != null) return;
            var canvas = Unbound.Instance?.canvas;
            if (canvas == null) return;

            var go = new GameObject("MM_StealUi", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _overlay = go.AddComponent<StealOverlay>();
            _overlay.Build();
            go.SetActive(false);
        }

        private class StealOverlay : MonoBehaviour
        {
            private enum Step { PickTarget, PickCard, Confirm }

            private RectTransform _panel;
            private TextMeshProUGUI _title;
            private TextMeshProUGUI _subtitle;
            private Transform _content;
            private Button _primaryButton;
            private Button _secondaryButton;
            private Player _thief;
            private Player _target;
            private CardInfo _selectedCard;
            private Step _step = Step.PickTarget;
            private bool _completedSteal;
            private bool _awaitingResult;

            internal void Build()
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var dim = CreateImage("Dim", transform, Vector2.zero, Vector2.one);
                dim.color = new Color(0f, 0f, 0f, 0.78f);

                _panel = CreatePanel(transform, new Vector2(760f, 560f));
                var bg = _panel.gameObject.AddComponent<Image>();
                bg.color = new Color(0.07f, 0.11f, 0.09f, 0.98f);

                _title = CreateHeader("Title", _panel, 32f);
                _subtitle = CreateHeader("Subtitle", _panel, 18f);
                _subtitle.rectTransform.anchoredPosition = new Vector2(24f, -54f);

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(_panel, false);
                _content = contentGo.transform;
                var contentRect = contentGo.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 0.18f);
                contentRect.anchorMax = new Vector2(1f, 0.72f);
                contentRect.offsetMin = new Vector2(24f, 0f);
                contentRect.offsetMax = new Vector2(-24f, 0f);

                _secondaryButton = CreateFooterButton("Secondary", _panel, new Vector2(0.06f, 0.06f), "CANCEL");
                _primaryButton = CreateFooterButton("Primary", _panel, new Vector2(0.56f, 0.06f), "NEXT");
            }

            internal void Open(Player thief)
            {
                _thief = thief;
                _target = null;
                _selectedCard = null;
                _step = Step.PickTarget;
                _completedSteal = false;
                _awaitingResult = false;
                gameObject.SetActive(true);
                RenderStep();
            }

            internal void Close(bool completedSteal)
            {
                if (_awaitingResult && !completedSteal) return;

                _completedSteal = completedSteal;
                _awaitingResult = false;
                gameObject.SetActive(false);
                ClearContent();

                if (_thief != null && !completedSteal && !StealLedger.HasUsedThief(_thief))
                {
                    StealLedger.OnStealUiClosedWithoutSteal(_thief);
                }
            }

            internal void HandleStealResult(bool ok, string message)
            {
                if (!_awaitingResult) return;
                _awaitingResult = false;
                if (!string.IsNullOrEmpty(message))
                {
                    PlayerNotice.Show(_thief, message);
                }

                Close(ok);
            }

            private void RenderStep()
            {
                ClearContent();

                if (_thief != null && !StealLedger.HasAnyStealableTarget(_thief))
                {
                    PlayerNotice.Show(_thief, "Nobody has cards to steal anymore.");
                    Close(false);
                    return;
                }

                switch (_step)
                {
                    case Step.PickTarget:
                        _title.text = "Thief";
                        _subtitle.text = "Step 1 - choose who to rob.";
                        BuildTargetGrid();
                        WireButtons("CANCEL", "NEXT", Cancel, ConfirmTarget);
                        break;
                    case Step.PickCard:
                        _title.text = "Thief";
                        _subtitle.text = $"Step 2 - pick a card from {PlayerLabel(_target)}.";
                        BuildCardGrid();
                        WireButtons("BACK", "NEXT", () => { _step = Step.PickTarget; RenderStep(); }, ConfirmCard);
                        break;
                    case Step.Confirm:
                        _title.text = "Thief";
                        _subtitle.text =
                            $"Steal {_selectedCard?.cardName ?? "card"} from {PlayerLabel(_target)}?";
                        BuildConfirmSummary();
                        WireButtons("BACK", "STEAL", () => { _step = Step.PickCard; RenderStep(); }, ExecuteSteal);
                        break;
                }
            }

            private void BuildTargetGrid()
            {
                var layout = _content.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(210f, 78f);
                layout.spacing = new Vector2(12f, 12f);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 2;

                foreach (var player in PlayerManager.instance.players)
                {
                    if (player == null || player.playerID == _thief.playerID) continue;
                    var count = StealRules.CountStealableCards(_thief, player);
                    var button = CreateTile($"{PlayerLabel(player)}\n{count} stealable", count > 0);
                    var captured = player;
                    button.onClick.AddListener(() =>
                    {
                        _target = captured;
                        HighlightTiles(button);
                    });
                    if (_target != null && _target.playerID == player.playerID) HighlightTiles(button);
                }
            }

            private void BuildCardGrid()
            {
                var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
                scrollGo.transform.SetParent(_content, false);
                var scrollRect = scrollGo.GetComponent<RectTransform>();
                scrollRect.anchorMin = Vector2.zero;
                scrollRect.anchorMax = Vector2.one;
                scrollRect.offsetMin = Vector2.zero;
                scrollRect.offsetMax = Vector2.zero;
                scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
                viewport.transform.SetParent(scrollGo.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
                viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

                var content = new GameObject("Cards", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;

                var grid = content.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(220f, 64f);
                grid.spacing = new Vector2(10f, 10f);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 3;
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scroll = scrollGo.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = contentRect;
                scroll.horizontal = false;
                scroll.vertical = true;

                var cards = _target?.data?.currentCards;
                if (cards == null) return;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var ok = StealRules.IsStealable(_thief, _target, card, out var reason);
                    var label = ok ? card.cardName : $"{card.cardName}\n({reason})";
                    var button = CreateTile(label, ok, content.transform);
                    if (!ok) continue;
                    var captured = card;
                    button.onClick.AddListener(() =>
                    {
                        _selectedCard = captured;
                        HighlightTiles(button);
                    });
                    if (_selectedCard == card) HighlightTiles(button);
                }
            }

            private void BuildConfirmSummary()
            {
                var textGo = new GameObject("Summary", typeof(RectTransform));
                textGo.transform.SetParent(_content, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(12f, 12f);
                rect.offsetMax = new Vector2(-12f, -12f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 24f;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.text =
                    $"Target: {PlayerLabel(_target)}\nCard: {_selectedCard?.cardName}\n\nThey won't see this coming.";
            }

            private void ConfirmTarget()
            {
                if (_target == null)
                {
                    PlayerNotice.Show(_thief, "Pick someone first.");
                    return;
                }

                if (StealRules.CountStealableCards(_thief, _target) <= 0)
                {
                    PlayerNotice.Show(_thief, "That player has nothing to steal.");
                    return;
                }

                _step = Step.PickCard;
                _selectedCard = null;
                RenderStep();
            }

            private void ConfirmCard()
            {
                if (_selectedCard == null)
                {
                    PlayerNotice.Show(_thief, "Pick a card first.");
                    return;
                }

                _step = Step.Confirm;
                RenderStep();
            }

            private void ExecuteSteal()
            {
                if (_selectedCard == null || _target == null)
                {
                    Close(false);
                    return;
                }

                _awaitingResult = true;
                gameObject.SetActive(false);
                ClearContent();
                StealLedger.RequestSteal(_thief, _target, _selectedCard);
            }

            private void Cancel() => Close(false);

            private void WireButtons(string secondary, string primary, UnityEngine.Events.UnityAction secondaryAction, UnityEngine.Events.UnityAction primaryAction)
            {
                _secondaryButton.onClick.RemoveAllListeners();
                _primaryButton.onClick.RemoveAllListeners();
                _secondaryButton.GetComponentInChildren<TextMeshProUGUI>().text = secondary;
                _primaryButton.GetComponentInChildren<TextMeshProUGUI>().text = primary;
                _secondaryButton.onClick.AddListener(secondaryAction);
                _primaryButton.onClick.AddListener(primaryAction);
            }

            private Button CreateTile(string label, bool enabled, Transform parent = null)
            {
                parent ??= _content;
                var go = new GameObject("Tile", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                var image = go.GetComponent<Image>();
                image.color = enabled
                    ? new Color(0.14f, 0.20f, 0.16f, 1f)
                    : new Color(0.10f, 0.10f, 0.10f, 0.8f);
                var button = go.GetComponent<Button>();
                button.interactable = enabled;
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8f, 4f);
                rect.offsetMax = new Vector2(-8f, -4f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = 15f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = enabled ? Color.white : new Color(0.65f, 0.65f, 0.65f);
                return button;
            }

            private void HighlightTiles(Button selected)
            {
                var parent = selected.transform.parent;
                foreach (Transform child in parent)
                {
                    var img = child.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.14f, 0.20f, 0.16f, 1f);
                }

                selected.GetComponent<Image>().color = new Color(0.18f, 0.52f, 0.32f, 1f);
            }

            private void ClearContent()
            {
                if (_content == null) return;
                for (var i = _content.childCount - 1; i >= 0; i--)
                {
                    Destroy(_content.GetChild(i).gameObject);
                }

                var layout = _content.GetComponent<GridLayoutGroup>();
                if (layout != null) Destroy(layout);
            }

            private static string PlayerLabel(Player player)
            {
                var name = player?.data?.view?.Owner?.NickName;
                return string.IsNullOrEmpty(name) ? "Player " + (player.playerID + 1) : name;
            }

            private static RectTransform CreatePanel(Transform parent, Vector2 size)
            {
                var go = new GameObject("Panel", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
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

            private static TextMeshProUGUI CreateHeader(string name, Transform parent, float size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(24f, -20f);
                rect.sizeDelta = new Vector2(-48f, 36f);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = size;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = Color.white;
                return tmp;
            }

            private static Button CreateFooterButton(string name, Transform parent, Vector2 anchor, string label)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = new Vector2(0f, 0f);
                rect.sizeDelta = new Vector2(280f, 52f);
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
