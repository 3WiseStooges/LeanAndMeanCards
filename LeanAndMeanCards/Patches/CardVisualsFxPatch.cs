using System;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Kills vanilla CardVisuals particle glow for this pack's sticker art.
    /// Vanilla re-enables `part` after Start, so a LateUpdate guard keeps it off.
    /// </summary>
    [HarmonyPatch(typeof(CardVisuals))]
    internal static class CardVisualsFxPatch
    {
        private static readonly FieldInfo PartField = AccessTools.Field(typeof(CardVisuals), "part");
        private static readonly FieldInfo SelectedColorField = AccessTools.Field(typeof(CardVisuals), "selectedColor");
        private static readonly FieldInfo DefaultColorField = AccessTools.Field(typeof(CardVisuals), "defaultColor");
        private static readonly FieldInfo IsSelectedField = AccessTools.Field(typeof(CardVisuals), "isSelected");

        private sealed class GlowBaseline : MonoBehaviour
        {
            public float Rate;
            public float Saturation;
            public bool Captured;
        }

        private sealed class GlowGuard : MonoBehaviour
        {
            private CardVisuals _visuals;

            private void Awake() => _visuals = GetComponent<CardVisuals>();

            private void LateUpdate()
            {
                if (_visuals == null) return;
                try
                {
                    Apply(_visuals, keepParticlesDown: true, stampMinis: false);
                }
                catch
                {
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void AfterStart(CardVisuals __instance)
        {
            if (__instance == null) return;
            try
            {
                Apply(__instance, keepParticlesDown: true, stampMinis: true);
                if (IsMmVisual(__instance) && __instance.GetComponent<GlowGuard>() == null)
                    __instance.gameObject.AddComponent<GlowGuard>();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"CardVisualsFx Start skipped: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("ChangeSelected")]
        private static void AfterChangeSelected(CardVisuals __instance)
        {
            if (__instance == null) return;
            try
            {
                Apply(__instance, keepParticlesDown: true, stampMinis: true);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"CardVisualsFx ChangeSelected skipped: {ex.Message}");
            }
        }

        internal static void Apply(CardVisuals visuals, bool keepParticlesDown, bool stampMinis = false)
        {
            if (visuals == null) return;
            var fx = FindFx(visuals);
            if (fx == null && !IsMmVisual(visuals)) return;

            var glow = fx != null ? CardArtFx.GlowScaleFor(fx) : 0f;
            var isSelected = IsSelectedField != null && (bool)IsSelectedField.GetValue(visuals);
            var part = PartField?.GetValue(visuals) as GeneralParticleSystem;

            if (part != null)
            {
                var baseline = part.GetComponent<GlowBaseline>()
                               ?? part.gameObject.AddComponent<GlowBaseline>();
                if (!baseline.Captured)
                {
                    baseline.Rate = part.rate;
                    baseline.Saturation = part.saturationMultiplier;
                    baseline.Captured = true;
                }

                if (glow <= 0.001f)
                {
                    KillParticle(part);
                    if (part.gameObject != visuals.gameObject)
                        part.gameObject.SetActive(false);
                }
                else
                {
                    part.enabled = true;
                    part.saturationMultiplier = baseline.Saturation * (0.12f * glow);
                    part.rate = Mathf.Max(0.01f, baseline.Rate * (0.04f + glow * 0.08f));
                    part.simulationSpeedMultiplier = isSelected
                        ? Mathf.Min(part.simulationSpeedMultiplier, 0.2f + glow * 0.15f)
                        : Mathf.Min(part.simulationSpeedMultiplier, 0.08f);

                    if (part.particleSettings != null)
                    {
                        var c = part.particleSettings.randomColor;
                        c = Color.Lerp(c, Color.white, 0.05f);
                        c.a = Mathf.Clamp01(c.a * (0.1f + glow * 0.15f));
                        part.particleSettings.randomColor = c;
                    }
                }
            }

            if (keepParticlesDown && glow <= 0.001f)
            {
                KillAllParticles(visuals.transform);
                var info = CardInfoOf(visuals);
                if (info != null) KillAllParticles(info.transform);
            }

            LockArtColors(visuals);
            if (stampMinis) CardArtFactory.TryAssignSprite(CardInfoOf(visuals));

            var moving = visuals.GetComponentInChildren<MmMovingCardBackground>(true);
            if (moving == null || fx == null) return;

            if (!fx.MovingBackground)
            {
                moving.enabled = false;
                return;
            }

            var selected = SelectedColorField != null
                ? (Color)SelectedColorField.GetValue(visuals)
                : Color.white;
            var def = DefaultColorField != null
                ? (Color)DefaultColorField.GetValue(visuals)
                : selected;
            var tint = isSelected ? selected : def;
            if (tint.maxColorComponent < 0.05f) tint = selected;
            var max = Mathf.Max(tint.r, tint.g, tint.b);
            if (max > 0.4f) tint *= 0.4f / max;
            moving.SetTint(tint);
            moving.enabled = true;
        }

        private static bool IsMmVisual(CardVisuals visuals)
        {
            return CardBarMiniIcons.IsMmCard(CardInfoOf(visuals));
        }

        private static CardInfo CardInfoOf(CardVisuals visuals)
        {
            return visuals.GetComponentInParent<CardInfo>() ?? visuals.GetComponent<CardInfo>();
        }

        private static void LockArtColors(CardVisuals visuals)
        {
            var info = CardInfoOf(visuals);
            var root = info != null ? info.transform : visuals.transform;
            foreach (var tag in root.GetComponentsInChildren<MmCardArtTag>(true))
            {
                var img = tag.GetComponent<Image>();
                if (img != null) img.color = MmArtColorLock.ArtColor;
                if (tag.GetComponent<MmArtColorLock>() == null)
                    tag.gameObject.AddComponent<MmArtColorLock>();
            }

            foreach (var colorLock in root.GetComponentsInChildren<MmArtColorLock>(true))
            {
                var img = colorLock.GetComponent<Image>();
                if (img != null) img.color = MmArtColorLock.ArtColor;
                var sr = colorLock.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = MmArtColorLock.ArtColor;
            }
        }

        internal static void KillAllParticles(Transform root)
        {
            if (root == null) return;
            foreach (var gps in root.GetComponentsInChildren<GeneralParticleSystem>(true))
                KillParticle(gps);

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var em = ps.emission;
                em.enabled = false;
                ps.Clear(true);
            }
        }

        private static void KillParticle(GeneralParticleSystem part)
        {
            if (part == null) return;
            part.saturationMultiplier = 0f;
            part.rate = 0f;
            part.simulationSpeedMultiplier = 0f;
            part.enabled = false;
            if (part.particleSettings != null)
            {
                var off = part.particleSettings.randomColor;
                off.a = 0f;
                part.particleSettings.randomColor = off;
            }
        }

        private static MmCardArtFxTag FindFx(CardVisuals visuals)
        {
            var fx = visuals.GetComponentInChildren<MmCardArtFxTag>(true);
            if (fx != null) return fx;

            var info = visuals.GetComponentInParent<CardInfo>();
            if (info == null) info = visuals.GetComponent<CardInfo>();
            if (info?.cardArt == null) return null;

            fx = info.cardArt.GetComponent<MmCardArtFxTag>();
            if (fx != null) return fx;

            if (info.cardArt.GetComponent<MmCardArtTag>() != null)
            {
                fx = info.cardArt.AddComponent<MmCardArtFxTag>();
                fx.Motion = MmCardArtMotion.None;
                fx.MovingBackground = false;
                fx.GlowScale = 0f;
            }

            return fx;
        }
    }
}
