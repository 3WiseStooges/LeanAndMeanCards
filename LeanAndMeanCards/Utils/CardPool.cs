using System.Reflection;
using HarmonyLib;

namespace LeanAndMeanCards.Utils
{
    internal static class CardPool
    {
        private static MethodInfo _isCardActive;

        internal static bool IsActive(CardInfo card)
        {
            if (card == null) return false;

            try
            {
                _isCardActive ??= AccessTools.Method(
                    AccessTools.TypeByName("UnboundLib.Utils.CardManager"),
                    "IsCardActive");
                if (_isCardActive == null) return true;

                object arg = card;
                var parms = _isCardActive.GetParameters();
                if (parms.Length == 1 && parms[0].ParameterType == typeof(string))
                {
                    arg = card.gameObject != null
                        ? CardEncoding.StripClone(card.gameObject.name)
                        : card.cardName;
                }

                if (_isCardActive.Invoke(null, new[] { arg }) is bool active) return active;
            }
            catch
            {
                // treat unknown cards as enabled
            }

            return true;
        }
    }
}
