using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Vanilla explosions (Timed Detonation, Explosive, etc.) and NetworkPhysicsObject
    /// BulletPush apply rigidbody impulses to anything in the radius. On the host that
    /// includes live bouncing bullets, which kills their vertical speed and looks like
    /// Drop Grenade. Dynamite already skipped its own blast; this covers the rest.
    ///
    /// Only Impulse is filtered. MoveTransform still applies its own gravity each frame.
    /// </summary>
    [HarmonyPatch(typeof(NetworkPhysicsObject), "BulletPush")]
    internal static class NpoBulletPushSkipProjectilesPatch
    {
        private static bool Prefix(NetworkPhysicsObject __instance)
        {
            return !DynamiteBlast.IsProjectileBody(__instance != null ? __instance.transform : null);
        }
    }

    [HarmonyPatch]
    internal static class NpoSendForceSkipProjectilesPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(NetworkPhysicsObject), "RPCA_SendForce");

        private static bool Prefix(NetworkPhysicsObject __instance)
        {
            return !DynamiteBlast.IsProjectileBody(__instance != null ? __instance.transform : null);
        }
    }

    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddForce), typeof(Vector2), typeof(ForceMode2D))]
    internal static class RbImpulseSkipProjectilesPatch
    {
        private static bool Prefix(Rigidbody2D __instance, ForceMode2D mode)
        {
            if (mode != ForceMode2D.Impulse) return true;
            return !DynamiteBlast.IsProjectileBody(__instance != null ? __instance.transform : null);
        }
    }

    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddForceAtPosition), typeof(Vector2), typeof(Vector2), typeof(ForceMode2D))]
    internal static class RbImpulseAtPositionSkipProjectilesPatch
    {
        private static bool Prefix(Rigidbody2D __instance, ForceMode2D mode)
        {
            if (mode != ForceMode2D.Impulse) return true;
            return !DynamiteBlast.IsProjectileBody(__instance != null ? __instance.transform : null);
        }
    }
}
