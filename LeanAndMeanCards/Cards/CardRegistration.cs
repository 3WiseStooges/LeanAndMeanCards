using System;
using LeanAndMeanCards.Utils;
using UnboundLib.Cards;

namespace LeanAndMeanCards.Cards
{
    internal static class CardRegistration
    {
        internal static void RegisterAll()
        {
            Bind<Thief>(info => Thief.Card = info);
            Bind<Takebacksies>(info => Takebacksies.Card = info);
            TakebacksiesInjector.Register();
            Bind<SandbagSimulator>(info => SandbagSimulator.Card = info);
            Bind<JarOfDirt>(info => JarOfDirt.Card = info);
            Bind<Confetti>(info => Confetti.Card = info);
            Bind<Shove>(info => Shove.Card = info);
            Bind<Pisser>(info => Pisser.Card = info);
            Bind<Doorstop>(info => Doorstop.Card = info);
            Bind<BozoShoes>(info => BozoShoes.Card = info);
            Bind<DraftSniper>(info => DraftSniper.Card = info);
            Bind<SilverEgg>(info => SilverEgg.Card = info);
            Bind<YeetCannon>(info => YeetCannon.Card = info);
            Bind<Dynamite>(info => Dynamite.Card = info);
            Bind<TaserTaserTaser>(info => TaserTaserTaser.Card = info);
            Bind<SafetyNet>(info => SafetyNet.Card = info);
        }

        private static void Bind<T>(Action<CardInfo> setStatic) where T : CustomCard
        {
            CustomCard.BuildCard<T>(info =>
            {
                setStatic(info);
                // Unbound sets cardArt and cardName after SetupCard. The callback
                // is the first moment both exist on the prefab.
                CardArtFactory.TryAssignSprite(info);
            });
        }
    }
}
