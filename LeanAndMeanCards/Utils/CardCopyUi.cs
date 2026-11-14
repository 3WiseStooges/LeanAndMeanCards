using System.Collections.Generic;
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
    /// The chooser for Duplicate and Clone, opened on the owner's client only, after the
    /// whole pick phase is over (see CardCopyManager's pick-end hook).
    ///
    /// Laid out with explicit anchored rects rather than nested layout groups. The first
    /// version put a ScrollRect inside a VerticalLayoutGroup, which drove the scroll rect's
    /// own transform and spilled the card rows over the buttons.
    ///
    /// Only copyable cards are listed. Showing the rest greyed out just made the list long
    /// and asked the reader to work out why half of it was dead; a single line under the
    /// list says how many were left out and why.
    /// </summary>
    internal static class CardCopyUi
    {
        private static CopyOverlay _overlay;
        private static WaitBanner _banner;

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

        /// <summary>Seconds left, shown in the chooser's header pill.</summary>
        internal static void SetCountdown(float seconds)
        {
            if (_overlay != null) _overlay.SetCountdown(seconds);
        }

        /// <summary>
        /// Genie's "waiting for players" label: what everyone who is not choosing sees
        /// while the pick-end hook holds the round.
        /// </summary>
        internal static void ShowWaiting(string message, float seconds)
        {
            EnsureBanner();
            _banner?.Show(message, seconds);
        }

        internal static void HideWaiting()
        {
            if (_banner != null) _banner.Hide();
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

        private static void EnsureBanner()
        {
            if (_banner != null) return;
            var canvas = Unbound.Instance?.canvas;
            if (canvas == null) return;

            var go = new GameObject("LAMC_CardCopyWait", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _banner = go.AddComponent<WaitBanner>();
            _banner.Build();
            go.SetActive(false);
        }

        private sealed class WaitBanner : MonoBehaviour
        {
            private TextMeshProUGUI _label;

            internal void Build()
            {
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -70f);
                rect.sizeDelta = new Vector2(620f, 44f);

                var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(transform, false);
                Stretch(bg.GetComponent<RectTransform>());
                UiGfx.Round(bg.GetComponent<Image>(), new Color(0.07f, 0.08f, 0.10f, 0.92f));

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(transform, false);
                var tr = textGo.GetComponent<RectTransform>();
                Stretch(tr);
                tr.offsetMin = new Vector2(16f, 0f);
                tr.offsetMax = new Vector2(-16f, 0f);
                _label = textGo.AddComponent<TextMeshProUGUI>();
                UiGfx.StyleTmp(_label, 16f, UiGfx.Hint, TextAlignmentOptions.Center, wrap: false);
            }

            internal void Show(string message, float seconds)
            {
                if (_label == null) return;
                gameObject.SetActive(true);
                _label.text = $"{message}   <color=#8EBAFF>{Mathf.CeilToInt(Mathf.Max(0f, seconds))}s</color>";
            }

            internal void Hide() => gameObject.SetActive(false);
        }

        private sealed class CopyOverlay : MonoBehaviour
        {
            private const float PanelW = 640f;
            private const float PanelH = 540f;
            private const float Pad = 22f;
            private const float RowH = 46f;

            private TextMeshProUGUI _title;
            private TextMeshProUGUI _subtitle;
            private TextMeshProUGUI _countdown;
            private TextMeshProUGUI _footnote;
            private RectTransform _listContent;
            private Button _confirm;
            private TextMeshProUGUI _confirmLabel;
            private Player _owner;
            private CardCopyKind _kind;
            private CardInfo _selected;
            private readonly List<Button> _rows = new List<Button>();
            private readonly List<CardInfo> _rowCards = new List<CardInfo>();

            internal void Build()
            {
                Stretch(gameObject.GetComponent<RectTransform>());

                var dim = NewImage("Dim", transform);
                Stretch(dim.rectTransform);
                UiGfx.Solid(dim, UiGfx.Dim, raycast: true);

                var panel = NewRect("Panel", transform);
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0.5f);
                panel.sizeDelta = new Vector2(PanelW, PanelH);
                UiGfx.Round(panel.gameObject.AddComponent<Image>(), UiGfx.Panel);
                UiGfx.AddAccentBar(panel);

                _title = NewText("Title", panel, 22f, UiGfx.Title, TextAlignmentOptions.Left);
                Place(_title.rectTransform, Pad, -Pad, PanelW - Pad * 2f - 110f, 30f);
                _title.fontStyle = FontStyles.Bold;

                _countdown = NewText("Countdown", panel, 20f, new Color(0.56f, 0.73f, 1f), TextAlignmentOptions.Right);
                Place(_countdown.rectTransform, PanelW - Pad - 110f, -Pad, 110f, 30f);
                _countdown.fontStyle = FontStyles.Bold;

                _subtitle = NewText("Subtitle", panel, 14f, UiGfx.Hint, TextAlignmentOptions.TopLeft);
                Place(_subtitle.rectTransform, Pad, -(Pad + 32f), PanelW - Pad * 2f, 40f);

                var rule = NewImage("Rule", panel);
                Place(rule.rectTransform, Pad, -(Pad + 76f), PanelW - Pad * 2f, 1f);
                UiGfx.Solid(rule, new Color(1f, 1f, 1f, 0.10f));

                BuildList(panel);

                _footnote = NewText("Footnote", panel, 12.5f, new Color(0.62f, 0.65f, 0.70f, 0.95f), TextAlignmentOptions.Left);
                Place(_footnote.rectTransform, Pad, -(PanelH - 92f), PanelW - Pad * 2f, 22f);

                var later = NewButton("Later", panel, "Later", new Color(1f, 1f, 1f, 0.06f));
                Place(later.GetComponent<RectTransform>(), PanelW - Pad - 300f, -(PanelH - 60f), 140f, 38f);
                later.onClick.AddListener(Close);

                _confirm = NewButton("Confirm", panel, "Copy it", new Color(0.18f, 0.34f, 0.60f, 0.95f));
                Place(_confirm.GetComponent<RectTransform>(), PanelW - Pad - 150f, -(PanelH - 60f), 150f, 38f);
                _confirmLabel = _confirm.GetComponentInChildren<TextMeshProUGUI>();
                _confirm.onClick.AddListener(Confirm);
            }

            private void BuildList(RectTransform panel)
            {
                var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
                scrollGo.transform.SetParent(panel, false);
                var scrollRect = scrollGo.GetComponent<RectTransform>();
                // Explicit rect: between the rule and the footnote. Nothing drives this but us.
                Place(scrollRect, Pad, -(Pad + 86f), PanelW - Pad * 2f, PanelH - Pad - 86f - 104f);

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
                viewport.transform.SetParent(scrollGo.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                Stretch(viewportRect);
                var viewportImage = viewport.GetComponent<Image>();
                viewportImage.color = new Color(1f, 1f, 1f, 0.012f);
                viewportImage.raycastTarget = true;

                var content = new GameObject("Rows", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                _listContent = content.GetComponent<RectTransform>();
                _listContent.anchorMin = new Vector2(0f, 1f);
                _listContent.anchorMax = new Vector2(1f, 1f);
                _listContent.pivot = new Vector2(0.5f, 1f);
                _listContent.anchoredPosition = Vector2.zero;
                _listContent.sizeDelta = new Vector2(0f, 0f);

                var layout = content.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 6f;
                layout.padding = new RectOffset(4, 10, 4, 4);
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scroll = scrollGo.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = _listContent;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 30f;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }

            internal void Open(Player owner, CardCopyKind kind)
            {
                _owner = owner;
                _kind = kind;
                _selected = null;
                gameObject.SetActive(true);
                Render();
            }

            internal void Close()
            {
                gameObject.SetActive(false);
                ClearRows();
                _selected = null;
            }

            internal void SetCountdown(float seconds)
            {
                if (_countdown == null) return;
                var left = Mathf.Max(0f, seconds);
                _countdown.text = $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";
                _countdown.color = left <= 15f
                    ? new Color(1f, 0.52f, 0.45f)
                    : new Color(0.56f, 0.73f, 1f);
            }

            private void Render()
            {
                var isClone = _kind == CardCopyKind.Clone;
                _title.text = isClone ? Cards.Clone.Title : Cards.Duplicate.Title;
                _subtitle.text = isClone
                    ? "Pick a card you own. You get a copy now, and another at the end of every round."
                    : "Pick a card you own. A second copy of it goes into your hand.";
                _confirmLabel.text = isClone ? "Clone it" : "Copy it";
                UpdateConfirm();
                BuildRows();
            }

            private void BuildRows()
            {
                ClearRows();

                var cards = _owner?.data?.currentCards;
                if (cards == null) return;

                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                var hidden = 0;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var key = card.cardName ?? card.GetInstanceID().ToString();
                    if (!seen.Add(key)) continue;

                    if (!CardCopyRules.IsCopyable(_owner, card, out _))
                    {
                        hidden++;
                        continue;
                    }

                    AddRow(card);
                }

                _footnote.text = hidden <= 0
                    ? ""
                    : hidden == 1
                        ? "1 card in your hand can't be copied (curse, Unique, or a one-use card)."
                        : $"{hidden} cards in your hand can't be copied (curses, Unique, or one-use cards).";
            }

            private void AddRow(CardInfo card)
            {
                var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_listContent, false);
                go.AddComponent<LayoutElement>().preferredHeight = RowH;
                UiGfx.Round(go.GetComponent<Image>(), UiGfx.Tile, raycast: true);
                var button = go.GetComponent<Button>();
                UiGfx.GhostColors(button);

                // Rarity reads as a colour chip, not as the label colour. A common card's
                // grey name looked disabled next to a rare card's blue one.
                var chip = NewImage("Rarity", go.transform);
                Place(chip.rectTransform, 12f, -(RowH - 20f) / 2f, 5f, 20f);
                UiGfx.Round(chip, RarityHelper.GetRarityColor(card));

                var name = NewText("Name", go.transform, 15f, UiGfx.Label, TextAlignmentOptions.Left);
                Place(name.rectTransform, 28f, -(RowH - 22f) / 2f, 380f, 22f);
                name.text = card.cardName;

                var held = CardOwnership.CountOf(_owner, card);
                if (held > 1)
                {
                    var count = NewText("Count", go.transform, 13f, new Color(0.66f, 0.70f, 0.76f), TextAlignmentOptions.Right);
                    Place(count.rectTransform, PanelW - Pad * 2f - 120f, -(RowH - 22f) / 2f, 90f, 22f);
                    count.text = $"you have {held}";
                }

                var captured = card;
                button.onClick.AddListener(() =>
                {
                    _selected = captured;
                    Highlight();
                    UpdateConfirm();
                });

                _rows.Add(button);
                _rowCards.Add(card);
            }

            private void Highlight()
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    if (row == null) continue;

                    var isSelected = i < _rowCards.Count && _rowCards[i] == _selected;
                    var img = row.GetComponent<Image>();
                    if (img != null) img.color = isSelected ? UiGfx.TileSelected : UiGfx.Tile;

                    var name = row.transform.Find("Name");
                    var tmp = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
                    if (tmp != null) tmp.color = isSelected ? UiGfx.Title : UiGfx.Label;
                }
            }

            private void UpdateConfirm()
            {
                if (_confirm == null) return;
                var ready = _selected != null;
                _confirm.interactable = ready;
                var img = _confirm.GetComponent<Image>();
                if (img != null)
                {
                    img.color = ready
                        ? new Color(0.18f, 0.34f, 0.60f, 0.95f)
                        : new Color(1f, 1f, 1f, 0.05f);
                }
            }

            private void Confirm()
            {
                if (_selected == null) return;

                var card = _selected;
                var owner = _owner;
                Close();
                CardCopyManager.Request(owner, card, _kind);
            }

            private void ClearRows()
            {
                _rows.Clear();
                _rowCards.Clear();
                if (_listContent == null) return;
                for (var i = _listContent.childCount - 1; i >= 0; i--)
                {
                    Destroy(_listContent.GetChild(i).gameObject);
                }
            }
        }

        // ------------------------------------------------------------- tiny helpers

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Top-left anchored placement, in panel pixels. y is negative downward.</summary>
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            UiGfx.StyleTmp(tmp, size, color, align);
            return tmp;
        }

        private static Button NewButton(string name, Transform parent, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            UiGfx.Round(go.GetComponent<Image>(), color, raycast: true);
            var button = go.GetComponent<Button>();
            UiGfx.GhostColors(button);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            UiGfx.StyleTmp(tmp, 14f, UiGfx.Label, TextAlignmentOptions.Center);
            return button;
        }
    }
}
