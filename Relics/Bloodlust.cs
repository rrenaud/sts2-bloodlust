using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Bloodlust.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class Bloodlust : CustomRelicModel
{
    public const int MaxVigor = 15;

    private int _storedVigor;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override List<(string, string)> Localization => new RelicLoc("Bloodlust",
        "Overkill damage you deal is stored. At the start of your next combat, gain that much " +
        $"[gold]Vigor[/gold] (max {MaxVigor}).",
        "It is never quite enough.");

    // No bundled art yet: borrow Red Skull's icon, outline and big image.
    protected override string IconBaseName => ModelDb.Relic<RedSkull>().Id.Entry.ToLowerInvariant();

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<VigorPower>()];

    public override bool ShowCounter => !IsCanonical;
    public override int DisplayAmount => IsCanonical ? 0 : _storedVigor;

    // Saved with the run so the stash survives save & quit between fights.
    [SavedProperty]
    public int StoredVigor
    {
        get => _storedVigor;
        set
        {
            AssertMutable();
            value = Math.Clamp(value, 0, MaxVigor);
            if (_storedVigor == value)
                return;
            _storedVigor = value;
            Status = _storedVigor > 0 ? RelicStatus.Active : RelicStatus.Normal;
            InvokeDisplayAmountChanged();
        }
    }

    public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result,
        ValueProp props, Creature target, CardModel? cardSource)
    {
        // Count our own hits and our pets' (Osty), only against the other side.
        bool ours = dealer != null && (dealer == Owner.Creature || dealer.PetOwner == Owner);
        if (ours && result.OverkillDamage > 0 && result.Receiver.Side != Owner.Creature.Side)
            StoredVigor += result.OverkillDamage;
        return Task.CompletedTask;
    }

    // Same timing as Akabeko: the first player turn of each combat.
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner.Creature) || Owner.PlayerCombatState?.TurnNumber > 1)
            return;
        int vigor = StoredVigor;
        StoredVigor = 0;
        if (vigor <= 0)
            return;
        Flash();
        await PowerCmd.Apply<VigorPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, vigor, Owner.Creature, null);
    }
}
