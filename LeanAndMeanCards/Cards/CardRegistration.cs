using LeanAndMeanCards.Utils;
using UnboundLib.Cards;

namespace LeanAndMeanCards.Cards
{
    internal static class CardRegistration
    {
        internal static void RegisterAll()
        {
            CustomCard.BuildCard<Thief>(info => Thief.Card = info);
            CustomCard.BuildCard<Takebacksies>(info => Takebacksies.Card = info);
            TakebacksiesInjector.Register();
            CustomCard.BuildCard<SandbagSimulator>(info => SandbagSimulator.Card = info);
            CustomCard.BuildCard<JarOfDirt>(info => JarOfDirt.Card = info);
            CustomCard.BuildCard<Confetti>(info => Confetti.Card = info);
            CustomCard.BuildCard<Shove>(info => Shove.Card = info);
            CustomCard.BuildCard<Pisser>(info => Pisser.Card = info);
            CustomCard.BuildCard<Doorstop>(info => Doorstop.Card = info);
            CustomCard.BuildCard<BozoShoes>(info => BozoShoes.Card = info);
            CustomCard.BuildCard<DraftSniper>(info => DraftSniper.Card = info);
            CustomCard.BuildCard<YeetCannon>(info => YeetCannon.Card = info);
            CustomCard.BuildCard<Dynamite>(info => Dynamite.Card = info);
            CustomCard.BuildCard<TaserTaserTaser>(info => TaserTaserTaser.Card = info);
            CustomCard.BuildCard<SafetyNet>(info => SafetyNet.Card = info);
        }
    }
}
