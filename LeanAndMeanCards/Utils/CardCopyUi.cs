using TMPro;
using UnboundLib;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal enum CardCopyKind
    {
        Duplicate,
        Clone
    }

    /// <summary>
    /// One-step chooser over the player's own hand, opened on the owner's client only.
    /// Holds the pick for the whole lobby while it is up (see <see cref="PickUiHold"/>),
    /// the same way Thief and Sandbag do, so nobody drops into the round under an overlay.
    /// </summary>
    internal static class CardCopyUi
    {
        private static CopyOverlay _overlay;

        internal static bool IsOpen => _overlay != null && _overlay.gameObject.activeSelf;

        internal static bool TryOpen(Player owner, CardCopyKind kind)
        {
            if (owner == null) return false;
            if (IsOpen) return false;
            if (StealUi.IsOpen || CardTargetUi.IsOpen) return false;
            if (CardCopyRules.CountCopyable(owner) <= 0) return false;

            EnsureOverlay();
            if (_overlay == null) return false;

            _overlay.Open(owner, kind);
            return true;
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

            var go = new GameObject("LAMC_CardCopyUi", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _overlay = go.AddComponent<CopyOverlay>();
            _overlay.Build();
            go.SetActive(false);
        }

        private class CopyOverlay : MonoBehaviour
        {
            private TextMeshProUGUI _title;
            private TextMeshProUGUI _subtitle;
            private Transform _content;
            private Button _primaryButton;
            private Button _secondaryButton;
            private Player _owner;
            private CardCopyKind _kind;
            private CardInfo _selected;
            private bool _holdingPick;

            internal void Build()
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                UiGfx.Solid(CreateImage("Dim", transform), UiGfx.Dim, raycast: true);

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

                _secondaryButton = CreateRowButton("Secondary", row.transform, "Later");
                _primaryButton = CreateRowButton("Primary", row.transform, "Copy");
            }

            internal void Open(Player owner, CardCopyKind kind)
            {
                _owner = owner;
                _kind = kind;
                _selected = null;

                if (!_holdingPick)
                {
                    PickUiHold.Push();
                    _holdingPick = true;
                }

                gameObject.SetActive(true);
                Render();
            }

            internal void Close()
            {
                gameObject.SetActive(false);
                ClearContent();
                ReleaseHold();
                _selected = null;
            }

            private void ReleaseHold()
            {
                if (!_holdingPick) return;
                _holdingPick = false;
                PickUiHold.Pop();
            }

            private void Render()
            {
                ClearContent();

                var isClone = _kind == CardCopyKind.Clone;
                _title.text = isClone ? Cards.Clone.Title : Cards.Duplicate.Title;
                _title.color = UiGfx.Title;
                _subtitle.text = isClone
                    ? "Pick a card you own. Another copy of it lands at the end of every round from now on."
                    : "Pick a card you own. A second copy of it lands right now.";

                BuildCardList();
                WireButtons(isClone ? "Clone it" : "Copy it");
            }

            private void BuildCardList()
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

                var cardsGo = new GameObject("Cards", typeof(RectTransform));
                cardsGo.transform.SetParent(viewport.transform, false);
                var cardsRect = cardsGo.GetComponent<RectTransform>();
                cardsRect.anchorMin = new Vector2(0f, 1f);
                cardsRect.anchorMax = new Vector2(1f, 1f);
                cardsRect.pivot = new Vector2(0.5f, 1f);
                cardsRect.anchoredPosition = Vector2.zero;

                var grid = cardsGo.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(248f, 48f);
                grid.spacing = new Vector2(10f, 10f);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
                grid.padding = new RectOffset(4, 4, 4, 4);
                cardsGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scroll = scrollGo.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = cardsRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 28f;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                var cards = _owner?.data?.currentCards;
                if (cards == null) return;

                var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    // One tile per distinct card, with a count. A hand of six Ammos
                    // should not fill the list with six identical rows.
                    var key = card.cardName ?? card.GetInstanceID().ToString();
                    if (!seen.Add(key)) continue;

                    var ok = CardCopyRules.IsCopyable(_owner, card, out var reason);
                    var held = CardOwnership.CountOf(_owner, card);
                    var suffix = ok
                        ? (held > 1 ? "\nyou have " + held : "")
                        : "\n" + reason;
                    var button = CreateTile(
                        card.cardName + suffix,
                        ok,
                        cardsGo.transform,
                        ok ? RarityHelper.GetRarityColor(card) : (Color?)null);
                    if (!ok) continue;

                    var captured = card;
                    button.onClick.AddListener(() =>
                    {
                        _selected = captured;
                        HighlightTiles(button);
                    });
                    if (_selected == card) HighlightTiles(button);
                }
            }

            private void WireButtons(string primaryLabel)
            {
                _secondaryButton.onClick.RemoveAllListeners();
                _primaryButton.onClick.RemoveAllListeners();
                _secondaryButton.GetComponentInChildren<TextMeshProUGUI>().text = "Later";
                _primaryButton.GetComponentInChildren<TextMeshProUGUI>().text = primaryLabel;
                // "Later" loses nothing: the card stays unspent, so the chooser comes back
                // at the next pick. There is no way to waste the effect by closing this.
                _secondaryButton.onClick.AddListener(Close);
                _primaryButton.onClick.AddListener(Confirm);
            }

            private void Confirm()
            {
                if (_selected == null)
                {
                    PlayerNotice.Show(_owner, "Pick a card first.");
                    return;
                }

                var card = _selected;
                var owner = _owner;
                var kind = _kind;
                Close();
                CardCopyManager.Request(owner, card, kind);
            }

            private Button CreateTile(string label, bool enabled, Transform parent, Color? labelColor)
            {
                var go = new GameObject("Tile", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                UiGfx.Round(go.GetComponent<Image>(), enabled ? UiGfx.Tile : UiGfx.TileDisabled, raycast: enabled);
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
                var color = enabled
                    ? (labelColor ?? UiGfx.Label)
                    : new Color(0.65f, 0.67f, 0.70f, 0.7f);
                UiGfx.StyleTmp(tmp, UiGfx.TileFont, color, TextAlignmentOptions.Center);
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

            private static Image CreateImage(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
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
