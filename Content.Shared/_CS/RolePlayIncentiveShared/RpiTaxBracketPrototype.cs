using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._CS.RolePlayIncentiveShared;

/// <summary>
/// This is a prototype for defining cool RPI stuff for like, tax brackets and such.
/// </summary>
[Prototype("rpiTaxBracket")]
public sealed partial class RpiTaxBracketPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// This bracket applies to you if you have less than this amount of $$$
    /// </summary>
    [DataField("cashThreshold", required: true)]
    public int CashThreshold = 0;

    /// <summary>
    /// The payout you get, per Judgement Point.
    /// </summary>
    [DataField("judgementPointPayout", required: true)]
    public int JudgementPointPayout = 0;

    /// <summary>
    /// How much you get penalized for dying. This is percent of your total cash!
    /// </summary>
    [DataField("deathPenalty", required: true)]
    public float DeathPenalty = 0f;

    /// <summary>
    /// How much you get penalized for being deep-fried. This is percent of your total cash!
    /// </summary>
    [DataField("deepFriedPenalty", required: true)]
    public float DeepFriedPenalty = 0f;

    /// <summary>
    /// Journalism
    /// </summary>
    [DataField("journalismDat", required: true)]
    public RpiJournalismData JournalismDat = new();

    /// <summary>
    /// Whats the multiplier for getting paid for mining?
    /// </summary>
    [DataField("actionMultipliers", required: true)]
    public Dictionary<RpiActionType, float> ActionMultipliers = new();
}

/// <summary>
/// Data specific to journalism paywards.
/// </summary>
[Serializable, DataDefinition]
public sealed partial class RpiJournalismData
{
    /// <summary>
    /// baseFlatPayout?
    /// </summary>
    [DataField("baseFlatPayout", required: true)]
    public int BaseFlatPayout = 0;

    /// <summary>
    /// baseNonJournalistFlatPayout?
    /// </summary>
    [DataField("baseNonJournalistFlatPayout", required: true)]
    public int BaseNonJournalistFlatPayout = 0;

    /// <summary>
    /// For every this many characters in the article, you get a bonus payout.
    /// </summary>
    [DataField("charCountDivisor", required: true)]
    public int CharCountDivisor = 100;

    /// <summary>
    /// The bonus per divisor.
    /// </summary>
    [DataField("multPerDivisor", required: true)]
    public float MultPerDivisor = 1.0f;

    /// <summary>
    /// The maximum pay you can get from a single article.
    /// </summary>
    [DataField("maxPay", required: true)]
    public float MaxPay = 5.0f;

    /// <summary>
    /// So you dont just spam the damn articles, apply a cooldown penalty.
    /// This only applies if the cooldown is less than 1 half complete
    /// otherwise it scales up with the time remaining, using a complicated
    /// exponential bell curve derivation that I dont feel like explaining here.
    /// </summary>
    [DataField("cooldownPenalty", required: true)]
    public float CooldownPenalty = 0.90f;

    /// <summary>
    /// Cooldown time in minutes.
    /// </summary>
    [DataField("cooldownTimeMinutes", required: true)]
    public int CooldownTimeMinutes = 20;

    // now some helpers
    public RpiJournalismPayResult GetPaypig(bool isJournal, int charCount, TimeSpan lastArticleTime)
    {
        var basePay = isJournal ? BaseFlatPayout : BaseNonJournalistFlatPayout;
        var pay = (double) basePay;
        if (isJournal)
        {
            if (charCount > CharCountDivisor)
            {
                var paymod = charCount / (double)CharCountDivisor;
                var payCent = basePay * (MultPerDivisor - 1.0f);
                paymod *= payCent;
                pay += paymod;
                if (pay > MaxPay)
                    pay = MaxPay;
            }
        }
        var cooldownMult = CooldownMult(lastArticleTime);
        pay *= cooldownMult;
        var adjustedBasePay = (float) BaseFlatPayout;
        adjustedBasePay *= cooldownMult;
        basePay = (int) Math.Ceiling(adjustedBasePay);
        var totalPay = (int) Math.Ceiling(pay);
        var minsTillCooled = GetTimeWhenFullyCooledDown(lastArticleTime).TotalMinutes;
        if (minsTillCooled < 0)
            minsTillCooled = 0;
        // cooldown mult is the resulting percent of their pay they are getting, after cooldown penalties.
        // 0.1 means they are getting 90% less pay.
        var cooldownMultPercent = 100 - (int) Math.Ceiling(cooldownMult * 100);
        return new RpiJournalismPayResult(
            isJournal,
            basePay,
            totalPay,
            cooldownMult,
            cooldownMultPercent,
            (int) Math.Ceiling(minsTillCooled));
    }

    private float CooldownMult(TimeSpan lastArticleTime)
    {
        if (lastArticleTime == TimeSpan.Zero)
            return 1.0f;
        if (CooldownTimeMinutes <= 0)
            return 1.0f;

        var timeSys = IoCManager.Resolve<IGameTiming>();
        var timeSinceLastArticle = timeSys.CurTime - lastArticleTime;
        var cooldownTime = TimeSpan.FromMinutes(CooldownTimeMinutes);

        if (timeSinceLastArticle >= cooldownTime)
            return 1.0f;
        // fraction from 0.0 (just published) to 1.0 (cooldown complete)
        var fraction = Math.Clamp(timeSinceLastArticle.TotalSeconds / cooldownTime.TotalSeconds, 0.0, 1.0);

        // smooth ease-in-out using a sinusoidal easing (gentle curve)
        // eased == 0 at fraction==0, eased == 1 at fraction==1
        var eased = 0.5 * (1 - Math.Cos(Math.PI * fraction));

        // interpolate from CooldownPenalty -> 1.0
        var mulTout = CooldownPenalty + (1.0 - CooldownPenalty) * eased;
        return (float) mulTout;
    }

    private TimeSpan GetTimeWhenFullyCooledDown(TimeSpan lastArticleTime)
    {
        if (lastArticleTime == TimeSpan.Zero)
            return TimeSpan.Zero;
        var timeSys = IoCManager.Resolve<IGameTiming>();
        var timeSinceLastArticle = timeSys.CurTime - lastArticleTime;
        var cooldownTime = TimeSpan.FromMinutes(CooldownTimeMinutes);
        if (timeSinceLastArticle >= cooldownTime)
            return TimeSpan.Zero;
        return cooldownTime - timeSinceLastArticle;
    }

}

/// <summary>
/// The return thing for when you the journalism treutnrs
/// </summary>
public struct RpiJournalismPayResult(
    bool isJournalist,
    int basePay,
    int totalPay,
    float cooldownMultiplier,
    int cooldownPercent,
    int minsTilCooled)
{
    public bool IsJournalist = isJournalist;
    public int BasePay = basePay;
    public int TotalPay = totalPay;
    public float CooldownMultiplier = cooldownMultiplier;
    public int CooldownPercent = cooldownPercent;
    public int MinsTillCooled = minsTilCooled;
}
