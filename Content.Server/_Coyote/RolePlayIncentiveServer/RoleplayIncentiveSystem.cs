using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Server.Store.Components;
using Content.Shared._Coyote;
using Content.Shared._Coyote.RolePlayIncentiveShared;
using Content.Shared._Coyote.RolePlayIncentiveShared.Components;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Content.Shared.Store;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

// ReSharper disable InconsistentNaming

namespace Content.Server._Coyote;

/// <summary>
/// This handles...
/// </summary>
public sealed class RoleplayIncentiveSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming          _timing = null!;
    [Dependency] private readonly BankSystem           _bank = null!;
    [Dependency] private readonly PopupSystem          _popupSystem = null!;
    [Dependency] private readonly ChatSystem           _chatsys = null!;
    [Dependency] private readonly IChatManager         _chatManager = null!;
    [Dependency] private readonly IPlayerManager       _playerManager = null!;
    [Dependency] private readonly SSDIndicatorSystem   _ssdThing = null!;
    [Dependency] private readonly IPrototypeManager    _prototype = default!;
    [Dependency] private readonly TagSystem            _tagSystem = default!;
    [Dependency] private readonly MobStateSystem       _mobStateSystem = default!;
    [Dependency] private readonly ExamineSystemShared  _examineSystem = default!;
    [Dependency] private readonly TransformSystem      _tf = default!;
    [Dependency] private readonly PaperSystem          _paperSystem = default!;
    [Dependency] private readonly IEntityManager       _entityManager = default!;
    [Dependency] private readonly HandsSystem          _heandsSystem = default!;
    [Dependency] private readonly StackSystem          _stack = default!;

    private List<ProtoId<RpiTaxBracketPrototype>> RpiDatumPrototypes = new()
    {
        "rpiTaxBracketBroke",
        "rpiTaxBracketEstablished",
        "rpiTaxBracketWealthy",
    };

    private ProtoId<RpiTaxBracketPrototype> TaxBracketDefault = "rpiTaxBracketDefault";

    private Dictionary<RpiChatActionCategory, string> ChatActionLookup = new()
    {
        { RpiChatActionCategory.Speaking, "rpiChatActionSpeaking" },
        { RpiChatActionCategory.Whispering, "rpiChatActionWhispering" },
        { RpiChatActionCategory.Emoting, "rpiChatActionEmoting" },
        { RpiChatActionCategory.QuickEmoting, "rpiChatActionQuickEmoting" },
        { RpiChatActionCategory.Subtling, "rpiChatActionSubtling" },
        { RpiChatActionCategory.Radio, "rpiChatActionRadio" },
    };

    private List<RpiContinuousProxyActionPrototype> AllContinuousProxies = new();

    private TimeSpan DeathPunishmentCooldown = TimeSpan.FromMinutes(30);
    private TimeSpan DeepFryerPunishmentCooldown = TimeSpan.FromMinutes(5); // please stop deep frying tesharis

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<RoleplayIncentiveComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiChatEvent>(OnGotRpiChatEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiActionMultEvent>(OnGotRpiActionEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiImmediatePayEvent>(OnRpiImmediatePayEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, GetRpiModifier>(OnSelfSucc);
        SubscribeLocalEvent<RoleplayIncentiveComponent, MobStateChangedEvent>(OnGotMobStateChanged);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiNewsArticleCreatedEvent>(OnNewsArticleCreated);
        SubscribeLocalEvent<RoleplayIncentiveComponent, FixedLightEvent>(OnLightGotFixed);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiAddAurasEvent>(OnAddAurasEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiAddJudgementsEvent>(OnAddJudgementsEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, RpiAddJobModifierEvent>(OnAddJobModifierEvent);
        SubscribeLocalEvent<RoleplayIncentiveComponent, GetVerbsEvent<Verb>>(GetRpiVerbs);
        // SubscribeLocalEvent<RoleplayIncentiveComponent, GetVerbsEvent<ExamineVerb>>   (OnGetExamineVerbs);
        SortTaxBrackets();
    }

    #region Setup stuff

    /// <summary>
    /// Sorts the tax brackets by cash threshold, lowest to highest.
    /// This is done so that we can easily find the correct tax bracket for a player.
    /// </summary>
    private void SortTaxBrackets()
    {
        RpiDatumPrototypes.Sort(
            (a, b) =>
            {
                if (!_prototype.TryIndex(a, out var protoA))
                {
                    Log.Warning($"RpiTaxBracketPrototype {a} not found!");
                    return 0;
                }

                if (!_prototype.TryIndex(b, out var protoB))
                {
                    Log.Warning($"RpiTaxBracketPrototype {b} not found!");
                    return 0;
                }

                return protoA.CashThreshold.CompareTo(protoB.CashThreshold);
            });
    }

    #endregion

    #region Event Handlers

    private void OnComponentInit(EntityUid uid, RoleplayIncentiveComponent component, ComponentInit args)
    {
        // set the next payward time
        component.NextPayward                    = _timing.CurTime + component.PaywardInterval;
        component.NextProxyCheck                 = _timing.CurTime + component.ProxyCheckInterval;
        component.NextProxySync                  = _timing.CurTime + component.ProxySyncInterval;
        component.NextAuraCheck                  = _timing.CurTime + component.AuraCheckInterval;
        component.FlarpiDatacore.LastFlarpiCheck = _timing.CurTime + component.FlarpiDatacore.FlarpiCheckInterval;
    }

    /// <summary>
    /// This is called when a roleplay incentive event is received.
    /// It checks if it should be done, then it does it when it happensed
    /// </summary>
    /// <param name="uid">The entity that did the thing</param>
    /// <param name="rpic">The roleplay incentive component on the entity</param>
    /// <param name="args">The roleplay incentive event that was received</param>
    /// <remarks>
    /// piss
    /// </remarks>
    private void OnGotRpiChatEvent(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiChatEvent args)
    {
        ProcessRoleplayIncentiveEvent(uid, args);
    }

    /// <summary>
    /// Adds a modifier to the next payward, based on the event.
    /// Duplicate events will take the highest modifier.
    /// Is a multiplier!!!
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="rpic"></param>
    /// <param name="args"></param>
    private void OnGotRpiActionEvent(
        EntityUid uid,
        RoleplayIncentiveComponent? rpic,
        RpiActionMultEvent args)
    {
        if (!Resolve(uid, ref rpic))
            return;
        if (args.Handled)
            return;
        args.Handled = true;
        // make the record
        var now = _timing.CurTime;
        var peoplePresent = -1; // cant really know this one yet
        if (args.CheckForPeoplePresent())
            peoplePresent = GetPeopleInRange(uid, 10f); // 10 tile radius
        var action = new RpiActionRecord(
            now,
            args.Action,
            args.Multiplier,
            peoplePresent,
            args.GetPeoplePresentModifier(),
            args.Paywards);
        // add it tothe actions taken
        rpic.MiscActionsTaken.Add(action);
    }

    /// <summary>
    /// Immediately pays the player, bypassing the normal payward system.
    /// This is used for things like mining, which should give immediate rewards.
    /// </summary>
    private void OnRpiImmediatePayEvent(
        EntityUid uid,
        RoleplayIncentiveComponent? rpic,
        RpiImmediatePayEvent args)
    {
        if (!Resolve(uid, ref rpic))
            return;
        if (args.Handled)
            return;
        args.Handled = true;
        var basePay = args.FlatPay;
        ModifyImmediatePay(
            uid,
            rpic,
            args,
            ref basePay);
        PaymentifySimple(
            uid,
            rpic,
            basePay,
            "coyote-rpi-immediate-pay-message",
            "coyote-rpi-immediate-pay-popup");
    }

    // private void OnGetExamineVerbs(
    //     EntityUid uid,
    //     RoleplayIncentiveComponent rpic,
    //     GetVerbsEvent<ExamineVerb> args)
    // {
    //     HandleExamineRpiVerb(uid, rpic, args);
    // }

    /*
     * None
     * Local -> RpiChatActionCategory.Speaking
     * Whisper -> RpiChatActionCategory.Whispering
     * Server
     * Damage
     * Radio -> RpiChatActionCategory.Radio
     * LOOC
     * OOC
     * Visual
     * Notifications
     * Emotes -> RpiChatActionCategory.Emoting OR RpiChatActionCategory.QuickEmoting
     * Dead
     * Admin
     * AdminAlert
     * AdminChat
     * Unspecified
     * Telepathic
     * Subtle -> RpiChatActionCategory.Subtling
     * rest are just null
     */

    /// <summary>
    /// Applies the self success multiplier to the payward
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnSelfSucc(
        EntityUid uid,
        RoleplayIncentiveComponent component,
        ref GetRpiModifier args)
    {
        if (TryComp<SSDIndicatorComponent>(uid, out var ssd)
            && _ssdThing.IsInNashStation(uid))
        {
            args.Modify(1.5f, 0f); // 'double' pay if youre in nash!
        }
    }

    /// <summary>
    /// If the mob dies, punish them for being awful
    /// </summary>
    private void OnGotMobStateChanged(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        MobStateChangedEvent args)
    {
        if (!rpic.PunishDeath)
            return;
        if (args.NewMobState != MobState.Dead)
            return;
        var curTime = _timing.CurTime;
        // if they died recently, dont punish them again
        if (curTime < rpic.LastDeathPunishment + DeathPunishmentCooldown)
            return;
        PunishPlayerForDeath(uid, rpic);
    }

    public void GetRpiVerbs(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        GetVerbsEvent<Verb> args
        )
    {
        if (args.User == args.Target
            && CanFlarpi(uid, rpic)
            && rpic.FlarpiDatacore.BankedFlarpis >= 1)
        {
            _prototype.TryIndex(rpic.FlarpiDatacore.DatacoreType, out FlarpiSettingsPrototype? flarpiProto);
            string vervoid = flarpiProto != null
                ? flarpiProto.FlarpiNameToken
                : "flompy";
            string flarpiName = Loc.GetString(
                $"coyote-rpi-flarpi-withdrawverb-{vervoid}",
                ("amount", rpic.FlarpiDatacore.BankedFlarpis));
            // flarpi withdraw!
            Verb vorb = new()
            {
                Text = flarpiName,
                Act = () =>
                {
                    FlarpiWithdraw(
                        uid,
                        rpic);
                },
                Priority = 2,
            };
            args.Verbs.Add(vorb);
        }

        if (HasComp<AdminGhostComponent>(args.User))
        {
            Verb verb = new()
            {
                Text = "Print RPI Details",
                Act = () =>
                {
                    var details = HandleEverything(
                        uid,
                        rpic,
                        true);
                    OutputToPaper(details, args.User);
                },
                Priority = 1,
            };
            args.Verbs.Add(verb);
        }
    }

    /// <summary>
    /// Now we take all the cool stuff we recorded, and blast it into a fluffing huge human readable
    /// set of data for easy debugging and whatnot.
    /// And turn it into a piece of paper, cus why the heck not.
    /// </summary>
    public void OutputToPaper(
        RpiPaywardDetails deets,
        EntityUid reader
        )
    {
        List<string> lines = new();
        lines.Add($"[bold]--- RPI Institute Payward Analysis ---[/bold]");
        lines.Add($"[bold]Subject:[/bold] [color=Blue]{deets.Name}[/color]");
        lines.Add($"[bold]Report Generated:[/bold] [color=Blue]{_timing.CurTime}[/color]");
        lines.Add($"");
        if (deets.RpiComponent != null)
        {
            lines.Add($"[bold]--- RPI Component Data ---[/bold]");
            lines.Add($"[bold]RPI ID:[/bold] [color=Blue]{deets.RpiComponent.RpiId}[/color]");
            lines.Add($"[bold]Total Chat Actions Recorded:[/bold] [color=Blue]{deets.RpiComponent.ChatActionsTaken.Count}[/color]");
            lines.Add($"[bold]Total Misc Actions Recorded:[/bold] [color=Blue]{deets.RpiComponent.MiscActionsTaken.Count}[/color]");
            lines.Add($"[bold]Last Payward Check:[/bold] [color=Blue]{deets.RpiComponent.LastCheck}[/color]");
            lines.Add($"[bold]Next Payward Check:[/bold] [color=Blue]{deets.RpiComponent.NextPayward}[/color]");
        }
        lines.Add($"");
        lines.Add($"[bold]--- Payward Breakdown ---[/bold]");
        lines.Add($"[bold]Base Chat Pay:[/bold] [color=Blue]{deets.ChatPay}[/color]");
        lines.Add($"[bold]Proxy Multiplier:[/bold] [color=Blue]{deets.ProxyMultiplier:0.00}[/color]");
        lines.Add($"[bold]Aura Multiplier:[/bold] [color=Blue]{deets.AuraMultiplier:0.00}[/color]");
        lines.Add($"[bold]Job Multiplier:[/bold] [color=Blue]{deets.JobModifier:0.00}[/color]");
        lines.Add($"[bold]Other Modifiers:[/bold] [color=Blue]{deets.OtherModifiers:0.00}[/color]");
        lines.Add($"[bold]Final Multiplier:[/bold] [color=Blue]{deets.FinalMultiplier:0.00}[/color]");
        lines.Add($"");
        lines.Add($"[bold]--- Final Pay Details ---[/bold]");
        lines.Add($"[bold]Base Pay:[/bold] [color=Blue]{deets.FinalPayDetails.BasePay}[/color]");
        lines.Add($"[bold]Raw Multiplier:[/bold] [color=Blue]{deets.FinalPayDetails.RawMultiplier:0.00}[/color]");
        lines.Add($"[bold]Final Multiplier Applied:[/bold] [color=Blue]{(deets.FinalPayDetails.HasMultiplier ? "Yes" : "No")}[/color]");
        lines.Add($"[bold]Final Pay:[/bold] [color=Blue]{deets.FinalPayDetails.FinalPay}[/color]");
        lines.Add($"");
        lines.Add($"[bold]--- Tax Bracket Details ---[/bold]");
        lines.Add($"[bold]Pay Per Judgement:[/bold] [color=Blue]{deets.TaxBracket.PayPerJudgement}[/color]");
        lines.Add($"[bold]Death Penalty:[/bold] [color=Blue]{deets.TaxBracket.DeathPenalty}[/color]");
        lines.Add($"[bold]Deep Fryer Penalty:[/bold] [color=Blue]{deets.TaxBracket.DeepFryPenalty}[/color]");
        lines.Add($"");
        lines.Add($"[bold]--- Tax Bracket Journalism Data ---[/bold]");
        lines.Add($"[bold]Base Journalist Payout:[/bold] [color=Blue]{deets.TaxBracket.JournalismData.BaseFlatPayout}[/color]");
        lines.Add($"[bold]Base Otherwise Payout:[/bold] [color=Blue]{deets.TaxBracket.JournalismData.BaseNonJournalistFlatPayout}[/color]");
        lines.Add($"[bold]Character Count Divisor:[/bold] [color=Blue]{deets.TaxBracket.JournalismData.CharCountDivisor}[/color]");
        lines.Add($"[bold]Multiplier Per Divisor:[/bold] [color=Blue]{deets.TaxBracket.JournalismData.MultPerDivisor:0.00}[/color]");
        lines.Add($"[bold]Max Pay Per Article:[/bold] [color=Blue]{deets.TaxBracket.JournalismData.MaxPay:0.00}[/color]");
        lines.Add($"[bold]Cooldown Penalty (base):[/bold] [color=Blue]{deets.TaxBracket.JournalismData.CooldownPenalty:0.00}[/color]");
        lines.Add($"[bold]Cooldown Time (minutes):[/bold] [color=Blue]{deets.TaxBracket.JournalismData.CooldownTimeMinutes}[/color]");
        // If component data is available, show current cooldown state using LastArticleTime
        var lastArticle = deets.RpiComponent?.LastArticleTime ?? TimeSpan.Zero;
        var payPreview = deets.TaxBracket.JournalismData.GetPaypig(
            true,
            0,
            lastArticle);
        lines.Add($"[bold]Cooldown Multiplier (current):[/bold] [color=Blue]{payPreview.CooldownMultiplier:0.00}[/color]");
        lines.Add($"[bold]Cooldown Percent Penalty:[/bold] [color=Blue]{payPreview.CooldownPercent}[/color][bold]%[/bold]");
        lines.Add($"[bold]Minutes Until Fully Cooled:[/bold] [color=Blue]{payPreview.MinsTillCooled}[/color]");
        lines.Add($"");
        lines.Add($"[bold]--- Chat Action Judgement Details ---[/bold]");
        foreach (var (category, details) in deets.ChatActionPays)
        {
            lines.Add($"[bold]Category:[/bold] [color=Blue]{category}[/color]");
            lines.Add($"[bold]* Messages Sent:[/bold] [color=Blue]{details.Message}[/color]");
            lines.Add($"[bold]* Total Chat Length:[/bold] [color=Blue]{details.ChatLength:0}[/color] [bold]characters[/bold]");
            lines.Add($"[bold]* Length Multiplier:[/bold] [color=Blue]{details.ChatLengthMultiplier:0.00}[/color]");
            lines.Add($"[bold]* People listening:[/bold] [color=Blue]{details.NumListenings:0}[/color]");
            lines.Add($"[bold]* Final Score:[/bold] $[color=Blue]{details.FinalScore:0}[/color]");
        }
        if (deets.Auras.Count > 0)
        {
            lines.Add($"");
            lines.Add($"[bold]--- Aura Data ---[/bold]");
            foreach (var aura in deets.Auras)
            {
                var mname = MetaData(aura.Source).EntityName;
                lines.Add($"[bold]* Source:[/bold] [color=Blue]{mname}[/color]");
                lines.Add($"[bold]* Aura ID:[/bold] [color=Blue]{aura.AuraId}[/color]");
                lines.Add($"[bold]* RPI UID:[/bold] [color=Blue]{aura.RpiUid}[/color]");
                lines.Add($"[bold]* Max Multiplier:[/bold] [color=Blue]{aura.Multiplier - 1f:0.00}[/color]");
                lines.Add($"[bold]* Current Effect Multiplier:[/bold] [color=Blue]{(aura.GetCurrentMultiplier()):0.00}[/color]");
                lines.Add($"[bold]* Max Distance:[/bold] [color=Blue]{aura.MaxDistance:0.00}[/color]");
                lines.Add($"[bold]* Time Till Full Effect:[/bold] [color=Blue]{aura.TimeTillFullEffect}[/color]");
                lines.Add($"[bold]* Decay Delay:[/bold] [color=Blue]{aura.DecayDelay}[/color]");
                lines.Add($"[bold]* Decay To Zero Time:[/bold] [color=Blue]{aura.DecayToZeroTime}[/color]");
                lines.Add($"[bold]* Time Spent In Aura:[/bold] [color=Blue]{aura.TimeSpentInAura}[/color]");
                lines.Add($"[bold]* Time Outside Aura:[/bold] [color=Blue]{aura.TimeOutsideAura}[/color]");
                lines.Add($"[bold]* Decay Coefficient:[/bold] [color=Blue]{aura.DecayCoefficient:0.0000}[/color]");
            }
        }
        lines.Add($"");
        lines.Add($"");
        lines.Add($"[bold]--- End of Report ---[/bold]");
        lines.Add($"[bold]Generated on:[/bold] [color=Blue]{DateTime.Now}[/color]");
        lines.Add($"[bold]RPI Institute Internal Use Only[/bold]");
        var finalText = string.Join("\n", lines);
        var coords = _tf.GetMapCoordinates(Transform(reader));
        var paperEntity = _entityManager.Spawn("Paper", coords);
        TryComp<PaperComponent>(paperEntity, out var paperComp);
        _paperSystem.SetContent((paperEntity, paperComp!), finalText);
        // then, THEN, we give it to the reader.
        _heandsSystem.TryPickupAnyHand(reader, paperEntity);
        //whatever
    }

    private void OnAddJobModifierEvent(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiAddJobModifierEvent args)
    {
        foreach (var jobMod in args.StuffToAdd)
        {
            AddJobModifier(
                uid,
                rpic,
                jobMod);
        }
    }

    public static void AddJobModifier(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ProtoId<RpiJobModifierPrototype> jobMod)
    {
        rpic.JobModifiers.Add(jobMod);
    }

    private void OnAddJudgementsEvent(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiAddJudgementsEvent args)
    {
        foreach (var judgement in args.StuffToAdd)
        {
            AddJudgementModifier(
                uid,
                rpic,
                judgement);
        }
    }

    public static void AddJudgementModifier(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ProtoId<RpiChatJudgementModifierPrototype> judgement)
    {
        rpic.ChatJudgementModifiers.Add(judgement);
    }

    private void OnAddAurasEvent(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiAddAurasEvent args)
    {
        foreach (var auraId in args.AurasToAdd)
        {
            AddAuraSource(
                uid,
                rpic,
                auraId);
        }
    }

    /// <summary>
    /// When a news article is created, give a payward for it.
    /// Extra if they are a journalist.
    /// </summary>
    private void OnNewsArticleCreated(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiNewsArticleCreatedEvent args)
    {
        var taxBracket = GetTaxBracketData(args.Doer);
        var jDat = taxBracket.JournalismData;
        RpiJournalismPayResult paypig; // NOTHING
        var lengf =
            args.NArticle.Content.Length
            + args.NArticle.Title.Length;
        var amJournalist = HasComp<PaywardActionNewsArticleCreationComponent>(args.Doer);
        paypig = jDat.GetPaypig(
            amJournalist,
            lengf,
            rpic.LastArticleTime);

        rpic.LastArticleTime = _timing.CurTime;
        // pay the player
        PayPlayer(args.Doer, paypig.TotalPay);
        _popupSystem.PopupEntity(
            Loc.GetString(
                "coyote-rpi-journalism-pay-popup",
                ("amount", paypig.TotalPay)),
            args.Doer,
            args.Doer,
            PopupType.Large);
        // the chat message is a bit more complicated
        var showCooldown = paypig.MinsTillCooled > 0;
        var showLengthBonus = amJournalist && paypig.BasePay != paypig.TotalPay;
        string chessage;
        if (showCooldown)
        {
            if (showLengthBonus)
            {
                chessage = Loc.GetString(
                    "coyote-rpi-journalism-pay-message-bonus-cooldown",
                    ("amount", paypig.TotalPay),
                    ("lengthBonus", paypig.TotalPay - paypig.BasePay),
                    ("charCount", lengf),
                    ("baseAmount", paypig.BasePay),
                    ("cooldownMult", paypig.CooldownPercent.ToString("0.00")),
                    ("minsTillCooled", paypig.MinsTillCooled));
            }
            else
            {
                chessage = Loc.GetString(
                    "coyote-rpi-journalism-pay-message-cooldown",
                    ("amount", paypig.TotalPay),
                    ("cooldownMult", paypig.CooldownPercent.ToString("0.00")),
                    ("minsTillCooled", paypig.MinsTillCooled));
            }
        }
        else
        {
            if (showLengthBonus)
            {
                chessage = Loc.GetString(
                    "coyote-rpi-journalism-pay-message-bonus",
                    ("amount", paypig.TotalPay),
                    ("lengthBonus", paypig.TotalPay - paypig.BasePay),
                    ("charCount", lengf),
                    ("baseAmount", paypig.BasePay));
            }
            else
            {
                chessage = Loc.GetString(
                    "coyote-rpi-journalism-pay-message",
                    ("amount", paypig.TotalPay));
            }
        }

        if (_playerManager.TryGetSessionByEntity(uid, out var session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                chessage,
                chessage,
                default,
                false,
                session.Channel);
        }
    }

    /// <summary>
    /// When a light is fixed, give a small payward for it.
    /// </summary>
    private void OnLightGotFixed(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        FixedLightEvent args)
    {
        // SO HOW HERES HOWS IT GONNA GO
        // You get a base payward for fixing a light thats been broken by Solar Flares
        // You get a bonus if:
        // 1. You are a janitor (big bonus)
        // 2. The light is on Nash (small bonus) - to do
        // 3. The light is on some other station (Big bonus) - to do
        // 4. The light is on a shuttle that is NOT yours (medium bonus) - also to do
        // 5. The light has been broken for a long time (small bonus)
        var paydata = AppraiseBrokenLight(
            args.Source, // who is appraising
            args.TimeSpentBroken, // how long it was broken
            rpic);
        if (paydata.FinalPay <= 0)
            return; // no pay, no pramgle
        PayPlayer(args.Source, paydata.FinalPay);
        // popup
        _popupSystem.PopupEntity(
            Loc.GetString(
                "coyote-rpi-light-fix-pay-popup",
                ("amount", paydata.FinalPay)),
            args.LightEntity, // show the popup on the light
            args.Source,
            PopupType.Large,
            suppressChat: true);
        // chat message
        string outtext;
        if (!paydata.IsJanitor)
        {
            outtext = Loc.GetString(
                "coyote-rpi-light-fix-pay-message-no-janitor",
                ("amount", paydata.FinalPay));
        }
        else
        {
            switch (paydata.SpreeCount)
            {
                case > 1
                    when paydata.TimeBroken > rpic.LightFixTimeBrokenBonusThreshold:
                    outtext = Loc.GetString(
                        "coyote-rpi-light-fix-pay-message-janitor-spree-timebroken",
                        ("amount", paydata.FinalPay),
                        ("baseAmount", paydata.BasePay),
                        ("janitorBonus", paydata.JanitorBonus),
                        ("spreeBonus", paydata.SpreeBonus),
                        ("spreeCount", paydata.SpreeCount),
                        ("timeBrokenBonus", paydata.TimeBrokenBonus),
                        ("timeBrokenMinutes", (int)paydata.TimeBroken.TotalMinutes));
                    break;
                case > 1:
                    outtext = Loc.GetString(
                        "coyote-rpi-light-fix-pay-message-janitor-spree",
                        ("amount", paydata.FinalPay),
                        ("baseAmount", paydata.BasePay),
                        ("janitorBonus", paydata.JanitorBonus),
                        ("spreeBonus", paydata.SpreeBonus),
                        ("spreeCount", paydata.SpreeCount));
                    break;
                default:
                {
                    if (paydata.TimeBroken > rpic.LightFixTimeBrokenBonusThreshold)
                    {
                        outtext = Loc.GetString(
                            "coyote-rpi-light-fix-pay-message-janitor-timebroken",
                            ("amount", paydata.FinalPay),
                            ("baseAmount", paydata.BasePay),
                            ("janitorBonus", paydata.JanitorBonus),
                            ("timeBrokenBonus", paydata.TimeBrokenBonus),
                            ("timeBrokenMinutes", (int)paydata.TimeBroken.TotalMinutes));
                    }
                    else
                    {
                        outtext = Loc.GetString(
                            "coyote-rpi-light-fix-pay-message-janitor",
                            ("amount", paydata.FinalPay),
                            ("baseAmount", paydata.BasePay),
                            ("janitorBonus", paydata.JanitorBonus));
                    }

                    break;
                }
            }
        }

        if (_playerManager.TryGetSessionByEntity(args.Source, out var session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                outtext,
                outtext,
                default,
                false,
                session.Channel);
        }
    }

    public RpiLightFixData AppraiseBrokenLight(
        EntityUid doer,
        TimeSpan TimeSpentBroken,
        RoleplayIncentiveComponent? rpic = null,
        bool justChecking = false)
    {
        if (!Resolve(doer, ref rpic))
            return new RpiLightFixData(0);
        var paydata = new RpiLightFixData(rpic.LightFixPay);
        // janitor bonus
        if (!HasComp<PaywardActionMaintenanceComponent>(doer))
            return paydata;
        paydata.ApplyJanitorBonus(rpic.LightFixByJanitorBonus);
        if (!justChecking)
            rpic.LightSpree.Add(_timing.CurTime);
        if (TimeSpentBroken >= rpic.LightFixTimeBrokenBonusThreshold)
        {
            var timeBrokenPastThreshold = TimeSpentBroken - rpic.LightFixTimeBrokenBonusThreshold;
            // after an hour, give a linear bonus up to the max time, counting per minute
            var minutesPastThreshold = Math.Min(
                timeBrokenPastThreshold.TotalMinutes,
                (rpic.LightFixTimeBrokenMaxBonusThreshold - rpic.LightFixTimeBrokenBonusThreshold).TotalMinutes);
            var timeBrokenBonus = (int)(minutesPastThreshold * (rpic.LightFixCashPerHourBroken / 60.0f));
            paydata.ApplyTimeBrokenBonus(timeBrokenBonus, TimeSpentBroken);
        }

        // recalculate the spree
        rpic.LightSpree = rpic.LightSpree
            .Where(t => _timing.CurTime - t <= rpic.MaxLightSpreeTime)
            .ToList();
        // for each light fixed in the spree, give a small bonus
        // uses an exponential asymptotic formula that follows:
        // up to 100% bonus easily, but past that, it will asymptotically approach a max of 300% bonus
        // so you can never get more than 3x bonus, but its really hard to get past 2x
        // formula: bonus = 1 - e^(-k * n), where k is a constant and n is the number of lights fixed in the spree
        // solving for k when n = 10 and bonus = 0.9 gives k = 0.2302585093
        // idfk what any of this is, its just algovomit and seems to work
        const double k = 0.2302585093;
        var spreeCount = rpic.LightSpree.Count;
        if (spreeCount > 1)
        {
            var bonusMult = 1 - Math.Exp(-k * spreeCount);
            var spreeBonus = (int)(paydata.BasePay * bonusMult);
            paydata.ApplySpreeBonus(spreeBonus, spreeCount);
        }

        return paydata;
    }

    public sealed class RpiLightFixData(int basePay)
    {
        public int BasePay = basePay;
        public int FinalPay = basePay;
        public bool IsJanitor = false;
        public int JanitorBonus = 0;
        public int SpreeBonus = 0;

        public int SpreeCount = 0;

        // public int StationBonus       = 0;
        // public int OtherStationBonus  = 0;
        // public int ShuttleBonus       = 0;
        public int TimeBrokenBonus = 0;
        public TimeSpan TimeBroken = TimeSpan.Zero;

        public void ApplyJanitorBonus(int amount)
        {
            IsJanitor = true;
            amount = Roundify(amount);
            JanitorBonus = amount;
            FinalPay += amount;
        }

        public void ApplySpreeBonus(int amount, int spreeCount)
        {
            amount = Roundify(amount);
            SpreeBonus = amount;
            SpreeCount = spreeCount;
            FinalPay += amount;
        }

        // public void ApplyStationBonus(int amount)
        // {
        //     StationBonus = amount;
        //     FinalPay += amount;
        // }
        //
        // public void ApplyOtherStationBonus(int amount)
        // {
        //     OtherStationBonus = amount;
        //     FinalPay += amount;
        // }
        //
        // public void ApplyShuttleBonus(int amount)
        // {
        //     ShuttleBonus = amount;
        //     FinalPay += amount;
        // }

        public void ApplyTimeBrokenBonus(int amount, TimeSpan timeBroken)
        {
            amount = Roundify(amount);
            TimeBroken = timeBroken;
            TimeBrokenBonus = amount;
            FinalPay += amount;
        }

        // Rounds the amount to the nearest 10
        public int Roundify(int amtount)
        {
            return (int)(Math.Round(amtount / 10.0) * 10);
        }
    }

    #endregion

    #region Main Update Loop

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RoleplayIncentiveComponent>();
        while (query.MoveNext(out var uid, out var rpic))
        {
            if (!rpic.DebugIgnoreState)
            {
                if (!ValidForRPI(uid))
                {
                    HousekeepFlarpi(uid, rpic, frameTime);
                    continue; // not valid for RPI
                }
            }

            HandleEverything(
                uid,
                rpic,
                false);
        }
    }

    public RpiPaywardDetails HandleEverything(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        bool justChecking = false)
    {
        var result = new RpiPaywardDetails();
        result.LoadName(MetaData(uid).EntityName);
        result.LoadComponentData(rpic);
        SyncContinuousComponentsAndProxies(uid, rpic);
        ProcessContinuousProxies(uid, rpic);
        ProcessAuras(
            uid,
            rpic,
            ref result,
            justChecking);
        ProcessFlarpies(
            uid,
            rpic,
            ref result,
            justChecking);
        PayoutPaywardToPlayer(
            uid,
            rpic,
            ref result,
            justChecking);
        return result;
    }

    #endregion

    #region Payward Action

    /// <summary>
    /// Goes through all the relevant actions taken and stored, judges them,
    /// And gives the player a payward if they did something good.
    /// It also checks for things like duplicate actions, if theres people around, etc.
    /// Basically if you do stuff, you get some pay for it!
    /// </summary>
    private void PayoutPaywardToPlayer(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ref RpiPaywardDetails details,
        bool justChecking)
    {
        if (!justChecking)
        {
            if (_timing.CurTime < rpic.NextPayward)
                return; // too soon to pay again
            rpic.NextPayward = _timing.CurTime + rpic.PaywardInterval;
        }
        if (!_bank.TryGetBalance(uid, out var hasThisMuchMoney))
            return; // no bank account, no pramgle

        var taxBracket = GetTaxBracketData(
            rpic,
            hasThisMuchMoney);
        details.LoadTaxBracketData(taxBracket);

        // ChatPay gets an int as our base pay
        var chatPay = GetChatActionPay(
            uid,
            rpic,
            taxBracket,
            ref details,
            justChecking);
        // MiscPay gets a multiplier to modify the base pay
        // var miscMult = GetMiscActionPayMult(
        //     uid,
        //     rpic,
        //     taxBracket); // no details cus it doesnt do anything yet
        // Continuous proxies are applied here too, as multipliers
        var proxyMult = GetProxiesPayMult(
            uid,
            rpic,
            !justChecking);
        // Aura mults! mobs who give you more money for being around them
        var auraMult = GetAuraPayMult(uid, rpic);
        // Job multipliers! my job gets cashpay for existing!
        var jobMult = GetJobModifierPayMult(uid, rpic);
        // other components wanteing to messing with me
        var modMult = 1f;
        var modifyEvent = new GetRpiModifier(uid, modMult);
        RaiseLocalEvent(
            uid,
            modifyEvent,
            true);
        if (Math.Abs(rpic.DebugMultiplier - 1f) > 0.001f)
        {
            Log.Info($"RPI Debug Multiplier applied: {rpic.DebugMultiplier}");
            modifyEvent.Multiplier = rpic.DebugMultiplier;
        }

        var finalMult = 1f;
        // finalMult += miscMult;
        finalMult += proxyMult;
        finalMult += (modifyEvent.Multiplier - 1f);
        finalMult += auraMult;
        finalMult += jobMult;
        var payDetails = ProcessPaymentDetails(
            (int)chatPay,
            finalMult);
        details.LoadProxyMultiplier(proxyMult);
        details.LoadAuraMultiplier(auraMult);
        details.LoadJobMultiplier(jobMult);
        details.LoadOtherMultiplier(modifyEvent.Multiplier);
        details.LoadChatPay((int)chatPay);
        details.LoadPayDetails(payDetails);

        if (justChecking)
            return; // we were just checking, no pramgle
        // pay the player
        PayPlayer(uid, payDetails.FinalPay);
        ShowPopup(uid, payDetails);
        ShowChatMessage(uid, payDetails);
        PruneOldActions(rpic);
        HandleFlarpiBanking(uid, rpic, payDetails);
    }

    #endregion

    #region Continuous Proxy Actions

    /// <summary>
    /// Checks the component's continuous proxies, and processes them.
    /// Easy peasy.
    /// </summary>
    private void ProcessContinuousProxies(EntityUid uid, RoleplayIncentiveComponent rpic)
    {
        if (_timing.CurTime < rpic.NextProxyCheck)
            return; // too soon to check again
        rpic.NextProxyCheck = _timing.CurTime + rpic.ProxyCheckInterval;
        if (rpic.Proxies.Count == 0) // now when I say proxy, I mean proximity
            return; // no proxies, no pramgle
        foreach (var (proxKind, proxData) in rpic.Proxies)
        {
            if (!_prototype.TryIndex(proxData.Proto, out var proxProto))
            {
                Log.Warning($"RpiProxyPrototype {proxData.Proto} not found!");
                continue;
            }

            if (proxProto.TargetMustHaveTheseComponents.Count == 0)
            {
                Log.Warning($"RpiProxyPrototype {proxData.Proto} has no target components defined!");
                continue;
            }

            // Check if the user has any excluded components
            if (proxProto.UserMustNotHaveTheseComponents.Any(comp => HasComp(uid, comp.Value.Component.GetType())))
                continue; // they have a component that excludes them from this proxy

            // Check if the user is missing any required components
            if (proxProto.UserMustHaveTheseComponents.Any(comp => !HasComp(uid, comp.Value.Component.GetType())))
                continue; // they don't have a component that includes them in this proxy

            // ok, lets roll through all the connected players,
            // and poll them for components and distance
            var ourCoords = Transform(uid).Coordinates;
            var somethingHappened = false;

            var entsWithComponents = proxProto.IsNonPlayerComponentQuery
                ? GetEntitiesWithComponents(
                    uid,
                    proxProto.TargetMustHaveTheseComponents,
                    proxProto.TargetMustNotHaveTheseComponents)
                : GetPlayersWithComponents(
                    uid,
                    proxProto.TargetMustHaveTheseComponents,
                    proxProto.TargetMustNotHaveTheseComponents);
            if (entsWithComponents.Count == 0)
            {
                // we didnt find anyone, so set inactive
                proxData.TickOutOfRange();
                continue;
            }

            var entsFound = 0;
            foreach (var otherEnt in entsWithComponents)
            {
                // now check distance
                if (!ourCoords.TryDistance(
                        EntityManager,
                        Transform(otherEnt).Coordinates,
                        out var dist))
                    continue; // cant get distance, no pramgle
                if (dist > proxProto.MaxDistance)
                    continue; // too far away, no pramgle
                var isOptimal = dist <= proxProto.OptimalDistance;
                var optMult = isOptimal ? proxProto.OptimalDistanceBonusMultiplier : 1f;
                proxData.TickInRange(optMult);
                somethingHappened = true;
                entsFound++;
                if (entsFound >= proxProto.MaxTargets)
                    break; // reached max targets, stop checking
            }

            if (!somethingHappened)
            {
                // we didnt find anyone, so set inactive
                proxData.TickOutOfRange();
            }
        }
    }

    /// <summary>
    /// Entities with a set of components, and lacking excluded components.
    /// Does not check if they are players.
    /// Is efficient too!
    /// </summary>
    private HashSet<EntityUid> GetEntitiesWithComponents(
        EntityUid uid,
        ComponentRegistry mustHave,
        ComponentRegistry mustLack,
        bool shouldBeAlive = false)
    {
        // Go through all the components in mustHave,
        // and get the entities that have them.
        // skip duplicates.
        HashSet<EntityUid> entsWithComponents = new();
        foreach (var comp in mustHave)
        {
            var query = EntityManager.AllEntityQueryEnumerator(comp.Value.Component.GetType());
            while (query.MoveNext(out var otherEnt, out var checkComp))
            {
                if (otherEnt == uid)
                    continue; // dont check ourselves
                entsWithComponents.Add(otherEnt);
            }
        }

        // then go through them and remove any that lack a mustHave component
        entsWithComponents.RemoveWhere(
            ent =>
                mustHave.Any(comp => !HasComp(ent, comp.Value.Component.GetType()))
                || (shouldBeAlive && !_mobStateSystem.IsAlive(ent))
                || mustLack.Any(comp => HasComp(ent, comp.Value.Component.GetType())));
        return entsWithComponents;
    }

    /// <summary>
    /// A slightly optimized version of GetEntitiesWithComponents that only checks players.
    /// </summary>
    private HashSet<EntityUid> GetPlayersWithComponents(
        EntityUid uid,
        ComponentRegistry mustHave,
        ComponentRegistry mustLack)
    {
        // Go through all the active sessions,
        // and get the entities that have the components.
        // skip duplicates.
        HashSet<EntityUid> entsWithComponents = new();
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } otherEnt)
                continue; // no entity, no pramgle
            if (otherEnt == uid)
                continue; // dont check ourselves
            if (!_mobStateSystem.IsAlive(otherEnt))
                continue; // They must be alive and well to count
            if (mustHave.Any(comp => !HasComp(otherEnt, comp.Value.Component.GetType())))
                continue; // they don't have a component that includes them in this proxy
            if (mustLack.Any(comp => HasComp(otherEnt, comp.Value.Component.GetType())))
                continue; // they have a component that excludes them from this proxy
            entsWithComponents.Add(otherEnt);
        }

        return entsWithComponents;
    }

    /// <summary>
    /// Goes through the user's components, and update the existence of our proxy list.
    /// gives it a vague sense of synchronicity.
    /// </summary>
    private void SyncContinuousComponentsAndProxies(EntityUid uid, RoleplayIncentiveComponent rpic)
    {
        if (_timing.CurTime < rpic.NextProxySync)
            return; // too soon to check again
        rpic.NextProxySync = _timing.CurTime + rpic.ProxySyncInterval;
        // first, if pirate, not have capturebait
        if (HasComp<IsPirateComponent>(uid))
        {
            RemComp<CaptureBaitComponent>(uid); // shrimple as
        }

        // first, go through all the RpiContinuousProxyActionPrototypes in existence
        foreach (var proxo in rpic.AllowedProxies)
        {
            if (!_prototype.TryIndex(proxo, out var proxProto))
            {
                Log.Warning($"RpiProxyPrototype {proxo} not found!");
                continue;
            }

            // we are just interested if the user has all the component this proot wants,
            // and none of the ones it doesnt want
            if (proxProto.UserMustNotHaveTheseComponents.Any(comp => HasComp(uid, comp.Value.Component.GetType())))
            {
                KillProxyIfExists(rpic, proxProto.ID);
                continue; // they have a component that excludes them from this proxy
            }

            if (proxProto.UserMustHaveTheseComponents.Any(comp => !HasComp(uid, comp.Value.Component.GetType())))
            {
                KillProxyIfExists(rpic, proxProto.ID);
                continue;
            }

            AddProxyIfNotExists(rpic, proxProto.ID);
        }
    }

    private static void KillProxyIfExists(RoleplayIncentiveComponent rpic,
        ProtoId<RpiContinuousProxyActionPrototype> proto)
    {
        rpic.Proxies.Remove(proto);
    }

    private static void AddProxyIfNotExists(RoleplayIncentiveComponent rpic,
        ProtoId<RpiContinuousProxyActionPrototype> proto)
    {
        if (rpic.Proxies.ContainsKey(proto))
            return;
        rpic.Proxies[proto] = new RpiContinuousActionProxyDatum(proto);
    }

    /// <summary>
    /// Gets the total multiplier from all active proxies.
    /// </summary>
    private float GetProxiesPayMult(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        bool pop = false
    )
    {
        float totalMult = 0f;
        var now = _timing.CurTime;
        foreach (var (proxKind, proxData) in rpic.Proxies)
        {
            var mult = proxData.GetCurrentMultiplier();
            // however we will be applying this to the total multiplier additively
            totalMult += (mult.Float() - 1f);
            if (pop)
                proxData.Reset(); // reset it after we use it
        }

        return totalMult;
    }

    #endregion

    #region Aura Farming

    /// <summary>
    /// Checks all our tracked auras, and adds up their effects.
    /// </summary>
    private static float GetAuraPayMult(EntityUid uid, RoleplayIncentiveComponent rpic)
    {
        float sum = 0;
        foreach (var auraData in rpic.AuraTracker)
        {
            sum += auraData.GetCurrentMultiplier();
        }

        return sum;
    }

    public void ProcessAuras(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ref RpiPaywardDetails details,
        bool justChecking = false)
    {
        var timeSinceLastCheck = _timing.CurTime - rpic.NextAuraCheck + rpic.AuraCheckInterval;
        if (!justChecking)
        {
            if (_timing.CurTime < rpic.NextAuraCheck)
                return; // too soon to check again
            rpic.NextAuraCheck = _timing.CurTime + rpic.AuraCheckInterval;
        }
        // if they are dead or disconnected, tick decay on all auras and return
        if (!rpic.DebugIgnoreState && !justChecking)
        {
            if (_mobStateSystem.IsDead(uid)
                || !PlayerIsConnected(uid))
            {
                foreach (var auraData in rpic.AuraTracker)
                {
                    auraData.TickOutOfRange(timeSinceLastCheck);
                    if (auraData.IsFullyDecayed())
                    {
                        StopTrackingAura(
                            uid,
                            rpic,
                            auraData.RpiUid,
                            auraData.AuraId);
                    }

                }
                return;
            }
        }

        // go through every RPI component in existence
        var query = EntityQueryEnumerator<RoleplayIncentiveComponent>();
        var myCoords = _tf.GetMapCoordinates(uid);
        while (query.MoveNext(out var otherUid, out var rpiss))
        {
            if (otherUid == uid)
                continue; // dont check ourselves
            if (rpiss.AuraHolder.Count == 0)
                continue; // no auras to check
            var theirId = GetRpiId(otherUid, rpiss);
            var auraCoords = _tf.GetMapCoordinates(otherUid);
            foreach (var auraData in rpiss.AuraHolder)
            {
                if (justChecking)
                {
                    IsTrackingAura(
                        uid,
                        rpic,
                        theirId,
                        auraData.AuraId,
                        out var myAuraData);
                    if (myAuraData != null)
                    {
                        details.LoadAuraData(myAuraData);
                    }
                    continue;
                }
                // are we in range of their aura, and are they alive and connected?
                // cus they have to actually *be* there to give us the aura
                // whether they are through a wall is not important, just proximity
                var allowTick = myCoords.InRange(auraCoords, auraData.MaxDistance);
                if (allowTick && !rpic.DebugIgnoreState)
                {
                    if (_mobStateSystem.IsDead(otherUid)
                        || !PlayerIsConnected(otherUid))
                    {
                        allowTick = false; // they are dead or disconnected, so no aura for us
                    }
                }

                // do we have this aura source?
                if (IsTrackingAura(
                        uid,
                        rpic,
                        theirId,
                        auraData.AuraId,
                        out var ourAuraData))
                {
                    if (allowTick)
                    {
                        // we are in range, so update our tracking data
                        ourAuraData.TickInRange(timeSinceLastCheck);
                        details.LoadAuraData(ourAuraData);
                    }
                    else
                    {
                        // we are out of range, so update our tracking data
                        ourAuraData.TickOutOfRange(timeSinceLastCheck);
                        // if we are fully decayed, remove the aura
                        if (ourAuraData.IsFullyDecayed())
                        {
                            StopTrackingAura(
                                uid,
                                rpic,
                                theirId,
                                auraData.AuraId);
                        }
                    }
                }
                else if (allowTick)
                {
                    // we are in range, but we dont have this aura yet
                    StartTrackingAura(
                        uid,
                        rpic,
                        auraData,
                        out var newAuraData);
                    newAuraData.TickInRange(rpic.AuraCheckInterval);
                }
            }
        }
    }

    public static bool IsTrackingAura(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        string rpiUid,
        string auraProtoId,
        [NotNullWhen(true)] out RpiAuraData? outData)
    {
        foreach (var auraData in rpic.AuraTracker
                     .Where(auraData => auraData.RpiUid == rpiUid)
                     .Where(auraData => auraData.AuraId == auraProtoId))
        {
            outData = auraData;
            return true;
        }

        outData = null;
        return false;
    }

    public void StartTrackingAura(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        RpiAuraData auraData,
        out RpiAuraData newAuraData)
    {
        newAuraData = new RpiAuraData(
            auraData.Source,
            auraData.RpiUid,
            auraData.AuraId,
            auraData.Multiplier,
            auraData.MaxDistance,
            auraData.TimeTillFullEffect,
            auraData.DecayDelay,
            auraData.DecayToZeroTime);
        rpic.AuraTracker.Add(newAuraData);
    }

    public void StopTrackingAura(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        string rpiUid,
        string auraProtoId)
    {
        rpic.AuraTracker.RemoveAll(
            auraData =>
                auraData.RpiUid == rpiUid
                && auraData.AuraId == auraProtoId);
    }

    public void RemoveAuraSource(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        string auraProtoId)
    {
        var rpicUid = GetRpiId(uid, rpic);
        rpic.AuraHolder.RemoveAll(
            auraData =>
                auraData.RpiUid == rpicUid
                && auraData.AuraId == auraProtoId);
    }

    public void AddAuraSource(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ProtoId<RpiAuraPrototype> auraProtoId)
    {
        if (!_prototype.TryIndex(auraProtoId, out var auraProto))
        {
            Log.Warning($"RpiAuraPrototype {auraProtoId} not found!");
            return;
        }

        var rpicUid = GetRpiId(uid, rpic);
        if (HasAuraSource(
                uid,
                rpic,
                auraProtoId.Id,
                out var _))
            return; // already have it
        var auraData = new RpiAuraData(
            uid,
            rpicUid,
            auraProto.ID,
            auraProto.Multiplier,
            auraProto.MaxDistance,
            TimeSpan.FromMinutes(auraProto.MinutesTillFullEffect),
            TimeSpan.FromMinutes(auraProto.MinutesUntilDecayDelay),
            TimeSpan.FromMinutes(auraProto.MinutesUntilDecayToZero));
        rpic.AuraHolder.Add(auraData);
    }

    public bool HasAuraSource(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        string auraProtoId,
        out RpiAuraData? outData)
    {
        var rpicUid = GetRpiId(uid, rpic);
        foreach (var auraData in rpic.AuraHolder
                     .Where(auraData => auraData.RpiUid == rpicUid)
                     .Where(auraData => auraData.AuraId == auraProtoId))
        {
            outData = auraData;
            return true;
        }

        outData = null;
        return false;
    }

    public string GetRpiId(EntityUid uid, RoleplayIncentiveComponent rpic)
    {
        if (!string.IsNullOrEmpty(rpic.RpiId))
            return rpic.RpiId;
        // generate a random human-readable string based on the time of day on mars
        var timeSeed = DateTime.UtcNow.Ticks + uid.GetHashCode();
        var random = new Random((int)(timeSeed & 0xFFFFFFFF) ^ (int)(timeSeed >> 32));
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rpiId = new string(
            Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)])
                .ToArray());
        rpic.RpiId = rpiId;
        return rpiId; // sure good enough
    }

    public bool ValidForRPI(EntityUid uid)
    {
        if (!PlayerIsConnected(uid))
            return false;
        if (!PlayerIsAlive(uid))
            return false;
        return true;
    }

    public bool PlayerIsAlive(EntityUid uid)
    {
        if (TryComp<GhostComponent>(uid, out var ghost))
            return false; // no ghosts pls
        if (_mobStateSystem.IsDead(uid))
            return false; // no dead ppl pls
        return true;
    }

    public bool PlayerIsConnected(EntityUid uid)
    {
        if (!_playerManager.TryGetSessionByEntity(uid, out var sesh))
            return false;
        return sesh.Status switch
        {
            SessionStatus.Connected or SessionStatus.InGame or SessionStatus.Connecting => true,
            _ => false,
        };
    }

    #endregion

    #region Flarpi

    public void ProcessFlarpies(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        ref RpiPaywardDetails details,
        bool justChecking
        )
    {
        if (!CanFlarpi(uid, rpic))
            return; // only pirates care about flarpies
        ConditionFlarpi(uid, rpic);
        FlarpiDatacore myFlarpy = rpic.FlarpiDatacore;
        if (!_prototype.TryIndex(myFlarpy.DatacoreType, out var flarpiProto))
        {
            Log.Warning($"FlarpiDatacoreProto {myFlarpy.DatacoreType} not found!");
            return;
        }
        if (justChecking)
        {
            details.LoadFlarpiData(myFlarpy);
            return;
        }
        if (_timing.CurTime < myFlarpy.LastFlarpiCheck + myFlarpy.FlarpiCheckInterval)
            return; // too soon to check again
        TimeSpan timeSinceLastCheck = _timing.CurTime - myFlarpy.LastFlarpiCheck;
        myFlarpy.LastFlarpiCheck = _timing.CurTime;

        // FLARPI CALC
        // Base flarpi per hour: baseFlarpi
        // people around calc: freelancers and or nfsd
        // then modify that number into the modes of whatever
        // then use that to multiply the base flarpi per hour
        // then turn that into flarpi per millisecond
        // then multiply that by the time since last check
        // then add that to current progress
        // eat my flarpi

        decimal flarpiPerHour = flarpiProto.BaseFlarpi;
        var freelancersNearby = 0;
        var nfsdNearby = 0;
        MapCoordinates myCoords = _tf.GetMapCoordinates(uid);

        if (flarpiProto.FreelancerCountMode != FlarpiCountMode.Nope)
        {
            var entityQuery = EntityQueryEnumerator<IsPirateComponent>();
            while (entityQuery.MoveNext(out EntityUid otherUid, out IsPirateComponent? _))
            {
                if (otherUid == uid)
                    continue; // dont check ourselves, we kinda already did
                if (!ValidForRPI(otherUid))
                    continue; // not connected or alive or somesuch
                if (flarpiProto.FreelancerCountMode == FlarpiCountMode.Nearby)
                    if (!myCoords.InRange(_tf.GetMapCoordinates(otherUid), flarpiProto.NearbyRadius))
                        continue; // too far away
                freelancersNearby++;
                if (freelancersNearby >= flarpiProto.MaxFreelancersConsidered)
                    break; // reached max count, stop checking
            }
            // apply freelancer count mode
            switch (flarpiProto.FreelancerCalculationMode)
            {
                case FlarpiCalculationMode.Linear:
                    flarpiPerHour += flarpiProto.BaseFlarpi * freelancersNearby;
                    break;
                case FlarpiCalculationMode.Doubling:
                    flarpiPerHour *= (decimal)Math.Pow(2, freelancersNearby);
                    break;
                case FlarpiCalculationMode.LinearDoubled:
                    flarpiPerHour += flarpiProto.BaseFlarpi * 2 * freelancersNearby;
                    break;
                case FlarpiCalculationMode.Nope:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // once more, now for Nfsd
        if (flarpiProto.NfsdCountMode != FlarpiCountMode.Nope)
        {
            var entityQuery = EntityQueryEnumerator<IsNfsdComponent>();
            while (entityQuery.MoveNext(out EntityUid otherUid, out IsNfsdComponent? n))
            {
                if (otherUid == uid)
                    continue; // dont check ourselves, we kinda already did
                if (!ValidForRPI(otherUid))
                    continue; // not connected or alive or somesuch
                if (flarpiProto.NfsdCountMode == FlarpiCountMode.Nearby)
                    if (!myCoords.InRange(_tf.GetMapCoordinates(otherUid), flarpiProto.NearbyRadius))
                        continue; // too far away
                nfsdNearby += n.Worth;
                if (nfsdNearby >= flarpiProto.MaxNfsdsConsidered)
                    break; // reached max count, stop checking
            }
            // apply nfsd count mode
            switch (flarpiProto.NfsdCalculationMode)
            {
                case FlarpiCalculationMode.Linear:
                    flarpiPerHour += flarpiProto.BaseFlarpi * nfsdNearby;
                    break;
                case FlarpiCalculationMode.Doubling:
                    flarpiPerHour *= (decimal)Math.Pow(2, nfsdNearby);
                    break;
                case FlarpiCalculationMode.LinearDoubled:
                    flarpiPerHour += flarpiProto.BaseFlarpi * 2 * nfsdNearby;
                    break;
                case FlarpiCalculationMode.Nope:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // now we have our flarpi per hour! floor at minimum 1, cus youre there, right?
        if (flarpiPerHour < 1m)
            flarpiPerHour = 1m;
        if (TryComp<IsNfsdComponent>(uid, out var nfsdComp) && flarpiPerHour < nfsdComp.Worth)
            flarpiPerHour = nfsdComp.Worth; // nfsd get at least their worth in flarpies

        decimal flarpiPointsPerHour  = flarpiPerHour * flarpiProto.PointsPerFlarpi;
        decimal flarpiPerMillisecond = flarpiPointsPerHour / 3600000m;
        decimal flarpiGained         = flarpiPerMillisecond * (decimal)timeSinceLastCheck.TotalMilliseconds;
        myFlarpy.CurrentProgress += flarpiGained;
        details.LoadFlarpiData(myFlarpy);
    }

    /// <summary>
    /// Bank those flarpies for later withdrawal!
    /// </summary>
    public void HandleFlarpiBanking(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        PayoutDetails payDetails
    )
    {
        if (!CanFlarpi(uid, rpic))
            return; // only pirates care about flarpies
        FlarpiDatacore myFlarpy = rpic.FlarpiDatacore;
        if (!_prototype.TryIndex(myFlarpy.DatacoreType, out var flarpiProto))
        {
            Log.Warning($"FlarpiDatacoreProto {myFlarpy.DatacoreType} not found!");
            return;
        }
        int lastCount = myFlarpy.BankedFlarpis;
        // check for level up
        while (myFlarpy.CurrentProgress >= flarpiProto.PointsPerFlarpi)
        {
            myFlarpy.CurrentProgress -= flarpiProto.PointsPerFlarpi;
            myFlarpy.BankedFlarpis++;
        }
        if (myFlarpy.CurrentProgress < 0)
            myFlarpy.CurrentProgress = 0; // safety

        // CASH BONUS! We check how much cash you made with this payward,
        // and give them a bonus flarpi for every X amount of cash earned.
        // but... its a diminishing returns system, start at 1 per 5000 spesos,
        // then after 3 of them its 1 per 10000 spesos,
        // then after 6 of them its 1 per 20000 spesos, so on and so forth.
        int cashEarned = payDetails.FinalPay;
        int cashThreshold = 5000;
        int flarpiFromCash = 0;
        while (cashEarned >= cashThreshold)
        {
            cashEarned -= cashThreshold;
            flarpiFromCash++;
            if (flarpiFromCash % 3 == 0)
            {
                cashThreshold *= 2; // double the threshold after 3 flarpies
            }
        }

        myFlarpy.BankedFlarpis += flarpiFromCash;

        // tell them about it, if they have any
        if (myFlarpy.BankedFlarpis <= 0)
            return; // no change, no pramgle
        string message;
        if (myFlarpy.BankedFlarpis > lastCount)
        {
            var newFlarpis = myFlarpy.BankedFlarpis - lastCount;
            message = Loc.GetString(
                $"coyote-rpi-flarpi-got-more-{flarpiProto.FlarpiNameToken.ToLower()}",
                ("amount", newFlarpis),
                ("totalAmount", myFlarpy.BankedFlarpis));
        }
        else
        {
            message = Loc.GetString(
                $"coyote-rpi-flarpi-has-banked-{flarpiProto.FlarpiNameToken.ToLower()}",
                ("amount", myFlarpy.BankedFlarpis));
        }
        if (_playerManager.TryGetSessionByEntity(uid, out ICommonSession? session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                message,
                message,
                default,
                false,
                session.Channel);
        } // why am i calling them flarpies, they're actually den bullions
    }

    /// <summary>
    /// Payout those flarpies into their hand if possible!
    /// </summary>
    public void FlarpiWithdraw(
        EntityUid uid,
        RoleplayIncentiveComponent rpic
    )
    {
        if (!CanFlarpi(uid, rpic))
            return; // only pirates care about flarpies
        FlarpiDatacore myFlarpy = rpic.FlarpiDatacore;
        if (myFlarpy.BankedFlarpis <= 0)
            return; // no flarpies to payout
        if (!_prototype.TryIndex(myFlarpy.DatacoreType, out FlarpiSettingsPrototype? flarpiProto))
        {
            Log.Warning($"FlarpiDatacoreProto {myFlarpy.DatacoreType} not found!");
            return;
        }
        if (!_prototype.TryIndex(flarpiProto.FlarpiCurrencyPrototype, out CurrencyPrototype? _))
        {
            Log.Warning($"FlarpiCurrencyPrototype {flarpiProto.FlarpiCurrencyPrototype} not found!");
            return;
        }
        // where we dropping, boys?
        int toGive = myFlarpy.BankedFlarpis;

        myFlarpy.BankedFlarpis = 0;
        MapCoordinates coords  = _tf.GetMapCoordinates(Transform(uid));
        List<EntityUid> ents   = _stack.SpawnMultiple(
            flarpiProto.FlarpiCurrencyPrototype,
            toGive,
            _tf.ToCoordinates(coords));
        foreach (EntityUid coigne in ents)
        {
            if (!_heandsSystem.TryPickupAnyHand(uid, coigne))
                break;
        }

        // show popup
        var message = Loc.GetString(
            $"coyote-rpi-flarpi-payout-message-{flarpiProto.FlarpiNameToken.ToLower()}",
            ("amount", toGive));
        _popupSystem.PopupEntity(
            message,
            uid,
            uid,
            PopupType.LargeLingering);
    }

    /// <summary>
    /// If someone is offline or dead, scoot their last check forward so it doesnt
    /// give them a million flarpies after they come back.
    /// </summary>
    public void HousekeepFlarpi(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        float frameTime
        )
    {
        if (!CanFlarpi(uid, rpic))
            return; // only pirates care about flarpies
        FlarpiDatacore myFlarpy = rpic.FlarpiDatacore;
        if (_timing.CurTime < myFlarpy.LastFlarpiCheck + myFlarpy.FlarpiCheckInterval)
            return; // too soon to check again
        myFlarpy.LastFlarpiCheck = _timing.CurTime;
    }

    public bool CanFlarpi(
        EntityUid uid,
        RoleplayIncentiveComponent rpic
        )
    {
        if (HasComp<IsPirateComponent>(uid))
            return true; // only pirates care about flarpies
        if (HasComp<IsNfsdComponent>(uid))
            return true; // nfsd too
        return false;
    }

    public void ConditionFlarpi(
        EntityUid uid,
        RoleplayIncentiveComponent rpic
        )
    {
        // set the right prototype if needed
        FlarpiDatacore myFlarpy = rpic.FlarpiDatacore;
        if (HasComp<IsPirateComponent>(uid)
            && !HasComp<IsNfsdComponent>(uid))
        {
            myFlarpy.DatacoreType = "FlarpiSettings_Default";
        }
        else if (HasComp<IsNfsdComponent>(uid)
            && !HasComp<IsPirateComponent>(uid))
        {
            myFlarpy.DatacoreType = "FlarpiSettings_NFSD";
        }
        else
        {
            myFlarpy.DatacoreType = "FlarpiSettings_Default";
        }
    }

    #endregion

    #region Punishment Actions

    /// <summary>
    /// Punishes the player for dying, based on their tax bracket.
    /// This will take money from their bank account, based on their tax bracket.
    /// </summary>
    private void PunishPlayerForDeath(EntityUid uid, RoleplayIncentiveComponent rpic)
    {
        if (!_bank.TryGetBalance(uid, out var hasThisMuchMoney))
            return; // no bank account, no pramgle
        var taxBracket = GetTaxBracketData(rpic, hasThisMuchMoney);
        var penalty = taxBracket.DeathPenalty;
        if (penalty > hasThisMuchMoney)
            penalty = hasThisMuchMoney; // cant take more than they have
        if (penalty <= 0)
            return; // no penalty, no punishment
        if (!_bank.TryBankWithdraw(uid, (int)penalty))
        {
            Log.Warning($"Failed to withdraw {penalty} from bank account of entity {uid}!");
            return;
        }

        rpic.LastDeathPunishment = _timing.CurTime;
        // tell them they got punished
        var message = Loc.GetString(
            "coyote-rpi-death-penalty-message",
            ("amount", (int)penalty));
        if (_playerManager.TryGetSessionByEntity(uid, out var session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                message,
                message,
                default,
                false,
                session.Channel);
        }

        // also show a popup
        _popupSystem.PopupEntity(
            message,
            uid,
            uid,
            PopupType.LargeCaution);
    }

    #endregion

    #region Helpers

    // private void HandleExamineRpiVerb(
    //     EntityUid uid,
    //     RoleplayIncentiveComponent rpic,
    //     GetVerbsEvent<ExamineVerb> args)
    // {
    //     if (args.User != args.Target
    //         && !HasComp<AdminGhostComponent>(args.User))
    //         return; // only self examine, if not admin ghost
    //
    //     var verb = new ExamineVerb()
    //     {
    //         Text = Loc.GetString("coyote-rpi-examine-verb-text"),
    //         Category = VerbCategory.Examine,
    //         Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/info.svg.192dpi.png")),
    //         Act = () =>
    //         {
    //             var markup = GetRpiExamineText(uid, rpic, args);
    //             _examineSystem.SendExamineTooltip(
    //                 args.User,
    //                 uid,
    //                 markup,
    //                 false,
    //                 false);
    //         },
    //     };
    //     args.Verbs.Add(verb);
    // }

    // private FormattedMessage GetRpiExamineText(
    //     EntityUid uid,
    //     RoleplayIncentiveComponent rpic,
    //     GetVerbsEvent<ExamineVerb> args)
    // {
    //     var msg = new FormattedMessage();
    //     msg.AddMarkup(Loc.GetString("coyote-rpi-examine-header"));
    // }

    private void PaymentifySimple(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        int basePay,
        string? popupLocKey,
        string? chatLocKey)
    {
        var payDetails = ProcessPaymentDetails(
            basePay,
            1f);
        // pay the player
        PayPlayer(uid, payDetails.FinalPay);
        if (popupLocKey != null)
        {
            ShowPopup(
                uid,
                payDetails,
                popupLocKey);
        }

        if (chatLocKey != null)
        {
            ShowChatMessageSimple(
                uid,
                payDetails,
                chatLocKey);
        }
    }

    private void PayPlayer(EntityUid uid, int amount)
    {
        if (!_bank.TryBankDeposit(uid, amount))
        {
            Log.Warning($"Failed to deposit {amount} into bank account of entity {uid}!");
        }
    }

    private void ModifyImmediatePay(
        EntityUid uid,
        RoleplayIncentiveComponent? rpic,
        RpiImmediatePayEvent args,
        ref int basePay)
    {
        if (!Resolve(uid, ref rpic))
            return;
        var taxBracket = GetTaxBracketData(uid);
        var multiplier = taxBracket.ActionMultipliers.GetValueOrDefault(args.Category, 1f);
        basePay = (int)(basePay * multiplier);
    }

    private float GetJobModifierPayMult(
        EntityUid uid,
        RoleplayIncentiveComponent rpic)
    {
        float jobMult = 0f;
        foreach (var protid in rpic.JobModifiers)
        {
            if (!_prototype.TryIndex(protid, out var jobProto))
            {
                Log.Warning($"RpiJobModifierPrototype {protid} not found!");
                continue;
            }

            jobMult += (jobProto.Multiplier - 1f);
        }

        return jobMult;
    }

    private static float GetMiscActionPayMult(
        EntityUid uid,
        RoleplayIncentiveComponent rpic,
        TaxBracketResult taxBracket)
    {
        // go through all the actions, and compile the BEST ONES EVER
        Dictionary<RpiActionType, RpiActionRecord> bestActions = new();
        foreach (var action in rpic.MiscActionsTaken.Where(action => action.IsValid()))
        {
            if (!action.TryPop())
            {
                continue;
            }

            // slot it into the best action for that type
            if (bestActions.TryGetValue(action.Category, out var existing))
            {
                if (action.GetMultiplier() > existing.GetMultiplier())
                {
                    bestActions[action.Category] = action;
                }
            }
            else
            {
                bestActions[action.Category] = action;
            }
        }

        var multOut = 1f;
        // now, sum up the best actions
        foreach (var kvp in bestActions)
        {
            // add mults ADDITIVFELY
            multOut += (kvp.Value.GetMultiplier() - 1f);
        }

        return multOut - 1f;
    }

    private float GetChatActionPay(
        EntityUid uid,
        RoleplayIncentiveComponent? rpic,
        TaxBracketResult taxBracket,
        ref RpiPaywardDetails details,
        bool justChecking = false)
    {
        if (!Resolve(uid, ref rpic))
            return 0;
        float total = 0;
        // go through all the actions, and compile the BEST ONES EVER
        Dictionary<RpiChatActionCategory, float> bestActions = new();
        Dictionary<RpiChatActionCategory, RpiJudgementDetails> bestJudgements = new();
        foreach (var action in rpic.ChatActionsTaken.Where(action => !action.ChatActionIsSpent))
        {
            if (action.ChatActionIsSpent)
                continue; // just in case, no pramgle
            var judgement = JudgeChatAction(
                action,
                rpic,
                out var judgeDetails);
            // slot it into the best action for that type
            if (bestActions.TryGetValue(action.Action, out var existing))
            {
                if (judgement > existing)
                {
                    bestActions[action.Action] = judgement;
                    bestJudgements[action.Action] = judgeDetails;
                }
            }
            else
            {
                bestActions[action.Action] = judgement;
                bestJudgements[action.Action] = judgeDetails;
            }

            if (justChecking)
                continue;
            action.ChatActionIsSpent = true; // mark it for deletion
        }

        // now, sum up the best actions
        foreach (var kvp in bestActions)
        {
            total += kvp.Value;
        }
        foreach (var kvp in bestJudgements)
        {
            details.AddJudgementDetails(kvp.Key, kvp.Value);
        }

        total *= taxBracket.PayPerJudgement;
        return total;
    }


    private static void PruneOldActions(RoleplayIncentiveComponent rpic)
    {
        rpic.ChatActionsTaken.RemoveAll(action => action.ChatActionIsSpent);
        rpic.MiscActionsTaken.RemoveAll(action => action.MiscActionIsSpent);
    }

    public TaxBracketResult GetTaxBracketData(EntityUid uid)
    {
        if (!TryComp<RoleplayIncentiveComponent>(uid, out var rpic))
            return new TaxBracketResult(); // default values
        if (!_bank.TryGetBalance(uid, out var hasThisMuchMoney))
            return new TaxBracketResult(); // no bank account, no pramgle
        return GetTaxBracketData(rpic, hasThisMuchMoney);
    }

    public TaxBracketResult GetTaxBracketData(
        RoleplayIncentiveComponent rpic,
        int hasThisMuchMoney)
    {
        var taxBracket = new TaxBracketResult(); // default values
        // go through the prototypes, and find the one that fits the player's money
        // if none fit, use the default
        if (!_prototype.TryIndex(TaxBracketDefault, out var defaultProto))
        {
            Log.Warning($"RpiTaxBracketPrototype {TaxBracketDefault} not found! ITS THE DEFAULT AOOOAOAOAOOA");
            return taxBracket;
        }

        RpiTaxBracketPrototype? proto = null;
        // go through the sorted list, and find the Lowest bracket that is higher than the player's money
        List<RpiTaxBracketPrototype> protosHigherThanPlayer = new();
        foreach (var protoId in RpiDatumPrototypes)
        {
            if (!_prototype.TryIndex(protoId, out var myProto))
            {
                Log.Warning($"RpiTaxBracketPrototype {protoId} not found!");
                continue;
            }

            if (hasThisMuchMoney < myProto.CashThreshold)
            {
                protosHigherThanPlayer.Add(myProto);
            }
        }

        // if we found any, use the lowest one
        if (protosHigherThanPlayer.Count > 0)
        {
            proto = protosHigherThanPlayer.OrderBy(p => p.CashThreshold).First();
        }

        // if we didnt find any, use the default
        Dictionary<RpiChatActionCategory, RpiChatActionPrototype> chatActionPrototypes = new();
        proto ??= defaultProto;
        taxBracket = new TaxBracketResult(
            proto.JudgementPointPayout,
            (int)(proto.DeathPenalty * hasThisMuchMoney),
            (int)(proto.DeepFriedPenalty * hasThisMuchMoney),
            proto.ActionMultipliers,
            proto.JournalismDat);

        // and now the overrides
        if (rpic.TaxBracketPayoutOverride != -1)
        {
            taxBracket.PayPerJudgement = rpic.TaxBracketPayoutOverride;
        }

        if (rpic.TaxBracketDeathPenaltyOverride != -1)
        {
            taxBracket.DeathPenalty = rpic.TaxBracketDeathPenaltyOverride;
        }

        if (rpic.TaxBracketDeepFryerPenaltyOverride != -1)
        {
            taxBracket.DeepFryPenalty = rpic.TaxBracketDeepFryerPenaltyOverride;
        }

        return taxBracket;
    }

    private PayoutDetails ProcessPaymentDetails(
        int basePay,
        float baseMult)
    {
        var finalPay = basePay;
        // then apply the multiplier
        finalPay = (int)(finalPay * baseMult);
        // clamp the pay amount to a minimum of 20 and a maximum of int.MaxValue
        finalPay = Math.Clamp(
            (int)(Math.Ceiling(finalPay / 10.0) * 10),
            20,
            int.MaxValue);

        // round the multiplier to 2 decimal places
        var multiplier = baseMult;
        var hasMultiplier = Math.Abs(multiplier - 1f) > 0.01f;
        return new PayoutDetails(
            basePay,
            finalPay,
            multiplier,
            baseMult,
            hasMultiplier);
    }

    private void ShowPopup(EntityUid uid, string message, PopupType popupType = PopupType.LargeCaution)
    {
        _popupSystem.PopupEntity(
            message,
            uid,
            uid,
            popupType);
    }

    private void ShowPopup(
        EntityUid uid,
        PayoutDetails payDetails,
        string locale = "coyote-rpi-payward-message",
        bool suppressChat = false)
    {
        if (payDetails.FinalPay == 0)
            return; // no pay, no popup
        var messageOverhead = Loc.GetString(
            locale,
            ("amount", payDetails.FinalPay));
        _popupSystem.PopupEntity(
            messageOverhead,
            uid,
            uid,
            PopupType.Small,
            suppressChat);
    }

    private void ShowChatMessage(EntityUid uid, PayoutDetails payDetails)
    {
        if (payDetails.FinalPay <= 0)
            return; // no pay, no popup
        var message = "Hi mom~";
        // convert the multiplier to a string with 2 decimal places, if present
        if (payDetails.HasMultiplier)
        {
            message = Loc.GetString(
                "coyote-rpi-payward-message-multiplier",
                ("amount", payDetails.FinalPay),
                ("basePay", payDetails.BasePay),
                ("multiplier", payDetails.Multiplier));
        }
        else
        {
            message = Loc.GetString(
                "coyote-rpi-payward-message",
                ("amount", payDetails.FinalPay));
        }

        // cum it to chat
        if (_playerManager.TryGetSessionByEntity(uid, out var session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                message,
                message,
                default,
                false,
                session.Channel);
        }
    }

    private void ShowChatMessageSimple(EntityUid uid, PayoutDetails payDetails, string locale)
    {
        if (payDetails.FinalPay <= 0)
            return; // no pay, no popup
        var message = Loc.GetString(
            locale,
            ("amount", payDetails.FinalPay));

        // cum it to chat
        if (_playerManager.TryGetSessionByEntity(uid, out var session))
        {
            _chatManager.ChatMessageToOne(
                ChatChannel.Notifications,
                message,
                message,
                default,
                false,
                session.Channel);
        }
    }

    private void ProcessRoleplayIncentiveEvent(EntityUid uid, RpiChatEvent args)
    {
        // first, check if the uid has the component
        if (!TryComp<RoleplayIncentiveComponent>(uid, out var incentive))
        {
            Log.Warning($"RoleplayIncentiveComponent not found on entity {uid}!");
            return;
        } // i guess?

        // then, check if the channel in the args can be translated to a RoleplayAct
        var actOut = ChatChannel2RpiChatAction(args.Channel);
        if (actOut == RpiChatActionCategory.None)
        {
            return; // lot of stuff happens and it dont
        }

        // if its EmotingOrQuickEmoting, we need to doffgerentiate thewween the tween the two
        if (actOut == RpiChatActionCategory.EmotingOrQuickEmoting)
        {
            actOut = DoffgerentiateEmotingAndQuickEmoting(
                args.Source,
                args.Message);
        }

        // make the thing
        var action = new RpiChatRecord(
            actOut,
            _timing.CurTime,
            args.Message,
            args.PeoplePresent);
        // add it to the actions taken
        incentive.ChatActionsTaken.Add(action);
        // and we're good
    }

    private bool GetChatActionLookup(
        RpiChatActionCategory action,
        [NotNullWhen(true)] out RpiChatActionPrototype? proot)
    {
        if (!ChatActionLookup.TryGetValue(action, out var myPrototype))
        {
            proot = null;
            return false;
        }

        if (!_prototype.TryIndex<RpiChatActionPrototype>(myPrototype, out var proto))
        {
            Log.Warning($"RpiChatActionPrototype {myPrototype} not found!");
            proot = null;
            return false;
        }

        proot = proto;
        return true;
    }

    private static RpiChatActionCategory ChatChannel2RpiChatAction(ChatChannel channel)
    {
        // this is a bit of a hack, but it works
        return channel switch
        {
            ChatChannel.Local => RpiChatActionCategory.Speaking,
            ChatChannel.Whisper => RpiChatActionCategory.Whispering,
            ChatChannel.Emotes => RpiChatActionCategory.EmotingOrQuickEmoting, // we dont know yet
            ChatChannel.Radio => RpiChatActionCategory.Radio,
            ChatChannel.Subtle => RpiChatActionCategory.Subtling,
            // the rest are not roleplay actions
            _ => RpiChatActionCategory.None,
        };
    }

    private RpiChatActionCategory DoffgerentiateEmotingAndQuickEmoting(
        EntityUid source,
        string message
    )
    {
        return _chatsys.TryEmoteChatInput(
            source,
            message,
            false)
            ? RpiChatActionCategory.QuickEmoting // if the message is a valid emote, then its a quick emote
            : RpiChatActionCategory.Emoting;

        // well i cant figure out how the system does it, so im just gonnasay if theres
        // no spaces, its a quick emote
        // return !message.Contains(' ')
        //     ? RpiChatActionCategory.QuickEmoting
        //     // otherwise, its a normal emote
        //     : RpiChatActionCategory.Emoting;
    }

    /// <summary>
    /// Passes judgement on the action
    /// Based on a set of criteria, it will return a judgement value
    /// It will be judged based on:
    /// - How long the text was
    /// - How many people were present
    /// - and thats it for now lol
    /// </summary>
    private float JudgeChatAction(
        RpiChatRecord chatRecord,
        RoleplayIncentiveComponent rpic,
        out RpiJudgementDetails details)
    {
        float judgement = 0f;
        float longMult = 1f;
        foreach (var jMult in rpic.ChatJudgementModifiers)
        {
            if (!_prototype.TryIndex(jMult, out RpiChatJudgementModifierPrototype? proto))
                continue;
            longMult += proto.GetMod(chatRecord.Action) - 1f;
        }

        int longth = (int)((chatRecord.Message?.Length ?? 1) * longMult);
        float lengthMult = GetMessageLengthMultiplier(chatRecord.Action, longth);
        float listenerMult = GetListenerMultiplier(chatRecord.Action, chatRecord.PeoplePresent);
        judgement += lengthMult;
        judgement += listenerMult;

        details = new RpiJudgementDetails(
            longth,
            lengthMult,
            listenerMult,
            judgement,
            chatRecord.Message);
        return judgement;
    }

    /// <summary>
    /// Gets the multiplier for the number of listeners present
    /// </summary>
    /// <param name="action">The action being performed</param>
    /// <param name="listeners">The number of listeners present</param>
    private int GetListenerMultiplier(RpiChatActionCategory action, int listeners)
    {
        // if there are no listeners, return 0
        if (listeners <= 0)
            return 0;
        if (!GetChatActionLookup(action, out var proto))
            return 0;
        if (!proto.MultiplyByPeoplePresent)
            return 0;
        // clamp the number of listeners to the max defined in the prototype
        listeners = Math.Clamp(
            listeners,
            0,
            proto.MaxPeoplePresent);
        return listeners;
    }

    /// <summary>
    /// Gets the message length multiplier for the action
    /// </summary>
    /// <param name="action">The action being performed</param>
    /// <param name="messageLength">The length of the message</param>
    private float GetMessageLengthMultiplier(RpiChatActionCategory action, int messageLength)
    {
        // if the message length is 0, return 0 change
        if (messageLength <= 0)
            return 0;

        // get the prototype for the action
        if (!GetChatActionLookup(action, out var proto))
            return 0;

        if (proto.LengthPerPoint <= 0)
        {
            return 0; // thingy isnt using length based judgement, also dont divide by 0
        }

        if (messageLength < proto.LengthPerPoint)
        {
            return 0; // too short to judge
        }

        var rawLengthMult = messageLength / (float)proto.LengthPerPoint;
        // floor it to the nearest whole number, with a minimum of 1 and max of who cares
        return Math.Clamp(
            (float)Math.Floor(rawLengthMult),
            0,
            100);
    }

    private int GetPeopleInRange(EntityUid origin, float range)
    {
        var recipients = 1; // you were there too
        var xforms = GetEntityQuery<TransformComponent>();

        var transformSource = xforms.GetComponent(origin);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            if (playerEntity == origin)
                continue;

            var transformEntity = xforms.GetComponent(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            // even if they are a ghost hearer, in some situations we still need the range
            if (sourceCoords.TryDistance(
                    EntityManager,
                    transformEntity.Coordinates,
                    out var distance)
                && distance <= range)
            {
                recipients++;
            }
        }

        return recipients;
    }

    #endregion
}
