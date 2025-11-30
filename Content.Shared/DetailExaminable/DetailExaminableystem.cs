using Content.Shared.Consent;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.DetailExaminable
{
    public sealed class DetailExaminableSystem : EntitySystem
    {
        [Dependency] private readonly ExamineSystemShared _examineSystem = default!;

        // DEN - Icons
        private SpriteSpecifier _detailVerbIcon =
            new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/examine.svg.192dpi.png"));

        private SpriteSpecifier _lewdVerbIcon =
            new SpriteSpecifier.Texture(new("/Textures/_DEN/Interface/VerbIcons/lewd.svg.192dpi.png"));
        // End DEN

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<DetailExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
        }

        private void OnGetExamineVerbs(EntityUid uid, DetailExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
        {
            if (Identity.Name(args.Target, EntityManager) != MetaData(args.Target).EntityName)
                return;

            var contentVerb = GetContentExamine(uid, component, args);
            if (contentVerb != null) // DEN: Have to null-check becuase GetContentExamine is now nullable
                args.Verbs.Add(contentVerb);

            var nsfwContentVerb = GetNsfwContentExamine(uid, component, args);
            if (nsfwContentVerb != null)
                args.Verbs.Add(nsfwContentVerb);
        }

        // DEN start: Common function for building examine verbs
        private ExamineVerb? GetExamineVerb(EntityUid uid,
            string content,
            string verbText,
            SpriteSpecifier? icon,
            GetVerbsEvent<ExamineVerb> args,
            ProtoId<ConsentTogglePrototype>? requiredConsent = null,
            bool hideIfEmpty = false)
        {
            if (hideIfEmpty && string.IsNullOrWhiteSpace(content))
                return null;

            var verb = new ExamineVerb
            {
                Act = () =>
                {
                    var markup = new FormattedMessage();
                    markup.AddMarkupPermissive(content);
                    _examineSystem.SendExamineTooltip(args.User, uid, markup, getVerbs: false, centerAtCursor: false);
                },
                Text = verbText,
                Category = VerbCategory.Examine,
                Icon = icon
            };

            return verb;
        }
        // End DEN

        private ExamineVerb? GetContentExamine(
            EntityUid uid,
            DetailExaminableComponent component,
            GetVerbsEvent<ExamineVerb> args
        )
        {
            // DEN: Use shared detail examine system for this
            return GetExamineVerb(uid,
                content: component.Content,
                verbText: Loc.GetString("detail-examinable-verb-text"),
                icon: _detailVerbIcon,
                args: args,
                hideIfEmpty: false);
        }

        private ExamineVerb? GetNsfwContentExamine(
            EntityUid uid,
            DetailExaminableComponent component,
            GetVerbsEvent<ExamineVerb> args
        )
        {
            // DEN: Use shared detail examine system for this
            return GetExamineVerb(uid,
                content: component.NsfwContent,
                verbText: Loc.GetString("detail-nsfw-examinable-verb-text"),
                icon: _lewdVerbIcon,
                args: args,
                hideIfEmpty: true);
        }
    }
}
