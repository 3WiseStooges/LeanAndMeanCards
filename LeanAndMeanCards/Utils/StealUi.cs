using System.Collections.Generic;
using TMPro;
using UnboundLib;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal static class StealUi
    {
        private static StealOverlay _overlay;

        internal static bool IsOpen => _overlay != null && _overlay.gameObject.activeSelf;

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

            var go = new GameObject("LAMC_StealUi", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _overlay = go.AddComponent<StealOverlay>();
            _overlay.Build();
            go.SetActive(false);
        }

        private class StealOverlay : MonoBehaviour
        {
            private enum Step { PickTarget, PickCard, Confirm }

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
            private bool _holdingPick;

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

                var panel = CreatePanel(transform, new Vector2(560f, 460f));
                UiGfx.Round(panel.gameObject.AddComponent<Image>(), UiGfx.Panel);
                UiGfx.AddAccentBar(panel);

                var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 18, 16);
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                _title = CreateHeader("Title", panel, UiGfx.TitleFont, 26f);
                _title.color = UiGfx.Title;
                _subtitle = CreateHeader("Subtitle", panel, UiGfx.HintFont, 36f);
                _subtitle.fontStyle = FontStyles.Normal;
                _subtitle.color = UiGfx.Hint;

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(panel, false);
                _content = contentGo.transform;
                var contentLe = contentGo.AddComponent<LayoutElement>();
                contentLe.minHeight = 240f;
                contentLe.flexibleHeight = 1f;
                contentLe.preferredHeight = 300f;

                var row = new GameObject("Buttons", typeof(RectTransform));
                row.transform.SetParent(panel, false);
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

                _secondaryButton = CreateRowButton("Secondary", row.transform, "Cancel");
                _primaryButton = CreateRowButton("Primary", row.transform, "Next");
            }

            internal void Open(Player thief)
            {
                _thief = thief;
                _target = null;
                _selectedCard = null;
                _step = Step.PickTarget;
                _completedSteal = false;
                _awaitingResult = false;
                if (!_holdingPick)
                {
                    PickUiHold.Push();
                    _holdingPick = true;
                }

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
                ReleaseHold();

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

            private void ReleaseHold()
            {
                if (!_holdingPick) return;
                _holdingPick = false;
                PickUiHold.Pop();
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
                        _subtitle.text = "Only you can choose. Pick who to rob.";
                        BuildTargetGrid();
                        WireButtons("Cancel", "Next", Cancel, ConfirmTarget);
                        break;
                    case Step.PickCard:
                        _title.text = "Thief";
                        _subtitle.text = $"Pick a card from {PlayerLabels.For(_target)}.";
                        BuildCardGrid();
                        WireButtons("Back", "Next", () => { _step = Step.PickTarget; RenderStep(); }, ConfirmCard);
                        break;
                    case Step.Confirm:
                        _title.text = "Thief";
                        _subtitle.text =
                            $"Steal {_selectedCard?.cardName ?? "card"} from {PlayerLabels.For(_target)}?";
                        BuildConfirmSummary();
                        WireButtons("Back", "Steal", () => { _step = Step.PickCard; RenderStep(); }, ExecuteSteal);
                        break;
                }
            }

            private void BuildTargetGrid()
            {
                var layout = _content.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(248f, 52f);
                layout.spacing = new Vector2(10f, 10f);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 2;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.padding = new RectOffset(4, 4, 4, 4);

                foreach (var player in PlayerManager.instance.players)
                {
                    if (player == null || player.playerID == _thief.playerID) continue;
                    var count = StealRules.CountStealableCards(_thief, player);
                    var button = CreateTile($"{PlayerLabels.For(player)}\n{count} stealable", count > 0);
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
                var scrollBg = scrollGo.GetComponent<Image>();
                scrollBg.color = new Color(1f, 1f, 1f, 0f);
                scrollBg.raycastTarget = false;

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
                viewport.transform.SetParent(scrollGo.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
                viewport.GetComponent<Mask>().showMaskGraphic = false;
                var viewportImage = viewport.GetComponent<Image>();
                viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
                viewportImage.raycastTarget = true;

                var content = new GameObject("Cards", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;

                var grid = content.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(248f, 48f);
                grid.spacing = new Vector2(10f, 10f);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
                grid.padding = new RectOffset(4, 4, 4, 4);
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scroll = scrollGo.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = contentRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 28f;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                var cards = _target?.data?.currentCards;
                if (cards == null) return;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var ok = StealRules.IsStealable(_thief, _target, card, out var reason);
                    var label = ok ? card.cardName : $"{card.cardName} ({reason})";
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
                UiGfx.StyleTmp(tmp, UiGfx.TitleFont, UiGfx.Title, TextAlignmentOptions.TopLeft);
                tmp.text =
                    $"Target: {PlayerLabels.For(_target)}\nCard: {_selectedCard?.cardName}";
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
                UiGfx.Round(
                    go.GetComponent<Image>(),
                    enabled ? UiGfx.Tile : UiGfx.TileDisabled,
                    raycast: enabled);
                var button = go.GetComponent<Button>();
                button.interactable = enabled;
                UiGfx.GhostColors(button);
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10f, 6f);
                rect.offsetMax = new Vector2(-10f, -6f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                UiGfx.StyleTmp(tmp, UiGfx.TileFont, enabled ? UiGfx.Label : new Color(0.65f, 0.67f, 0.70f, 0.7f), TextAlignmentOptions.Center);
                return button;
            }

            private void HighlightTiles(Button selected)
            {
                var parent = selected.transform.parent;
                foreach (Transform child in parent)
                {
                    var img = child.GetComponent<Image>();
                    var btn = child.GetComponent<Button>();
                    if (img == null) continue;
                    img.color = btn != null && btn.interactable ? UiGfx.Tile : UiGfx.TileDisabled;
                }

                selected.GetComponent<Image>().color = UiGfx.TileSelected;
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

            private static TextMeshProUGUI CreateHeader(string name, Transform parent, float size, float height)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.AddComponent<LayoutElement>().preferredHeight = height;
                var tmp = go.AddComponent<TextMeshProUGUI>();
                UiGfx.StyleTmp(tmp, size, UiGfx.Title, TextAlignmentOptions.TopLeft);
                tmp.fontStyle = FontStyles.Bold;
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
                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                UiGfx.StyleTmp(tmp, UiGfx.ButtonFont, UiGfx.Label, TextAlignmentOptions.Center);
                return button;
            }
        }
    }
}
